using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Corriger à la main le jeu RetroAchievements d'un jeu émulé (clic droit sur la tuile).
/// Montre l'association actuelle, les versions acceptées si la tienne n'est pas reconnue, et la liste de la console.
/// </summary>
public partial class RetroAchievementsMatchWindow : Window
{
    private readonly LibraryService _library;
    private readonly Game _game;
    private List<RetroAchievementsGame> _allGames = new List<RetroAchievementsGame>();

    public RetroAchievementsMatchWindow(LibraryService library, Game game)
    {
        InitializeComponent();
        _library = library;
        _game = game;
        Title = $"{game.Name} : jeu RetroAchievements";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Lecture de la liste…";
        SearchBox.Focus();

        await RunAsync(async () =>
        {
            _allGames = await _library.GetRetroAchievementsGamesAsync(_game);
            GamesList.ItemsSource = _allGames;
            ShowCurrentChoice();

            if (_game.IsRetroAchievementsVersionUnknown)
            {
                await ShowAcceptedVersionsAsync();
            }
        });
    }

    private void ShowCurrentChoice()
    {
        string title = _allGames.FirstOrDefault(game => game.Id == _game.RetroAchievementsGameId)?.Title ?? "";

        StatusText.Text = _game.RetroAchievementsMatch switch
        {
            RetroAchievementsMatch.Hash => $"Associé à « {title} ». Ta version est reconnue.",
            RetroAchievementsMatch.Name => $"Associé à « {title} » par son nom. Ta version n'est pas reconnue : ses succès ne se débloqueront probablement pas.",
            RetroAchievementsMatch.Manual when _game.RetroAchievementsGameId > 0 =>
                $"Choisi à la main : « {title} ». Ta version n'est pas reconnue : ses succès ne se débloqueront probablement pas.",
            RetroAchievementsMatch.Manual => "Tu as choisi de n'associer aucun jeu.",
            _ => "Aucun jeu trouvé pour l'instant. Choisis-le dans la liste s'il existe."
        };
    }

    private async Task ShowAcceptedVersionsAsync()
    {
        List<RetroAchievementsVersion> versions = await _library.GetRetroAchievementsVersionsAsync(_game.RetroAchievementsGameId);

        IEnumerable<string> names = versions.Select(version => version.FileName ?? version.Hash);

        VersionsText.Text = $"Version(s) acceptée(s) : {string.Join(" ; ", names)}";
        VersionsText.Visibility = Visibility.Visible;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string search = NameTools.RemoveDiacritics(SearchBox.Text.Trim());

        GamesList.ItemsSource = search.Length == 0
            ? _allGames
            : _allGames.Where(game => NameTools.RemoveDiacritics(game.Title).Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ChooseButton.IsEnabled = GamesList.SelectedItem is not null;
    }

    private void GamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GamesList.SelectedItem is not null)
        {
            ChooseButton_Click(sender, e);
        }
    }

    private void ChooseButton_Click(object sender, RoutedEventArgs e)
    {
        _library.SetRetroAchievementsGame(_game, GamesList.SelectedItem as RetroAchievementsGame);
        DialogResult = true;
    }

    private void NoGameButton_Click(object sender, RoutedEventArgs e)
    {
        _library.SetRetroAchievementsGame(_game, null);
        DialogResult = true;
    }

    private async void AutomaticButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            await _library.ResetRetroAchievementsGameAsync(_game);
            DialogResult = true;
        });
    }

    // Une erreur du site ne ferme pas l'appli : on le dit dans la fenêtre, et rien n'est changé.
    private async Task RunAsync(Func<Task> action)
    {
        AutomaticButton.IsEnabled = false;
        NoGameButton.IsEnabled = false;

        try
        {
            await action();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or System.IO.IOException or AchievementSourceUnavailableException)
        {
            Logger.Log($"Fenêtre RetroAchievements : {ex.Message}");
            StatusText.Text = "RetroAchievements ne répond pas. Réessaie dans un moment.";
        }
        finally
        {
            AutomaticButton.IsEnabled = true;
            NoGameButton.IsEnabled = true;
        }
    }
}
