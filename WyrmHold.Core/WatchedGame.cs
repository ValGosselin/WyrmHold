namespace Wyrmhold.Core;

/// <summary>Un jeu de la liste de suivi (table WatchedGame).</summary>
public class WatchedGame
{
    // L'identifiant IsThereAnyDeal du jeu : il suffit pour redemander ses prix.
    public string ItadId { get; set; } = "";

    public string Title { get; set; } = "";

    // « game », « dlc »… comme dans les résultats de recherche.
    public string Type { get; set; } = "";

    // Date d'ajout à la liste (secondes depuis 1970, comme les autres dates de la base).
    public long AddedUnix { get; set; }

    // ----- Dernier prix connu (rempli par WatchListService.CheckPricesAsync) -----

    // Meilleur prix du moment, ou null si aucune boutique ne vend le jeu en ce moment.
    public decimal? BestPrice { get; set; }

    // La boutique qui propose ce meilleur prix.
    public string? BestShop { get; set; }

    // Réduction de cette offre, en %.
    public int BestCut { get; set; }

    // Plus bas historique, toutes boutiques confondues (null si inconnu).
    public decimal? HistoryLow { get; set; }

    // « EUR » pour les prix en France.
    public string Currency { get; set; } = "";

    // Date de la dernière vérification (0 = jamais vérifié).
    public long PricesCheckedUnix { get; set; }

    // ----- Synchro avec la Waitlist IsThereAnyDeal (seulement si le compte est relié) -----

    // Vrai si le jeu a déjà été vu sur ta Waitlist. S'il disparaît ensuite du site,
    // c'est que tu l'y as retiré : on le retire aussi ici.
    public bool SyncedWithItad { get; set; }
}
