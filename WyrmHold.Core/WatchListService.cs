namespace Wyrmhold.Core;

/// <summary>
/// La liste de suivi : les jeux dont tu veux surveiller le prix.
/// Garde une copie en mémoire (pour l'affichage) et la base à jour (pour la retrouver au prochain démarrage).
/// Si ton compte IsThereAnyDeal est relié (facultatif), elle se synchronise avec ta Waitlist.
/// </summary>
public class WatchListService
{
    // IsThereAnyDeal accepte au plus 200 jeux par demande de prix.
    private const int MaxGamesPerRequest = 200;

    private readonly GameDatabase _database = new GameDatabase();

    private List<WatchedGame> _games = new List<WatchedGame>();

    // IReadOnlyList : le reste de l'appli peut lire la liste, mais pas la modifier sans passer par Add/Remove.
    public IReadOnlyList<WatchedGame> Games => _games;

    /// <summary>Relit la liste depuis la base.</summary>
    public void Load()
    {
        _games = _database.LoadWatchedGames();
    }

    public bool IsWatched(string itadId)
    {
        return _games.Any(game => game.ItadId == itadId);
    }

    public void Add(string itadId, string title, string? type)
    {
        AddLocal(itadId, title, type, isSynced: false);
    }

    private void AddLocal(string itadId, string title, string? type, bool isSynced)
    {
        if (IsWatched(itadId))
        {
            return;
        }

        var game = new WatchedGame
        {
            ItadId = itadId,
            Title = title,
            Type = type ?? "",   // certains produits n'ont pas de type : la base attend un texte, pas null
            AddedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            SyncedWithItad = isSynced
        };

        // D'abord la base : si elle échoue (exception), la liste en mémoire reste cohérente avec elle.
        _database.AddWatchedGame(game);
        _games.Add(game);
    }

    public void Remove(string itadId)
    {
        _database.RemoveWatchedGame(itadId);
        _games.RemoveAll(game => game.ItadId == itadId);
    }

    // ===================== Prix =====================

    /// <summary>
    /// Demande à IsThereAnyDeal les prix actuels de TOUS les jeux suivis (une requête par tranche
    /// de 200 jeux, donc une seule en pratique), puis enregistre le meilleur prix de chacun.
    /// Les boutiques masquées dans le filtre de l'onglet Boutiques sont ignorées.
    /// Renvoie le nombre de jeux mis à jour.
    /// </summary>
    public async Task<int> CheckPricesAsync(IsThereAnyDealApi api, IReadOnlySet<string> hiddenShops)
    {
        if (_games.Count == 0 || !api.IsConfigured)
        {
            return 0;
        }

        // On travaille sur une copie de la liste : si tu ajoutes ou retires un jeu pendant
        // la vérification (qui prend une seconde ou deux), la boucle n'est pas perturbée.
        List<WatchedGame> games = _games.ToList();
        Dictionary<string, WatchedGame> gamesById = games.ToDictionary(game => game.ItadId);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int updated = 0;

        // Chunk découpe la liste en paquets de 200 au plus.
        foreach (WatchedGame[] chunk in games.Chunk(MaxGamesPerRequest))
        {
            List<ItadGamePrices> prices = await api.GetPricesAsync(chunk.Select(game => game.ItadId));

            foreach (ItadGamePrices gamePrices in prices)
            {
                if (!gamesById.TryGetValue(gamePrices.Id, out WatchedGame? game))
                {
                    continue;
                }

                ItadDeal? best = gamePrices.Deals
                    .Where(deal => !hiddenShops.Contains(deal.Shop.Name))
                    .OrderBy(deal => deal.Price.Amount)
                    .FirstOrDefault();

                game.BestPrice = best?.Price.Amount;
                game.BestShop = best?.Shop.Name;
                game.BestCut = best?.Cut ?? 0;
                game.HistoryLow = gamePrices.HistoryLow?.AllTime?.Amount;
                game.Currency = best?.Price.Currency ?? gamePrices.HistoryLow?.AllTime?.Currency ?? "";
                game.PricesCheckedUnix = now;
                updated++;
            }
        }

        _database.SaveWatchedPrices(games);
        return updated;
    }

    // ===================== Synchro avec la Waitlist (facultative) =====================

