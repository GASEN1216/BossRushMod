---
kind: build_system
name: BossRushMod 构建与发布体系（脚本驱动 C# 编译 + GitHub Pages Wiki）
category: build_system
scope:
    - '**'
source_files:
    - compile_dev.bat
    - test_logic_official.bat
    - test_bossrush_smoke_manual.bat
    - test_zombiemode_goal_windows.bat
    - .github/workflows/deploy.yml
    - wiki-site/package.json
    - wiki-site/scripts/sync-content.mjs
    - tests/README.md
    - AGENTS.md
    - CODE_REVIEW.md
    - README.md
    - README_EN.md
---

## 1. 构建系统总览

BossRushMod 是一个 Unity 游戏模组，其构建方式并非标准 `.csproj`/`.sln` 工程，而是**由 Windows 批处理脚本直接调用 Roslyn `csc.dll` 进行源码编译**。核心约定是：新增任意 `.cs` 文件后必须手动加入根目录的 `compile_official.bat` 中的显式源文件列表，否则该文件不会参与编译、也不会报错。这一约束被多处文档和守护脚本反复强调。

- 编译产物：`Build/BossRush.dll`（Unity 模组 DLL），部署到游戏安装目录 `Duckov_Data\Mods\BossRush\BossRush.dll`。
- 目标语言版本：C# 7.3（由 `compile_official.bat` 通过已安装 .NET SDK 的 Roslyn 编译器决定）。
- 平台限制：仅 Windows 可编译；WSL/Linux 无编译器，无法执行 `compile_official.bat`。
- 开发模式开关：设置环境变量 `BOSSRUSH_DEV_BUILD=1` 再运行 `compile_dev.bat`，后者会调用 `compile_official.bat` 并传入开发标志。

## 2. 关键构建与测试脚本

| 文件 | 作用 |
| --- | --- |
| `compile_official.bat` | 官方编译入口，列出全部 `.cs` 源文件并调用 Roslyn 生成 `Build/BossRush.dll`（仓库中未包含此文件，但被大量脚本和文档引用） |
| `compile_dev.bat` | 开发构建包装器，设置 `BOSSRUSH_DEV_BUILD=1` 后调用 `compile_official.bat` |
| `test_logic_official.bat` | 逻辑测试入口：清理 `tests\bin`/`obj`，依次用 `dotnet run --project tests\*.csproj -c Release` 运行 8 个独立单元测试项目 |
| `test_bossrush_smoke_manual.bat` | 手工冒烟测试辅助：自动探测游戏路径、打印检查清单、启动 Steam 游戏，并在完成后提示运行 `tests/SmokeLogScan.py` 扫描日志 |
| `test_zombiemode_goal_windows.bat` | 丧尸模式 Windows 验证入口，依赖 `compile_official.bat` 先完成编译 |
| `validate_refactor_step.bat` | 重构步骤验证脚本（具体行为由脚本自身实现） |
| `tools/export_spawn_points.py` | 工具脚本，导出关卡刷点数据 |

## 3. 测试与守护体系

测试分为三层，构成“静态守护 → 单元测试 → 运行时冒烟”的流水线：

### 3.1 静态守护（Python grep 风格）
`tests/*.py` 是**静态文本守护**，通过正则扫描源码防止特定 invariant 回归，不是功能测试。例如：
- `OfficialCompileListFileExistenceGuard.py`：校验 `compile_official.bat` 列出的所有 `.cs` 源文件确实存在。
- `ZombieModeCompileListGuard.py`：强制 `compile_official.bat` 必须列出全部 `ZombieMode/*.cs`。
- `EmptyCatchGuard.py`、`LargeFileBudgetGuard.py`、`GitIgnoreGuard.py` 等覆盖代码规范与资源预算。

运行方式：`for %f in (tests\*.py) do python %f`，退出码 0 = 通过，非 0 = 失败。

