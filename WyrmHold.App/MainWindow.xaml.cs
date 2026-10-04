using System.Windows;
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
        Title = $"Wyrmhold — {games.Count} jeu(x), dont {installedCount} installé(s)";

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
        }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchSelectedGame();
    }



    private void GamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        LaunchSelectedGame();
    }
}