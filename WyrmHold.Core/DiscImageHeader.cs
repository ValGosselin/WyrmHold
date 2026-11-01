using System.IO.Compression;
using System.Text;

namespace Wyrmhold.Core;

/// <summary>
/// Ce qu'on lit au début d'un disque GameCube ou Wii.
/// </summary>
public record DiscImageInfo(string GameId, string? Name, GameConsole Console, int DiscNumber);

/// <summary>
/// Lit l'en-tête d'un disque GameCube ou Wii, quel que soit le format du fichier.
/// Formats vérifiés le 1er novembre 2026 dans le code de Dolphin (DiscIO, docs/WiaAndRvz.md) :
/// - ISO et GCM : le disque tel quel, en-tête au tout début ;
/// - WBFS : une copie de l'en-tête juste après le 1er secteur ;
/// - WIA et RVZ : les 0x80 premiers octets du disque sont gardés en clair à l'offset 0x58 ;
/// - CISO : le disque commence après un en-tête de 0x8000 octets ;
/// - GCZ : compressé par blocs, le 1er bloc contient l'en-tête.
/// Lecture seule : le fichier n'est jamais modifié.
/// </summary>
public static class DiscImageHeader
{
    // Assez pour lire le numéro du jeu (0x00), le numéro du disque (0x06), les marques (0x18, 0x1C) et le nom (0x20).
    private const int HeaderSize = 0x80;

    private const int NameOffset = 0x20;
    private const int NameLength = 0x60;

    private const uint WiiMagic = 0x5D1C9EA3;
    private const uint GameCubeMagic = 0xC2339F3D;

    private const int WiaHeaderOffset = 0x58;
    private const int CisoHeaderSize = 0x8000;
    private const uint GczMagic = 0xB10BC001;
    private const int GczHeaderSize = 32;

    // Dans le GCZ, le bit de poids fort de la position d'un bloc veut dire « bloc non compressé ».
    private const ulong GczUncompressedFlag = 1UL << 63;

    /// <summary>
    /// Renvoie les infos du disque, ou null si ce n'est pas un disque reconnu (fichier illisible, WAD…).
    /// </summary>
    public static DiscImageInfo? TryRead(string path)
    {
        try
        {
            using FileStream file = RomFile.OpenRead(path);
            byte[] start = RomFile.ReadAt(file, 0, 0x100);

            return start.Length < 0x20 ? null : Parse(FindDiscHeader(file, start));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.Log($"Disque illisible ({Path.GetFileName(path)}) : {ex.Message}");
            return null;
        }
    }

    private static byte[] FindDiscHeader(FileStream file, byte[] start)
    {
        string magic = Encoding.ASCII.GetString(start, 0, 4);

        if (magic == "WBFS")
        {
            // L'octet 8 donne la taille d'un secteur (1 << n), et la copie de l'en-tête commence au secteur suivant.
            return RomFile.ReadAt(file, 1L << start[8], HeaderSize);
        }

        if (magic is "WIA\u0001" or "RVZ\u0001")
        {
            return RomFile.ReadAt(file, WiaHeaderOffset, HeaderSize);
        }

        if (magic == "CISO")
        {
            return RomFile.ReadAt(file, CisoHeaderSize, HeaderSize);
        }

        if (BitConverter.ToUInt32(start, 0) == GczMagic)
        {
            return ReadFirstGczBlock(file, start);
        }

        return start;
    }

    private static byte[] ReadFirstGczBlock(FileStream file, byte[] start)
    {
        uint blockCount = BitConverter.ToUInt32(start, 28);
        ulong firstBlock = BitConverter.ToUInt64(start, GczHeaderSize);

        // Après l'en-tête : une position (8 octets) puis une somme de contrôle (4 octets) par bloc.
        long dataStart = GczHeaderSize + (8L + 4L) * blockCount;
        long blockStart = dataStart + (long)(firstBlock & ~GczUncompressedFlag);

        if ((firstBlock & GczUncompressedFlag) != 0)
        {
            return RomFile.ReadAt(file, blockStart, HeaderSize);
        }

        file.Position = blockStart;

        using ZLibStream inflate = new ZLibStream(file, CompressionMode.Decompress, leaveOpen: true);
        byte[] header = new byte[HeaderSize];
        inflate.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false);
        return header;
    }

    private static DiscImageInfo? Parse(byte[] header)
    {
        if (header.Length < HeaderSize)
        {
            return null;
        }

        // Ces deux marques sont écrites en « gros-boutiste » : l'octet de poids fort d'abord.
        GameConsole console = RomFile.ReadUInt32BigEndian(header, 0x18) == WiiMagic ? GameConsole.Wii
            : RomFile.ReadUInt32BigEndian(header, 0x1C) == GameCubeMagic ? GameConsole.GameCube
            : GameConsole.None;

        if (console == GameConsole.None)
        {
            return null;
        }

        string gameId = Encoding.ASCII.GetString(header, 0, 6);

        return gameId.All(char.IsAsciiLetterOrDigit)
            ? new DiscImageInfo(gameId, ReadName(header), console, header[6])
            : null;
    }

    // Un nom japonais est en Shift-JIS : pas de nom interne dans ce cas, le nom du fichier le remplacera.
    private static string? ReadName(byte[] header)
    {
        byte[] bytes = header.Skip(NameOffset).Take(NameLength).TakeWhile(value => value != 0).ToArray();

        if (bytes.Length == 0 || bytes.Any(value => value >= 0x80))
        {
            return null;
        }

        string name = Encoding.ASCII.GetString(bytes).Trim();
        return name.Length > 0 ? name : null;
    }
}
