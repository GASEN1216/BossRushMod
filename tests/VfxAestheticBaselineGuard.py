"""共享特效层与装备 / 遗种巢特效的审美底线（2026-09-23 特效审美审查 VA-01…VA-14、VB-27）。

钉住的是「修过一次、最容易被无意改回去」的结构，不是观感本身（观感只能 L3 实机看）：

1. RingParticleEffect（霜雾 / 飞行云雾的基类）：
   - 不再每帧手撒：没有 LateUpdate、没有 .Emit(；发射只走 rateOverTime；
   - 共享材质 Legacy Particles/Alpha Blended 的 _TintColor 写中性 0.5（SetVector 原始值），
     不能再写白色（颜色与 alpha 翻倍，灰烟变发光白团）；
   - 退场等粒子走完：StopEffect 的协程里是 StopEmitting，不是一刀 Destroy。
2. 霜雾：只用 World 空间发射器，整件活粒子（薄霜 + 冰晶）不超过 24 颗。
3. 套装爆发：SpawnSetBurst 默认不开灯、转调 NewWeaponFx.PlayBurst；霜噬 / 雷噬两处高频调用
   不传开灯参数（每 1–1.4 秒一盏动态光会让地面一闪一闪）。
4. 新武器爆发：碎片走一次性粒子（BossRushFxKit.PlayBurst），不再是平移的实心椭圆精灵
   （NewWeaponBurstShardFx 不得回来）；灯在前半程衰减完。
5. 霜之哀伤挥砍：自动发射关闭、按弧长逐点 Emit、不用 startSizeMultiplier、挥击后留尾巴再回收。
6. 遗种巢崽特效：召回时走 BossRushFxKit.Release 自然退场，搭建异常时手动回收资源。

反向验证：main() 末尾对每条断言做一次「按字节替换锚点 → 必须转红」的自检。
"""
from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
RING = "Common/Effects/RingParticleEffect.cs"
MIST = "Integration/Bonus/FrostMistEffect.cs"
VISUALS = "Integration/Bonus/SetBonusVisuals.cs"
NOVA = "Integration/Bonus/FrostSetBonus_Nova.cs"
STORM = "Integration/Bonus/ThunderSetBonus_Storm.cs"
WEAPON_FX = "Integration/NewWeapons/Common/NewWeaponFx.cs"
FROSTMOURNE = "Integration/Frostmourne/FrostmourneSwingFx.cs"
PET = "PetNest/PetNestAuraEffect.cs"
PATHS = (RING, MIST, VISUALS, NOVA, STORM, WEAPON_FX, FROSTMOURNE, PET)

MIST_BUDGET = 24


def norm(text):
    return re.sub(r"\s+", " ", text)


def body(code, signature):
    start = code.find(signature)
    if start < 0:
        return None
    opening = code.find("{", start)
    depth = 0
    for index in range(opening, len(code)):
        depth += (code[index] == "{") - (code[index] == "}")
        if depth == 0:
            return norm(code[opening:index + 1])
    return None


def number(code, pattern):
    m = re.search(pattern, code)
    return float(m.group(1)) if m else None


