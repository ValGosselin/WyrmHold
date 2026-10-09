using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Fenêtre « Signaler un bug » : l'utilisateur décrit le problème, voit le rapport nettoyé,
/// puis choisit de l'ouvrir sur GitHub ou de le copier. Rien ne part sans un clic.
/// </summary>
public partial class BugReportWindow : Window
{
    private readonly BugReport _report;

    // crashFile : le rapport d'un plantage précédent (voir CrashReporter), ou null pour un bug signalé à la main.
    public BugReportWindow(AppSettings settings, string? crashFile = null)
    {
        InitializeComponent();

        _report = BugReport.Create(settings, crashFile);

        if (_report.IsCrash)
        {
            IntroText.Text = "Wyrmhold s'est fermé de façon inattendue. Si tu te souviens de ce que tu faisais juste avant, "
                + "écris-le ici : ça aide beaucoup à trouver la cause.";
        }

        UpdatePreview();
        DescriptionBox.Focus();
    }

    private void DescriptionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // L'aperçu suit la frappe : on voit toujours ce qui partira.
        _report.Description = DescriptionBox.Text;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        PreviewBox.Text = _report.ToText();
        StatusText.Text = "";
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // UseShellExecute : l'adresse s'ouvre dans le navigateur par défaut de l'utilisateur.
            Process.Start(new ProcessStartInfo(_report.GetGitHubUrl()) { UseShellExecute = true });
            StatusText.Text = "Page GitHub ouverte : vérifie le ticket, puis clique sur « Create » (ou « Submit new issue »). "
                + "Si le journal a été raccourci, colle le rapport complet (« Copier le rapport ») dans le ticket.";
        }
        catch (Exception ex)
        {
            Logger.Log($"Ouverture de la page GitHub impossible : {ex.Message}");
            StatusText.Text = "Le navigateur n'a pas pu s'ouvrir. Utilise « Copier le rapport ».";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText($"{_report.Title}\n\n{_report.ToText()}");
            StatusText.Text = "Rapport copié : colle-le où tu veux (Discord, e-mail…) avec Ctrl + V.";
        }
        catch (Exception ex)
        {
            // Le presse-papiers peut être bloqué un instant par un autre programme.
            Logger.Log($"Copie du rapport impossible : {ex.Message}");
            StatusText.Text = "Copie impossible pour l'instant, réessaie dans une seconde.";
        }
    }
}
