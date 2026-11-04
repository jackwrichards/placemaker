<#
  Neighbourhood polish, offline: replays YardStitch.Plan on the houses a game run dumped (lab op "polishDump") and draws
  each plan as a picture (plots' lawns green, driveways grey, houses brown, hedges dark green, fences brown, walls grey,
  hidden lines red dots, a shared green dark). Seconds per try instead of a game launch; check the result in the game after.

    powershell -File tools/hot/Replay-Polish.ps1 -Run <run folder or its scenarios folder> [-Knobs "maxDeepen=12;fadeWidth=6"] [-Out <folder>]

  Pictures: <Out>\polish-<scenario>.png (default Out: <run>\replay), with the plan's notes in polish-<scenario>.txt.
#>
param(
    [Parameter(Mandatory = $true)][string]$Run,
    [string]$Knobs = "",
    [string]$Out = ""
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$scen = if (Test-Path (Join-Path $Run 'scenarios')) { Join-Path $Run 'scenarios' } else { $Run }
if (-not $Out) { $Out = Join-Path (Split-Path $scen -Parent) 'replay' }
New-Item -ItemType Directory -Force $Out | Out-Null
$env:PM_POLISH_REPLAY = $scen
$env:PM_POLISH_OUT = $Out
$env:PM_POLISH_KNOBS = $Knobs
$result = & dotnet test (Join-Path $repo 'Tests\Placemaker.Tests') --filter "FullyQualifiedName~NeighbourReplay" 2>&1
$summary = $result | Select-String 'Passed!|Failed!|error CS' | Select-Object -First 5
$summary | ForEach-Object { Write-Host $_.Line }
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
foreach ($svg in Get-ChildItem $Out -Filter 'polish-*.svg') {
    $png = [IO.Path]::ChangeExtension($svg.FullName, '.png')
    $h = 1000
    $m = [regex]::Match((Get-Content $svg.FullName -TotalCount 1), 'height="([\d.]+)"')
    if ($m.Success) { $h = [int][math]::Ceiling([double]::Parse($m.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)) }
    if ($edge) {
        $p = Start-Process -FilePath $edge -ArgumentList @('--headless=new', '--disable-gpu', '--hide-scrollbars', "--screenshot=$png", "--window-size=1000,$h", ('file:///' + ($svg.FullName -replace '\\', '/'))) -PassThru -WindowStyle Hidden
        $p.WaitForExit(20000) | Out-Null
    }
    $notes = [IO.Path]::ChangeExtension($svg.FullName, '.txt')
    Write-Host ("{0}: {1}" -f $png, ((Get-Content $notes -ErrorAction SilentlyContinue | Select-Object -Last 1)))
}
