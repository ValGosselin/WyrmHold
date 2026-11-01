using System.Runtime.InteropServices;
using System.Text;

namespace Wyrmhold.Core;

/// <summary>
/// Lit le chemin du programme d'un processus avec les fonctions de Windows.
/// Process.MainModule ouvre le processus avec beaucoup de droits (lire sa mémoire) : les anti-triches
/// le refusent, ou le remarquent. Ici on ne demande que PROCESS_QUERY_LIMITED_INFORMATION, le droit
/// minimal prévu pour ce cas : connaître le nom du programme, rien d'autre.
/// </summary>
public static class NativeProcesses
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    // Largeur du tampon qui reçoit le chemin (en caractères) : bien assez pour un chemin normal.
    private const int PathBufferLength = 1024;

    // La question posée à NtQueryInformationProcess : « sa ligne de commande ».
    private const int ProcessCommandLineInformation = 60;

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

    /// <summary>
    /// La ligne de commande d'un processus, ou null si elle est illisible. Sert à savoir quel jeu un émulateur a
    /// ouvert : tous ses jeux tournent dans le même programme. Même droit minimal que ci-dessus, et seuls les
    /// émulateurs sont interrogés (voir GameWatcher).
    /// </summary>
    public static string? GetCommandLine(int processId)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);

        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // Premier appel à vide : Windows répond la taille nécessaire.
            NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, out int size);

            if (size <= 0)
            {
                return null;
            }

            IntPtr buffer = Marshal.AllocHGlobal(size);

            try
            {
                if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, size, out _) < 0)
                {
                    return null;
                }

                // La réponse est un UNICODE_STRING : sa longueur en octets (2 octets), puis, un pointeur plus loin,
                // l'adresse du texte (Windows le range dans le même tampon).
                int length = (ushort)Marshal.ReadInt16(buffer);
                IntPtr text = Marshal.ReadIntPtr(buffer, IntPtr.Size);

                return length == 0 ? null : Marshal.PtrToStringUni(text, length / sizeof(char));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
