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
        LoadGames();
    }

    private void LoadGames()
    {
        List<Game> games = _library.ScanAll();

        GamesList.ItemsSource = games.OrderBy(g => g.Name).ToList();
        Title = $"Wyrmhold — {games.Count} jeu(x)";
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

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadGames();
    }

    private void GamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        LaunchSelectedGame();
    }
}