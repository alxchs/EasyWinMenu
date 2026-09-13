using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Draws the Easy Windows & Menus icon from vectors and packs it into a .ico.
//
// Every size is rendered on its own rather than scaled down from 256px: a downscaled
// icon turns its gaps into grey mush at 16px, which is exactly where a tray icon lives.
// Small sizes also drop detail and snap the tile gaps to whole pixels.
//
//   dotnet run --project tools/IconForge -- <output.ico> [preview-dir] [variant]

internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("uso: IconForge <saida.ico> [pasta-de-previa] [variante]");
            return 2;
        }

        var output = Path.GetFullPath(args[0]);
        var previewDir = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
        var variant = args.Length > 2 ? args[2] : "tiles";

        var palette = Palette.For(variant);
        var frames = Sizes.ToDictionary(size => size, size => Render(size, palette));

        IcoWriter.Write(output, frames);
        Console.WriteLine($"icone: {output}");

        if (previewDir is not null)
        {
            Directory.CreateDirectory(previewDir);
            WritePng(Path.Combine(previewDir, $"{variant}-256.png"), frames[256]);
            WritePng(Path.Combine(previewDir, $"{variant}-sheet.png"), ContactSheet(frames));
            Console.WriteLine($"previa: {previewDir}");
        }

        return 0;
    }

    private static BitmapSource Render(int size, Palette palette)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Draw(dc, size, palette);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Draw(DrawingContext dc, double size, Palette palette)
    {
        var small = size <= 24;

        // Plate: a squircle-ish rounded square, lit from the top-left.
        var inset = small ? 0.5 : size * 0.04;
        var plate = new Rect(inset, inset, size - (inset * 2), size - (inset * 2));
        var plateRadius = plate.Width * 0.24;

        var plateBrush = new LinearGradientBrush(palette.PlateTop, palette.PlateBottom, new Point(0, 0), new Point(1, 1));
        plateBrush.Freeze();
        dc.DrawRoundedRectangle(plateBrush, null, plate, plateRadius, plateRadius);

        if (!small)
        {
            // A soft sheen across the top half gives the plate some volume.
            var sheen = new LinearGradientBrush(
                Color.FromArgb(70, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                new Point(0.5, 0),
                new Point(0.5, 0.55));
            sheen.Freeze();
            dc.DrawRoundedRectangle(sheen, null, plate, plateRadius, plateRadius);
        }

        if (palette.Rim is { } rim)
        {
            var pen = new Pen(new SolidColorBrush(rim), Math.Max(1, size / 64));
            pen.Freeze();
            var edge = Math.Max(0.5, pen.Thickness / 2);
            var rimRect = new Rect(plate.X + edge, plate.Y + edge, plate.Width - (edge * 2), plate.Height - (edge * 2));
            dc.DrawRoundedRectangle(null, pen, rimRect, plateRadius - edge, plateRadius - edge);
        }

        // Four tiles: the panes of a window, and the groups the app arranges them into.
        var margin = plate.Width * (small ? 0.2 : 0.22);
        var gap = Snap(small ? Math.Max(1, size * 0.08) : plate.Width * 0.08, small);
        var area = plate.Width - (margin * 2);
        var tile = Snap((area - gap) / 2, small);
        var originX = Snap(plate.X + ((plate.Width - ((tile * 2) + gap)) / 2), small);
        var originY = originX;
        var tileRadius = small ? Math.Max(1, tile * 0.22) : tile * 0.3;

        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                var rect = new Rect(originX + (column * (tile + gap)), originY + (row * (tile + gap)), tile, tile);
                var isAccent = row == 1 && column == 1;
                var brush = new SolidColorBrush(isAccent ? palette.Accent : palette.Tile);
                brush.Freeze();
                dc.DrawRoundedRectangle(brush, null, rect, tileRadius, tileRadius);

                if (isAccent && size >= 48)
                {
                    DrawSparkle(dc, rect, palette.Sparkle);
                }
            }
        }
    }

    /// <summary>
    /// A four-pointed sparkle on the highlighted tile: the "easy" in the name. Only drawn
    /// where there are enough pixels for it to read as a shape rather than a smudge.
    /// </summary>
    private static void DrawSparkle(DrawingContext dc, Rect tile, Color color)
    {
        var cx = tile.X + (tile.Width / 2);
        var cy = tile.Y + (tile.Height / 2);
        var reach = tile.Width * 0.3;
        var waist = reach * 0.28;

        var figure = new PathFigure { StartPoint = new Point(cx, cy - reach), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new QuadraticBezierSegment(new Point(cx + waist, cy - waist), new Point(cx + reach, cy), true));
        figure.Segments.Add(new QuadraticBezierSegment(new Point(cx + waist, cy + waist), new Point(cx, cy + reach), true));
        figure.Segments.Add(new QuadraticBezierSegment(new Point(cx - waist, cy + waist), new Point(cx - reach, cy), true));
        figure.Segments.Add(new QuadraticBezierSegment(new Point(cx - waist, cy - waist), new Point(cx, cy - reach), true));

        var geometry = new PathGeometry([figure]);
        geometry.Freeze();

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private static double Snap(double value, bool small) => small ? Math.Round(value) : value;

    private static BitmapSource ContactSheet(IReadOnlyDictionary<int, BitmapSource> frames)
    {
        // Every size on a light and on a dark strip, because the tray can be either.
        const int padding = 16;
        var width = frames.Keys.Sum(size => size + padding) + padding;
        var height = (256 + (padding * 2)) * 2;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)), null, new Rect(0, 0, width, height / 2.0));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)), null, new Rect(0, height / 2.0, width, height / 2.0));

            foreach (var strip in new[] { 0.0, height / 2.0 })
            {
                double x = padding;
                foreach (var (size, frame) in frames.OrderBy(pair => pair.Key))
                {
                    var y = strip + padding + ((256 - size) / 2.0);
                    dc.DrawImage(frame, new Rect(x, y, size, size));
                    x += size + padding;
                }
            }
        }

        var sheet = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        sheet.Render(visual);
        return sheet;
    }

    private static void WritePng(string path, BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

internal sealed record Palette(Color PlateTop, Color PlateBottom, Color Tile, Color Accent, Color Sparkle, Color? Rim)
{
    public static Palette For(string variant) => variant switch
    {
        // Bright friendly blue with a warm highlighted tile.
        "tiles" => new Palette(
            Color.FromRgb(0x5F, 0xC0, 0xFF),
            Color.FromRgb(0x1F, 0x5F, 0xE0),
            Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xFF, 0xC4, 0x3D),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            null),

        // Black and cyan, the look of the phone this app takes after; the rim keeps it
        // from sinking into a dark taskbar.
        "cyan" => new Palette(
            Color.FromRgb(0x14, 0x1B, 0x22),
            Color.FromRgb(0x05, 0x08, 0x0B),
            Color.FromRgb(0x00, 0xE5, 0xFF),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0x00, 0x9C, 0xB3),
            Color.FromArgb(0x90, 0x00, 0xE5, 0xFF)),

        // Violet to blue, closer to the Windows 11 wallpaper family.
        "violet" => new Palette(
            Color.FromRgb(0x8B, 0x5C, 0xF6),
            Color.FromRgb(0x25, 0x63, 0xEB),
            Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF),
            Color.FromRgb(0x5E, 0xEA, 0xD4),
            Color.FromRgb(0x1E, 0x3A, 0x8A),
            null),

        _ => throw new ArgumentException($"variante desconhecida: {variant}")
    };
}

