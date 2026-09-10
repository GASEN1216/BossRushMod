"""F3 天空岛岛内验收套件的结构不变式。

这套东西的价值全在两件事上，而这两件事都很容易在后续改动里被悄悄破坏：

1. **它必须能在岛内跑。** 主套件的 `CheckStartGate` 从 2026 年起就显式拒绝天空岛
   （"请先退出天空岛"），天空岛因此长期零 F3 覆盖。岛内入口一旦被合并回主门、
   或者收尾阶段被改成走主套件的 `RunFinalChecks`（那会 LoadScene 回基地），
   这趟出击就会被验收自己送走，等于回到原点。
2. **它必须只读。** 岛内验收跑在玩家真实的一趟出击上。任何写剧情、搬玩家、生成敌人、
   开箱的动作都会污染他正在做的事，而且这种污染在报告里看不出来。

守卫钉住的是结构，不是行为：它证明不了套件跑起来是对的，只证明上面两条纪律还在。
实机 smoke 仍按 `Assets/Data/GameplayCoverage.json` 的 `M_SKY_ISLAND_*` 逐条走。
"""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
NEWLINE = chr(10)

SUITE = "DebugAndTools/F3GameplayValidationSkyIsland.cs"
CASES = "DebugAndTools/F3GameplayValidationSkyIslandCases.cs"

# `Inspect` 的 CJK 码位区间。写成整数常量而不是字面汉字或 \u 转义，是因为后两者
# 在文件被按非 UTF-8 读写、或经过会折反斜杠的工具时会静默变形，而这条断言完全靠
# 区间成立——变形之后它会安静地永远为真，也就是最糟的假绿。
EXPECTED_CJK_RANGES = [
    ("CjkIdeographStart", "0x4E00"), ("CjkIdeographEnd", "0x9FFF"),
    ("CjkExtensionAStart", "0x3400"), ("CjkExtensionAEnd", "0x4DBF"),
    ("CjkPunctuationStart", "0x3000"), ("CjkPunctuationEnd", "0x303F"),
    ("FullWidthFormsStart", "0xFF00"), ("FullWidthFormsEnd", "0xFFEF"),
]

# 会改变玩家这趟出击状态的生产入口。岛内验收一条都不许调。
# 只列**确实会写**的：读属性（IsReady / Current / Summary）不在此列。
FORBIDDEN_MUTATIONS = [
    ".RecordSearch(",               # 收录见闻
    ".RecordEncounterCleared(",     # 记清场
    ".RecordRegionVisited(",        # 点亮区域
    ".BeginStoryChallenge(",        # 开战
    ".SpawnEnemy(",                 # 刷怪
    ".VisitNextLandmark(",          # 搬玩家
    ".CycleLighting(",              # 改光色
    ".DropBountyReward(",           # 发奖励箱
    ".TryAccept(", ".TryClaim(", ".TryAbandon(",   # 动委托
    "SetPosition(",                 # 搬玩家
    "SavesSystem.Save<",            # 直接写档
    "SkyIslandRewardCrate.Create(", "SkyIslandRewardCrate.Build(",  # 建箱
]


