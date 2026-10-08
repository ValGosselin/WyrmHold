using System.Runtime.InteropServices;
using System.Text;

namespace Wyrmhold.Core;

/// <summary>
/// Lit le chemin du programme d'un processus avec les fonctions de Windows.
/// Process.MainModule ouvre le processus avec beaucoup de droits (lire sa mémoire) : les anti-triches
/// le refusent, ou le remarquent. Ici on ne demande que PROCESS_QUERY_LIMITED_INFORMATION, le droit
/// minimal prévu pour ce cas : connaître le nom du programme, rien d'autre.
/// </summary>
internal static class NativeProcesses
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    // Largeur du tampon qui reçoit le chemin (en caractères) : bien assez pour un chemin normal.
    private const int PathBufferLength = 1024;

    /// <summary>
    /// Le chemin complet du programme (ex. « D:\Jeux\Hades\Hades.exe »), ou null s'il est illisible
    /// (processus système protégé, ou déjà fermé).
    /// </summary>
    public static string? GetExecutablePath(int processId)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);

        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            StringBuilder buffer = new StringBuilder(PathBufferLength);
            int length = buffer.Capacity;

            return QueryFullProcessImageName(handle, 0, buffer, ref length)
                ? buffer.ToString(0, length)
                : null;
        }
        finally
        {
            // Chaque « poignée » ouverte doit être rendue à Windows.
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
