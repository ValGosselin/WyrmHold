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
    // Les clés partagées par toute l'appli : _itad relit la sienne à chaque appel (voir Secrets.Current).
    private readonly IsThereAnyDealApi _itad = new IsThereAnyDealApi(Secrets.Current);

    // Ton compte IsThereAnyDeal, s'il est relié (facultatif) : sert à synchroniser la Waitlist.
    private readonly ItadAccount _itadAccount = new ItadAccount(Secrets.Current);

    // Numéro de la dernière demande de prix (même principe que pour les patch notes) :
    // si on clique sur un autre jeu avant la réponse, l'ancienne réponse est ignorée.
    private int _pricesRequest;

    // La présentation des jeux (jaquette, genres, description), gardée en mémoire jeu par jeu.
    private readonly GamePresentationService _presentations;

    // Les captures et la bande-annonce du jeu affiché (null si le jeu n'est pas sur Steam).
    private SteamGameMedia? _currentMedia;
    private SteamTrailer? _currentTrailer;
    private string _currentPresentationTitle = "";

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

    // Onglets Promos et Pour toi : deux listes de promos chargées page par page (voir DealsFeed).
    private const int PromosPageSize = 50;
    private readonly DealsFeed _promos;
    private readonly DealsFeed _forYou;

    // Onglet Pour toi : la bibliothèque (pour calculer tes genres), le calcul, et les genres retenus.
    private List<Game> _libraryGames = new List<Game>();
    private readonly GenreRecommender _recommender = new GenreRecommender();
    private List<PreferredGenre> _preferredGenres = new List<PreferredGenre>();
    private bool _genresComputed;
    private bool _genresFailed;

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
        // Les listes de promos sont créées AVANT InitializeComponent : pendant celui-ci, les onglets
        // déclenchent déjà RefreshLeftList, qui les lit. Chaque liste sait comment charger une page :
        // le tri (et les genres) sont relus à chaque chargement.
        _promos = new DealsFeed(offset => _itad.GetDealsAsync(offset, PromosPageSize, GetSelectedTag(PromosSortMode)));
        _forYou = new DealsFeed(offset => GenreRecommender.GetDealsAsync(_itad,
            _preferredGenres.Select(genre => genre.Name),
            _settings?.RecommendationMinSteamPercent ?? 80,
            _settings?.RecommendationMinSteamReviews ?? 2000,
            offset, PromosPageSize, GetSelectedTag(ForYouSortMode)));

        InitializeComponent();
        _presentations = new GamePresentationService(_itad);

        UpdateKeyStatus();

        // Une clé ajoutée ou changée dans Réglages (ou l'assistant) : l'onglet se débloque sans redémarrer.
        // Dispatcher : l'événement peut venir d'un autre fil ; on revient sur celui de l'interface.
        Secrets.Changed += () => Dispatcher.Invoke(UpdateKeyStatus);

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

    // ===================== État gardé d'une fois sur l'autre =====================

    // La liste de gauche à rouvrir, en attendant que l'onglet Boutiques soit affiché
    // (Promos et Pour toi se chargent sur internet : inutile de le faire si tu ne viens pas ici).
    private string? _pendingLeftTab;

    /// <summary>Note l'état de l'onglet (listes, tris, cases) dans state, avant de quitter.</summary>
    public void CaptureState(UiState state)
    {
        state.ShopsLeftTab = _pendingLeftTab ?? _leftMode switch
        {
            LeftListMode.WatchList => "watchlist",
            LeftListMode.Promos => "promos",
            LeftListMode.ForYou => "foryou",
            _ => "results"
        };
        state.PromosSort = GetSelectedTag(PromosSortMode);
        state.ForYouSort = GetSelectedTag(ForYouSortMode);
        state.DealsSort = GetSelectedTag(DealsSortMode);
        state.GamesOnly = GamesOnlyCheck.IsChecked == true;
        state.HideOwned = HideOwnedCheck.IsChecked == true;
        state.OnlyDeals = OnlyDealsCheck.IsChecked == true;
    }

    /// <summary>Remet l'état enregistré. La liste de gauche attend que l'onglet soit affiché.</summary>
    public void RestoreState(UiState state)
    {
        // Les tris d'abord : les listes ne sont pas encore chargées, rien ne part sur internet.
        SelectByTag(PromosSortMode, state.PromosSort);
        SelectByTag(ForYouSortMode, state.ForYouSort);
        SelectByTag(DealsSortMode, state.DealsSort);
        GamesOnlyCheck.IsChecked = state.GamesOnly;
        HideOwnedCheck.IsChecked = state.HideOwned;
        OnlyDealsCheck.IsChecked = state.OnlyDeals;

        if (state.ShopsLeftTab != "results")
        {
            _pendingLeftTab = state.ShopsLeftTab;
            IsVisibleChanged += ApplyPendingLeftTab;
        }
    }

    private void ApplyPendingLeftTab(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible || _pendingLeftTab is null)
        {
            return;
        }

        IsVisibleChanged -= ApplyPendingLeftTab;
        string tab = _pendingLeftTab;
        _pendingLeftTab = null;

        // Cocher le bouton déclenche LeftTab_Checked, qui charge la liste si besoin.
        RadioButton button = tab switch
        {
            "watchlist" => WatchListTabButton,
            "promos" => PromosTabButton,
            "foryou" => ForYouTabButton,
            _ => ResultsTabButton
        };
        button.IsChecked = true;
    }

    // Sélectionne l'élément dont le Tag vaut tag (s'il n'existe pas, on ne change rien).
    private static void SelectByTag(ComboBox comboBox, string tag)
    {
        ComboBoxItem? match = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, tag));

        if (match is not null)
        {
            comboBox.SelectedItem = match;
        }
    }

    // Sans clé IsThereAnyDeal, la recherche est bloquée avec un message qui dit où l'ajouter.
    private void UpdateKeyStatus()
    {
        ShopSearchButton.IsEnabled = _itad.IsConfigured;

        if (!_itad.IsConfigured)
        {
            ShopStatusText.Text = "Pour chercher des prix, ajoute ta clé IsThereAnyDeal dans Réglages → Clés API.";
        }
        else if (!_hasSearched)
        {
            ShopStatusText.Text = "";
        }
    }

    // ===================== Bibliothèque =====================

    /// <summary>
    /// Reçoit les jeux de la bibliothèque (appelé par MainWindow à chaque chargement ou actualisation)
    /// et met à jour les badges « Déjà dans ta bibliothèque ».
    /// </summary>
    public void SetLibrary(IEnumerable<Game> games)
    {
        _libraryGames = games.ToList();

        // La bibliothèque a changé (temps de jeu, nouveaux jeux) : les genres seront recalculés
        // à la prochaine ouverture de l'onglet Pour toi.
        _genresComputed = false;

        _libraryByName = _libraryGames
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
            : ForYouTabButton?.IsChecked == true ? LeftListMode.ForYou
            : LeftListMode.Results;

        RefreshLeftList();

        // Première ouverture de l'onglet Promos : on charge la liste (une seule requête).
        if (_leftMode == LeftListMode.Promos && !_promos.IsLoaded)
        {
            await LoadFeedAsync(_promos, reset: true);
        }

        // Onglet Pour toi : on calcule d'abord tes genres (si la bibliothèque a changé), puis on charge.
        if (_leftMode == LeftListMode.ForYou && (!_genresComputed || !_forYou.IsLoaded))
        {
            await LoadForYouAsync(recomputeGenres: !_genresComputed);
        }
    }

    // ===================== Meilleures promos et « Pour toi » =====================

    /// <summary>Le Tag de l'élément choisi dans une liste déroulante (ici, la valeur de tri).</summary>
    private static string GetSelectedTag(ComboBox? comboBox)
    {
        return (comboBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    }

    /// <summary>
    /// Charge une page de promos (voir DealsFeed.LoadAsync) en tenant la liste de gauche à jour :
    /// « Chargement… » pendant l'attente, puis les promos reçues.
    /// </summary>
    private async Task LoadFeedAsync(DealsFeed feed, bool reset)
    {
        if (feed.IsLoading || !_itad.IsConfigured)
        {
            return;
        }

        Task loading = feed.LoadAsync(reset);   // passe IsLoading à true tout de suite
        RefreshLeftList();                      // affiche « Chargement… » et désactive les boutons
        await loading;
        RefreshLeftList();
    }

    /// <summary>
    /// Onglet Pour toi : (re)calcule tes genres préférés si besoin, affiche leurs cases, puis charge les promos.
    /// </summary>
    private async Task LoadForYouAsync(bool recomputeGenres)
    {
        if (_forYou.IsLoading)
        {
            return;
        }

        if (recomputeGenres)
        {
            ShopStatusText.Text = "Calcul de tes genres préférés…";

            try
            {
                _preferredGenres = await _recommender.GetPreferredGenresAsync(
                    _libraryGames, _settings?.ExcludedRecommendationGenres ?? new List<string>());
                _genresComputed = true;
                _genresFailed = false;
            }
            catch (Exception ex)
            {
                // Sans la liste des tags de Steam, impossible de traduire les genres pour IsThereAnyDeal.
                _genresFailed = true;
                Logger.Log($"Calcul des genres préférés impossible : {ex.Message}");
            }

            BuildGenreCheckBoxes();
        }

        if (_genresFailed || _preferredGenres.Count == 0)
        {
            RefreshLeftList();   // affiche pourquoi il n'y a pas de recommandations
            return;
        }

        await LoadFeedAsync(_forYou, reset: true);
    }

    /// <summary>Une case cochée par genre préféré : la décocher retire le genre (le suivant prend sa place).</summary>
    private void BuildGenreCheckBoxes()
    {
        ForYouGenresPanel.Children.Clear();

        foreach (PreferredGenre genre in _preferredGenres)
        {
            var checkBox = new CheckBox
            {
                Content = genre.Label,
                IsChecked = true,
                Tag = genre.Name,
                Margin = new Thickness(0, 0, 12, 2),
                ToolTip = "Décocher pour retirer ce genre des recommandations"
            };

            checkBox.Unchecked += GenreCheckBox_Unchecked;
            ForYouGenresPanel.Children.Add(checkBox);
        }

        bool hasExcluded = _settings?.ExcludedRecommendationGenres.Count > 0;
        ResetGenresText.Visibility = hasExcluded ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void GenreCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string genreName } || _settings == null)
        {
            return;
        }

        _settings.ExcludedRecommendationGenres.Add(genreName);
        SaveSettings();
        await LoadForYouAsync(recomputeGenres: true);
    }

    private async void ResetGenresLink_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null)
        {
            return;
        }

        _settings.ExcludedRecommendationGenres.Clear();
        SaveSettings();
        await LoadForYouAsync(recomputeGenres: true);
    }

    private async void PromosSortMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectedIndex="0" déclenche cet événement pendant InitializeComponent : rien à charger alors.
        if (!_promos.IsLoaded)
        {
            return;
        }

        await LoadFeedAsync(_promos, reset: true);
    }

    private async void ForYouSortMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_forYou.IsLoaded)
        {
            return;
        }

        await LoadFeedAsync(_forYou, reset: true);
    }

    private async void LoadMorePromosButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadFeedAsync(_leftMode == LeftListMode.ForYou ? _forYou : _promos, reset: false);
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
        // Le bouton ↻ met à jour ce que montre l'onglet : la liste des promos, les recommandations
        // (genres recalculés compris), ou les jeux suivis.
        if (_leftMode == LeftListMode.Promos)
        {
            await LoadFeedAsync(_promos, reset: true);
        }
        else if (_leftMode == LeftListMode.ForYou)
        {
            await LoadForYouAsync(recomputeGenres: true);
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
        bool isForYou = _leftMode == LeftListMode.ForYou;
        PromosOptionsPanel.Visibility = isPromos ? Visibility.Visible : Visibility.Collapsed;
        ForYouOptionsPanel.Visibility = isForYou ? Visibility.Visible : Visibility.Collapsed;
        LoadMorePromosButton.Visibility = isPromos || isForYou ? Visibility.Visible : Visibility.Collapsed;

        List<SearchResultRow> shown;

        if (isPromos || isForYou)
        {
            DealsFeed feed = isForYou ? _forYou : _promos;

            // Pour toi : les jeux possédés sont toujours masqués (le but est de découvrir).
            bool hideOwned = isForYou || HideOwnedCheck.IsChecked == true;
            bool gamesOnly = GamesOnlyCheck.IsChecked == true;

            // Les filtres s'appliquent aux promos déjà chargées : aucune requête en plus.
            shown = feed.Items
                .Where(item => !_hiddenShops.Contains(item.Deal.Shop.Name))
                .Where(item => !hideOwned || GetOwnedPlatforms(item.Title) == null)
                .Where(item => !gamesOnly || item.Type == "game")
                .Select(MakePromoRow)
                .ToList();

            int hiddenCount = feed.Items.Count - shown.Count;
            string hiddenReason = isForYou ? "déjà possédée(s) ou masquée(s)" : "masquée(s) par les filtres";

            ShopStatusText.Text = isForYou && _genresFailed
                ? "Impossible de calculer tes genres (détail dans le journal)."
                : isForYou && _genresComputed && _preferredGenres.Count == 0
                    ? "Pas encore assez de temps de jeu sur des jeux avec des genres connus."
                    : feed.IsLoading
                        ? "Chargement des promos…"
                        : feed.LoadFailed
                            ? "Impossible de charger les promos (détail dans le journal)."
                            : hiddenCount > 0
                                ? $"{shown.Count} promo(s), {hiddenCount} {hiddenReason}"
                                : $"{shown.Count} promo(s)";

            LoadMorePromosButton.IsEnabled = feed.HasMore && !feed.IsLoading;
            PromosSortMode.IsEnabled = !_promos.IsLoading;
            ForYouSortMode.IsEnabled = !_forYou.IsLoading;
            ForYouGenresPanel.IsEnabled = !_forYou.IsLoading;
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
            // (il peut contenir « … ajoute ta clé IsThereAnyDeal dans Réglages… »).
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
        StopTrailer();
        RightScroll.ScrollToTop();   // nouveau jeu : on revient en haut de sa fiche
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
            ShowMedia(game.Title, presentation.SteamMedia);

            PresentationPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Logger.Log($"Présentation IsThereAnyDeal « {game.Title} » : {ex.Message}");
        }
    }

    // ===================== Captures et bande-annonce =====================

    /// <summary>Remplit la rangée d'images : la première bande-annonce, puis les captures.</summary>
    private void ShowMedia(string title, SteamGameMedia? media)
    {
        _currentMedia = media;
        _currentPresentationTitle = title;

        // On prend la première bande-annonce qui a une image (c'est celle que le studio met en avant).
        _currentTrailer = media?.Trailers.FirstOrDefault(trailer => trailer.ThumbnailUrl != null);

        bool hasScreenshots = media != null && media.Screenshots.Count > 0;

        if (media == null || (!hasScreenshots && _currentTrailer == null))
        {
            PresentationMedia.Visibility = Visibility.Collapsed;
            ScreenshotsList.ItemsSource = null;
            return;
        }

        TrailerBox.Visibility = _currentTrailer == null ? Visibility.Collapsed : Visibility.Visible;
        WebImageLoader.SetUrl(TrailerThumbnail, _currentTrailer?.ThumbnailUrl);
        TrailerBox.ToolTip = _currentTrailer == null
            ? null
            : _currentTrailer.MicrotrailerUrl == null
                ? $"{_currentTrailer.Name} : ouvrir sur Steam"
                : $"{_currentTrailer.Name} : cliquer pour lire un extrait (sans le son)";

        ScreenshotsList.ItemsSource = media.Screenshots;
        PresentationMedia.Visibility = Visibility.Visible;
    }

    private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMedia == null || sender is not Button { Tag: SteamScreenshot screenshot })
        {
            return;
        }

        List<string> fullUrls = _currentMedia.Screenshots.Select(s => s.FullUrl).ToList();

        var viewer = new ImageViewerWindow(_currentPresentationTitle, fullUrls, _currentMedia.Screenshots.IndexOf(screenshot))
        {
            Owner = Window.GetWindow(this)
        };

        viewer.Show();
    }

    // Vrai pendant le téléchargement de l'extrait : un second clic ne relance pas un second téléchargement.
    private bool _isLoadingTrailer;

    /// <summary>Clic sur la bande-annonce : lit l'extrait, ou l'arrête s'il est déjà en cours.</summary>
    private async void TrailerBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SteamTrailer? trailer = _currentTrailer;

        if (trailer == null || _isLoadingTrailer)
        {
            return;
        }

        // Pas d'extrait fourni par Steam : on ouvre directement la page du jeu.
        if (trailer.MicrotrailerUrl == null)
        {
            OpenSteamPage();
            return;
        }

        if (TrailerVideo.Visibility == Visibility.Visible)
        {
            StopTrailer();
            return;
        }

        _isLoadingTrailer = true;
        ShowTrailerStatus("Chargement de l'extrait…");

        try
        {
            // La vidéo (environ 3 Mo) n'est téléchargée qu'à ce moment-là, puis lue depuis le disque :
            // le lecteur de WPF refuse de la lire directement depuis internet.
            string localFile = await VideoCache.GetLocalFileAsync(trailer.MicrotrailerUrl);

            // Un autre jeu a été choisi pendant le téléchargement : on ne lit pas l'ancien extrait.
            if (trailer != _currentTrailer)
            {
                return;
            }

            TrailerStatus.Visibility = Visibility.Collapsed;
            TrailerVideo.Source = new Uri(localFile);
            TrailerVideo.Visibility = Visibility.Visible;
            TrailerPlayIcon.Visibility = Visibility.Collapsed;
            TrailerVideo.Play();
        }
        catch (Exception ex)
        {
            Logger.Log($"Extrait vidéo impossible à télécharger ({trailer.MicrotrailerUrl}) : {ex.Message}");
            ShowTrailerStatus("Extrait indisponible : utilise le lien vers Steam en dessous.");
        }
        finally
        {
            _isLoadingTrailer = false;
        }
    }

    private void ShowTrailerStatus(string text)
    {
        TrailerStatusText.Text = text;
        TrailerStatus.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// La molette au-dessus de la rangée d'images fait défiler toute la colonne (comme ailleurs),
    /// au lieu d'être « avalée » par la rangée, qui ne défile que de gauche à droite.
    /// </summary>
    private void MediaStrip_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        RightScroll.ScrollToVerticalOffset(RightScroll.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    // Fin de l'extrait : on le relance depuis le début, comme sur la boutique Steam.
    private void TrailerVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        TrailerVideo.Position = TimeSpan.Zero;
        TrailerVideo.Play();
    }

    private void TrailerVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        Logger.Log($"Extrait vidéo illisible ({_currentTrailer?.MicrotrailerUrl}) : {e.ErrorException?.Message}");
        StopTrailer();
        ShowTrailerStatus("Extrait illisible sur ce PC : utilise le lien vers Steam en dessous.");
    }

    /// <summary>Arrête l'extrait et remet l'image de la bande-annonce.</summary>
    private void StopTrailer()
    {
        TrailerVideo.Stop();
        TrailerVideo.Source = null;
        TrailerVideo.Visibility = Visibility.Collapsed;
        TrailerPlayIcon.Visibility = Visibility.Visible;
        TrailerStatus.Visibility = Visibility.Collapsed;
    }

    private void SteamPageLink_Click(object sender, RoutedEventArgs e)
    {
        OpenSteamPage();
    }

    private void OpenSteamPage()
    {
        if (_currentMedia != null)
        {
            BrowserHelper.Open(_currentMedia.StoreUrl);
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
        SaveSettings();
    }

    /// <summary>
    /// Enregistre settings.json. En cas d'échec, le choix s'applique quand même :
    /// il ne sera juste pas retenu au prochain démarrage.
    /// </summary>
    private void SaveSettings()
    {
        try
        {
            _settings?.Save();
        }
        catch (Exception ex)
        {
            Logger.Log($"Enregistrement des réglages impossible : {ex.Message}");
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
    Promos,
    ForYou
}
