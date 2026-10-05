using System.Diagnostics;

namespace Wyrmhold.Core;

public class PlaytimeTracker
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public async Task<TimeSpan?> TrackSessionAsync(string installPath)
    {
        string folder = Path.TrimEndingDirectorySeparator(installPath) + Path.DirectorySeparatorChar;

        DateTime waitUntil = DateTime.Now + StartTimeout;

        while (!await Task.Run(() => IsRunning(folder)))
        {
            if (DateTime.Now > waitUntil)
            {
                return null;
            }

            await Task.Delay(PollInterval);
        }

        DateTime sessionStart = DateTime.Now;

        while (await Task.Run(() => IsRunning(folder)))
        {
            await Task.Delay(PollInterval);
        }

        return DateTime.Now - sessionStart;
    }

    private static bool IsRunning(string folder)
    {
        Process[] processes = Process.GetProcesses();

        try
        {
            return processes.Any(process => IsInFolder(process, folder));
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static bool IsInFolder(Process process, string folder)
    {
        try
        {
            string? exePath = process.MainModule?.FileName;
            return exePath is not null && exePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}