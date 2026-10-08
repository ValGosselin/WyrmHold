using System.Text.Json;

namespace Wyrmhold.Core;

/// <summary>
/// Ton compte IsThereAnyDeal relié à Wyrmhold (facultatif).
/// Les jetons sont gardés chiffrés avec DPAPI (SecureStore), comme ceux de GOG : seul ton compte
/// Windows peut les relire.
/// </summary>
public class ItadAccount
{
    private const string StoreName = "itad";

    private readonly string _clientId;

    public ItadAccount(string clientId)
    {
        _clientId = clientId;
    }

    // Le client ID est dans secrets.json : sans lui, impossible de se connecter.
    public bool IsConfigured => !string.IsNullOrEmpty(_clientId);

    public bool IsConnected => SecureStore.Exists(StoreName);

    /// <summary>Termine la connexion : échange le code reçu contre des jetons et les enregistre.</summary>
    public async Task ConnectAsync(string code, string codeVerifier)
    {
        ItadTokens tokens = await ItadAuth.ExchangeCodeAsync(_clientId, code, codeVerifier);
        Save(tokens);
    }

    public void Disconnect()
    {
        SecureStore.Delete(StoreName);
    }

    /// <summary>
    /// Un jeton d'accès valide : celui qu'on a, ou un nouveau demandé avec le jeton de renouvellement
    /// s'il expire dans moins d'une minute.
    /// </summary>
    public async Task<string> GetAccessTokenAsync()
    {
        ItadTokens tokens = Load()
            ?? throw new InvalidOperationException("Compte IsThereAnyDeal non connecté.");

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (tokens.ExpiresUnix - 60 > now)
        {
            return tokens.AccessToken;
        }

        if (string.IsNullOrEmpty(tokens.RefreshToken))
        {
            throw new InvalidOperationException("Session IsThereAnyDeal expirée : reconnecte-toi dans l'onglet Comptes.");
        }

        ItadTokens fresh = await ItadAuth.RefreshAsync(_clientId, tokens.RefreshToken);

        // Si le site ne renvoie pas de nouveau jeton de renouvellement, on garde l'ancien.
        // « with » crée une copie du record avec seulement cette valeur changée.
        if (string.IsNullOrEmpty(fresh.RefreshToken))
        {
            fresh = fresh with { RefreshToken = tokens.RefreshToken };
        }

        Save(fresh);
        return fresh.AccessToken;
    }

    private static void Save(ItadTokens tokens)
    {
        SecureStore.Save(StoreName, JsonSerializer.Serialize(tokens));
    }

    private static ItadTokens? Load()
    {
        string? json = SecureStore.Load(StoreName);

        if (json == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ItadTokens>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
