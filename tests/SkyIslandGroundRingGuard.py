# -*- coding: utf-8 -*-
"""天空岛贴地圈的抬高与截图可见度探针（2026-09-15）。

第四轮全自动验收截图：悬根林广场与归航钟庭的撤离绿环只剩几段碎弧，白天几乎看不见（首轮 09-14 的同一张就是虚线），
自动可见度断言却判了 PASS。
- 原因（生成器 × 运行时）：广场是 tools/generate_sky_island.py 的 paved_disc 铺的——调用高度比岛面高 0.03–0.05 m，
  台面顶在调用高度 +0.02 m，12 道放射缝顶到约 +0.058 m；装饰铺路条在岛面 +0.028 + 序号×0.011 m。这些都没有碰撞体，
  而撤离环按地面碰撞体（岛面）+0.06 m 放，正好与台面共面，放射缝又从 2.5 m 半径上横穿过去。
- 探针：旧口径在环的投影框里取离邻域最远的 15% 像素比亮度，框里有一片日照石面就过。

守的是：
1. SkyIslandGroundRing.GroundLift 高过生成器里最高的地面装饰（圆盘台面 / 放射缝 / 铺路条）至少 3 cm；
   撤离环、噬风预警圈、头目圈都引用它，天空岛里建贴地圈的地方不再各写一个字面量高度。
   圆盘外沿的倒圆（torus）在圆盘半径 -0.35 m 处，离环的 2.5 m 半径很远，不计入。
2. 截图探针对 ground_ring / echo_ring 走沿环带逐段取样的 JudgeRingCoverage，不退回投影框亮度口径；
   找不到 LineRenderer 时样本为空（判据给 SKIP），不偷偷用旧口径。
步骤表里圆环阈值的下限（覆盖率 ≥ 0.5）由 SkyIslandAutotestTableGuard 管。反向检查在内存里逐条破坏，必须转红。
"""
import ast
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source  # noqa: E402

GENERATOR = "tools/generate_sky_island.py"
RING = "DebugAndTools/SkyIsland/SkyIslandGroundRing.cs"
STORM = "DebugAndTools/SkyIsland/SkyIslandStormBoss.cs"
FORGE = "DebugAndTools/SkyIsland/SkyIslandBossForge.cs"
CAPTURE = "DebugAndTools/F3GameplayValidationAutotestCapture.cs"
SKY_DIR = "DebugAndTools/SkyIsland"
CLEARANCE = 0.03


def method_body(code, signature):
    start = code.find(signature)
    if start < 0:
        return None
    brace = code.find("{", start)
    if brace < 0:
        return None
    depth = 0
    for i in range(brace, len(code)):
        if code[i] == "{":
            depth += 1
        elif code[i] == "}":
            depth -= 1
            if depth == 0:
                return code[brace:i + 1]
    return None


