<#
.SYNOPSIS
  Uploads Placemaker to Paradox Mods with the game's own uploader (ModPublisher), from a clean release build.

.DESCRIPTION
  The project builds in standalone mode, so `dotnet publish` never reaches the toolchain's publish targets; this
  script does their job: a fresh release build (UI included) into bin\Publish\build, a content folder with only the
  mod's files (bin\Publish\Placemaker: no .pdb, no logs), then ModPublisher with Properties\PublishConfiguration.xml.
  It installs nothing into Mods\Placemaker.

  The uploader logs in with the Paradox account last signed in in the game (it never takes a password here).

    -Command Publish     the first upload; ModId must be empty. The new id is written back into the config.
    -Command NewVersion  a new version of the mod's files; needs ModId, a new ModVersion and a ChangeLog.
    -Command Update      the page only (text, pictures, links, AccessLevel); the files stay as they are.
    -StageOnly           build and stage, print the page that would go up, upload nothing.
    -PageApproved        the user has seen that page (-StageOnly) and said yes: without it nothing is uploaded.

  THE PAGE: every upload sends the whole mod page with it (Paradox keeps a page per version; there is no files-only
  upload). So the page lives here, as the user keeps it: Properties\PublishConfiguration.xml (name, short description,
  links, forum link, tags), Properties\ModPage\LongDescription.md (the description, copied into the config),
  Properties\Thumbnail.jpg and Properties\Screenshots\01-10.jpg. An edit made on the website is overwritten by the next
  upload: the page is changed here. (0.3.0 sent an old copy of the page; 0.3.2 sent the copy the upload API reads back,
  which is not the page the user edits on the website. Both rolled the user's page back, 26 Oct.)
#>
param(
    [ValidateSet('Publish', 'NewVersion', 'Update')][string]$Command = 'Publish',
    [switch]$StageOnly,
    [switch]$PageApproved
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$config = Join-Path $root 'Properties\PublishConfiguration.xml'
$buildDir = Join-Path $root 'bin\Publish\build'
$content = Join-Path $root 'bin\Publish\Placemaker'

# --- the configuration: catch what the uploader would silently get wrong ---
[xml]$x = Get-Content -Raw -Encoding utf8 -LiteralPath $config
$p = $x.Publish
$modId = "$($p.ModId.Value)".Trim()
$access = "$($p.AccessLevel.Value)"
$problems = @()
if ($access -cnotin @('Public', 'Unlisted', 'Private')) { $problems += "AccessLevel '$access' is not Public, Unlisted or Private (the uploader would make it Public)" }
if ($Command -eq 'Publish' -and $modId -and $modId -ne '0') { $problems += "ModId is ${modId}: the mod exists already; use -Command NewVersion or Update (Publish would make a second listing)" }
if ($Command -ne 'Publish' -and -not $modId) { $problems += "ModId is empty: $Command needs the mod's id" }
if ($Command -ne 'Publish' -and -not ("$($p.ChangeLog.Value)$($p.ChangeLog.InnerText)".Trim())) { $problems += 'ChangeLog is empty' }
$github = @($p.ExternalLink | Where-Object { $_.Type -eq 'github' -and $_.Url })
if ($access -ne 'Private' -and -not $github) { $problems += "AccessLevel is $access but there is no github ExternalLink: the GPL-3.0 needs the source public before anyone else gets the mod" }
$pictures = @($p.Thumbnail.Value) + @($p.Screenshot | ForEach-Object { $_.Value })
$shots = @($p.Screenshot | Where-Object { $_.Value })
if ($shots.Count -gt 10) { $problems += "$($shots.Count) screenshots: Paradox Mods takes at most 10" }
foreach ($pic in $pictures) { if ($pic -and -not (Test-Path -LiteralPath (Join-Path $root $pic))) { $problems += "missing picture: $pic" } }
# The description sent is Properties\ModPage\LongDescription.md, word for word
$md = Join-Path $root 'Properties\ModPage\LongDescription.md'
if (-not (Test-Path -LiteralPath $md)) { $problems += 'missing Properties\ModPage\LongDescription.md' }
elseif ((Get-Content -Raw -Encoding utf8 -LiteralPath $md).Replace("`r`n", "`n").Trim() -ne "$($p.LongDescription)".Replace("`r`n", "`n").Trim()) { $problems += 'the description in PublishConfiguration.xml is not Properties\ModPage\LongDescription.md: copy it in' }
if (-not $p.ForumLink.Value) { $problems += 'no ForumLink: the upload would clear the forum link on the page' }
# Every upload replaces the whole page with this one: only after the user said yes to it
if (-not $StageOnly -and -not $PageApproved) { $problems += 'every upload replaces the whole mod page with this one: show it with -StageOnly, and upload with -PageApproved only after the user said yes to it' }
if ($problems) { $problems | ForEach-Object { Write-Host "STOP: $_" }; exit 1 }

# --- a fresh release build and the content folder ---
if ($Command -ne 'Update') {
    foreach ($d in @($buildDir, $content)) { if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force } }
    $ui = Join-Path $root 'UI\build'
    if (Test-Path -LiteralPath $ui) { Remove-Item -LiteralPath $ui -Recurse -Force }
    & dotnet build (Join-Path $root 'Placemaker.csproj') -c Release -p:Deploy=false "-p:OutDir=$buildDir\" -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { Write-Host 'STOP: the build failed'; exit 1 }
    New-Item -ItemType Directory -Force -Path $content | Out-Null
    foreach ($s in @($buildDir, $ui)) { Get-ChildItem -LiteralPath $s -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $content -Recurse -Force } }
    # (not -Include: with -LiteralPath, Windows PowerShell ignores it and matches every file)
    Get-ChildItem -LiteralPath $content -Recurse -File | Where-Object { $_.Name -match '\.(pdb|log)$|\.deps\.json$|\.runtimeconfig\.json$' } | Remove-Item -Force
    if (-not (Test-Path -LiteralPath (Join-Path $content 'Placemaker.dll')) -or -not (Test-Path -LiteralPath (Join-Path $content 'Placemaker.mjs'))) {
        Write-Host 'STOP: the content folder has no Placemaker.dll or Placemaker.mjs'; exit 1
    }
    $files = Get-ChildItem -LiteralPath $content -Recurse -File
    $mb = [math]::Round((($files | Measure-Object Length -Sum).Sum) / 1MB, 1)
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $content 'Placemaker.dll')).ProductVersion
    Write-Host "content: $($files.Count) files, $mb MB, Placemaker $version"
    Get-ChildItem -LiteralPath $content -Force | ForEach-Object { Write-Host "  $($_.Name)" }
}
Write-Host "THE PAGE THAT GOES UP (it replaces the whole page on the website):"
Write-Host "  name: $($p.DisplayName.Value), version $($p.ModVersion.Value), game $($p.GameVersion.Value), $access"
Write-Host "  short description: $($p.ShortDescription.Value)"
Write-Host "  description: Properties\ModPage\LongDescription.md, $("$($p.LongDescription)".Trim().Length) chars"
Write-Host "  thumbnail: $($p.Thumbnail.Value)"
$shots | ForEach-Object { Write-Host "  picture: $($_.Value)" }
Write-Host "  forum link: $($p.ForumLink.Value)"
@($p.ExternalLink) | ForEach-Object { Write-Host "  link: $($_.Type) $($_.Url)" }
@($p.Tag) | ForEach-Object { Write-Host "  tag: $($_.Value)" }
Write-Host "  changelog: $("$($p.ChangeLog.Value)$($p.ChangeLog.InnerText)".Trim())"
if ($StageOnly) { Write-Host 'stage only: nothing uploaded'; exit 0 }

