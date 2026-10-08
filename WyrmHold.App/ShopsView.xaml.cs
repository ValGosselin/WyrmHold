using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Onglet « Boutiques » : recherche d'un jeu sur IsThereAnyDeal, puis ses prix en France.
/// </summary>
public partial class ShopsView : UserControl
{
    // secrets.json n'est lu qu'une fois (static : partagé, initialisé avant les champs ci-dessous).
    private static readonly Secrets AppSecrets = Secrets.Load();

    private readonly IsThereAnyDealApi _itad = new IsThereAnyDealApi(AppSecrets.IsThereAnyDealApiKey);

    // Ton compte IsThereAnyDeal, s'il est relié (facultatif) : sert à synchroniser la Waitlist.
    private readonly ItadAccount _itadAccount = new ItadAccount(AppSecrets.IsThereAnyDealClientId);

    // Numéro de la dernière demande de prix (même principe que pour les patch notes) :
    // si on clique sur un autre jeu avant la réponse, l'ancienne réponse est ignorée.
    private int _pricesRequest;

    // La présentation des jeux (jaquette, genres, description), gardée en mémoire jeu par jeu.
    private readonly GamePresentationService _presentations;

    // Tous les résultats de la dernière recherche, avant le filtre « Jeux et éditions seulement ».
    private List<ItadSearchResult> _searchResults = new List<ItadSearchResult>();

    // Les offres du jeu affiché, dans l'ordre reçu. On les garde pour pouvoir les filtrer
    // et les retrier sans redemander les prix à IsThereAnyDeal.
    private List<DealRow> _deals = new List<DealRow>();

    // Les boutiques décochées. HashSet : recherche très rapide de « ce nom est-il dedans ? ».
    private HashSet<string> _hiddenShops = new HashSet<string>();

    private AppSettings? _settings;

    // Ta bibliothèque, rangée par nom « normalisé » (minuscules, sans ponctuation) :
    // « Hogwarts Legacy™ » et « Hogwarts Legacy » tombent dans la même case.
    // Un même nom peut correspondre à plusieurs copies (Steam + Epic…), d'où la liste.
    private Dictionary<string, List<Game>> _libraryByName = new Dictionary<string, List<Game>>();

    // Vrai pendant qu'on reconstruit la liste des résultats : le changement de sélection
    // que ça provoque ne doit pas relancer une demande de prix.
    private bool _isRebuildingResults;

    // Vrai dès qu'une recherche a été lancée : avant, on n'affiche pas « Aucun jeu trouvé ».
    private bool _hasSearched;

    // La liste de suivi (base locale) et le choix d'affichage à gauche : résultats ou suivis.
    private readonly WatchListService _watchList = new WatchListService();
    private LeftListMode _leftMode = LeftListMode.Results;

    // Onglet Promos : les promos chargées (page après page), où reprendre, et l'état du chargement.
    private const int PromosPageSize = 50;
    private List<ItadDealListItem> _promos = new List<ItadDealListItem>();
    private int _promosNextOffset;
    private bool _promosHasMore;
    private bool _promosLoaded;
    private bool _isLoadingPromos;
    private bool _promosLoadFailed;

    // Le jeu dont les offres sont affichées à droite (null tant qu'aucun jeu n'a été choisi).
    private SearchResultRow? _currentGame;

    // État de la vérification des prix des jeux suivis.
    private bool _isCheckingPrices;
    private bool _lastCheckFailed;
    private bool _lastSyncFailed;

