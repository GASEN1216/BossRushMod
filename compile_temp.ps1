$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$GAME_PATH = "D:\sofrware\steam\steamapps\common\Escape from Duckov"
$OUTPUT_DIR = "Build"
$MOD_NAME = "BossRush"

if (-not (Test-Path $OUTPUT_DIR)) {
    New-Item -ItemType Directory -Path $OUTPUT_DIR | Out-Null
}

$sdkPath = (Get-ChildItem "C:\Program Files\dotnet\sdk" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
Write-Host "使用 SDK: $sdkPath"

$sourceFiles = Get-ChildItem -Path . -Recurse -Include "*.cs" | Where-Object { $_.FullName -notmatch "\\Build\\" } | ForEach-Object { $_.FullName }

$refs = @(
    "UnityEngine.dll", "UnityEngine.CoreModule.dll", "UnityEngine.PhysicsModule.dll",
    "UnityEngine.UI.dll", "UnityEngine.JSONSerializeModule.dll", "UnityEngine.AIModule.dll",
    "UnityEngine.AudioModule.dll", "UnityEngine.UnityWebRequestWWWModule.dll",
    "Eflatun.SceneReference.dll", "Assembly-CSharp.dll", "UnityEngine.UIModule.dll",
    "UnityEngine.InputLegacyModule.dll", "UnityEngine.IMGUIModule.dll",
    "UnityEngine.ImageConversionModule.dll", "UnityEngine.TextRenderingModule.dll",
    "UnityEngine.AssetBundleModule.dll", "UnityEngine.AnimationModule.dll",
    "Unity.TextMeshPro.dll", "ItemStatsSystem.dll", "UniTask.dll",
    "Sirenix.OdinInspector.Attributes.dll", "TeamSoda.Duckov.Core.dll",
    "TeamSoda.Duckov.Utilities.dll", "AstarPathfindingProject.dll", "PackageTools.dll",
    "Drawing.dll", "SodaLocalization.dll", "NodeCanvas.dll", "ParadoxNotion.dll",
    "System.Core.dll", "System.dll", "mscorlib.dll", "netstandard.dll"
) | ForEach-Object { "/reference:$_" }

$args = @(
    "/langversion:7.3",
    "/target:library",
    "/out:$OUTPUT_DIR\$MOD_NAME.dll",
    "/lib:$GAME_PATH\Duckov_Data\Managed",
    "/nowarn:CS0436,CS0162,CS0414"
) + $refs + $sourceFiles

& dotnet "$sdkPath\Roslyn\bincore\csc.dll" $args

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n编译成功！输出: $OUTPUT_DIR\$MOD_NAME.dll" -ForegroundColor Green
} else {
    Write-Host "`n编译失败！" -ForegroundColor Red
}
