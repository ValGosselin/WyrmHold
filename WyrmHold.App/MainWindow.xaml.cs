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

    private bool _isExiting;
    private bool _trayTipShown;
    private string _summary = "Wyrmhold";
    private ICollectionView? _gamesView;
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


    public MainWindow()
    {
        InitializeComponent();
        SetUpTrayIcon();

        Application.Current.SessionEnding += (sender, e) =>
        {
            _isExiting = true;
            _trayIcon.Dispose();
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        BuildSavedViews(null);
        RefreshAccountsTab();
        await LoadGamesAsync();
        RefreshAccountsTab();
    }

    // ===================== Bibliothèque =====================

    private async Task LoadGamesAsync()
    {
        RefreshButton.IsEnabled = false;
        Title = "Wyrmhold — chargement…";

        List<Game> games = await _library.ScanAllAsync();
        ShowGames(games);

        if (_library.IsSteamFamilyConnected)
        {
            Title = $"{_summary} — synchronisation de la famille Steam…";

            if (await SyncSteamFamilySilentlyAsync())
            {
                games = _library.LoadGames();
                ShowGames(games);
            }
        }

        if (_library.IsEpicConnected)
        {
            Title = $"{_summary} — synchronisation d'Epic Games…";

            if (await SyncEpicSilentlyAsync())
            {
                games = _library.LoadGames();
                ShowGames(games);
            }
        }

        if (_library.IsUbisoftConnected)
        {
            Title = $"{_summary} — synchronisation d'Ubisoft Connect…";

            if (await SyncUbisoftSilentlyAsync())
            {
                games = _library.LoadGames();
                ShowGames(games);
            }
        }
        if (_library.IsEaConnected)
        {
            Title = $"{_summary} — synchronisation d'EA…";

            if (await SyncEaSilentlyAsync())
            {
                games = _library.LoadGames();
                ShowGames(games);
            }
        }
        if (_library.IsBattleNetConnected)
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

    private void RebuildGamesView()
    {
        List<Game> sortedGames = SortGames(_allGames);

        _gamesView = CollectionViewSource.GetDefaultView(sortedGames);
        _gamesView.Filter = item => item is Game game && MatchesFilters(game);
        GamesList.ItemsSource = _gamesView;
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

        object? selectedGame = GamesList.SelectedItem;

        _sortMode = ReadSelectedTag(SortMode);
        RebuildGamesView();

        // On garde le jeu sélectionné, et on le fait défiler jusqu'à l'écran.
        if (selectedGame is not null)
        {
            GamesList.SelectedItem = selectedGame;
            GamesList.ScrollIntoView(selectedGame);
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
        if (!MatchesActivity(game))
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
        BuildGameCollectionsPanel();
    }

    /// <summary>
    /// Dans la fiche du jeu : une case à cocher par collection.
    /// </summary>
    private void BuildGameCollectionsPanel()
    {
        GameCollectionsPanel.Children.Clear();

        if (GamesList.SelectedItem is not Game game)
        {
            return;
        }

        if (_collections.Count == 0)
        {
            GameCollectionsPanel.Children.Add(new TextBlock
            {
                Text = "Aucune collection pour l'instant : crée-en une avec le bouton « Gérer… ».",
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap
            });
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
            || GamesList.SelectedItem is not Game game)
        {
            return;
        }

        try
        {
            _library.SetGameInCollection(game, collection, checkBox.IsChecked == true);
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
    private void RandomGameButton_Click(object sender, RoutedEventArgs e)
    {
        List<Game> shownGames = _gamesView?.Cast<Game>().ToList() ?? new List<Game>();

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
        if (GamesList.SelectedItem is not Game game)
        {
            return;
        }

        try
        {
            _library.SetFavorite(game, !game.IsFavorite);
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
        _gamesView?.Refresh();
        UpdateResultCount();
    }

    private void UpdateResultCount()
    {
        int shownCount = _gamesView?.Cast<object>().Count() ?? 0;
        ResultCountText.Text = $"{shownCount} jeu(x) affiché(s)";
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

        Title = _summary;
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

    private void LaunchSelectedGame()
    {
        if (GamesList.SelectedItem is not Game game)
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

        _ = TrackPlaytimeAsync(game);
    }

    private async Task TrackPlaytimeAsync(Game game)
    {
        try
        {
            await _library.TrackPlaytimeAsync(game);
            GamesList.Items.Refresh();
        }
        catch (Exception ex)
        {
            Logger.Log($"Suivi du temps de jeu impossible pour {game.Name} : {ex.Message}");
        }
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
            : "Clé API absente de secrets.json";

        SetAccountRow(SteamFamilyStatusText, SteamFamilyButton, _library.IsSteamFamilyConnected);
        SetAccountRow(GogStatusText, GogAccountButton, _library.IsGogConnected);
        SetAccountRow(EpicStatusText, EpicAccountButton, _library.IsEpicConnected);
        SetAccountRow(UbisoftStatusText, UbisoftAccountButton, _library.IsUbisoftConnected);
        SetAccountRow(EaStatusText, EaAccountButton, _library.IsEaConnected, _library.HasImportedEaGames);
        SetAccountRow(BattleNetStatusText, BattleNetAccountButton, _library.IsBattleNetConnected, _library.HasImportedBattleNetGames);

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

        e.Cancel = true;
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

        _isExiting = true;
        _trayIcon.Dispose();
        Application.Current.Shutdown();
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
