using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

// Les jetons de connexion IsThereAnyDeal. ExpiresUnix : moment où AccessToken ne sera plus accepté.
public record ItadTokens(string AccessToken, string RefreshToken, long ExpiresUnix);

/// <summary>
/// Connexion à un compte IsThereAnyDeal avec OAuth « authorization code » + PKCE
/// (méthode décrite dans la documentation officielle de l'API).
///
/// Le principe de PKCE : avant d'ouvrir la page de connexion, Wyrmhold invente un secret au hasard
/// (le « code verifier ») et n'envoie que son empreinte SHA-256 (le « code challenge »). Quand il échange
/// ensuite le code reçu contre des jetons, il montre le secret d'origine : IsThereAnyDeal vérifie qu'il
/// correspond à l'empreinte. Quelqu'un qui intercepterait le code ne pourrait donc rien en faire.
/// Pas besoin de « client secret » : une appli de bureau ne peut pas le cacher (ton appli est réglée
/// sur « This app will NOT store OAuth secret securely » sur le site).
/// </summary>
public static class ItadAuth
{
    private const string AuthorizeUrl = "https://isthereanydeal.com/oauth/authorize/";
    private const string TokenUrl = "https://isthereanydeal.com/oauth/token/";

    // Adresse de retour après la connexion. Elle doit être ajoutée telle quelle (« Add URL ») sur la page
    // de ton appli IsThereAnyDeal. Elle n'est jamais vraiment ouverte : la fenêtre de connexion l'intercepte.
    public const string RedirectUri = "http://localhost/wyrmhold-itad-callback";

    // Droits demandés : lire et modifier la Waitlist, rien d'autre.
    private const string Scopes = "wait_read wait_write";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Wyrmhold/0.1");
        return client;
    }

    // ----- PKCE -----

    /// <summary>Le secret tiré au hasard (32 octets aléatoires, écrits en Base64 « pour adresse web »).</summary>
    public static string CreateCodeVerifier()
    {
        return Base64Url(RandomNumberGenerator.GetBytes(32));
    }

    /// <summary>L'empreinte SHA-256 du secret : c'est elle qui part dans l'adresse de connexion.</summary>
    public static string CreateCodeChallenge(string codeVerifier)
    {
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
    }

    /// <summary>
    /// Une valeur au hasard que le site nous renvoie telle quelle après la connexion. Si elle ne correspond pas,
    /// la réponse ne vient pas de NOTRE demande de connexion : on l'ignore.
    /// </summary>
    public static string CreateState()
    {
        return Base64Url(RandomNumberGenerator.GetBytes(16));
    }

    // Base64 classique, puis les 3 changements demandés par OAuth : pas de « = », « - » au lieu de « + »,
    // « _ » au lieu de « / » (ces caractères ont un sens spécial dans une adresse web).
    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // ----- Adresses et jetons -----

    public static string BuildLoginUrl(string clientId, string codeChallenge, string state)
    {
        return AuthorizeUrl
            + "?response_type=code"
            + $"&client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
            + $"&scope={Uri.EscapeDataString(Scopes)}"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
            + "&code_challenge_method=S256";
    }

    /// <summary>Échange le code reçu après la connexion contre les jetons.</summary>
    public static Task<ItadTokens> ExchangeCodeAsync(string clientId, string code, string codeVerifier)
    {
        return RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = codeVerifier
        });
    }

    /// <summary>Demande un nouveau jeton d'accès quand l'ancien a expiré, sans te redemander de te connecter.</summary>
    public static Task<ItadTokens> RefreshAsync(string clientId, string refreshToken)
    {
        return RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken
        });
    }

    private static async Task<ItadTokens> RequestTokensAsync(Dictionary<string, string> form)
    {
        // FormUrlEncodedContent : le format d'un formulaire web (grant_type=...&client_id=...),
        // celui qu'attend une adresse de jetons OAuth.
        using var content = new FormUrlEncodedContent(form);
        using HttpResponseMessage response = await Http.PostAsync(TokenUrl, content);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            // On n'écrit PAS le corps de la réponse dans le message : il pourrait contenir un jeton.
            throw new HttpRequestException($"IsThereAnyDeal a refusé la connexion ({(int)response.StatusCode} {response.StatusCode}).");
        }

        ItadTokenResponse? tokens = JsonSerializer.Deserialize<ItadTokenResponse>(body);

        if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken))
        {
            throw new HttpRequestException("Réponse de connexion IsThereAnyDeal inattendue (pas de jeton).");
        }

        // expires_in = durée de validité en secondes. Si elle manque, on suppose 1 heure.
        long lifetime = tokens.ExpiresIn > 0 ? tokens.ExpiresIn : 3600;
        long expiresUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + lifetime;

        return new ItadTokens(tokens.AccessToken, tokens.RefreshToken ?? "", expiresUnix);
    }
}

internal class ItadTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public long ExpiresIn { get; set; }
}
