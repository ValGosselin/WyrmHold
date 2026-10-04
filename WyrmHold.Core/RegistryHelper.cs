using Microsoft.Win32;

namespace Wyrmhold.Core;

internal static class RegistryHelper
{
    private static readonly RegistryView[] Views =
    {
        RegistryView.Registry64,
        RegistryView.Registry32
    };

    public static List<RegistryKey> OpenLocalMachineKeys(string path)
    {
        List<RegistryKey> keys = new List<RegistryKey>();

        foreach (RegistryView view in Views)
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            RegistryKey? key = baseKey.OpenSubKey(path);

            if (key is not null)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    public static string? ReadLocalMachineValue(string path, string valueName)
    {
        foreach (RegistryView view in Views)
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? key = baseKey.OpenSubKey(path);

            if (key?.GetValue(valueName) is string value)
            {
                return value;
            }
        }

        return null;
    }
}