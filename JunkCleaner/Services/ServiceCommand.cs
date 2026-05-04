using System.Diagnostics;

namespace JunkCleaner.Services;

internal static class ServiceCommand
{
    public static void TrySc(string serviceName, string action)
    {
        try
        {
            using var proc = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = action + " " + serviceName,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });

            proc?.WaitForExit(8000);
        }
        catch
        {
            // Best effort: deletion may still work if the service is already stopped or not present.
        }
    }
}
