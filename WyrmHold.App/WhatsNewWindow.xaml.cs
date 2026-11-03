using System.Windows;
using System.Windows.Controls;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// « Quoi de neuf » : les nouveautés d'une ou plusieurs versions, la plus récente en haut.
/// </summary>
public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow(List<ReleaseNote> notes)
    {
        InitializeComponent();

        VersionText.Text = $"Version {AppInfo.Version}";

        foreach (ReleaseNote note in notes)
        {
            NotesList.Children.Add(new TextBlock
            {
                Text = $"Version {note.Version.ToString(3)}",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, NotesList.Children.Count == 0 ? 0 : 16, 0, 0)
            });

            ReleaseNotesView.AddTo(NotesList, note.Markdown);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
