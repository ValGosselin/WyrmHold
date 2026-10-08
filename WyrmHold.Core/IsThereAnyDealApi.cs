using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Wyrmhold.Core;

/// <summary>
/// Accès à IsThereAnyDeal (API v2) : recherche d'un jeu, puis ses prix dans un pays donné.
/// Demande une clé API (secrets.json → IsThereAnyDealApiKey).
/// Limite affichée sur ton compte : 100 requêtes par tranche de 5 minutes.
/// </summary>
public class IsThereAnyDealApi
{
    private const string BaseUrl = "https://api.isthereanydeal.com/";

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly string _apiKey;

    public IsThereAnyDealApi(string apiKey)
    {
        _apiKey = apiKey;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Wyrmhold/0.1");
        return client;
    }

    /// <summary>Cherche les jeux qui correspondent au titre (20 résultats au plus).</summary>
    public async Task<List<ItadSearchResult>> SearchAsync(string title)
    {
        string cleanedTitle = NameTools.CleanForSearch(title);
        string url = BaseUrl + "games/search/v1?title=" + Uri.EscapeDataString(cleanedTitle) + "&results=20";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        string json = await SendAsync(request);

        return JsonSerializer.Deserialize<List<ItadSearchResult>>(json)
            ?? new List<ItadSearchResult>();
    }

    /// <summary>
    /// Récupère les prix d'un ou plusieurs jeux (200 au plus par appel) dans un pays.
    /// Un seul appel suffit pour toute une liste : c'est ce qui permettra de vérifier
    /// tous les jeux suivis sans gaspiller la limite de requêtes.
    /// </summary>
    public async Task<List<ItadGamePrices>> GetPricesAsync(IEnumerable<string> gameIds, string country = "FR")
    {
        string url = BaseUrl + "games/prices/v3?country=" + country;

        // Cette route est un POST : la liste des identifiants voyage dans le corps de la requête,
        // en JSON (ex. ["018d937f-…", "01849783-…"]), et pas dans l'adresse.
        string body = JsonSerializer.Serialize(gameIds.ToList());

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        string json = await SendAsync(request);

        return JsonSerializer.Deserialize<List<ItadGamePrices>>(json)
            ?? new List<ItadGamePrices>();
    }

    /// <summary>La fiche d'un jeu : jaquette, studio, date de sortie, tags, notes, numéro Steam.</summary>
    public async Task<ItadGameInfo?> GetGameInfoAsync(string gameId)
    {
        // Un seul jeu par appel : cette route n'accepte pas de liste.
        string url = BaseUrl + "games/info/v2?id=" + Uri.EscapeDataString(gameId);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        string json = await SendAsync(request);

        return JsonSerializer.Deserialize<ItadGameInfo>(json);
    }

