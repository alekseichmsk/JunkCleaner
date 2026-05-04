using System.Diagnostics;

namespace JunkCleaner.Services;

public static class DnsFlushService
{
    public static async Task<(bool Ok, string CombinedOutput)> FlushAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ipconfig.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("/flushdns");

            using var proc = Process.Start(psi);
            if (proc is null)
                return (false, "Не удалось запустить ipconfig.");

            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var o = await proc.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var e = await proc.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var merged = string.Join(
                Environment.NewLine,
                new[] { o, e }.Where(static s => !string.IsNullOrWhiteSpace(s)));
            var exitOk = proc.ExitCode == 0;
            return (exitOk, string.IsNullOrWhiteSpace(merged) ? $"Код выхода: {proc.ExitCode}" : merged.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
