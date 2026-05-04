using JunkCleaner.Security;

namespace JunkCleaner.Tests;

public sealed class ProtectedPathsTests
{
    [Fact]
    public void System_related_path_under_system_folder_is_blocked()
    {
        var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        Assert.False(string.IsNullOrEmpty(sys));

        var candidate = Path.Combine(sys, "drivers", "etc", "hosts");
        Assert.True(ProtectedPaths.IsBlocked(candidate));
    }

    [Fact]
    public void User_temp_path_is_typically_allowed()
    {
        var tmp = Path.GetTempPath();
        Assert.False(string.IsNullOrEmpty(tmp));

        var candidate = Path.Combine(tmp, "junk-cleaner-unit-test.txt");
        Assert.False(ProtectedPaths.IsBlocked(candidate));
    }

    [Fact]
    public void Is_under_allowed_roots_matches_normalized_paths()
    {
        var root = ProtectedPaths.NormalizePath(Path.GetTempPath());
        var inner = ProtectedPaths.NormalizePath(Path.Combine(root, "a", "b"));
        Assert.True(ProtectedPaths.IsUnderAllowedRoots(inner, new[] { root }));

        var windir = ProtectedPaths.NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Assert.False(ProtectedPaths.IsUnderAllowedRoots(windir, new[] { root }));
    }

    [Fact]
    public void Program_files_roots_are_blocked()
    {
        var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.False(string.IsNullOrEmpty(pf64));

        var candidate = Path.Combine(pf64, "dotnet", "dotnet.exe");
        Assert.True(ProtectedPaths.IsBlocked(candidate));
    }
}