def main():
    def read(path):
        return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))

    suite = read(SUITE)
    cases = read(CASES)
    runner = read("DebugAndTools/F3GameplayValidationRunner.cs")
    execution = read("DebugAndTools/F3GameplayValidationExecution.cs")
    session = read("DebugAndTools/SkyIsland/SkyIslandSession.cs")
    bat = (ROOT / "compile_official.bat").read_text(encoding="utf-8", errors="ignore")
    errors = []

    def need(source, label, *tokens):
        for token in tokens:
            if token not in source:
                errors.append(label + " 缺少 " + token)

    # ---- 1. 两个新文件必须进编译清单（无通配符，漏了不报错）----
    for path in (SUITE, CASES):
        if path.replace("/", chr(92)) not in bat:
            errors.append("编译清单缺少 " + path)

    # ---- 2. 岛内入口存在，且门的方向与主套件相反 ----
    need(suite, "岛内入口",
         "internal static bool TryStartSkyIsland(ModBehaviour host, out string reason)",
         "private bool CheckSkyIslandStartGate(out string reason)",
         "_instance._skyIslandMode = true;")
    gate = suite.split("private bool CheckSkyIslandStartGate(out string reason)", 1)
    if len(gate) != 2:
        errors.append("找不到岛内启动门")
    else:
        gate_body = gate[1].split(NEWLINE + "        }", 1)[0]
        need(gate_body, "岛内启动门",
             # 必须真的在岛上：没有会话 / 会话没就绪 / 活动场景不是 raid 都不许跑
             "_host.GetComponent<SkyIslandSession>()",
             "session.IsReady",
             "SkyIslandRaidLease.IsRaidScene(SceneManager.GetActiveScene())",
             # 跑在真实出击上，必须是专用测试档
             "IsDedicatedCurrentSlot()",
             "ModBehaviour.DevModeEnabled")

    # ---- 3. 主套件仍然拒绝在岛内启动（互斥按用例区分，不是整条删掉）----
    main_gate = runner.split("private bool CheckStartGate(out string reason)", 1)
    if len(main_gate) != 2:
        errors.append("找不到主套件启动门")
    else:
        main_body = main_gate[1].split(NEWLINE + "        }", 1)[0]
        if "_host.GetComponent<SkyIslandSession>()" not in main_body:
            errors.append("主套件启动门不再拒绝天空岛：它会 LoadScene 回基地，等于把这趟出击送走")

    # ---- 4. 两个阶段都要按模式分流；收尾绝不切图 ----
    need(execution, "会话分流",
         "_skyIslandMode ? RunSkyIslandSuite() : RunSuite()",
         "_skyIslandMode ? RunSkyIslandFinalChecks() : RunFinalChecks()",
         # 中途取消/异常时标志必须在唯一收尾路径上复位，否则下次从基地启动会错走岛内编排
         "_skyIslandMode = false;")
    final = suite.split("private IEnumerator RunSkyIslandFinalChecks()", 1)
    if len(final) != 2:
        errors.append("找不到岛内收尾阶段")
    else:
        final_body = final[1].split(NEWLINE + "        }", 1)[0]
        for token in ("LoadScene", "returnToBase", "ReturnToBase"):
            if token in final_body:
                errors.append("岛内收尾不得切图（会把玩家连这趟出击一起送回基地）：" + token)

    # ---- 5. 只读纪律 ----
    for label, source in (("岛内套件", suite), ("岛内用例", cases)):
        for token in FORBIDDEN_MUTATIONS:
            if token in source:
                errors.append(label + " 不得改变玩家这趟出击的状态：" + token)
        # `TryApply` 有两个同名入口，只有一个是安全的：
        #   `SkyIslandStoryRules.TryApply` 是**纯函数**，只按输入算出候选状态并返回文案，
        #     英文完整性用例正是靠它把全部动作回执取出来扫 CJK；
        #   `SkyIslandStoryService.TryApply` 会 `store.Store(candidate)`，那是真写存档。
        # 所以不能整条禁掉，只能要求出现的每一处都是静态那个。
        cursor = source.find(".TryApply(")
        while cursor >= 0:
            if not source[:cursor].endswith("SkyIslandStoryRules"):
                errors.append(label + " 只允许调用纯函数 SkyIslandStoryRules.TryApply，"
                              "不得调用会写存档的 SkyIslandStoryService.TryApply")
                break
            cursor = source.find(".TryApply(", cursor + 1)

    # ---- 6. 判据不成立时记 SKIP，不许记 PASS ----
    need(runner, "SKIP 通道", "catch (SkyIslandSkipCase skip)", 'Record(id, "SKIP"')
    need(suite, "SKIP 信号", "internal sealed class SkyIslandSkipCase : Exception")
    english = cases.split("private bool ValidateSkyIslandEnglishText(", 1)
    if len(english) != 2:
        errors.append("找不到英文完整性用例")
    elif "throw new SkyIslandSkipCase" not in english[1].split(NEWLINE + "        }", 1)[0]:
        errors.append("中文语境下英文用例必须记 SKIP：扫不出中文残留是恒真，记 PASS 就是假绿")

    # ---- 7. CJK 区间必须是整数常量且数值正确 ----
    for name, value in EXPECTED_CJK_RANGES:
        if (name + " = " + value) not in cases:
            errors.append("CJK 码位常量不正确或不存在：" + name + " = " + value)
    inspect = cases.split("private static void Inspect(", 1)
    if len(inspect) != 2:
        errors.append("找不到 CJK 扫描函数")
    else:
        inspect_body = inspect[1].split(NEWLINE + "        }", 1)[0]
        if chr(92) + "u4E00" in inspect_body or chr(92) + "u9FFF" in inspect_body:
            errors.append("CJK 扫描不得用 \\u 转义（跨工具复制会被折反斜杠）")
        for token in ("CjkIdeographStart", "CjkExtensionAStart", "CjkPunctuationStart", "FullWidthFormsStart"):
            if token not in inspect_body:
                errors.append("CJK 扫描漏了区间 " + token)

    # ---- 8. 批量 SKIP 清单必须与实际跑的用例一致 ----
    suite_body = suite.split("private IEnumerator RunSkyIslandSuite()", 1)
    if len(suite_body) != 2:
        errors.append("找不到岛内编排")
    else:
        body = suite_body[1].split(NEWLINE + "        }", 1)[0]
        executed = set()
        for marker in ('RunSyncCase("', 'RunSkyIslandCase("'):
            start = 0
            while True:
                start = body.find(marker, start)
                if start < 0:
                    break
                start += len(marker)
                executed.add(body[start:body.find('"', start)])
        listed = set()
        listing = suite.split("private static readonly string[] SkyIslandCaseIds", 1)[1]
        listing = listing.split("};", 1)[0]
        for chunk in listing.split('"')[1::2]:
            listed.add(chunk)
        missing = sorted(executed - listed)
        extra = sorted(listed - executed)
        if missing:
            errors.append("SkyIslandCaseIds 漏登记（装配失败时报告会留空白）：" + ",".join(missing))
        if extra:
            errors.append("SkyIslandCaseIds 登记了不存在的用例：" + ",".join(extra))

    # ---- 9. 会话侧的观测面必须是只读的 ----
    need(session, "会话观测面",
         "internal SkyIslandValidationSnapshot ValidationSnapshot()",
         "internal bool ValidationIsInsideExtractionAt(Vector3 position, out string markerName)",
         "internal static float ValidationExtractionRadius")
    probe = session.split("internal bool ValidationIsInsideExtractionAt(", 1)
    if len(probe) != 2:
        errors.append("找不到撤离几何探测入口")
    else:
        probe_body = probe[1].split(NEWLINE + "        }", 1)[0]
        # 只准读 exitMarker / bellExit 与传入坐标；碰玩家或写字段就不再是只读探测
        for token in ("player", "extractionStarted", "Close(", "SetPosition("):
            if token in probe_body:
                errors.append("撤离几何探测必须只读、不得读写玩家或倒计时状态：" + token)
        if "ExtractionRadius" not in probe_body:
            errors.append("撤离几何探测必须与真实判定共用同一个半径常量")

    if errors:
        for error in errors:
            print("  - " + error)
        print("SkyIslandValidationSuiteGuard: FAIL")
        raise SystemExit(1)
    print("SkyIslandValidationSuiteGuard: PASS (岛内入口/只读纪律/SKIP 通道/CJK 区间/用例清单)")


if __name__ == "__main__":
    main()
