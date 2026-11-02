namespace Wyrmhold.Core;

/// <summary>
/// L'état de la fenêtre principale quand on quitte Wyrmhold (ou qu'on le range en arrière-plan),
/// enregistré dans settings.json et remis au démarrage : on retrouve la même config.
/// La recherche tapée n'est pas gardée (au redémarrage, des jeux « disparus » seraient déroutants),
/// ni l'onglet ouvert : Wyrmhold démarre toujours sur la Bibliothèque (choix de Val du 2 novembre 2026).
/// </summary>
public class UiState
{
    // ----- Fenêtre -----

    // Position et taille « normales » (même si la fenêtre était agrandie). null = jamais enregistrées.
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    // ----- Bibliothèque -----

    // Filtres et tri, au même format que les vues enregistrées. null = rien d'enregistré.
    public SavedViewFilters? LibraryFilters { get; set; }

    // Le jeu sélectionné, sous la forme « Steam_1245620 » (voir GameWatcher.KeyOf).
    public string? SelectedGameKey { get; set; }

    // ----- Boutiques -----

    // La liste de gauche : « results », « watchlist », « promos », « foryou » ou « free ».
    public string ShopsLeftTab { get; set; } = "results";

    // Onglet « 🎁 Gratuits » : la liste (« demos », « f2p » ou « epic ») et le tri (« » = populaires, « new »).
    public string FreeKind { get; set; } = "demos";
    public string FreeSort { get; set; } = "";
    public string PromosSort { get; set; } = "";
    public string ForYouSort { get; set; } = "rank";
    public string DealsSort { get; set; } = "price";
    public bool GamesOnly { get; set; }
    public bool HideOwned { get; set; }
    public bool OnlyDeals { get; set; }
}
