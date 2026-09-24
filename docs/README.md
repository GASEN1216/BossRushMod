# docs 目录说明

`docs/` 放架构说明、教程、参考表、设计稿、报告与契约。默认 local-only：被守卫读取的少数文件在 `.gitignore` 里逐个放行，其余只在本机。**协作规则不在这里**：仓库级规则是根目录 `AGENTS.md`，子系统规则是各目录的 `AGENTS.md`，本目录的写作与纳管规则见 [AGENTS.md](AGENTS.md)。

事实优先级：当前代码、构建脚本与守卫 > `AGENTS.md` > 本目录。文档与代码冲突时以代码为准，并顺手改正文档。按模块查实现细节从 [仓库知识库](../.qoder/repowiki/README.md) 进入。

2026-09-24 整理：按主流文档结构（Diátaxis：explanation / how-to / reference，加上 design 与 reports）重排为英文目录，删掉过程件与已被取代的文档，留下的逐篇对照代码核对过。整理前的全部原文备份在仓库外的 `BossRushMod_docs_backup_2026-09-24.zip`。

## 目录结构

| 目录 | 放什么 | 读者什么时候来 |
| --- | --- | --- |
| [architecture/](architecture) | 长期有效的架构约定与系统设计说明 | 动一个子系统之前 |
| [guides/](guides) | 制作教程、操作手册、接线清单；天空岛专项在 [guides/sky-island/](guides/sky-island) | 要做一件具体的事 |
| [reference/](reference) | 物品 ID 表、官方本地化表、官方机制笔记、参考图 | 查表 |
| [contracts.md](contracts.md) | 外部契约与 breaking 边界、TypeID 台账（§1，被守卫解析）、官方 API 静默失败类陷阱（§7.1） | 改存档 / 配置 / TypeID / Harmony 之前 |
| [design/](design) | 设计稿与提案，文首有「已实现 / 部分实现 / 未实现」状态行；[zombie-mode/](design/zombie-mode)、[roadmap/](design/roadmap)（未实现的拓展） | 想知道为什么这样设计，或要开新系统 |
| [reports/](reports) | 审查、测试、实机验收、交付评估的结论报告（历史快照） | 追溯某次改动的来龙去脉 |
| [changelog/](changelog) | 版本与玩法变化总览 | 写更新说明、对外介绍 |
| [prompts/](prompts) | 可复用的长任务提示词 | 起一轮无人值守任务 |
| [ai-docs-migration.md](ai-docs-migration.md) | AI 协作文档的变更与冲突记录 | 改 AGENTS / 本目录规则时 |

`飞书应用密钥.md`、`AI生图API和密钥.md` 是本地配置，**含密钥**：不提交，不复制进回答、提交、PR 或其他文档。

## architecture/ — 架构约定

- [UI 制作共识](architecture/UI制作共识.md)：**做新界面先读**。页型、按钮位置与层级、信息层级、「干净」的做法、交付前自检（进 git，根 `AGENTS.md` §4.14 指向它）
- [游戏模式状态机设计](architecture/游戏模式状态机设计.md)：标准 BossRush / D / E / F / 丧尸的激活标志、转移与清理契约
- [刷怪与恢复系统设计](architecture/刷怪与恢复系统设计.md)：刷怪核心、落点校正、卡住恢复
- [Harmony 补丁契约稳定性](architecture/Harmony补丁契约稳定性.md)：动态绑定为什么会静默失效，官方更新后的自检流程
- [自研着色器与官方渲染管线约定](architecture/自研着色器与官方渲染管线约定.md)：URP Deferred 下自研着色器必须带 `UniversalGBuffer`
- [事件订阅生命周期约定](architecture/事件订阅生命周期约定.md) · [Hooks 分层约定](architecture/Hooks分层约定.md) · [Config 归位约定](architecture/Config归位约定.md) · [Utilities 去重约定](architecture/Utilities去重约定.md)
- [BOSS 模板约定](architecture/BOSS模板约定.md) · [NPC 共性对比记录](architecture/NPC共性对比记录.md) · [长期架构目标](architecture/长期架构目标.md)（non-goal 登记）

