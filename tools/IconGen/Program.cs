using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace JunkCleaner.IconGen;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var repoRoot = FindRepoRoot();
        var svgPath = args.Length >= 1
            ? Path.GetFullPath(args[0])
            : Path.Combine(repoRoot, "JunkCleaner", "Ui", "Frame.svg");
        var outIco = args.Length >= 2
            ? Path.GetFullPath(args[1])
            : Path.Combine(repoRoot, "JunkCleaner", "Ui", "app.ico");

        if (!File.Exists(svgPath))
            throw new FileNotFoundException("SVG not found.", svgPath);

        var settings = new WpfDrawingSettings { IncludeRuntime = true };
        var reader = new FileSvgReader(settings);
        reader.Read(svgPath);
        var drawing = reader.Drawing ?? throw new InvalidOperationException("SVG failed to load.");
        drawing.Freeze();

        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var parts = new List<(int Size, byte[] PngBytes)>();
        foreach (var s in sizes)
            parts.Add((s, RenderPng(drawing, s)));

        IcoFileWriter.Write(outIco, parts);
        Console.WriteLine("OK: " + outIco);
        return 0;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "JunkCleaner.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static byte[] RenderPng(DrawingGroup drawing, int size)
    {
        var bounds = drawing.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("Invalid SVG bounds.");

        var scale = Math.Min(size / bounds.Width, size / bounds.Height);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(
                new TranslateTransform(
                    (size - bounds.Width * scale) * 0.5 - bounds.Left * scale,
                    (size - bounds.Height * scale) * 0.5 - bounds.Top * scale));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawDrawing(drawing);
            dc.Pop();
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
