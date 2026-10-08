using System.Windows;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Demande quoi faire quand on ferme la fenêtre : quitter, ou garder Wyrmhold en arrière-plan.
/// </summary>
public partial class CloseChoiceWindow : Window
{
    // Le choix fait : CloseActions.Background ou CloseActions.Quit.
    // null si la fenêtre a été fermée sans choisir (croix ou Échap) : on ne fait alors rien.
    public string? Choice { get; private set; }

    public bool RememberChoice => RememberCheck.IsChecked == true;

    public CloseChoiceWindow()
    {
        InitializeComponent();
    }

    private void BackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseActions.Background;
        DialogResult = true;
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseActions.Quit;
        DialogResult = true;
    }
}