    /// <summary>
    /// Met ta liste de suivi et ta Waitlist IsThereAnyDeal d'accord. Les règles :
    /// 1. Les retraits faits ici sans avoir pu prévenir le site sont d'abord envoyés.
    /// 2. Un jeu sur le site mais pas ici → ajouté ici.
    /// 3. Un jeu ici mais plus sur le site :
    ///    - s'il y avait déjà été vu (SyncedWithItad) → tu l'as retiré sur le site : retiré ici aussi ;
    ///    - sinon (ajouté ici pendant que le site était injoignable, ou avant de relier le compte) → envoyé au site.
    /// </summary>
    public async Task SyncWithWaitlistAsync(IsThereAnyDealApi api, ItadAccount account)
    {
        string token = await account.GetAccessTokenAsync();

        // 1. Retraits en attente.
        List<string> pendingRemovals = _database.LoadPendingWaitlistRemovals();

        if (pendingRemovals.Count > 0)
        {
            await api.RemoveFromWaitlistAsync(token, pendingRemovals);
            _database.ClearPendingWaitlistRemovals();
        }

        // 2. Ce qu'il y a sur le site.
        List<ItadWaitlistGame> remoteGames = await api.GetWaitlistAsync(token);
        HashSet<string> remoteIds = remoteGames.Select(game => game.Id).ToHashSet();

        // 3. Les jeux d'ici absents du site. ToList() : on retire des éléments de _games pendant la boucle,
        //    on parcourt donc une copie.
        List<string> toUpload = new List<string>();

        foreach (WatchedGame game in _games.ToList())
        {
            if (remoteIds.Contains(game.ItadId))
            {
                continue;
            }

            if (game.SyncedWithItad)
            {
                Remove(game.ItadId);
            }
            else
            {
                toUpload.Add(game.ItadId);
            }
        }

        await api.AddToWaitlistAsync(token, toUpload);

        // 2 (suite). Les jeux du site absents d'ici.
        foreach (ItadWaitlistGame remoteGame in remoteGames)
        {
            AddLocal(remoteGame.Id, remoteGame.Title, remoteGame.Type, isSynced: true);
        }

        // Tout est maintenant des deux côtés.
        MarkSynced(_games.Select(game => game.ItadId).ToList(), true);
    }

    /// <summary>
    /// Prévient le site d'un ajout fait ici. En cas d'échec, pas grave : le jeu reste « non synchronisé »
    /// et sera envoyé à la prochaine synchro. Renvoie vrai si le site est à jour.
    /// </summary>
    public async Task<bool> PushAddAsync(IsThereAnyDealApi api, ItadAccount account, string itadId)
    {
        try
        {
            string token = await account.GetAccessTokenAsync();
            await api.AddToWaitlistAsync(token, new[] { itadId });
            MarkSynced(new List<string> { itadId }, true);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Ajout à la Waitlist IsThereAnyDeal impossible : {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Prévient le site d'un retrait fait ici. En cas d'échec, le retrait est mis en attente
    /// (table WaitlistPendingRemoval) et réessayé à la prochaine synchro. Renvoie vrai si le site est à jour.
    /// </summary>
    public async Task<bool> PushRemoveAsync(IsThereAnyDealApi api, ItadAccount account, string itadId)
    {
        try
        {
            string token = await account.GetAccessTokenAsync();
            await api.RemoveFromWaitlistAsync(token, new[] { itadId });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Retrait de la Waitlist IsThereAnyDeal impossible, réessai à la prochaine synchro : {ex.Message}");
            _database.AddPendingWaitlistRemoval(itadId);
            return false;
        }
    }

    /// <summary>
    /// À la déconnexion du compte : plus aucun jeu n'est considéré comme synchronisé.
    /// Ainsi, si tu te reconnectes plus tard, rien n'est supprimé : les deux listes sont fusionnées.
    /// </summary>
    public void ForgetWaitlistSync()
    {
        MarkSynced(_games.Select(game => game.ItadId).ToList(), false);
        _database.ClearPendingWaitlistRemovals();
    }

    private void MarkSynced(List<string> itadIds, bool isSynced)
    {
        _database.SetWatchedGamesSynced(itadIds, isSynced);

        foreach (WatchedGame game in _games.Where(game => itadIds.Contains(game.ItadId)))
        {
            game.SyncedWithItad = isSynced;
        }
    }
}
