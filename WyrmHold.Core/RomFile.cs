namespace Wyrmhold.Core;

/// <summary>
/// Petits outils communs aux lecteurs d'en-têtes de jeux. Tout est en lecture seule, et l'émulateur peut
/// avoir le même fichier ouvert (d'où FileShare.ReadWrite).
/// </summary>
internal static class RomFile
{
    public static FileStream OpenRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    // Lit jusqu'à « count » octets à cet endroit (moins si le fichier se termine avant).
    public static byte[] ReadAt(FileStream file, long offset, int count)
    {
        if (offset < 0 || offset >= file.Length)
        {
            return Array.Empty<byte>();
        }

        file.Position = offset;

        byte[] buffer = new byte[count];
        int read = file.ReadAtLeast(buffer, count, throwOnEndOfStream: false);
        return read == count ? buffer : buffer[..read];
    }

    public static uint ReadUInt32BigEndian(byte[] bytes, int offset)
    {
        return (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);
    }

    // Un texte en UTF-16, arrêté au premier caractère nul. Les textes des consoles Nintendo sont écrits ainsi.
    public static string ReadUtf16(byte[] bytes, int offset, int length)
    {
        string text = System.Text.Encoding.Unicode.GetString(bytes, offset, Math.Min(length, bytes.Length - offset));
        int end = text.IndexOf('\0');
        return (end >= 0 ? text.Substring(0, end) : text).Trim();
    }
}
