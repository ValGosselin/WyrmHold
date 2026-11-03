using System.Windows;
using System.Windows.Controls;

namespace WyrmHold.App;

/// <summary>
/// Dessine le texte d'une version (les « ### » et les « - » de notes-de-version.md) dans un panneau.
/// Partagé par UpdateWindow et WhatsNewWindow.
/// </summary>
public static class ReleaseNotesView
{
    public static void AddTo(Panel panel, string markdown)
    {
        foreach (string rawLine in markdown.Split('\n'))
        {
            string line = rawLine.Trim();

            if (line.StartsWith("#"))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = line.TrimStart('#').Trim(),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 4)
                });
            }
            else if (line.StartsWith("- "))
            {
                panel.Children.Add(MakeLine($"•  {line[2..]}"));
            }
            else if (line.Length > 0)
            {
                panel.Children.Add(MakeLine(line));
            }
        }
    }

    private static TextBlock MakeLine(string text)
    {
        TextBlock block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };

        block.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        return block;
    }
}
