# -*- coding: utf-8 -*-
u"""天空岛帧时间分项计时（2026-09-14 B 轮）：只在 Dev 构建里干活，正式构建零开销。

钉住：
1. `SkyIslandFrameProfile.Start / Mark / BeginRecording` 带 `[Conditional("BOSSRUSH_DEV")]`（正式构建里调用点整条不存在）；
   读时钟、读帧号、写录制的代码只出现在 `#if BOSSRUSH_DEV` 区块里；`TryTakeRecording` 在 `#else` 分支恒返回 false。
2. 段枚举与段名表一一对应。
3. 标记接在岛上每帧运行的各部分：会话 Update（HUD、剧情 owner 其余部分、遭遇、搜刮、居民、落盘门、门与标记、光照、氛围音效、
   地面射线、撤离与 HUD 刷新）、剧情 owner（对话与面板、信鸽）、局内 owner（云蚋、采集、灯与耗材、夜风与云蚋刷新）；
   每个段至少用一次；`Start()` 全仓唯一，就在会话 Update 开头。
4. 录制只由 F3 开窗关窗：`BeginRecording` / `TryTakeRecording` 只出现在 F3 运行时用例文件；`SamplePerformance` 只在岛内模式下
   开窗（采样循环之前）与关窗（循环之后），分项计时不合格时这条用例不记 PASS。
5. 模块销毁时丢掉没交出去的录制；编译清单登记；判据在纯判据区。

文本守卫证明不了「正式构建里真的没有调用」——那由 C# 编译器的 Conditional 语义保证；这里钉的是「用对了 Conditional、
没有在非 Dev 代码里读时钟」。反向检查在内存里恢复错误写法。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

PROFILE = "DebugAndTools/SkyIsland/SkyIslandFrameProfile.cs"
SESSION = "DebugAndTools/SkyIsland/SkyIslandSession.cs"
WORLD = "DebugAndTools/SkyIsland/SkyIslandWorldStory.cs"
FIELDCRAFT = "DebugAndTools/SkyIsland/SkyIslandFieldcraft.cs"
MODULE = "DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs"
RUNNER = "DebugAndTools/F3GameplayValidationRunner.cs"
RUNTIME = "DebugAndTools/F3GameplayValidationSkyIslandRuntimeCases.cs"
BAT = "compile_official.bat"
FIXED = [PROFILE, SESSION, WORLD, FIELDCRAFT, MODULE, RUNNER, RUNTIME, BAT]

REQUIRED_MARKS = {
    SESSION: ("Hud", "StoryRest", "Encounters", "Scavenging", "Residents", "StorySave", "GatesAndMarkers", "Lighting",
              "Ambience", "GroundProbe", "HudRefresh"),
    WORLD: ("StoryUi", "Pigeon"),
    FIELDCRAFT: ("Gnats", "Gathering", "FiresAndBuffs", "WindAndSwarm"),
}
ENTRY = {SESSION: "private void Update()", WORLD: "internal void Tick()", FIELDCRAFT: "internal void Tick()"}
DEV_ONLY_TOKENS = ("Stopwatch.GetTimestamp", "frameCount", "recorded.Add", "current[", "recording = true")


def squash(text):
    text = re.sub(r"\s+", " ", text or "")
    return re.sub(r"\s*([(){};:,.\[\]<>=!?|&+*/-])\s*", r"\1", text)


def body_of(source, signature):
    start = source.find(signature)
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


def non_dev_lines(raw):
    """去掉 `#if BOSSRUSH_DEV` 区块（保留 `#else` 分支与区块外的代码）后剩下的代码行；注释行不算。"""
    kept, stack = [], []
    for line in raw.splitlines():
        stripped = line.strip()
        if stripped.startswith("#if"):
            stack.append("dev" if "BOSSRUSH_DEV" in stripped else "other")
            continue
        if stripped.startswith("#else"):
            if stack:
                stack[-1] = "release" if stack[-1] == "dev" else stack[-1]
            continue
        if stripped.startswith("#endif"):
            if stack:
                stack.pop()
            continue
        if "dev" in stack or stripped.startswith("//") or stripped.startswith("///"):
            continue
        kept.append(line)
    return "\n".join(kept)


def production_paths():
    paths = sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / "DebugAndTools/SkyIsland").glob("*.cs"))
    paths += sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / "Integration/SkyIsland").glob("*.cs"))
    paths += sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / "DebugAndTools").glob("F3GameplayValidation*.cs"))
    return paths


def check(sources):
    errors = []
    raw_profile = sources[PROFILE]
    profile = clean_source(raw_profile)

    # ---- 1. Conditional 与 Dev 区块 ----
    for method in ("Start", "Mark", "BeginRecording"):
        if not re.search(r'\[Conditional\("BOSSRUSH_DEV"\)\]\s*internal static void ' + method + r"\s*\(", raw_profile):
            errors.append("SkyIslandFrameProfile." + method + " 必须带 [Conditional(\"BOSSRUSH_DEV\")]：否则正式构建每帧都在调它")
    outside = non_dev_lines(raw_profile)
    for token in DEV_ONLY_TOKENS:
        if token in outside:
            errors.append("读时钟 / 帧号或写录制的代码只许写在 #if BOSSRUSH_DEV 里：区块外出现了 " + token)
    take = raw_profile.split("internal static bool TryTakeRecording(", 1)[-1].split("internal static void ResetStaticCaches", 1)[0]
    if not re.search(r"#else\s*frames = null;\s*return false;\s*#endif", take):
        errors.append("TryTakeRecording 在正式构建（#else 分支）必须恒返回 false、交出 null")

    # ---- 2. 段枚举与段名表 ----
    enum_block = profile.split("internal enum SkyIslandFrameSegment", 1)[-1].split("}", 1)[0]
    segments = re.findall(r"\b([A-Z]\w*)\s*=\s*(\d+)", enum_block)
    names = re.findall(r'"([a-z_]+)"', profile.split("internal static readonly string[] SegmentNames", 1)[-1].split("};", 1)[0])
    if not segments or len(segments) != len(names) or [int(v) for _, v in segments] != list(range(len(segments))):
        errors.append("段枚举必须从 0 连续编号，并与 SegmentNames 一一对应（枚举 %d 项、段名 %d 个）" % (len(segments), len(names)))
    enum_names = {name for name, _ in segments}

    # ---- 3. 标记接线 ----
    used = set()
    starts = 0
    for path in production_paths():
        if path == PROFILE or path not in sources:
            continue
        text = clean_source(sources[path])
        starts += len(re.findall(r"\bSkyIslandFrameProfile\s*\.\s*Start\s*\(\s*\)", text))
        for segment in re.findall(r"\bSkyIslandFrameProfile\s*\.\s*Mark\s*\(\s*SkyIslandFrameSegment\s*\.\s*(\w+)\s*\)", text):
            if segment not in enum_names:
                errors.append(path + " 标记了不存在的段：" + segment)
            used.add(segment)
    for path, marks in REQUIRED_MARKS.items():
        entry = squash(body_of(clean_source(sources[path]), ENTRY[path]) or "")
        for mark in marks:
            if squash("SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.%s);" % mark) not in entry:
                errors.append(path + " 的每帧入口 " + ENTRY[path] + " 缺分段标记 " + mark)
    unused = sorted(enum_names - used)
    if unused:
        errors.append("这些段没有任何标记（报告里恒为 0，读起来像「这一段不花时间」）：" + ",".join(unused))
    update = squash(body_of(clean_source(sources[SESSION]), "private void Update()") or "")
    if starts != 1 or not update.strip().startswith(squash("if (closed) return; SkyIslandFrameProfile.Start();")):
        errors.append("SkyIslandFrameProfile.Start() 全仓只许一处，就在会话 Update 开头（实际 %d 处）" % starts)

    # ---- 4. 录制只由 F3 开窗关窗 ----
    for path in production_paths():
        if path in (PROFILE, RUNTIME) or path not in sources:
            continue
        text = clean_source(sources[path])
        for token in ("BeginRecording(", "TryTakeRecording("):
            if token in text:
                errors.append(path + " 不许开关分项计时的录制（只由 F3 运行时用例开窗关窗）：" + token)
    sample = squash(body_of(clean_source(sources[RUNNER]), "private IEnumerator SamplePerformance(string caseId, float seconds, bool baseline)") or "")
    order = [sample.find(squash(t)) for t in ("BeginSkyIslandFrameProfile();",
                                              "while (Time.realtimeSinceStartup < until && !ShouldAbort())",
                                              "string profileReason = AppendSkyIslandFrameProfile(ref metrics);",
                                              "if (profileReason == null && (baseline || p95 <= Mathf.Max(50f, _baselineP95Ms * 1.75f)))")]
    if min(order) < 0 or order != sorted(order):
        errors.append("SamplePerformance 必须在采样循环之前开窗、之后关窗，分项计时不合格时不记 PASS")
    runtime = clean_source(sources[RUNTIME])
    # 岛内模式门写在 SkyIsland partial 里（宿主 partial 有行数预算，只留两行调用）：主套件若也开了录制，没人打标记，PERF 用例会记成 profile_no_frames。
    begin = squash(body_of(runtime, "private void BeginSkyIslandFrameProfile()") or "").strip()
    append = squash(body_of(runtime, "private string AppendSkyIslandFrameProfile(ref string metrics)") or "").strip()
    if not begin.startswith(squash("if (!_skyIslandMode) return;")) or "SkyIslandFrameProfile.BeginRecording();" not in begin:
        errors.append("分项计时只在岛内套件开窗：BeginSkyIslandFrameProfile 必须先判 _skyIslandMode，再 BeginRecording")
    if not append.startswith(squash("if (!_skyIslandMode) return null;")) or squash("metrics += SkyIslandFrameProfileMetrics(out reason);") not in append:
        errors.append("分项计时只在岛内套件关窗：AppendSkyIslandFrameProfile 必须先判 _skyIslandMode，再把分项 metrics 追加进去")
    pure = runtime.split("#region 纯判据", 1)[-1].split("#endregion", 1)[0]
    if "internal static bool JudgeFrameProfile(" not in pure:
        errors.append("分项计时的判据必须写在纯判据区（隔离回归逐字抽出执行）")
    metrics = squash(body_of(runtime, "private string SkyIslandFrameProfileMetrics(out string reason)") or "")
    for token in ("SkyIslandFrameProfile.TryTakeRecording(out frames);", "light.shadows != LightShadows.None", "renderer.isVisible",
                  "JudgeFrameProfile(SkyIslandFrameProfile.SegmentNames,"):
        if squash(token) not in metrics:
            errors.append("关窗取数缺 " + token + "（活动灯数、开阴影的灯数与可见 renderer 数都要进 metrics）")

    # ---- 5. 生命周期与登记 ----
    destroy = squash(body_of(clean_source(sources[MODULE]), "public override void OnDestroy()") or "")
    if "SkyIslandFrameProfile.ResetStaticCaches();" not in destroy:
        errors.append("模块销毁时要丢掉没交出去的分项计时录制")
    if "echo(DebugAndTools\\SkyIsland\\SkyIslandFrameProfile.cs" not in sources[BAT]:
        errors.append("编译清单缺 SkyIslandFrameProfile.cs")
    return errors


def main():
    sources = {}
    for rel in sorted(set(FIXED + production_paths())):
        path = ROOT / rel
        if not path.is_file():
            print("SkyIslandFrameProfileGuard: FAIL - 找不到 " + rel)
            return 1
        sources[rel] = path.read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    errors = check(sources)

    probes = [
        (PROFILE, '        [Conditional("BOSSRUSH_DEV")]\n        internal static void Mark(', "        internal static void Mark("),
        (PROFILE, '"ground_probe", "hud_refresh"', '"ground_probe"'),
        (PROFILE, "#else\n            frames = null;\n            return false;\n#endif", "#endif"),
        (SESSION, "            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.Encounters);\n", ""),
        (FIELDCRAFT, "            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.Gnats);\n", ""),
        (WORLD, "            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.Pigeon);\n", "            SkyIslandFrameProfile.Mark(SkyIslandFrameSegment.Hud);\n"),
        (SESSION, "            SkyIslandFrameProfile.Start();\n", ""),
        (RUNNER, "            BeginSkyIslandFrameProfile();\n", ""),
        (RUNTIME, "        private void BeginSkyIslandFrameProfile()\n        {\n            if (!_skyIslandMode) return;\n",
         "        private void BeginSkyIslandFrameProfile()\n        {\n"),
        (RUNTIME, "        private string AppendSkyIslandFrameProfile(ref string metrics)\n        {\n            if (!_skyIslandMode) return null;\n",
         "        private string AppendSkyIslandFrameProfile(ref string metrics)\n        {\n"),
        (RUNNER, "if (profileReason == null && (baseline || p95 <= Mathf.Max(50f, _baselineP95Ms * 1.75f)))",
         "if (baseline || p95 <= Mathf.Max(50f, _baselineP95Ms * 1.75f))"),
        (MODULE, "            SkyIslandFrameProfile.ResetStaticCaches();\n", ""),
    ]
    for path, before, after in probes:
        text = sources[path]
        if before not in text:
            errors.append(u"反向检查锚点失效：" + path + " -> " + before.strip()[:70])
            continue
        altered = dict(sources)
        altered[path] = text.replace(before, after, 1)
        if not check(altered):
            errors.append(u"未拦截错误写法：" + path + " -> " + before.strip()[:70])
    # 在非 Dev 代码里读时钟：把一次时钟读取挪到 #if 区块外
    leak = sources[PROFILE].replace("        internal static void ResetStaticCaches()\n        {\n",
                                    "        internal static void ResetStaticCaches()\n        {\n            long leaked = System.Diagnostics.Stopwatch.GetTimestamp();\n", 1)
    if leak == sources[PROFILE] or not check(dict(sources, **{PROFILE: leak})):
        errors.append(u"未拦截错误写法：正式构建路径里读时钟")

    if errors:
        print("SkyIslandFrameProfileGuard: FAIL\n  - " + "\n  - ".join(errors))
        return 1
    print("SkyIslandFrameProfileGuard: PASS（Conditional + Dev 区块 / 段表一一对应 / 17 段标记接线 / 录制只由 F3 开关 / "
          "模块销毁复位；%d 个反向检查）" % (len(probes) + 1))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
