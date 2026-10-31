using SkiaSharp;
using WhatsAppClient.App.Services;
using Xunit;

namespace WhatsAppClient.App.Tests;

public class AlertImageComposerTests
{
    private readonly AlertImageComposer _composer = new();

    // Banner is 1440x448 → scaled to 1080 wide = 336 tall. Tiles are 4:3.
    private const int Width = 1080;
    private const int BannerHeight = 336;

    private static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(new SKColor(200, 100, 50));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static (int W, int H) Dimensions(byte[] jpeg)
    {
        Assert.True(jpeg.Length > 2 && jpeg[0] == 0xFF && jpeg[1] == 0xD8, "output is not JPEG");
        using var bmp = SKBitmap.Decode(jpeg);
        return (bmp.Width, bmp.Height);
    }

    [Fact]
    public void Compose_NoSnapshots_IsBannerOnly()
    {
        var (w, h) = Dimensions(_composer.Compose(Array.Empty<byte[]>()));
        Assert.Equal(Width, w);
        Assert.Equal(BannerHeight, h);
    }

    [Fact]
    public void Compose_OneSnapshot_StacksFullWidthTileUnderBanner()
    {
        // 1 column → cell is 1080 wide, 810 tall.
        var (w, h) = Dimensions(_composer.Compose(new[] { Png(640, 480) }));
        Assert.Equal(Width, w);
        Assert.Equal(BannerHeight + 810, h);
    }

    [Fact]
    public void Compose_ThreeSnapshots_StacksThreeFullWidthTiles()
    {
        // 1 column → each cell 1080x810; 3 tiles → 3 rows → 2430 tall.
        var (w, h) = Dimensions(_composer.Compose(new[] { Png(640, 480), Png(640, 480), Png(640, 480) }));
        Assert.Equal(Width, w);
        Assert.Equal(BannerHeight + 810 * 3, h);
    }

    [Fact]
    public void Compose_SkipsUndecodableSnapshots()
    {
        // One real image + one garbage buffer → treated as a single valid tile (1 column).
        var (w, h) = Dimensions(_composer.Compose(new[] { Png(640, 480), new byte[] { 1, 2, 3, 4 } }));
        Assert.Equal(Width, w);
        Assert.Equal(BannerHeight + 810, h);
    }
}
