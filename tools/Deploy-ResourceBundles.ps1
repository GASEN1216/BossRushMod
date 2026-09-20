param(
    [Parameter(Mandatory=$true)][string]$SourceRoot,
    [Parameter(Mandatory=$true)][string]$TargetRoot,
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
function Get-ResourceHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}
function Assert-TargetBundleInventory([string]$Root, $Allowed, $Policy) {
    if ($Policy.rejectUnlisted -ne $true) { throw 'Release manifest must require rejectUnlisted.' }
    if (-not (Test-Path -LiteralPath $Root)) { return }
    # Explicit traversal rejects junctions instead of following paths outside the target.
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Root)
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Cannot verify reparse point in release target: $($entry.FullName)"
            }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName); continue }
            $relative = $entry.FullName.Substring($Root.Length + 1).Replace('\', '/')
            $stream = [IO.File]::OpenRead($entry.FullName)
            try {
                $header = New-Object byte[] 16
                $length = $stream.Read($header, 0, $header.Length)
                $signature = [Text.Encoding]::ASCII.GetString($header, 0, $length).Split([char]0)[0]
            } finally { $stream.Dispose() }
            $isBundle = $Policy.signatures -contains $signature
            # Damaged/empty extensionless files in Assets are also rejected, not silently ignored.
            if ($relative.StartsWith('Assets/', [StringComparison]::OrdinalIgnoreCase) -and
                -not [IO.Path]::GetExtension($entry.Name)) { $isBundle = $true }
            if ($isBundle -and -not $Allowed.Contains($relative)) {
                throw "Unlisted AssetBundle in release target: $relative SHA256=$(Get-ResourceHash $entry.FullName)"
            }
        }
    }
}
try {
    $sourceBase = [IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
    $targetBase = [IO.Path]::GetFullPath($TargetRoot).TrimEnd('\')
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'resource_release_manifest.json') -Raw | ConvertFrom-Json
    $allowed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $entries = @()
    foreach ($relative in $manifest.bundles) {
        if ($relative -notmatch '^Assets/[A-Za-z0-9_/-]+$' -or $relative.Contains('..')) {
            throw "Invalid resource path: $relative"
        }
        if (-not $allowed.Add($relative)) { throw "Duplicate release path: $relative" }
        $source = [IO.Path]::GetFullPath((Join-Path $sourceBase $relative))
        $target = [IO.Path]::GetFullPath((Join-Path $targetBase $relative))
        if (-not $source.StartsWith($sourceBase + '\', [StringComparison]::OrdinalIgnoreCase) -or
            -not $target.StartsWith($targetBase + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Resource outside release roots: $relative"
        }
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing release bundle: $relative" }
        $entries += [PSCustomObject]@{ relative=$relative; source=$source; target=$target; sha256=(Get-ResourceHash $source) }
    }
    # Fail closed before backing up or copying even one listed bundle.
    Assert-TargetBundleInventory $targetBase $allowed $manifest.targetBundlePolicy
    $backupRoot = Join-Path $sourceBase ('Build/resource-release-backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    $results = @()
    foreach ($entry in $entries) {
        $targetHash = ''
        if (Test-Path -LiteralPath $entry.target -PathType Leaf) { $targetHash = Get-ResourceHash $entry.target }
        if ($targetHash -ne $entry.sha256 -and -not $VerifyOnly) {
            if ($targetHash) {
                $backup = Join-Path $backupRoot $entry.relative
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null
                Copy-Item -LiteralPath $entry.target -Destination $backup
            }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($entry.target)) | Out-Null
            Copy-Item -LiteralPath $entry.source -Destination $entry.target -Force
            $targetHash = Get-ResourceHash $entry.target
        }
        $results += [PSCustomObject]@{path=$entry.relative; sha256=$entry.sha256; targetSha256=$targetHash; match=($targetHash -eq $entry.sha256)}
    }
    $report = Join-Path $sourceBase 'Build/resource-release-verification.json'
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($report)) | Out-Null
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $report -Encoding UTF8
    $failures = @($results | Where-Object { -not $_.match })
    if ($failures.Count) { throw "Resource hash mismatch: $($failures.path -join ', ')" }
    Assert-TargetBundleInventory $targetBase $allowed $manifest.targetBundlePolicy
    Write-Host "Resource release: $($results.Count) bundles verified by SHA-256."
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