    /// <summary>
    /// Les réglages de l'appli, donnés par MainWindow (le même objet que le reste de la fenêtre).
    /// Quand on les reçoit, on reprend la liste des boutiques masquées.
    /// </summary>
    public AppSettings? Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            _hiddenShops = new HashSet<string>(value?.HiddenShops ?? new List<string>());
        }
    }

    public ShopsView()
    {
        InitializeComponent();
        _presentations = new GamePresentationService(_itad);

        if (!_itad.IsConfigured)
        {
            ShopStatusText.Text = "Clé IsThereAnyDealApiKey absente de secrets.json.";
            ShopSearchButton.IsEnabled = false;
        }

        try
        {
            // La base a déjà été préparée par LibraryService (créé par MainWindow avant cet onglet).
            _watchList.Load();
        }
        catch (Exception ex)
        {
            Logger.Log($"Lecture de la liste de suivi impossible : {ex.Message}");
        }

        UpdateWatchListTab();
    }

    // ===================== Bibliothèque =====================

    /// <summary>
    /// Reçoit les jeux de la bibliothèque (appelé par MainWindow à chaque chargement ou actualisation)
    /// et met à jour les badges « Déjà dans ta bibliothèque ».
    /// </summary>
    public void SetLibrary(IEnumerable<Game> games)
    {
        _libraryByName = games
            .GroupBy(game => NormalizeTitle(game.Name))
            .ToDictionary(group => group.Key, group => group.ToList());

        RefreshLeftList();
    }

    private static string NormalizeTitle(string title)
    {
        return NameTools.Normalize(NameTools.CleanForSearch(title));
    }

    /// <summary>
    /// Si le jeu est dans ta bibliothèque, renvoie ses plateformes (« Steam, Epic Games (famille) ») ;
    /// sinon null.
    /// </summary>
    private string? GetOwnedPlatforms(string title)
    {
        if (!_libraryByName.TryGetValue(NormalizeTitle(title), out List<Game>? copies))
        {
            return null;
        }

        IEnumerable<string> platforms = copies
            .Select(game => game.IsFamilyShared == true ? $"{game.PlatformName} (famille)" : game.PlatformName)
            .Distinct();

        return string.Join(", ", platforms);
    }

    /// <summary>La ligne verte sous le titre du jeu affiché à droite.</summary>
    private void UpdateOwnedText()
    {
        string? ownedPlatforms = _currentGame == null ? null : GetOwnedPlatforms(_currentGame.Title);

        if (ownedPlatforms != null)
        {
            OwnedText.Text = $"✓ Déjà dans ta bibliothèque : {ownedPlatforms}";
            OwnedText.Visibility = Visibility.Visible;
        }
        else
        {
            OwnedText.Visibility = Visibility.Collapsed;
        }
    }

    // ===================== Liste de suivi =====================

    private async void LeftTab_Checked(object sender, RoutedEventArgs e)
    {
        // Pendant InitializeComponent, les boutons placés après dans le XAML n'existent pas encore :
        // « ?. » renvoie null au lieu de planter, et null == true est faux.
        _leftMode = WatchListTabButton?.IsChecked == true ? LeftListMode.WatchList
            : PromosTabButton?.IsChecked == true ? LeftListMode.Promos
            : LeftListMode.Results;

        RefreshLeftList();

        // Première ouverture de l'onglet Promos : on charge la liste (une seule requête).
        if (_leftMode == LeftListMode.Promos && !_promosLoaded)
        {
            await LoadPromosAsync(reset: true);
        }
    }

    // ===================== Meilleures promos =====================

    /// <summary>
    /// Charge une page de promos. reset = true : on repart du début (premier affichage, tri changé, ↻) ;
    /// reset = false : on ajoute la page suivante à la suite (« Charger 50 promos de plus »).
    /// </summary>
    private async Task LoadPromosAsync(bool reset)
    {
        if (_isLoadingPromos || !_itad.IsConfigured)
        {
            return;
        }

        _isLoadingPromos = true;

        if (reset)
        {
            _promos = new List<ItadDealListItem>();
            _promosNextOffset = 0;
            _promosHasMore = false;
        }

        RefreshLeftList();   // affiche « Chargement des promos… » et désactive les boutons

        try
        {
            string sort = (PromosSortMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            ItadDealsPage page = await _itad.GetDealsAsync(_promosNextOffset, PromosPageSize, sort);

            // Si la liste du site a bougé entre deux pages, un même jeu pourrait revenir :
            // HashSet.Add renvoie false quand l'identifiant y est déjà, et on ne l'ajoute pas une 2e fois.
            HashSet<string> knownIds = _promos.Select(item => item.Id).ToHashSet();

            foreach (ItadDealListItem item in page.List)
            {
                if (knownIds.Add(item.Id))
                {
                    _promos.Add(item);
                }
            }

            _promosNextOffset = page.NextOffset;
            _promosHasMore = page.HasMore;
            _promosLoaded = true;
            _promosLoadFailed = false;
        }
        catch (Exception ex)
        {
            _promosLoadFailed = true;
            Logger.Log($"Chargement des promos IsThereAnyDeal impossible : {ex.Message}");
        }
        finally
        {
            _isLoadingPromos = false;
            RefreshLeftList();
        }
    }

    private async void PromosSortMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectedIndex="0" déclenche cet événement pendant InitializeComponent : rien à charger alors.
        if (!_promosLoaded)
        {
            return;
        }

        await LoadPromosAsync(reset: true);
    }

    private async void LoadMorePromosButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadPromosAsync(reset: false);
    }

    private void PromosFilter_Changed(object sender, RoutedEventArgs e)
    {
        RefreshLeftList();
    }

    /// <summary>Une ligne de l'onglet Promos : titre, meilleur prix, badges.</summary>
    private SearchResultRow MakePromoRow(ItadDealListItem item)
    {
        ItadDeal deal = item.Deal;

        return new SearchResultRow
        {
            Id = item.Id,
            Title = item.Title,
            Type = item.Type ?? "",
            OwnedPlatforms = GetOwnedPlatforms(item.Title),
            IsWatched = _watchList.IsWatched(item.Id),
            PriceLine = $"{DealRow.FormatPrice(deal.Price)} chez {deal.Shop.Name}",
            IsOnSale = deal.Cut > 0,
            SaleText = $"-{deal.Cut} %",
            IsAtHistoricalLow = deal.HistoryLow != null && deal.Price.Amount <= deal.HistoryLow.Amount
        };
    }

    private async void FollowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGame == null)
        {
            return;
        }

        // On garde le jeu dans une variable : _currentGame peut changer pendant l'attente (await) plus bas.
        SearchResultRow game = _currentGame;
        bool wasWatched = _watchList.IsWatched(game.Id);

        try
        {
            if (wasWatched)
            {
                _watchList.Remove(game.Id);
            }
            else
            {
                _watchList.Add(game.Id, game.Title, game.Type);
            }
        }
        catch (Exception ex)
        {
            DealsStatusText.Text = "Impossible de modifier la liste de suivi (détail dans le journal).";
            Logger.Log($"Liste de suivi « {game.Title} » : {ex.Message}");
            return;
        }

        UpdateFollowButton();
        UpdateWatchListTab();
        RefreshLeftList();   // l'étoile « ★ Suivi » des lignes, et la liste des suivis si elle est affichée

        // Compte relié : on prévient aussi ta Waitlist. En cas d'échec, la synchro suivante rattrapera.
        if (_itadAccount.IsConnected)
        {
            bool isUpToDate = wasWatched
                ? await _watchList.PushRemoveAsync(_itad, _itadAccount, game.Id)
                : await _watchList.PushAddAsync(_itad, _itadAccount, game.Id);

            if (!isUpToDate)
            {
                ShopStatusText.Text = "Waitlist IsThereAnyDeal pas encore à jour : ce sera fait à la prochaine synchro.";
            }
        }
    }

    /// <summary>Le bouton dit ce qu'un clic va faire : suivre, ou arrêter de suivre.</summary>
    private void UpdateFollowButton()
    {
        if (_currentGame == null)
        {
            FollowButton.Visibility = Visibility.Collapsed;
            return;
        }

        FollowButton.Visibility = Visibility.Visible;
        FollowButton.Content = _watchList.IsWatched(_currentGame.Id) ? "★ Suivi — ne plus suivre" : "☆ Suivre";
    }

    private async void RefreshPricesButton_Click(object sender, RoutedEventArgs e)
    {
        // Le bouton ↻ met à jour ce que montre l'onglet : la liste des promos, ou les jeux suivis.
        if (_leftMode == LeftListMode.Promos)
        {
            await LoadPromosAsync(reset: true);
        }
        else
        {
            await RefreshWatchListAsync();
        }
    }

    /// <summary>
    /// Met la liste de suivi à jour : d'abord la synchro avec ta Waitlist (si le compte est relié),
    /// puis les prix de tous les jeux suivis. Appelé par MainWindow à chaque ouverture de Wyrmhold,
    /// et par le bouton ↻. Ne lance jamais deux mises à jour en même temps.
    /// </summary>
    public async Task RefreshWatchListAsync()
    {
        if (_isCheckingPrices || !_itad.IsConfigured)
        {
            return;
        }

        _isCheckingPrices = true;
        RefreshPricesButton.IsEnabled = false;
        RefreshLeftList();   // affiche « vérification des prix… »

        // La synchro a son propre try/catch : si elle échoue, on vérifie quand même les prix.
        if (_itadAccount.IsConnected)
        {
            try
            {
                await _watchList.SyncWithWaitlistAsync(_itad, _itadAccount);
                _lastSyncFailed = false;
            }
            catch (Exception ex)
            {
                _lastSyncFailed = true;
                Logger.Log($"Synchronisation de la Waitlist IsThereAnyDeal impossible : {ex.Message}");
            }
        }

        try
        {
            await _watchList.CheckPricesAsync(_itad, _hiddenShops);
            _lastCheckFailed = false;
        }
        catch (Exception ex)
        {
            // Pas de réseau, limite de requêtes atteinte… on garde les derniers prix connus.
            _lastCheckFailed = true;
            Logger.Log($"Vérification des prix des jeux suivis impossible : {ex.Message}");
        }
        finally
        {
            _isCheckingPrices = false;
            RefreshPricesButton.IsEnabled = true;
            UpdateWatchListTab();   // le nombre de jeux en promo a pu changer
            RefreshLeftList();
        }
    }

    // ===================== Compte IsThereAnyDeal (utilisé par l'onglet Comptes) =====================

    public bool IsItadConfigured => _itadAccount.IsConfigured;

    public bool IsItadConnected => _itadAccount.IsConnected;

    /// <summary>Termine la connexion du compte, puis synchronise tout de suite la liste de suivi.</summary>
    public async Task ConnectItadAsync(string loginCode, string codeVerifier)
    {
        await _itadAccount.ConnectAsync(loginCode, codeVerifier);
        await RefreshWatchListAsync();
    }

    /// <summary>Délie le compte. La liste de suivi reste dans Wyrmhold, mais n'est plus synchronisée.</summary>
    public void DisconnectItad()
    {
        _itadAccount.Disconnect();
        _lastSyncFailed = false;

        try
        {
            _watchList.ForgetWaitlistSync();
        }
        catch (Exception ex)
        {
            Logger.Log($"Remise à zéro de la synchro Waitlist impossible : {ex.Message}");
        }

        RefreshLeftList();
    }

    /// <summary>La fin du texte d'état de l'onglet Suivis : synchro et vérification des prix.</summary>
    private string GetPriceCheckText()
    {
        if (_isCheckingPrices)
        {
            return " · vérification des prix…";
        }

        // Le message de synchro passe devant : c'est l'information la plus importante si elle a échoué.
        if (_itadAccount.IsConnected && _lastSyncFailed)
        {
            return " · synchro Waitlist impossible (détail dans le journal)";
        }

        if (_lastCheckFailed)
        {
            return " · vérification impossible, derniers prix connus affichés";
        }

        long lastChecked = _watchList.Games.Count == 0 ? 0 : _watchList.Games.Max(game => game.PricesCheckedUnix);

        if (lastChecked == 0)
        {
            return "";
        }

        DateTime checkedAt = DateTimeOffset.FromUnixTimeSeconds(lastChecked).LocalDateTime;
        return $" · prix vérifiés le {checkedAt:dd/MM à HH:mm}";
    }

    /// <summary>L'onglet « Suivis » affiche le nombre de jeux suivis.</summary>
    private void UpdateWatchListTab()
    {
        // Un jeu est « en promo » si sa meilleure offre connue a une réduction.
        int onSale = _watchList.Games.Count(game => game.BestPrice != null && game.BestCut > 0);

        WatchListTabButton.Content = onSale > 0
            ? $"★ Suivis ({_watchList.Games.Count} · {onSale} en promo)"
            : $"★ Suivis ({_watchList.Games.Count})";
    }

    // ===================== Recherche =====================

    private async void ShopSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SearchAsync();
        }
    }

    private async void ShopSearchButton_Click(object sender, RoutedEventArgs e)
    {
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        string title = ShopSearchBox.Text.Trim();

        if (title.Length == 0 || !_itad.IsConfigured)
        {
            return;
        }

        ShopSearchButton.IsEnabled = false;
        ShopStatusText.Text = "Recherche…";

        try
        {
            _searchResults = await _itad.SearchAsync(title);
            _hasSearched = true;

            // Une nouvelle recherche ramène sur l'onglet « Résultats » (s'il l'était déjà, rien ne change).
            ResultsTabButton.IsChecked = true;
            RefreshLeftList();
        }
        catch (Exception ex)
        {
            ShopStatusText.Text = "La recherche a échoué (détail dans le journal).";
            Logger.Log($"Recherche IsThereAnyDeal « {title} » : {ex.Message}");
        }
        finally
        {
            // finally s'exécute dans tous les cas, erreur ou non : le bouton ne reste jamais bloqué.
            ShopSearchButton.IsEnabled = true;
        }
    }

    private void GamesOnlyCheck_Changed(object sender, RoutedEventArgs e)
    {
        RefreshLeftList();
    }

    /// <summary>
    /// Remplit la liste de gauche : les résultats de recherche, ou la liste de suivi.
    /// Chez IsThereAnyDeal, chaque édition (Standard, Deluxe, Gold…) est un jeu à part, de type « game » ;
    /// les extensions sont « dlc », et les bandes-son n'ont souvent pas de type du tout.
    /// « Jeux et éditions seulement » garde donc uniquement le type « game » (résultats seulement).
    /// </summary>
    private void RefreshLeftList()
    {
        // Appelé aussi pendant InitializeComponent (IsChecked="True" des onglets) :
        // les éléments placés plus bas dans le XAML peuvent ne pas exister encore.
        if (SearchResultsList == null || ShopStatusText == null)
        {
            return;
        }

        bool isPromos = _leftMode == LeftListMode.Promos;
        PromosOptionsPanel.Visibility = isPromos ? Visibility.Visible : Visibility.Collapsed;
        LoadMorePromosButton.Visibility = isPromos ? Visibility.Visible : Visibility.Collapsed;

        List<SearchResultRow> shown;

        if (_leftMode == LeftListMode.Promos)
        {
            bool hideOwned = HideOwnedCheck.IsChecked == true;
            bool gamesOnly = GamesOnlyCheck.IsChecked == true;

            // Les filtres s'appliquent aux promos déjà chargées : aucune requête en plus.
            shown = _promos
                .Where(item => !_hiddenShops.Contains(item.Deal.Shop.Name))
                .Where(item => !hideOwned || GetOwnedPlatforms(item.Title) == null)
                .Where(item => !gamesOnly || item.Type == "game")
                .Select(MakePromoRow)
                .ToList();

            int hiddenCount = _promos.Count - shown.Count;

            ShopStatusText.Text = _isLoadingPromos
                ? "Chargement des promos…"
                : _promosLoadFailed
                    ? "Impossible de charger les promos (détail dans le journal)."
                    : hiddenCount > 0
                        ? $"{shown.Count} promo(s), {hiddenCount} masquée(s) par les filtres"
                        : $"{shown.Count} promo(s)";

            LoadMorePromosButton.IsEnabled = _promosHasMore && !_isLoadingPromos;
            PromosSortMode.IsEnabled = !_isLoadingPromos;
        }
        else if (_leftMode == LeftListMode.WatchList)
        {
            shown = _watchList.Games
                .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(game => MakeRow(game.ItadId, game.Title, game.Type, game))
                .ToList();

            ShopStatusText.Text = shown.Count == 0
                ? "Aucun jeu suivi : choisis un jeu et clique sur « ☆ Suivre »."
                : $"{shown.Count} jeu(x) suivi(s){GetPriceCheckText()}";
        }
        else
        {
            bool gamesOnly = GamesOnlyCheck.IsChecked == true;

            shown = _searchResults
                .Where(result => !gamesOnly || result.Type == "game")
                .Select(result => MakeRow(result.Id, result.Title, result.Type))
                .ToList();

            // Avant toute recherche, on ne touche pas au texte d'état
            // (il peut contenir « Clé IsThereAnyDealApiKey absente… »).
            if (_hasSearched)
            {
                int hiddenCount = _searchResults.Count - shown.Count;
                ShopStatusText.Text = _searchResults.Count == 0
                    ? "Aucun jeu trouvé."
                    : hiddenCount > 0
                        ? $"{shown.Count} résultat(s), {hiddenCount} masqué(s)"
                        : $"{shown.Count} résultat(s)";
            }
        }

        // Remplacer la liste efface la sélection : on la remet sur le jeu affiché à droite (même Id),
        // sans relancer la demande de prix grâce à _isRebuildingResults.
        _isRebuildingResults = true;
        SearchResultsList.ItemsSource = shown;
        SearchResultsList.SelectedItem = shown.FirstOrDefault(row => row.Id == _currentGame?.Id);
        _isRebuildingResults = false;

        UpdateOwnedText();
    }

    /// <summary>
    /// Crée une ligne d'affichage, avec les infos « possédé » et « suivi » en plus.
    /// watched est donné seulement pour l'onglet Suivis : la ligne affiche alors son dernier prix connu.
    /// </summary>
    private SearchResultRow MakeRow(string itadId, string title, string? type, WatchedGame? watched = null)
    {
        return new SearchResultRow
        {
            Id = itadId,
            Title = title,
            Type = type ?? "",
            OwnedPlatforms = GetOwnedPlatforms(title),
            IsWatched = _watchList.IsWatched(itadId),
            PriceLine = watched == null ? "" : GetPriceLine(watched),

            // Badges : seulement dans l'onglet Suivis (watched != null) et si un prix est connu.
            IsOnSale = watched?.BestPrice != null && watched.BestCut > 0,
            SaleText = watched == null ? "" : $"En promo -{watched.BestCut} %",
            IsAtHistoricalLow = watched?.BestPrice != null
                && watched.HistoryLow != null
                && watched.BestPrice <= watched.HistoryLow
        };
    }

    /// <summary>« 48,99 € chez Steam · -30 % », ou pourquoi il n'y a pas de prix.</summary>
    private static string GetPriceLine(WatchedGame game)
    {
        if (game.PricesCheckedUnix == 0)
        {
            return "Prix pas encore vérifié (bouton ↻)";
        }

        if (game.BestPrice == null)
        {
            return "Aucune offre en ce moment";
        }

        string price = DealRow.FormatPrice(new ItadPrice { Amount = game.BestPrice.Value, Currency = game.Currency });

        // La réduction n'est plus écrite ici : le badge vert « En promo -30 % » l'affiche.
        return $"{price} chez {game.BestShop}";
    }

    // ===================== Offres d'un jeu =====================

    private async void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRebuildingResults || SearchResultsList.SelectedItem is not SearchResultRow game)
        {
            return;
        }

        int request = ++_pricesRequest;

        _currentGame = game;
        SelectedGameTitle.Text = game.Title;
        UpdateOwnedText();
        UpdateFollowButton();
        DealsStatusText.Text = "Chargement des prix…";
        HistoryLowText.Text = "";
        HistoryCompareText.Text = "";
        _deals = new List<DealRow>();
        DealsList.ItemsSource = null;

        // La présentation se charge en même temps que les prix (« _ = » : on lance sans attendre).
        PresentationPanel.Visibility = Visibility.Collapsed;
        _ = ShowPresentationAsync(game, request);

        try
        {
            List<ItadGamePrices> prices = await _itad.GetPricesAsync(new[] { game.Id });

            if (request != _pricesRequest)
            {
                return;   // un autre jeu a été choisi pendant le chargement
            }

            ItadGamePrices? gamePrices = prices.FirstOrDefault();
            ItadPrice? historyLow = gamePrices?.HistoryLow?.AllTime;

            _deals = gamePrices == null
                ? new List<DealRow>()
                : gamePrices.Deals.Select(deal => DealRow.FromDeal(deal, historyLow)).ToList();

            ShowHistoryLow(gamePrices?.HistoryLow);
            BuildShopFilter();
            ApplyDealsView();
        }
        catch (Exception ex)
        {
            if (request != _pricesRequest)
            {
                return;
            }

            DealsStatusText.Text = "Impossible de charger les prix (détail dans le journal).";
            Logger.Log($"Prix IsThereAnyDeal « {game.Title} » : {ex.Message}");
        }
    }

    /// <summary>
    /// Remplit le bloc de présentation au-dessus des prix. Si elle n'arrive pas, le bloc reste caché :
    /// les prix s'affichent quand même.
    /// </summary>
    private async Task ShowPresentationAsync(SearchResultRow game, int request)
    {
        try
        {
            GamePresentation? presentation = await _presentations.GetAsync(game.Id);

            // Un autre jeu a été choisi pendant le chargement : cette présentation n'est plus la bonne.
            if (request != _pricesRequest || presentation == null)
            {
                return;
            }

            WebImageLoader.SetUrl(PresentationCover, presentation.CoverUrl);
            PresentationCover.Visibility = presentation.CoverUrl == null ? Visibility.Collapsed : Visibility.Visible;

            SetTextOrHide(PresentationInfo, presentation.InfoLine);
            SetTextOrHide(PresentationTags, presentation.TagsLine);
            SetTextOrHide(PresentationReviews, presentation.ReviewLine);
            SetTextOrHide(PresentationDescription, presentation.Description ?? "");

            PresentationPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Logger.Log($"Présentation IsThereAnyDeal « {game.Title} » : {ex.Message}");
        }
    }

    /// <summary>Affiche le texte, ou cache la ligne s'il est vide (pas de trou dans le bloc).</summary>
    private static void SetTextOrHide(TextBlock textBlock, string text)
    {
        textBlock.Text = text;
        textBlock.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Affiche le plus bas historique (toujours, 1 an, 3 mois) et le compare au meilleur prix actuel.
    /// La comparaison porte sur TOUTES les offres, même celles masquées par les filtres.
    /// </summary>
    private void ShowHistoryLow(ItadHistoryLow? history)
    {
        ItadPrice? allTime = history?.AllTime;

        if (allTime == null)
        {
            HistoryLowText.Text = "Pas d'historique de prix connu pour ce jeu.";
            HistoryCompareText.Text = "";
            return;
        }

        // Les plus bas sur 1 an et 3 mois peuvent manquer : on n'affiche que ceux qui existent.
        var parts = new List<string> { $"Plus bas historique : {FormatLow(allTime)}" };

        if (history!.OneYear != null)
        {
            parts.Add($"sur 1 an : {FormatLow(history.OneYear)}");
        }

        if (history.ThreeMonths != null)
        {
            parts.Add($"sur 3 mois : {FormatLow(history.ThreeMonths)}");
        }

        HistoryLowText.Text = string.Join("  ·  ", parts);

        if (_deals.Count == 0)
        {
            HistoryCompareText.Text = "";
            return;
        }

        decimal bestNow = _deals.Min(deal => deal.PriceAmount);
        string bestNowText = DealRow.FormatPrice(new ItadPrice { Amount = bestNow, Currency = allTime.Currency });

        if (bestNow <= allTime.Amount)
        {
            HistoryCompareText.Text = $"Le meilleur prix actuel ({bestNowText}) est le plus bas jamais vu !";
            HistoryCompareText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessTextBrush");
        }
        else if (allTime.Amount == 0)
        {
            // Jeu déjà offert gratuitement (ex. sur l'Epic Game Store) : un pourcentage n'aurait pas de sens,
            // et diviser par zéro ferait planter le calcul.
            HistoryCompareText.Text = $"Meilleur prix actuel : {bestNowText}. Ce jeu a déjà été offert gratuitement par le passé.";
            HistoryCompareText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        }
        else
        {
            decimal percentAbove = (bestNow - allTime.Amount) / allTime.Amount * 100;
            HistoryCompareText.Text = $"Le meilleur prix actuel ({bestNowText}) est {percentAbove:0} % au-dessus du plus bas historique.";
            HistoryCompareText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        }
    }

    private static string FormatLow(ItadPrice price)
    {
        return price.Amount == 0 ? "gratuit" : DealRow.FormatPrice(price);
    }

    /// <summary>Une case à cocher par boutique présente dans les offres du jeu affiché.</summary>
    private void BuildShopFilter()
    {
        ShopFilterPanel.Children.Clear();

        IEnumerable<string> shopNames = _deals
            .Select(deal => deal.ShopName)
            .Distinct()
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase);

        foreach (string shopName in shopNames)
        {
            var check = new CheckBox
            {
                Content = shopName,
                IsChecked = !_hiddenShops.Contains(shopName),
                Margin = new Thickness(0, 2, 0, 2)
            };

            // On abonne les événements APRÈS avoir réglé IsChecked : sinon la création
            // de la case déclencherait déjà un « changement ».
            check.Checked += ShopCheck_Changed;
            check.Unchecked += ShopCheck_Changed;

            ShopFilterPanel.Children.Add(check);
        }

        UpdateShopFilterButton();
    }

    private void ShopCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox check || check.Content is not string shopName)
        {
            return;
        }

        if (check.IsChecked == true)
        {
            _hiddenShops.Remove(shopName);
        }
        else
        {
            _hiddenShops.Add(shopName);
        }

        SaveHiddenShops();
        UpdateShopFilterButton();
        ApplyDealsView();
        RefreshLeftList();   // l'onglet Promos masque aussi les promos de ces boutiques
    }

    /// <summary>Enregistre la liste des boutiques masquées dans settings.json.</summary>
    private void SaveHiddenShops()
    {
        if (_settings == null)
        {
            return;
        }

        _settings.HiddenShops = _hiddenShops.OrderBy(name => name).ToList();

        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            // Le filtre s'applique quand même : il ne sera juste pas retenu au prochain démarrage.
            Logger.Log($"Enregistrement des boutiques masquées impossible : {ex.Message}");
        }
    }

    /// <summary>Le bouton indique combien de boutiques de ce jeu sont masquées.</summary>
    private void UpdateShopFilterButton()
    {
        int hiddenHere = _deals.Select(deal => deal.ShopName).Distinct().Count(_hiddenShops.Contains);

        ShopFilterButton.Content = hiddenHere > 0
            ? $"Boutiques ({hiddenHere} masquée(s)) ▾"
            : "Boutiques ▾";
    }

    private void DealsFilter_Changed(object sender, RoutedEventArgs e)
    {
        ApplyDealsView();
    }

    private void DealsSortMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyDealsView();
    }

    /// <summary>
    /// Affiche les offres gardées dans _deals : d'abord les filtres (boutiques, promo, prix max),
    /// puis le tri choisi.
    /// </summary>
    private void ApplyDealsView()
    {
        // SelectedIndex="0" sur la liste de tri déclenche ce code pendant InitializeComponent,
        // avant que DealsList (placée plus bas dans le XAML) n'existe : on ne fait rien dans ce cas.
        if (DealsList == null)
        {
            return;
        }

        IEnumerable<DealRow> shown = _deals.Where(deal => !_hiddenShops.Contains(deal.ShopName));

        if (OnlyDealsCheck.IsChecked == true)
        {
            shown = shown.Where(deal => deal.HasCut);
        }

        // Case vide = pas de prix maximum. Texte illisible = on l'ignore aussi (pas d'erreur bloquante).
        if (TryParsePrice(MaxPriceBox.Text, out decimal maxPrice))
        {
            shown = shown.Where(deal => deal.PriceAmount <= maxPrice);
        }

        string mode = (DealsSortMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "price";

        // ThenBy départage les égalités : à réduction égale, le moins cher d'abord, etc.
        shown = mode switch
        {
            "cut" => shown.OrderByDescending(deal => deal.Cut).ThenBy(deal => deal.PriceAmount),
            "shop" => shown.OrderBy(deal => deal.ShopName, StringComparer.CurrentCultureIgnoreCase),
            _ => shown.OrderBy(deal => deal.PriceAmount).ThenBy(deal => deal.ShopName, StringComparer.CurrentCultureIgnoreCase)
        };

        List<DealRow> rows = shown.ToList();
        DealsList.ItemsSource = rows;

        if (SearchResultsList.SelectedItem == null)
        {
            return;   // aucun jeu choisi : on garde le texte d'état tel quel
        }

        int hiddenCount = _deals.Count - rows.Count;
        DealsStatusText.Text = _deals.Count == 0
            ? "Aucune offre en France pour ce jeu."
            : hiddenCount > 0
                ? $"{rows.Count} offre(s) affichée(s) sur {_deals.Count} — {hiddenCount} masquée(s) par les filtres"
                : $"{rows.Count} offre(s)";
    }

    /// <summary>
    /// Lit un prix tapé par l'utilisateur : « 20 », « 19,99 » ou « 19.99 ».
    /// On essaie d'abord la langue de Windows (virgule en français), puis le format international (point).
    /// </summary>
    private static bool TryParsePrice(string text, out decimal price)
    {
        text = text.Trim();

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out price)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out price);
    }

    // ===================== Liens =====================

    private void OpenDealButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string url)
        {
            BrowserHelper.Open(url);
        }
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        BrowserHelper.Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }
}

