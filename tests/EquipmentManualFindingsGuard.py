"""Pin the model, VFX and held-only preparation wiring from the September 19 playtest."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]

def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))

def body(text, signature):
    start = text.index(signature)
    opening = text.index("{", start)
    depth = 0
    for index in range(opening, len(text)):
        depth += (text[index] == "{") - (text[index] == "}")
        if depth == 0:
            return re.sub(r"\s+", " ", text[opening:index + 1])
    raise AssertionError("Unclosed method: " + signature)

def require(text, token, label):
    assert re.sub(r"\s+", " ", token) in text, label

def main():
    gear = source("Integration/Config/FrostThunderSetConfig.cs")
    require(gear, "private const float DEFAULT_DURABILITY = 100f;", "set durability must be 100")
    configured = body(gear, "private static void ConfigureSetItem(")
    require(configured, 'ApplyModelFit(modelBaseName, slotTag == "Armor");', "set fit must run from real configurator")
    fit = body(gear, "private static void ApplyModelFit(")
    for token in ('if (root.Find(fitName) != null) return;', 'fit.localScale = isArmor ? Vector3.one * (4f / 3f) : Vector3.one;',
                  'fit.localRotation = isArmor ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);'):
        require(fit, token, "set fitting must be idempotent and keep declared scale/orientation")

    core = source("Integration/NewWeapons/Common/NewWeaponConfiguratorCore.cs")
    require(body(core, "internal static bool ConfigureMelee("), "ApplyModelFit(modelAgent, spec.TypeId);", "weapon fit must run before binding")
    fit = body(core, "private static void ApplyModelFit(")
    for token in ('if (root.Find(fitName) != null) return;', 'if (centeredGrip) visual.localPosition = Vector3.zero;',
                  'fit.localScale = dagger ? Vector3.one * 0.8f : Vector3.one;'):
        require(fit, token, "weapon grip/scale correction missing")

    swing = source("Integration/NewWeapons/Common/NewWeaponSwingFx.cs")
    require(swing, "private const float ParticleTailDuration = 0.3f;", "swing particles need natural tail lifetime")
    require(body(swing, "private void Update()"), "if (elapsed >= Duration + ParticleTailDuration)", "swing cannot clear particles at end of movement")
    tint = body(swing, "private void Tint(")
    require(tint, "main.startColor = Color.white;", "particle tint must be applied once")
    # 2026-09-19 第二轮：rateOverDistance 把一帧的量全撒在当帧那一个点上，缓出曲线下
    # 起手一帧就吃掉小半条弧，于是起手堆成一坨、后半条是空的。改成逐点手撒。
    built = body(swing, "private void EnsureBuilt(")
    require(built, "emission.enabled = false;", "auto emission must stay off; the trail is emitted point by point")
    require(built, "emission.rateOverDistance = new ParticleSystem.MinMaxCurve(0f);", "rateOverDistance clumps the trail at the start")
    arc = body(swing, "private void EmitAlongArc(")
    for token in ("Mathf.Lerp(lastEmittedAngle, currentAngle,", "emitParams.position = pivotParent.TransformPoint(local);",
                  "trailParticles.Emit(emitParams, 1);"):
        require(arc, token, "swing trail must be emitted at interpolated points along the arc")
    fx = source("Integration/NewWeapons/Common/NewWeaponFx.cs")
    require(body(fx, "internal static void Play("), "BossRushProceduralSprites.GetRingSprite();", "burst must use hollow ring")

    manager = source("Integration/NewWeapons/SummonStaff/SummonStaffManager.cs")
    require(body(manager, "protected override void Update()"), "IsHoldingSummonStaff(targetCharacter)", "preparation must be held-only")
    prepare = body(manager, "private IEnumerator PrepareWhileHeld(")
    assert prepare.count("if (!CanPrepare(player))") == 2, "each preparation stage must recheck owner"
    require(prepare, "SummonStaffAction.PreparePreset();", "preset preparation missing")
    require(body(manager, "protected override void OnDestroy()"), "OnMainCharacterChangeHoldItemAgentEvent -= OnHoldItemChanged;", "hold event cleanup missing")
    action = source("Integration/NewWeapons/SummonStaff/SummonStaffAction.cs")
    spawn = body(action, "private async UniTaskVoid SpawnAlliesAsync(")
    assert "FindPreset(" not in spawn, "first-click spawn cannot scan presets"
    require(spawn, "await UniTask.NextFrame();", "allies must be created in distinct frames")
    require(body(action, "protected override bool IsReadyInternal()"), "if (cachedPreset == null) return false;", "cast must await preset preparation")
    thunder = source("Integration/NewWeapons/ThunderRing/ThunderRingRuntime.cs")
    release = body(thunder, "private static void HandlePlayerAttack(")
    assert release.index("NewWeaponFx.PlayArc(") < release.index("targetHealth.Hurt(thunderDamage);"), "death must not interrupt release visuals"
    assert "⚡" not in thunder, "unsupported emoji in ring prompt"
    print("EquipmentManualFindingsGuard: PASS")
    return 0

if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (AssertionError, ValueError) as error:
        print("EquipmentManualFindingsGuard: FAIL - " + str(error))
        raise SystemExit(1)
