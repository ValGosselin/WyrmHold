using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

public partial class MainWindow : Window
{
    private const string SteamStoreUrl = "https://store.steampowered.com/";

    private readonly LibraryService _library = new LibraryService();
    private readonly System.Windows.Forms.NotifyIcon _trayIcon = new System.Windows.Forms.NotifyIcon();

    private bool _isExiting;
    private bool _trayTipShown;
    private string _summary = "Wyrmhold";

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

        await CompleteGamesAsync(games);
        RefreshButton.IsEnabled = true;
    }

    private void ShowGames(List<Game> games)
    {
        GamesList.ItemsSource = games.OrderBy(g => NameTools.Normalize(g.Name)).ToList();

        int installedCount = games.Count(g => g.IsInstalled);
        _summary = $"Wyrmhold — {games.Count} jeu(x), dont {installedCount} installé(s)";
        Title = _summary;
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
    }

    private static void SetAccountRow(TextBlock statusText, Button button, bool isConnected)
    {
        statusText.Text = isConnected ? "Connecté" : "Non connecté";
        button.Content = isConnected ? "Se déconnecter" : "Se connecter";
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
}