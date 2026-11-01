using System.Text;

namespace Wyrmhold.Core;

/// <summary>
/// Ce qu'on lit dans un jeu 3DS. TitleId ou Title peuvent manquer : Title est null si le jeu est chiffré.
/// </summary>
public record ThreeDsRomInfo(string? TitleId, string? Title);

/// <summary>
/// Lit le Title ID et le titre d'un jeu 3DS (.3ds, .cci, .cxi, .app).
/// Formats vérifiés le 1er novembre 2026 sur 3dbrew (NCSD, NCCH, ExeFS, SMDH) :
/// - un .3ds est un NCSD : un en-tête, puis des partitions ; la 1re est le jeu, au format NCCH ;
/// - le NCCH contient un petit système de fichiers, l'ExeFS, et son fichier « icon » (au format SMDH) porte les titres.
/// Toutes les positions sont en « unités » de 0x200 octets.
/// Les fichiers compressés .zcci et .zcxi ne sont pas lus : leur nom de fichier est utilisé.
/// </summary>
public static class ThreeDsRomHeader
{
    private const int MediaUnit = 0x200;
    private const int HeaderSize = 0x200;

    private const int MagicOffset = 0x100;
    private const int NcsdFirstPartitionOffset = 0x120;
    private const int NcchProgramIdOffset = 0x118;
    private const int NcchFlagsOffset = 0x18F;
    private const int NcchExeFsOffset = 0x1A0;
    private const byte NoCryptoFlag = 0x4;

    private const int ExeFsEntryCount = 10;
    private const int ExeFsEntrySize = 16;

    private const int SmdhTitlesOffset = 8;
    private const int SmdhTitleSize = 0x200;
    private const int SmdhShortTitleLength = 0x80;

    // Dans le SMDH : 0 = japonais, 1 = anglais, 2 = français. Anglais d'abord pour trouver la jaquette.
    private static readonly int[] LanguageOrder = { 1, 2, 0 };

    public static ThreeDsRomInfo? TryRead(string path)
    {
        try
        {
            using FileStream file = RomFile.OpenRead(path);
            byte[] header = RomFile.ReadAt(file, 0, HeaderSize);

            if (header.Length < HeaderSize)
            {
                return null;
            }

            string magic = Encoding.ASCII.GetString(header, MagicOffset, 4);
            long ncchStart = 0;

            if (magic == "NCSD")
            {
                ncchStart = (long)BitConverter.ToUInt32(header, NcsdFirstPartitionOffset) * MediaUnit;
                header = RomFile.ReadAt(file, ncchStart, HeaderSize);
            }
            else if (magic != "NCCH")
            {
                return null;
            }

            if (header.Length < HeaderSize || Encoding.ASCII.GetString(header, MagicOffset, 4) != "NCCH")
            {
                return null;
            }

            ulong programId = BitConverter.ToUInt64(header, NcchProgramIdOffset);
            string? titleId = programId == 0 ? null : programId.ToString("x16");

            // Un jeu chiffré n'a pas de titre lisible (Azahar ne lance de toute façon que des jeux déchiffrés).
            bool isEncrypted = (header[NcchFlagsOffset] & NoCryptoFlag) == 0;
            string? title = isEncrypted ? null : ReadTitle(file, ncchStart, header);

            return new ThreeDsRomInfo(titleId, title);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log($"Jeu 3DS illisible ({Path.GetFileName(path)}) : {ex.Message}");
            return null;
        }
    }

    private static string? ReadTitle(FileStream file, long ncchStart, byte[] ncch)
    {
        long exeFsStart = ncchStart + (long)BitConverter.ToUInt32(ncch, NcchExeFsOffset) * MediaUnit;
        byte[] exeFs = RomFile.ReadAt(file, exeFsStart, HeaderSize);

        if (exeFs.Length < HeaderSize)
        {
            return null;
        }

        for (int index = 0; index < ExeFsEntryCount; index++)
        {
            int entry = index * ExeFsEntrySize;
            string name = Encoding.ASCII.GetString(exeFs, entry, 8).TrimEnd('\0');

            if (name != "icon")
            {
                continue;
            }

            // Les positions de l'ExeFS comptent à partir de la fin de son en-tête.
            long smdhStart = exeFsStart + HeaderSize + BitConverter.ToUInt32(exeFs, entry + 8);
            return ReadSmdhTitle(RomFile.ReadAt(file, smdhStart, SmdhTitlesOffset + SmdhTitleSize * 3));
        }

        return null;
    }

    private static string? ReadSmdhTitle(byte[] smdh)
    {
        if (smdh.Length < SmdhTitlesOffset + SmdhTitleSize * 3 || Encoding.ASCII.GetString(smdh, 0, 4) != "SMDH")
        {
            return null;
        }

        foreach (int language in LanguageOrder)
        {
            string title = RomFile.ReadUtf16(smdh, SmdhTitlesOffset + language * SmdhTitleSize, SmdhShortTitleLength);

            if (title.Length > 0)
            {
                return title.Replace('\n', ' ');
            }
        }

        return null;
    }
}
