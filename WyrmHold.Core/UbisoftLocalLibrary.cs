using System.Text.RegularExpressions;

namespace Wyrmhold.Core;

internal record UbisoftLocalGame(uint ProductId, string Name, string? SpaceId);

/// <summary>
/// Lit le cache local d'Ubisoft Connect pour trouver les jeux possédés par le compte connecté.
/// </summary>
internal static class UbisoftLocalLibrary
{
    private static readonly string CacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ubisoft Game Launcher", "cache");

    public static List<UbisoftLocalGame> ReadOwnedGames()
    {
        List<UbisoftLocalGame> games = new List<UbisoftLocalGame>();

        string ownershipFolder = Path.Combine(CacheFolder, "ownership");
        string configurationFile = Path.Combine(CacheFolder, "configuration", "configurations");

        if (!Directory.Exists(ownershipFolder) || !File.Exists(configurationFile))
        {
            return games;
        }

        string? ownershipFile = Directory.GetFiles(ownershipFolder)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (ownershipFile is null)
        {
            return games;
        }

        HashSet<uint> ownedProductIds = ReadMainProductIds(File.ReadAllBytes(ownershipFile));
        var configurations = UbiParser.Parsers.ParseConfigurationCacheFile(configurationFile);

        foreach (var product in configurations.Configurations)
        {
            if (!ownedProductIds.Contains(product.ProductId))
            {
                continue;
            }

            string? name = ReadRootValue(product.Configuration_, "name");

            if (name is null)
            {
                continue;
            }

            games.Add(new UbisoftLocalGame(
                product.ProductId,
                name,
                ReadRootValue(product.Configuration_, "space_id")));
            
        }

        return games;
    }

    private static HashSet<uint> ReadMainProductIds(byte[] bytes)
    {
        HashSet<uint> productIds = new HashSet<uint>();

        for (int i = 0; i < bytes.Length - 2; i++)
        {
            if (bytes[i] != 0x0A)
            {
                continue;
            }

            int position = i + 1;
            int length = 0;
            int shift = 0;

            while (position < bytes.Length && shift <= 28)
            {
                byte current = bytes[position];
                position++;
                length |= (current & 0x7F) << shift;

                if ((current & 0x80) == 0)
                {
                    break;
                }

                shift += 7;
            }

            if (length <= 0 || position + length > bytes.Length)
            {
                continue;
            }

            try
            {
                var game = Uplay.OwnershipCache.OwnedGame.Parser.ParseFrom(bytes, position, length);

                if (game.ProductId != 0 && game.ProductId == game.UplayId && game.ProductType == 0)
                {
                    productIds.Add(game.ProductId);
                }
            }
            catch (Google.Protobuf.InvalidProtocolBufferException)
            {
            }
        }

        return productIds;
    }

    private static string? ReadRootValue(string yaml, string key)
    {
        Match match = Regex.Match(yaml, $@"^  {key}:[ \t]*(.+?)\s*$", RegexOptions.Multiline);

        if (!match.Success)
        {
            return null;
        }

        string value = match.Groups[1].Value.Trim().Trim('\'', '"');

        if (Regex.IsMatch(value, @"^l\d+$"))
        {
            Match localized = Regex.Match(yaml, $@"^    {value}:[ \t]*(.+?)\s*$", RegexOptions.Multiline);

            if (localized.Success)
            {
                value = localized.Groups[1].Value.Trim().Trim('\'', '"');
            }
        }

        return value.Length > 0 ? value : null;
    }
}