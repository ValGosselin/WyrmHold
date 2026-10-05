namespace Wyrmhold.Core;

public class MetadataFetcher
{
    private const int BatchSize = 50;

    public async Task<List<Game>> FetchMissingAsync(IEnumerable<Game> games)
    {
        List<Game> toFetch = games.Where(g => g.MetadataUpdatedUnix == 0).ToList();

        if (toFetch.Count == 0)
        {
            return toFetch;
        }

        Dictionary<Game, int> steamAppIds = new Dictionary<Game, int>();

        foreach (Game game in toFetch)
        {
            if (game.Platform == Platform.Steam)
            {
                if (int.TryParse(game.PlatformGameId, out int appId))
                {
                    steamAppIds[game] = appId;
                }
            }
            else
            {
                string? foundAppId = await SteamStoreApi.FindAppIdByNameAsync(game.Name);

                if (int.TryParse(foundAppId, out int appId))
                {
                    steamAppIds[game] = appId;
                }
            }
        }

        Dictionary<int, string> tagNames = await SteamStoreApi.GetTagNamesAsync();
        Dictionary<int, StoreItem> storeItems = new Dictionary<int, StoreItem>();

        foreach (int[] batch in steamAppIds.Values.Distinct().Chunk(BatchSize))
        {
            foreach (StoreItem item in await SteamStoreApi.GetItemsAsync(batch))
            {
                storeItems[item.AppId] = item;
            }
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (Game game in toFetch)
        {
            if (steamAppIds.TryGetValue(game, out int appId)
                && storeItems.TryGetValue(appId, out StoreItem? item))
            {
                game.Description = item.BasicInfo?.ShortDescription;
                game.Developers = item.BasicInfo is null
                    ? null
                    : string.Join(", ", item.BasicInfo.Developers.Select(d => d.Name));
                game.ReleaseDateUnix = item.Release?.SteamReleaseDate ?? 0;
                game.IsEarlyAccess = item.IsEarlyAccess;
                game.Tags = string.Join(", ", item.Tags
                    .Select(tag => tagNames.GetValueOrDefault(tag.TagId))
                    .OfType<string>());
            }

            game.MetadataUpdatedUnix = now;
        }

        return toFetch;
    }
}