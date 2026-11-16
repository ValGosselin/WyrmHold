using System.Windows;
using System.Windows.Controls;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Fenêtre « Choisir une jaquette » (clic droit sur une tuile, ticket #41) : montre les jaquettes verticales
/// de SteamGridDB pour un jeu ; un clic télécharge l'image choisie et la garde comme jaquette.
/// DialogResult = true si une jaquette a été changée.
/// </summary>
public partial class CoverPickerWindow : Window
{
    private readonly LibraryService _library;
    private readonly Game _game;

    public CoverPickerWindow(LibraryService library, Game game)
    {
        InitializeComponent();
        _library = library;
        _game = game;
        Title = $"Choisir une jaquette · {game.Name}";
        HeaderText.Text = game.Name;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        List<CoverChoice> choices;

        try
        {
            choices = await _library.GetCoverChoicesAsync(_game);
        }
        catch (Exception ex)
        {
            Logger.Log($"Jaquettes SteamGridDB illisibles pour {_game.Name} : {ex.Message}");
            StatusText.Text = "SteamGridDB n'a pas répondu. Réessaie dans un moment ; les détails sont dans le journal.";
            return;
        }

        if (choices.Count == 0)
        {
            StatusText.Text = $"SteamGridDB ne propose aucune jaquette verticale pour {_game.Name}.";
            return;
        }

        ChoicesList.ItemsSource = choices;
        StatusText.Visibility = Visibility.Collapsed;
        ChoicesScroll.Visibility = Visibility.Visible;
    }

    private async void Choice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CoverChoice choice })
        {
            return;
        }

        // Pendant le téléchargement : plus de clic possible, et on dit ce qui se passe.
        ChoicesScroll.IsEnabled = false;
        Cursor = System.Windows.Input.Cursors.Wait;

        try
        {
            await _library.SetCoverAsync(_game, choice);
            Logger.Log($"Jaquette de {_game.Name} choisie à la main (SteamGridDB, par {choice.Author}).");
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Jaquette choisie impossible à télécharger pour {_game.Name} : {ex.Message}");
            MessageBox.Show(this, "Cette jaquette n'a pas pu être téléchargée. Essaie une autre image ; les détails sont dans le journal.", "Wyrmhold");
            ChoicesScroll.IsEnabled = true;
            Cursor = null;
        }
    }
}
