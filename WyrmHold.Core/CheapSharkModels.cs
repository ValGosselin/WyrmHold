using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

// Les classes ci-dessous ont la même forme que le JSON renvoyé par CheapShark.
// Particularité de cette API : les prix arrivent sous forme de texte ("4.99"), pas de nombre.
// [JsonNumberHandling(AllowReadingFromString)] dit à System.Text.Json d'accepter ce texte
// et de le convertir en decimal (toujours avec le point, quelle que soit la langue de Windows).

/// <summary>Un résultat de recherche par titre (/games?title=...).</summary>
public class CheapSharkSearchResult
{
    [JsonPropertyName("gameID")]
    public string GameId { get; set; } = "";

    // Peut être vide : tous les jeux suivis par CheapShark ne sont pas sur Steam.
    [JsonPropertyName("steamAppID")]
    public string? SteamAppId { get; set; }

    // « external » est le nom du jeu tel qu'il s'affiche.
    [JsonPropertyName("external")]
    public string Title { get; set; } = "";

    [JsonPropertyName("cheapest")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal CheapestPrice { get; set; }
}

/// <summary>La fiche d'un jeu (/games?id=...) : ses infos, son plus bas historique et ses offres.</summary>
public class CheapSharkGameDetails
{
    [JsonPropertyName("info")]
    public CheapSharkGameInfo? Info { get; set; }

    [JsonPropertyName("cheapestPriceEver")]
    public CheapSharkLowestPrice? CheapestPriceEver { get; set; }

    [JsonPropertyName("deals")]
    public List<CheapSharkDeal> Deals { get; set; } = new List<CheapSharkDeal>();
}

public class CheapSharkGameInfo
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("steamAppID")]
    public string? SteamAppId { get; set; }
}

public class CheapSharkLowestPrice
{
    [JsonPropertyName("price")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Price { get; set; }

    // Date au format « timestamp Unix » : nombre de secondes depuis le 1er janvier 1970.
    [JsonPropertyName("date")]
    public long Date { get; set; }
}

/// <summary>Une offre : un prix dans une boutique.</summary>
public class CheapSharkDeal
{
    // Numéro de la boutique : le nom se trouve dans la liste /stores.
    [JsonPropertyName("storeID")]
    public string StoreId { get; set; } = "";

    [JsonPropertyName("dealID")]
    public string DealId { get; set; } = "";

    [JsonPropertyName("price")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Price { get; set; }

    // Prix normal, hors promo.
    [JsonPropertyName("retailPrice")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal RetailPrice { get; set; }

    // Réduction en pourcentage (ex. 75.012504).
    [JsonPropertyName("savings")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Savings { get; set; }
}

/// <summary>Une boutique suivie par CheapShark (/stores).</summary>
public class CheapSharkStore
{
    [JsonPropertyName("storeID")]
    public string StoreId { get; set; } = "";

    [JsonPropertyName("storeName")]
    public string StoreName { get; set; } = "";

    // 1 = boutique encore suivie, 0 = plus suivie.
    [JsonPropertyName("isActive")]
    public int IsActive { get; set; }
}
