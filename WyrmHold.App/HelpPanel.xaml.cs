using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// L'aide d'un jeu : recherche (Google, YouTube, forums Steam, Reddit), liens rapides
/// et guides Steam à gauche, navigateur intégré à droite. Wyrmhold ne recopie rien :
/// les pages s'affichent telles quelles, comme dans un navigateur.
/// Sert dans la fenêtre « Aide » et dans l'overlay ; c'est elles qui appellent StartAsync.
/// </summary>
public partial class HelpPanel : UserControl
{
    private readonly LibraryService _library;
    private readonly Game _game;

    // Le navigateur : deux versions possibles, mêmes commandes (IWebView2).
    // - WebView2 (normal) : une vraie fenêtre Windows posée dans la nôtre. Rapide, mais INVISIBLE
    //   dans une fenêtre transparente (AllowsTransparency), comme l'overlay.
    // - WebView2CompositionControl : la page est dessinée par WPF lui-même. Un peu moins rapide,
    //   mais elle s'affiche aussi dans une fenêtre transparente. C'est celle de l'overlay.
    private readonly IWebView2 _browser;
    private readonly FrameworkElement _browserElement;

    // Les guides reçus pour le tri choisi (le filtre, lui, s'applique sans rappeler Steam).
    private List<SteamGuide> _guides = new List<SteamGuide>();

    // null = jeu introuvable sur Steam : pas de forum Steam.
    private string? _steamAppId;

    // Une page demandée avant que le navigateur soit prêt : on l'ouvre dès qu'il l'est.
    private string? _pendingUrl;
    private bool _browserReady;

    // Le navigateur intégré n'a pas pu démarrer : les pages s'ouvrent dans le navigateur de Windows.
    private bool _browserFailed;

    // Pour ignorer la réponse d'un ancien tri si on a changé d'avis entre-temps.
    private int _guidesRequest;

    // Pendant InitializeComponent, les listes déroulantes déclenchent déjà leur événement : on les ignore.
    private readonly bool _ready;
    private bool _started;

    // initialSearch : des mots déjà tapés dans la case, et la recherche Google lancée à l'ouverture
    // (ex. le nom d'un succès, depuis « Comment l'obtenir ? »).
    // forTransparentWindow : true pour l'overlay (navigateur « composition », voir plus haut).
    public HelpPanel(LibraryService library, Game game, string? initialSearch, bool forTransparentWindow)
    {
        InitializeComponent();
        _library = library;
        _game = game;
        _ready = true;

        if (forTransparentWindow)
        {
            WebView2CompositionControl composition = new WebView2CompositionControl();
            _browser = composition;
            _browserElement = composition;
        }
        else
        {
            WebView2 normal = new WebView2();
            _browser = normal;
            _browserElement = normal;
        }

        _browserElement.Visibility = Visibility.Collapsed;
        BrowserHost.Children.Add(_browserElement);

        GameNameText.Text = game.Name;
        UpdateAchievementsButton();

        if (!string.IsNullOrWhiteSpace(initialSearch))
        {
            // Le navigateur n'est pas encore prêt : Navigate garde l'adresse et l'ouvrira dès qu'il le sera.
            SearchFor(initialSearch);
        }
    }

    /// <summary>Le bouton « 🏆 Succès » : à la fenêtre (ou à l'overlay) de montrer les succès.</summary>
    public event Action? AchievementsRequested;

    // L'overlay affiche déjà le nom du jeu dans sa barre du haut.
    public bool ShowGameName
    {
        set => GameNameText.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Démarre le navigateur et lit les guides Steam (une seule fois).</summary>
    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        // Les deux en même temps : le navigateur démarre pendant que Steam répond.
        Task browser = InitializeBrowserAsync();
        Task guides = LoadGuidesAsync();
        await Task.WhenAll(browser, guides);
    }

    /// <summary>Cherche ces mots sur Google (ex. « Comment l'obtenir ? » d'un succès).</summary>
    public void SearchFor(string words)
    {
        SearchBox.Text = words;
        Navigate("https://www.google.com/search?q=" + Encode(BuildQuery()));
    }

    /// <summary>Libère le navigateur (la fenêtre ou l'overlay qui le contient se ferme).</summary>
    public void DisposeBrowser()
    {
        (_browser as IDisposable)?.Dispose();
    }

    // ----- Succès -----

    // « 🏆 Succès (37/50) » : caché si le jeu n'a pas de succès connus, ou si leur source est désactivée.
    public void UpdateAchievementsButton()
    {
        bool canShow = _game.HasAchievements && _library.Settings.IsAchievementSourceEnabled(_game.Platform);

        AchievementsButton.Content = $"🏆 Succès ({_game.AchievementsText})";
        AchievementsButton.Visibility = canShow ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AchievementsButton_Click(object sender, RoutedEventArgs e)
    {
        AchievementsRequested?.Invoke();
    }

    // ----- Navigateur intégré -----

    private async Task InitializeBrowserAsync()
    {
        try
        {
            // Un dossier à part : les connexions (Steam, Reddit…) sont gardées d'une ouverture à l'autre.
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder("Aide"));

            await _browser.EnsureCoreWebView2Async(environment);

            // Un lien qui veut ouvrir un nouvel onglet s'ouvre ici, dans la même vue.
            _browser.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
                _browser.CoreWebView2.Navigate(args.Uri);
            };

            _browser.CoreWebView2.SourceChanged += (s, args) => AddressText.Text = _browser.CoreWebView2.Source;

            _browserReady = true;

            if (_pendingUrl is not null)
            {
                Navigate(_pendingUrl);
                _pendingUrl = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Navigateur de l'aide impossible à démarrer : {ex.Message}");
            _browserFailed = true;
            BrowserPlaceholder.Text = "Le navigateur intégré n'a pas pu démarrer : les pages s'ouvriront dans ton navigateur habituel.";
        }
    }

