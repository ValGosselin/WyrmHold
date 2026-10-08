using System.Windows;
using System.Windows.Input;

namespace WyrmHold.App;

/// <summary>
/// Affiche une série d'images en grand (les captures d'un jeu), une à la fois.
/// </summary>
public partial class ImageViewerWindow : Window
{
    private readonly IReadOnlyList<string> _urls;
    private int _index;

    public ImageViewerWindow(string title, IReadOnlyList<string> urls, int startIndex)
    {
        InitializeComponent();
        Title = title;
        _urls = urls;
        _index = startIndex;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        WebImageLoader.SetUrl(FullImage, _urls[_index]);
        CounterText.Text = $"{_index + 1} / {_urls.Count}";

        // Pas de flèche « précédente » sur la première image, ni « suivante » sur la dernière.
        PreviousButton.Visibility = _index > 0 ? Visibility.Visible : Visibility.Hidden;
        NextButton.Visibility = _index < _urls.Count - 1 ? Visibility.Visible : Visibility.Hidden;
    }

    private void Move(int step)
    {
        int newIndex = _index + step;

        if (newIndex >= 0 && newIndex < _urls.Count)
        {
            _index = newIndex;
            ShowCurrent();
        }
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        Move(-1);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        Move(1);
    }

    // PreviewKeyDown : on capte les flèches AVANT que WPF ne s'en serve pour déplacer le focus d'un bouton à l'autre.
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Right:
                Move(1);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }
}