/// <summary>Une ligne de la liste des résultats de recherche.</summary>
public class SearchResultRow
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Type { get; init; } = "";

    // Plateformes où tu possèdes ce jeu, ou null si tu ne l'as pas.
    public string? OwnedPlatforms { get; init; }

    public bool IsOwned => OwnedPlatforms != null;

    // Vrai si le jeu est dans ta liste de suivi.
    public bool IsWatched { get; init; }

    // Dernier meilleur prix connu (onglet Suivis seulement ; vide ailleurs).
    public string PriceLine { get; init; } = "";
    public bool HasPriceLine => PriceLine.Length > 0;

    // Badges des jeux suivis.
    public bool IsOnSale { get; init; }
    public string SaleText { get; init; } = "";
    public bool IsAtHistoricalLow { get; init; }
    public string OwnedBadgeText => $"✓ Dans ta bibliothèque ({OwnedPlatforms})";
}

/// <summary>
/// Une ligne de la liste des offres, avec les textes déjà prêts à afficher.
/// Le XAML se contente de les lier ({Binding PriceText}…) : la mise en forme reste en C#.
/// </summary>
public class DealRow
{
    public string ShopName { get; init; } = "";
    public string PriceText { get; init; } = "";
    public string RegularPriceText { get; init; } = "";
    public string CutText { get; init; } = "";
    public bool HasCut { get; init; }
    public string Url { get; init; } = "";
    public bool IsOfficialShop { get; init; }

