using System.Text.Json;
using System.Text.RegularExpressions;

namespace Wyrmhold.Core;

/// <summary>
/// Lit l'historique des commandes du compte EA pour en déduire les jeux possédés.
/// </summary>
public static class EaOrderHistory
{
    // La page « Historique des commandes » du site, et l'adresse des données qu'elle affiche.
    public const string PageUrl = "https://myaccount.ea.com/cp-ui/orderhistory/index";
    public const string Url = "https://myaccount.ea.com/am/data/1/order-history?dateRange=ALL";

    // Essais, démos et versions d'évaluation : on ne les compte pas comme des jeux possédés.
    private static readonly Regex NotAFullGame = new Regex(
        @"\b(trial|essai|d[ée]mo|demo)\b",
        RegexOptions.IgnoreCase);

    public static List<string> ReadOwnedGameNames(string json)
    {
        List<string> names = new List<string>();
        int orderCount = 0;

        using JsonDocument document = JsonDocument.Parse(json);

        foreach (JsonElement order in FindOrders(document.RootElement))
        {
            orderCount++;

            string? orderStatus = ReadString(order, "status");
            string? transactionType = ReadString(order, "transactionType");

            if (orderStatus != "Completed"
                || (transactionType is not null && transactionType != "DEBIT")
                || ReadBool(order, "gift"))
            {
                Logger.Log($"Commande EA écartée (état {orderStatus}, type {transactionType}, cadeau {ReadBool(order, "gift")})");
                continue;
            }

            foreach (JsonElement item in order.GetProperty("items").EnumerateArray())
            {
                string? name = ReadString(item, "name");
                string? reason = GetRejectionReason(item, name);

                if (reason is not null)
                {
                    Logger.Log($"Article EA écarté ({reason}) : {name}");
                    continue;
                }

                names.Add(NameTools.CleanForSearch(name!));
            }
        }

        Logger.Log($"Historique EA : {orderCount} commande(s) lue(s), {names.Count} jeu(x) retenu(s)");
        return names;
    }

    /// <summary>
    /// Renvoie la raison pour laquelle un article n'est pas un jeu possédé, ou null s'il l'est.
    /// </summary>
    private static string? GetRejectionReason(JsonElement item, string? name)
    {
        string? status = ReadString(item, "status");
        string? platform = ReadString(item, "platform");

        if (string.IsNullOrWhiteSpace(name))
        {
            return "sans nom";
        }

        if (status != "FULFILLED")
        {
            return $"état {status}";
        }

        if (platform is null || !platform.Contains("PC", StringComparison.OrdinalIgnoreCase))
        {
            return $"plateforme {platform}";
        }

        if (ReadBool(item, "subscription"))
        {
            return "abonnement";
        }

        if (NotAFullGame.IsMatch(name))
        {
            return "essai ou démo";
        }

        return null;
    }

    /// <summary>
    /// Cherche les commandes partout dans la réponse, quel que soit le niveau où elles sont rangées :
    /// une commande est un objet qui a un numéro de commande et une liste d'articles.
    /// </summary>
    private static IEnumerable<JsonElement> FindOrders(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("orderNumber", out _)
                && element.TryGetProperty("items", out JsonElement items)
                && items.ValueKind == JsonValueKind.Array)
            {
                yield return element;
                yield break;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                foreach (JsonElement order in FindOrders(property.Value))
                {
                    yield return order;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                foreach (JsonElement order in FindOrders(child))
                {
                    yield return order;
                }
            }
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }
}