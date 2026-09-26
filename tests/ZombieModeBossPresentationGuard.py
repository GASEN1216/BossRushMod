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
    boss = source("ZombieMode/ZombieModeBossController.cs")
    look = source("ZombieMode/ZombieModeBossVisuals.cs")
    damage = source("ZombieMode/ZombieModeDamageRuntime.cs")
    wave = source("ZombieMode/ZombieModeWaveController.cs")
    patch = source("Patches/Combat/BossLethalHealthProtectionPatch.cs")
    register = body(boss, "private void RegisterZombieModeBossRuntime(")
    assert "ZombieModeBossVisuals.Attach(this, instance);" in register, "Boss runtime must attach presentation"
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
    tick = body(look, "private void Update()")
    for token in ("owner.ZombieModeCurrentRunId != marker.RunId", "marker.DeathSettled", "marker.RemovedFromRuntime",
                  "owner.IsZombieModeRuntimePaused()", "if (paused) return;", "sparks.Pause(false)"):
        assert token in tick, "appearance lifecycle: " + token
    assert "GetComponentsInChildren" not in tick and "new " not in tick, "appearance must not rebuild per frame"
    destroy = body(look, "private void OnDestroy()")
    for resource in ("armorMesh", "seamMesh", "displayPreset"):
        assert "Destroy(" + resource + ")" in destroy, "instance resource cleanup: " + resource
    assert "Shader.Find" not in look and "new Material(" not in look and "Collider" not in look
    hunter = body(boss, "internal void TickZombieModeHunterState(")
    assert "boss.transform.position =" not in hunter, "Hunter must not teleport before warning"
    assert "dash.Initialize(runId, boss, player.transform.position," in hunter
    assert "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, telegraph, dash, null);" in hunter
    assert "return ZombieModeDamageRuntime.InjectBeforeHealthLoss(instructions);" in patch
    hurt = body(wave, "private void HandleZombieModeHealthHurt(")
    assert "AbsorbZombieModeBossFinalDamage(" not in hurt and "RestoreZombieModeFinalDamageReduction(" not in wave, "no post-death heal-back or double absorption"
    reduce = body(damage, "internal static void ReduceFinalDamage(")
    assert "marker.RunId != owner.ZombieModeCurrentRunId" in reduce
    assert "owner.ApplyZombieModeEnemyDefense(health, ref info, marker);" in reduce
    assert "owner.AbsorbZombieModeBossFinalDamage(target, marker, info.finalDamage)" in reduce
    assert "info.finalDamage = Mathf.Max(0f, info.finalDamage - absorbed);" in reduce
    elite = body(source("ZombieMode/ZombieModePollution.cs"), "internal void ApplyZombieModeEnemyDefense(")
    assert "damageInfo.damageValue" not in elite and "SetHealth(" not in elite, "elite defenses must consume actual damage before health loss"
    print("ZombieModeBossPresentationGuard: PASS (static wiring only)")


if __name__ == "__main__":
    main()
