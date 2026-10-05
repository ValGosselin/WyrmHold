using System.Net;

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
                byte[]? imageBytes = await TryDownloadAsync(url, cancellationToken);

                if (imageBytes is null)
                {
                    string? exactUrl = await SteamStoreApi.GetLibraryCapsuleUrlAsync(game.PlatformGameId);

                    if (exactUrl is not null)
                    {
                        imageBytes = await TryDownloadAsync(exactUrl, cancellationToken);
                    }
                }

                if (imageBytes is null)
                {
                    await File.WriteAllTextAsync(GetMissingMarkerPath(game), "", cancellationToken);
                    return;
                }

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

    public async Task DownloadMissingCoversFromSteamGridDbAsync(IEnumerable<Game> games, SteamGridDbApi steamGridDb)
    {
        if (!steamGridDb.IsConfigured)
        {
            return;
        }

        List<Game> toSearch = games
            .Where(g => !File.Exists(GetCoverPath(g)) && !File.Exists(GetSgdbMissingMarkerPath(g)))
            .Where(g => g.Platform != Platform.Steam || File.Exists(GetMissingMarkerPath(g)))
            .ToList();

        foreach (Game game in toSearch)
        {
            try
            {
                string? coverUrl = null;

                if (game.Platform != Platform.Steam)
                {
                    string? steamAppId = await SteamStoreApi.FindAppIdByNameAsync(game.Name);

                    if (steamAppId is not null)
                    {
                        coverUrl = await SteamStoreApi.GetLibraryCapsuleUrlAsync(steamAppId);
                    }
                }

                coverUrl ??= await steamGridDb.FindCoverUrlAsync(game);

                if (coverUrl is null)
                {
                    await File.WriteAllTextAsync(GetSgdbMissingMarkerPath(game), "");
                    continue;
                }

                byte[] imageBytes = await Http.GetByteArrayAsync(coverUrl);
                await File.WriteAllBytesAsync(GetCoverPath(game), imageBytes);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Logger.Log("Limite de requêtes SteamGridDB atteinte, la suite sera téléchargée plus tard.");
                return;
            }
            catch (Exception ex)
            {
                Logger.Log($"Jaquette SteamGridDB introuvable pour {game.Name} : {ex.Message}");
            }
        }
    }

    private static async Task<byte[]?> TryDownloadAsync(string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private string GetCoverPath(Game game)
    {
        return Path.Combine(_folder, $"{game.Platform}_{game.PlatformGameId}.jpg");
    }

    private string GetMissingMarkerPath(Game game)
    {
        return Path.ChangeExtension(GetCoverPath(game), ".missing");
    }

    private string GetSgdbMissingMarkerPath(Game game)
    {
        return Path.ChangeExtension(GetCoverPath(game), ".sgdbmissing");
    }
}