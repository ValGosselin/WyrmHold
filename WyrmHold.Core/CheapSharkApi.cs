using System.Text.Json;

namespace Wyrmhold.Core;

/// <summary>
/// Accès à CheapShark : gratuit, sans clé ni compte. Sert de premier essai du comparateur de prix.
/// Limite : les prix sont en dollars US (IsThereAnyDeal prendra le relais pour les euros).
/// </summary>
public static class CheapSharkApi
{
    private const string BaseUrl = "https://www.cheapshark.com/api/1.0/";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();

        // Un navigateur se présente toujours (« je suis Chrome, version… ») grâce à l'en-tête User-Agent.
        // HttpClient n'en envoie aucun par défaut, et CheapShark refuse ces requêtes anonymes (erreur 400).
        // On présente donc Wyrmhold par son nom.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Wyrmhold/0.1");

        return client;
    }

    /// <summary>
    /// Télécharge une réponse. Contrairement à GetStringAsync, en cas d'erreur on garde
    /// le texte renvoyé par le serveur : il explique souvent pourquoi la requête est refusée.
    /// </summary>
    private static async Task<string> GetJsonAsync(string url)
    {
        using HttpResponseMessage response = await Http.GetAsync(url);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            string excerpt = body.Length > 300 ? body.Substring(0, 300) + "…" : body;
            throw new HttpRequestException($"CheapShark a répondu {(int)response.StatusCode} ({response.StatusCode}) : {excerpt}");
        }

        return body;
    }

    /// <summary>Cherche les jeux dont le titre contient le texte donné (20 résultats au plus).</summary>
    public static async Task<List<CheapSharkSearchResult>> SearchAsync(string title)
    {
        string cleanedTitle = NameTools.CleanForSearch(title);
        string url = BaseUrl + "games?title=" + Uri.EscapeDataString(cleanedTitle) + "&limit=20";

        string json = await GetJsonAsync(url);

        return JsonSerializer.Deserialize<List<CheapSharkSearchResult>>(json)
            ?? new List<CheapSharkSearchResult>();
    }

    /// <summary>Récupère la fiche d'un jeu trouvé par SearchAsync : toutes ses offres et son plus bas historique.</summary>
    public static async Task<CheapSharkGameDetails?> GetGameAsync(string gameId)
    {
        string url = BaseUrl + "games?id=" + Uri.EscapeDataString(gameId);

        string json = await GetJsonAsync(url);

        return JsonSerializer.Deserialize<CheapSharkGameDetails>(json);
    }

    /// <summary>Renvoie un dictionnaire « numéro de boutique → nom » (ex. "1" → "Steam").</summary>
    public static async Task<Dictionary<string, string>> GetStoreNamesAsync()
    {
        string json = await GetJsonAsync(BaseUrl + "stores");

        List<CheapSharkStore> stores = JsonSerializer.Deserialize<List<CheapSharkStore>>(json)
            ?? new List<CheapSharkStore>();

        return stores.ToDictionary(store => store.StoreId, store => store.StoreName);
    }

    /// <summary>L'adresse qui redirige vers la page de l'offre sur la boutique.</summary>
    public static string GetDealUrl(string dealId)
    {
        // Le dealID arrive déjà encodé pour une adresse web (il contient par ex. « %3D ») :
        // on ne le repasse pas dans Uri.EscapeDataString, sinon il serait encodé deux fois.
        return "https://www.cheapshark.com/redirect?dealID=" + dealId;
    }
}