## guides/ — 教程与操作手册

内容与装备：

- [新增物品代码接线清单](guides/新增物品代码接线清单.md)：TypeID、配置器、本地化、掉落、获取途径、部署一条龙
- [头盔佩戴与装备尺寸校准](guides/头盔佩戴与装备尺寸校准.md) · [手把手在 Unity 里制作头盔护甲](guides/手把手教你如何在unity里制作头盔护甲.md)（附录含官方装备挂载机制）
- [自定义近战武器 Unity 资源制作指南](guides/焚皇断界戟_Unity资源制作指南.md)（焚皇断界戟 / 霜之哀伤）
- [自定义交互物模型制作教程](guides/自定义交互物模型制作教程.md) · [便携安全区装置 Unity 资源制作约定](guides/便携安全区装置_Unity资源制作约定.md)
- [AI 图片生成与 Unity 自动打包流程](guides/AI图片生成与Unity自动打包流程.md) · [BossRushUI 图集规格](guides/BossRushUI_图集规格.md) · [Wiki 书 UI 资源契约](guides/WikiBookUI_Guide.md)

NPC：

- [捏脸 NPC 使用手册](guides/捏脸NPC使用手册.md) / [捏脸 NPC 工具](guides/捏脸NPC工具.md)：鸭形 NPC 的首选路线
- [异形 NPC（AssetBundle 路线）制作流程](guides/哥布林NPC制作流程.md) · [自定义 NPC 移动系统教程](guides/自定义NPC移动系统教程.md)
- [鸭科夫官方任务系统接入教程](guides/官方任务系统接入教程.md)

场景与地图：

- [从零搭建自定义场景：Blender → Unity → Mod](guides/从零搭建自定义场景_Blender到Unity到Mod完整教程.md)：首次做场景从这里开始
- [自建场景原型试用：石砌遗迹与石堡前哨](guides/石堡前哨独立场景试用.md)

天空岛（专项规则在 `DebugAndTools/SkyIsland/AGENTS.md`，资源侧数据在 [ArtSource/SkyIsland/](../ArtSource/SkyIsland/README.md)）：

- [天空岛：实际交付与验收](guides/sky-island/天空岛_实际交付与验收.md)：当前实现总览
- [天空岛待人工验证清单](guides/sky-island/天空岛_待人工验证清单.md)：**实机验收从这里开始**
- [Tripo3D 建模接入与画风对齐教程](guides/sky-island/天空岛_Tripo3D建模接入与画风对齐教程.md) · [自定义地图接入官方 M 键地图](guides/sky-island/自定义地图接入官方M键地图.md)
- 场景设计目标见 [design/天空岛大地图](design/天空岛大地图_场景设计与制作教程.md)

验收与发布：

- [游戏内验收覆盖维护](guides/游戏内验收覆盖维护.md)：F3 验收用例与覆盖表怎么维护
- [星愿许愿台本地发布与混淆流程](guides/星愿许愿台本地发布与混淆流程.md)

## reference/ — 查表

- [物品 ID 表](reference/Bossrush使用物品ID表.md)：全部自定义 TypeID 与建筑 ID（与 `contracts.md` §1、根 `AGENTS.md` §4.3 三处一致）
- [官方本地化表快照](reference/official-localization/README.md)：查官方文本键（守卫与 Wiki 生图工具读取）
- [官方战利品掉落机制](reference/战利品掉落机制.md) · [官方 V1.3.10 Mod 相关更新笔记](reference/V1.3.10.md) · [BossRush 学习笔记](reference/BossRush_学习笔记.md)
- [ModBehaviour.Instance 分类表](reference/2026-05-14-modbehaviour-instance-classification.md)（进 git，被守卫读取）
- `reference/images/日报UI参考图.png`：日报 UI 取色参考，`tools/gen_daily_report_ui.py` 读取

