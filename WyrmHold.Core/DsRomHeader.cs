namespace Wyrmhold.Core;

/// <summary>
/// Lit le vrai titre d'un jeu DS, celui du menu de la console (l'en-tête n'a qu'un nom de 12 lettres en majuscules).
/// Format vérifié le 1er novembre 2026 (GBATEK, « DS Cartridge Icon/Title ») : l'en-tête donne à l'offset 0x68
/// la position du bloc « icône et titres », et chaque titre y occupe 0x100 octets en UTF-16.
/// </summary>
public static class DsRomHeader
{
    private const int BannerPointerOffset = 0x68;
    private const int TitleLength = 0x100;

    // Anglais d'abord : c'est la langue dans laquelle SteamGridDB connaît le plus de jeux (jaquette). Puis français, japonais.
    private static readonly int[] TitleOffsets = { 0x340, 0x440, 0x240 };

    public static string? TryReadTitle(string path)
    {
        try
        {
            using FileStream file = RomFile.OpenRead(path);
            byte[] header = RomFile.ReadAt(file, BannerPointerOffset, 4);

            if (header.Length < 4)
            {
                return null;
            }

            uint banner = BitConverter.ToUInt32(header, 0);

            if (banner == 0)
            {
                return null;
            }

            foreach (int titleOffset in TitleOffsets)
            {
                byte[] bytes = RomFile.ReadAt(file, banner + titleOffset, TitleLength);
                string title = RomFile.ReadUtf16(bytes, 0, bytes.Length);

                if (title.Length > 0)
                {
                    return WithoutPublisher(title);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log($"Jeu DS illisible ({Path.GetFileName(path)}) : {ex.Message}");
        }

        return null;
    }

    // Le titre tient sur 2 ou 3 lignes, et la dernière est l'éditeur : « Pokemon Platinum Version\nNintendo ».
    private static string WithoutPublisher(string title)
    {
        string[] lines = title.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(" ", lines.Length > 1 ? lines[..^1] : lines);
    }
}