    /// <summary>
    /// Les meilleures promos du moment (une offre par jeu, la meilleure), page par page.
    /// sort : "" = l'ordre par défaut du site, "-cut" = plus forte réduction, "price" = prix le plus bas
    /// (les seules valeurs données en exemple par la documentation).
    /// </summary>
    public async Task<ItadDealsPage> GetDealsAsync(int offset, int limit, string sort, string country = "FR")
    {
        string url = BaseUrl + "deals/v2"
            + $"?country={country}&offset={offset}&limit={limit}";

        if (sort.Length > 0)
        {
            url += "&sort=" + Uri.EscapeDataString(sort);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        string json = await SendAsync(request);

        return JsonSerializer.Deserialize<ItadDealsPage>(json) ?? new ItadDealsPage();
    }

    /// <summary>
    /// Les promos du moment qui portent certains tags (ex. « RPG », « Strategy »).
    /// matchAll = true : filtre « tags » ; false : filtre « tagsUnion ».
    /// La documentation n'explique pas la différence entre les deux : l'essai en console sert à la trouver.
    /// </summary>
    public async Task<ItadDealsPage> GetDealsByTagsAsync(IEnumerable<string> tags, bool matchAll,
        int offset, int limit, string sort, string country = "FR",
        Dictionary<string, object>? extraFilter = null)
    {
        // Le filtre : les tags, plus d'éventuels critères en plus (type, avis Steam…).
        var filter = new Dictionary<string, object>(extraFilter ?? new Dictionary<string, object>());
        List<string> tagList = tags.ToList();

        if (tagList.Count > 0)
        {
            filter[matchAll ? "tags" : "tagsUnion"] = tagList;
        }

        // Version POST de /deals/v2 : tous les réglages voyagent dans le corps, en JSON,
        // y compris le filtre (plus simple que de le glisser dans l'adresse).
        var body = new Dictionary<string, object>
        {
            ["country"] = country,
            ["offset"] = offset,
            ["limit"] = limit,
            ["filter"] = filter
        };

        if (sort.Length > 0)
        {
            body["sort"] = sort;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "deals/v2");
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        string json = await SendAsync(request);

        return JsonSerializer.Deserialize<ItadDealsPage>(json) ?? new ItadDealsPage();
    }

    // ===================== Waitlist (compte relié avec OAuth) =====================
    // Ces routes ne demandent pas la clé API mais le jeton de ton compte (scopes wait_read / wait_write).

    /// <summary>Les jeux de ta Waitlist sur le site.</summary>
    public async Task<List<ItadWaitlistGame>> GetWaitlistAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "waitlist/games/v1");
        string json = await SendWithTokenAsync(request, accessToken);

        return JsonSerializer.Deserialize<List<ItadWaitlistGame>>(json)
            ?? new List<ItadWaitlistGame>();
    }

    public Task AddToWaitlistAsync(string accessToken, IEnumerable<string> gameIds)
    {
        return SendGameIdsAsync(HttpMethod.Put, accessToken, gameIds);
    }

    public Task RemoveFromWaitlistAsync(string accessToken, IEnumerable<string> gameIds)
    {
        return SendGameIdsAsync(HttpMethod.Delete, accessToken, gameIds);
    }

    /// <summary>PUT (ajout) ou DELETE (retrait) : la liste des identifiants voyage en JSON dans le corps.</summary>
    private async Task SendGameIdsAsync(HttpMethod method, string accessToken, IEnumerable<string> gameIds)
    {
        List<string> ids = gameIds.ToList();

        // La documentation exige au moins un identifiant : une liste vide serait refusée.
        if (ids.Count == 0)
        {
            return;
        }

        using var request = new HttpRequestMessage(method, BaseUrl + "waitlist/games/v1");
        request.Content = new StringContent(JsonSerializer.Serialize(ids), Encoding.UTF8, "application/json");

        await SendWithTokenAsync(request, accessToken);
    }

    /// <summary>Envoie la requête avec le jeton de ton compte (en-tête « Authorization: Bearer … »).</summary>
    private static Task<string> SendWithTokenAsync(HttpRequestMessage request, string accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return ReadResponseAsync(request);
    }

    // ===================== Envoi commun =====================

    /// <summary>Envoie la requête avec la clé, et renvoie le JSON (ou une erreur lisible).</summary>
    private Task<string> SendAsync(HttpRequestMessage request)
    {
        // La clé part dans un en-tête plutôt que dans l'adresse (?key=…) :
        // une adresse peut finir dans un journal ou un message d'erreur, pas un en-tête.
        request.Headers.Add("ITAD-API-Key", _apiKey);
        return ReadResponseAsync(request);
    }

    /// <summary>Partie commune aux deux façons de s'identifier : envoi, puis lecture de la réponse.</summary>
    private static async Task<string> ReadResponseAsync(HttpRequestMessage request)
    {
        using HttpResponseMessage response = await Http.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            string excerpt = body.Length > 300 ? body.Substring(0, 300) + "…" : body;
            throw new HttpRequestException($"IsThereAnyDeal a répondu {(int)response.StatusCode} ({response.StatusCode}) : {excerpt}");
        }

        return body;
    }
}
