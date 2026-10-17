#requires -Version 7
# Puts before and after screenshots of the same states side by side, with every changed pixel marked, so the
# ui-check skill's before/after look finds side effects next to the intended change. It reports; it judges
# nothing and never fails.
#   -Before <dir>  shots of the base build (run-sandbox.ps1 -Ref main), one PNG per state
#   -After <dir>   shots of the same states, with the same file names, of the branch's build
# Each image in <After>\compared is before | after | the after shot faded, with changed pixels in magenta. A pixel
# counts as changed when a colour channel differs by more than 40 of 255, which ignores the faint shimmer of
# the window's translucent backdrop. Shots of different sizes are put side by side unmarked.
param(
    [Parameter(Mandatory)][string]$Before,
    [Parameter(Mandatory)][string]$After
)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
# Newer .NET splits GDI+ out of System.Drawing.Common; name it too where it exists.
$references = @("System.Drawing.Common", "System.Drawing.Primitives") +
    @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -like "System.Private.Windows.*" } |
        ForEach-Object Location)
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ShotDiff
{
    // Returns the share of changed pixels (0..1) and, when any changed, writes before | after | changes to sideBySide.
    public static double Compare(string beforePath, string afterPath, int tolerance, string sideBySide)
    {
        using var before = new Bitmap(beforePath);
        using var after = new Bitmap(afterPath);
        if (before.Width != after.Width || before.Height != after.Height)
        {
            Save(before, after, null, sideBySide);
            return 1;
        }

        var a = Pixels(before);
        var b = Pixels(after);
        var changed = new bool[a.Length / 4];
        var count = 0;
        for (var i = 0; i < changed.Length; i++)
        {
            var p = i * 4;
            if (Math.Abs(a[p] - b[p]) > tolerance || Math.Abs(a[p + 1] - b[p + 1]) > tolerance
                || Math.Abs(a[p + 2] - b[p + 2]) > tolerance)
            {
                changed[i] = true;
                count++;
            }
        }

        var share = (double)count / changed.Length;
        if (count > 0)
        {
            Save(before, after, changed, sideBySide);
        }

        return share;
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bitmap.Width * 4, bitmap.Width * 4);
            }

            return bytes;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void Save(Bitmap before, Bitmap after, bool[] changed, string path)
    {
        var width = before.Width + after.Width * 2;
        var height = Math.Max(before.Height, after.Height);
        using var output = new Bitmap(width, height);
        using (var g = Graphics.FromImage(output))
        {
            g.Clear(Color.White);
            g.DrawImage(before, 0, 0, before.Width, before.Height);
            g.DrawImage(after, before.Width, 0, after.Width, after.Height);
            // The after shot faded, with every changed pixel in magenta.
            using var faded = new ImageAttributes();
            faded.SetColorMatrix(new ColorMatrix { Matrix33 = 0.35f });
            g.DrawImage(after, new Rectangle(before.Width + after.Width, 0, after.Width, after.Height),
                0, 0, after.Width, after.Height, GraphicsUnit.Pixel, faded);
        }

        if (changed is not null)
        {
            for (var i = 0; i < changed.Length; i++)
            {
                if (changed[i])
                {
                    output.SetPixel(before.Width + after.Width + i % after.Width, i / after.Width, Color.Magenta);
                }
            }
        }

        output.Save(path, ImageFormat.Png);
    }
}
'@

$beforeDir = (Resolve-Path $Before).Path
$afterDir = (Resolve-Path $After).Path
$comparedDir = Join-Path $afterDir 'compared'
Remove-Item $comparedDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $comparedDir | Out-Null

$beforeShots = @(Get-ChildItem $beforeDir -Filter *.png -File | ForEach-Object Name)
$afterShots = @(Get-ChildItem $afterDir -Filter *.png -File | ForEach-Object Name)
foreach ($name in $afterShots | Where-Object { $_ -in $beforeShots } | Sort-Object) {
    $share = [ShotDiff]::Compare((Join-Path $beforeDir $name), (Join-Path $afterDir $name), 40,
        (Join-Path $comparedDir $name))
    Write-Host ("{0,7:0.00}%  {1}" -f ($share * 100), $name)
}
foreach ($name in $afterShots | Where-Object { $_ -notin $beforeShots } | Sort-Object) { Write-Host "no before  $name" }
foreach ($name in $beforeShots | Where-Object { $_ -notin $afterShots } | Sort-Object) { Write-Host "no after   $name" }
Write-Host "Side by side: $comparedDir (an identical pair writes no image)"
