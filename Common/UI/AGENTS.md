# Common/UI/AGENTS.md — 共享 UI 专项规则

> 先读根目录 `AGENTS.md`。以下保留原根规则 §4.14 的完整条文。

### 4.14 UI 走共享库与官方界面

- Canvas `sortingOrder` 用 `BossRushUILayers` 常量；颜色用 `BossRushUIColors` token，遮罩用 `Backdrop`。
- 面板、按钮、卡片底图走 `BossRushUI.ApplyPanelSkin`：卡片、分隔线、滚动滑块与细轨显式传 `BossRushUISkinPart`，`radius <= 3` 的细条程序化绘制（规格见 `docs/guides/BossRushUI_图集规格.md`）。深色面板要有边就调 `BossRushUI.ApplyPanelStroke`（描边色 `BossRushUIColors.Stroke`），或用 `ApplyFramedPanelSkin` 一次套上底图与描边——图集里烤进去的内描边会被深色 token 乘到看不见。
- 按钮字色用 `BossRushUI.GetButtonTextColor(背景色)`，不写死；对比度按实际合成后的底色算，正文至少 4.5:1。游戏是 Linear 色彩空间，半透明在线性光里混合，复算按线性模型（`docs/contracts.md` §7.1），观感修复以实机截图取色为准。
- 缓动只用 `BossRushUI.EaseOut`（位移）与 `BossRushUI.SmoothStep`（原地淡变），子元素错峰入场用 `BossRushUIEntranceAnimation`，不引入 DOTween 一类第三方依赖。走 unscaled 时间的表现层自带 `BossRushUI.IsGamePaused()` 门；常驻 HUD 跟随 `BossRushUI.IsOfficialHudHidden()`。
- **质感层（2026-09-23，owner 验收「不要塑料感」）**：按钮一律经过 `ZombieModeUIHelper.ApplyButtonColors` / `SetButtonBaseColor`，面板与卡片一律经过 `ApplyPanelStroke`（或 `ApplyFramedPanelSkin`、`CreateCard`、`CreateModalSurface`），这样才拿得到共享层（`Common/UI/BossRushUIFeel.cs`）挂上的官方 `UI/hover`、`UI/click` 音效、按下回弹、外投影、顶边高光与描边置顶。手搓 `AddComponent<Button>()` 或裸 `Image` 当面板，等于绕开这一层。关闭用 `BossRushUIKit.PlayCloseAndDestroy`（先释放输入租约），手搓遮罩调 `BossRushUIKit.StyleBackdrop`。压在游戏世界上的 HUD 字用 `BossRushUIKit.ApplyWorldTextOutline`；`UI.Outline` / `UI.Shadow` 挂在 TMP 上无效。
- **按钮配色口径**：主操作的整块填充用 `BossRushUIColors.AccentFill`，每屏最多一个；`Accent` 只做描边、强调竖条、焦点环、进度条、小字强调，不铺满整块按钮（owner 点名的「平涂薄荷绿」）；危险且不可逆用 `Danger`；其余是次级按钮（`BossRushUIKit.StyleSecondaryButton`）；`Success` 只表示「已完成 / 已达成」状态。富文本颜色用 token 预先转成的 hex，不写 `<color=red>` 一类纯色。
- **程序化特效材质**一律用 `BossRushFxMaterials.Get(Alpha / Additive, 贴图)`（`Common/Effects/BossRushFxMaterials.cs`）。游戏里 `Shader.Find` 找不到 `Legacy Shaders/Particles/Additive`、`Particles/Additive`、`Particles/Alpha Blended`、`Mobile/Particles/*`、`Unlit/Color`、`Unlit/Transparent`，`Standard` 在 URP 下画不出来；各写一串回退链只会落到不发光的 `Sprites/Default` 或 alpha 失效的方片。
- 字体用 `BossRushUI.ApplyGameFont` / `ZombieModeUIHelper.GetGameFont()`，新文本用 TMP（内置 Arial 渲染不了中文）；`CanvasScaler` 调 `ZombieModeUIHelper.ConfigureCanvasScaler`。
- 玩家可见文本（含 `WikiContent/` 正文与随包数据表）**只能用 GBK 收录的符号**：官方字体是中文字体，★☆○●◎◇■□△▲※→←↑↓√Ⅰ① 一定有字形，Emoji、✓✗❄❌⚠、U+2212 减号、U+2022 圆点在游戏里是空白豆腐块（2026-09-19 实测）。判据与例外见 `tests/PlayerFacingGlyphGuard.py`；日志与只给人在编辑器里读的报告不受此限。
- 能直接复用官方 prefab（`GameplayDataSettings.UIPrefabs.*`、克隆 `MapSelectionEntry` 等）就不用共享库重造。官方 `UIPrefabs.ScrollRect` 的 content 自带竖排布局与自适应高度：往里手动摆位置的内容要先 `DestroyImmediate` 摘掉这两个组件，否则卡片被压成一列（2026-09-25 鸭王杯三页实测）。
- 选项先判断再挂，不挂灰掉的占位项；「能不能挂」与「点了会不会被拒」共用同一份判据；同一页超过 3–4 项就分二级。列表页（合成配方、航务委托）例外：上限 6 项，且不带立绘。
  付费服务照主流商店口径（2026-09-14 拍板）：没有要做的不挂；钱不够、还在冷却照挂，按钮上写明价钱或还要等几秒。剧情前置没到、已经做完、纯说明性的占位项一律不挂，「还差什么」进正文。