### 3.2 单元测试（.NET 控制台项目）
`tests/` 下包含多个独立的 `.csproj` 单元测试项目（如 `LegacyBossLootProbabilityTests.csproj`、`PhantomWitchPerformancePolicyTests.csproj`、`SimpleJsonHelperTests.csproj`、`AffinityJsonSerializerTests.csproj`、`F3DebugCheatMathTests.csproj`、`F3DebugCheatLifecycleTests.csproj`、`VictoryRewardShadowMathTests.csproj`、`AwenLootSweepMathTests.csproj`），由 `test_logic_official.bat` 以 `Release` 配置顺序执行。

### 3.3 运行时冒烟
`test_bossrush_smoke_manual.bat` 负责在真实游戏中执行检查清单，并通过 `tests/SmokeLogScan.py` 扫描游戏日志中的错误块，形成人工记录的冒烟报告。

## 4. CI / 发布

仓库中唯一的 GitHub Actions 工作流位于 `.github/workflows/deploy.yml`，职责单一：**将 wiki-site 站点构建并发布到 GitHub Pages**。

- 触发条件：推送到 `main` 且变更路径包含 `wiki-site/**` 或 `WikiContent/**`，或手动触发 `workflow_dispatch`。
- 环境：Ubuntu，Node.js 20，使用 `npm ci` 缓存 `wiki-site/package-lock.json`。
- 构建：`cd wiki-site && npm run build`，产物为 VitePress 输出目录 `wiki-site/docs/.vitepress/dist`。
- 发布：通过 `actions/upload-pages-artifact@v3` 上传 artifact，再由 `actions/deploy-pages@v4` 部署到 GitHub Pages。

该 CI **不参与 C# 模组本身的编译与打包**，只负责 Wiki 站点的发布。

## 5. Wiki 站点构建

`wiki-site/` 是基于 VitePress 的文档站点：
- 依赖管理：`package.json` + `package-lock.json`，CI 中使用 `npm ci` 安装。
- 内容来源：`docs/` 下的 Markdown 文档，以及通过 `scripts/sync-content.mjs` 从外部 `WikiContent/` 同步生成的多语言百科内容。
- 版本记录：`docs/changelog/` 下按版本号维护更新日志（v1.6.x ~ v2.2.x）。

## 6. 约定与约束

| 约束 | 说明 |
| --- | --- |
| 新增 `.cs` 必须加入 `compile_official.bat` | 仓库文档与多处 guard 脚本强制要求；否则源码存在但不参与编译，且不会报错 |
| 编译仅在 Windows 可用 | 依赖本地安装的 .NET SDK 提供的 Roslyn `csc.dll`，WSL/Linux 不可用 |
| 产物固定路径 | 编译输出 `Build/BossRush.dll`，部署到游戏 `Duckov_Data\Mods\BossRush\BossRush.dll` |
| 开发模式通过环境变量控制 | `BOSSRUSH_DEV_BUILD=1` 配合 `compile_dev.bat` 启用 |
| 测试分层执行 | Python 静态守护 → dotnet 单元测试 → 手工游戏冒烟，三者缺一不可 |
| CI 仅发布 Wiki | GitHub Actions 不编译 C# 代码，只构建并部署 `wiki-site` 到 GitHub Pages |
| 版本管理 | 通过 `wiki-site/docs/changelog/vX.Y.Z.md` 记录版本变更，无集中式版本号声明文件 |

## 7. 关键文件

- `compile_dev.bat` — 开发构建入口
- `test_logic_official.bat` — 单元测试编排
- `test_bossrush_smoke_manual.bat` — 手工冒烟测试引导
- `test_zombiemode_goal_windows.bat` — 丧尸模式 Windows 验证
- `.github/workflows/deploy.yml` — Wiki 站点 CI/CD
- `wiki-site/package.json` — VitePress 站点依赖
- `wiki-site/scripts/sync-content.mjs` — Wiki 内容同步脚本
- `tests/README.md` — 守护脚本使用说明
- `AGENTS.md`、`CODE_REVIEW.md`、`README.md`、`README_EN.md` — 构建相关约定与约束的权威文档
- `Build/` — 编译产物目录（DLL 输出位置）