/// <summary>
/// Writes a multi-resolution .ico. Frames below 256px are stored as 32-bit DIBs, which
/// every consumer understands — System.Drawing.Icon, used for the tray, is unreliable
/// with PNG-compressed small frames. Only the 256px frame is stored as PNG, as Windows
/// itself does, since an uncompressed one would be a quarter of a megabyte.
/// </summary>
internal static class IcoWriter
{
    public static void Write(string path, IReadOnlyDictionary<int, BitmapSource> frames)
    {
        var ordered = frames.OrderBy(pair => pair.Key).ToList();
        var payloads = ordered.Select(pair => pair.Key >= 256 ? EncodePng(pair.Value) : EncodeDib(pair.Value)).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);            // reserved
        writer.Write((ushort)1);            // type: icon
        writer.Write((ushort)ordered.Count);

        var offset = 6 + (16 * ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var size = ordered[i].Key;
            writer.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);          // palette colours
            writer.Write((byte)0);          // reserved
            writer.Write((ushort)1);        // colour planes
            writer.Write((ushort)32);       // bits per pixel
            writer.Write(payloads[i].Length);
            writer.Write(offset);
            offset += payloads[i].Length;
        }

        foreach (var payload in payloads)
        {
            writer.Write(payload);
        }
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }

    private static byte[] EncodeDib(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;

        // Straight (non-premultiplied) BGRA is what an icon DIB expects.
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);

        var maskRowBytes = ((width + 31) / 32) * 4;

        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);

        writer.Write(40);                   // BITMAPINFOHEADER size
        writer.Write(width);
        writer.Write(height * 2);           // colour rows plus mask rows
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);                    // BI_RGB
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        // DIB rows run bottom-up.
        for (var y = height - 1; y >= 0; y--)
        {
            writer.Write(pixels, y * width * 4, width * 4);
        }

        // The alpha channel carries transparency, so the AND mask stays all zero.
        writer.Write(new byte[maskRowBytes * height]);

        return memory.ToArray();
    }
}
