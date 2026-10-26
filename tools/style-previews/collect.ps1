<#
.SYNOPSIS
  Style previews: shrinks a previews run's pictures into UI/src/assets/previews/<type>__<style>__<view>.jpg (800 x 400, JPEG 80, lightly sharpened),
  the files the panel's StylePreview bundles. Made with tools/style-previews/make-scenarios.js.
.EXAMPLE
  powershell -File tools/style-previews/collect.ps1 -Run C:\Users\jack\cs2-area-tool\.forensics\suite\runs\send-previews-parks-1025-2120
#>
param([Parameter(Mandatory = $true)][string[]]$Run, [int]$Width = 800, [int]$Quality = 80)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\..\UI\src\assets\previews'
New-Item -ItemType Directory -Force $out | Out-Null
$codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$ep = New-Object System.Drawing.Imaging.EncoderParameters(1)
$ep.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, [long]$Quality)
$n = 0
# a light sharpen after the shrink (the user: "up the sharpness ever so slightly"): centre 1 + 4a, its four neighbours -a
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class PmSharpen {
    public static void Apply(Bitmap bmp, float a) {
        var r = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var d = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int n = d.Stride * bmp.Height; var src = new byte[n]; Marshal.Copy(d.Scan0, src, 0, n); var dst = (byte[])src.Clone();
        for (int y = 1; y < bmp.Height - 1; y++)
            for (int x = 1; x < bmp.Width - 1; x++) {
                int i = y * d.Stride + x * 4;
                for (int c = 0; c < 3; c++) {
                    float v = src[i + c] * (1 + 4 * a) - a * (src[i + c - 4] + src[i + c + 4] + src[i + c - d.Stride] + src[i + c + d.Stride]);
                    dst[i + c] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v)));
                }
            }
        Marshal.Copy(dst, 0, d.Scan0, n); bmp.UnlockBits(d);
    }
}
'@
# the grade: saturation 1.12 (luminance weights 0.3086 / 0.6094 / 0.0820), contrast 1.08 round mid grey, brightness +0.025
$sat = 1.12; $con = 1.08; $off = 0.5 * (1 - $con) + 0.025
$lr = 0.3086 * (1 - $sat); $lg = 0.6094 * (1 - $sat); $lb = 0.0820 * (1 - $sat)
$m = New-Object 'single[][]' 5
# (each entry its own value first: in PowerShell the comma binds tighter than *)
$a0 = ($lr + $sat) * $con; $a1 = $lr * $con
$b0 = $lg * $con; $b1 = ($lg + $sat) * $con
$c0 = $lb * $con; $c1 = ($lb + $sat) * $con
$m[0] = [single[]]($a0, $a1, $a1, 0, 0)
$m[1] = [single[]]($b0, $b1, $b0, 0, 0)
$m[2] = [single[]]($c0, $c0, $c1, 0, 0)
$m[3] = [single[]](0, 0, 0, 1, 0)
$m[4] = [single[]]($off, $off, $off, 0, 1)
$grade = New-Object System.Drawing.Imaging.ColorMatrix (, $m)
foreach ($r in $Run) {
    foreach ($f in Get-ChildItem (Join-Path $r 'shots') -Filter '*.png') {
        # NN-prv-<type>-<style>.NN-<view>.png (a trial run's ids end in -t)
        if ($f.Name -notmatch '^\d+-prv-([A-Za-z0-9]+)-(.+?)(-t)?\.\d+-(close|iso|top)\.png$') { continue }
        $name = "$($Matches[1])__$($Matches[2])__$($Matches[4]).jpg"
        $src = [System.Drawing.Image]::FromFile($f.FullName)
        # 2:1 (StylePreview's box has the same shape, smaller in the panel than 16:9; the user, 25 Oct). Close and top: the full
        # width, an even trim top and bottom. Iso (its camera leaves spare grass at the top): cropped in a touch, 95 % of the
        # width, the window 4/5 of the way down.
        $iso = $Matches[4] -eq 'iso'
        $sw = if ($iso) { [int]($src.Width * 0.95) } else { $src.Width }
        $sh = [int]($sw / 2); $sx = [int](($src.Width - $sw) / 2)
        $sy = [int](($src.Height - $sh) * $(if ($iso) { 0.8 } else { 0.5 }))
        $h = [int]($sh * $Width / $sw)
        $bmp = New-Object System.Drawing.Bitmap $Width, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.SmoothingMode = 'HighQuality'
        # a mild grade, the same for every picture (the user, 25 Oct): contrast, saturation and brightness a touch up
        $ia = New-Object System.Drawing.Imaging.ImageAttributes
        $ia.SetColorMatrix($grade)
        $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $Width, $h), $sx, $sy, $sw, $sh, [System.Drawing.GraphicsUnit]::Pixel, $ia)
        [PmSharpen]::Apply($bmp, 0.12)
        # a soft vignette: the corners a little darker
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddEllipse([int](-$Width * 0.25), [int](-$h * 0.25), [int]($Width * 1.5), [int]($h * 1.5))
        $vb = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
        $vb.CenterColor = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)
        $vb.SurroundColors = @([System.Drawing.Color]::FromArgb(110, 0, 0, 0))
        $blend = New-Object System.Drawing.Drawing2D.Blend
        $blend.Positions = [single[]](0, 0.35, 1)
        $blend.Factors = [single[]](0, 0.55, 1)
        $vb.Blend = $blend
        $g.FillRectangle($vb, 0, 0, $Width, $h)
        $vb.Dispose(); $path.Dispose(); $ia.Dispose()
        $bmp.Save((Join-Path $out $name), $codec, $ep)
        $g.Dispose(); $bmp.Dispose(); $src.Dispose()
        $n++
    }
}
$kb = [int]((Get-ChildItem $out -Filter *.jpg | Measure-Object Length -Sum).Sum / 1KB)
Write-Host "$n pictures into $out ($kb KB in all)"
