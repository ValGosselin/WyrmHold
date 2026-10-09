using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Fenêtre « À propos » : version, lien vers le dépôt, mentions (marques, sources des données) et licence.
/// </summary>
public partial class AboutWindow : Window
{
    private readonly AppSettings _settings;

    public AboutWindow(AppSettings settings)
    {
        InitializeComponent();

        _settings = settings;
        VersionText.Text = $"Version {AppInfo.Version}";
        RepositoryLink.NavigateUri = new Uri(AppInfo.RepositoryUrl);
        RepositoryLinkText.Text = AppInfo.RepositoryUrl.Replace("https://", "");
    }

    // Dans une fenêtre WPF, un Hyperlink n'ouvre rien tout seul : on passe l'adresse au navigateur par défaut.
    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Log($"Ouverture de {e.Uri} impossible : {ex.Message}");
        }

        e.Handled = true;
    }

    private void ReportBugButton_Click(object sender, RoutedEventArgs e)
    {
        // « À propos » se ferme d'abord : sinon le rapport s'ouvrirait derrière elle.
        // Owner = la fenêtre principale, qui reste ouverte.
        Window? mainWindow = Owner;
        Close();
        new BugReportWindow(_settings) { Owner = mainWindow }.Show();
    }
}
