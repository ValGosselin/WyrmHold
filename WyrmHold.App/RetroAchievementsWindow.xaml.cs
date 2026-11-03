using System.Windows;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// La connexion du compte RetroAchievements (Comptes → RetroAchievements) : le pseudo et la clé
/// sont vérifiés par un vrai appel, et enregistrés (chiffrés) seulement s'ils sont acceptés.
/// </summary>
public partial class RetroAchievementsWindow : Window
{
    // L'adresse donnée par la documentation de l'API pour trouver sa clé.
    private const string SettingsUrl = "https://retroachievements.org/controlpanel.php";

    public RetroAchievementsWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UserBox.Focus();
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        BrowserHelper.Open(SettingsUrl);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        ResultText.Text = "Vérification en cours…";

        try
        {
            (KeyTestResult result, RetroAchievementsProfile? profile) =
                await ApiKeyTester.TestRetroAchievementsAsync(UserBox.Text, ApiKeyBox.Text);

            ResultText.Text = result.Message;

            if (profile is null)
            {
                return;
            }

            Secrets keys = Secrets.Current;
            keys.RetroAchievementsUser = profile.User;
            keys.RetroAchievementsApiKey = ApiKeyBox.Text;
            keys.RetroAchievementsUlid = profile.Ulid ?? "";
            keys.Save();

            DialogResult = true;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
}
