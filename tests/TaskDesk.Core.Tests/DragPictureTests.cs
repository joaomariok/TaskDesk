using TaskDesk.Core.Shell;

namespace TaskDesk.Core.Tests;

public class DragPictureTests
{
    // Backing #202020 (Dark's solid window background), as opaque premultiplied BGRA.
    private static readonly byte[] Backing = [0x20, 0x20, 0x20, 0xFF];

    private static byte[] Pixel(byte b, byte g, byte r, byte a) => [b, g, r, a];

    private static byte[] Repeat(byte[] pixel, int count) =>
        [.. Enumerable.Range(0, count).SelectMany(_ => pixel)];

    private static byte[] At(DragPicture picture, int x, int y) =>
        picture.Pixels.AsSpan(((y * picture.PixelWidth) + x) * 4, 4).ToArray();

    [Fact]
    public void Rows_stack_top_to_bottom_over_the_solid_backing()
    {
        var red = new RowPixels(Repeat(Pixel(0, 0, 0xFF, 0xFF), 2), 2, 1);
        var clear = new RowPixels(Repeat(Pixel(0, 0, 0, 0), 2), 2, 1);

        var picture = DragPicture.Compose([red, clear], Backing, fade: false);

        Assert.Equal((2, 2), (picture.PixelWidth, picture.PixelHeight));
        Assert.Equal(Pixel(0, 0, 0xFF, 0xFF), At(picture, 1, 0));
        Assert.Equal(Backing, At(picture, 0, 1));
    }

    [Fact]
    public void A_translucent_row_pixel_blends_over_the_backing()
    {
        // White at half coverage, premultiplied: 0x80 in each channel.
        var half = new RowPixels(Pixel(0x80, 0x80, 0x80, 0x80), 1, 1);

        var picture = DragPicture.Compose([half], Backing, fade: false);

        // 0x80 + 0x20 * (1 - 0x80 / 255) = 0x80 + 0x10 = 0x90, fully opaque.
        Assert.Equal(Pixel(0x90, 0x90, 0x90, 0xFF), At(picture, 0, 0));
    }

    [Fact]
    public void A_narrower_row_leaves_the_backing_to_its_right()
    {
        var wide = new RowPixels(Repeat(Pixel(0, 0, 0xFF, 0xFF), 3), 3, 1);
        var narrow = new RowPixels(Pixel(0, 0xFF, 0, 0xFF), 1, 1);

        var picture = DragPicture.Compose([wide, narrow], Backing, fade: false);

        Assert.Equal(3, picture.PixelWidth);
        Assert.Equal(Pixel(0, 0xFF, 0, 0xFF), At(picture, 0, 1));
        Assert.Equal(Backing, At(picture, 2, 1));
    }

    [Fact]
    public void A_faded_picture_keeps_its_top_and_fades_its_bottom_out()
    {
        // Ten rows of one opaque white pixel each, faded: the bottom two fifths fall to nothing.
        var white = new RowPixels(Pixel(0xFF, 0xFF, 0xFF, 0xFF), 1, 1);

        var picture = DragPicture.Compose([.. Enumerable.Repeat(white, 10)], Backing, fade: true);

        Assert.Equal(Pixel(0xFF, 0xFF, 0xFF, 0xFF), At(picture, 0, 0));
        Assert.Equal(Pixel(0xFF, 0xFF, 0xFF, 0xFF), At(picture, 0, 5));
        var bottom = At(picture, 0, 9);
        Assert.InRange(bottom[3], (byte)1, (byte)40);
        Assert.Equal(bottom[3], bottom[0]); // Premultiplied: the colour fades with the alpha.
        Assert.True(At(picture, 0, 7)[3] > bottom[3]);
    }

    [Fact]
    public void An_empty_picture_has_no_pixels()
    {
        var picture = DragPicture.Compose([], Backing, fade: false);

        Assert.Equal((0, 0), (picture.PixelWidth, picture.PixelHeight));
        Assert.Empty(picture.Pixels);
    }
}
