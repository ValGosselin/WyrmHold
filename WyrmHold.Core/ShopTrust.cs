namespace Wyrmhold.Core;

/// <summary>
/// Fiabilité d'une boutique, pour le comparateur de prix.
/// IsThereAnyDeal ne liste que des boutiques qu'il juge agréées (clés venant des éditeurs
/// ou de distributeurs officiels) : les sites de revente de clés sont déjà exclus.
/// On distingue donc seulement la boutique officielle d'une plateforme et les revendeurs agréés.
/// </summary>
public static class ShopTrust
{
    // Noms des boutiques tels qu'IsThereAnyDeal les écrit, passés dans NameTools.Normalize
    // (minuscules, sans espaces ni ponctuation) pour que « Epic Game Store » = « epicgamestore ».
    private static readonly HashSet<string> OfficialShops = new HashSet<string>
    {
        NameTools.Normalize("Steam"),
        NameTools.Normalize("GOG"),
        NameTools.Normalize("Epic Game Store"),
        NameTools.Normalize("Ubisoft Store"),
        NameTools.Normalize("EA Store"),
        NameTools.Normalize("Blizzard"),
        NameTools.Normalize("Microsoft Store")
    };

    /// <summary>Vrai si la boutique appartient à la plateforme elle-même (Steam, Epic, GOG…).</summary>
    public static bool IsOfficial(string shopName)
    {
        return OfficialShops.Contains(NameTools.Normalize(shopName));
    }
}
