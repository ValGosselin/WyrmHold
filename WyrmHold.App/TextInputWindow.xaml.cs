using System.Windows;

namespace WyrmHold.App;

/// <summary>
/// Petite fenêtre réutilisable qui demande un texte (un nom, par exemple).
/// </summary>
public partial class TextInputWindow : Window
{
    public string Text => InputBox.Text.Trim();

    public TextInputWindow(string title, string prompt, string initialText = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialText;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        InputBox.Focus();
        InputBox.SelectAll();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (Text.Length == 0)
        {
            MessageBox.Show("Écris d'abord un nom.", "Wyrmhold");
            return;
        }

        DialogResult = true;
    }
}
