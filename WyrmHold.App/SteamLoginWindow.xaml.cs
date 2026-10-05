using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

public partial class SteamLoginWindow : Window
{
    private const string StoreUrl = "https://store.steampowered.com/";
    private const string LoginUrl = "https://store.steampowered.com/login/";

    private bool _loginShown;

    public string? AccessToken { get; private set; }
    public string? SessionSteamId { get; private set; }

    public SteamLoginWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
            null, AppPaths.GetWebViewFolder(Platform.Steam));

            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.NavigationCompleted += Browser_NavigationCompleted;
            Browser.CoreWebView2.Navigate(StoreUrl);
        }
        catch (Exception ex)
        {
            Logger.Log($"Connexion Steam impossible : {ex.Message}");
            MessageBox.Show("Impossible d'ouvrir la connexion à Steam. Les détails sont dans le journal.", "Wyrmhold");
            Close();
        }
    }

    private async void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (AccessToken is not null || Browser.CoreWebView2.Source.Contains("/login"))
        {
            return;
        }

        try
        {
            if (await TryGetTokenAsync())
            {
                return;
            }

            if (!_loginShown)
            {
                _loginShown = true;
                Browser.CoreWebView2.Navigate(LoginUrl);
            }
            else
            {
                Logger.Log("Connexion terminée, mais aucun cookie de session Steam trouvé.");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Lecture du jeton Steam impossible : {ex.Message}");
        }
    }

    private async Task<bool> TryGetTokenAsync()
    {
        List<CoreWebView2Cookie> cookies =
            await Browser.CoreWebView2.CookieManager.GetCookiesAsync(StoreUrl);

        CoreWebView2Cookie? loginCookie = cookies.FirstOrDefault(c => c.Name == "steamLoginSecure");
        if (!SteamWebApi.TryParseLoginCookie(loginCookie?.Value, out string steamId, out string token))
        {
            return false;
        }

        SessionSteamId = steamId;
        AccessToken = token;
        DialogResult = true;
        return true;
    }
}