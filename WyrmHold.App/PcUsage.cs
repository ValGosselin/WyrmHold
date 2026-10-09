using System.Runtime.InteropServices;

namespace WyrmHold.App;

/// <summary>
/// L'utilisation du processeur et de la mémoire, pour la barre du haut de l'overlay.
/// Deux fonctions de Windows, rien à installer. Pas d'images par seconde (FPS) :
/// il faudrait entrer dans le jeu (injection), ce que Wyrmhold ne fait jamais.
/// </summary>
public sealed class PcUsage
{
    // Les compteurs de la lecture précédente : le processeur se mesure par différence entre deux lectures.
    private long _lastIdle;
    private long _lastTotal;

    /// <summary>
    /// Le processeur occupé (0 à 100 %) depuis l'appel précédent ; null au premier appel ou si Windows refuse.
    /// </summary>
    public double? ReadCpuPercent()
    {
        // Temps passé par tous les cœurs : à ne rien faire (idle), dans Windows (kernel, qui COMPREND idle)
        // et dans les programmes (user). En centaines de nanosecondes.
        if (!GetSystemTimes(out long idle, out long kernel, out long user))
        {
            return null;
        }

        long total = kernel + user;
        long idleDelta = idle - _lastIdle;
        long totalDelta = total - _lastTotal;
        bool isFirst = _lastTotal == 0;

        _lastIdle = idle;
        _lastTotal = total;

        if (isFirst || totalDelta <= 0)
        {
            return null;
        }

        return Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
    }

    /// <summary>La mémoire vive utilisée et totale, en Go ; null si Windows refuse.</summary>
    public static (double UsedGb, double TotalGb)? ReadMemory()
    {
        MemoryStatus status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };

        if (!GlobalMemoryStatusEx(ref status))
        {
            return null;
        }

        const double Gb = 1024.0 * 1024 * 1024;
        return ((status.TotalPhys - status.AvailPhys) / Gb, status.TotalPhys / Gb);
    }

    // Les trois FILETIME (64 bits chacun) lus directement comme des nombres.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    // MEMORYSTATUSEX de Windows : les champs dans le même ordre, Length rempli avant l'appel.
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
