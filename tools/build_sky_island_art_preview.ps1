param(
    [string]$ProjectPath = 'D:/code/ykf/duckov_modding-main/UnityFiles/BossRush',
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [switch]$Physics
)

$ErrorActionPreference = 'Stop'
$skyRepo = Split-Path -Parent $PSScriptRoot
$previewProject = Join-Path $skyRepo 'Build/sky-island-art-preview'
$sourceAssets = Join-Path $ProjectPath 'Assets/SkyIsland'
if (!(Test-Path -LiteralPath (Join-Path $sourceAssets 'SkyIslandWorld.prefab'))) {
    throw 'Build the world prefab before preparing the art preview project.'
}

# Keep game DLLs and their custom render-pipeline types outside the preview host.
# Only copies of authored art, metadata and the resource-only builder are imported.
foreach ($folder in @('Assets/Editor','ArtSource/SkyIsland','ProjectSettings','Packages','SkyIslandExport')) {
    New-Item -ItemType Directory -Path (Join-Path $previewProject $folder) -Force | Out-Null
}
# Copy-Item -Recurse -Force 只会补新文件，不会覆盖已存在的旧文件：预览宿主会一直沿用
# 第一次跑时的 SkyIslandWorld.prefab，于是新加的模型渲不出来而贴图却更新了，极难察觉。
# 先删干净再整份复制。
$staleAssets = Join-Path $previewProject 'Assets/SkyIsland'
if (Test-Path -LiteralPath $staleAssets) { Remove-Item -LiteralPath $staleAssets -Recurse -Force }
Copy-Item -LiteralPath $sourceAssets -Destination (Join-Path $previewProject 'Assets') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $ProjectPath 'Assets/Editor/SkyIslandBundleBuilder.cs') -Destination (Join-Path $previewProject 'Assets/Editor/SkyIslandBundleBuilder.cs') -Force
Copy-Item -LiteralPath (Join-Path $ProjectPath 'ArtSource/SkyIsland/sky_island_geometry.json') -Destination (Join-Path $previewProject 'ArtSource/SkyIsland/sky_island_geometry.json') -Force
Copy-Item -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $previewProject 'ProjectSettings/ProjectVersion.txt') -Force
Copy-Item -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectSettings.asset') -Destination (Join-Path $previewProject 'ProjectSettings/ProjectSettings.asset') -Force
$manifest = @{ dependencies = @{
    'com.unity.render-pipelines.universal' = '14.0.12'
    'com.unity.nuget.newtonsoft-json' = '3.2.1'
    'com.unity.modules.assetbundle' = '1.0.0'
    'com.unity.modules.physics' = '1.0.0'
    'com.unity.modules.imageconversion' = '1.0.0'
    'com.unity.modules.jsonserialize' = '1.0.0'
    'com.unity.modules.imgui' = '1.0.0'
    'com.unity.modules.ui' = '1.0.0'
} }
[IO.File]::WriteAllText((Join-Path $previewProject 'Packages/manifest.json'), ($manifest | ConvertTo-Json -Depth 3))
$method = 'BossRush.SkyIslandBundleBuilder.RenderPreviewsAndExit'
$logName = 'sky_island_life_preview.log'
if ($Physics) {
    foreach ($name in @('sky_island_world','sky_island_bundle_validation.json')) {
        Copy-Item -LiteralPath (Join-Path $ProjectPath ('SkyIslandExport/'+$name)) -Destination (Join-Path $previewProject ('SkyIslandExport/'+$name)) -Force
    }
    $method = 'BossRush.SkyIslandBundleBuilder.ValidatePhysicsAndExit'
    $logName = 'sky_island_life_physics.log'
}
& $UnityPath -batchmode -projectPath $previewProject -executeMethod $method -logFile (Join-Path $skyRepo ('Build/'+$logName)) | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Art preview failed; see Build/$logName"
}
Write-Output "SKY_ISLAND_ART_HOST_PASS $previewProject/SkyIslandExport"
