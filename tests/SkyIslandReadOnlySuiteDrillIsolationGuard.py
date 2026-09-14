# -*- coding: utf-8 -*-
"""岛内只读验收不得引用 Dev 演练代码（2026-09-14）。

Dev 演练套件（`DebugAndTools/F3GameplayValidationSkyIslandDrill.cs` + `DebugAndTools/SkyIsland/SkyIslandGnatsDrill.cs`）
会改这趟出击的状态：强制夜里、刷云蚋、给躲闪状态机登记合成弹道、打死一只、压主角血量、弹官方对话。
它能被接受，全靠三件事：只在 Dev 构建里存在、只经独立按钮进入、报告头如实写 `read_only=false`。

只读套件哪怕只引用它一个符号，`read_only=true` 的报告里就混进了会改状态的步骤——而报告本身看不出来。
正式构建里这些符号不存在（`#if BOSSRUSH_DEV`），只读文件引用它们会让正式构建编不过；可 Dev 构建照样编得过，
而 F3 恰恰只在 Dev 构建里跑，所以编译挡不住，要守卫挡。

钉住：
1. 只读文件（岛内编排、两份用例、会话观测面）清洗注释后不出现任何演练符号或 `SKY_DRILL_` 用例 id；
2. 演练文件里声明的每一个成员名都被下面的符号表覆盖——演练里新加一个方法而忘了登记，这份守卫会先红，
   免得「符号表过期 → 只读文件引用了新方法也照样绿」。
反向检查在内存里往每个只读文件塞一个演练符号，必须转红。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source  # noqa: E402

READ_ONLY = (
    "DebugAndTools/F3GameplayValidationSkyIsland.cs",
    "DebugAndTools/F3GameplayValidationSkyIslandCases.cs",
    "DebugAndTools/F3GameplayValidationSkyIslandRuntimeCases.cs",
    "DebugAndTools/SkyIsland/SkyIslandSessionValidation.cs",
)
DRILL_FILES = (
    "DebugAndTools/F3GameplayValidationSkyIslandDrill.cs",
    "DebugAndTools/SkyIsland/SkyIslandGnatsDrill.cs",
)
DRILL_SYMBOL = re.compile(
    r"\b(?:SkyIslandDrillCaseIds|TryStartSkyIslandDrill|RunSkyIslandDrill\w*|_skyIslandDrill|CountLooseLootForDrill"
    r"|Drill(?:ApproachSeconds|Shots|ShotInterval|BiteWatchSeconds|FloorWatchSeconds)"
    r"|DevSpawnAround|DevSyntheticShot|DevKillOne|DevMotorStats)\b|SKY_DRILL_")
DECLARATION = re.compile(
    r"\b(?:internal|private|public|protected)\s+(?:static\s+)?(?:readonly\s+)?(?:const\s+)?"
    r"[\w<>\[\],.]+\s+(\w+)\s*(?:\(|=|;)")


def check(sources):
    errors = []
    for rel in READ_ONLY:
        code = sources.get(rel)
        if code is None:
            errors.append("找不到只读文件 " + rel)
            continue
        hits = sorted(set(m.group(0) for m in DRILL_SYMBOL.finditer(code)))
        if hits:
            errors.append("只读验收文件 %s 引用了 Dev 演练代码：%s（演练会改状态，只能经独立按钮进入）"
                          % (rel, ", ".join(hits)))
    for rel in DRILL_FILES:
        code = sources.get(rel)
        if code is None:
            errors.append("找不到演练文件 " + rel)
            continue
        declared = set(DECLARATION.findall(code))
        # 类型名与局部变量不在此列：只看成员声明。F3GameplayValidationRunner / SkyIslandGnats 是宿主类型本身。
        uncovered = sorted(name for name in declared
                           if not DRILL_SYMBOL.search(name) and name not in ("F3GameplayValidationRunner", "SkyIslandGnats"))
        if uncovered:
            errors.append("演练文件 %s 声明了符号表没覆盖的成员：%s。把它加进本守卫的 DRILL_SYMBOL，"
                          "否则只读文件引用它时这里看不见" % (rel, ", ".join(uncovered)))
    return errors


def main():
    sources = {}
    for rel in READ_ONLY + DRILL_FILES:
        path = ROOT / rel
        if path.is_file():
            sources[rel] = clean_source(path.read_text(encoding="utf-8-sig"))
    errors = check(sources)

    probes = 0
    for rel in READ_ONLY:
        if rel not in sources:
            continue
        for injected in ("\nvoid Zz() { string r; TryStartSkyIslandDrill(null, out r); }\n",
                         '\nstring zz = "SKY_DRILL_GNAT_SWARM";\n',
                         "\nvoid Zz(SkyIslandGnats s) { int a; float b, c; s.DevMotorStats(out b, out c, out a); }\n"):
            probes += 1
            altered = dict(sources)
            altered[rel] = sources[rel] + injected
            if not check(altered):
                errors.append("反向检查失效：往 %s 塞入演练符号之后守卫仍然全绿" % rel)
    probes += 1
    drill = DRILL_FILES[0]
    if drill in sources:
        altered = dict(sources)
        altered[drill] = sources[drill] + "\nprivate IEnumerator ZzNewDrillStep() { yield break; }\n"
        if not check(altered):
            errors.append("反向检查失效：演练文件新增一个没登记的成员之后守卫仍然全绿")

    if errors:
        print("SkyIslandReadOnlySuiteDrillIsolationGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("SkyIslandReadOnlySuiteDrillIsolationGuard: PASS（%d 个只读文件不引用演练代码；演练成员全部在符号表里；%d 个反向检查）"
          % (len(READ_ONLY), probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
