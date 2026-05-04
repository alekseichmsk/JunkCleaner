using System.IO;
using System.Runtime.InteropServices;
using JunkCleaner.Contracts;
using JunkCleaner.Interop;
using JunkCleaner.Models;
using JunkCleaner.Services;

namespace JunkCleaner.Categories;

public sealed class RecycleBinCategory : ICleanupCategory
{
    public string Id => "recycle-bin";

    public string DisplayName => "Корзина";

    public string Description => "Очистка корзины для всех дисков (Shell API).";

    public bool RequiresAdmin => false;

    public async Task<ScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await StaInterop.InvokeStaAsync(ScanSta).ConfigureAwait(false);

        ScanResult ScanSta()
        {
            try
            {
                var hr = QueryAllRecycleBinsCombined(out var size, out var items);
                if (hr != RecycleBinInterop.HRESULT_S_OK)
                {
                    return new ScanResult
                    {
                        CategoryId = Id,
                        Success = false,
                        ErrorMessage = $"Не удалось запросить размер корзины (HR=0x{hr:X8}).",
                        UsesNativeCleanup = true,
                    };
                }

                return new ScanResult
                {
                    CategoryId = Id,
                    Success = true,
                    TotalBytes = ClampToPositiveLong(size),
                    NativeItemCount = ClampToPositiveLong(items),
                    FilePaths = Array.Empty<string>(),
                    UsesNativeCleanup = true,
                };
            }
            catch (Exception ex)
            {
                return new ScanResult
                {
                    CategoryId = Id,
                    Success = false,
                    ErrorMessage = ex.Message,
                    UsesNativeCleanup = true,
                };
            }
        }
    }

    public async Task<CleanupResult> CleanAsync(ScanResult scanResult, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await StaInterop.InvokeStaAsync(() => CleanSta(scanResult)).ConfigureAwait(false);

        CleanupResult CleanSta(ScanResult scan)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var hr = RecycleBinInterop.SHEmptyRecycleBinW(
                    nint.Zero,
                    null,
                    RecycleBinInterop.SHEmptyRecycleBinFlags.SherbNoConfirmation |
                    RecycleBinInterop.SHEmptyRecycleBinFlags.SherbNoProgressUi |
                    RecycleBinInterop.SHEmptyRecycleBinFlags.SherbNoSound);

                if (hr != RecycleBinInterop.HRESULT_S_OK)
                {
                    return new CleanupResult
                    {
                        CategoryId = Id,
                        Success = false,
                        ErrorMessage = $"Ошибка очистки корзины (HR=0x{hr:X8}).",
                    };
                }

                var deletedApprox = scan.NativeItemCount > int.MaxValue
                    ? int.MaxValue
                    : (int)Math.Max(scan.NativeItemCount, 0);

                return new CleanupResult
                {
                    CategoryId = Id,
                    Success = true,
                    FreedBytes = Math.Max(scan.TotalBytes, 0),
                    FilesDeleted = deletedApprox,
                };
            }
            catch (Exception ex)
            {
                return new CleanupResult
                {
                    CategoryId = Id,
                    Success = false,
                    ErrorMessage = ex.Message,
                };
            }
        }
    }

    private static int QueryAllRecycleBinsCombined(out ulong aggregateSize, out ulong aggregateItems)
    {
        aggregateSize = 0;
        aggregateItems = 0;

        var infoSize = Marshal.SizeOf<RecycleBinInterop.SHQueryRbInfo>();
        uint infoSizeExplicit = unchecked((uint)infoSize);

        var infoAll = new RecycleBinInterop.SHQueryRbInfo { CbSize = infoSizeExplicit };
        var hrAll = TryQuery(null);
        if (hrAll != RecycleBinInterop.HRESULT_S_OK)
            hrAll = TryQuery(string.Empty);

        if (hrAll == RecycleBinInterop.HRESULT_S_OK)
        {
            aggregateSize = infoAll.I64Size;
            aggregateItems = infoAll.I64NumItems;
            return hrAll;
        }

        var anyOk = false;
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
                continue;

            switch (drive.DriveType)
            {
                case DriveType.Fixed:
                case DriveType.Removable:
                    break;
                default:
                    continue;
            }

            var rootDir = TrimRoot(drive.RootDirectory.FullName);

            infoAll.CbSize = infoSizeExplicit;
            var hr = TryQuery(rootDir);
            if (hr != RecycleBinInterop.HRESULT_S_OK)
                continue;

            anyOk = true;
            aggregateSize += infoAll.I64Size;
            aggregateItems += infoAll.I64NumItems;
            infoAll = new RecycleBinInterop.SHQueryRbInfo { CbSize = infoSizeExplicit };
        }

        return anyOk ? RecycleBinInterop.HRESULT_S_OK : hrAll;

        int TryQuery(string? rootPath)
        {
            infoAll.CbSize = infoSizeExplicit;
            infoAll.I64Size = 0;
            infoAll.I64NumItems = 0;
            return RecycleBinInterop.SHQueryRecycleBinW(rootPath, ref infoAll);
        }
    }

    private static string TrimRoot(string fullRoot)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(fullRoot);
        if (string.IsNullOrEmpty(trimmed))
            return fullRoot.EndsWith(":\\", StringComparison.OrdinalIgnoreCase) ? fullRoot : fullRoot + "\\";

        var last = trimmed[^1];
        if (last is ':' && trimmed.Length == 2)
            return trimmed + "\\";

        return trimmed + "\\";
    }

    private static long ClampToPositiveLong(ulong value) =>
        value > long.MaxValue ? long.MaxValue : (long)value;
}
