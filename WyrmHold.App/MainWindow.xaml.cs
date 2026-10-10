using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;
using System.ComponentModel;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace WyrmHold.App;

public partial class MainWindow : Window
{
    private const string SteamStoreUrl = "https://store.steampowered.com/";

    private readonly LibraryService _library = new LibraryService();
    private readonly System.Windows.Forms.NotifyIcon _trayIcon = new System.Windows.Forms.NotifyIcon();
    private readonly OverlayController _overlay;

    private bool _isExiting;
    private bool _trayTipShown;
    private string _summary = "Wyrmhold";
    // Les tuiles affichées en ce moment (après filtres, regroupement et tri).
    private List<Game> _visibleGames = new List<Game>();
    private bool _isBuildingCopySelector;
    private bool _isLoadingSettings;
    private readonly HashSet<Platform> _selectedPlatforms = new HashSet<Platform>();
    private string _searchKey = "";
    private string _installFilter = "all";
    private string _originFilter = "all";
    private List<Game> _allGames = new List<Game>();
    private string _sortMode = "name";
    private string _activityFilter = "all";
    private string _tagFilter = "all";
    private bool _isBuildingTagFilter;
    private bool _favoritesOnly;
    private List<GameCollection> _collections = new List<GameCollection>();
    private long _collectionFilter;   // 0 = toutes les collections
    private bool _isBuildingCollectionFilter;
    private bool _isBuildingSavedViews;
    private string _achievementFilter = "all";

    // Les jeux du bandeau « nouveaux succès » (vidé quand on le ferme).
    private readonly List<AddedAchievements> _addedAchievements = new List<AddedAchievements>();

    // Numéro de la dernière demande de patch notes : une réponse plus ancienne est ignorée.
    private int _patchNotesRequest;
    private string _patchNotesLinkUrl = "";


    public MainWindow()
    {
        InitializeComponent();
        SetUpTrayIcon();

        // L'onglet Boutiques utilise le MÊME objet de réglages que le reste de la fenêtre :
        // avec deux copies, chacune écraserait les changements de l'autre en enregistrant.
        ShopsPanel.Settings = _library.Settings;

        // La même config que la dernière fois : taille de la fenêtre, puis l'onglet Boutiques.
        // (Bibliothèque et onglet ouvert : après le 1er chargement des jeux, voir RestoreLibraryState.)
        RestoreWindowPlacement();
        ShopsPanel.RestoreState(_library.Settings.Ui);

        _overlay = new OverlayController(_library);
        _playerCountsTimer.Tick += PlayerCountsTimer_Tick;

        // Une session de jeu vient d'être enregistrée : le temps de jeu affiché change.
        _library.PlaySessionRecorded += game => GamesList.Items.Refresh();

        Application.Current.SessionEnding += (sender, e) =>
        {
            SaveUiState();
            _isExiting = true;
            _overlay.Dispose();
            _trayIcon.Dispose();
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateThemeButton();
        LoadSettingsTab();
        ApplyOverlaySettings();
        VersionText.Text = $"Wyrmhold {AppInfo.Version}";
#if DEBUG
        TestCrashButton.Visibility = Visibility.Visible;
#endif

        // Plantage lors de la dernière utilisation : on propose de le signaler (rien n'est envoyé sans accord).
        OfferCrashReport();

        // Premier lancement : l'assistant des clés API, AVANT de lire la bibliothèque (pour qu'elle soit complète).
        OfferSetupWizard();
        UpdateApiKeysSummary();

        // Prix des jeux suivis : vérifiés à chaque ouverture, EN PARALLÈLE du chargement de la bibliothèque.
        // « _ = » : on lance la tâche sans l'attendre (elle gère elle-même ses erreurs).
        _ = ShopsPanel.RefreshWatchListAsync();
        BuildSavedViews(null);
        RefreshAccountsTab();
        await LoadGamesAsync();
        RefreshAccountsTab();

        // (Les mises à jour sont cherchées AVANT l'ouverture de cette fenêtre, voir UpdateWindow.)
    }

    // ===================== Bibliothèque =====================

    private async Task LoadGamesAsync()
    {
        RefreshButton.IsEnabled = false;
        Title = "Wyrmhold — chargement…";

        List<Game> games = await _library.ScanAllAsync();
        ShowGames(games);
        RestoreLibraryState();

        // Les joueurs en jeu, dès que les tuiles sont là (au démarrage et à chaque « Actualiser ») : ils ne dépendent
        // ni des jaquettes ni des succès, dont la lecture peut prendre plusieurs minutes.
        // « _ = » : en parallèle du reste du chargement, sans l'attendre (environ 20 s pour 1 300 jeux).
        _ = RefreshPlayerCountsAsync();

        // Chaque synchronisation est sautée si son lanceur est désactivé dans Réglages.
        if (_library.IsLauncherEnabled(Platform.Steam) && _library.IsSteamFamilyConnected)
        {
            Title = $"{_summary} — synchronisation de la famille Steam…";

            if (await SyncSteamFamilySilentlyAsync())
            {
                games = _library.LoadGames();
                ShowGames(games);
            }
        }

        if (_library.IsLauncherEnabled(Platform.Epic) && _library.IsEpicConnected)
        {
            Title = $"{_summary} — synchronisation d'Epic Games…";

            if (await SyncEpicSilentlyAsync())
            {
                games = _library.LoadGames();
                ShowGames(games);
            }
        }

        if (_library.IsLauncherEnabled(Platform.Ubisoft) && _library.IsUbisoftConnected)
        {
            Title = $"{_summary} — synchronisation d'Ubisoft Connect…";

            if (await SyncUbisoftSilentlyAsync())
            {
                games = _library.LoadGames();
                ShowGames(games);
            }
        }
        if (_library.IsLauncherEnabled(Platform.Ea) && _library.IsEaConnected)
        {
            Title = $"{_summary} — synchronisation d'EA…";

            if (await SyncEaSilentlyAsync())
            {
                games = _library.LoadGames();
                ShowGames(games);
            }
        }
        if (_library.IsLauncherEnabled(Platform.BattleNet) && _library.IsBattleNetConnected)
        {
            Title = $"{_summary} — synchronisation de Battle.net…";

            if (await SyncBattleNetSilentlyAsync())
            {
                games = _library.LoadGames();
                ShowGames(games);
            }
        }

        await CompleteGamesAsync(games);
        RefreshButton.IsEnabled = true;
    }

    // ----- Joueurs en jeu (Steam) -----

    // Toutes les 10 minutes, seulement quand la fenêtre est affichée (rien en arrière-plan ni réduite).
    private readonly System.Windows.Threading.DispatcherTimer _playerCountsTimer =
        new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };

    private bool _isRefreshingPlayerCounts;

    private async Task RefreshPlayerCountsAsync()
    {
        if (_isRefreshingPlayerCounts)
        {
            return;
        }

        _isRefreshingPlayerCounts = true;

        try
        {
            await _library.RefreshPlayerCountsAsync(_allGames);

            // La bibliothèque a pu être rechargée pendant ce temps (nouveaux objets) : on leur donne aussi les chiffres.
            _library.ApplyKnownPlayerCounts(_allGames);
        }
        catch (Exception ex)
        {
            Logger.Log($"Joueurs en jeu impossibles à mettre à jour : {ex.Message}");
        }
        finally
        {
            _isRefreshingPlayerCounts = false;
        }

        // Le minuteur repart de zéro : la prochaine mise à jour sera dans 10 minutes.
        _playerCountsTimer.Stop();
        _playerCountsTimer.Start();
    }