    private void Navigate(string url)
    {
        if (!_browserReady)
        {
            // Navigateur en panne : on se rabat sur le navigateur de Windows.
            if (_browserFailed)
            {
                BrowserHelper.Open(url);
            }
            else
            {
                _pendingUrl = url;
            }
            return;
        }

        BrowserPlaceholder.Visibility = Visibility.Collapsed;
        _browserElement.Visibility = Visibility.Visible;
        _browser.CoreWebView2.Navigate(url);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_browserReady && _browser.CoreWebView2.CanGoBack)
        {
            _browser.CoreWebView2.GoBack();
        }
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_browserReady && _browser.CoreWebView2.CanGoForward)
        {
            _browser.CoreWebView2.GoForward();
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_browserReady && _browserElement.Visibility == Visibility.Visible)
        {
            _browser.CoreWebView2.Reload();
        }
    }

    private void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        if (_browserReady && _browserElement.Visibility == Visibility.Visible)
        {
            BrowserHelper.Open(_browser.CoreWebView2.Source);
        }
    }

    // ----- Recherche -----

    // « Elden Ring sauvegarde corrompue » : le nom du jeu est ajouté devant les mots tapés.
    private string BuildQuery(string suffix = "")
    {
        string words = SearchBox.Text.Trim();
        return string.Join(" ", new[] { _game.Name, words, suffix }.Where(part => part.Length > 0));
    }

    private static string Encode(string text) => Uri.EscapeDataString(text);

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Navigate("https://www.google.com/search?q=" + Encode(BuildQuery()));
        }
    }

    private void SearchGoogle_Click(object sender, RoutedEventArgs e)
    {
        Navigate("https://www.google.com/search?q=" + Encode(BuildQuery()));
    }

    private void SearchYouTube_Click(object sender, RoutedEventArgs e)
    {
        Navigate("https://www.youtube.com/results?search_query=" + Encode(BuildQuery()));
    }

    private void SearchSteamForum_Click(object sender, RoutedEventArgs e)
    {
        if (_steamAppId is null)
        {
            return;
        }

        // Le forum est déjà celui du jeu : seuls les mots tapés comptent.
        string words = SearchBox.Text.Trim();
        Navigate(words.Length == 0
            ? $"https://steamcommunity.com/app/{_steamAppId}/discussions/"
            : $"https://steamcommunity.com/app/{_steamAppId}/discussions/search/?q={Encode(words)}");
    }

    private void SearchReddit_Click(object sender, RoutedEventArgs e)
    {
        Navigate("https://www.reddit.com/search/?q=" + Encode(BuildQuery()));
    }

    // ----- Liens rapides -----

    private void SteamForum_Click(object sender, RoutedEventArgs e)
    {
        if (_steamAppId is not null)
        {
            Navigate($"https://steamcommunity.com/app/{_steamAppId}/discussions/");
        }
    }

    private void Reddit_Click(object sender, RoutedEventArgs e)
    {
        Navigate("https://www.reddit.com/search/?q=" + Encode(_game.Name));
    }

    private void Wiki_Click(object sender, RoutedEventArgs e)
    {
        // Pas de moyen fiable de trouver le wiki d'un jeu : Google le trouve très bien.
        Navigate("https://www.google.com/search?q=" + Encode($"{_game.Name} wiki"));
    }

    // ----- Guides Steam -----

    private async Task LoadGuidesAsync()
    {
        int request = ++_guidesRequest;
        bool newestFirst = ReadSelectedTag(GuidesSort) == "newest";

        GuidesStatusText.Text = "Chargement des guides…";
        GuidesList.ItemsSource = null;

        GuidesResult result = await _library.GetGuidesAsync(_game, newestFirst);

        if (request != _guidesRequest)
        {
            return;
        }

        _steamAppId = result.SteamAppId;
        _guides = result.Guides;

        // Pas d'appid Steam : les boutons du forum Steam ne serviraient à rien.
        bool hasSteamForum = _steamAppId is not null;
        SteamForumButton.IsEnabled = hasSteamForum;
        SearchSteamForumButton.IsEnabled = hasSteamForum;

        ShowGuides(result.Message);
    }

    private void ShowGuides(string message = "")
    {
        string filter = ReadSelectedTag(GuidesFilter);

        List<SteamGuide> shown = filter switch
        {
            "achievements" => _guides.Where(guide => guide.IsAboutAchievements).ToList(),
            "french" => _guides.Where(guide => guide.IsFrench).ToList(),
            _ => _guides
        };

        GuidesList.ItemsSource = shown;

        GuidesStatusText.Text = message.Length > 0 ? message
            : shown.Count == 0 && filter == "achievements" ? "Aucun guide de succès parmi les 100 premiers."
            : shown.Count == 0 && filter == "french" ? "Aucun guide en français parmi les 100 premiers."
            : $"{shown.Count} guide(s)";
    }

    private async void GuidesSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || !_started)
        {
            return;
        }

        await LoadGuidesAsync();
    }

    private void GuidesFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || !_started)
        {
            return;
        }

        ShowGuides();
    }

    private void GuidesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GuidesList.SelectedItem is SteamGuide guide)
        {
            Navigate(guide.Url);
        }
    }

    private static string ReadSelectedTag(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    }
}
