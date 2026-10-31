using SkiaSharp;

namespace WhatsAppClient.App.Services;

/// <summary>
/// Composes the branded alert image used as the <c>home_snapshot</c> template's image header:
/// the Whisperer banner on top, with any camera snapshots tiled below it. This is what lets a
/// single image header carry both the branding and (optionally) several snapshots, since a
/// WhatsApp template header holds exactly one image and the body is text-only.
/// </summary>
public interface IAlertImageComposer
{
    /// <summary>
    /// Returns JPEG bytes of the banner with <paramref name="snapshots"/> tiled beneath it.
    /// With no snapshots the result is just the banner. Snapshot bytes that can't be decoded as
    /// an image are skipped.
    /// </summary>
    byte[] Compose(IReadOnlyList<byte[]> snapshots);
}

public sealed class AlertImageComposer : IAlertImageComposer
{
    /// <summary>Output width; a WhatsApp-friendly header size that the banner and tiles scale to.</summary>
    private const int CanvasWidth = 1080;
    private const int JpegQuality = 80;
    private static readonly SKColor Backing = new(10, 86, 95); // deep teal, shows in any unfilled cell

    // The banner ships embedded in this assembly (see the .csproj EmbeddedResource).
    private static readonly byte[] BannerBytes = LoadBanner();

    public byte[] Compose(IReadOnlyList<byte[]> snapshots)
    {
        using var banner = SKBitmap.Decode(BannerBytes)
            ?? throw new InvalidOperationException("Failed to decode the embedded alert banner.");
        var bannerHeight = (int)Math.Round(banner.Height * (CanvasWidth / (double)banner.Width));

        // Decode what we can; unreadable snapshots are dropped rather than failing the whole alert.
        var tiles = new List<SKBitmap>(snapshots.Count);
        foreach (var bytes in snapshots)
        {
            // SKBitmap.Decode returns null — or throws when no codec matches — for non-image bytes.
            SKBitmap? bmp = null;
            try { bmp = SKBitmap.Decode(bytes); }
            catch (Exception) { /* skip undecodable snapshot */ }
            if (bmp is not null) tiles.Add(bmp);
        }

        try
        {
            // Always a single column: each snapshot is a full-width 4:3 tile stacked under the banner.
            const int cols = 1;
            var rows = tiles.Count;
            var cellW = CanvasWidth / cols;
            var cellH = cellW * 3 / 4;

            var info = new SKImageInfo(CanvasWidth, bannerHeight + rows * cellH);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(Backing);

            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            canvas.DrawBitmap(banner,
                new SKRect(0, 0, banner.Width, banner.Height),
                new SKRect(0, 0, CanvasWidth, bannerHeight), paint);

            for (var i = 0; i < tiles.Count; i++)
            {
                var row = i / cols;
                var col = i % cols;
                var dest = new SKRect(col * cellW, bannerHeight + row * cellH,
                    col * cellW + cellW, bannerHeight + row * cellH + cellH);
                DrawCenterCropped(canvas, tiles[i], dest, paint);
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return data.ToArray();
        }
        finally
        {
            foreach (var t in tiles) t.Dispose();
        }
    }

    // Fills dest with the bitmap, center-cropping it to dest's aspect ratio (no stretching).
    private static void DrawCenterCropped(SKCanvas canvas, SKBitmap bmp, SKRect dest, SKPaint paint)
    {
        var destAr = dest.Width / dest.Height;
        var srcAr = bmp.Width / (float)bmp.Height;
        float sx, sy, sw, sh;
        if (srcAr > destAr) // source is wider → crop the sides
        {
            sh = bmp.Height;
            sw = bmp.Height * destAr;
            sx = (bmp.Width - sw) / 2f;
            sy = 0;
        }
        else                // source is taller → crop top/bottom
        {
            sw = bmp.Width;
            sh = bmp.Width / destAr;
            sx = 0;
            sy = (bmp.Height - sh) / 2f;
        }
        canvas.DrawBitmap(bmp, new SKRect(sx, sy, sx + sw, sy + sh), dest, paint);
    }

    private static byte[] LoadBanner()
    {
        var asm = typeof(AlertImageComposer).Assembly;
        const string name = "WhatsAppClient.App.Assets.alert-banner.jpg";
        using var s = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded alert banner '{name}' not found.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
