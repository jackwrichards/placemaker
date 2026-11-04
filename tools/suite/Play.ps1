# Playground for the user: launches CS2 with only Placemaker, in a window, builds the flat test grid,
# points the camera at it at 45 degrees and hands over the controls. Nothing runs, nothing quits.
# The user's own Placemaker options are kept; their game settings are put back when they close the game.
# Through the game queue it goes next after the request in the game now; the queue then waits until the game is closed.
#   powershell -File tools/suite/Play.ps1                 # default grid
#   powershell -File tools/suite/Play.ps1 -Grid 12x8      # a bigger grid
#   powershell -File tools/suite/Play.ps1 -AllMods        # with the whole playset (slow boot)
#   powershell -File tools/suite/Play.ps1 -Direct         # now, outside the queue (the game must be closed)
#   powershell -File tools/suite/Play.ps1 -Direct -Mods PreservePhotoMode,SunGlasses
#       # also these Paradox mods (by DLL name, from the subscribed-mods cache), loaded as local mods for this session
#       # only: copied into Mods\ before the launch and removed again when the game closes. Needs -Direct.
#   powershell -File tools/suite/Play.ps1 -Direct -Menu   # the same launch, but it stops at the main menu (no test grid)
param([string]$Grid = '', [switch]$AllMods, [string]$Window = '1600x900', [ValidateSet('suite', 'game')][string]$Look = 'suite', [switch]$NoBuild, [switch]$Direct, [string]$Who = '', [switch]$DevMode, [string[]]$Mods = @(), [switch]$Menu)
$runId = 'play-' + (Get-Date -Format 'MMdd-HHmmss')
$args2 = @('-File', (Join-Path $PSScriptRoot 'Run-Suite.ps1'), '-Playground', '-RunId', $runId, '-Window', $Window, '-Look', $Look)
if ($Grid) { $args2 += @('-Grid', $Grid) }
if ($AllMods) { $args2 += '-AllMods' }
if ($DevMode) { $args2 += '-DevMode' }
if ($Menu) { $args2 += '-Menu' }
if ($NoBuild) { $args2 += '-NoBuild' }
if ($Direct) { $args2 += '-Direct' }
if ($Who) { $args2 += @('-Who', $Who) }

$ud = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$added = @()
$Mods = @($Mods | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Mods.Count) {
    if (-not $Direct) { Write-Host 'STOP: -Mods needs -Direct (the queue launches only Placemaker)'; exit 2 }
    $cache = Join-Path $ud '.cache\Mods\pdx_mods'
    foreach ($m in $Mods) {
        $src = Get-ChildItem -LiteralPath $cache -Directory -ErrorAction SilentlyContinue |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "$m.dll") } |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $src) { Write-Host "STOP: no subscribed mod with $m.dll in $cache"; exit 2 }
        $dst = Join-Path $ud "Mods\$m"
        if (Test-Path -LiteralPath $dst) {
            if (-not (Test-Path -LiteralPath (Join-Path $dst '.playground-copy'))) { Write-Host "STOP: Mods\$m exists and is not a playground copy; leaving it alone"; exit 2 }
            Remove-Item -LiteralPath $dst -Recurse -Force
        }
        Copy-Item -LiteralPath $src.FullName -Destination $dst -Recurse
        Set-Content -LiteralPath (Join-Path $dst '.playground-copy') -Value "copied from $($src.FullName) for run $runId" -Encoding ASCII
        $added += $dst
        Write-Host "playground: loading $m from $($src.Name) as a local mod for this session"
    }
}
$code = 1
try {
    & powershell -NoProfile -ExecutionPolicy Bypass @args2
    $code = $LASTEXITCODE
}
finally {
    foreach ($d in $added) {
        if (Test-Path -LiteralPath (Join-Path $d '.playground-copy')) { Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue; Write-Host "playground: removed the session copy $d" }
    }
}
exit $code
