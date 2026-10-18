using System.Globalization;
using Microsoft.UI;
using Windows.UI;

namespace TaskDesk.Views;

/// <summary>A List's colour as stored: "#RRGGBB".</summary>
public static class ListColour
{
    /// <summary>Reads "#RRGGBB" as an opaque colour; false for null, empty or any other form.</summary>
    public static bool TryParse(string? hex, out Color colour)
    {
        colour = default;
        if (hex is not { Length: 7 } || hex[0] != '#'
            || !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        colour = ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}
