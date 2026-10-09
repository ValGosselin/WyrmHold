using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Fenêtre « Aide » d'un jeu : recherche (Google, YouTube, forums Steam, Reddit), liens rapides
/// et guides Steam à gauche, navigateur intégré à droite. Wyrmhold ne recopie rien :
/// les pages s'affichent telles quelles, comme dans un navigateur.
/// </summary>
public partial class HelpWindow : Window
{
    private readonly LibraryService _library;
    private readonly Game _game;

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

    // initialSearch : des mots déjà tapés dans la case, et la recherche Google lancée à l'ouverture
    // (ex. le nom d'un succès, depuis « Comment l'obtenir ? »).
    public HelpWindow(LibraryService library, Game game, string? initialSearch = null)
    {
        InitializeComponent();
        _library = library;
        _game = game;

        Title = $"Aide — {game.Name}";
        GameNameText.Text = game.Name;
        UpdateAchievementsButton();

        if (!string.IsNullOrWhiteSpace(initialSearch))
        {
            // Le navigateur n'est pas encore prêt : Navigate garde l'adresse et l'ouvrira dès qu'il le sera.
            Search(initialSearch);
        }
    }

    /// <summary>
    /// La fenêtre « Voir les succès » reliée à cette aide (au plus une) : les deux boutons
    /// « 🏆 Succès » et « ❓ Comment l'obtenir ? » passent de l'une à l'autre au lieu d'en ouvrir de nouvelles.
    /// </summary>
    public AchievementsWindow? LinkedAchievements { get; private set; }

    /// <summary>
    /// Relie une aide et une fenêtre de succès. opener = celle qui a ouvert l'autre :
    /// quand elle se ferme, l'autre se ferme aussi (ex. Ctrl + Maj + G en jeu ferme tout).
    /// </summary>
    public static void Link(HelpWindow help, AchievementsWindow achievements, Window opener)
    {
        help.LinkedAchievements = achievements;
        achievements.LinkedHelp = help;

        // L'une se ferme : l'autre n'a plus de partenaire.
        help.Closed += (s, e) => achievements.LinkedHelp = null;
        achievements.Closed += (s, e) => help.LinkedAchievements = null;

        Window opened = opener == help ? achievements : help;
        bool isOpenedClosed = false;
        opened.Closed += (s, e) => isOpenedClosed = true;
        opener.Closed += (s, e) =>
        {
            if (!isOpenedClosed)
            {
                opened.Close();
            }
        };
    }

    /// <summary>
    /// « Comment l'obtenir ? » depuis la fenêtre des succès reliée : mêmes mots, même recherche Google,
    /// dans cette fenêtre-ci, qui repasse devant.
    /// </summary>
    public void SearchFor(string words)
    {
        Search(words);
        Activate();
    }

    private void Search(string words)
    {
        SearchBox.Text = words;
        Navigate("https://www.google.com/search?q=" + Encode(BuildQuery()));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Les deux en même temps : le navigateur démarre pendant que Steam répond.
        Task browser = InitializeBrowserAsync();
        Task guides = LoadGuidesAsync();
        await Task.WhenAll(browser, guides);
    }

    // Échap ferme la fenêtre (quand le navigateur n'a pas le clavier).
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    // ----- Succès -----

    // « 🏆 Succès (37/50) » : caché si le jeu n'a pas de succès connus, ou si leur source est désactivée.
    private void UpdateAchievementsButton()
    {
        bool canShow = _game.HasAchievements && _library.Settings.IsAchievementSourceEnabled(_game.Platform);

        AchievementsButton.Content = $"🏆 Succès ({_game.AchievementsText})";
        AchievementsButton.Visibility = canShow ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AchievementsButton_Click(object sender, RoutedEventArgs e)
    {
        // Déjà ouverte : on la ramène devant, sans en ouvrir une 2e.
        if (LinkedAchievements is not null)
        {
            LinkedAchievements.Activate();
            return;
        }

        // Pas d'Owner : une fenêtre « possédée » resterait toujours devant l'aide, qui ne pourrait plus repasser devant.
        // Topmost comme l'aide : en jeu, les deux passent devant le jeu.
        AchievementsWindow window = new AchievementsWindow(_library, _game)
        {
            Topmost = Topmost,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.Closed += (s, args) => UpdateAchievementsButton();   // la progression a pu changer
        Link(this, window, opener: this);
        window.Show();
    }

    // ----- Navigateur intégré -----

    private async Task InitializeBrowserAsync()
    {
        try
        {
            // Un dossier à part : les connexions (Steam, Reddit…) sont gardées d'une ouverture à l'autre.
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder("Aide"));

            await Browser.EnsureCoreWebView2Async(environment);

            // Un lien qui veut ouvrir un nouvel onglet s'ouvre ici, dans la même vue.
            Browser.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
                Browser.CoreWebView2.Navigate(args.Uri);
            };

            Browser.CoreWebView2.SourceChanged += (s, args) => AddressText.Text = Browser.CoreWebView2.Source;

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
        Browser.Visibility = Visibility.Visible;
        Browser.CoreWebView2.Navigate(url);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_browserReady && Browser.CoreWebView2.CanGoBack)
        {
            Browser.CoreWebView2.GoBack();
        }
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_browserReady && Browser.CoreWebView2.CanGoForward)
        {
            Browser.CoreWebView2.GoForward();
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_browserReady && Browser.Visibility == Visibility.Visible)
        {
            Browser.CoreWebView2.Reload();
        }
    }

    private void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        if (_browserReady && Browser.Visibility == Visibility.Visible)
        {
            BrowserHelper.Open(Browser.CoreWebView2.Source);
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
        // Pendant InitializeComponent, la liste déroulante déclenche déjà son événement.
        if (!IsLoaded)
        {
            return;
        }

        await LoadGuidesAsync();
    }

    private void GuidesFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
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
