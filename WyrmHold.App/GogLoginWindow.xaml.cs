using System.Web;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

public partial class GogLoginWindow : Window
{
    public string? LoginCode { get; private set; }

    public GogLoginWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder(Platform.Gog));

            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.NavigationStarting += Browser_NavigationStarting;
            Browser.CoreWebView2.Navigate(GogApi.LoginUrl);
        }
        catch (Exception ex)
        {
            Logger.Log($"Connexion GOG impossible : {ex.Message}");
            MessageBox.Show("Impossible d'ouvrir la connexion à GOG. Les détails sont dans le journal.", "Wyrmhold");
            Close();
        }
    }

    private void Browser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (LoginCode is not null || !e.Uri.StartsWith(GogApi.LoginSuccessUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;

        string? code = HttpUtility.ParseQueryString(new Uri(e.Uri).Query)["code"];

        if (string.IsNullOrEmpty(code))
        {
            Logger.Log("Connexion GOG terminée, mais aucun code reçu.");
            return;
        }

        LoginCode = code;
        DialogResult = true;
    }
}