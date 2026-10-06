using System.Windows;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

public partial class UbisoftLoginWindow : Window
{
    private bool _isClosed;

    public UbisoftCapture? Capture { get; private set; }

    public UbisoftLoginWindow()
    {
        InitializeComponent();
        Closed += (sender, e) => _isClosed = true;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder(Platform.Ubisoft));

            await Browser.EnsureCoreWebView2Async(environment);

            UbisoftPageCapture pageCapture = new UbisoftPageCapture(Browser.CoreWebView2);
            Browser.CoreWebView2.Navigate(UbisoftPageCapture.GamesActivityUrl);

            UbisoftCapture? capture = await pageCapture.WaitForDataAsync(TimeSpan.FromMinutes(10));

            if (_isClosed || capture is null)
            {
                return;
            }

            Capture = capture;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            if (_isClosed)
            {
                return;
            }

            Logger.Log($"Connexion Ubisoft impossible : {ex.Message}");
            MessageBox.Show("Impossible d'ouvrir la connexion à Ubisoft. Les détails sont dans le journal.", "Wyrmhold");
            Close();
        }
    }
}