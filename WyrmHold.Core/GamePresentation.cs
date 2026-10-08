using System.Globalization;

namespace Wyrmhold.Core;

/// <summary>
/// Ce qu'on affiche au-dessus des prix d'un jeu dans la page Boutiques :
/// jaquette, studio, date de sortie, genres, note Steam et description.
/// </summary>
public class GamePresentation
{
    public string? CoverUrl { get; set; }
    public string Developers { get; set; } = "";
    public string? ReleaseDate { get; set; }
    public List<string> Tags { get; set; } = new List<string>();
    public int? SteamScore { get; set; }
    public int? SteamReviewCount { get; set; }

    // Description courte, en français, venue de la boutique Steam (null si le jeu n'est pas sur Steam).
    public string? Description { get; set; }

    // « Supergiant Games · sorti le 25/09/2025 »
    public string InfoLine => string.Join(" · ",
        new[] { Developers, ReleaseDateText }.Where(text => text.Length > 0));

    private string ReleaseDateText =>
        DateTime.TryParseExact(ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
            ? $"sorti le {date:dd/MM/yyyy}"
            : "";

    // Les 6 premiers tags suffisent : au-delà, la ligne devient illisible.
    public string TagsLine => string.Join(" · ", Tags.Take(6));

    // « 👍 96 % d'avis positifs sur Steam (12 345 avis) »
    public string ReviewLine => SteamScore == null
        ? ""
        : SteamReviewCount == null
            ? $"👍 {SteamScore} % d'avis positifs sur Steam"
            : $"👍 {SteamScore} % d'avis positifs sur Steam ({SteamReviewCount.Value.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"))} avis)";
}

/// <summary>
/// Prépare la présentation d'un jeu en combinant deux sources :
/// 1. IsThereAnyDeal (/games/info/v2) : jaquette, studio, date, tags, note, numéro Steam ;
/// 2. la boutique Steam : la description courte en français, grâce au numéro Steam.
/// Chaque présentation est gardée en mémoire : recliquer sur un jeu ne relance aucune requête.
/// </summary>
public class GamePresentationService
{
    private readonly IsThereAnyDealApi _itad;
    private readonly Dictionary<string, GamePresentation> _cache = new Dictionary<string, GamePresentation>();

    public GamePresentationService(IsThereAnyDealApi itad)
    {
        _itad = itad;
    }

    /// <summary>La présentation d'un jeu (identifiant IsThereAnyDeal), ou null si IsThereAnyDeal ne le connaît pas.</summary>
    public async Task<GamePresentation?> GetAsync(string itadId)
    {
        if (_cache.TryGetValue(itadId, out GamePresentation? cached))
        {
            return cached;
        }

        ItadGameInfo? info = await _itad.GetGameInfoAsync(itadId);

        if (info == null)
        {
            return null;
        }

        ItadReview? steamReview = info.Reviews.FirstOrDefault(review => review.Source == "Steam");

        var presentation = new GamePresentation
        {
            CoverUrl = info.Assets?.Boxart ?? info.Assets?.Banner300,
            Developers = string.Join(", ", info.Developers.Select(developer => developer.Name)),
            ReleaseDate = info.ReleaseDate,
            Tags = info.Tags,
            SteamScore = steamReview?.Score,
            SteamReviewCount = steamReview?.Count
        };

        // La description est un bonus : si Steam ne répond pas, on affiche le reste quand même.
        if (info.AppId is int appId)
        {
            try
            {
                presentation.Description = await SteamStoreApi.GetShortDescriptionAsync(appId);
            }
            catch (Exception ex)
            {
                Logger.Log($"Description Steam indisponible pour {info.Title} (appid {appId}) : {ex.Message}");
            }
        }

        _cache[itadId] = presentation;
        return presentation;
    }
}
