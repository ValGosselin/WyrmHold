using System.Diagnostics;

namespace Wyrmhold.Core;

public record PlaySessionTimes(DateTimeOffset Start, DateTimeOffset End);

public class PlaytimeTracker
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public async Task<PlaySessionTimes?> TrackSessionAsync(string installPath)
    {
        string folder = Path.TrimEndingDirectorySeparator(installPath) + Path.DirectorySeparatorChar;

        DateTimeOffset waitUntil = DateTimeOffset.Now + StartTimeout;

        while (!await Task.Run(() => IsRunning(folder)))
        {
            if (DateTimeOffset.Now > waitUntil)
            {
                return null;
            }

            await Task.Delay(PollInterval);
        }

        DateTimeOffset sessionStart = DateTimeOffset.Now;

        while (await Task.Run(() => IsRunning(folder)))
        {
            await Task.Delay(PollInterval);
        }

        return new PlaySessionTimes(sessionStart, DateTimeOffset.Now);
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