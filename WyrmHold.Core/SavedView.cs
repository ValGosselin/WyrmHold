namespace Wyrmhold.Core;

/// <summary>
/// L'état de tous les filtres et du tri, tel qu'on l'enregistre dans une vue.
/// Les propriétés ont un « set » public pour que le JSON puisse les remplir à la relecture.
/// </summary>
public class SavedViewFilters
{
    public string SearchText { get; set; } = "";
    public List<string> Platforms { get; set; } = new List<string>();
    public string InstallFilter { get; set; } = "all";
    public string OriginFilter { get; set; } = "all";
    public string ActivityFilter { get; set; } = "all";
    public string TagFilter { get; set; } = "all";

    // Absent des vues enregistrées avant la phase 5 : le JSON garde alors la valeur par défaut.
    public string AchievementFilter { get; set; } = "all";
    public long CollectionId { get; set; }
    public bool FavoritesOnly { get; set; }
    public string SortMode { get; set; } = "name";
}

/// <summary>
/// Une vue enregistrée : un nom et ses réglages.
/// </summary>
public record SavedView(long Id, string Name, SavedViewFilters Filters);
