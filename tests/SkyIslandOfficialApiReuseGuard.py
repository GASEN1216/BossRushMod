# -*- coding: utf-8 -*-
"""天空岛必须复用官方 API，而不是各造一套——以及**刻意不接**官方任务系统的理由归档。

## 背景（2026-09-13 调查）

owner 问「我们天空岛的 NPC 在剧情推动的时候有没有使用原版的 api」。查证结果是**没有，一行都没调**，
而且这不是「官方没有」——官方有，**我们自己还早就封装好了**：

| 能力 | 官方 | 我们的封装 | 谁在用 | 天空岛（改之前）|
| --- | --- | --- | --- | --- |
| 剧情对话 | `Dialogues.DialogueUI` + `DialogueTree.RequestSubtitles/RequestMultipleChoices` | `Integration/Dialogue/DialogueManager.cs` | 征程、快递员 | **零调用** |
| 图鉴条目 | `Duckov.NoteIndexs.NoteIndex` | 范例 `Campaign/CampaignNoteBridge.cs` | 征程 | **零调用**（自建手记）|

两处都改过来了，本守卫钉住不许退回去。

## 官方任务系统：**刻意不接**，理由在此归档

`Duckov.Quests.QuestManager` / `Quest` / `Task` / `QuestGiverView` 全套存在且完整，但天空岛**不接**：

1. `Quest` 与 `Task` 都是 **MonoBehaviour prefab**，`QuestManager.ActivateQuest` 只收 `int id`
   并去 `GameplayDataSettings.QuestCollection` 里 `Instantiate`；mod 要注册一条任务得运行时造 prefab 塞进去。
2. `QuestGiverID` 是**写死的 enum**（12 个官方 NPC），**没有 mod 的位置**。
3. `QuestManager` 实现 `ISaveDataProvider`，会把 mod 任务序列化进**官方存档键 `"Quest"/"Data"`**；
   卸载 mod 之后官方对缺失 id 打 LogError。这是**污染玩家存档**，按 `AGENTS.md` §10
   属于没有 owner 签字不得执行的事。
4. 语义也对不上：岛上委托是**按出击计、不进存档**的（`SkyIslandBounty.cs` 文件头），
   官方 Quest 是持久任务。

**这四条写在这里，是为了让下一个人不必重查一遍。** 想改这个决定，先拿到 owner 对第 3 条的签字。

反向检查在内存里恢复旧写法，确保每条断言真的抓得住。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source

SKY = "DebugAndTools/SkyIsland/"
DIALOGUE = SKY + "SkyIslandResidentDialogue.cs"
WORLD = SKY + "SkyIslandWorldStory.cs"
BRIDGE = SKY + "SkyIslandNoteBridge.cs"
PANEL = SKY + "SkyIslandStoryPresentation.cs"
FINDINGS = "CODE_REVIEW_FINDINGS.md"
START = "Integration/BossRushIntegration_StartAndScene.cs"

PATHS = [DIALOGUE, WORLD, BRIDGE, PANEL, START, FINDINGS]

# 天空岛**不许**出现的官方任务系统符号（理由见文件头）。
# findings 里那条归档小节的标题，守卫按它取范围。
ARCHIVE_HEADING = "### 本轮登记为 documented、**不是缺陷**的一条决策"

QUEST_SYMBOLS = ("QuestManager", "QuestGiverID", "QuestCollection", "QuestGiverView", "Duckov.Quests")


def check(sources):
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    dialogue = clean_source(sources[DIALOGUE])
    world = clean_source(sources[WORLD])
    bridge = clean_source(sources[BRIDGE])
    panel_raw = sources[PANEL]
    start = clean_source(sources[START])

    # ---- 1) 居民叙事走官方对话 ----
    require("SkyIslandResidentDialogue.Run(" in world,
            WORLD + " 的 Talk 没有走官方对话：官方 DialogueUI 我们自己早就封装好了"
                    "（DialogueManager），征程与快递员都在用，天空岛不该另画一套台词框")
    for token, why in (
        ("DialogueActorFactory.CreateBilingual(", "官方对话要有 actor 才有立绘位与名字"),
        ("DialogueManager.ShowDialogueSequenceBilingual(", "台词要逐句推进，不是一次糊一屏"),
        ("DialogueManager.ShowMultipleChoiceBilingual(", "说完要能问一句「要不要办事」"),
        ("SkyIslandUiArt.GetPortrait(", "立绘复用现成的，不为对话另出一套图"),
    ):
        require(token in dialogue, DIALOGUE + " 缺 " + token + "：" + why)

    # ---- 2) fail-open：跟 NPC 说不上话不能变成办不了事 ----
    # 拿不到 actor、官方对话抛异常，这两条都必须兜到功能面板：
    # 官方对话挂了不能变成「接不了委托、买不到苔药」。
    require(dialogue.count("if (CanContinue()) openPanel();") >= 2,
            DIALOGUE + " 的失败路径没有兜到 openPanel()："
                       "拿不到 actor、官方对话抛异常时都必须直接开功能面板——"
                       "官方对话挂了不能变成「接不了委托、买不到苔药」")
    require("catch (Exception" in dialogue and "[WARNING]" in dialogue,
            DIALOGUE + " 官方对话失败时必须记一条 WARNING 并继续，不许静默吞掉")

    # ---- 2b) 但「被取消」不是失败，不许也去开面板 ----
    # 取消发生在玩家走开、战斗打起来、模块销毁的时候（WorldStory.CanContinueDialogue）。
    # 这时候弹一个模态面板出来，等于在战斗中凭空给玩家一个暂停键。
    cancel_catch = "catch (OperationCanceledException) { }"
    require(cancel_catch in dialogue,
            DIALOGUE + " 缺 `" + cancel_catch + "`：取消必须与真失败分开接。"
                       "混进 catch (Exception) 的话，玩家一走开就会被塞一个功能面板——"
                       "而那是个 timeScale=0 的模态")
    require("DialogueManager.IsDialogueActive" in dialogue,
            DIALOGUE + " 没有挡重入：官方对话一次只能有一段，"
                       "否则两段台词会互相顶掉")
    for token, why in (
        ("CancellationTokenSource", "每段对话要能被单独取消，而不是去动别的模块的对话"),
        ("cancellation = null;", "finally 里必须把取消源交还，否则 Active 永远为真、再也说不上话"),
    ):
        require(token in dialogue, DIALOGUE + " 缺 " + token + "：" + why)

    # 会话侧必须真的去取消：光有取消源没人调等于没有。
    require("if (dialogue != null && dialogue.Active && !dialogue.CanContinue()) dialogue.Dispose();" in world,
            WORLD + " 的 Tick 没有在「不能继续」时取消对话："
                    "战斗打起来、模块销毁之后，在途的 async 仍会走到 openPanel()")
    require("if (dialogue != null) dialogue.Dispose();" in world,
            WORLD + " 的 Hide 没有取消在途对话：收起岛上一切之后还会弹出一个功能面板")

    # ---- 3) 面板的 reopen 不能指回 Talk ----
    # 指回 Talk 的话，每点一个选项（接一单委托、买一副苔药）都要把整段台词从头再听一遍。
    require("reopen = delegate { OpenResidentPanel(id, speaker); };" in world,
            WORLD + " 居民面板的 reopen 必须指向 OpenResidentPanel 而不是 Talk："
                    "指回 Talk 的话每点一个选项都要重播整段对话")
    require("reopen = delegate { Talk(id, speaker); };" not in world,
            WORLD + " 居民面板的 reopen 指回了 Talk：选项回执会重播整段台词")

    # ---- 4) 见闻走官方图鉴 ----
    for token, why in (
        ("NoteIndex.SetNoteDynamic(note)", "官方按 key 查条目靠字典"),
        ("notes.Add(note)", "图鉴界面列条目走 notes 列表，只 SetNoteDynamic 的话一条也看不见"),
        ("NoteIndex.SetNoteUnlocked(key)", "已收录的见闻要在官方图鉴里点亮"),
        ("SkyIslandPointText.Lore(id)", "图鉴正文取岛上同一份文案，不许另写"),
    ):
        require(token in bridge, BRIDGE + " 缺 " + token + "：" + why)
    require("SkyIslandNoteBridge.InjectNoteKeys();" in start,
            START + " 没有注入见闻图鉴文案：官方按 Note_{key}_Title/_Content 查表，缺了显示裸 key")

    # ---- 5) 官方任务系统刻意不接 ----
    for symbol in QUEST_SYMBOLS:
        for rel in (WORLD, BRIDGE, DIALOGUE):
            require(symbol not in sources[rel],
                    rel + " 引用了官方任务系统符号 " + symbol + "。"
                          "这是刻意不接的：Quest/Task 是 prefab、QuestGiverID 是写死的 enum、"
                          "而且 QuestManager 会把 mod 任务写进官方存档键（卸载后官方报错）——"
                          "属 AGENTS §10 需 owner 签字。理由见本守卫文件头。")

    # ---- 6) 自绘面板保留的理由必须写下来 ----
    # 此前这条只存在于 repowiki，代码与文档里从来没写过「为什么不用官方对话」——
    # 所以 owner 才会问「我们天空岛的 NPC 有没有使用原版的 api」。
    head = panel_raw[:panel_raw.find("using ")] if "using " in panel_raw else panel_raw[:4000]
    for token, why in (
        ("timeScale = 0", "要点名是哪一条官方 DialogueUI 给不了的能力"),
        ("免费暂停", "要说清没有模态门的后果：面板里挂着苔药与整备，那就是战斗中的回血站"),
    ):
        require(token in head,
                PANEL + " 文件头缺「" + token + "」：**为什么保留自绘面板**这条理由必须写在代码里，"
                        "否则下一个人还会再问一遍「为什么不用官方对话」（" + why + "）")

    # ---- 7) 「任务系统刻意不接」的理由必须归档在 findings 里 ----
    # 光在代码注释里写不够：这是一条**决策**，要能在问题库里查到，才不会每轮重查一遍。
    # 断言只在归档小节内取词——整篇 findings 里 prefab / AGENTS.md / LogError 到处都是，
    # 拿全文做断言的话，把这一小节整个删掉守卫也不会红。
    findings = sources[FINDINGS]
    if ARCHIVE_HEADING not in findings:
        require(False,
                FINDINGS + " 找不到归档小节「" + ARCHIVE_HEADING + "」。owner 明确问过"
                           "「玩家的任务也是原版有的，你可以看看我们是否使用了」——"
                           "结论与四条理由必须落在问题库里，否则下一轮还要从头查一遍官方任务系统。")
    else:
        # CRLF 也吃得下：换行符自身就是切点的一部分。
        section = findings.split(ARCHIVE_HEADING, 1)[1].split(chr(10) + "## ", 1)[0]
        for token, why in (
            ("Duckov.Quests", "要点名是哪一套官方系统"),
            ("prefab", "Quest/Task 是 MonoBehaviour prefab，mod 注册不进去"),
            ("QuestGiverID", "给予者是写死的 enum，没有 mod 的位置"),
            ("LogError", "卸载 mod 后官方对存档里缺失的 id 报错"),
            ("AGENTS.md", "污染官方存档属 §10，需 owner 签字"),
            ("NoteIndex", "要同时写明**相反的那条该接**，否则读者会以为「官方的一律不用」"),
        ):
            require(token in section,
                    FINDINGS + " 的官方任务系统归档小节里缺「" + token + "」：" + why)
    return errors


def main():
    sources = {}
    for rel in PATHS:
        path = ROOT / rel
        if not path.is_file():
            print("SkyIslandOfficialApiReuseGuard: FAIL - 找不到 " + rel)
            return 1
        sources[rel] = path.read_text(encoding="utf-8-sig")

    errors = check(sources)

    probes = [
        # 居民叙事退回自绘
        (WORLD, "SkyIslandResidentDialogue.Run(", "NoOpDialogue.Run("),
        # 取消被当成失败处理：玩家一走开就被塞一个 timeScale=0 的模态面板
        (DIALOGUE, "catch (OperationCanceledException) { }", ""),
        # 拿不到 actor 时不再兜底开面板
        (DIALOGUE, "if (CanContinue()) openPanel();", ""),
        # finally 不交还取消源：Active 永远为真，再也说不上话
        (DIALOGUE, "cancellation = null;", ""),
        # 不挡重入：两段台词互相顶掉
        (DIALOGUE, "DialogueManager.IsDialogueActive", "false"),
        # 会话侧不再取消在途对话
        (WORLD, "if (dialogue != null && dialogue.Active && !dialogue.CanContinue()) dialogue.Dispose();", ""),
        (WORLD, "if (dialogue != null) dialogue.Dispose();", ""),
        # reopen 指回 Talk：每点一个选项重播整段台词
        (WORLD, "reopen = delegate { OpenResidentPanel(id, speaker); };",
                "reopen = delegate { Talk(id, speaker); };"),
        # 图鉴只写字典不写列表：按 key 查得到，界面一条也看不见
        (BRIDGE, "notes.Add(note);", ""),
        # 图鉴正文另写一份
        (BRIDGE, "SkyIslandPointText.Lore(id)", '"另写的正文"'),
        # 接了官方任务系统
        (BRIDGE, "using Duckov.NoteIndexs;", "using Duckov.NoteIndexs;\nusing Duckov.Quests;"),
        # 自绘面板的理由被删掉
        (PANEL, "免费暂停", "（略）"),
        # 决策归档整节被删掉：后人又得从头查一遍官方任务系统
        (FINDINGS, "### 本轮登记为 documented", "### 本轮某节"),
        # 只删掉其中一条理由（存档污染那条最容易被当成「保守」而删）
        (FINDINGS, "打 `LogError`", "照常工作"),
        (FINDINGS, "`QuestGiverID` 是写死的 enum", "给予者可以随便填"),
    ]
    for path, before, after in probes:
        if before not in sources[path]:
            errors.append("反向检查锚点失效：" + path + " -> " + before)
            continue
        altered = dict(sources)
        altered[path] = altered[path].replace(before, after, 1)
        if not check(altered):
            errors.append("未拦截旧写法：" + path + " -> " + before)

    if errors:
        print("SkyIslandOfficialApiReuseGuard: FAIL\n  " + "\n  ".join(errors))
        return 1
    print("SkyIslandOfficialApiReuseGuard: PASS（官方对话 + 官方图鉴 + fail-open + "
          "任务系统刻意不接的理由归档；%d 个反向检查）" % len(probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
