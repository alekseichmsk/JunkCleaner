using JunkCleaner.Duplicates;

namespace JunkCleaner.Tests;

public sealed class DuplicateScanEngineTests
{
    [Fact]
    public async Task Identical_files_produce_one_duplicate_group()
    {
        var root = Path.Combine(Path.GetTempPath(), "junk-dup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var a = Path.Combine(root, "a.bin");
            var b = Path.Combine(root, "b.bin");
            var c = Path.Combine(root, "c.bin");

            await File.WriteAllTextAsync(a, "hello");
            await File.WriteAllTextAsync(b, "hello");
            await File.WriteAllTextAsync(c, "world");

            var options = new DuplicateScanOptions(new[] { root }, minFileSizeBytes: 1, DuplicateExtensionPreset.All);
            var engine = new DuplicateScanEngine();

            var result = await engine.ScanAsync(options, progress: null, CancellationToken.None);

            Assert.Single(result);
            Assert.Equal(2, result[0].Files.Count);
            Assert.Equal(result[0].Files[0].LengthBytes, result[0].Files[1].LengthBytes);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
