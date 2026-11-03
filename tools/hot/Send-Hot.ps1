<#
.SYNOPSIS
  Fast loop for the neighbourhood-polish planner: builds Core/Neighbours/YardStitch.cs as a hot planner, writes it into
  the scenario file (every "{hot}" becomes its path) and sends the scenario WITHOUT rebuilding the mod, so the queue
  reuses the game already open with this worktree's bin\Release build (no relaunch). The scenario loads it with
  {"do":"lab","op":"hotload","path":"{hot}"}.

  powershell -File tools/hot/Send-Hot.ps1 -File suite/scenarios/polish-hot.json
  (the first time after a real mod change: send once with tools/suite/Send-Suite.ps1 -Build, so the game has the loader)
#>
param(
    [Parameter(Mandatory = $true)][string]$File,
    [string]$Who = 'polish-hot',
    [switch]$Build
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$dll = (& powershell -NoProfile -File (Join-Path $PSScriptRoot 'Build-Hot.ps1') | Select-Object -Last 1).Trim()
if (-not (Test-Path $dll)) { throw "hot build failed: $dll" }
Write-Output "hot planner: $dll"
$text = [IO.File]::ReadAllText((Resolve-Path (Join-Path $repo $File)))
$text = $text.Replace('{hot}', $dll.Replace('\', '\\'))
$tmp = Join-Path $env:TEMP ("placemaker-hot-" + [IO.Path]::GetFileNameWithoutExtension($dll) + ".json")
[IO.File]::WriteAllText($tmp, $text)
$extra = @(); if ($Build) { $extra += '-Build' }
& powershell -NoProfile -File (Join-Path $repo 'tools\suite\Send-Suite.ps1') -File $tmp -Who $Who @extra
exit $LASTEXITCODE
