using System.Runtime.InteropServices;

namespace JunkCleaner.Interop;

internal static class RecycleBinInterop
{
    /// <summary>
    /// Win32 layout: DWORD cbSize, then ULONGLONG ×2 starting at offset 8.
    /// <see cref="SHQueryRbInfo"/> must be 24 bytes or <see cref="SHQueryRecycleBin"/> returns E_INVALIDARG (0x80070057).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct SHQueryRbInfo
    {
        [FieldOffset(0)]
        public uint CbSize;

        [FieldOffset(8)]
        public ulong I64Size;

        [FieldOffset(16)]
        public ulong I64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SHQueryRecycleBinW(string? pszRootPath, ref SHQueryRbInfo psqrbInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SHEmptyRecycleBinW(nint hwnd, string? pszRootPath, SHEmptyRecycleBinFlags dwFlags);

    [Flags]
    internal enum SHEmptyRecycleBinFlags : uint
    {
        SherbNoConfirmation = 0x00000001,
        SherbNoProgressUi = 0x00000002,
        SherbNoSound = 0x00000004,
    }

    internal const int HRESULT_S_OK = 0;
}