- **交互骨架（UI 制作共识，2026-09-24，owner 要求新 UI 一律照做）**：全文 `docs/architecture/UI制作共识.md`。动手前先定页型——单决策页（样板：鸭王杯入场选人页）、列表 + 详情（遗种巢巢页）、分区任务清单（孵化页）、一屏表单（远征页）、确认弹窗，能用官方界面就不自绘。按钮跟着它作用的对象走：单对象操作进该对象的行内或详情底栏，批量「选择」 / 排序进列表头，页面结论放底栏最右，换视图用页签；不要每张卡挂几颗按钮，不要把单对象与全局操作混在一条动作条里，不要依赖看不见的跨页选中。危险操作进确认前是红描边红字、靠左、远离主操作，确认一律走共享的 `BossRushConfirmDialog`（`Common/UI/BossRushConfirmDialog.cs`），不再各写一份。列表行只放识别信息（图、名字、一行状态），说明进详情、「说明」页或空状态。「干净」照鸭王杯选人页：页头只有横幅 / 标题（2026-09-29 owner：不加引导句），一页一个决定，数量有界不滚动，大留白，卡片整张可点，深色底 + 一种强调色，流程页不挂风险 / 免责文字（危险操作的后果写进确认框），白话文案。交付前按该文第 10 节自检。
- 叙事走官方对话（`DialogueManager.ShowDialogueSequenceBilingual` / `ShowMultipleChoiceBilingual`，长文案一句一屏），图鉴条目走官方 `NoteIndex`（我们的存档是权威，官方图鉴只做双向镜像）。镜像会随官方存档写进 `NoteIndexData`，2026-09-14 拍板接受为 §10「写入官方存档键」的例外（`docs/contracts.md` §7.1）。自绘面板只在官方给不了的能力上保留，理由写进文件头。跨局、一次性的持久剧情可以在 owner 明确授权后接 `Duckov.Quests`；已授权范围：**天空岛跨局主线**（2026-09-16：Jeff 序章 590001 加岛上三条 590011–590013，给予者是岛上居民，用官方 enum 之外的整数 5901–5903）、**鸭王征程六章**（2026-09-22：590101–590106，给予者官方 Jeff=1）与**新内容一次性引导**（2026-09-25：590201–590214，给予者官方 Jeff=1）。任务表分别为 `SkyIslandOfficialQuestTable`、`CampaignQuestTable` 与 `CampaignGuideTable`，后两者共用征程客户端；投影核心只有 `Utilities/OfficialQuests/` 一份（唯一实例、四个 Harmony 补丁只装一次，守卫 `OfficialQuestProjectionGuard`），各客户端都以各自的 Mod 存档为权威，官方 Quest 只做 UI / 事件投影，并在保存快照中过滤自定义 ID。按出击刷新的岛内委托不接跨局 Quest（教程见 `docs/guides/官方任务系统接入教程.md`）。

守卫：`BossRushUISharedLibraryGuard`、`BossRushUISkinLoaderGuard`、`BossRushUIFeelGuard`、`SkyIslandUiContrastGuard`、`SkyIslandOfficialApiReuseGuard`、`SkyIslandChoiceGateGuard`。

