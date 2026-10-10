# Build only the metronome's four assets; no global asset rebuild or project upgrade.
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$locations = & python -c "import sys,json;sys.path.insert(0,sys.argv[1]);from unity_project_path import find_unity_project,find_unity_editor;print(json.dumps(dict(project=find_unity_project(),editor=find_unity_editor())))" $PSScriptRoot | ConvertFrom-Json
if (-not $locations.project -or -not $locations.editor) { throw 'Unity project/editor unavailable' }
if (Get-Process Unity -ErrorAction SilentlyContinue) { throw 'Unity is already running; do not interrupt its project session' }
$source = Join-Path $taskRepo 'ArtSource/ReloadMetronome/ReloadMetronomeBundleBuilder.cs.txt'
$destination = Join-Path $locations.project 'Assets/Editor/ReloadMetronomeBundleBuilder.cs'
Copy-Item -LiteralPath $source -Destination $destination -Force
$previousRepo = $env:BOSSRUSH_RESOURCE_REPO
$env:BOSSRUSH_RESOURCE_REPO = $taskRepo
$log = Join-Path $taskRepo 'Build/reload-metronome-unity.log'
try {
    $process = Start-Process -FilePath $locations.editor -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        '-projectPath', ('"' + $locations.project + '"'),
        '-executeMethod', 'ReloadMetronomeBundleBuilder.BuildAndExit',
        '-logFile', ('"' + $log + '"')
    )
    if ($process.ExitCode -ne 0) { throw "Unity build failed ($($process.ExitCode)); inspect $log" }
    $built = Join-Path $taskRepo 'Build/reload-metronome-art/reload_metronome'
    $target = Join-Path $taskRepo 'Assets/ui/reload_metronome'
    if (-not (Test-Path -LiteralPath $built)) { throw 'Unity did not produce the metronome bundle' }
    Copy-Item -LiteralPath $built -Destination $target -Force
    if ((Get-FileHash -LiteralPath $built).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw 'Asset copy hash mismatch' }
    Get-Content (Join-Path $taskRepo 'Build/reload-metronome-art/verification.txt')
} finally {
    if ($null -eq $previousRepo) { Remove-Item Env:BOSSRUSH_RESOURCE_REPO -ErrorAction SilentlyContinue }
    else { $env:BOSSRUSH_RESOURCE_REPO = $previousRepo }
}
