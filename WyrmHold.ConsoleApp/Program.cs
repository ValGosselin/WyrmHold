using Wyrmhold.Core;

// Phase 6 — essais IsThereAnyDeal, prix en France (euros).
// 1 = comparateur de prix (chercher un jeu, voir ses offres)
// 2 = promos filtrées par tags (préparation des recommandations par genre)
// (Le premier essai avec CheapShark reste disponible dans CheapSharkApi.)

try
{
    Secrets secrets = Secrets.Load();
    var itad = new IsThereAnyDealApi(secrets.IsThereAnyDealApiKey);

    if (!itad.IsConfigured)
    {
        Console.WriteLine("Clé IsThereAnyDealApiKey absente de secrets.json.");
        return;
    }

    // Le mode peut aussi être donné au lancement : « dotnet run -- 2 ».
    string mode;

    if (args.Length > 0)
    {
        mode = args[0];
    }
    else
    {
        Console.Write("1 = comparateur de prix, 2 = promos par tags : ");
        mode = Console.ReadLine() ?? "";
    }

    if (mode.Trim() == "6")
    {
        await TestRecommendationsAsync(itad);
    }
    else if (mode.Trim() == "5")
    {
        await TestSortValuesAsync(itad);
    }
    else if (mode.Trim() == "4")
    {
        // « dotnet run -- 4 Hades » : la présentation du premier jeu trouvé.
        await ShowPresentationAsync(itad, string.Join(" ", args.Skip(1)));
    }
    else if (mode.Trim() == "3")
    {
        await TestQualityFiltersAsync(itad);
    }
    else if (mode.Trim() == "2")
    {
        await TestDealsByTagsAsync(itad, args.Skip(1).ToArray());
    }
    else
    {
        await ComparePricesAsync(itad);
    }

    Console.WriteLine();
    Console.WriteLine("Prix fournis par IsThereAnyDeal.");
}
catch (Exception ex)
{
    // Pas de réseau, clé refusée, limite dépassée… on affiche l'erreur au lieu de planter.
    Console.WriteLine($"Erreur : {ex.Message}");
}

