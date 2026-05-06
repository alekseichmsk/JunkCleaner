using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Velopack.Sources;

namespace JunkCleaner.Updates;

/// <summary>
/// Velopack's default downloader can miss some Windows-specific proxy setups.
/// This downloader uses WinINET defaults and prefers SocketsHttpHandler for TLS.
/// </summary>
internal sealed class WindowsVelopackFileDownloader : HttpClientFileDownloader
{
    protected override HttpClientHandler CreateHttpClientHandler()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = false,
            UseProxy = true,
#pragma warning disable CA1416 // Windows-specific WinINET proxy probing
            Proxy = WebRequest.GetSystemWebProxy(),
#pragma warning restore CA1416
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
        };

        if (UpdateSettings.RelaxTlsValidation)
            handler.ServerCertificateCustomValidationCallback = UnsafeAcceptAllCertificates;

        return handler;
    }

    private static bool UnsafeAcceptAllCertificates(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
        => true;
}
