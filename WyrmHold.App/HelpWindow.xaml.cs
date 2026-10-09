using System.Windows;
using System.Windows.Input;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// La fenêtre « Aide » d'un jeu (bouton « 🧭 Aide » de la fiche). Tout le contenu est dans HelpPanel ;
/// la fenêtre ne gère que Échap et la fenêtre de succès reliée.
/// </summary>
public partial class HelpWindow : Window
{
    private readonly LibraryService _library;
    private readonly Game _game;
    private readonly HelpPanel _panel;

    // initialSearch : des mots déjà tapés dans la case, et la recherche Google lancée à l'ouverture
    // (ex. le nom d'un succès, depuis « Comment l'obtenir ? »).
    public HelpWindow(LibraryService library, Game game, string? initialSearch = null)
    {
        InitializeComponent();
        _library = library;
        _game = game;

        Title = $"Aide — {game.Name}";

        _panel = new HelpPanel(library, game, initialSearch);
        _panel.AchievementsRequested += OpenAchievements;
        PanelHost.Content = _panel;

        Closed += (sender, e) => _panel.DisposeBrowser();
    }

    /// <summary>
    /// La fenêtre « Voir les succès » reliée à cette aide (au plus une) : les deux boutons
    /// « 🏆 Succès » et « ❓ Comment l'obtenir ? » passent de l'une à l'autre au lieu d'en ouvrir de nouvelles.
    /// </summary>
    public AchievementsWindow? LinkedAchievements { get; private set; }

    /// <summary>
    /// Relie une aide et une fenêtre de succès. opener = celle qui a ouvert l'autre :
    /// quand elle se ferme, l'autre se ferme aussi.
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
        _panel.SearchFor(words);
        Activate();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _panel.StartAsync();
    }

    // Échap ferme la fenêtre (quand le navigateur n'a pas le clavier).
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void OpenAchievements()
    {
        // Déjà ouverte : on la ramène devant, sans en ouvrir une 2e.
        if (LinkedAchievements is not null)
        {
            LinkedAchievements.Activate();
            return;
        }

        // Pas d'Owner : une fenêtre « possédée » resterait toujours devant l'aide, qui ne pourrait plus repasser devant.
        AchievementsWindow window = new AchievementsWindow(_library, _game)
        {
            Topmost = Topmost,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.Closed += (s, args) => _panel.UpdateAchievementsButton();   // la progression a pu changer
        Link(this, window, opener: this);
        window.Show();
    }
}