static async Task ComparePricesAsync(IsThereAnyDealApi itad)
{
    Console.Write("Jeu à chercher : ");
    string title = Console.ReadLine() ?? "";

    List<ItadSearchResult> results = await itad.SearchAsync(title);

    if (results.Count == 0)
    {
        Console.WriteLine("Aucun jeu trouvé.");
        return;
    }

    for (int i = 0; i < results.Count; i++)
    {
        Console.WriteLine($"{i + 1}. {results[i].Title} [{results[i].Type}]");
    }

    Console.Write("Numéro du jeu : ");
    if (!int.TryParse(Console.ReadLine(), out int choice) || choice < 1 || choice > results.Count)
    {
        Console.WriteLine("Choix invalide.");
        return;
    }

    ItadSearchResult selected = results[choice - 1];

    // On passe une liste d'un seul identifiant : la méthode sait en traiter jusqu'à 200.
    List<ItadGamePrices> prices = await itad.GetPricesAsync(new[] { selected.Id });
    ItadGamePrices? game = prices.FirstOrDefault();

    if (game == null || game.Deals.Count == 0)
    {
        Console.WriteLine("Aucune offre pour ce jeu en France.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"Offres pour {selected.Title} :");

    foreach (ItadDeal deal in game.Deals.OrderBy(deal => deal.Price.Amount))
    {
        string promo = deal.Cut > 0 ? $"-{deal.Cut} %, prix normal {deal.Regular.Amount:0.00} {deal.Regular.Currency}" : "pas de promo";

        Console.WriteLine($"- {deal.Shop.Name,-22} {deal.Price.Amount,7:0.00} {deal.Price.Currency}   ({promo})");
        Console.WriteLine($"  {deal.Url}");
    }

    if (game.HistoryLow?.AllTime != null)
    {
        Console.WriteLine();
        Console.WriteLine($"Plus bas historique : {game.HistoryLow.AllTime.Amount:0.00} {game.HistoryLow.AllTime.Currency}");
    }
}

// Compare le nombre de promos pour deux tags, seuls puis ensemble, avec « tags » et « tagsUnion ».
// Si « tags » donne MOINS de promos que chaque tag seul, il veut dire « tous les tags » (ET).
// Si « tagsUnion » donne PLUS, il veut dire « au moins un des tags » (OU).
// Les deux tags peuvent aussi être donnés au lancement : « dotnet run -- 2 Horror Racing ».
static async Task TestDealsByTagsAsync(IsThereAnyDealApi itad, string[] tagArgs)
{
    string tagA;
    string tagB;

    if (tagArgs.Length >= 2)
    {
        tagA = tagArgs[0];
        tagB = tagArgs[1];
    }
    else
    {
        Console.Write("Premier tag (Entrée = Roguelike) : ");
        tagA = ReadOrDefault("Roguelike");
        Console.Write("Second tag (Entrée = Deckbuilding) : ");
        tagB = ReadOrDefault("Deckbuilding");
    }

    Console.WriteLine();
    await CountAsync(itad, "Sans filtre", null, true);
    await CountAsync(itad, $"tags [{tagA}]", new[] { tagA }, true);
    await CountAsync(itad, $"tags [{tagB}]", new[] { tagB }, true);
    await CountAsync(itad, $"tags [{tagA}, {tagB}]", new[] { tagA, tagB }, true);
    await CountAsync(itad, $"tagsUnion [{tagA}, {tagB}]", new[] { tagA, tagB }, false);
}

// Les filtres de qualité, sous la forme donnée par la spécification de l'API (dist/openapi.yaml) :
// « type » = liste de numéros (1 = jeu, 2 = DLC…) ; « steamPerc » et « steamCount » = { min, max },
// les deux clés obligatoires (null = pas de limite).
static async Task TestQualityFiltersAsync(IsThereAnyDealApi itad)
{
    string[] tags = { "RPG", "Strategy" };

    var tests = new List<(string Label, Dictionary<string, object> Filter)>
    {
        ("Sans critère", new Dictionary<string, object>()),
        ("jeux seulement", new Dictionary<string, object> { ["type"] = new[] { 1 } }),
        // Essais du 8 octobre 2026 : steamPerc avec max = null renvoie 0 promo, il faut écrire max = 100 ;
        // steamCount accepte max = null.
        ("+ 80 % d'avis positifs", new Dictionary<string, object>
        {
            ["type"] = new[] { 1 },
            ["steamPerc"] = new { min = 80, max = 100 }
        }),
        ("+ 500 avis au moins", new Dictionary<string, object>
        {
            ["type"] = new[] { 1 },
            ["steamPerc"] = new { min = 80, max = 100 },
            ["steamCount"] = new { min = 500, max = (int?)null }
        }),
    };

    foreach ((string label, Dictionary<string, object> filter) in tests)
    {
        try
        {
            ItadDealsPage page = await itad.GetDealsByTagsAsync(tags, false, 0, 200, "", extraFilter: filter);
            string count = page.HasMore ? "plus de 200" : page.List.Count.ToString();
            string examples = string.Join(" | ", page.List.Take(5).Select(item => $"{item.Title} [{item.Type}]"));

            Console.WriteLine($"{label,-24} {count,12}   ex. : {examples}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"{label,-24} refusé : {ex.Message}");
        }
    }
}

// La documentation dit seulement que « sort » prend les mêmes valeurs que la liste des promos du site,
// sans les lister. On essaie des valeurs probables : refusée (erreur), ignorée (même ordre que sans tri) ou acceptée.
static async Task TestSortValuesAsync(IsThereAnyDealApi itad)
{
    string[] tags = { "RPG", "Strategy" };
    var quality = new Dictionary<string, object>
    {
        ["type"] = new[] { 1 },
        ["steamPerc"] = new { min = 80, max = 100 },
        ["steamCount"] = new { min = 2000, max = (int?)null }
    };

    string[] sorts =
    {
        "", "-cut", "price", "rank", "-rank", "trending", "-trending", "waitlisted", "-waitlisted",
        "collected", "-collected", "steam", "-steam", "release-date", "-release-date", "time", "-time", "popularity"
    };

    string? defaultOrder = null;

    foreach (string sort in sorts)
    {
        try
        {
            ItadDealsPage page = await itad.GetDealsByTagsAsync(tags, false, 0, 6, sort, extraFilter: quality);
            string titles = string.Join(" | ", page.List.Select(item => item.Title));
            defaultOrder ??= titles;

            string verdict = sort.Length > 0 && titles == defaultOrder ? "(même ordre que sans tri)" : "";
            Console.WriteLine($"{(sort.Length == 0 ? "(aucun)" : sort),-15} {verdict} {titles}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"{sort,-15} refusé : {ex.Message}");
        }
    }
}

// Les genres préférés calculés depuis ta bibliothèque (lue seulement), puis les promos recommandées.
static async Task TestRecommendationsAsync(IsThereAnyDealApi itad)
{
    List<Game> games = new GameDatabase().LoadGames();
    var recommender = new GenreRecommender();
    List<PreferredGenre> genres = await recommender.GetPreferredGenresAsync(games, Array.Empty<string>());

    Console.WriteLine($"{games.Count} jeux, {games.Count(g => g.PlaytimeMinutes > 0)} joués, {games.Count(g => g.TagList.Length > 0)} avec des tags");
    Console.WriteLine("Genres préférés :");

    foreach (PreferredGenre genre in genres)
    {
        Console.WriteLine($"  {genre.Label,-30} → {genre.Name}");
    }

    ItadDealsPage page = await GenreRecommender.GetDealsAsync(itad, genres.Select(g => g.Name), 80, 2000, 0, 15, "rank");

    Console.WriteLine();
    Console.WriteLine("Promos recommandées (tri : popularité) :");

    foreach (ItadDealListItem item in page.List)
    {
        Console.WriteLine($"  {item.Title,-45} {item.Deal.Price.Amount,7:0.00} € (-{item.Deal.Cut} %) chez {item.Deal.Shop.Name}");
    }
}

static async Task ShowPresentationAsync(IsThereAnyDealApi itad, string title)
{
    List<ItadSearchResult> results = await itad.SearchAsync(title);
    ItadSearchResult? first = results.FirstOrDefault();

    if (first == null)
    {
        Console.WriteLine("Aucun jeu trouvé.");
        return;
    }

    GamePresentation? presentation = await new GamePresentationService(itad).GetAsync(first.Id);

    if (presentation == null)
    {
        Console.WriteLine("Pas de fiche pour ce jeu.");
        return;
    }

    Console.WriteLine(first.Title);
    Console.WriteLine($"Jaquette    : {presentation.CoverUrl}");
    Console.WriteLine($"Infos       : {presentation.InfoLine}");
    Console.WriteLine($"Genres      : {presentation.TagsLine}");
    Console.WriteLine($"Avis        : {presentation.ReviewLine}");
    Console.WriteLine($"Description : {presentation.Description}");

    SteamGameMedia? media = presentation.SteamMedia;
    Console.WriteLine($"Captures    : {media?.Screenshots.Count ?? 0}  ex. {media?.Screenshots.FirstOrDefault()?.SmallUrl}");
    Console.WriteLine($"Bandes-ann. : {media?.Trailers.Count ?? 0}  ex. {media?.Trailers.FirstOrDefault()?.Name}");
    Console.WriteLine($"  image     : {media?.Trailers.FirstOrDefault()?.ThumbnailUrl}");
    Console.WriteLine($"  teaser    : {media?.Trailers.FirstOrDefault()?.MicrotrailerUrl}");
    Console.WriteLine($"Page Steam  : {media?.StoreUrl}");
}

static string ReadOrDefault(string defaultValue)
{
    string text = (Console.ReadLine() ?? "").Trim();
    return text.Length == 0 ? defaultValue : text;
}

static async Task CountAsync(IsThereAnyDealApi itad, string label, string[]? tags, bool matchAll)
{
    // 3 pages de 200 au plus : de quoi comparer, sans user la limite (100 requêtes / 5 minutes).
    const int PageSize = 200;
    const int MaxPages = 3;

    List<ItadDealListItem> items = new List<ItadDealListItem>();
    int offset = 0;
    bool hasMore = true;

    for (int page = 0; page < MaxPages && hasMore; page++)
    {
        ItadDealsPage result = tags == null
            ? await itad.GetDealsAsync(offset, PageSize, "")
            : await itad.GetDealsByTagsAsync(tags, matchAll, offset, PageSize, "");

        items.AddRange(result.List);
        offset = result.NextOffset;
        hasMore = result.HasMore;
    }

    string count = hasMore ? $"plus de {items.Count}" : items.Count.ToString();
    string examples = string.Join(" | ", items.Take(4).Select(item => item.Title));

    Console.WriteLine($"{label,-40} {count,12} promo(s)   ex. : {examples}");
}
