"""天空岛头目 / 岛主招式的「光 + 声」回执接线（2026-09-27 发版复审补的两层）。

此前所有头目招式都没有音效、结算没有光、换阶段与倒下只有字幕。这里钉住：
1. 五种一次性音效由 tools/gen_sky_island_sfx.py 可复现生成，且 SkyIslandBossSfx.Files 与枚举顺序、生成表逐一对应；
2. 音效有节流（同一种 0.08 s 内一次）与距离门（45 m），闪光有同时盏数上限（URP 逐像素光预算）；
3. 调用点：每个 Boss 预警圈画出来就响蓄力声；custom 招式（无官方火球）补落地闷响、官方火球招式不重复；
   十一位档案 Boss 倒下统一经 RaiseDefeated 放倒下回执且先于剧情回调；四位多阶段 Boss 跨档时放换阶段回执；
   噬风的预警圈与倒下同样接上；共享静态状态在 ResetStaticCaches 里收掉。

文本守卫防不住「保留调用、杀掉执行路径」，表现是否好看由 owner 实机目检。脚本末尾带内存反向检查：逐条把源码改坏，确认本守卫会红。
"""
import re
import sys
from pathlib import Path

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent
SKY = ROOT / "SkyIsland"
FILES = {
    "fx": SKY / "SkyIslandImpactFx.cs",
    "assets": SKY / "SkyIslandFxAssets.cs",
    "forge": SKY / "SkyIslandBossForge.cs",
    "foreman": SKY / "SkyIslandForemanBoss.cs",
    "root": SKY / "SkyIslandRootHunterBoss.cs",
    "sickle": SKY / "SkyIslandSickleBoss.cs",
    "storm": SKY / "SkyIslandStormBoss.cs",
    "voice": SKY / "SkyIslandBossVoice.cs",
    "gen": ROOT / "tools" / "gen_sky_island_sfx.py",
}
CUES = ["boss_telegraph.wav", "boss_impact.wav", "boss_shatter.wav", "boss_phase.wav", "boss_defeat.wav", "homecoming_bell.wav"]


def squash(text):
    return re.sub(r"\s+", "", text or "")


def body_of(text, signature):
    if text is None:
        return None
    at = text.find(signature)
    if at < 0:
        return None
    start = text.find("{", at)
    if start < 0:
        return None
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    return None


def need(errors, haystack, needle, message):
    if haystack is None or squash(needle) not in squash(haystack):
        errors.append(message)


def ordered(errors, haystack, needles, message):
    flat = squash(haystack)
    pos = 0
    for needle in needles:
        found = flat.find(squash(needle), pos)
        if found < 0:
            errors.append(message)
            return
        pos = found + 1


