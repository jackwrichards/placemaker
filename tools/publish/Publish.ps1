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
    -Command NewVersion  a new version of the mod's files; needs ModId, a new ModVersion and a ChangeLog. Sends only those:
                         the page (thumbnail, screenshots, descriptions, links) stays as it is on the website. Not the
                         game's uploader (it writes every page field from this config, and its own placeholder
                         thumbnail when none is named): tools/publish/NewVersion, which starts from the live page and
                         changes only the files, version and changelog, then reads the page back.
    -Command Update      the page only (text, pictures, links, AccessLevel); the files stay as they are. REFUSED unless
                         -OverwritePage: the page is the user's, kept up on the website (forum link, pictures, text);
                         this config's copy of it is old and would replace all of it.
    -StageOnly           build and stage, print what would go up, upload nothing (NewVersion: also reads the live page
                         and prints what would be sent back).
#>
param(
    [ValidateSet('Publish', 'NewVersion', 'Update')][string]$Command = 'Publish',
    [switch]$StageOnly,
    [switch]$OverwritePage
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
# The mod page belongs to the user (they edit it on the website): only an explicit -OverwritePage replaces it.
if ($Command -eq 'Update' -and -not $OverwritePage) { $problems += 'Update replaces the whole mod page (pictures, text, forum link) with this config''s old copy; the user keeps the page on the website. Change it there, or pass -OverwritePage if the user asked for exactly that' }
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
Write-Host "page: $($p.DisplayName.Value) $($p.ModVersion.Value), $access, $($pictures.Count) pictures, command $Command"
if ($StageOnly -and $Command -ne 'NewVersion') { Write-Host 'stage only: nothing uploaded'; exit 0 }

# --- the upload ---
# The uploader and the game share one saved Paradox login (.pdxsdk): an upload while a game runs (the user's or the
# game queue's) left the game holding an outdated session, and the login came back logged out (2026-10-17).
if (Get-Process Cities2 -ErrorAction SilentlyContinue) { Write-Host 'STOP: a game is running (yours or the game queue''s); the uploader shares its Paradox login, so close it first'; exit 1 }
$publisher = $env:CSII_MODPUBLISHERPATH
if (-not $publisher) { $publisher = [Environment]::GetEnvironmentVariable('CSII_MODPUBLISHERPATH', 'User') }
if (-not $publisher -or -not (Test-Path -LiteralPath $publisher)) { Write-Host "STOP: ModPublisher not found (CSII_MODPUBLISHERPATH = '$publisher')"; exit 1 }
# NewVersion: the files, version and changelog only, the page left as the user made it on the website. The game's
# uploader cannot do that (it writes every page field from this config; the 0.3.0 upload, 26 Oct, wiped the user's
# thumbnail, pictures and forum link): tools/publish/NewVersion starts from the live page instead.
if ($Command -eq 'NewVersion') {
    $tool = Join-Path $root 'bin\Publish\newversion-tool'
    $env:CSII_MODPUBLISHERPATH = $publisher
    & dotnet build (Join-Path $root 'tools\publish\NewVersion\NewVersion.csproj') -c Release -o $tool -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { Write-Host 'STOP: the NewVersion tool did not build'; exit 1 }
    $changes = Join-Path $root 'bin\Publish\changelog.txt'
    [IO.File]::WriteAllText($changes, "$($p.ChangeLog.Value)$($p.ChangeLog.InnerText)".Trim(), (New-Object Text.UTF8Encoding $false))
    $toolArgs = @($modId, "$($p.ModVersion.Value)", $changes, $content)
    if ($StageOnly) { $toolArgs += '--dry-run' }
    $out = & dotnet (Join-Path $tool 'NewVersion.dll') @toolArgs 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $out | ForEach-Object { Write-Host $_ }
    $log = Join-Path $root "bin\Publish\publish-$(Get-Date -Format yyyyMMdd-HHmmss).log"
    $out | Set-Content -Encoding utf8 -LiteralPath $log
    Write-Host "NewVersion exit code $code; its output is in $log"
    exit $code
}
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
