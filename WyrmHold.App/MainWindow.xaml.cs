using System.Windows;
using System.Windows.Input;
using Wyrmhold.Core;

namespace WyrmHold.App;

public partial class MainWindow : Window
{
    private readonly SteamScanner _scanner = new SteamScanner();
    private readonly GameDatabase _database = new GameDatabase();

    public MainWindow()
    {
        InitializeComponent();
        _database.Initialize();
        LoadGames();
    }

    private void LoadGames()
    {
        List<Game> games = _scanner.GetInstalledGames();
        _database.SaveGames(Platform.Steam, games);

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

        GameLauncher.Launch(game);
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