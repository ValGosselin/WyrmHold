using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

// Les classes ci-dessous ont la même forme que le JSON d'IsThereAnyDeal.
// On ne garde que les champs utiles pour l'instant : System.Text.Json ignore les autres.
// Contrairement à CheapShark, les prix sont de vrais nombres ici, et la devise est fournie.

/// <summary>Un résultat de recherche par titre (/games/search/v1).</summary>
public class ItadSearchResult
{
    // L'identifiant IsThereAnyDeal du jeu (un UUID) : c'est lui qu'on donne à /games/prices/v3.
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    // « game », « dlc » ou « package » ; null pour certains produits (les bandes-son, par exemple).
    // D'où le « ? » : sans lui, le compilateur croirait que la valeur n'est jamais null.
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

/// <summary>Réponse de /games/lookup/v1 : {"found":true,"game":{"id":…,"title":…,"type":…}} ou {"found":false}.</summary>
public class ItadLookupResponse
{
    [JsonPropertyName("found")]
    public bool Found { get; set; }

    [JsonPropertyName("game")]
    public ItadSearchResult? Game { get; set; }
}

/// <summary>Un jeu de ta Waitlist sur le site (/waitlist/games/v1).</summary>
public class ItadWaitlistGame
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

/// <summary>Les prix d'un jeu (/games/prices/v3) : plus bas historique et offres par boutique.</summary>
public class ItadGamePrices
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("historyLow")]
    public ItadHistoryLow? HistoryLow { get; set; }

    [JsonPropertyName("deals")]
    public List<ItadDeal> Deals { get; set; } = new List<ItadDeal>();
}

/// <summary>Les plus bas prix connus : depuis toujours, sur 1 an, sur 3 mois.</summary>
public class ItadHistoryLow
{
    [JsonPropertyName("all")]
    public ItadPrice? AllTime { get; set; }

    [JsonPropertyName("y1")]
    public ItadPrice? OneYear { get; set; }

    [JsonPropertyName("m3")]
    public ItadPrice? ThreeMonths { get; set; }
}

/// <summary>Une offre : un prix dans une boutique.</summary>
public class ItadDeal
{
    [JsonPropertyName("shop")]
    public ItadShop Shop { get; set; } = new ItadShop();

    // Prix actuel.
    [JsonPropertyName("price")]
    public ItadPrice Price { get; set; } = new ItadPrice();

    // Prix normal, hors promo.
    [JsonPropertyName("regular")]
    public ItadPrice Regular { get; set; } = new ItadPrice();

    // Réduction en pourcentage (nombre entier, ex. 75).
    [JsonPropertyName("cut")]
    public int Cut { get; set; }

    // Le plus bas prix déjà vu dans CETTE boutique.
    [JsonPropertyName("storeLow")]
    public ItadPrice? StoreLow { get; set; }

    // Plus bas historique toutes boutiques : fourni dans la liste des promos (/deals/v2),
    // absent (null) dans /games/prices/v3 où il est donné pour le jeu entier.
    [JsonPropertyName("historyLow")]
    public ItadPrice? HistoryLow { get; set; }

    // Lien vers l'offre. Règle d'IsThereAnyDeal : ne jamais le modifier.
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public class ItadShop
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class ItadPrice
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    // Code de la devise : « EUR » quand on demande les prix en France.
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";
}

/// <summary>La fiche d'un jeu (/games/info/v2) : sert à présenter le jeu au-dessus de ses prix.</summary>
public class ItadGameInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("assets")]
    public ItadAssets? Assets { get; set; }

    // Le numéro du jeu sur Steam (null s'il n'y est pas) : sert à demander sa description à Steam.
    [JsonPropertyName("appid")]
    public int? AppId { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new List<string>();

    // Date au format « 2025-09-25 » (texte), ou null si inconnue.
    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("developers")]
    public List<ItadCompany> Developers { get; set; } = new List<ItadCompany>();

    // Les notes du jeu, une par site (Steam, Metacritic, OpenCritic…).
    [JsonPropertyName("reviews")]
    public List<ItadReview> Reviews { get; set; } = new List<ItadReview>();
}

public class ItadAssets
{
    // La jaquette verticale (format boîte de jeu).
    [JsonPropertyName("boxart")]
    public string? Boxart { get; set; }

    [JsonPropertyName("banner300")]
    public string? Banner300 { get; set; }
}

public class ItadCompany
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class ItadReview
{
    // Note sur 100 (ex. 96 = 96 % d'avis positifs pour Steam).
    [JsonPropertyName("score")]
    public int? Score { get; set; }

    // Le site qui donne la note : « Steam », « Metacritic »…
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    // Le nombre d'avis.
    [JsonPropertyName("count")]
    public int? Count { get; set; }
}

/// <summary>Une page de la liste des meilleures promos du moment (/deals/v2).</summary>
public class ItadDealsPage
{
    // Où reprendre pour charger la page suivante.
    [JsonPropertyName("nextOffset")]
    public int NextOffset { get; set; }

    // Vrai s'il reste des promos après cette page.
    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }

    [JsonPropertyName("list")]
    public List<ItadDealListItem> List { get; set; } = new List<ItadDealListItem>();
}

/// <summary>Un jeu de la liste des promos, avec sa meilleure offre du moment.</summary>
public class ItadDealListItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("deal")]
    public ItadDeal Deal { get; set; } = new ItadDeal();
}
