using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Wyrmhold.Core;

/// <summary>Une page de jeux gratuits Steam, et s'il en reste après.</summary>
public record FreeGamesPage(List<FreeGame> Games, int TotalCount, int NextStart, bool HasMore);

/// <summary>
/// Les démos et les jeux gratuits de la boutique Steam, lus page par page dans la recherche de la boutique
/// (celle du site, sans clé). Ce n'est pas une API documentée : vérifié en console le 10 octobre 2026.
///   store.steampowered.com/search/results/?infinite=1&amp;start=0&amp;count=50&amp;cc=fr&amp;l=french
///   + category1=10                   → démos (40 128 le 10 octobre 2026)
///   + maxprice=free&amp;category1=998    → jeux gratuits, sans les DLC ni les logiciels (22 877)
///   + sort_by=Released_DESC          → nouveautés (sans sort_by : classement de Steam, les plus populaires d'abord)
///   + term=…                         → recherche par nom
/// Réponse : du JSON avec total_count et results_html (une balise &lt;a class="search_result_row"&gt; par jeu).
/// </summary>
public static class SteamFreeCatalog
{
    public const int PageSize = 50;

    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// Une page de démos (source = SteamDemo), de jeux gratuits (SteamFreeToPlay) ou de tous les jeux (SteamGame,
    /// pour l'onglet Résultats filtré par tag).
    /// newestFirst : nouveautés d'abord ; sinon, l'ordre de Steam (les plus populaires).
    /// search : un nom à chercher, ou vide. tagId : un tag de Steam (« tags=122 » = RPG, vérifié le 10 octobre 2026), ou null.
    /// </summary>
    public static async Task<FreeGamesPage> GetPageAsync(FreeGameSource source, int start, bool newestFirst, string search,
        int? tagId = null, CancellationToken token = default)
    {
        string filter = source switch
        {
            FreeGameSource.SteamDemo => "category1=10",
            FreeGameSource.SteamFreeToPlay => "maxprice=free&category1=998",
            _ => "category1=998"   // jeux seulement (pas de DLC ni de logiciels)
        };
        string url = $"https://store.steampowered.com/search/results/?infinite=1&start={start}&count={PageSize}&cc=fr&l=french&{filter}";

        if (newestFirst)
        {
            url += "&sort_by=Released_DESC";
        }

        if (tagId is int tag)
        {
            url += "&tags=" + tag;
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            url += "&term=" + Uri.EscapeDataString(search.Trim());
        }

        string json = await Http.GetStringAsync(url, token);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        int total = root.TryGetProperty("total_count", out JsonElement totalElement) && totalElement.ValueKind == JsonValueKind.Number
            ? totalElement.GetInt32()
            : 0;
        string html = root.TryGetProperty("results_html", out JsonElement htmlElement) ? htmlElement.GetString() ?? "" : "";

        List<FreeGame> games = ParseRows(html, source);

        // On avance du nombre de lignes reçues (et non de PageSize) : si Steam en renvoie moins, on ne saute rien.
        int rowCount = CountRows(html);
        int nextStart = start + rowCount;

        return new FreeGamesPage(games, total, nextStart, rowCount > 0 && nextStart < total);
    }

    private static int CountRows(string html)
    {
        return Regex.Matches(html, "class=\"search_result_row").Count;
    }

    /// <summary>Transforme le HTML des résultats en jeux : appid, nom, image, date de sortie, avis.</summary>
    private static List<FreeGame> ParseRows(string html, FreeGameSource source)
    {
        var games = new List<FreeGame>();

        // AngleSharp lit le HTML comme un navigateur, puis on cherche les éléments avec des sélecteurs CSS.
        IDocument document = new HtmlParser().ParseDocument(html);

        foreach (IElement row in document.QuerySelectorAll("a.search_result_row"))
        {
            // Les lots (bundles) ont plusieurs appid séparés par des virgules, ou aucun : on les écarte,
            // on ne garde que les jeux qu'on peut installer d'un coup.
            if (!int.TryParse(row.GetAttribute("data-ds-appid"), out int appId))
            {
                continue;
            }

            string title = row.QuerySelector("span.title")?.TextContent.Trim() ?? "";

            if (title.Length == 0)
            {
                continue;
            }

            games.Add(new FreeGame
            {
                Source = source,
                Title = title,
                SteamAppId = appId,
                ImageUrl = row.QuerySelector("div.search_capsule img")?.GetAttribute("src"),

                // header.jpg sans le dossier « haché » : c'est cette adresse qui répond (vérifié le 10 octobre 2026).
                CoverUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg",
                StoreUrl = $"https://store.steampowered.com/app/{appId}/",
                ReleaseText = row.QuerySelector("div.search_released")?.TextContent.Trim() ?? "",
                ReviewText = ReadReviews(row.QuerySelector("span.search_review_summary")?.GetAttribute("data-tooltip-html"))
            });
        }

        return games;
    }

    /// <summary>
    /// « extrêmement positives&lt;br&gt;98 % des 771 évaluations… » → « 98 % d'avis positifs (771 avis) ».
    /// Texte inattendu : on n'affiche rien plutôt qu'une information fausse.
    /// </summary>
    private static string ReadReviews(string? tooltip)
    {
        if (string.IsNullOrEmpty(tooltip))
        {
            return "";
        }

        // \s couvre les espaces insécables que Steam met entre les milliers (« 12 345 »).
        Match match = Regex.Match(tooltip, @"(\d+)\s*%\s*des\s+([\d\s.,]+?)\s+évaluation");

        if (!match.Success)
        {
            return "";
        }

        string count = Regex.Replace(match.Groups[2].Value, @"[\s.,]", " ").Trim();
        return $"{match.Groups[1].Value} % d'avis positifs ({count} avis)";
    }
}
