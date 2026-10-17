#requires -Version 7
# Compares two screenshot tours (scripts/ui-tests.ps1 -Screenshots) and lists the shots that changed, so every
# visual change gets looked at, not only the ones a ticket names. It reports; it never fails.
#   -Baseline <dir>  the tour to compare against, usually one taken on main
#   -Current <dir>   the new tour; side-by-side images go to <Current>\changed
#   -Threshold <n>   share of changed pixels, in percent, above which a shot counts as changed
# A pixel counts as changed when a colour channel differs by more than 40 of 255, which ignores the faint
# shimmer of the window's translucent backdrop. Each side-by-side image is baseline | current | changes in magenta.
param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Current,
    [double]$Threshold = 0.3
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

public static class TourDiff
{
    // Returns the share of changed pixels (0..1) and writes baseline | current | changes to sideBySide.
    public static double Compare(string baselinePath, string currentPath, int tolerance, string sideBySide)
    {
        using var baseline = new Bitmap(baselinePath);
        using var current = new Bitmap(currentPath);
        if (baseline.Width != current.Width || baseline.Height != current.Height)
        {
            Save(baseline, current, null, sideBySide);
            return 1;
        }

        var a = Pixels(baseline);
        var b = Pixels(current);
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
            Save(baseline, current, changed, sideBySide);
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

    private static void Save(Bitmap baseline, Bitmap current, bool[] changed, string path)
    {
        var width = baseline.Width + current.Width * 2;
        var height = Math.Max(baseline.Height, current.Height);
        using var output = new Bitmap(width, height);
        using (var g = Graphics.FromImage(output))
        {
            g.Clear(Color.White);
            g.DrawImage(baseline, 0, 0, baseline.Width, baseline.Height);
            g.DrawImage(current, baseline.Width, 0, current.Width, current.Height);
            // The current shot faded, with every changed pixel in magenta.
            using var faded = new ImageAttributes();
            faded.SetColorMatrix(new ColorMatrix { Matrix33 = 0.35f });
            g.DrawImage(current, new Rectangle(baseline.Width + current.Width, 0, current.Width, current.Height),
                0, 0, current.Width, current.Height, GraphicsUnit.Pixel, faded);
        }

        if (changed is not null)
        {
            for (var i = 0; i < changed.Length; i++)
            {
                if (changed[i])
                {
                    output.SetPixel(baseline.Width + current.Width + i % current.Width, i / current.Width, Color.Magenta);
                }
            }
        }

        output.Save(path, ImageFormat.Png);
    }
}
'@

$baselineDir = (Resolve-Path $Baseline).Path
$currentDir = (Resolve-Path $Current).Path
$changedDir = Join-Path $currentDir 'changed'
Remove-Item $changedDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $changedDir | Out-Null

$baselineShots = @(Get-ChildItem $baselineDir -Filter *.png -File | ForEach-Object Name)
$currentShots = @(Get-ChildItem $currentDir -Filter *.png -File | ForEach-Object Name)
$changed = 0
foreach ($name in $currentShots | Sort-Object) {
    if ($name -notin $baselineShots) {
        Write-Host "NEW      $name"
        $changed++
        continue
    }

    $share = [TourDiff]::Compare((Join-Path $baselineDir $name), (Join-Path $currentDir $name), 40,
        (Join-Path $changedDir $name))
    $percent = [math]::Round($share * 100, 2)
    if ($percent -gt $Threshold) {
        Write-Host ("CHANGED  {0}  {1}% of pixels" -f $name, $percent)
        $changed++
    }
    else {
        Write-Host ("same     {0}  {1}%" -f $name, $percent)
        Remove-Item (Join-Path $changedDir $name) -ErrorAction SilentlyContinue
    }
}
foreach ($name in $baselineShots | Where-Object { $_ -notin $currentShots } | Sort-Object) {
    Write-Host "MISSING  $name"
    $changed++
}

Write-Host ($changed -eq 0 ? 'Tour unchanged.' : "$changed shot(s) changed; check each by eye: $changedDir")
