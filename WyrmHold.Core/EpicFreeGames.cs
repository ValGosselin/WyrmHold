using System.Globalization;
using System.Text.Json;

namespace Wyrmhold.Core;

/// <summary>
/// Les jeux offerts par l'Epic Games Store (celui de la semaine et ceux annoncés), sans connexion ni clé.
/// Adresse utilisée par la page d'accueil de la boutique Epic, vérifiée le 10 octobre 2026 :
///   store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=fr&amp;country=FR
/// Un jeu est offert quand une de ses promotions vaut 0 % du prix (« discountPercentage »: 0) :
/// dans « promotionalOffers » s'il l'est maintenant, dans « upcomingPromotionalOffers » s'il le sera bientôt.
/// La liste contient aussi des jeux simplement en vitrine (sans promotion gratuite) : on les écarte.
/// Pour récupérer un jeu, il faut cliquer « Obtenir » sur sa page en étant connecté : Wyrmhold ouvre la page.
/// </summary>
public static class EpicFreeGames
{
    private const string Url = "https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=fr&country=FR";

    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Les jeux offerts en ce moment, puis ceux à venir (du plus proche au plus lointain).</summary>
    public static async Task<List<FreeGame>> GetAsync(CancellationToken token = default)
    {
        string json = await Http.GetStringAsync(Url, token);

        using JsonDocument document = JsonDocument.Parse(json);
        var games = new List<FreeGame>();

        if (!TryGetPath(document.RootElement, out JsonElement elements, "data", "Catalog", "searchStore", "elements"))
        {
            return games;
        }

        foreach (JsonElement element in elements.EnumerateArray())
        {
            // Le jeu offert maintenant, sinon le prochain : la première promotion à 0 %.
            (DateTime From, DateTime Until)? period = FindFreePeriod(element, "promotionalOffers")
                ?? FindFreePeriod(element, "upcomingPromotionalOffers");

            if (period == null)
            {
                continue;
            }

            string title = GetString(element, "title");
            string? slug = FindPageSlug(element);

            if (title.Length == 0 || slug == null)
            {
                continue;
            }

            games.Add(new FreeGame
            {
                Source = FreeGameSource.EpicGiveaway,
                Title = title,
                ImageUrl = FindImage(element, "Thumbnail") ?? FindImage(element, "OfferImageWide"),
                CoverUrl = FindImage(element, "OfferImageWide") ?? FindImage(element, "OfferImageTall"),
                StoreUrl = $"https://store.epicgames.com/fr/p/{slug}",
                Description = GetString(element, "description"),
                FreeFrom = period.Value.From,
                FreeUntil = period.Value.Until
            });
        }

        // Offerts maintenant d'abord, puis par date de début.
        return games
            .OrderBy(game => game.IsAvailableNow ? 0 : 1)
            .ThenBy(game => game.FreeFrom)
            .ToList();
    }

    /// <summary>La période (heure de ton PC) de la première promotion à 0 % de la liste demandée, ou null.</summary>
    private static (DateTime, DateTime)? FindFreePeriod(JsonElement element, string listName)
    {
        // Structure : promotions.<listName>[].promotionalOffers[] { startDate, endDate, discountSetting.discountPercentage }
        if (!TryGetPath(element, out JsonElement groups, "promotions", listName) || groups.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("promotionalOffers", out JsonElement offers) || offers.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement offer in offers.EnumerateArray())
            {
                bool isFree = TryGetPath(offer, out JsonElement percent, "discountSetting", "discountPercentage")
                    && percent.ValueKind == JsonValueKind.Number && percent.GetInt32() == 0;

                if (isFree
                    && TryParseDate(GetString(offer, "startDate"), out DateTime from)
                    && TryParseDate(GetString(offer, "endDate"), out DateTime until))
                {
                    return (from, until);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Le morceau d'adresse de la page du jeu (« out-of-sight-b96ca8 »). Selon les jeux, Epic le range
    /// dans offerMappings, catalogNs.mappings ou productSlug (qui finit parfois par « /home »).
    /// </summary>
    private static string? FindPageSlug(JsonElement element)
    {
        foreach (string[] path in new[] { new[] { "offerMappings" }, new[] { "catalogNs", "mappings" } })
        {
            if (TryGetPath(element, out JsonElement list, path) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement mapping in list.EnumerateArray())
                {
                    string slug = GetString(mapping, "pageSlug");

                    if (slug.Length > 0)
                    {
                        return slug;
                    }
                }
            }
        }

        string productSlug = GetString(element, "productSlug");

        if (productSlug.EndsWith("/home"))
        {
            productSlug = productSlug.Substring(0, productSlug.Length - "/home".Length);
        }

        return productSlug.Length > 0 ? productSlug : null;
    }

    private static string? FindImage(JsonElement element, string type)
    {
        if (!element.TryGetProperty("keyImages", out JsonElement images) || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement image in images.EnumerateArray())
        {
            if (GetString(image, "type") == type)
            {
                string url = GetString(image, "url");
                return url.Length > 0 ? url : null;
            }
        }

        return null;
    }

    // Les dates d'Epic sont en temps universel (« 2026-10-15T15:00:00.000Z ») : on les passe à l'heure du PC.
    private static bool TryParseDate(string text, out DateTime date)
    {
        bool ok = DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime utc);
        date = ok ? utc.ToLocalTime() : default;
        return ok;
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    /// <summary>Descend dans le JSON propriété par propriété ; faux si l'une d'elles manque.</summary>
    private static bool TryGetPath(JsonElement start, out JsonElement result, params string[] names)
    {
        result = start;

        foreach (string name in names)
        {
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(name, out result))
            {
                return false;
            }
        }

        return true;
    }
}