def decoration_top(generator):
    """生成器里离岛面最高的地面装饰顶高（米）与明细；解析不出来返回 (None, 错误)。"""
    body = re.search(r"def paved_disc\(x,y,z,r,mat='Limestone'\):\n(.*?)\n\n", generator.replace("\r\n", "\n"), re.S)
    if not body:
        return None, "生成器里找不到 paved_disc"
    text = body.group(1)
    slab = re.search(r"cylinder\(\(x,y([-+][\d.]+),z\),r,([\d.]+),", text)
    seam = re.search(r"beam\(\(x\+2\*math\.cos\(a\),y([-+][\d.]+),z\+2\*math\.sin\(a\)\),\(.*?\),([\d.]+),'Chalk'", text)
    if not slab or not seam:
        return None, "paved_disc 的台面或放射缝形状变了，本守卫的解析要跟着改"
    slab_top = float(slab.group(1)) + float(slab.group(2)) / 2
    seam_top = float(seam.group(1)) + float(seam.group(2)) / 2
    calls = [float(v) for v in re.findall(r"paved_disc\(x,y\+([\d.]+),z", generator)]
    if not calls:
        return None, "生成器里找不到 paved_disc(x,y+…,z,…) 的调用"
    disc_top = max(calls) + max(slab_top, seam_top)

    strip = re.search(r"y=islands\[sid\]\['height'\]\+([\d.]+)\+index\*([\d.]+)", generator)
    start = generator.find("routes={")
    if not strip or start < 0:
        return None, "生成器里找不到铺路条的高度公式或 routes 表"
    brace = generator.find("{", start)
    depth = 0
    end = -1
    for i in range(brace, len(generator)):
        if generator[i] == "{":
            depth += 1
        elif generator[i] == "}":
            depth -= 1
            if depth == 0:
                end = i
                break
    try:
        routes = ast.literal_eval(generator[brace:end + 1])
    except (ValueError, SyntaxError) as error:
        return None, "routes 表解析失败：%s" % error
    max_index = max(len(paths) for paths in routes.values()) - 1
    strip_top = float(strip.group(1)) + float(strip.group(2)) * max_index
    detail = "disc_top=%.4f(calls_max=%.3f,slab=%.3f,seam=%.4f),strip_top=%.4f(max_index=%d)" % (
        disc_top, max(calls), slab_top, seam_top, strip_top, max_index)
    return max(disc_top, strip_top), detail


def check(sources):
    errors = []
    top, detail = decoration_top(sources[GENERATOR])
    if top is None:
        errors.append(detail)
        top = 0.0

    ring = sources[RING]
    lift = re.search(r"internal const float GroundLift = ([\d.]+)f;", ring)
    if not lift:
        errors.append("SkyIslandGroundRing 缺少 GroundLift 常量")
    elif float(lift.group(1)) + 1e-9 < top + CLEARANCE:
        errors.append("贴地圈抬高 %.3f m 不够：地面装饰最高 %.4f m，至少要再高 %.2f m（%s）" % (float(lift.group(1)), top, CLEARANCE, detail))
    if "internal const float GroundOffset = SkyIslandGroundRing.GroundLift;" not in ring:
        errors.append("撤离环的 GroundOffset 必须引用 SkyIslandGroundRing.GroundLift，不再单独写高度")

    warning = method_body(sources[STORM], "private LineRenderer CreateWarningRing()")
    if warning is None or warning.count("Vector3.up * SkyIslandGroundRing.GroundLift") != 2:
        errors.append("噬风首战与回响的预警圈都要按 SkyIslandGroundRing.GroundLift 抬高")
    local_of = method_body(sources[FORGE], "private static Vector3 LocalOf(")
    if local_of is None or "Vector3.up * SkyIslandGroundRing.GroundLift" not in local_of:
        errors.append("头目贴地圈（SkyIslandBossForge.LocalOf）要按 SkyIslandGroundRing.GroundLift 抬高")
    for rel, code in sources["sky"].items():
        for line in code.splitlines():
            if "SkyIslandGroundRing.Create(" in line and re.search(r"Vector3\.up \* [\d.]+f", line):
                errors.append("%s 建贴地圈写了字面量高度：%s" % (rel, line.strip()))

    capture = sources[CAPTURE]
    target = method_body(capture, "private static bool AutotestRingTarget(")
    if target is None or '"ground_ring"' not in target or '"echo_ring"' not in target:
        errors.append("撤离环与噬风预警圈要按圆环探针取样（AutotestRingTarget）")
    collect = method_body(capture, "private List<AutotestWorldProbe> CollectAutotestWorldProbes(")
    if collect is None or "if (AutotestRingTarget(target)) CollectAutotestRingSamples(camera, bestTransform, ref best);" not in collect:
        errors.append("取数时圆环目标要沿 LineRenderer 取环带采样点")
    samples = method_body(capture, "private static void CollectAutotestRingSamples(")
    if samples is None:
        errors.append("缺少 CollectAutotestRingSamples")
    else:
        marked = samples.find("probe.Ring = true;")
        bail = samples.find("if (line == null || line.positionCount < 8) return;")
        if not (0 <= marked < bail):
            errors.append("找不到 LineRenderer 时也要记成圆环探针（样本为空、判据 SKIP），不能退回投影框亮度口径")
    analyze = method_body(capture, "private static void AnalyzeAutotestWorld(")
    if analyze is None:
        errors.append("缺少 AnalyzeAutotestWorld")
    else:
        branch = analyze.find("if (probe.Ring)")
        legacy = analyze.find("F3AutotestJudges.JudgeWorldVisibility(")
        if not (0 <= branch < legacy) or "AnalyzeAutotestRing(pixels, analysis, probe);" not in analyze:
            errors.append("圆环探针要先于投影框亮度口径分流到 AnalyzeAutotestRing")
    ring_judge = method_body(capture, "private static void AnalyzeAutotestRing(")
    if ring_judge is None or "F3AutotestJudges.JudgeRingCoverage(" not in ring_judge:
        errors.append("圆环探针要用 F3AutotestJudges.JudgeRingCoverage 判覆盖率")
    return errors


