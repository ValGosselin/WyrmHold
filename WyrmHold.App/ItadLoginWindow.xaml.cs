using System.Web;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Fenêtre de connexion à IsThereAnyDeal : affiche la page officielle du site, puis récupère le code
/// de connexion quand le site redirige vers ItadAuth.RedirectUri. Aucun mot de passe ne passe par Wyrmhold.
/// </summary>
public partial class ItadLoginWindow : Window
{
    // Valeur au hasard que le site doit nous renvoyer à l'identique (voir ItadAuth.CreateState).
    private readonly string _state = ItadAuth.CreateState();

    // Le secret PKCE : MainWindow le passe ensuite à ItadAccount.ConnectAsync avec le code.
    public string CodeVerifier { get; } = ItadAuth.CreateCodeVerifier();

    public string? LoginCode { get; private set; }

    public ItadLoginWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        string clientId = Secrets.Load().IsThereAnyDealClientId;

        if (string.IsNullOrEmpty(clientId))
        {
            MessageBox.Show("IsThereAnyDealClientId est absent de secrets.json.", "Wyrmhold");
            Close();
            return;
        }

        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder("IsThereAnyDeal"));

            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.NavigationStarting += Browser_NavigationStarting;

            string codeChallenge = ItadAuth.CreateCodeChallenge(CodeVerifier);
            Browser.CoreWebView2.Navigate(ItadAuth.BuildLoginUrl(clientId, codeChallenge, _state));
        }
        catch (Exception ex)
        {
            Logger.Log($"Connexion IsThereAnyDeal impossible : {ex.Message}");
            MessageBox.Show("Impossible d'ouvrir la connexion à IsThereAnyDeal. Les détails sont dans le journal.", "Wyrmhold");
            Close();
        }
    }

    private void Browser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (LoginCode is not null || !e.Uri.StartsWith(ItadAuth.RedirectUri, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // On empêche le navigateur d'aller sur l'adresse de retour : elle n'existe pas, seuls ses paramètres comptent.
        e.Cancel = true;

        var query = HttpUtility.ParseQueryString(new Uri(e.Uri).Query);

        // Si tu refuses l'accès sur le site, il revient avec « error=access_denied » au lieu d'un code.
        string? error = query["error"];
        if (!string.IsNullOrEmpty(error))
        {
            Logger.Log($"Connexion IsThereAnyDeal refusée ou annulée : {error}");
            DialogResult = false;
            return;
        }

        if (query["state"] != _state)
        {
            Logger.Log("Connexion IsThereAnyDeal : réponse ignorée, elle ne correspond pas à notre demande (state).");
            DialogResult = false;
            return;
        }

        string? code = query["code"];

        if (string.IsNullOrEmpty(code))
        {
            Logger.Log("Connexion IsThereAnyDeal terminée, mais aucun code reçu.");
            DialogResult = false;
            return;
        }

        LoginCode = code;
        DialogResult = true;
    }
}
