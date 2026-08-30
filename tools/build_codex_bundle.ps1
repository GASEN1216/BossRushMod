# 鸭皇图鉴立绘 AssetBundle 一键构建。
#
# 做三件事：把 Mod 侧生成的立绘拷进兄弟 Unity 工程 -> 调 Unity 跑构建器 -> 把产物拷回 Mod 的 Assets/ui。
#
# 注意（都踩过）：
#   - 免费证书**不能加** -batchmode / -nographics，会直接拒绝激活；用普通 Editor + -executeMethod。
#   - Unity 会独占工程锁，跑之前必须先关掉已打开的同一工程。
#   - 构建器本身会回读校验 asset 命名与数量，命名不符会以非零码退出，别只看有没有生成文件。

$ErrorActionPreference = 'Stop'

$ModRoot     = Split-Path -Parent $PSScriptRoot
$UnityProj   = 'D:\code\ykf\duckov_modding-main\UnityFiles\BossRush'
$UnityExe    = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe'
$SrcDir      = Join-Path $ModRoot 'Assets\ui\Codex'
$DstDir      = Join-Path $UnityProj 'Assets\UI\Codex'
$ExportPath  = Join-Path $UnityProj 'CodexExport\codex_portraits'
$FinalPath   = Join-Path $ModRoot 'Assets\ui\codex_portraits'
$LogPath     = Join-Path $ModRoot 'output\unity_codex_build.log'

if (-not (Test-Path $UnityExe)) { throw "找不到 Unity: $UnityExe" }
if (-not (Test-Path $SrcDir))   { throw "找不到立绘源目录: $SrcDir（先跑 tools/gen_codex_art.py）" }

$pngs = Get-ChildItem -Path $SrcDir -Filter 'codex_portrait_*.png' -File
if ($pngs.Count -eq 0) { throw "源目录里没有 codex_portrait_*.png" }
Write-Host "[1/4] 源立绘 $($pngs.Count) 张"

New-Item -ItemType Directory -Force -Path $DstDir | Out-Null
Copy-Item -Path (Join-Path $SrcDir 'codex_portrait_*.png') -Destination $DstDir -Force
Write-Host "[2/4] 已拷入 Unity 工程: $DstDir"

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogPath) | Out-Null
Write-Host "[3/4] 调 Unity 构建（首次导入 36 张贴图可能要几分钟）..."
# 必须用 Start-Process -Wait：Unity.exe 是 GUI 程序，直接用 & 调不会等它结束，
# $LASTEXITCODE 会是空的，而且外层任务一结束还会把正在启动的 Unity 子进程带走
# （表现为日志停在 "Begin MonoManager ReloadAssembly"、没有任何产物）。
$proc = Start-Process -FilePath $UnityExe -Wait -PassThru -ArgumentList @(
    '-projectPath', $UnityProj,
    '-executeMethod', 'CodexPortraitBundleBuilder.BuildOnlyAndExit',
    '-logFile', $LogPath
)
$code = $proc.ExitCode
if ($code -ne 0) {
    Write-Host "Unity 退出码 $code，日志尾部："
    if (Test-Path $LogPath) { Get-Content $LogPath -Tail 40 }
    throw "AssetBundle 构建失败"
}

if (-not (Test-Path $ExportPath)) { throw "构建器没有产出 bundle: $ExportPath" }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $FinalPath) | Out-Null
Copy-Item -Path $ExportPath -Destination $FinalPath -Force
$size = (Get-Item $FinalPath).Length
Write-Host "[4/4] 已落位: $FinalPath（$size bytes）"
Write-Host "接下来跑 compile_official.bat，部署步骤会把它复制进游戏目录。"