# --- the upload ---
# The uploader and the game share one saved Paradox login (.pdxsdk): an upload while a game runs (the user's or the
# game queue's) left the game holding an outdated session, and the login came back logged out (2026-10-17).
if (Get-Process Cities2 -ErrorAction SilentlyContinue) { Write-Host 'STOP: a game is running (yours or the game queue''s); the uploader shares its Paradox login, so close it first'; exit 1 }
$publisher = $env:CSII_MODPUBLISHERPATH
if (-not $publisher) { $publisher = [Environment]::GetEnvironmentVariable('CSII_MODPUBLISHERPATH', 'User') }
if (-not $publisher -or -not (Test-Path -LiteralPath $publisher)) { Write-Host "STOP: ModPublisher not found (CSII_MODPUBLISHERPATH = '$publisher')"; exit 1 }
# The game's uploader sends this config's whole page (above) with the files
$send = $config
Push-Location $root   # the config's picture paths are relative to the project folder
try {
    $out = & $publisher $Command $send -c $content -v 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
} finally { Pop-Location }
$out | ForEach-Object { Write-Host $_ }
$log = Join-Path $root "bin\Publish\publish-$(Get-Date -Format yyyyMMdd-HHmmss).log"
$out | Set-Content -Encoding utf8 -LiteralPath $log
if ($Command -eq 'Publish') {
    $m = $out | Select-String -Pattern 'Mod published with Id=(\d+)' | Select-Object -Last 1
    if ($m) {
        $id = $m.Matches[0].Groups[1].Value
        $text = Get-Content -Raw -Encoding utf8 -LiteralPath $config
        $text = $text -replace '<ModId Value="[^"]*" />', "<ModId Value=`"$id`" />"
        [IO.File]::WriteAllText($config, $text, (New-Object Text.UTF8Encoding $false))
        Write-Host "ModId $id written into Properties\PublishConfiguration.xml: commit it"
    } elseif ($code -eq 0) { Write-Host "WARNING: no mod id in the uploader's output; find it on the mod's page and put it in ModId by hand" }
}
Write-Host "uploader exit code $code; its output is in $log"
exit $code
