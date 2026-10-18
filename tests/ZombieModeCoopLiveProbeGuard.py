"""Keep the on-demand real-game zombie observer Dev-only and free of gameplay writes."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
PROBE = "DebugAndTools/F3GameplayValidationZombieCoopProbe.cs"


def validate(raw):
    lines = [line.strip() for line in raw.splitlines() if line.strip()]
    assert lines[0] == "#if BOSSRUSH_DEV" and lines[-1] == "#endif", "whole probe must be Dev-only"
    assert sum(line.startswith("#if") for line in lines) == 1 and lines.count("#endif") == 1, "Dev boundary must enclose every declaration"
    src = re.sub(r"\s+", " ", clean_source(raw))
    assert 'if (!SavesSystem.KeyExisits(marker) || SavesSystem.Load<string>(marker) != "v1:slot:" + SavesSystem.CurrentSlot)' in src, "probe requires the real dedicated slot marker"
    assert "throw new InvalidOperationException(\"dedicated_test_slot_required\")" in src, "dedicated gate must reject"
    assert "Asset = BossRushCoop.Runtime != null ? (string)typeof(BossRushCoopRuntimeModule).GetField(\"localAssetId\"" in src, "observe published asset without refreshing identity"
    assert "BossRushCoop.LocalAssetId" not in src, "asset getter can send a hello and change identity"
    assert not re.search(r"\b(?:IsZombieModeRunValid|IsCoopZombieRunLeaseCurrent|Tick|Deliver|Broadcast|SendToHost|SendToPlayer|MarkSceneReady|SetPosition|AddHealth|Hurt|SetInvincible|StartCoroutine|Instantiate|Destroy|ApplySlow|SaveFile|Save)\s*(?:<[^>]+>)?\s*\(", src), "observer must not mutate gameplay, network or persistence"
    assert not re.search(r"\b(?:Update|OnGUI|OnEnable|OnAwake)\s*\(", src), "observer is called explicitly, never per-frame or a UI"
    assert "partial class ModBehaviour" not in src, "do not grow host partials"
    assert "data.FullSuiteRunning" not in src and "FullSuiteRunning = F3GameplayValidationRunner.IsRunning" in src, "report assistance instead of concealing it"
    assert "result.Health = actor.Health.CurrentHealth; result.Dead = actor.Health.IsDead; result.Invincible = actor.Health.Invincible;" in src, "actual local health and invincibility must be observable"
    assert "player.HostAlive" in src and "player.IsAlive" not in src, "observe existing native facts without changing cached alive state"
    assert "return BossRushCoopJson.ToJson(data);" in src, "return structured evidence without recording PASS"
    assert not re.search(r'"PASS"|\bRecord\s*\(', src), "sampling cannot certify its own success"


if __name__ == "__main__":
    try:
        validate((ROOT / PROBE).read_text(encoding="utf-8-sig"))
        print("ZombieModeCoopLiveProbeGuard PASS")
    except AssertionError as error:
        print("ZombieModeCoopLiveProbeGuard FAIL:", error)
        raise SystemExit(1)