def check(code):
    errors = []
    # 音效与特效资源在 2026-09-27 拆到 SkyIslandFxAssets.cs：两份一起当「表现层」查。
    fx, forge, gen = code["fx"] + "\n" + code.get("assets", ""), code["forge"], code["gen"]

    # 1. 生成表与播放表一一对应
    files = re.search(r"privatestaticreadonlystring\[\]Files=\{([^}]*)\}", squash(fx))
    listed = re.findall(r'"([^"]+)"', files.group(1)) if files else []
    if listed != CUES:
        errors.append("SkyIslandBossSfx.Files 必须按枚举顺序列出 %s，实际 %s" % (CUES, listed))
    enum = body_of(fx, "internal enum SkyIslandBossCue")
    for i, name in enumerate(["Telegraph", "Impact", "Shatter", "Phase", "Defeat", "Toll"]):
        need(errors, enum, "%s = %d" % (name, i), "SkyIslandBossCue.%s 必须等于 %d（下标对应 Files）" % (name, i))
    for cue in CUES:
        if not re.search(r'"%s":\s*[0-9.]+' % re.escape(cue), gen):
            errors.append("tools/gen_sky_island_sfx.py 的 DURATIONS 缺 %s（音效必须可复现生成并过 --check）" % cue)
        if 'name == "%s"' % cue not in gen:
            errors.append("tools/gen_sky_island_sfx.py 的 generate 缺 %s 分支" % cue)

    # 2. 节流、距离门、闪光上限
    need(errors, fx, "private const float MinInterval = 0.08f;", "音效节流常量缺失或被改")
    need(errors, fx, "private const float AudibleRange = 45f;", "音效距离门常量缺失或被改")
    play = body_of(fx, "internal static void Play(Transform root, SkyIslandBossCue cue, Vector3 at)")
    ordered(errors, play, ["if (now < nextAllowed[index]) return;", "> AudibleRange * AudibleRange) return;",
                           "nextAllowed[index] = now + MinInterval;", "post.Invoke("],
            "SkyIslandBossSfx.Play 必须先过节流与距离门再播")
    need(errors, fx, "private const int FlashCap = 4;", "闪光同时盏数上限缺失或被改")
    flash = body_of(fx, "internal static void Flash(Transform root, Vector3 at, float range, Color tint, float intensity, float seconds)")
    ordered(errors, flash, ["if (flashes.Count >= FlashCap) return;", "light.shadows = LightShadows.None;",
                            "SkyIslandLightFade.FadeTo(light, 0f, seconds, true);"],
            "Flash 必须先查盏数上限、不投影、并淡出自毁")
    reset = body_of(fx, "internal static void ResetStaticCaches()")
    need(errors, reset, "flashes.Clear();", "ResetStaticCaches 要收掉闪光")
    need(errors, reset, "SkyIslandBossSfx.ResetStaticCaches();", "ResetStaticCaches 要收掉音效发声体")

    # 3. 调用点
    ring = body_of(forge, "internal static LineRenderer CreateGroundRing(")
    ordered(errors, ring, ["if (!line.enabled)", "SkyIslandBossSfx.Play(root, SkyIslandBossCue.Telegraph, world);", "return line;"],
            "Boss 预警圈画出来之后（材质可用）才响蓄力声")
    detonate = body_of(forge, "internal static void Detonate(")
    need(errors, detonate, "SkyIslandImpactFx.Play(source.transform.parent, origin, radius, tint, 0, !blast);",
         "Detonate：custom 招式补落地闷响，官方火球招式不重复")
    impact = body_of(fx, "internal static void Play(Transform root, Vector3 origin, float radius, Color tint, int dustCount, bool thump)")
    need(errors, impact, "if (thump) SkyIslandBossSfx.Play(root, SkyIslandBossCue.Impact, origin);", "结算表现要按 thump 补闷响")
    need(errors, impact, "Flash(root, origin,", "结算表现要有闪光")
    raise_ = body_of(forge, "internal static void RaiseDefeated(SkyIslandBossProfile profile, Vector3 position, DamageInfo damage)")
    ordered(errors, raise_, ["SkyIslandImpactFx.DefeatBurst(null, position, DefeatTint(profile));", "Action<SkyIslandBossProfile, Vector3> handler = Defeated;",
                             "if (!KilledByMainCharacter(damage)) return;", "handler(profile, position);"],
            "RaiseDefeated 要先放倒下回执（谁打死的都放），首杀剧情回调只认主角击杀（CR-2026-09-29-112）")
    phases = {
        "foreman": ("phase = target;", "SkyIslandImpactFx.PhaseBurst(context.Root, boss.transform.position, StarfireTint);", "DeployPylons();"),
        "root": ("phase = target;", "SkyIslandImpactFx.PhaseBurst(context.Root, boss.transform.position, AmbushTint);", "int index = PickHollow();"),
        "sickle": ("phase = target;", "SkyIslandImpactFx.PhaseBurst(context.Root, boss.transform.position, SweepTint);", "OpenSluice(player);"),
        "storm": ("phase = target;", "SkyIslandImpactFx.PhaseBurst(boss.transform.parent, boss.transform.position, RingTint);", "EnterPhase();"),
    }
    for key, seq in phases.items():
        ordered(errors, code[key], list(seq), FILES[key].name + " 跨档时要放换阶段回执")
    storm_dead = body_of(code["storm"], "private void OnDead(DamageInfo damage)")
    need(errors, storm_dead, "SkyIslandImpactFx.DefeatBurst(boss.transform.parent, boss.transform.position, RingTint);", "噬风倒下要放倒下回执")
    need(errors, code["storm"], "SkyIslandBossSfx.Play(boss.transform.parent, SkyIslandBossCue.Telegraph, eyeOrigin);", "噬风预警要响蓄力声")
    voice_dead = body_of(code["voice"], "private void OnDead(DamageInfo damage)")
    ordered(errors, voice_dead, ["if (profile == null)", "SkyIslandImpactFx.DefeatBurst("],
            "具名剧情对手（折翎、守钟装置）倒下要补倒下回执，且只给没有档案的那一类")
    return errors


def load():
    code = {}
    for key, path in FILES.items():
        text = path.read_text(encoding="utf-8")
        code[key] = text if path.suffix == ".py" else clean_source(text)
    return code


def reverse_checks(code):
    probes = [
        ("assets", '"boss_defeat.wav"', '"boss_death.wav"'),
        ("assets", "if (now < nextAllowed[index]) return;", ""),
        ("fx", "if (flashes.Count >= FlashCap) return;", ""),
        ("forge", "SkyIslandBossSfx.Play(root, SkyIslandBossCue.Telegraph, world);", ""),
        ("forge", "tint, 0, !blast);", "tint, 0, true);"),
        ("forge", "SkyIslandImpactFx.DefeatBurst(null, position, DefeatTint(profile));", ""),
        ("sickle", "SkyIslandImpactFx.PhaseBurst(context.Root, boss.transform.position, SweepTint);", ""),
        ("gen", '"boss_phase.wav": 1.6', '"boss_phase_x.wav": 1.6'),
        ("voice", "if (profile == null)", "if (profile != null)"),
    ]
    failures = []
    for key, old, new in probes:
        if code[key].count(old) != 1:
            failures.append("反向检查锚点不唯一：%s %s" % (key, old))
            continue
        broken = dict(code)
        broken[key] = code[key].replace(old, new)
        if not check(broken):
            failures.append("反向检查没有转红：%s 删改 %s" % (key, old))
    return failures


def main():
    code = load()
    errors = check(code)
    if not errors:
        errors = reverse_checks(code)
    if errors:
        for e in errors:
            print("FAIL:", e)
        sys.exit(1)
    print("PASS: SkyIslandBossFeedbackGuard（五种音效生成/播放对应、节流与光预算、全部调用点；9 个反向探针均转红）")


if __name__ == "__main__":
    main()
