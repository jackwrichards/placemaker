<#
.SYNOPSIS
  Style previews: shrinks a previews run's pictures into UI/src/assets/previews/<type>~<style>~<view>.jpg (480 x 270, JPEG 78),
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
foreach ($r in $Run) {
    foreach ($f in Get-ChildItem (Join-Path $r 'shots') -Filter '*.png') {
        # NN-prv-<type>-<style>.NN-<view>.png (a trial run's ids end in -t)
        if ($f.Name -notmatch '^\d+-prv-([A-Za-z0-9]+)-(.+?)(-t)?\.\d+-(close|iso|top)\.png$') { continue }
        $name = "$($Matches[1])~$($Matches[2])~$($Matches[4]).jpg"
        $src = [System.Drawing.Image]::FromFile($f.FullName)
        $h = [int]($src.Height * $Width / $src.Width)
        $bmp = New-Object System.Drawing.Bitmap $Width, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.DrawImage($src, 0, 0, $Width, $h)
        $bmp.Save((Join-Path $out $name), $codec, $ep)
        $g.Dispose(); $bmp.Dispose(); $src.Dispose()
        $n++
    }
}
$kb = [int]((Get-ChildItem $out -Filter *.jpg | Measure-Object Length -Sum).Sum / 1KB)
Write-Host "$n pictures into $out ($kb KB in all)"
