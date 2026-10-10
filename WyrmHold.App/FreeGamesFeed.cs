using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// La liste de l'onglet « 🎁 Gratuits » (démos, jeux gratuits ou jeux offerts par Epic), chargée page par page.
/// Même principe que DealsFeed, avec une différence : changer de liste ou de tri pendant un chargement
/// est permis. Chaque remise à zéro prend un nouveau numéro, et une réponse qui arrive avec un ancien
/// numéro est ignorée (sinon les démos pourraient s'afficher alors que tu as choisi Epic).
/// </summary>
public class FreeGamesFeed
{
    private readonly Func<int, Task<FreeGamesPage>> _loadPage;
    private int _nextStart;
    private int _generation;

    public FreeGamesFeed(Func<int, Task<FreeGamesPage>> loadPage)
    {
        _loadPage = loadPage;
    }

    public List<FreeGame> Items { get; private set; } = new List<FreeGame>();
    public int TotalCount { get; private set; }
    public bool HasMore { get; private set; }
    public bool IsLoaded { get; private set; }
    public bool IsLoading { get; private set; }
    public bool LoadFailed { get; private set; }

    /// <summary>
    /// reset = true : on repart du début (liste, tri ou recherche changés, ↻) ;
    /// reset = false : on ajoute la page suivante (« Charger plus »).
    /// </summary>
    public async Task LoadAsync(bool reset)
    {
        if (IsLoading && !reset)
        {
            return;
        }

        int generation = reset ? ++_generation : _generation;
        IsLoading = true;

        if (reset)
        {
            Items = new List<FreeGame>();
            _nextStart = 0;
            TotalCount = 0;
            HasMore = false;
        }

        try
        {
            FreeGamesPage page = await _loadPage(_nextStart);

            if (generation != _generation)
            {
                return;   // la liste a été remise à zéro pendant l'attente : cette page n'est plus la bonne
            }

            // Le classement de Steam bouge un peu entre deux pages : un jeu peut revenir, on ne l'ajoute pas 2 fois.
            HashSet<string> known = Items.Select(KeyOf).ToHashSet();

            foreach (FreeGame game in page.Games)
            {
                if (known.Add(KeyOf(game)))
                {
                    Items.Add(game);
                }
            }

            _nextStart = page.NextStart;
            TotalCount = page.TotalCount;
            HasMore = page.HasMore;
            IsLoaded = true;
            LoadFailed = false;
        }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                LoadFailed = true;
                Logger.Log($"Chargement des jeux gratuits impossible : {ex.Message}");
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Ce qui identifie un jeu : son appid Steam, sinon son adresse (Epic).</summary>
    public static string KeyOf(FreeGame game)
    {
        return game.SteamAppId is int appId ? $"steam:{appId}" : game.StoreUrl;
    }
}
