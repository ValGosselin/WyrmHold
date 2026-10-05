using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wyrmhold.Core;

namespace WyrmHold.App;

public partial class MainWindow : Window
{
    private readonly LibraryService _library = new LibraryService();

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadGamesAsync();
    }

    private async Task LoadGamesAsync()
    {
        RefreshButton.IsEnabled = false;
        Title = "Wyrmhold — chargement…";

        List<Game> games = await _library.ScanAllAsync();
        GamesList.ItemsSource = games.OrderBy(g => g.Name).ToList();

        int installedCount = games.Count(g => g.IsInstalled);
        string summary = $"Wyrmhold — {games.Count} jeu(x), dont {installedCount} installé(s)";
        Title = $"{summary} — téléchargement des jaquettes…";

        try
        {
            await _library.DownloadCoversAsync(games);
            GamesList.Items.Refresh();
        }
        catch (Exception ex)
        {
            Logger.Log($"Téléchargement des jaquettes impossible : {ex.Message}");
        }
        Title = $"{summary} — récupération des infos…";

        try
        {
            await _library.UpdateMetadataAsync(games);
        }
        catch (Exception ex)
        {
            Logger.Log($"Récupération des infos impossible : {ex.Message}");
        }
        Title = summary;
        RefreshButton.IsEnabled = true;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadGamesAsync();
    }

    private void LaunchSelectedGame()
    {
        if (GamesList.SelectedItem is not Game game)
        {
            MessageBox.Show("Sélectionne d'abord un jeu dans la liste.", "Wyrmhold");
            return;
        }

        if (!_library.Launch(game))
        {
            MessageBox.Show($"Impossible de lancer {game.Name}. Les détails sont dans le journal.", "Wyrmhold");
            return;
        }

        _ = TrackPlaytimeAsync(game);
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchSelectedGame();
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



    private void GamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject clickedElement
            || ItemsControl.ContainerFromElement(GamesList, clickedElement) is not ListBoxItem)
        {
            return;
        }

        LaunchSelectedGame();
    }
    private async void FamilyButton_Click(object sender, RoutedEventArgs e)
    {
        SteamLoginWindow loginWindow = new SteamLoginWindow { Owner = this };

        if (loginWindow.ShowDialog() != true || loginWindow.AccessToken is null)
        {
            return;
        }
        if (loginWindow.SessionSteamId != _library.SteamId)
        {
            MessageBox.Show(
                "Tu es connecté à Steam avec un autre compte que celui configuré dans Wyrmhold. "
                + "Déconnecte-toi de Steam dans Wyrmhold, puis reconnecte-toi avec ton compte.",
                "Wyrmhold");
            return;
        }

        FamilyButton.IsEnabled = false;

        try
        {
            int count = await _library.ImportFamilyLibraryAsync(loginWindow.AccessToken);
            MessageBox.Show($"{count} jeu(x) de la famille importé(s).", "Wyrmhold");
            await LoadGamesAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"Import de la famille impossible : {ex.Message}");
            MessageBox.Show("L'import de la bibliothèque familiale a échoué. Les détails sont dans le journal.", "Wyrmhold");
        }
        finally
        {
            FamilyButton.IsEnabled = true;
        }
    }
    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        string webViewFolder = Path.Combine(AppPaths.DataFolder, "WebView2");

        if (!Directory.Exists(webViewFolder))
        {
            MessageBox.Show("Aucune session Steam n'est enregistrée dans Wyrmhold.", "Wyrmhold");
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            "Effacer la session Steam enregistrée dans Wyrmhold ? Il faudra te reconnecter au prochain import.",
            "Wyrmhold",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Directory.Delete(webViewFolder, recursive: true);
            MessageBox.Show("Session Steam effacée.", "Wyrmhold");
        }
        catch (Exception ex)
        {
            Logger.Log($"Déconnexion Steam impossible : {ex.Message}");
            MessageBox.Show("Impossible d'effacer la session pour l'instant. Réessaie dans quelques secondes.", "Wyrmhold");
        }
    }
}