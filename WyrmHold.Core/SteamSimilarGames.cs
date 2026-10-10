using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Wyrmhold.Core;

/// <summary>Un jeu proposé dans « Jeux similaires » : son numéro Steam, son nom et son image.</summary>
public record SimilarGame(int AppId, string Name, string ImageUrl);

/// <summary>
/// Les jeux « similaires » d'après la boutique Steam, sans clé : la page « Plus de jeux comme celui-ci »
///   store.steampowered.com/recommended/morelike/app/&lt;appid&gt;/
/// (Steam les choisit d'après les tags que les joueurs donnent aux jeux). Ce n'est pas une API documentée :
/// vérifié le 10 octobre 2026 (Detroit: Become Human → Beyond: Two Souls, Heavy Rain…).
/// Dans la page, chaque jeu est une balise &lt;a class="similar_grid_capsule" data-ds-appid="…"&gt; avec son image ;
/// le bloc « released » contient les jeux déjà sortis, dans l'ordre de Steam.
/// Les noms, absents de la page, viennent ensuite de la boutique Steam (GetItems).
/// </summary>
public static class SteamSimilarGames
{
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    // Gardés en mémoire pendant que Wyrmhold est ouvert : revenir sur un jeu ne relance aucune requête.
    private static readonly Dictionary<int, List<SimilarGame>> Cache = new Dictionary<int, List<SimilarGame>>();

    /// <summary>
    /// Les jeux similaires à appId, dans l'ordre de Steam, sans ceux de skipAppIds (déjà possédés…).
    /// Au plus count jeux.
    /// </summary>
    public static async Task<List<SimilarGame>> GetAsync(int appId, IReadOnlySet<int> skipAppIds, int count)
    {
        List<SimilarGame> all = await GetAllAsync(appId);

        return all
            .Where(game => game.AppId != appId && !skipAppIds.Contains(game.AppId))
            .Take(count)
            .ToList();
    }

    private static async Task<List<SimilarGame>> GetAllAsync(int appId)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(appId, out List<SimilarGame>? cached))
            {
                return cached;
            }
        }

        string html = await Http.GetStringAsync($"https://store.steampowered.com/recommended/morelike/app/{appId}/?l=french&cc=fr");
        IDocument document = new HtmlParser().ParseDocument(html);

        // Les jeux déjà sortis d'abord ; si Steam change la page, tous les jeux de la grille.
        IHtmlCollection<IElement> capsules = document.QuerySelectorAll("#released a.similar_grid_capsule[data-ds-appid]");

        if (capsules.Length == 0)
        {
            capsules = document.QuerySelectorAll("a.similar_grid_capsule[data-ds-appid]");
        }

        // Numéro → image, dans l'ordre de la page, sans doublon (un lot a plusieurs numéros : écarté).
        var images = new List<(int AppId, string ImageUrl)>();

        foreach (IElement capsule in capsules)
        {
            if (int.TryParse(capsule.GetAttribute("data-ds-appid"), out int similarId)
                && images.All(known => known.AppId != similarId))
            {
                images.Add((similarId, capsule.QuerySelector("img")?.GetAttribute("src")
                    ?? $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{similarId}/capsule_616x353.jpg"));
            }
        }

        // Les noms : un seul appel pour tous (les 20 premiers suffisent largement pour en garder 3).
        images = images.Take(20).ToList();
        Dictionary<int, string> names = images.Count == 0
            ? new Dictionary<int, string>()
            : (await SteamStoreApi.GetItemsAsync(images.Select(image => image.AppId)))
                .Where(item => item.Name.Length > 0)
                .GroupBy(item => item.AppId)
                .ToDictionary(group => group.Key, group => group.First().Name);

        List<SimilarGame> result = images
            .Where(image => names.ContainsKey(image.AppId))
            .Select(image => new SimilarGame(image.AppId, names[image.AppId], image.ImageUrl))
            .ToList();

        lock (Cache)
        {
            Cache[appId] = result;
        }

        return result;
    }
}
