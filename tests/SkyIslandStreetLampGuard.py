# -*- coding: utf-8 -*-
u"""天空岛路灯夜里照亮周围（2026-10-01，COMPAT）。

岛上的环境着色器是 UniversalMaterialType=Unlit：延迟管线的点光源照不到地面和墙面，灯罩也只是画出来的不透明玻璃，
原先塞在灯罩里的发光灯芯被挡住，夜里路灯既不亮也不照亮周围。现在：
- 生成器（sky_island_surface_materials）把 Tripo 路灯 / 铜灯柱的灯罩玻璃面分进带自发光的材质，并在玻璃中心、
  护栏灯与石灯的发光部位登记灯位，全部摆放结束后写成场景标记 NightLamp_<种类>_<序号>，清单进 geometry.json 的 nightLamps；
- 运行时 SkyIslandStreetLamps 在夜里把玩家附近的灯写进着色器全局数组（环境着色器自己加灯光），最近几盏另放真实点光源。

钉住：
1. 证据：Validation/sky_island_geometry.json 的 nightLamps 覆盖四种灯、名字是 NightLamp_<种类>_<三位序号> 且不重名；
   两种灯罩玻璃材质带自发光与暖色 emissionRgba；不再有被挡住的旧灯芯（lantern() 的 Tripo 分支不画 Glow 球）。
2. 生成器：灯位在全部摆放结束后才写成标记；摆放裁决平移 / 撤下时灯位同步（snapshot / rollback / fit 三处）。
3. C#：标记前缀与生成器一致；会话在光照之后 Apply、光照 Tick 之后 Tick、光照释放之前 Dispose；
   Tick 在白天（系数 ≤ 0）先早返，挑灯、写数组、建光源都在早返之后，真实光源只在 EnsureLights 里懒建；
   昼夜系数口径：星夜全亮、晴昼 / 晨光熄灭、暮色介于两者之间（按 SkyIslandLighting 四档日光色复算）。
反向验证：在内存里恢复错误写法（删 Tick 调用、Apply 里建光源、把早返挪到挑灯之后、证据删一种灯），每条必须转红。
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GEOMETRY = ROOT / "ArtSource/SkyIsland/Validation/sky_island_geometry.json"
GENERATOR = ROOT / "tools/generate_sky_island.py"
PLACEMENT = ROOT / "tools/sky_island_prop_placement.py"
SURFACES = ROOT / "tools/sky_island_surface_materials.py"
LAMPS = ROOT / "SkyIsland/SkyIslandStreetLamps.cs"
SESSION = ROOT / "SkyIsland/SkyIslandSession.cs"
SESSION_TICK = ROOT / "SkyIsland/SkyIslandSessionTick.cs"
LIGHTING = ROOT / "SkyIsland/SkyIslandLighting.cs"
KINDS = {"glass_street_lamp", "glass_brass_lamp_post", "rail", "stone"}
GLASS = ("Sky_TripoStreetLampGlass", "Sky_TripoBrassLampPostGlass")


def read(path):
    return path.read_text(encoding="utf-8-sig").replace("\r\n", "\n")


def body_of(source, signature):
    start = source.find(signature)
    if start < 0:
        return None
    brace = source.find("{", start + len(signature))
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:i]
    return None


def check_evidence(geometry, errors):
    lamps = geometry.get("nightLamps") or []
    kinds = {lamp.get("kind") for lamp in lamps}
    if not KINDS <= kinds:
        errors.append("EVIDENCE_LAMP_KIND_MISSING: nightLamps 缺少 %s" % sorted(KINDS - kinds))
    names = [lamp.get("name") for lamp in lamps]
    if len(set(names)) != len(names):
        errors.append("EVIDENCE_LAMP_NAME_DUPLICATED")
    for lamp in lamps:
        name = lamp.get("name") or ""
        if not re.fullmatch(r"NightLamp_%s_\d{3}" % re.escape(lamp.get("kind") or "?"), name):
            errors.append("EVIDENCE_LAMP_NAME: %s" % name)
            break
        position = lamp.get("position")
        if not (isinstance(position, list) and len(position) == 3 and all(isinstance(v, (int, float)) for v in position)):
            errors.append("EVIDENCE_LAMP_POSITION: %s" % name)
            break
    materials = geometry.get("materials") or {}
    for name in GLASS:
        row = materials.get(name) or {}
        if not row.get("emission", 0) > 0 or len(row.get("emissionRgba") or []) < 3:
            errors.append("GLASS_NOT_EMISSIVE: %s" % name)
    return len(lamps)


def check_generator(generator, placement, surfaces, errors):
    main = generator[generator.find("def main("):]
    marker_at = main.find("marker(lamp['name'],lamp['position'])")
    finish_at = main.find("sky_island_settlement.finish_gardens(")
    if marker_at < 0 or finish_at < 0 or marker_at < finish_at:
        errors.append("LAMP_MARKERS_BEFORE_PLACEMENT_DONE: 灯位标记必须在全部摆放结束后写")
    if "'nightLamps':night_lamps" not in generator:
        errors.append("LAMP_EVIDENCE_NOT_WRITTEN")
    lantern = generator[generator.find("def lantern("):generator.find("def pot(")]
    tripo_branch = lantern[:lantern.find("return")]
    if "'Glow'" in tripo_branch:
        errors.append("HIDDEN_WICK_BACK: Tripo 铜灯柱分支又画了被灯罩挡住的 Glow 灯芯")
    for token in ("lamp_light('rail'", "lamp_light('stone'", "lamp_light('lantern'"):
        if token not in generator:
            errors.append("LAMP_NOT_REGISTERED: %s" % token)
    if "g.lamp_light('glass_' +" not in surfaces:
        errors.append("GLASS_LAMP_NOT_REGISTERED")
    for token in ("state[LAMPS_KEY]=", "del lamps[before[LAMPS_KEY]:]", "translate_lamps(g,before,dx,dz)"):
        if token not in placement:
            errors.append("LAMP_NOT_TRACKED_BY_PLACEMENT: %s" % token)


def night_factor(source, r, g, b):
    body = body_of(source, "internal static float NightFactor(")
    low = re.search(r"luminance - ([0-9.]+)f\) / ([0-9.]+)f", body or "")
    if not low:
        return None
    t = (0.2126 * r + 0.7152 * g + 0.0722 * b - float(low.group(1))) / float(low.group(2))
    return 1.0 if t <= 0 else 0.0 if t >= 1 else 1.0 - t


def check_runtime(lamps, session, tick, lighting, errors):
    if 'MarkerPrefix = "NightLamp_"' not in lamps:
        errors.append("MARKER_PREFIX_DRIFT")
    if "internal const int MaxShaderLamps = 16;" not in lamps:
        errors.append("SHADER_LAMP_CAPACITY_DRIFT: 与着色器 SKY_ISLAND_MAX_LAMPS=16 不一致")
    apply_at = session.find("streetLamps.Apply(root);")
    if apply_at < 0 or apply_at < session.find("lighting.Apply(root);"):
        errors.append("APPLY_NOT_AFTER_LIGHTING")
    dispose_at = session.find("streetLamps.Dispose()")
    if dispose_at < 0 or dispose_at > session.find('Safe("lighting"'):
        errors.append("DISPOSE_NOT_BEFORE_LIGHTING")
    tick_at = tick.find("streetLamps.Tick(player.transform.position, lighting.AppliedSun)")
    if tick_at < 0 or tick_at < tick.find("lighting.Tick();") or tick_at > tick.find("SkyIslandFrameSegment.Lighting"):
        errors.append("TICK_NOT_WIRED: 路灯要在光照写完日光色之后、光照计时段结束之前 Tick")
    if "internal Color AppliedSun" not in lighting:
        errors.append("APPLIED_SUN_MISSING")
    body = body_of(lamps, "internal void Tick(Vector3 viewer, Color sun)") or ""
    early = body.find("if (factor <= 0f)")
    for heavy in ("SelectNearest(", "SetGlobalVectorArray(", "EnsureLights()"):
        at = body.find(heavy)
        if early < 0 or at < 0 or at < early:
            errors.append("DAYTIME_WORK_BEFORE_EARLY_RETURN: %s" % heavy)
    apply_body = body_of(lamps, "internal void Apply(GameObject root)") or ""
    if "EnsureLights" in apply_body or "AddComponent<Light>" in apply_body:
        errors.append("LIGHTS_CREATED_BEFORE_NIGHT")
    presets = {"星夜": (0.26, 0.37, 0.58), "暮色": (1.0, 0.56, 0.28), "晴昼": (1.0, 0.91, 0.72), "晨光": (1.0, 0.82, 0.62)}
    values = {name: night_factor(lamps, *rgb) for name, rgb in presets.items()}
    if values["星夜"] != 1.0 or values["晴昼"] != 0.0 or values["晨光"] != 0.0 or not (0.0 < (values["暮色"] or 0) < 1.0):
        errors.append("NIGHT_FACTOR_SEMANTICS: %r" % values)


def run(geometry, generator, placement, surfaces, lamps, session, tick, lighting):
    errors = []
    count = check_evidence(geometry, errors)
    check_generator(generator, placement, surfaces, errors)
    check_runtime(lamps, session, tick, lighting, errors)
    return errors, count


def main():
    inputs = [json.loads(GEOMETRY.read_text(encoding="utf-8")), read(GENERATOR), read(PLACEMENT), read(SURFACES),
              read(LAMPS), read(SESSION), read(SESSION_TICK), read(LIGHTING)]
    errors, count = run(*inputs)
    # 反向：每一种错误写法都必须被抓到。
    probes = 0
    for label, index, mutate, expected in (
        ("tick removed", 6, lambda s: s.replace("streetLamps.Tick(player.transform.position, lighting.AppliedSun)", "0"), "TICK_NOT_WIRED"),
        ("lights in Apply", 4, lambda s: s.replace("lightRoot.transform.SetParent(root.transform, false);",
                                                   "lightRoot.transform.SetParent(root.transform, false); EnsureLights();"),
         "LIGHTS_CREATED_BEFORE_NIGHT"),
        ("early return late", 4, lambda s: s.replace("int count = SelectNearest(viewer);", "")
         .replace("float now = Time.time;", "int count = SelectNearest(viewer); float now = Time.time;", 1)
         .replace("if (factor <= 0f)", "int unused = 0; if (factor <= 0f)", 1)
         .replace("float factor = SkyIslandStreetLampRules.NightFactor(sun.r, sun.g, sun.b);",
                  "int early = SelectNearest(viewer); float factor = SkyIslandStreetLampRules.NightFactor(sun.r, sun.g, sun.b);", 1),
         "DAYTIME_WORK_BEFORE_EARLY_RETURN"),
        ("stone lamps dropped", 0, lambda g: dict(g, nightLamps=[l for l in g.get("nightLamps", []) if l.get("kind") != "stone"]),
         "EVIDENCE_LAMP_KIND_MISSING"),
    ):
        mutated = list(inputs)
        mutated[index] = mutate(mutated[index])
        found, _ = run(*mutated)
        if not any(expected in e for e in found):
            errors.append("PROBE_NOT_REJECTED: %s" % label)
        probes += 1
    if errors:
        print("SkyIslandStreetLampGuard: FAIL\n  - " + "\n  - ".join(errors))
        sys.exit(1)
    print("SkyIslandStreetLampGuard: PASS (%d night lamps, %d in-memory probes rejected)" % (count, probes))


if __name__ == "__main__":
    main()