def load_sources():
    sources = {rel: clean_source((ROOT / rel).read_text(encoding="utf-8")) for rel in (RING, STORM, FORGE, CAPTURE)}
    sources[GENERATOR] = (ROOT / GENERATOR).read_text(encoding="utf-8")
    sources["sky"] = {str(path.relative_to(ROOT)).replace("\\", "/"): clean_source(path.read_text(encoding="utf-8"))
                      for path in sorted((ROOT / SKY_DIR).glob("*.cs"))}
    return sources


def reverse_checks(sources):
    cases = (
        ("抬高退回 0.06", RING, "internal const float GroundLift = 0.16f;", "internal const float GroundLift = 0.06f;"),
        ("撤离环另写高度", RING, "internal const float GroundOffset = SkyIslandGroundRing.GroundLift;", "internal const float GroundOffset = 0.06f;"),
        ("放射缝抬高到 +0.15", GENERATOR, "beam((x+2*math.cos(a),y+.03,z+2*math.sin(a))", "beam((x+2*math.cos(a),y+.15,z+2*math.sin(a))"),
        ("预警圈退回字面量", STORM, "map.InverseTransformPoint(eyeOrigin) + Vector3.up * SkyIslandGroundRing.GroundLift",
         "map.InverseTransformPoint(eyeOrigin) + Vector3.up * 0.08f"),
        ("头目圈退回字面量", FORGE, "return local + Vector3.up * SkyIslandGroundRing.GroundLift;", "return local + Vector3.up * 0.08f;"),
        ("圆环不分流", CAPTURE, "if (probe.Ring)", "if (false && probe.Ring)"),
        ("找不到 LineRenderer 时不记圆环", CAPTURE, "probe.Ring = true;", ""),
        ("圆环判据换回亮度口径", CAPTURE, "F3AutotestJudges.JudgeRingCoverage(", "F3AutotestJudges.JudgeRingCoverageLegacy("),
    )
    failures = []
    for name, rel, old, new in cases:
        if old not in sources[rel]:
            failures.append("反向检查「" + name + "」找不到破坏点: " + old)
            continue
        mutated = dict(sources)
        mutated[rel] = sources[rel].replace(old, new, 1)
        if rel == STORM or rel == FORGE:
            sky = dict(sources["sky"])
            sky[rel] = mutated[rel]
            mutated["sky"] = sky
        if not check(mutated):
            failures.append("反向检查「" + name + "」没有转红")
    return failures


def main():
    sources = load_sources()
    errors = check(sources) or reverse_checks(sources)
    if errors:
        for error in errors:
            print("FAIL: " + error)
        return 1
    top, detail = decoration_top(sources[GENERATOR])
    print("PASS: 贴地圈抬高高过地面装饰（%s）；撤离环 / 噬风预警圈 / 头目圈共用抬高；圆环探针按环带覆盖率取样（反向检查 8/8 转红）" % detail)
    return 0


if __name__ == "__main__":
    sys.exit(main())