## design/ — 设计稿与提案

各模式与系统：[Mode D](design/bossrush模式D设计文档.md) · [Mode F 血猎追击](design/ModeF-血猎追击设计文档.md) · [Mode G 宿命回响](design/2026-08-10_ModeG宿命回响完整新模式方案.md) · [Mode H 百战留痕（斗蛐蛐脑暴）](design/2026-08-17_斗蛐蛐新模式创意脑暴.md) · [鸭王杯押钱 / 押物品](design/鸭王杯押钱_2026-09-24.md) · [遗种巢（养崽脑暴）](design/2026-08-28_养崽系统创意脑暴.md) · [遗种巢 UI 交互重排](design/遗种巢UI交互重排_2026-09-24.md) · [鸭科夫日报](design/P2-日报系统.md) · [变异词条](design/变异词条系统扩充方案_2026-07.md) · [成就](design/Achievements.md) · [星愿许愿台](design/星愿许愿台设计文档.md) · [天空岛大地图](design/天空岛大地图_场景设计与制作教程.md) · [玩家反馈保守优化复审](design/2026-08-03_玩家反馈保守优化方案复审.md)

Boss、武器与 NPC：[焚天龙皇](design/龙王boss设计文档.md) · [焚天龙铳](design/焚天龙铳_现状与完善方案_2026-07-05.md) · [焚皇断界戟](design/龙皇专属近战武器从0到1制作方案.md) · [龙裔遗族专属武器](design/龙裔遗族专属武器设计方案.md) · [幽灵女巫](design/P0-新Boss-幽灵女巫.md) · [新武器](design/P0-新武器扩展.md) · [新套装](design/P1-新套装体系.md) · [护士羽织](design/NurseNPC_YuZhi_Design.md)

末日丧尸模式：[目录](design/zombie-mode/README.md)（设计文档、状态机、NPC 价目表、选项扩展、体验对齐执行记录；专项规则 `ZombieMode/AGENTS.md`）

路线图：[未来拓展](design/roadmap/README.md)（未实现选题与试炼四合一实施规格）

进行中的架构计划：[模块解耦、复用体系与上下文治理计划](design/2026-09-22_BossRushMod模块解耦与上下文治理计划.md) / [GitHub 架构对照与深审记录](design/2026-09-22_BossRushMod_GitHub架构对照与深审记录.md)（进 git，新窗口从计划 §0 开始，代码迁移尚未实施）

## reports/ — 结论报告

全部是带日期的历史快照：结论与修复状态以根目录 `FIX_TRACKER.md` / `CODE_REVIEW_FINDINGS.md` 为准。每个专题只留结论件，批次过程件与中间轮次已删。

- [reports/reviews/](reports/reviews)：代码审查与复审。最近一次全仓审计是 [2026-09-21 全仓审计报告](reports/reviews/2026-09-21_full_audit_report.md) + [修复闭环](reports/reviews/2026-09-22_full_audit_fixes.md)；官方更新后的 Harmony 同步清单也放这里（[2026-06-30](reports/reviews/官方更新同步清单-2026-06-30.md)）
- [testing](reports/testing)：人工实测与修复记录、F3 复测、全自动实机验收、性能分析、资源交付
- [reports/sky-island/](reports/sky-island)：天空岛各轮内容交付、评估、审核（时长模型脚本在 `时长模型/`）

## 维护约定

- 新文档先按上表选目录；不为单个专题再建顶层目录。
- 移动或改名文档时，同步改所有引用：`AGENTS.md`、`CODE_REVIEW_FINDINGS.md`、`FIX_TRACKER.md`、`.qoder/repowiki/`、代码注释、守卫与工具里的路径；被守卫读取的文档还要改 `.gitignore` 的放行行。
- 设计稿写状态行；报告写日期与历史快照说明；教程与架构说明跟着代码改。
- 规则性文字不写会过期的数字，写「以某命令 / 某文件为准」。
