using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Une liste de promos IsThereAnyDeal chargée page par page (onglets « 🔥 Promos » et « ✨ Pour toi »).
/// Elle retient les promos déjà reçues, où reprendre, et l'état du chargement.
/// La façon de charger une page (quel tri, quels genres…) est donnée à la création : loadPage(offset).
/// </summary>
public class DealsFeed
{
    private readonly Func<int, Task<ItadDealsPage>> _loadPage;
    private int _nextOffset;

    public DealsFeed(Func<int, Task<ItadDealsPage>> loadPage)
    {
        _loadPage = loadPage;
    }

    public List<ItadDealListItem> Items { get; private set; } = new List<ItadDealListItem>();
    public bool HasMore { get; private set; }
    public bool IsLoaded { get; private set; }
    public bool IsLoading { get; private set; }
    public bool LoadFailed { get; private set; }

    /// <summary>
    /// Charge une page. reset = true : on repart du début (premier affichage, tri changé, ↻) ;
    /// reset = false : on ajoute la page suivante à la suite (« Charger plus »).
    /// IsLoading passe à true AVANT la première attente : l'appelant peut tout de suite afficher « Chargement… ».
    /// </summary>
    public async Task LoadAsync(bool reset)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;

        if (reset)
        {
            Items = new List<ItadDealListItem>();
            _nextOffset = 0;
            HasMore = false;
        }

        try
        {
            ItadDealsPage page = await _loadPage(_nextOffset);

            // Si la liste du site a bougé entre deux pages, un même jeu pourrait revenir :
            // HashSet.Add renvoie false quand l'identifiant y est déjà, et on ne l'ajoute pas une 2e fois.
            HashSet<string> knownIds = Items.Select(item => item.Id).ToHashSet();

            foreach (ItadDealListItem item in page.List)
            {
                if (knownIds.Add(item.Id))
                {
                    Items.Add(item);
                }
            }

            _nextOffset = page.NextOffset;
            HasMore = page.HasMore;
            IsLoaded = true;
            LoadFailed = false;
        }
        catch (Exception ex)
        {
            LoadFailed = true;
            Logger.Log($"Chargement de promos IsThereAnyDeal impossible : {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