def check(sources):
    errors = []
    src = {path: clean_source(text) for path, text in sources.items()}

    # ---- 1. RingParticleEffect ----
    ring = src[RING]
    if re.search(r"\bvoid\s+LateUpdate\s*\(", ring):
        errors.append("RingParticleEffect 不得再有 LateUpdate（旧版每帧手撒，密度随帧率走）")
    if ".Emit(" in ring:
        errors.append("RingParticleEffect 不得手动 Emit：发射只走 rateOverTime")
    shared = body(ring, "internal static Material GetSharedParticleMaterial()")
    if shared is None:
        errors.append("缺少 RingParticleEffect.GetSharedParticleMaterial")
    else:
        if 'mat.SetVector("_TintColor", new Vector4(0.5f, 0.5f, 0.5f, 0.5f));' not in shared:
            errors.append("共享粒子材质的 _TintColor 必须用 SetVector 写中性 0.5（Legacy 着色器 2× 顶点色 × _TintColor）")
        if re.search(r'SetColor\("_TintColor"', shared):
            errors.append("共享粒子材质不得用 SetColor 写 _TintColor（白色翻倍；线性空间下 0.5 还会被换算成 0.214）")
    stop = body(ring, "private IEnumerator DoStop()")
    if stop is None or "ParticleSystemStopBehavior.StopEmitting)" not in stop or "AnyAlive(systems)" not in stop:
        errors.append("RingParticleEffect.StopEffect 必须停发射并等粒子走完再销毁")

    # ---- 2. 霜雾 ----
    mist = src[MIST]
    if "protected override bool EnableLocalEmitters => false;" not in mist:
        errors.append("霜雾不得再用 Local 空间发射器（一块跟着人走的硬饼）")
    emitters = number(mist, r"EmitterCount\s*=>\s*(\d+)\s*;")
    per = number(mist, r"WorldMaxParticles\s*=>\s*(\d+)\s*;")
    glints = number(mist, r"GlintMaxParticles\s*=\s*(\d+)\s*;")
    if None in (emitters, per, glints):
        errors.append("霜雾预算解析失败（EmitterCount / WorldMaxParticles / GlintMaxParticles）")
    elif emitters * per + glints > MIST_BUDGET:
        errors.append("霜雾活粒子上限 %d 超过 %d" % (emitters * per + glints, MIST_BUDGET))

    # ---- 3. 套装爆发 ----
    burst = body(src[VISUALS], "private void SpawnSetBurst(")
    if burst is None or "NewWeaponFx.PlayBurst(position, color, radius, life, shardCount, withLight);" not in burst:
        errors.append("SpawnSetBurst 必须转调 NewWeaponFx.PlayBurst（与新武器同一份实现）")
    if "bool withLight = false)" not in src[VISUALS]:
        errors.append("SpawnSetBurst 默认必须不开灯")
    for path, label in ((NOVA, "霜噬"), (STORM, "雷噬")):
        calls = re.findall(r"SpawnSetBurst\(([^;]*)\);", src[path])
        if not calls:
            errors.append(label + " 找不到 SpawnSetBurst 调用")
        for call in calls:
            if len([a for a in call.split(",") if a.strip()]) != 5:
                errors.append(label + " 是高频触发，SpawnSetBurst 不得传开灯参数")

    # ---- 4. 新武器爆发 ----
    fx = src[WEAPON_FX]
    if "class NewWeaponBurstShardFx" in fx:
        errors.append("爆发碎片不得回到平移的实心椭圆精灵（NewWeaponBurstShardFx）")
    play = body(fx, "internal static void Play(")
    if play is None or "BossRushFxKit.PlayBurst(" not in play:
        errors.append("爆发碎片必须走 BossRushFxKit.PlayBurst 的一次性粒子")
    update = body(fx[fx.find("public class NewWeaponBurstFx"):], "private void Update()")
    if update is None or "Mathf.Clamp01(t * 2f)" not in update:
        errors.append("爆发环的灯必须在前半程衰减完")

    # ---- 5. 霜之哀伤挥砍 ----
    fm = src[FROSTMOURNE]
    if "startSizeMultiplier" in fm:
        errors.append("霜之哀伤挥砍不得用 startSizeMultiplier（常量模式下是把尺寸直接设成 1.8 m）")
    tint = body(fm, "private static void TintParticlesIce(")
    if tint is None or "emission.enabled = false;" not in tint \
            or "emission.rateOverDistance = new ParticleSystem.MinMaxCurve(0f);" not in tint:
        errors.append("霜之哀伤挥砍必须关掉自动发射（rateOverDistance 会把粒子堆在起手处）")
    arc = body(fm, "private void EmitAlongArc(")
    if arc is None or "Mathf.Lerp(lastEmittedAngle, currentAngle," not in arc or "ps.Emit(emitParams, 1);" not in arc:
        errors.append("霜之哀伤挥砍必须按弧长逐点 Emit")
    fm_update = body(fm, "private void Update()")
    if fm_update is None or "if (elapsed >= Duration + ParticleTailDuration)" not in fm_update:
        errors.append("霜之哀伤挥砍必须留尾巴再回收，不能在挥击结束那一帧清空")

    # ---- 6. 遗种巢 ----
    pet = src[PET]
    dispose = body(pet, "internal void Dispose()")
    if dispose is None or "BossRushFxKit.Release(gameObject," not in dispose:
        errors.append("崽特效回收必须自然退场（BossRushFxKit.Release），不能一帧消失")
    attach = body(pet, "internal static PetNestAuraEffect Attach(")
    if attach is None or "if (fx != null) fx.OnDestroy();" not in attach:
        errors.append("崽特效搭建异常时必须手动回收已建资源（未激活过的对象不会走 OnDestroy）")
    return errors


PROBES = [
    (RING, 'mat.SetVector("_TintColor", new Vector4(0.5f, 0.5f, 0.5f, 0.5f));',
     'mat.SetColor("_TintColor", Color.white);'),
    (RING, "private void Update()", "private void LateUpdate() { } private void Update()"),
    (MIST, "protected override int WorldMaxParticles => 6;", "protected override int WorldMaxParticles => 40;"),
    (NOVA, "SpawnSetBurst(origin, FROST_SET_BURST_COLOR, 0.9f, 0.22f, 3);",
     "SpawnSetBurst(origin, FROST_SET_BURST_COLOR, 0.9f, 0.22f, 3, true);"),
    (FROSTMOURNE, "main.startSize = new ParticleSystem.MinMaxCurve(0.16f * sizeScale, 0.28f * sizeScale);",
     "main.startSizeMultiplier = 1.8f * sizeScale;"),
    (PET, "BossRushFxKit.Release(gameObject, 0.3f, 1.5f, true);", "Destroy(gameObject);"),
]


def main():
    sources = {}
    for path in PATHS:
        full = ROOT / path
        if not full.exists():
            print("VfxAestheticBaselineGuard: FAIL - missing " + path)
            return 1
        sources[path] = full.read_text(encoding="utf-8-sig")
    errors = check(sources)
    for path, before, after in PROBES:
        if sources[path].count(before) != 1:
            errors.append("反向检查锚点失效：" + path + " -> " + before)
            continue
        altered = dict(sources)
        altered[path] = altered[path].replace(before, after, 1)
        if not check(altered):
            errors.append("反向检查未转红：" + path + " -> " + after)
    if errors:
        print("VfxAestheticBaselineGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("VfxAestheticBaselineGuard: PASS（%d 条结构断言 + %d 个反向探针）" % (15, len(PROBES)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
