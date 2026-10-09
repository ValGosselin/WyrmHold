using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// L'assistant des clés API : au premier lancement, puis depuis Réglages → « Modifier les clés… ».
/// Les champs sont remplis avec les clés actuelles ; rien n'est enregistré avant « Terminer »
/// (fermer la fenêtre avec la croix = aucun changement).
/// </summary>
public partial class SetupWizardWindow : Window
{
    private readonly StackPanel[] _pages;
    private int _pageIndex;

    // fromSettings : ouvert depuis Réglages → on saute la page de bienvenue.
    public SetupWizardWindow(bool fromSettings = false)
    {
        InitializeComponent();

        _pages = new[] { WelcomePage, SteamPage, SteamGridDbPage, ItadPage, DonePage };

        Secrets keys = Secrets.Current;
        SteamIdBox.Text = keys.SteamId;
        SteamKeyBox.Text = keys.SteamApiKey;
        SteamGridDbKeyBox.Text = keys.SteamGridDbApiKey;
        ItadKeyBox.Text = keys.IsThereAnyDealApiKey;
        ItadClientIdBox.Text = keys.IsThereAnyDealClientId;

        if (fromSettings)
        {
            Title = "Clés API — Wyrmhold";
        }

        ShowPage(fromSettings ? 1 : 0);
    }

    // ===================== Navigation =====================

    private void ShowPage(int index)
    {
        _pageIndex = index;

        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }

        StepText.Text = $"Étape {index + 1} sur {_pages.Length}";
        BackButton.Visibility = index == 0 ? Visibility.Hidden : Visibility.Visible;

        if (_pages[index] == DonePage)
        {
            SummaryText.Text = BuildSummary();
        }

        UpdateNextButton();
    }

    // Le bouton dit « Passer » quand la page a un champ de clé vide : on comprend qu'on peut sauter l'étape.
    private void UpdateNextButton()
    {
        StackPanel page = _pages[_pageIndex];

        bool isEmptyKeyPage =
            (page == SteamPage && SteamKeyBox.Text.Trim().Length == 0)
            || (page == SteamGridDbPage && SteamGridDbKeyBox.Text.Trim().Length == 0)
            || (page == ItadPage && ItadKeyBox.Text.Trim().Length == 0);

        NextButton.Content = page == DonePage ? "Terminer" : isEmptyKeyPage ? "Passer →" : "Suivant →";
    }

    private void KeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Appelé aussi pendant InitializeComponent, avant que _pages existe.
        if (_pages != null)
        {
            UpdateNextButton();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(Math.Max(_pageIndex - 1, 0));
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pages[_pageIndex] != DonePage)
        {
            ShowPage(_pageIndex + 1);
            return;
        }

        // Terminer : on copie les champs dans les clés partagées, puis on enregistre (chiffré).
        // Save() prévient le reste de l'appli (onglet Boutiques…).
        Secrets keys = Secrets.Current;
        keys.SteamId = SteamIdBox.Text;
        keys.SteamApiKey = SteamKeyBox.Text;
        keys.SteamGridDbApiKey = SteamGridDbKeyBox.Text;
        keys.IsThereAnyDealApiKey = ItadKeyBox.Text;
        keys.IsThereAnyDealClientId = ItadClientIdBox.Text;

        try
        {
            keys.Save();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Enregistrement des clés API impossible : {ex.Message}");
            MessageBox.Show("Impossible d'enregistrer les clés. Les détails sont dans le journal.", "Wyrmhold");
        }
    }

    private string BuildSummary()
    {
        // Masquées : le récapitulatif peut être vu par quelqu'un d'autre (partage d'écran…).
        string Line(string name, string value) =>
            $"{name,-14} {(value.Trim().Length > 0 ? "✔ " + ApiKeyTester.Mask(value.Trim()) : "— non renseignée")}";

        return string.Join("\n",
            Line("Steam", SteamKeyBox.Text),
            Line("SteamID", SteamIdBox.Text),
            Line("SteamGridDB", SteamGridDbKeyBox.Text),
            Line("IsThereAnyDeal", ItadKeyBox.Text));
    }

    // ===================== Boutons des pages =====================

    private void OpenLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
        {
            try
            {
                // Le navigateur habituel de l'utilisateur, où il est peut-être déjà connecté.
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Log($"Ouverture de {url} impossible : {ex.Message}");
            }
        }
    }

    private void SteamLoginButton_Click(object sender, RoutedEventArgs e)
    {
        // La même fenêtre de connexion que pour la famille Steam. On n'en garde QUE l'identifiant :
        // le jeton de session qu'elle lit aussi est oublié aussitôt.
        var login = new SteamLoginWindow { Owner = this };

        if (login.ShowDialog() == true && !string.IsNullOrEmpty(login.SessionSteamId))
        {
            SteamIdBox.Text = login.SessionSteamId;
            SteamTestText.Text = "✔ Identifiant Steam trouvé.";
        }
    }

    private async void TestSteamButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTestAsync((Button)sender, SteamTestText, () => ApiKeyTester.TestSteamAsync(SteamKeyBox.Text, SteamIdBox.Text));
    }

    private async void TestSteamGridDbButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTestAsync((Button)sender, SteamGridDbTestText, () => ApiKeyTester.TestSteamGridDbAsync(SteamGridDbKeyBox.Text));
    }

    private async void TestItadButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTestAsync((Button)sender, ItadTestText, () => ApiKeyTester.TestIsThereAnyDealAsync(ItadKeyBox.Text));
    }

    // Partie commune aux trois boutons « Tester » : bouton bloqué pendant l'essai, puis le résultat.
    private static async Task RunTestAsync(Button button, TextBlock resultText, Func<Task<KeyTestResult>> test)
    {
        button.IsEnabled = false;
        resultText.Text = "Essai en cours…";

        try
        {
            KeyTestResult result = await test();
            resultText.Text = result.Message;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
