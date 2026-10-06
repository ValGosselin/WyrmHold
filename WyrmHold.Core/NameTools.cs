namespace Wyrmhold.Core;

public static class NameTools
{
    public static string CleanForSearch(string name)
    {
        string cleaned = name.Replace("™", "").Replace("®", "").Replace("©", "").Trim();

        if (cleaned.EndsWith(" Demo", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - " Demo".Length).Trim();
        }

        return cleaned;
    }

    public static string Normalize(string name)
    {
        return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}