    // Les valeurs brutes, pour trier et filtrer (on ne trie pas des textes comme « 48,99 € »).
    public decimal PriceAmount { get; init; }
    public int Cut { get; init; }

    // Vrai si ce prix égale (ou bat) le plus bas jamais vu, toutes boutiques confondues.
    public bool IsHistoricalLow { get; init; }

    // Ces deux textes se calculent à partir de IsOfficialShop (propriétés « => » : pas de valeur stockée).
    public string TrustText => IsOfficialShop ? "Officiel" : "Revendeur agréé";
    public string TrustTooltip => IsOfficialShop
        ? "Boutique de la plateforme elle-même."
        : "Revendeur reconnu par IsThereAnyDeal : la clé vient de l'éditeur ou d'un distributeur officiel.";

    public static DealRow FromDeal(ItadDeal deal, ItadPrice? historyLow)
    {
        return new DealRow
        {
            ShopName = deal.Shop.Name,
            IsOfficialShop = ShopTrust.IsOfficial(deal.Shop.Name),
            PriceAmount = deal.Price.Amount,
            Cut = deal.Cut,
            IsHistoricalLow = historyLow != null && deal.Price.Amount <= historyLow.Amount,
            PriceText = FormatPrice(deal.Price),
            RegularPriceText = FormatPrice(deal.Regular),
            CutText = $"-{deal.Cut} %",
            HasCut = deal.Cut > 0,
            Url = deal.Url
        };
    }

    public static string FormatPrice(ItadPrice price)
    {
        // « 48,99 € » pour l'euro ; sinon le code de la devise (« 48,99 USD »).
        return price.Currency == "EUR"
            ? $"{price.Amount:0.00} €"
            : $"{price.Amount:0.00} {price.Currency}";
    }
}

/// <summary>Ce qu'affiche la liste de gauche de l'onglet Boutiques.</summary>
public enum LeftListMode
{
    Results,
    WatchList,
    Promos
}
