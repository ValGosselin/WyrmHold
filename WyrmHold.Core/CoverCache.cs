namespace Wyrmhold.Core;

public class CoverCache
{
    private static readonly HttpClient Http = new HttpClient();

    private readonly string _folder;

    public CoverCache()
    {
        _folder = Path.Combine(AppPaths.DataFolder, "covers");
        Directory.CreateDirectory(_folder);
    }

    public void AttachCovers(IEnumerable<Game> games)
    {
        foreach (Game game in games)
        {
            string path = GetCoverPath(game);
            game.CoverPath = File.Exists(path) ? path : null;
        }
    }

    public async Task DownloadMissingSteamCoversAsync(IEnumerable<Game> games)
    {
        List<Game> toDownload = games
            .Where(g => g.Platform == Platform.Steam)
            .Where(g => !File.Exists(GetCoverPath(g)) && !File.Exists(GetMissingMarkerPath(g)))
            .ToList();

        int failures = 0;
        ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 8 };

        await Parallel.ForEachAsync(toDownload, options, async (game, cancellationToken) =>
        {
            string url = "https://shared.steamstatic.com/store_item_assets/steam/apps/"
                + $"{game.PlatformGameId}/library_600x900.jpg";

            try
            {
                using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    await File.WriteAllTextAsync(GetMissingMarkerPath(game), "", cancellationToken);
                    return;
                }

                byte[] imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                await File.WriteAllBytesAsync(GetCoverPath(game), imageBytes, cancellationToken);
            }
            catch (Exception)
            {
                Interlocked.Increment(ref failures);
            }
        });

        if (failures > 0)
        {
            Logger.Log($"{failures} jaquette(s) Steam n'ont pas pu être téléchargées.");
        }
    }

    private string GetCoverPath(Game game)
    {
        return Path.Combine(_folder, $"{game.Platform}_{game.PlatformGameId}.jpg");
    }

    private string GetMissingMarkerPath(Game game)
    {
        return Path.ChangeExtension(GetCoverPath(game), ".missing");
    }
}