<#
.SYNOPSIS
  Style previews: shrinks a previews run's pictures into UI/src/assets/previews/<type>__<style>__<view>.jpg (480 x 270, JPEG 78),
  the files the panel's StylePreview bundles. Made with tools/style-previews/make-scenarios.js.
.EXAMPLE
  powershell -File tools/style-previews/collect.ps1 -Run C:\Users\jack\cs2-area-tool\.forensics\suite\runs\send-previews-parks-1025-2120
#>
param([Parameter(Mandatory = $true)][string[]]$Run, [int]$Width = 480, [int]$Quality = 78)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\..\UI\src\assets\previews'
New-Item -ItemType Directory -Force $out | Out-Null
$codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$ep = New-Object System.Drawing.Imaging.EncoderParameters(1)
$ep.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, [long]$Quality)
$n = 0
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
        $h = [int]($src.Height * $Width / $src.Width)
        $bmp = New-Object System.Drawing.Bitmap $Width, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.SmoothingMode = 'HighQuality'
        # a mild grade, the same for every picture (the user, 25 Oct): contrast, saturation and brightness a touch up
        $ia = New-Object System.Drawing.Imaging.ImageAttributes
        $ia.SetColorMatrix($grade)
        $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $Width, $h), 0, 0, $src.Width, $src.Height, [System.Drawing.GraphicsUnit]::Pixel, $ia)
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
