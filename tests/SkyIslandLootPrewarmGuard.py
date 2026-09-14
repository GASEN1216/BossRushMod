# -*- coding: utf-8 -*-
u"""天空岛物资池预热（CR-2026-09-14-014）：预热调用必须在进岛装配路径上，不在每帧路径上。

背景：`SkyIslandLootPools.GetBand` 首次用到才建池，而搜刮箱要等玩家走进 72 m 才由
`SkyIslandScavenging.Build → SkyIslandRewardCrate.Fill → SkyIslandLootPools.Get` 去建、去填，
第一次走近远航档或星工档箱子的那一帧就要扫全部官方标签（首轮实机 `SKY_LOOT_BANDS` 这一步 639 ms）。
现在会话在读条画面下（`navigationReady = true` 之前）按品质带分帧预热。

钉住：
1. 会话 `Build()` 协程里调用 `SkyIslandLootPools.Prewarm()` 并逐帧透传 `Current`，位置在 `lease.BeginLoad();` 之后、
   `navigationReady = true;` 之前（装配期间官方关卡初始化还在等会话，读条画面还在）。
2. 全仓只有这一个调用点；任何 `Update` / `LateUpdate` / `Tick` / `Frame` 方法体里都没有它。
3. `Prewarm` 按 `SkyIslandLootTables.PrewarmBands()` 逐带建、已缓存跳过、**每建一个带让出一帧**；不清缓存（缓存复位口径不变，
   只在模块销毁时 `ResetStaticCaches`，且全仓唯一调用点在模块 `OnDestroy`）。
4. 预热带表覆盖每一档的常规带与保底带（`PrewarmBands` 的算法；集合相等由执行回归逐项核对）。
5. F3 `SKY_LOOT_BANDS` 先读缓存状态、再第一次调用 `Get`（反过来判据恒真），并交给纯判据 `JudgeLootPrewarm`。

反向检查在内存里恢复错误写法，确认每条都抓得住。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

SESSION = "DebugAndTools/SkyIsland/SkyIslandSession.cs"
POOLS = "DebugAndTools/SkyIsland/SkyIslandLootPools.cs"
TABLES = "DebugAndTools/SkyIsland/SkyIslandLootTables.cs"
MODULE = "DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs"
CASES = "DebugAndTools/F3GameplayValidationSkyIslandCases.cs"
FIXED = [SESSION, POOLS, TABLES, MODULE, CASES]
PER_FRAME = re.compile(r"(?:private|internal|public|protected)?\s*(?:static\s+)?void\s+(?:Update|LateUpdate|FixedUpdate|Tick|Frame|OnUpdate)\s*\(")


def squash(text):
    text = re.sub(r"\s+", " ", text or "")
    return re.sub(r"\s*([(){};:,.\[\]<>=!?|&+*/-])\s*", r"\1", text)


def body_of(source, signature, start_at=0):
    start = source.find(signature, start_at)
    if start < 0:
        return None
    brace = source.find("{", start + len(signature))
    if brace < 0:
        return None
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:i]
    return None


def scanned_paths():
    paths = sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / "DebugAndTools/SkyIsland").glob("*.cs"))
    paths += sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / "Integration/SkyIsland").glob("*.cs"))
    paths += sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / "DebugAndTools").glob("F3GameplayValidation*.cs"))
    return paths


def check(sources):
    errors = []
    src = {path: clean_source(text) for path, text in sources.items()}

    # ---- 1. 装配路径上调用、逐帧透传 ----
    build = squash(body_of(src[SESSION], "private IEnumerator Build()") or "")
    if not build:
        errors.append(SESSION + " 找不到 Build() 协程")
    call = build.find(squash("IEnumerator warm = SkyIslandLootPools.Prewarm();"))
    drive = build.find(squash("while (warm.MoveNext()) yield return warm.Current;"))
    begin = build.find(squash("lease.BeginLoad();"))
    ready = build.find(squash("navigationReady = true;"))
    if call < 0 or drive < 0:
        errors.append("会话装配协程没有预热物资池（或没有逐帧透传 Prewarm 的 Current）")
    elif not (0 <= begin < call < drive < ready):
        errors.append("物资池预热必须在 lease.BeginLoad() 之后、navigationReady = true 之前（读条画面下、官方关卡初始化还在等会话）")

    # ---- 2. 全仓唯一调用点，不在每帧路径上 ----
    total = 0
    for path, text in src.items():
        if path in FIXED and path != SESSION:
            pass
        total += len(re.findall(r"\bSkyIslandLootPools\s*\.\s*Prewarm\s*\(", text))
        for match in PER_FRAME.finditer(text):
            body = body_of(text, match.group(0), match.start()) or ""
            if re.search(r"\bPrewarm\s*\(", body):
                errors.append(path + " 的每帧方法里调用了物资池预热：" + match.group(0).strip())
    if total != 1:
        errors.append("SkyIslandLootPools.Prewarm() 全仓只许一个调用点（会话装配协程），实际 %d 个" % total)

    # ---- 3. 逐带建、每建一个让出一帧、不清缓存 ----
    prewarm = squash(body_of(src[POOLS], "internal static IEnumerator Prewarm()") or "")
    loop_at = prewarm.find(squash("for (int i = 0; i < bands.Length; i++)"))
    tokens = ["int[][] bands = SkyIslandLootTables.PrewarmBands();", "for (int i = 0; i < bands.Length; i++)",
              "if (IsCached(bands[i][0], bands[i][1]))", "continue;", "GetBand(bands[i][0], bands[i][1]);", "yield return null;"]
    position = -1
    for token in tokens:
        found = prewarm.find(squash(token), position + 1)
        if found < 0:
            errors.append("物资池预热缺或顺序不对：" + token)
            break
        position = found
    if prewarm and loop_at >= 0:
        loop = squash(body_of(prewarm, squash("for (int i = 0; i < bands.Length; i++)")) or "")
        if "yield return null;" not in loop or loop.find("GetBand(") > loop.find("yield return null;"):
            errors.append("物资池预热必须每建完一个品质带就让出一帧（单帧建完全部带等于把卡顿挪到读条最后一帧）")
    for token in ("cache.Clear()", "weights.Clear()", "ResetStaticCaches("):
        if token in prewarm:
            errors.append("物资池预热不得复位缓存（缓存口径不变，只在模块销毁时复位）：" + token)
    resets = sum(len(re.findall(r"\bSkyIslandLootPools\s*\.\s*ResetStaticCaches\s*\(", text)) for text in src.values())
    destroy = squash(body_of(src[MODULE], "public override void OnDestroy()") or "")
    if resets != 1 or "SkyIslandLootPools.ResetStaticCaches();" not in destroy:
        errors.append("物资池缓存复位只许一个调用点，就在模块 OnDestroy（实际 %d 处）" % resets)

    # ---- 4. 预热带表 ----
    bands = squash(body_of(src[TABLES], "internal static int[][] PrewarmBands()") or "")
    for token in ("SkyIslandLootTier.Supply", "SkyIslandLootTier.Voyage", "SkyIslandLootTier.Starworks",
                  "AddBand(bands, MinQuality(tiers[i]), MaxQuality(tiers[i]));", "int guarantee = GuaranteeMinQuality(tiers[i]);",
                  "if (guarantee > 0) AddBand(bands, guarantee, MaxQuality(tiers[i]));"):
        if squash(token) not in bands:
            errors.append("预热带表必须覆盖每一档的常规带与保底带：缺 " + token)

    # ---- 5. F3：先读缓存再第一次 Get ----
    loot = squash(body_of(src[CASES], "private bool ValidateSkyIslandLootBands(out string metrics, out string reason)") or "")
    cached = loot.find(squash("SkyIslandLootPools.IsCached("))
    first_get = loot.find(squash("SkyIslandLootPools.Get("))
    if cached < 0 or first_get < 0 or cached > first_get:
        errors.append("SKY_LOOT_BANDS 必须在第一次 Get 之前读缓存状态：Get 会顺手把没建的带建起来，读晚了判据恒真")
    if "JudgeLootPrewarm(" not in loot:
        errors.append("SKY_LOOT_BANDS 的预热判据必须交给纯判据 JudgeLootPrewarm（隔离回归执行的那一份）")
    return errors


def main():
    sources = {}
    for rel in sorted(set(FIXED + scanned_paths())):
        path = ROOT / rel
        if not path.is_file():
            print("SkyIslandLootPrewarmGuard: FAIL - 找不到 " + rel)
            return 1
        sources[rel] = path.read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    errors = check(sources)

    update_anchor = "            if (hud != null) hud.Tick(Time.unscaledDeltaTime, HudSuppressed());\n"
    probes = [
        # 预热挪进每帧路径（装配里删掉、Update 里加上）
        (SESSION, "            IEnumerator warm = SkyIslandLootPools.Prewarm();\n            while (warm.MoveNext()) yield return warm.Current;\n", ""),
        (SESSION, update_anchor, update_anchor + "            IEnumerator lateWarm = SkyIslandLootPools.Prewarm(); lateWarm.MoveNext();\n"),
        # 预热排到 navigationReady 之后（读条画面已经收起）
        (SESSION, "            navigationReady = true;\n", "            navigationReady = true;\n            IEnumerator warmLate = SkyIslandLootPools.Prewarm();\n            while (warmLate.MoveNext()) yield return warmLate.Current;\n"),
        # 不让出帧
        (POOLS, "                LastPrewarm = stats;\n                yield return null;\n", "                LastPrewarm = stats;\n"),
        # 预热时顺手清缓存
        (POOLS, "            int[][] bands = SkyIslandLootTables.PrewarmBands();\n", "            int[][] bands = SkyIslandLootTables.PrewarmBands();\n            cache.Clear();\n"),
        # 漏掉保底带
        (TABLES, "                if (guarantee > 0) AddBand(bands, guarantee, MaxQuality(tiers[i]));\n", ""),
        # F3 先 Get 再读缓存
        (CASES, "            int[][] prewarmBands = SkyIslandLootTables.PrewarmBands();\n",
         "            SkyIslandLootPools.Get(SkyIslandLootTier.Supply);\n            int[][] prewarmBands = SkyIslandLootTables.PrewarmBands();\n"),
    ]
    first_update = sources[SESSION].find("private void Update()")
    for path, before, after in probes:
        text = sources[path]
        if before not in text:
            errors.append(u"反向检查锚点失效：" + path + " -> " + before.strip()[:70])
            continue
        altered = dict(sources)
        if path == SESSION and before == update_anchor:
            at = text.find(before, first_update)
            altered[path] = text[:at] + after + text[at + len(before):]
            # 同时把装配里的调用删掉，模拟「挪进每帧路径」
            altered[path] = altered[path].replace(probes[0][1], "", 1)
        else:
            altered[path] = text.replace(before, after, 1)
        if not check(altered):
            errors.append(u"未拦截错误写法：" + path + " -> " + before.strip()[:70])

    if errors:
        print("SkyIslandLootPrewarmGuard: FAIL\n  - " + "\n  - ".join(errors))
        return 1
    print("SkyIslandLootPrewarmGuard: PASS（装配路径上逐带分帧预热、全仓唯一调用点、不进每帧路径、缓存口径不变、"
          "F3 先读缓存再 Get；%d 个反向检查）" % len(probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
