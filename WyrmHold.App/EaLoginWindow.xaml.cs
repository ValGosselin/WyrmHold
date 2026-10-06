using System.Windows;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

public partial class EaLoginWindow : Window
{
    private bool _isClosed;

    public string? OrderHistoryJson { get; private set; }

    public EaLoginWindow()
    {
        InitializeComponent();
        Closed += (sender, e) => _isClosed = true;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder(Platform.Ea));

            await Browser.EnsureCoreWebView2Async(environment);

            EaOrderHistoryCapture capture = new EaOrderHistoryCapture(Browser.CoreWebView2);
            Browser.CoreWebView2.Navigate(EaOrderHistory.Url);

            string? json = await capture.WaitForJsonAsync(TimeSpan.FromMinutes(10));

            if (_isClosed || json is null)
            {
                return;
            }

            OrderHistoryJson = json;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            if (_isClosed)
            {
                return;
            }

            Logger.Log($"Connexion EA impossible : {ex.Message}");
            MessageBox.Show("Impossible d'ouvrir la connexion à EA. Les détails sont dans le journal.", "Wyrmhold");
            Close();
        }
    }
}