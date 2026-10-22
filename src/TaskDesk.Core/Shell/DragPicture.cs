namespace TaskDesk.Core.Shell;

/// <summary>One row's rendered pixels: premultiplied BGRA, top to bottom, <paramref name="Width"/> by <paramref name="Height"/>.</summary>
public sealed record RowPixels(byte[] Pixels, int Width, int Height);

/// <summary>A drag picture's pixels: premultiplied BGRA, top to bottom.</summary>
public sealed record DragPicture(byte[] Pixels, int PixelWidth, int PixelHeight)
{
    /// <summary>
    /// Stacks <paramref name="rows"/> top to bottom over an opaque <paramref name="backing"/> colour (BGRA), as wide
    /// as the widest row. With <paramref name="fade"/>, the picture fades out at its bottom (<see cref="DragRules.FadeAlpha"/>).
    /// </summary>
    public static DragPicture Compose(IReadOnlyList<RowPixels> rows, byte[] backing, bool fade)
    {
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Width);
        var height = rows.Sum(r => r.Height);
        var pixels = new byte[width * height * 4];

        var top = 0;
        foreach (var row in rows)
        {
            for (var y = 0; y < row.Height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var to = (((top + y) * width) + x) * 4;
                    if (x >= row.Width)
                    {
                        backing.CopyTo(pixels, to);
                        continue;
                    }

                    // Premultiplied "over": the row pixel plus the backing it lets through.
                    var from = ((y * row.Width) + x) * 4;
                    var through = 255 - row.Pixels[from + 3];
                    for (var c = 0; c < 4; c++)
                    {
                        pixels[to + c] = (byte)Math.Min(255, row.Pixels[from + c] + ((backing[c] * through) + 127) / 255);
                    }
                }
            }

            top += row.Height;
        }

        if (fade)
        {
            for (var y = 0; y < height; y++)
            {
                var alpha = DragRules.FadeAlpha(y + 0.5, height);
                if (alpha >= 1)
                {
                    continue;
                }

                for (var i = y * width * 4; i < (y + 1) * width * 4; i++)
                {
                    pixels[i] = (byte)Math.Round(pixels[i] * alpha);
                }
            }
        }

        return new DragPicture(pixels, width, height);
    }
}
