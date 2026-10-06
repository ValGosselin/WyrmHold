using System.Windows;
using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

public partial class BattleNetLoginWindow : Window
{
    private bool _isClosed;

    public string? GamesJson { get; private set; }

    public BattleNetLoginWindow()
    {
        InitializeComponent();
        Closed += (sender, e) => _isClosed = true;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, AppPaths.GetWebViewFolder(Platform.BattleNet));

            await Browser.EnsureCoreWebView2Async(environment);

            BattleNetFetchResult result = await BattleNetSession.FetchGamesAsync(
                Browser.CoreWebView2, TimeSpan.FromMinutes(10));

            if (_isClosed)
            {
                return;
            }

            if (result.Status != BattleNetFetchStatus.Success)
            {
                Logger.Log($"Connexion Battle.net : liste des jeux non reçue ({result.Status}).");
                MessageBox.Show("Battle.net n'a pas envoyé la liste de tes jeux. Réessaie dans quelques instants.", "Wyrmhold");
                Close();
                return;
            }

            GamesJson = result.Json;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            if (_isClosed)
            {
                return;
            }

            Logger.Log($"Connexion Battle.net impossible : {ex.Message}");
            MessageBox.Show("Impossible d'ouvrir la connexion à Battle.net. Les détails sont dans le journal.", "Wyrmhold");
            Close();
        }
    }
}