using System.Text;
using System.Text.RegularExpressions;

namespace Wyrmhold.Core;

/// <summary>
/// Ce qu'on lit dans un NSP : le Title ID, et si c'est le jeu lui-même (pas une mise à jour ni un DLC).
/// </summary>
public record SwitchPackageInfo(string TitleId, bool IsBaseGame);

/// <summary>
/// Lit le Title ID d'un NSP sans le déchiffrer, donc sans les clés de la console : Wyrmhold ne touche pas à prod.keys.
/// Un NSP est une archive PFS0 : un en-tête, la liste de ses fichiers, puis les fichiers.
/// Vérifié le 1er novembre 2026 sur de vrais NSP : la plupart des jeux contiennent un « .cnmt.xml » en clair
/// (Type + Id) ; un ticket « &lt;id&gt;.tik » n'est pas toujours là (absent de Tomodachi Life).
/// Un XCI n'a rien de lisible sans les clés : il ne passe pas ici.
/// </summary>
public static class SwitchPackageReader
{
    private const int HeaderSize = 16;
    private const int EntrySize = 24;

    // Garde-fous : au-delà, ce n'est pas un vrai NSP, on ne réserve pas de mémoire pour rien.
    private const int MaxEntries = 4096;
    private const int MaxNamesSize = 1024 * 1024;
    private const int MaxXmlSize = 1024 * 1024;

    private record Entry(string Name, long Offset, long Size);

    public static SwitchPackageInfo? TryRead(string path)
    {
        try
        {
            using FileStream file = RomFile.OpenRead(path);
            List<Entry> entries = ReadEntries(file, out long dataStart);

            return ReadFromXml(file, entries, dataStart) ?? ReadFromTicket(entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log($"NSP illisible ({Path.GetFileName(path)}) : {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// true pour le Title ID d'un jeu : il finit par 000. Une mise à jour finit par 800, un DLC par autre chose.
    /// </summary>
    public static bool IsBaseTitleId(string titleId)
    {
        return titleId.EndsWith("000", StringComparison.Ordinal);
    }

    private static List<Entry> ReadEntries(FileStream file, out long dataStart)
    {
        dataStart = 0;

        byte[] header = RomFile.ReadAt(file, 0, HeaderSize);

        if (header.Length < HeaderSize || Encoding.ASCII.GetString(header, 0, 4) != "PFS0")
        {
            return new List<Entry>();
        }

        int count = BitConverter.ToInt32(header, 4);
        int namesSize = BitConverter.ToInt32(header, 8);

        if (count <= 0 || count > MaxEntries || namesSize <= 0 || namesSize > MaxNamesSize)
        {
            return new List<Entry>();
        }

        byte[] table = RomFile.ReadAt(file, HeaderSize, count * EntrySize + namesSize);

        if (table.Length < count * EntrySize + namesSize)
        {
            return new List<Entry>();
        }

        dataStart = HeaderSize + table.Length;

        List<Entry> entries = new List<Entry>();

        for (int index = 0; index < count; index++)
        {
            int position = index * EntrySize;
            long offset = BitConverter.ToInt64(table, position);
            long size = BitConverter.ToInt64(table, position + 8);
            int nameStart = count * EntrySize + BitConverter.ToInt32(table, position + 16);

            if (nameStart < table.Length)
            {
                int nameEnd = Array.IndexOf(table, (byte)0, nameStart);
                int nameLength = (nameEnd < 0 ? table.Length : nameEnd) - nameStart;
                entries.Add(new Entry(Encoding.ASCII.GetString(table, nameStart, nameLength), offset, size));
            }
        }

        return entries;
    }

    private static SwitchPackageInfo? ReadFromXml(FileStream file, List<Entry> entries, long dataStart)
    {
        SwitchPackageInfo? found = null;

        foreach (Entry entry in entries.Where(e => e.Name.EndsWith(".cnmt.xml", StringComparison.OrdinalIgnoreCase)))
        {
            if (entry.Size is <= 0 or > MaxXmlSize)
            {
                continue;
            }

            string xml = Encoding.UTF8.GetString(RomFile.ReadAt(file, dataStart + entry.Offset, (int)entry.Size));
            Match type = Regex.Match(xml, "<Type>([^<]+)</Type>");
            Match id = Regex.Match(xml, "<Id>0x([0-9a-fA-F]{16})</Id>");

            if (!type.Success || !id.Success)
            {
                continue;
            }

            SwitchPackageInfo info = new SwitchPackageInfo(
                id.Groups[1].Value.ToLowerInvariant(),
                type.Groups[1].Value == "Application");

            // Un même NSP peut contenir le jeu et sa mise à jour : le jeu passe avant.
            if (info.IsBaseGame)
            {
                return info;
            }

            found ??= info;
        }

        return found;
    }

    // Le nom d'un ticket commence par le Title ID : « 0100187003a360000000000000000005.tik ».
    private static SwitchPackageInfo? ReadFromTicket(List<Entry> entries)
    {
        Entry? ticket = entries
            .Where(e => e.Name.EndsWith(".tik", StringComparison.OrdinalIgnoreCase)
                && NameTools.FindSwitchTitleId(e.Name.Substring(0, Math.Min(16, e.Name.Length))) is not null)
            .MinBy(e => IsBaseTitleId(e.Name.Substring(0, 16).ToLowerInvariant()) ? 0 : 1);

        if (ticket is null)
        {
            return null;
        }

        string titleId = ticket.Name.Substring(0, 16).ToLowerInvariant();
        return new SwitchPackageInfo(titleId, IsBaseTitleId(titleId));
    }
}
