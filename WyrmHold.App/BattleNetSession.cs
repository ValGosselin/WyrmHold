using Microsoft.Web.WebView2.Core;
using Wyrmhold.Core;

namespace WyrmHold.App;

public enum BattleNetFetchStatus
{
    Success,
    NotLoggedIn,
    NoResponse
}

public record BattleNetFetchResult(BattleNetFetchStatus Status, string? Json);

/// <summary>
/// Récupère la liste des jeux du compte Battle.net dans un navigateur WebView2,
/// en deux étapes : ouvrir l'espace compte (connexion si besoin), puis demander la liste.
/// </summary>
public static class BattleNetSession
{
    private const string AccountHost = "account.battle.net";

    public static async Task<BattleNetFetchResult> FetchGamesAsync(CoreWebView2 webView, TimeSpan loginTimeout)
    {
        // Étape 1 : ouvrir l'espace compte. Si tu n'es pas connecté, Battle.net affiche
        // sa page de connexion ; on attend d'arriver sur une vraie page de l'espace compte.
        TaskCompletionSource<bool> accountPageLoaded = new TaskCompletionSource<bool>();

        webView.NavigationCompleted += (sender, e) =>
        {
            if (e.IsSuccess && IsAccountPage(webView.Source))
            {
                accountPageLoaded.TrySetResult(true);
            }
        };

        webView.Navigate(BattleNetAccount.AccountSettingsUrl);

        Task finished = await Task.WhenAny(accountPageLoaded.Task, Task.Delay(loginTimeout));

        if (finished != accountPageLoaded.Task)
        {
            return new BattleNetFetchResult(BattleNetFetchStatus.NotLoggedIn, null);
        }

        // Étape 2 : la session est ouverte, on demande la liste des jeux.
        JsonResponseCapture capture = new JsonResponseCapture(webView, BattleNetAccount.GamesUrl);
        webView.Navigate(BattleNetAccount.GamesUrl);

        string? json = await capture.WaitForJsonAsync(TimeSpan.FromSeconds(20));

        if (json is not null)
        {
            await KeepSessionCookiesAsync(webView);
        }

        return json is null
            ? new BattleNetFetchResult(BattleNetFetchStatus.NoResponse, null)
            : new BattleNetFetchResult(BattleNetFetchStatus.Success, json);
    }

    /// <summary>
    /// Battle.net pose des cookies « de session » (BA-tassadar-cl, cl…), jetés quand le navigateur se ferme : le site
    /// redemandait alors le mot de passe, 15 secondes après un redémarrage (vérifié le 2 novembre 2026).
    /// On leur donne une date de fin pour qu'ils restent. Ils ne servent qu'à ce profil, supprimé par « Se déconnecter ».
    /// </summary>
    private static async Task KeepSessionCookiesAsync(CoreWebView2 webView)
    {
        // null = tous les cookies du profil : ce dossier WebView2 ne sert qu'à Battle.net.
        foreach (CoreWebView2Cookie cookie in await webView.CookieManager.GetCookiesAsync(null))
        {
            if (cookie.IsSession)
            {
                cookie.Expires = DateTime.Now.AddYears(1);   // comme BA-tassadar, le cookie principal
                webView.CookieManager.AddOrUpdateCookie(cookie);
            }
        }
    }

    /// <summary>
    /// Une page de l'espace compte (l'aperçu, par exemple), et pas une étape de la connexion.
    /// </summary>
    private static bool IsAccountPage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || !uri.Host.Equals(AccountHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string path = uri.AbsolutePath;

        return !path.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/oauth2", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/api", StringComparison.OrdinalIgnoreCase);
    }
}