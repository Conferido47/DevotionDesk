using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace IconGen;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Usage:
        // dotnet run --project tools/IconGen/IconGen.csproj -- <svgPath> <icoOutPath>
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: IconGen <svgPath> <icoOutPath>");
            return 2;
        }

        var svgPath = args[0];
        var icoPath = args[1];

        if (!File.Exists(svgPath))
        {
            Console.Error.WriteLine($"SVG not found: {svgPath}");
            return 3;
        }

        var drawing = LoadSvgDrawing(svgPath);
        if (drawing == null)
        {
            Console.Error.WriteLine("Failed to render SVG.");
            return 4;
        }

        // Common icon sizes.
        var sizes = new[] { 16, 32, 48, 64, 128, 256 };
        var pngBlobs = sizes.Select(s => RenderPng(drawing, s, s)).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(icoPath))!);
        WriteIco(icoPath, sizes, pngBlobs);

        Console.WriteLine($"Wrote: {icoPath}");
        return 0;
    }

    private static DrawingGroup? LoadSvgDrawing(string svgPath)
    {
        using var svgReader = new FileSvgReader(new WpfDrawingSettings
        {
            TextAsGeometry = true,
            OptimizePath = true
        }, isEmbedded: true);

        var drawing = svgReader.Read(svgPath);
        if (drawing == null)
            return null;

        drawing.Freeze();
        return drawing;
    }

    private static byte[] RenderPng(DrawingGroup drawing, int pixelWidth, int pixelHeight)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bounds = drawing.Bounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
                bounds = new Rect(0, 0, 1024, 1024);

            // Keep aspect ratio; center.
            var scale = Math.Min(pixelWidth / bounds.Width, pixelHeight / bounds.Height);
            var w = bounds.Width * scale;
            var h = bounds.Height * scale;
            var x = (pixelWidth - w) / 2.0;
            var y = (pixelHeight - h) / 2.0;

            dc.PushTransform(new TranslateTransform(x - bounds.X * scale, y - bounds.Y * scale));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawDrawing(drawing);
            dc.Pop();
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static void WriteIco(string icoPath, int[] sizes, List<byte[]> pngBlobs)
    {
        // ICO file containing PNG images.
        using var fs = File.Create(icoPath);
        using var bw = new BinaryWriter(fs);

        bw.Write((ushort)0);     // reserved
        bw.Write((ushort)1);     // type = icon
        bw.Write((ushort)sizes.Length);

        var dirEntryStart = fs.Position;
        var imageDataOffset = (int)(6 + (16 * sizes.Length));
        var offsets = new int[sizes.Length];
        var lengths = new int[sizes.Length];

        var runningOffset = imageDataOffset;
        for (var i = 0; i < sizes.Length; i++)
        {
            var s = sizes[i];
            var blob = pngBlobs[i];
            offsets[i] = runningOffset;
            lengths[i] = blob.Length;
            runningOffset += blob.Length;

            bw.Write((byte)(s >= 256 ? 0 : s));
            bw.Write((byte)(s >= 256 ? 0 : s));
            bw.Write((byte)0);      // palette
            bw.Write((byte)0);      // reserved
            bw.Write((ushort)1);    // planes
            bw.Write((ushort)32);   // bit count
            bw.Write((uint)blob.Length);
            bw.Write((uint)offsets[i]);
        }

        // Image data
        for (var i = 0; i < sizes.Length; i++)
            bw.Write(pngBlobs[i]);
    }
}
