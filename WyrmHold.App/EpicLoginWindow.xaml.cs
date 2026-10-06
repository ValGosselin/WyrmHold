using System.Windows;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

public partial class EpicLoginWindow : Window
{
    public string? AuthorizationCode { get; private set; }

    public EpicLoginWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder(Platform.Epic));

            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.WebResourceResponseReceived += Browser_WebResourceResponseReceived;
            Browser.CoreWebView2.Navigate(EpicApi.LoginUrl);
        }
        catch (Exception ex)
        {
            Logger.Log($"Connexion Epic Games impossible : {ex.Message}");
            MessageBox.Show("Impossible d'ouvrir la connexion à Epic Games. Les détails sont dans le journal.", "Wyrmhold");
            Close();
        }
    }

    private async void Browser_WebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (AuthorizationCode is not null
            || e.Response.StatusCode != 200
            || !e.Request.Uri.StartsWith(EpicApi.AuthorizationCodeUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? code = await MainWindow.ReadAuthorizationCodeAsync(e.Response);

        if (code is null || AuthorizationCode is not null)
        {
            return;
        }

        AuthorizationCode = code;
        DialogResult = true;
    }
}