    private async void PlayerCountsTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        await RefreshPlayerCountsAsync();
    }
    private async Task<bool> SyncBattleNetSilentlyAsync()
    {
        CoreWebView2Controller? controller = null;

        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder(Platform.BattleNet));

            controller = await environment.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(this).Handle);
            controller.IsVisible = false;

            BattleNetFetchResult result = await BattleNetSession.FetchGamesAsync(
                controller.CoreWebView2, TimeSpan.FromSeconds(30));

            switch (result.Status)
            {
                case BattleNetFetchStatus.NotLoggedIn:
                    Logger.Log($"Session Battle.net expirée (page atteinte : {ReadPagePath(controller.CoreWebView2.Source)}) : reconnecte-toi dans l'onglet Comptes.");
                    _library.ForgetBattleNetSession();
                    return false;

                case BattleNetFetchStatus.NoResponse:
                    Logger.Log("Synchronisation de Battle.net : liste des jeux non reçue.");
                    return false;
            }

            _library.ImportBattleNetLibrary(result.Json!);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Synchronisation de Battle.net impossible : {ex.Message}");
            return false;
        }
        finally
        {
            controller?.Close();
        }
    }

    private void ShowGames(List<Game> games)
    {
        _allGames = games;
        AssignCopies();

        // L'onglet Boutiques en a besoin pour le badge « Déjà dans ta bibliothèque ».
        ShopsPanel.SetLibrary(games);

        // Les derniers joueurs en jeu connus, tout de suite (les jeux rechargés sont de nouveaux objets).
        _library.ApplyKnownPlayerCounts(games);

        // 1. D'abord les filtres : ils peuvent remettre à zéro un choix devenu impossible
        //    (une collection supprimée, un genre ou une plateforme qui n'a plus de jeux).
        BuildPlatformFilters(games);
        BuildTagFilter(games);
        _collections = _library.LoadCollections();
        BuildCollectionFilter();

        // 2. Ensuite la vue, qui filtre avec des choix à jour.
        RebuildGamesView();
        BuildGameCollectionsPanel();

        int installedCount = games.Count(g => g.IsInstalled);
        _summary = $"Wyrmhold — {games.Count} jeu(x), dont {installedCount} installé(s)";
        Title = _summary;

        UpdateResultCount();
    }
    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged « remonte » depuis les listes et ComboBox contenues dans les onglets :
        // on ne réagit que si c'est bien le TabControl lui-même qui a changé d'onglet.
        if (e.Source != MainTabs || !StatsTab.IsSelected)
        {
            return;
        }

        StatsTab.DataContext = _library.ComputeStatistics(_allGames);
    }

    /// <summary>
    /// Fait connaître à chaque jeu ses autres copies (même nom, autres plateformes).
    /// Sans regroupement, chaque jeu n'a que lui-même comme copie.
    /// </summary>
    private void AssignCopies()
    {
        if (!_library.Settings.MergeDuplicates)
        {
            foreach (Game game in _allGames)
            {
                game.Copies = new List<Game> { game };
            }

            return;
        }

        foreach (IGrouping<string, Game> group in _allGames.GroupBy(GetGroupKey))
        {
            List<Game> copies = group.OrderBy(g => g.Platform).ToList();

            foreach (Game game in copies)
            {
                game.Copies = copies;
            }
        }
    }

    /// <summary>
    /// La clé de regroupement. Un nom sans aucune lettre ni chiffre donnerait une clé vide :
    /// dans ce cas, le jeu reste seul (clé unique plateforme + identifiant).
    /// </summary>
    private static string GetGroupKey(Game game)
    {
        return game.GroupKey.Length > 0 ? game.GroupKey : $"{game.Platform}|{game.PlatformGameId}";
    }

    /// <summary>
    /// Filtre, regroupe, trie. On filtre chaque copie AVANT de regrouper : avec le filtre « Epic »,
    /// un jeu possédé sur Steam et Epic reste affiché, et c'est sa copie Epic qui sert de tuile.
    /// </summary>
    private void RebuildGamesView()
    {
        IEnumerable<Game> matching = _allGames.Where(MatchesFilters);

        List<Game> shown = _library.Settings.MergeDuplicates
            ? matching.GroupBy(GetGroupKey).Select(group => PickBestCopy(group)).ToList()
            : matching.ToList();

        _visibleGames = SortGames(shown);
        GamesList.ItemsSource = _visibleGames;
    }

    /// <summary>
    /// La copie qui représente le jeu sur sa tuile : installée d'abord, puis la plus jouée, puis la plus récente.
    /// </summary>
    private static Game PickBestCopy(IEnumerable<Game> copies)
    {
        return copies
            .OrderByDescending(g => g.IsInstalled)
            .ThenByDescending(g => g.PlaytimeMinutes)
            .ThenByDescending(g => g.LastPlayedUnix)
            .First();
    }
    private List<Game> SortGames(List<Game> games)
    {
        IOrderedEnumerable<Game> sorted = _sortMode switch
        {
            "lastPlayed" => games.OrderByDescending(g => g.LastPlayedUnix),
            "playtime" => games.OrderByDescending(g => g.PlaytimeMinutes),
            "release" => games.OrderByDescending(g => g.ReleaseDateUnix),
            "added" => games.OrderByDescending(g => g.AddedUnix),
            "size" => games.OrderByDescending(g => g.IsInstalled ? g.SizeOnDiskBytes : 0),
            // Les jeux sans succès passent après ceux à 0 %.
            "achievements" => games.OrderByDescending(g => g.HasAchievements ? g.AchievementsRatio : -1),
            _ => games.OrderBy(g => NameTools.Normalize(g.Name))
        };

        // À égalité (deux jeux jamais lancés, par exemple), on range par nom.
        return sorted.ThenBy(g => NameTools.Normalize(g.Name)).ToList();
    }

    private void SortMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _sortMode = ReadSelectedTag(SortMode);
        RebuildGamesViewKeepingSelection();
    }

    private void RebuildGamesViewKeepingSelection(bool scrollToSelection = true)
    {
        Game? selectedTile = GamesList.SelectedItem as Game;
        Game? shownCopy = CurrentGame;

        RebuildGamesView();

        if (selectedTile is null)
        {
            return;
        }

        // On retrouve le même jeu dans la nouvelle liste : la même copie, ou une autre copie du même jeu
        // (après un changement de filtre, ce n'est peut-être plus la même copie qui sert de tuile).
        Game? tile = _visibleGames.FirstOrDefault(g => g == selectedTile)
            ?? _visibleGames.FirstOrDefault(g => g.CopiesOrSelf.Contains(selectedTile));

        if (tile is null)
        {
            return;   // le jeu ne correspond plus aux filtres
        }

        GamesList.SelectedItem = tile;

        // Si tu avais choisi une autre copie dans la fiche, on la remet.
        if (shownCopy is not null && shownCopy != tile && tile.CopiesOrSelf.Contains(shownCopy))
        {
            ShowDetails(tile, shownCopy);
        }

        if (scrollToSelection)
        {
            GamesList.ScrollIntoView(tile);
        }
    }
    // ===================== Recherche et filtres =====================

    private bool MatchesFilters(Game game)
    {
        if (_selectedPlatforms.Count > 0 && !_selectedPlatforms.Contains(game.Platform))
        {
            return false;
        }
        if (_installFilter == "installed" && !game.IsInstalled
            || _installFilter == "notInstalled" && game.IsInstalled)
        {
            return false;
        }

        if (_originFilter == "mine" && game.IsFamilyShared == true
            || _originFilter == "family" && game.IsFamilyShared != true)
        {
            return false;
        }
        if (!MatchesActivity(game) || !MatchesAchievements(game))
        {
            return false;
        }
        if (_tagFilter != "all" && !game.TagList.Contains(_tagFilter))
        {
            return false;
        }
        if (_favoritesOnly && !game.IsFavorite)
        {
            return false;
        }
        if (_collectionFilter != 0 && !game.CollectionIds.Contains(_collectionFilter))
        {
            return false;
        }

        return _searchKey.Length == 0 || NameTools.Normalize(game.Name).Contains(_searchKey);
    }
    private bool MatchesActivity(Game game)
    {
        const long OneDay = 24 * 60 * 60;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return _activityFilter switch
        {
            "neverPlayed" => game.PlaytimeMinutes == 0 && game.LastPlayedUnix == 0,
            "underOneHour" => game.PlaytimeMinutes > 0 && game.PlaytimeMinutes < 60,
            "overTenHours" => game.PlaytimeMinutes >= 10 * 60,
            "last7Days" => game.LastPlayedUnix >= now - 7 * OneDay,
            "last30Days" => game.LastPlayedUnix >= now - 30 * OneDay,
            "recentlyUpdated" => game.IsRecentlyUpdated,
            _ => true
        };
    }

    private bool MatchesAchievements(Game game)
    {
        return _achievementFilter switch
        {
            "complete" => game.IsAchievementsComplete,
            "inProgress" => game.HasAchievements && !game.IsAchievementsComplete,
            "newAchievements" => game.HasAddedAchievements,
            "withAchievements" => game.HasAchievements,
            "withoutAchievements" => !game.HasAchievements,
            _ => true
        };
    }

    private void BuildPlatformFilters(List<Game> games)
    {
        List<IGrouping<Platform, Game>> platforms = games
            .GroupBy(g => g.Platform)
            .OrderBy(group => group.First().PlatformName)
            .ToList();

        // Une plateforme qui n'a plus aucun jeu ne doit pas rester cochée en cachant tout.
        _selectedPlatforms.IntersectWith(platforms.Select(group => group.Key));

        PlatformFilters.Children.Clear();

        foreach (IGrouping<Platform, Game> group in platforms)
        {
            ToggleButton button = new ToggleButton
            {
                Content = $"{group.First().PlatformName} ({group.Count()})",
                Tag = group.Key,
                IsChecked = _selectedPlatforms.Contains(group.Key),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 0)
            };

            button.Checked += PlatformFilter_Changed;
            button.Unchecked += PlatformFilter_Changed;
            PlatformFilters.Children.Add(button);
        }
    }
    private void BuildTagFilter(List<Game> games)
    {
        _isBuildingTagFilter = true;

        TagFilter.Items.Clear();
        TagFilter.Items.Add(new ComboBoxItem { Content = "Tous les genres", Tag = "all" });

        ComboBoxItem? selectedItem = null;

        IEnumerable<IGrouping<string, string>> tags = games
            .SelectMany(g => g.TagList)
            .GroupBy(tag => tag)
            .OrderBy(group => group.Key, StringComparer.CurrentCulture);

        foreach (IGrouping<string, string> group in tags)
        {
            ComboBoxItem item = new ComboBoxItem
            {
                Content = $"{group.Key} ({group.Count()})",
                Tag = group.Key
            };

            TagFilter.Items.Add(item);

            if (group.Key == _tagFilter)
            {
                selectedItem = item;
            }
        }

        // Si le genre choisi n'existe plus dans la bibliothèque, on revient à « Tous les genres ».
        if (selectedItem is null)
        {
            _tagFilter = "all";
        }

        TagFilter.SelectedItem = selectedItem ?? TagFilter.Items[0];
        _isBuildingTagFilter = false;
    }

    private void PlatformFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: Platform platform } button)
        {
            return;
        }

        if (button.IsChecked == true)
        {
            _selectedPlatforms.Add(platform);
        }
        else
        {
            _selectedPlatforms.Remove(platform);
        }

        RefreshFilters();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchKey = NameTools.Normalize(SearchBox.Text);
        RefreshFilters();
    }
    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Pendant la création de la fenêtre, cet événement arrive avant que tous les contrôles existent.
        if (!IsLoaded || _isBuildingTagFilter)
        {
            return;
        }

        _installFilter = ReadSelectedTag(InstallFilter);
        _originFilter = ReadSelectedTag(OriginFilter);
        _activityFilter = ReadSelectedTag(ActivityFilter);
        _tagFilter = ReadSelectedTag(TagFilter);
        _achievementFilter = ReadSelectedTag(AchievementFilter);
        RefreshFilters();
    }

    private static string ReadSelectedTag(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClearFilters();
        }
    }

    private void ClearFilters()
    {
        _selectedPlatforms.Clear();

        foreach (ToggleButton button in PlatformFilters.Children.OfType<ToggleButton>())
        {
            button.IsChecked = false;
        }
        InstallFilter.SelectedIndex = 0;
        OriginFilter.SelectedIndex = 0;
        ActivityFilter.SelectedIndex = 0;
        TagFilter.SelectedIndex = 0;
        AchievementFilter.SelectedIndex = 0;
        FavoritesFilter.IsChecked = false;
        CollectionFilter.SelectedIndex = 0;
        SavedViewsList.SelectedIndex = 0;
        SearchBox.Clear();
    }
    private void FavoritesFilter_Changed(object sender, RoutedEventArgs e)
    {
        _favoritesOnly = FavoritesFilter.IsChecked == true;
        RefreshFilters();
    }
    // ===================== Collections =====================

    private void BuildCollectionFilter()
    {
        _isBuildingCollectionFilter = true;

        CollectionFilter.Items.Clear();
        CollectionFilter.Items.Add(new ComboBoxItem { Content = "Toutes les collections", Tag = 0L });

        ComboBoxItem? selectedItem = null;

        foreach (GameCollection collection in _collections)
        {
            int count = _allGames.Count(g => g.CollectionIds.Contains(collection.Id));

            ComboBoxItem item = new ComboBoxItem
            {
                Content = $"{collection.Name} ({count})",
                Tag = collection.Id
            };

            CollectionFilter.Items.Add(item);

            if (collection.Id == _collectionFilter)
            {
                selectedItem = item;
            }
        }

        // La collection choisie a peut-être été supprimée entre-temps.
        if (selectedItem is null)
        {
            _collectionFilter = 0;
        }

        CollectionFilter.SelectedItem = selectedItem ?? CollectionFilter.Items[0];
        _isBuildingCollectionFilter = false;
    }

    private void CollectionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isBuildingCollectionFilter)
        {
            return;
        }

        _collectionFilter = (CollectionFilter.SelectedItem as ComboBoxItem)?.Tag is long id ? id : 0;
        RefreshFilters();
    }

    private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Game? tile = GamesList.SelectedItem as Game;
        ShowDetails(tile, tile);
    }

    // ===================== Fiche et copies =====================

    // Le jeu affiché dans la fiche : la tuile sélectionnée, ou la copie choisie dans « Plateforme ».
    // C'est lui qu'on lance, qu'on met en favori, dont on montre les succès et les patch notes.
    private Game? CurrentGame => DetailPanel.DataContext as Game;

    private void ShowDetails(Game? tile, Game? copy)
    {
        DetailPanel.DataContext = copy;

        // La liste « Plateforme » n'apparaît que pour un jeu possédé sur plusieurs plateformes.
        _isBuildingCopySelector = true;
        bool hasCopies = tile is not null && tile.HasOtherCopies;
        CopySelectorPanel.Visibility = hasCopies ? Visibility.Visible : Visibility.Collapsed;
        CopySelector.ItemsSource = hasCopies ? tile!.Copies : null;
        CopySelector.SelectedItem = hasCopies ? copy : null;
        _isBuildingCopySelector = false;

        BuildGameCollectionsPanel();
        _ = LoadPatchNotesAsync(copy);
    }

    private void CopySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isBuildingCopySelector || CopySelector.SelectedItem is not Game copy || copy == CurrentGame)
        {
            return;
        }

        ShowDetails(GamesList.SelectedItem as Game, copy);
    }

    // ===================== Patch notes =====================

    private async Task LoadPatchNotesAsync(Game? game)
    {
        // Chaque sélection reçoit un numéro. Si une autre sélection arrive pendant qu'on attend,
        // son numéro est plus grand et on abandonne celle-ci.
        int request = ++_patchNotesRequest;

        PatchNotesList.ItemsSource = null;
        PatchNotesLinkButton.Visibility = Visibility.Collapsed;

        if (game is null)
        {
            PatchNotesStatusText.Text = "";
            return;
        }

        PatchNotesStatusText.Text = "Chargement…";

        // Petite pause : si tu descends la liste avec les flèches, on n'appelle Steam
        // que pour le jeu sur lequel tu t'arrêtes.
        await Task.Delay(400);

        if (request != _patchNotesRequest)
        {
            return;
        }

        PatchNotesResult result;

        try
        {
            result = await _library.GetPatchNotesAsync(game);
        }
        catch (Exception ex)
        {
            Logger.Log($"Patch notes indisponibles pour {game.Name} : {ex.Message}");

            if (request == _patchNotesRequest)
            {
                PatchNotesStatusText.Text = "Patch notes indisponibles pour le moment.";
            }

            return;
        }

        if (request != _patchNotesRequest)
        {
            return;
        }

        PatchNotesStatusText.Text = result.Message;
        PatchNotesList.ItemsSource = result.Notes;

        _patchNotesLinkUrl = result.LinkUrl;
        PatchNotesLinkButton.Content = result.LinkText;
        PatchNotesLinkButton.Visibility = result.LinkUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowAchievementsButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentGame is not Game game)
        {
            return;
        }

        AchievementsWindow window = new AchievementsWindow(_library, game) { Owner = this };
        window.ShowDialog();

        // La fenêtre a relu les succès : le tri et les filtres de succès peuvent avoir changé.
        if (_sortMode == "achievements" || _achievementFilter != "all")
        {
            RebuildGamesViewKeepingSelection();
            UpdateResultCount();
        }
    }

    private void PatchNotesLinkButton_Click(object sender, RoutedEventArgs e)
    {
        OpenInBrowser(_patchNotesLinkUrl);
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        OpenInBrowser(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Log($"Impossible d'ouvrir {url} : {ex.Message}");
        }
    }

    /// <summary>
    /// Dans la fiche du jeu : une case à cocher par collection.
    /// </summary>
    private void BuildGameCollectionsPanel()
    {
        GameCollectionsPanel.Children.Clear();

        if (CurrentGame is not Game game)
        {
            return;
        }

        if (_collections.Count == 0)
        {
            TextBlock emptyText = new TextBlock
            {
                Text = "Aucune collection pour l'instant : crée-en une avec le bouton « Gérer… ».",
                TextWrapping = TextWrapping.Wrap
            };

            // L'équivalent en C# de Foreground="{DynamicResource SecondaryTextBrush}" :
            // la couleur suivra le thème, même s'il change pendant que le texte est affiché.
            emptyText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            GameCollectionsPanel.Children.Add(emptyText);
            return;
        }

        foreach (GameCollection collection in _collections)
        {
            CheckBox checkBox = new CheckBox
            {
                Content = collection.Name,
                Tag = collection,
                IsChecked = game.CollectionIds.Contains(collection.Id),
                Margin = new Thickness(0, 0, 0, 4)
            };

            checkBox.Checked += GameCollectionCheckBox_Changed;
            checkBox.Unchecked += GameCollectionCheckBox_Changed;
            GameCollectionsPanel.Children.Add(checkBox);
        }
    }

    private void GameCollectionCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: GameCollection collection } checkBox
            || CurrentGame is not Game game)
        {
            return;
        }

        try
        {
            // Un jeu regroupé est « un seul jeu » pour toi : toutes ses copies entrent ou sortent ensemble.
            foreach (Game copy in game.CopiesOrSelf)
            {
                _library.SetGameInCollection(copy, collection, checkBox.IsChecked == true);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Collection impossible à modifier pour {game.Name} : {ex.Message}");
            MessageBox.Show("La collection n'a pas pu être modifiée. Les détails sont dans le journal.", "Wyrmhold");
            return;
        }

        // Met à jour le nombre de jeux affiché à côté de chaque collection.
        BuildCollectionFilter();

        // Si on filtre sur cette collection, un jeu qu'on en retire doit disparaître.
        if (_collectionFilter == collection.Id)
        {
            RefreshFilters();
        }
    }

    private void ManageCollectionsButton_Click(object sender, RoutedEventArgs e)
    {
        CollectionsWindow window = new CollectionsWindow(_library) { Owner = this };
        window.ShowDialog();

        // Une collection a pu être créée, renommée ou supprimée : on recharge tout.
        ShowGames(_library.LoadGames());
    }
    // ===================== Thème clair / sombre =====================

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        SetTheme(ThemeManager.Current.Id == "dark" ? "light" : "dark");
    }

    /// <summary>
    /// Change de thème depuis le bouton ou l'onglet Réglages, et garde les deux d'accord.
    /// </summary>
    private void SetTheme(string themeId)
    {
        ThemeManager.Apply(themeId);
        UpdateThemeButton();

        _isLoadingSettings = true;
        LightThemeRadio.IsChecked = ThemeManager.Current.Id != "dark";
        DarkThemeRadio.IsChecked = ThemeManager.Current.Id == "dark";
        _isLoadingSettings = false;

        _library.Settings.Theme = ThemeManager.Current.Id;
        SaveSettings();
    }

    // ===================== Réglages =====================

    private void LoadSettingsTab()
    {
        // Pendant qu'on coche les cases, leurs événements ne doivent rien enregistrer.
        _isLoadingSettings = true;

        LightThemeRadio.IsChecked = ThemeManager.Current.Id != "dark";
        DarkThemeRadio.IsChecked = ThemeManager.Current.Id == "dark";
        MergeDuplicatesCheck.IsChecked = _library.Settings.MergeDuplicates;

        foreach (CheckBox checkBox in new[] { SteamAchievementsCheck, GogAchievementsCheck, EpicAchievementsCheck })
        {
            checkBox.IsChecked = checkBox.Tag is string name
                && Enum.TryParse(name, out Platform platform)
                && _library.Settings.IsAchievementSourceEnabled(platform);
        }

        foreach (CheckBox checkBox in LauncherChecks.Children.OfType<CheckBox>())
        {
            checkBox.IsChecked = checkBox.Tag is string name
                && Enum.TryParse(name, out Platform platform)
                && _library.Settings.IsLauncherEnabled(platform);
        }

        string closeAction = _library.Settings.CloseAction;
        CloseBackgroundRadio.IsChecked = closeAction == CloseActions.Background;
        CloseQuitRadio.IsChecked = closeAction == CloseActions.Quit;
        CloseAskRadio.IsChecked = closeAction != CloseActions.Background && closeAction != CloseActions.Quit;

        OverlayEnabledCheck.IsChecked = _library.Settings.OverlayEnabled;
        OverlayHotkeyBox.Text = _library.Settings.OverlayHotkey;
        OverlayHotkeyBox.IsEnabled = _library.Settings.OverlayEnabled;
        MuteSteamAchievementsCheck.IsChecked = _library.Settings.MuteSteamAchievementNotifications;
        AchievementSoundCheck.IsChecked = _library.Settings.AchievementSoundEnabled;

        _isLoadingSettings = false;
    }

    private void MuteSteamAchievementsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _library.Settings.MuteSteamAchievementNotifications = MuteSteamAchievementsCheck.IsChecked == true;
        SaveSettings();
    }

    private void AchievementSoundCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _library.Settings.AchievementSoundEnabled = AchievementSoundCheck.IsChecked == true;
        SaveSettings();
    }

    private void TestToastButton_Click(object sender, RoutedEventArgs e)
    {
        _overlay.ShowTestToast();
    }

    private void TestCompletedToastButton_Click(object sender, RoutedEventArgs e)
    {
        _overlay.ShowTestCompletedToast();
    }

    // ----- Overlay en jeu -----

    /// <summary>
    /// Applique les réglages de l'overlay et dit dans Réglages si le raccourci marche.
    /// </summary>
    private void ApplyOverlaySettings()
    {
        _overlay.ApplySettings();

        OverlayHotkeyBox.Text = _overlay.CurrentHotkey;

        OverlayHotkeyStatusText.Text = !_library.Settings.OverlayEnabled
            ? "Overlay désactivé : aucune surveillance, aucun raccourci."
            : _overlay.IsHotkeyAccepted
                ? "Pour changer le raccourci : clique dans la case, puis appuie sur la nouvelle combinaison (avec Ctrl, Alt ou Windows). Échap pour annuler."
                : $"Raccourci déjà utilisé par une autre application : {_overlay.CurrentHotkey}. Choisis-en un autre.";
    }

    private void OverlayEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _library.Settings.OverlayEnabled = OverlayEnabledCheck.IsChecked == true;
        SaveSettings();

        OverlayHotkeyBox.IsEnabled = _library.Settings.OverlayEnabled;
        ApplyOverlaySettings();
    }

    // La case du raccourci de l'overlay.
    private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Sinon Windows intercepterait l'ancien raccourci avant qu'il n'arrive dans la case.
        _overlay.SuspendHotkey();
        OverlayHotkeyStatusText.Text = "Appuie sur la nouvelle combinaison… (Échap pour annuler)";
    }

    private void HotkeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ApplyOverlaySettings();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Aucune touche n'est écrite dans la case : on lit seulement la combinaison.
        e.Handled = true;

        // Avec Alt, WPF range la vraie touche dans SystemKey.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();   // rend l'ancien raccourci (voir LostKeyboardFocus)
            return;
        }

        // Ctrl, Maj, Alt ou Windows seuls : on attend la touche principale.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        if (!HotkeyText.TryFormat(Keyboard.Modifiers, key, out string text))
        {
            OverlayHotkeyStatusText.Text = "Combinaison refusée : ajoute Ctrl, Alt ou Windows à ta touche. (Échap pour annuler)";
            return;
        }

        _library.Settings.OverlayHotkey = text;
        SaveSettings();
        Keyboard.ClearFocus();   // enregistre le nouveau raccourci (voir LostKeyboardFocus)
    }

    private void CloseActionRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings || sender is not RadioButton { Tag: string action })
        {
            return;
        }

        _library.Settings.CloseAction = action;
        SaveSettings();
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        SetTheme(sender == DarkThemeRadio ? "dark" : "light");
    }

    private void MergeDuplicatesCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _library.Settings.MergeDuplicates = MergeDuplicatesCheck.IsChecked == true;
        SaveSettings();

        AssignCopies();
        RebuildGamesViewKeepingSelection();
        UpdateResultCount();
    }

    private void AchievementSourceCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings || sender is not CheckBox { Tag: string name } checkBox
            || !Enum.TryParse(name, out Platform platform))
        {
            return;
        }

        _library.Settings.SetAchievementSourceEnabled(platform, checkBox.IsChecked == true);
        SaveSettings();
    }

    private async void LauncherCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings || sender is not CheckBox { Tag: string name } checkBox
            || !Enum.TryParse(name, out Platform platform))
        {
            return;
        }

        bool isEnabled = checkBox.IsChecked == true;
        _library.Settings.SetLauncherEnabled(platform, isEnabled);
        SaveSettings();
        RefreshAccountsTab();

        // Réactivé : on relit ce lanceur (nouveaux jeux installés entre-temps…), sauf si une lecture est en cours.
        // Désactivé : ses jeux disparaissent tout de suite de l'affichage, sans rien relire.
        if (isEnabled && RefreshButton.IsEnabled)
        {
            await LoadGamesAsync();
        }
        else
        {
            ShowGames(_library.LoadGames());
        }
    }

    private void SaveSettings()
    {
        try
        {
            _library.SaveSettings();
        }
        catch (Exception ex)
        {
            // Le réglage s'applique quand même : il ne sera juste pas retenu au prochain démarrage.
            Logger.Log($"Réglages impossibles à enregistrer : {ex.Message}");
        }
    }

    /// <summary>
    /// Le bouton montre le thème vers lequel il bascule : une lune en clair, un soleil en sombre.
    /// </summary>
    private void UpdateThemeButton()
    {
        bool isDark = ThemeManager.Current.Id == "dark";
        ThemeButton.Content = isDark ? "☀" : "🌙";
        ThemeButton.ToolTip = isDark ? "Passer en mode clair" : "Passer en mode sombre";
    }

    private void RandomGameButton_Click(object sender, RoutedEventArgs e)
    {
        List<Game> shownGames = _visibleGames.ToList();

        if (shownGames.Count == 0)
        {
            MessageBox.Show("Aucun jeu ne correspond aux filtres actuels.", "Wyrmhold");
            return;
        }

        // Pour que relancer le tirage donne toujours un autre jeu.
        if (shownGames.Count > 1 && GamesList.SelectedItem is Game currentGame)
        {
            shownGames.Remove(currentGame);
        }

        Game pickedGame = shownGames[Random.Shared.Next(shownGames.Count)];

        GamesList.SelectedItem = pickedGame;
        GamesList.ScrollIntoView(pickedGame);
        GamesList.Focus();
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentGame is not Game game)
        {
            return;
        }

        try
        {
            // Même idée que pour les collections : toutes les copies deviennent favorites ensemble.
            bool isFavorite = !game.IsFavorite;

            foreach (Game copy in game.CopiesOrSelf)
            {
                _library.SetFavorite(copy, isFavorite);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Favori impossible à enregistrer pour {game.Name} : {ex.Message}");
            MessageBox.Show("Le favori n'a pas pu être enregistré. Les détails sont dans le journal.", "Wyrmhold");
            return;
        }

        // Si on n'affiche que les favoris, un jeu retiré des favoris doit disparaître de la liste.
        if (_favoritesOnly)
        {
            RefreshFilters();
        }
    }

    private void RefreshFilters()
    {
        // Sans défilement : en tapant une recherche, la liste ne doit pas sauter vers le jeu sélectionné.
        RebuildGamesViewKeepingSelection(scrollToSelection: false);
        UpdateResultCount();
    }

    private void UpdateResultCount()
    {
        ResultCountText.Text = $"{_visibleGames.Count} jeu(x) affiché(s)";
    }
    private async Task<bool> SyncEaSilentlyAsync()
    {
        CoreWebView2Controller? controller = null;

        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder(Platform.Ea));

            controller = await environment.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(this).Handle);
            controller.IsVisible = false;

            CoreWebView2 webView = controller.CoreWebView2;

            // Étape 1 : ouvrir la page du site, comme le ferait un navigateur.
            // Si la session du site a expiré, EA passe par sa page de connexion, qui te reconnecte
            // toute seule grâce au cookie « Se souvenir de moi », puis revient sur la page.
            TaskCompletionSource<bool> accountPageLoaded = new TaskCompletionSource<bool>();

            webView.NavigationCompleted += (sender, e) =>
            {
                if (e.IsSuccess && webView.Source.StartsWith(EaOrderHistory.PageUrl, StringComparison.OrdinalIgnoreCase))
                {
                    accountPageLoaded.TrySetResult(true);
                }
            };

            webView.Navigate(EaOrderHistory.PageUrl);

            Task finished = await Task.WhenAny(accountPageLoaded.Task, Task.Delay(TimeSpan.FromSeconds(30)));

            if (finished != accountPageLoaded.Task)
            {
                Logger.Log($"Session EA expirée (page atteinte : {ReadPagePath(webView.Source)}) : reconnecte-toi dans l'onglet Comptes.");
                _library.ForgetEaSession();
                return false;
            }

            // Étape 2 : la session est ouverte, on demande l'historique complet.
            EaOrderHistoryCapture capture = new EaOrderHistoryCapture(webView);
            webView.Navigate(EaOrderHistory.Url);

            string? json = await capture.WaitForJsonAsync(TimeSpan.FromSeconds(20));

            if (json is null)
            {
                Logger.Log($"Synchronisation d'EA : historique non reçu (page atteinte : {ReadPagePath(webView.Source)}).");
                return false;
            }

            _library.ImportEaLibrary(json);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Synchronisation d'EA impossible : {ex.Message}");
            return false;
        }
        finally
        {
            controller?.Close();
        }
    }

    /// <summary>
    /// Garde seulement le site et le chemin d'une adresse, sans les paramètres :
    /// ceux des pages de connexion peuvent contenir des codes qu'on ne veut pas dans le journal.
    /// </summary>
    private static string ReadPagePath(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? uri.GetLeftPart(UriPartial.Path)
            : "inconnue";
    }

    private async Task CompleteGamesAsync(List<Game> games)
    {
        Title = $"{_summary} — téléchargement des jaquettes…";

        try
        {
            await _library.DownloadCoversAsync(games);
            GamesList.Items.Refresh();
        }
        catch (Exception ex)
        {
            Logger.Log($"Téléchargement des jaquettes impossible : {ex.Message}");
        }

        Title = $"{_summary} — récupération des infos…";

        try
        {
            await _library.UpdateMetadataAsync(games);
        }
        catch (Exception ex)
        {
            Logger.Log($"Récupération des infos impossible : {ex.Message}");
        }

        Title = $"{_summary} — mesure de la taille des jeux…";

        try
        {
            await _library.UpdateMissingSizesAsync(games);
        }
        catch (Exception ex)
        {
            Logger.Log($"Mesure de la taille des jeux impossible : {ex.Message}");
        }

        Title = $"{_summary} — lecture des succès…";

        try
        {
            // Progress<T> renvoie chaque avancée sur le fil de l'interface : on peut y toucher au Title.
            Progress<(int Done, int Total)> progress = new Progress<(int Done, int Total)>(
                p => Title = $"{_summary} — succès {p.Done}/{p.Total}…");

            AchievementRefreshResult result = await _library.RefreshAchievementsAsync(games, progress);

            if (result.RefreshedCount > 0)
            {
                // Les vignettes se redessinent toutes seules ; il reste à refaire le tri et les filtres.
                RebuildGamesViewKeepingSelection();
                UpdateResultCount();
            }

            if (result.Added.Count > 0)
            {
                ShowAddedAchievements(result.Added);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Lecture des succès impossible : {ex.Message}");
        }

        Title = _summary;
    }

    /// <summary>
    /// Prévient que des mises à jour ont ajouté des succès : bandeau orange en haut de la bibliothèque
    /// (« Voir ces jeux » applique le filtre « Succès ajoutés »), et, si Wyrmhold est caché dans la barre
    /// des tâches, une bulle près de l'horloge. Une nouvelle lecture complète le bandeau au lieu de l'effacer.
    /// </summary>
    private void ShowAddedAchievements(List<AddedAchievements> added)
    {
        foreach (AddedAchievements item in added)
        {
            _addedAchievements.RemoveAll(known => known.Game == item.Game);
            _addedAchievements.Add(item);
        }

        // Les 3 jeux qui en ont reçu le plus, puis « et N autres ».
        List<AddedAchievements> sorted = _addedAchievements.OrderByDescending(item => item.Count).ToList();
        string names = string.Join(", ", sorted.Take(3).Select(item => $"{item.Game.Name} (+{item.Count})"));
        string more = sorted.Count > 3 ? $" et {sorted.Count - 3} autre(s)" : "";
        string text = sorted.Count == 1
            ? $"🏆 Nouveaux succès ajoutés par une mise à jour : {names}"
            : $"🏆 {sorted.Count} jeux ont reçu de nouveaux succès : {names}{more}";

        AddedAchievementsText.Text = text;
        AddedAchievementsBanner.Visibility = Visibility.Visible;

        if (!IsVisible)
        {
            _trayIcon.ShowBalloonTip(5000, "Wyrmhold", text, System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void AddedAchievementsShow_Click(object sender, RoutedEventArgs e)
    {
        // Choisir « Succès ajoutés » dans la liste déclenche Filter_SelectionChanged, qui refait l'affichage.
        SelectByTag(AchievementFilter, "newAchievements");
        CloseAddedAchievementsBanner();
    }

    private void AddedAchievementsClose_Click(object sender, RoutedEventArgs e)
    {
        CloseAddedAchievementsBanner();
    }

    private void CloseAddedAchievementsBanner()
    {
        AddedAchievementsBanner.Visibility = Visibility.Collapsed;
        _addedAchievements.Clear();
    }

    private async Task<bool> SyncSteamFamilySilentlyAsync()
    {
        CoreWebView2Controller? controller = null;

        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder(Platform.Steam));

            controller = await environment.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(this).Handle);
            controller.IsVisible = false;

            TaskCompletionSource<bool> pageLoaded = new TaskCompletionSource<bool>();
            controller.CoreWebView2.NavigationCompleted += (sender, e) => pageLoaded.TrySetResult(e.IsSuccess);
            controller.CoreWebView2.Navigate(SteamStoreUrl);

            Task finished = await Task.WhenAny(pageLoaded.Task, Task.Delay(TimeSpan.FromSeconds(20)));

            if (finished != pageLoaded.Task)
            {
                Logger.Log("Synchronisation de la famille Steam : Steam ne répond pas.");
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));

            List<CoreWebView2Cookie> cookies = await controller.CoreWebView2.CookieManager.GetCookiesAsync(SteamStoreUrl);
            CoreWebView2Cookie? loginCookie = cookies.FirstOrDefault(c => c.Name == "steamLoginSecure");

            if (!SteamWebApi.TryParseLoginCookie(loginCookie?.Value, out string steamId, out string token)
                || steamId != _library.SteamId)
            {
                Logger.Log("Session de la famille Steam expirée : reconnecte-toi dans l'onglet Comptes.");
                _library.ForgetSteamFamilySession();
                return false;
            }

            await _library.ImportFamilyLibraryAsync(token);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Synchronisation de la famille Steam impossible : {ex.Message}");
            return false;
        }
        finally
        {
            controller?.Close();
        }
    }

    private async Task<bool> SyncEpicSilentlyAsync()
    {
        CoreWebView2Controller? controller = null;

        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder(Platform.Epic));

            controller = await environment.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(this).Handle);
            controller.IsVisible = false;

            TaskCompletionSource<string?> codeReceived = new TaskCompletionSource<string?>();

            controller.CoreWebView2.WebResourceResponseReceived += async (sender, e) =>
            {
                if (e.Response.StatusCode != 200
                    || !e.Request.Uri.StartsWith(EpicApi.AuthorizationCodeUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                codeReceived.TrySetResult(await ReadAuthorizationCodeAsync(e.Response));
            };

            controller.CoreWebView2.Navigate(EpicApi.AuthorizationCodeUrl);

            Task finished = await Task.WhenAny(codeReceived.Task, Task.Delay(TimeSpan.FromSeconds(20)));

            if (finished != codeReceived.Task)
            {
                Logger.Log("Synchronisation d'Epic Games : Epic ne répond pas.");
                return false;
            }

            string? authorizationCode = await codeReceived.Task;

            if (authorizationCode is null)
            {
                Logger.Log("Session Epic Games expirée : reconnecte-toi dans l'onglet Comptes.");
                _library.ForgetEpicSession();
                return false;
            }

            await _library.ImportEpicLibraryAsync(authorizationCode);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Synchronisation d'Epic Games impossible : {ex.Message}");
            return false;
        }
        finally
        {
            controller?.Close();
        }
    }

    public static async Task<string?> ReadAuthorizationCodeAsync(CoreWebView2WebResourceResponseView response)
    {
        string? json = await WebViewHelpers.ReadContentAsync(response);
        return json is null ? null : EpicApi.ReadAuthorizationCode(json);
    }

    private async Task<bool> SyncUbisoftSilentlyAsync()
    {
        CoreWebView2Controller? controller = null;

        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder(Platform.Ubisoft));

            controller = await environment.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(this).Handle);
            controller.IsVisible = false;

            UbisoftPageCapture pageCapture = new UbisoftPageCapture(controller.CoreWebView2);
            controller.CoreWebView2.Navigate(UbisoftPageCapture.GamesActivityUrl);

            UbisoftCapture? capture = await pageCapture.WaitForDataAsync(TimeSpan.FromSeconds(30));

            if (capture is null)
            {
                if (!controller.CoreWebView2.Source.StartsWith(UbisoftPageCapture.GamesActivityUrl, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log("Session Ubisoft expirée : reconnecte-toi dans l'onglet Comptes.");
                    _library.ForgetUbisoftSession();
                }
                else
                {
                    Logger.Log("Synchronisation d'Ubisoft Connect : Ubisoft ne répond pas.");
                }

                return false;
            }

            _library.ImportUbisoftLibrary(capture);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Synchronisation d'Ubisoft Connect impossible : {ex.Message}");
            return false;
        }
        finally
        {
            controller?.Close();
        }
    }

    // ===================== Lancement =====================

    // ===================== Menu clic droit des tuiles =====================

    /// <summary>
    /// Le jeu de la tuile cliquée (le menu reçoit la tuile comme DataContext), qui devient aussi la sélection :
    /// « Jouer » et « Favori » réutilisent ainsi les boutons de la fiche.
    /// </summary>
    private Game? SelectTileFromMenu(object sender)
    {
        if ((sender as FrameworkElement)?.DataContext is not Game tile)
        {
            return null;
        }

        GamesList.SelectedItem = tile;
        return tile;
    }

    private void TileMenuPlay_Click(object sender, RoutedEventArgs e)
    {
        if (SelectTileFromMenu(sender) is not null)
        {
            LaunchSelectedGame();
        }
    }

    private void TileMenuFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (SelectTileFromMenu(sender) is not null)
        {
            FavoriteButton_Click(sender, e);
        }
    }

    /// <summary>
    /// « Re-télécharger la jaquette » : pour une mauvaise image (ex. It's Fine qui affichait « It's A Wipe! »).
    /// </summary>
    private async void TileMenuRedownloadCover_Click(object sender, RoutedEventArgs e)
    {
        if (SelectTileFromMenu(sender) is not Game game)
        {
            return;
        }

        string? oldPath = game.CoverPath;
        bool found;

        try
        {
            found = await _library.RedownloadCoverAsync(game);
        }
        catch (Exception ex)
        {
            Logger.Log($"Jaquette impossible à re-télécharger pour {game.Name} : {ex.Message}");
            MessageBox.Show("La jaquette n'a pas pu être re-téléchargée. Les détails sont dans le journal.", "Wyrmhold");
            return;
        }

        // Même nom de fichier, autre image : on la retire de la mémoire, puis on redessine la tuile et la fiche.
        CoverLoader.Forget(oldPath);
        CoverLoader.Forget(game.CoverPath);
        GamesList.Items.Refresh();

        // La fiche : on lui retire puis on lui redonne son jeu, pour que sa jaquette soit relue.
        object shown = DetailPanel.DataContext;
        DetailPanel.DataContext = null;
        DetailPanel.DataContext = shown;

        Logger.Log(found
            ? $"Jaquette de {game.Name} re-téléchargée."
            : $"Jaquette de {game.Name} : aucune image trouvée (Steam, SteamGridDB).");

        if (!found)
        {
            MessageBox.Show($"Aucune jaquette trouvée pour {game.Name} (ni chez {game.PlatformName}, ni chez Steam, ni sur SteamGridDB). La tuile affiche son nom.", "Wyrmhold");
        }
    }

    private void TileMenuOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectTileFromMenu(sender) is not Game game)
        {
            return;
        }

        if (string.IsNullOrEmpty(game.InstallPath) || !Directory.Exists(game.InstallPath))
        {
            MessageBox.Show($"Le dossier de {game.Name} est introuvable. Clique sur Actualiser si le jeu a été déplacé ou désinstallé.", "Wyrmhold");
            return;
        }

        // Comme un double-clic sur le dossier : l'Explorateur de fichiers s'ouvre dedans.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(game.InstallPath) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>
    /// « Désinstaller… » : Wyrmhold demande confirmation, puis lance le désinstalleur officiel du launcher
    /// (il ne supprime jamais de fichier lui-même). Pour Epic, il ouvre son launcher et explique où cliquer.
    /// </summary>
    private void TileMenuUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (SelectTileFromMenu(sender) is not Game game)
        {
            return;
        }

        UninstallPlan? plan = GameUninstaller.FindPlan(game);

        if (plan is null)
        {
            MessageBox.Show(
                $"Wyrmhold n'a pas trouvé le désinstalleur de {game.Name}.\n\n"
                + $"Désinstalle-le depuis {game.PlatformName}, ou depuis Paramètres → Applications → Applications installées de Windows.",
                "Désinstaller", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string size = game.SizeText.Length > 0 ? $", {game.SizeText}" : "";

        string question = plan.Kind == UninstallKind.OpenLauncher
            ? $"{game.PlatformName} ne permet pas de désinstaller un jeu depuis une autre application.\n\n"
              + $"Wyrmhold va ouvrir {game.PlatformName} : dans ta Bibliothèque, clique sur « ⋯ » à côté de « {game.Name} », puis sur Désinstaller."
            : $"Désinstaller {game.Name} ({game.PlatformName}{size}) ?\n\n"
              + $"Wyrmhold lance le désinstalleur de {game.PlatformName}, qui peut te demander de confirmer à nouveau.";

        MessageBoxResult answer = MessageBox.Show(question, "Désinstaller",
            plan.Kind == UninstallKind.OpenLauncher ? MessageBoxButton.OKCancel : MessageBoxButton.YesNo,
            plan.Kind == UninstallKind.OpenLauncher ? MessageBoxImage.Information : MessageBoxImage.Warning,
            plan.Kind == UninstallKind.OpenLauncher ? MessageBoxResult.OK : MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes && answer != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            if (!GameUninstaller.Run(plan))
            {
                return;   // demande d'autorisation de Windows refusée : rien n'a été lancé
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Désinstalleur impossible à lancer pour {game.Name} ({plan.FileName}) : {ex.Message}");
            MessageBox.Show($"Le désinstalleur de {game.Name} n'a pas pu être lancé. Les détails sont dans le journal.", "Wyrmhold");
            return;
        }

        Logger.Log($"Désinstallation de {game.Name} ({game.PlatformName}) lancée : {plan.FileName} {plan.Arguments}");
        WatchUninstall(game);
    }

    // ----- Fin d'une désinstallation -----

    // Les jeux en cours de désinstallation, et jusqu'à quand on surveille leur dossier.
    private readonly Dictionary<string, (Game Game, DateTime Until)> _uninstalling = new Dictionary<string, (Game, DateTime)>();

    // Toutes les 10 secondes tant qu'un jeu est en cours de désinstallation.
    private readonly System.Windows.Threading.DispatcherTimer _uninstallTimer =
        new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };

    // Un désinstalleur laissé ouvert, ou annulé : on arrête de surveiller au bout de 15 minutes.
    private static readonly TimeSpan UninstallWatchDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Surveille le dossier du jeu : dès qu'il a disparu, la bibliothèque est relue (comme le début
    /// d'« Actualiser ») et la tuile passe en « non installé ».
    /// </summary>
    private void WatchUninstall(Game game)
    {
        if (string.IsNullOrEmpty(game.InstallPath))
        {
            return;
        }

        _uninstalling[GameWatcher.KeyOf(game)] = (game, DateTime.Now + UninstallWatchDuration);

        if (!_uninstallTimer.IsEnabled)
        {
            _uninstallTimer.Tick -= UninstallTimer_Tick;
            _uninstallTimer.Tick += UninstallTimer_Tick;
            _uninstallTimer.Start();
        }
    }

    private async void UninstallTimer_Tick(object? sender, EventArgs e)
    {
        bool anyFinished = false;

        foreach ((string key, (Game game, DateTime until)) in _uninstalling.ToList())
        {
            if (!Directory.Exists(game.InstallPath))
            {
                Logger.Log($"Désinstallation de {game.Name} terminée (dossier supprimé).");
                _uninstalling.Remove(key);
                anyFinished = true;
            }
            else if (DateTime.Now > until)
            {
                // Annulée, ou le désinstalleur a gardé le dossier (sauvegardes…) : « Actualiser » fera le point.
                _uninstalling.Remove(key);
            }
        }

        if (_uninstalling.Count == 0)
        {
            _uninstallTimer.Stop();
        }

        if (anyFinished)
        {
            try
            {
                ShowGames(await _library.ScanAllAsync());
            }
            catch (Exception ex)
            {
                Logger.Log($"Bibliothèque impossible à relire après une désinstallation : {ex.Message}");
            }
        }
    }

    private void LaunchSelectedGame()
    {
        if (CurrentGame is not Game game)
        {
            MessageBox.Show("Sélectionne d'abord un jeu dans la liste.", "Wyrmhold");
            return;
        }

        if (!game.IsInstalled && game.Platform != Platform.Steam)
        {
            MessageBox.Show(
                $"{game.Name} n'est pas installé. Installe-le depuis {game.PlatformName}, puis clique sur Actualiser.",
                "Wyrmhold");
            return;
        }

        if (!_library.Launch(game))
        {
            MessageBox.Show($"Impossible de lancer {game.Name}. Les détails sont dans le journal.", "Wyrmhold");
            return;
        }

        // Le détecteur de jeu repère sa session ; elle sera enregistrée à sa fermeture.
        _library.ExpectPlaySession(game);
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchSelectedGame();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadGamesAsync();
        RefreshAccountsTab();
    }

    private void GamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject clickedElement
            || ItemsControl.ContainerFromElement(GamesList, clickedElement) is not ListBoxItem)
        {
            return;
        }

        LaunchSelectedGame();
    }

    // ===================== Comptes =====================

    private void RefreshAccountsTab()
    {
        SteamStatusText.Text = _library.IsSteamApiConfigured
            ? "Bibliothèque synchronisée avec ta clé API"
            : "Clé API manquante : Réglages → Clés API";

        SetAccountRow(SteamFamilyStatusText, SteamFamilyButton, _library.IsSteamFamilyConnected);
        SetAccountRow(GogStatusText, GogAccountButton, _library.IsGogConnected);
        SetAccountRow(EpicStatusText, EpicAccountButton, _library.IsEpicConnected);
        SetAccountRow(UbisoftStatusText, UbisoftAccountButton, _library.IsUbisoftConnected);
        SetAccountRow(EaStatusText, EaAccountButton, _library.IsEaConnected, _library.HasImportedEaGames);
        SetAccountRow(BattleNetStatusText, BattleNetAccountButton, _library.IsBattleNetConnected, _library.HasImportedBattleNetGames);

        // Un lanceur désactivé dans Réglages : sa ligne est grisée (la connexion reste gardée pour plus tard).
        ApplyLauncherState(Platform.Steam, SteamStatusText, null);
        ApplyLauncherState(Platform.Steam, SteamFamilyStatusText, SteamFamilyButton);
        ApplyLauncherState(Platform.Gog, GogStatusText, GogAccountButton);
        ApplyLauncherState(Platform.Epic, EpicStatusText, EpicAccountButton);
        ApplyLauncherState(Platform.Ubisoft, UbisoftStatusText, UbisoftAccountButton);
        ApplyLauncherState(Platform.Ea, EaStatusText, EaAccountButton);
        ApplyLauncherState(Platform.BattleNet, BattleNetStatusText, BattleNetAccountButton);

        // IsThereAnyDeal : facultatif, ne sert qu'à synchroniser la liste de suivi avec ta Waitlist.
        if (ShopsPanel.IsItadConfigured)
        {
            SetAccountRow(ItadStatusText, ItadAccountButton, ShopsPanel.IsItadConnected);
            ItadStatusText.Text = ShopsPanel.IsItadConnected
                ? "Connecté : liste de suivi synchronisée avec ta Waitlist"
                : "Facultatif : synchronise ta liste de suivi avec ta Waitlist";
            ItadAccountButton.IsEnabled = true;
        }
        else
        {
            ItadStatusText.Text = "Client ID manquant : Réglages → Clés API (facultatif)";
            ItadAccountButton.Content = "Se connecter";
            ItadAccountButton.IsEnabled = false;
        }

    }
    private async void BattleNetAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (_library.IsBattleNetConnected)
        {
            if (!ConfirmDisconnect("Battle.net"))
            {
                return;
            }

            TryDisconnect(_library.DisconnectBattleNet);
        }
        else
        {
            BattleNetLoginWindow loginWindow = new BattleNetLoginWindow { Owner = this };

            if (loginWindow.ShowDialog() != true || loginWindow.GamesJson is null)
            {
                return;
            }

            try
            {
                int count = _library.ImportBattleNetLibrary(loginWindow.GamesJson);
                MessageBox.Show($"Battle.net connecté : {count} jeu(x) importé(s).", "Wyrmhold");
            }
            catch (Exception ex)
            {
                Logger.Log($"Connexion Battle.net impossible : {ex}");
                MessageBox.Show("La connexion à Battle.net a échoué. Les détails sont dans le journal.", "Wyrmhold");
            }
        }

        RefreshAccountsTab();
        await ReloadAfterAccountChangeAsync();
    }
    private async void EaAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (_library.IsEaConnected)
        {
            if (!ConfirmDisconnect("EA"))
            {
                return;
            }

            TryDisconnect(_library.DisconnectEa);
        }
        else
        {
            EaLoginWindow loginWindow = new EaLoginWindow { Owner = this };

            if (loginWindow.ShowDialog() != true || loginWindow.OrderHistoryJson is null)
            {
                return;
            }

            try
            {
                int count = _library.ImportEaLibrary(loginWindow.OrderHistoryJson);
                MessageBox.Show($"EA connecté : {count} jeu(x) importé(s).", "Wyrmhold");
            }
            catch (Exception ex)
            {
                Logger.Log($"Connexion EA impossible : {ex}");
                MessageBox.Show("La connexion à EA a échoué. Les détails sont dans le journal.", "Wyrmhold");
            }
        }

        RefreshAccountsTab();
        await ReloadAfterAccountChangeAsync();
    }

    private static void SetAccountRow(TextBlock statusText, Button button, bool isConnected, bool hasImportedGames = false)
    {
        if (isConnected)
        {
            statusText.Text = "Connecté";
            button.Content = "Se déconnecter";
        }
        else if (hasImportedGames)
        {
            statusText.Text = "Session expirée, tes jeux restent affichés";
            button.Content = "Se reconnecter";
        }
        else
        {
            statusText.Text = "Non connecté";
            button.Content = "Se connecter";
        }
    }

    private void ApplyLauncherState(Platform platform, TextBlock statusText, Button? button)
    {
        bool isEnabled = _library.IsLauncherEnabled(platform);

        if (!isEnabled)
        {
            statusText.Text = "Lanceur désactivé (Réglages → Lanceurs)";
        }

        if (button != null)
        {
            button.IsEnabled = isEnabled;
        }
    }

    private static bool ConfirmDisconnect(string accountName)
    {
        MessageBoxResult answer = MessageBox.Show(
            $"Se déconnecter de {accountName} ? Les jeux non installés de ce compte disparaîtront "
            + "de la bibliothèque jusqu'à ta prochaine connexion.",
            "Wyrmhold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return answer == MessageBoxResult.Yes;
    }

    private async void SteamFamilyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_library.IsSteamFamilyConnected)
        {
            if (!ConfirmDisconnect("la famille Steam"))
            {
                return;
            }

            TryDisconnect(_library.DisconnectSteamFamily);
        }
        else
        {
            SteamLoginWindow loginWindow = new SteamLoginWindow { Owner = this };

            if (loginWindow.ShowDialog() != true || loginWindow.AccessToken is null)
            {
                return;
            }

            if (loginWindow.SessionSteamId != _library.SteamId)
            {
                MessageBox.Show(
                    "Tu t'es connecté avec un autre compte Steam que celui configuré dans Wyrmhold. "
                    + "La session a été effacée : réessaie avec ton compte.",
                    "Wyrmhold");
                TryDisconnect(_library.DisconnectSteamFamily);
                RefreshAccountsTab();
                return;
            }

            SteamFamilyButton.IsEnabled = false;

            try
            {
                int count = await _library.ImportFamilyLibraryAsync(loginWindow.AccessToken);
                MessageBox.Show($"Famille Steam connectée : {count} jeu(x) importé(s).", "Wyrmhold");
            }
            catch (Exception ex)
            {
                Logger.Log($"Import de la famille Steam impossible : {ex.Message}");
                MessageBox.Show("L'import de la bibliothèque familiale a échoué. Les détails sont dans le journal.", "Wyrmhold");
            }
            finally
            {
                SteamFamilyButton.IsEnabled = true;
            }
        }

        RefreshAccountsTab();
        await ReloadAfterAccountChangeAsync();
    }

    private async void ItadAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (ShopsPanel.IsItadConnected)
        {
            // Message à part : contrairement aux lanceurs, aucun jeu ne disparaît de la bibliothèque.
            MessageBoxResult answer = MessageBox.Show(
                "Se déconnecter d'IsThereAnyDeal ? Ta liste de suivi reste dans Wyrmhold, "
                + "mais elle ne sera plus synchronisée avec ta Waitlist.",
                "Wyrmhold",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            ShopsPanel.DisconnectItad();
        }
        else
        {
            ItadLoginWindow loginWindow = new ItadLoginWindow { Owner = this };

            if (loginWindow.ShowDialog() != true || loginWindow.LoginCode is null)
            {
                return;
            }

            ItadAccountButton.IsEnabled = false;

            try
            {
                await ShopsPanel.ConnectItadAsync(loginWindow.LoginCode, loginWindow.CodeVerifier);
                MessageBox.Show(
                    "IsThereAnyDeal connecté. Ta liste de suivi (onglet Boutiques → Suivis) est maintenant "
                    + "synchronisée avec ta Waitlist.",
                    "Wyrmhold");
            }
            catch (Exception ex)
            {
                Logger.Log($"Connexion IsThereAnyDeal impossible : {ex.Message}");
                MessageBox.Show("La connexion à IsThereAnyDeal a échoué. Les détails sont dans le journal.", "Wyrmhold");
            }
            finally
            {
                ItadAccountButton.IsEnabled = true;
            }
        }

        RefreshAccountsTab();
    }

    private async void GogAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (_library.IsGogConnected)
        {
            if (!ConfirmDisconnect("GOG"))
            {
                return;
            }

            TryDisconnect(_library.DisconnectGog);
        }
        else
        {
            GogLoginWindow loginWindow = new GogLoginWindow { Owner = this };

            if (loginWindow.ShowDialog() != true || loginWindow.LoginCode is null)
            {
                return;
            }

            GogAccountButton.IsEnabled = false;

            try
            {
                int count = await _library.ConnectGogAsync(loginWindow.LoginCode);
                MessageBox.Show($"GOG connecté : {count} jeu(x) importé(s).", "Wyrmhold");
            }
            catch (Exception ex)
            {
                Logger.Log($"Connexion GOG impossible : {ex.Message}");
                MessageBox.Show("La connexion à GOG a échoué. Les détails sont dans le journal.", "Wyrmhold");
            }
            finally
            {
                GogAccountButton.IsEnabled = true;
            }
        }

        RefreshAccountsTab();
        await ReloadAfterAccountChangeAsync();
    }

    private async void EpicAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (_library.IsEpicConnected)
        {
            if (!ConfirmDisconnect("Epic Games"))
            {
                return;
            }

            TryDisconnect(_library.DisconnectEpic);
        }
        else
        {
            EpicLoginWindow loginWindow = new EpicLoginWindow { Owner = this };

            if (loginWindow.ShowDialog() != true || loginWindow.AuthorizationCode is null)
            {
                return;
            }

            EpicAccountButton.IsEnabled = false;
            Title = $"{_summary} — import de la bibliothèque Epic Games…";

            try
            {
                int count = await _library.ImportEpicLibraryAsync(loginWindow.AuthorizationCode);
                MessageBox.Show($"Epic Games connecté : {count} jeu(x) importé(s).", "Wyrmhold");
            }
            catch (Exception ex)
            {
                Logger.Log($"Connexion Epic Games impossible : {ex}");
                MessageBox.Show("La connexion à Epic Games a échoué. Les détails sont dans le journal.", "Wyrmhold");
            }
            finally
            {
                EpicAccountButton.IsEnabled = true;
                Title = _summary;
            }
        }

        RefreshAccountsTab();
        await ReloadAfterAccountChangeAsync();
    }

    private async void UbisoftAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (_library.IsUbisoftConnected)
        {
            if (!ConfirmDisconnect("Ubisoft Connect"))
            {
                return;
            }

            TryDisconnect(_library.DisconnectUbisoft);
        }
        else
        {
            UbisoftLoginWindow loginWindow = new UbisoftLoginWindow { Owner = this };

            if (loginWindow.ShowDialog() != true || loginWindow.Capture is null)
            {
                return;
            }

            try
            {
                int count = _library.ImportUbisoftLibrary(loginWindow.Capture);
                MessageBox.Show($"Ubisoft Connect connecté : {count} jeu(x) importé(s).", "Wyrmhold");
            }
            catch (Exception ex)
            {
                Logger.Log($"Connexion Ubisoft impossible : {ex}");
                MessageBox.Show("La connexion à Ubisoft Connect a échoué. Les détails sont dans le journal.", "Wyrmhold");
            }
        }

        RefreshAccountsTab();
        await ReloadAfterAccountChangeAsync();
    }

    private static void TryDisconnect(Action disconnect)
    {
        try
        {
            disconnect();
        }
        catch (Exception ex)
        {
            Logger.Log($"Déconnexion incomplète : {ex.Message}");
            MessageBox.Show(
                "La déconnexion n'a pas pu se terminer complètement. Réessaie dans quelques secondes.",
                "Wyrmhold");
        }
    }

    private async Task ReloadAfterAccountChangeAsync()
    {
        List<Game> games = _library.LoadGames();
        ShowGames(games);
        await CompleteGamesAsync(games);
    }

    // ===================== Zone de notification =====================

    private void SetUpTrayIcon()
    {
        string? exePath = Environment.ProcessPath;

        _trayIcon.Icon = (exePath is null ? null : System.Drawing.Icon.ExtractAssociatedIcon(exePath))
            ?? System.Drawing.SystemIcons.Application;
        _trayIcon.Text = "Wyrmhold";
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (sender, e) => ShowFromTray();

        System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir Wyrmhold", null, (sender, e) => ShowFromTray());
        menu.Items.Add("Quitter", null, (sender, e) => ExitApplication());
        _trayIcon.ContextMenuStrip = menu;
    }

    private void ShowFromTray()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        // Dans tous les cas, on annule d'abord la fermeture : c'est le choix ci-dessous qui décide.
        e.Cancel = true;

        string action = _library.Settings.CloseAction;

        if (action != CloseActions.Background && action != CloseActions.Quit)
        {
            var dialog = new CloseChoiceWindow { Owner = this };

            // Fenêtre fermée sans choisir : Wyrmhold reste ouvert, comme si de rien n'était.
            if (dialog.ShowDialog() != true || dialog.Choice == null)
            {
                return;
            }

            action = dialog.Choice;

            if (dialog.RememberChoice)
            {
                _library.Settings.CloseAction = action;
                SaveSettings();
                LoadSettingsTab();   // l'onglet Réglages montre le nouveau choix
            }
        }

        if (action == CloseActions.Quit)
        {
            // On quitte « juste après » : arrêter l'appli pendant qu'elle traite encore
            // la demande de fermeture de la fenêtre peut poser problème.
            Dispatcher.InvokeAsync(ExitApplication);
            return;
        }

        HideToTray();
    }

    private void HideToTray()
    {
        // Wyrmhold peut ensuite être arrêté avec Windows sans repasser par ici : on enregistre maintenant.
        SaveUiState();
        Hide();

        if (!_trayTipShown)
        {
            _trayIcon.ShowBalloonTip(
                3000,
                "Wyrmhold",
                "Wyrmhold continue en arrière-plan pour compter ton temps de jeu.",
                System.Windows.Forms.ToolTipIcon.Info);

            _trayTipShown = true;
        }
    }

    private void ExitApplication()
    {
        if (_library.HasActiveSessions)
        {
            MessageBoxResult answer = MessageBox.Show(
                "Une partie est en cours de suivi. Si tu quittes maintenant, elle ne sera pas comptée. Quitter quand même ?",
                "Wyrmhold",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        SaveUiState();
        _isExiting = true;
        _overlay.Dispose();
        _trayIcon.Dispose();
        Application.Current.Shutdown();
    }

    // ===================== Même config au prochain lancement =====================

    // La bibliothèque n'est remise qu'une fois (au 1er chargement), pas à chaque actualisation.
    private bool _isLibraryStateRestored;

    /// <summary>
    /// Note l'état de la fenêtre (taille, onglet, filtres, tri, jeu choisi, Boutiques) dans settings.json.
    /// </summary>
    private void SaveUiState()
    {
        try
        {
            UiState state = _library.Settings.Ui;

            // RestoreBounds = la taille « normale », même si la fenêtre est agrandie ou réduite.
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            {
                state.WindowLeft = bounds.Left;
                state.WindowTop = bounds.Top;
                state.WindowWidth = bounds.Width;
                state.WindowHeight = bounds.Height;
            }
            state.WindowMaximized = WindowState == WindowState.Maximized;

            state.MainTab = MainTabs.SelectedIndex;

            // Avant le 1er chargement des jeux, les filtres affichés ne sont pas encore les tiens : on garde les anciens.
            if (_isLibraryStateRestored)
            {
                SavedViewFilters filters = CaptureFilters();
                filters.SearchText = "";
                state.LibraryFilters = filters;
                state.SelectedGameKey = CurrentGame is Game game ? GameWatcher.KeyOf(game) : null;
            }

            ShopsPanel.CaptureState(state);
            SaveSettings();
        }
        catch (Exception ex)
        {
            // Ne jamais empêcher de quitter pour ça.
            Logger.Log($"État de la fenêtre impossible à enregistrer : {ex.Message}");
        }
    }

    /// <summary>
    /// Remet la taille et la position de la fenêtre, si elles sont encore visibles sur un des écrans
    /// (un écran débranché depuis ne doit pas faire ouvrir Wyrmhold hors de vue).
    /// </summary>
    private void RestoreWindowPlacement()
    {
        UiState state = _library.Settings.Ui;

        if (state.WindowLeft is double left && state.WindowTop is double top
            && state.WindowWidth is double width && state.WindowHeight is double height)
        {
            Rect saved = new Rect(left, top, Math.Max(width, MinWidth), Math.Max(height, MinHeight));
            Rect screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

            // Au moins 100 × 100 pixels de la fenêtre sur un écran.
            Rect visible = Rect.Intersect(saved, screens);
            if (!visible.IsEmpty && visible.Width >= 100 && visible.Height >= 100)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = saved.Left;
                Top = saved.Top;
                Width = saved.Width;
                Height = saved.Height;
            }
        }

        if (state.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// Après le 1er chargement des jeux (les listes de plateformes, genres et collections existent) :
    /// filtres, tri, jeu sélectionné et onglet ouvert de la dernière fois.
    /// </summary>
    private void RestoreLibraryState()
    {
        if (_isLibraryStateRestored)
        {
            return;
        }

        _isLibraryStateRestored = true;
        UiState state = _library.Settings.Ui;

        try
        {
            if (state.LibraryFilters is SavedViewFilters filters)
            {
                ApplyView(filters);
            }

            if (state.SelectedGameKey is string key)
            {
                Game? tile = _visibleGames.FirstOrDefault(g => g.CopiesOrSelf.Any(copy => GameWatcher.KeyOf(copy) == key));

                if (tile is not null)
                {
                    GamesList.SelectedItem = tile;
                    GamesList.ScrollIntoView(tile);
                }
            }

            if (state.MainTab > 0 && state.MainTab < MainTabs.Items.Count)
            {
                MainTabs.SelectedIndex = state.MainTab;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"État de la fenêtre impossible à remettre : {ex.Message}");
        }
    }

    /// <summary>
    /// Après un plantage (voir App.xaml.cs) : on quitte sans rien demander, en retirant quand même
    /// l'icône de la barre des tâches (sinon elle resterait affichée jusqu'au survol de la souris).
    /// </summary>
    public void ExitAfterCrash()
    {
        _isExiting = true;

        try
        {
            _overlay.Dispose();
            _trayIcon.Dispose();
        }
        catch
        {
            // L'appli est déjà dans un état bancal : on ferme quand même.
        }

        Application.Current.Shutdown();
    }

    // ===================== Clés API =====================

    private void OfferSetupWizard()
    {
        if (_library.Settings.SetupWizardDone)
        {
            return;
        }

        // Quelqu'un qui a déjà des clés (ex. importées de l'ancien secrets.json) n'a pas besoin de l'assistant.
        if (!Secrets.Current.HasAnyKey)
        {
            new SetupWizardWindow { Owner = this }.ShowDialog();
        }

        // Proposé une seule fois, qu'il soit terminé ou fermé : la suite se fait dans Réglages.
        _library.Settings.SetupWizardDone = true;
        _library.SaveSettings();
    }

    private async void EditApiKeysButton_Click(object sender, RoutedEventArgs e)
    {
        if (new SetupWizardWindow(fromSettings: true) { Owner = this }.ShowDialog() != true)
        {
            return;
        }

        UpdateApiKeysSummary();
        RefreshAccountsTab();

        // Les nouvelles clés servent tout de suite : on relit la bibliothèque (jeux Steam non installés,
        // jaquettes…). Si une lecture est déjà en cours (bouton Actualiser bloqué), elle s'en chargera.
        if (RefreshButton.IsEnabled)
        {
            await LoadGamesAsync();
            RefreshAccountsTab();
        }
    }

    private void UpdateApiKeysSummary()
    {
        Secrets keys = Secrets.Current;

        string Line(string name, string value) =>
            $"{name,-15} {(value.Length > 0 ? "✔ " + ApiKeyTester.Mask(value) : "— non renseignée")}";

        ApiKeysSummaryText.Text = string.Join("\n",
            Line("Steam", keys.SteamApiKey),
            Line("SteamID", keys.SteamId),
            Line("SteamGridDB", keys.SteamGridDbApiKey),
            Line("IsThereAnyDeal", keys.IsThereAnyDealApiKey),
            Line("ITAD client ID", keys.IsThereAnyDealClientId));
    }

    // ===================== Signaler un bug =====================

    private void ReportBugButton_Click(object sender, RoutedEventArgs e)
    {
        new BugReportWindow(_library.Settings) { Owner = this }.Show();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow(_library.Settings) { Owner = this }.ShowDialog();
    }

    // Version Debug seulement : une erreur volontaire, pour vérifier tout le parcours du plantage.
    private void TestCrashButton_Click(object sender, RoutedEventArgs e)
    {
        throw new InvalidOperationException(
            $"Plantage de test (fichier {System.IO.Path.Combine(AppPaths.DataFolder, "test.txt")}).");
    }

    private void OfferCrashReport()
    {
        string? crashFile = CrashReporter.GetPendingCrashFile();

        if (crashFile == null)
        {
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            "Wyrmhold s'est fermé de façon inattendue la dernière fois. Veux-tu signaler ce plantage ?\n\n"
            + "Tu verras le rapport avant de l'envoyer.",
            "Wyrmhold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        // La fenêtre lit le fichier dès sa création : on peut le supprimer juste après.
        if (answer == MessageBoxResult.Yes)
        {
            new BugReportWindow(_library.Settings, crashFile) { Owner = this }.Show();
        }

        CrashReporter.ClearPending();
    }

    // ===================== Vues enregistrées =====================

    private void BuildSavedViews(long? viewIdToSelect)
    {
        _isBuildingSavedViews = true;

        SavedViewsList.Items.Clear();
        SavedViewsList.Items.Add(new ComboBoxItem { Content = "Vues enregistrées…" });

        foreach (SavedView view in _library.LoadSavedViews())
        {
            ComboBoxItem item = new ComboBoxItem { Content = view.Name, Tag = view };
            SavedViewsList.Items.Add(item);

            if (view.Id == viewIdToSelect)
            {
                SavedViewsList.SelectedItem = item;
            }
        }

        if (SavedViewsList.SelectedItem is null)
        {
            SavedViewsList.SelectedIndex = 0;
        }

        _isBuildingSavedViews = false;
    }

    private void SavedViewsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isBuildingSavedViews
            || (SavedViewsList.SelectedItem as ComboBoxItem)?.Tag is not SavedView view)
        {
            return;
        }

        ApplyView(view.Filters);
    }

    /// <summary>
    /// Photographie l'état actuel des filtres et du tri.
    /// </summary>
    private SavedViewFilters CaptureFilters()
    {
        return new SavedViewFilters
        {
            SearchText = SearchBox.Text,
            Platforms = _selectedPlatforms.Select(p => p.ToString()).ToList(),
            InstallFilter = _installFilter,
            OriginFilter = _originFilter,
            ActivityFilter = _activityFilter,
            TagFilter = _tagFilter,
            AchievementFilter = _achievementFilter,
            CollectionId = _collectionFilter,
            FavoritesOnly = _favoritesOnly,
            SortMode = _sortMode
        };
    }

    /// <summary>
    /// Remet les contrôles dans l'état enregistré. Chaque contrôle déclenche son propre
    /// événement, qui met à jour le champ correspondant et rafraîchit la liste.
    /// </summary>
    private void ApplyView(SavedViewFilters filters)
    {
        SearchBox.Text = filters.SearchText;

        _selectedPlatforms.Clear();

        foreach (ToggleButton button in PlatformFilters.Children.OfType<ToggleButton>())
        {
            button.IsChecked = button.Tag is Platform platform
                && filters.Platforms.Contains(platform.ToString());
        }

        SelectByTag(InstallFilter, filters.InstallFilter);
        SelectByTag(OriginFilter, filters.OriginFilter);
        SelectByTag(ActivityFilter, filters.ActivityFilter);
        SelectByTag(TagFilter, filters.TagFilter);
        SelectByTag(AchievementFilter, filters.AchievementFilter);
        SelectByTag(CollectionFilter, filters.CollectionId);
        FavoritesFilter.IsChecked = filters.FavoritesOnly;
        SelectByTag(SortMode, filters.SortMode);
    }

    /// <summary>
    /// Sélectionne l'élément dont le Tag vaut « tag » ; s'il n'existe plus
    /// (un genre ou une collection disparus), revient au premier élément.
    /// </summary>
    private static void SelectByTag(ComboBox comboBox, object tag)
    {
        ComboBoxItem? match = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, tag));

        comboBox.SelectedItem = match ?? comboBox.Items[0];
    }

    private void SaveViewButton_Click(object sender, RoutedEventArgs e)
    {
        string currentName = (SavedViewsList.SelectedItem as ComboBoxItem)?.Tag is SavedView view ? view.Name : "";

        TextInputWindow dialog = new TextInputWindow(
            "Enregistrer la vue",
            "Nom de la vue (si elle existe déjà, ses réglages seront remplacés) :",
            currentName)
        { Owner = this };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _library.SaveView(dialog.Text, CaptureFilters());
        }
        catch (Exception ex)
        {
            Logger.Log($"Vue impossible à enregistrer : {ex.Message}");
            MessageBox.Show("La vue n'a pas pu être enregistrée. Les détails sont dans le journal.", "Wyrmhold");
            return;
        }

        long? savedId = _library.LoadSavedViews()
            .FirstOrDefault(v => string.Equals(v.Name, dialog.Text, StringComparison.OrdinalIgnoreCase))?.Id;

        BuildSavedViews(savedId);
    }

    private void DeleteViewButton_Click(object sender, RoutedEventArgs e)
    {
        if ((SavedViewsList.SelectedItem as ComboBoxItem)?.Tag is not SavedView view)
        {
            MessageBox.Show("Choisis d'abord la vue à supprimer dans la liste.", "Wyrmhold");
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            $"Supprimer la vue « {view.Name} » ? Tes filtres actuels ne changent pas.",
            "Wyrmhold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _library.DeleteSavedView(view);
        BuildSavedViews(null);
    }
}
