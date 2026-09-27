"""Boss 外形与战斗接线；只能证明结构，观感与碰撞需实机验收。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent


def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def body(text, signature):
    start = text.index(signature)
    opening = text.index("{", start)
    depth = 0
    for i in range(opening, len(text)):
        depth += (text[i] == "{") - (text[i] == "}")
        if depth == 0:
            return text[opening:i + 1]
    raise AssertionError(signature)


def main():
    boss = source("ZombieMode/ZombieModeRuntimeModule_BossController.cs")
    look = source("ZombieMode/ZombieModeBossVisuals.cs")
    damage = source("ZombieMode/ZombieModeDamageRuntime.cs")
    wave = source("ZombieMode/ZombieModeRuntimeModule_WaveController.cs")
    patch = source("Patches/Combat/BossLethalHealthProtectionPatch.cs")
    register = body(boss, "internal void RegisterZombieModeBossRuntime(")
    assert "ZombieModeBossVisuals.Attach(owner, instance);" in register, "Boss runtime must attach presentation"
    attach = body(look, "internal static void Attach(")
    assert "Transform modelRoot = instance.Character.modelRoot;" in attach
    assert "root.transform.SetParent(modelRoot != null ? modelRoot : instance.Character.transform, false);" in attach, "boss silhouette must follow official facing root"
    silhouette = body(look, "private static void BuildSilhouette(")
    for kind in ("Titan", "Hunter", "Splitter", "Shielder", "Corruptor"):
        case = silhouette.split("case ZombieModeBossKind." + kind + ":", 1)[1].split("break;", 1)[0]
        assert "Shard(armor, glow, color," in case, kind + " must build a silhouette"
        tick = body(boss, "internal void TickZombieMode" + kind + "State(")
        assert "ZombieModeBossVisuals.Pulse(instance.Marker);" in tick, kind + " skill must signal presentation"
        triggers = re.findall(r"if \(now >= [^\n]+", tick)
        for trigger in triggers:
            if "NextPoisonPathTime" in trigger:
                continue  # 持续毒径已有地面表现，不重复每段播施法脉冲。
            assert "ZombieModeBossVisuals.Pulse(instance.Marker);" in body(tick, trigger), kind + " missing pulse: " + trigger
    init = body(look, "private void Initialize(")
    assert "displayPreset = Instantiate(originalPreset);" in init
    assert 'displayPreset.nameKey = "BossRush_ZombieMode_Boss_" + instance.Kind;' in init
    assert "displayPreset.showName = true;" in init, "official health bar must show boss identity"
    assert "CharacterRandomPreset_CharacterIconType.SetValue(" in init and "CharacterIconTypes.boss" in init, \
        "zombie bosses must carry the official boss icon like other mod bosses"
    # 官方 CharacterMainControl.OnDead 按 characterPreset.nameKey 写 SavesCounter 与击杀任务：
    # 显示用副本必须在致死一击扣血前换回，否则官方存档多出自定义计数键、击杀丧尸任务漏算 Boss。
    restore = body(look, "internal static void RestoreOfficialPreset(")
    assert "look.instance.Character.characterPreset = look.originalPreset;" in restore
    assert "if (marker.IsBoss && info.finalDamage >= health.CurrentHealth)" in body(damage, "internal static void ReduceFinalDamage(") \
        and "ZombieModeBossVisuals.RestoreOfficialPreset(marker);" in body(damage, "internal static void ReduceFinalDamage("), \
        "lethal hit must restore the official preset before OnDead"
    assert "ZombieModeBossVisuals.PlayDeath(marker);" in body(boss, "internal void HandleZombieModeBossDeathEffects("), \
        "boss death must play the presentation burst"
    # 死因：残留腐蚀区 / 毒径 / 死亡毒云以 Boss 尸体为来源。尸体要活过最长的残留区，
    # 死后（静态 OnDead，晚于写击杀计数的实例 OnDeadEvent）再挂回显示副本，结算页才显示 Boss 名而不是「自己」。
    assert "KeepCorpseForResidualZones(instance.Character);" in init, "boss corpse must outlive residual zones"
    for duration in ("CorruptorZoneDurationSeconds", "CorruptorPoisonPathDurationSeconds", "CorruptorDeathCloudDurationSeconds"):
        assert "ZombieModeTuning." + duration in look.split("CorpseKeepSeconds =", 1)[1].split(";", 1)[0], \
            "corpse lifetime must cover " + duration
    death = body(look, "internal static void PlayDeath(")
    assert "look.instance.Character.characterPreset = look.displayPreset;" in death and "RestoreOfficialPreset(" not in death, \
        "after official kill count, the corpse must carry the boss display preset for the death reason"
    # 俯视镜头下的身份：五种纹章各有独立分支，贴图按种类缓存。
    sigil = body(look, "private static float SigilAlpha(")
    for kind in ("Titan", "Hunter", "Splitter", "Shielder"):
        assert "case ZombieModeBossKind." + kind + ":" in sigil, kind + " must paint its own sigil"
    assert "default:" in sigil, "Corruptor sigil branch"
    assert "GetSigilTexture(instance.Kind)" in init
    # LineRenderer 线宽是世界米数、不随 Transform 缩放；低于 0.06 m 在游戏镜头下是 1–2 px 噪点（VB-08）。
    width = re.search(r"const float CrestLineWidth = ([0-9.]+)f;", look)
    assert width and float(width.group(1)) >= 0.06, "crest line must be at least 0.06 m wide"
    assert "main.scalingMode = ParticleSystemScalingMode.Local;" in body(look, "private void CreateSparks("), \
        "ember sizes are world meters only under Local scaling"
    tick = body(look, "private void Update()")
    for token in ("owner.ZombieModeCurrentRunId != marker.RunId", "marker.DeathSettled", "marker.RemovedFromRuntime",
                  "owner.IsZombieModeRuntimePaused()", "if (paused) return;", "sparks.Pause(false)"):
        assert token in tick, "appearance lifecycle: " + token
    assert "GetComponentsInChildren" not in tick and "new " not in tick, "appearance must not rebuild per frame"
    destroy = body(look, "private void OnDestroy()")
    for resource in ("armorMesh", "seamMesh", "displayPreset"):
        assert "Destroy(" + resource + ")" in destroy, "instance resource cleanup: " + resource
    reset = body(look, "internal static void ResetStaticCaches()")
    assert "for (int i = 0; i < SigilTextures.Length; i++)" in reset, "sigil reset must visit every cached texture"
    assert "if (SigilTextures[i] != null) Destroy(SigilTextures[i]);" in reset, "sigil cache must release Unity textures"
    assert "SigilTextures[i] = null;" in reset, "sigil cache must drop destroyed references"
    module = source("ZombieMode/ZombieModeRuntimeModule.cs")
    assert "ZombieModeBossVisuals.ResetStaticCaches();" in body(module, "public override void OnDestroy()"), \
        "ZombieMode owner must release sigil cache on destroy"
    assert "Shader.Find" not in look and "new Material(" not in look and "Collider" not in look
    hunter = body(boss, "internal void TickZombieModeHunterState(")
    assert "boss.transform.position =" not in hunter, "Hunter must not teleport before warning"
    assert "dash.Initialize(runId, boss, player.transform.position," in hunter
    assert "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, telegraph, dash, null);" in hunter
    # 丧尸减伤 Transpiler 必须自成一类：IL 失配时拒绝安装只能拆掉它自己，
    # 不能连带共享 Hurt 上下文补丁（Mode G 屏障 / 逆鳞无敌 / Boss 致死钳制）一起失效。
    zombie_patch = body(damage, "internal static class ZombieModeHealthHurtDamagePatch")
    assert "[HarmonyPatch(typeof(Health), nameof(Health.Hurt))]" in damage.split("internal static class ZombieModeHealthHurtDamagePatch", 1)[0][-200:]
    assert "return ZombieModeDamageRuntime.InjectBeforeHealthLoss(instructions);" in zombie_patch
    assert "[HarmonyTranspiler]" not in patch and "InjectBeforeHealthLoss" not in patch,         "zombie transpiler must not share the lethal-protection patch class"
    hurt = body(wave, "private void HandleZombieModeHealthHurt(")
    assert "AbsorbZombieModeBossFinalDamage(" not in hurt and "RestoreZombieModeFinalDamageReduction(" not in wave, "no post-death heal-back or double absorption"
    reduce = body(damage, "internal static void ReduceFinalDamage(")
    assert "marker.RunId != owner.ZombieModeCurrentRunId" in reduce
    assert "owner.ApplyZombieModeEnemyDefense(health, ref info, marker);" in reduce
    assert "owner.AbsorbZombieModeBossFinalDamage(target, marker, info.finalDamage)" in reduce
    assert "info.finalDamage = Mathf.Max(0f, info.finalDamage - absorbed);" in reduce
    elite = body(source("ZombieMode/ZombieModeRuntimeModule_PollutionTuning.cs"), "internal void ApplyZombieModeEnemyDefense(")
    assert "damageInfo.damageValue" not in elite and "SetHealth(" not in elite, "elite defenses must consume actual damage before health loss"
    bridge = body(source("ZombieMode/ZombieModeCombatHostBridge.cs"), "internal void ApplyZombieModeEnemyDefense(")
    assert "if (module != null) module.ApplyZombieModeEnemyDefense(health, ref damageInfo, marker);" in bridge, \
        "defense bridge must forward finalDamage by reference to the real module owner"
    assert "shield.AbsorbDamage(ref damageInfo.finalDamage);" in elite, "elite shield must consume finalDamage in the owner"
    print("ZombieModeBossPresentationGuard: PASS (static wiring only)")


if __name__ == "__main__":
    main()
