using System.Windows;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// La fenêtre « Voir les succès » (fiche du jeu, ou bouton « 🏆 Succès » de l'Aide).
/// Tout le contenu est dans AchievementsPanel ; la fenêtre ne gère que l'aide reliée.
/// </summary>
public partial class AchievementsWindow : Window
{
    private readonly LibraryService _library;
    private readonly Game _game;
    private readonly AchievementsPanel _panel;

    public AchievementsWindow(LibraryService library, Game game)
    {
        InitializeComponent();
        _library = library;
        _game = game;

        Title = $"Succès — {game.Name}";

        _panel = new AchievementsPanel(library, game);
        _panel.HelpRequested += OpenHelp;
        PanelHost.Content = _panel;

        Closed += (sender, e) => _panel.Stop();
    }

    // La fenêtre Aide reliée (au plus une), voir HelpWindow.Link.
    public HelpWindow? LinkedHelp { get; set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _panel.StartAsync();
    }

    /// <summary>
    /// « Comment l'obtenir ? » : ouvre la fenêtre Aide avec « nom du succès + succès » déjà cherché sur Google
    /// (les boutons YouTube, forums Steam et Reddit reprennent les mêmes mots).
    /// </summary>
    private void OpenHelp(string words)
    {
        // Une aide est déjà reliée : la recherche s'y fait, sans ouvrir de 2e fenêtre.
        if (LinkedHelp is not null)
        {
            LinkedHelp.SearchFor(words);
            return;
        }

        // Pas d'Owner (voir HelpWindow.OpenAchievements) ; l'aide se ferme avec cette fenêtre.
        HelpWindow window = new HelpWindow(_library, _game, words)
        {
            Topmost = Topmost,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        HelpWindow.Link(window, this, opener: this);
        window.Show();
    }
}
