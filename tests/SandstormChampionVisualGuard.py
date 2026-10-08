"""沙暴表现的几何、资源所有权和预算约束；不判断画面审美是否合格。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
BASE = "Integration/SandstormChampion/"


def read(name):
    return clean_source((ROOT / BASE / name).read_text(encoding="utf-8-sig"))


def body(source, signature):
    start = source.index("{", source.index(signature)) + 1
    end, depth = start, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return re.sub(r"\s+", " ", source[start:end - 1]).strip()


def main():
    volume = read("SandstormChampionVolume.cs")
    boss = read("SandstormChampionBody.cs")
    hazards = read("SandstormChampionHazards.cs")
    asset = read("SandstormChampionAssetManager.cs")
    # DashWarning retains a wide textured ground strip; decorative air wireframes must be gone.
    for name, source in (("body", boss), ("hazards", hazards.split("internal sealed class SandstormOrb", 1)[1]), ("asset", asset), ("volume", volume)):
        assert not re.search(r"(?:AddComponent<|new\s+)(?:LineRenderer|TrailRenderer)", source), name + " recreates decorative wires"
    assert "SandstormChampionVolume.Create(root, 0.95f * s, 3f * s, true);" in boss, "boss lost its volume owner"
    assert "SandstormChampionVolume.Create(transform, _radius, height, false);" in hazards, "tornado lost its volume owner"
    assert "seed._field = SandstormGroundField.Create(go.transform, SandstormChampionConfig.SeedContactRadius);" in hazards
    assert "SandstormGroundField.Create(null, radius);" in asset, "damage warning radius must reach the ground field"
    assert "homing ? BossRushParticleShape.Bubble : BossRushParticleShape.Pearl" in hazards, "projectile membrane missing"
    assert "if (!homing) _sharkVisual = SandstormSharkVisual.Create(transform);" in body(hazards, "private void Init(SandstormChampionController owner, Vector3 direction,"), "straight projectile lost its sand dragon silhouette"
    assert "if (_sharkVisual != null) _sharkVisual.Release();" in body(hazards, "internal void Pop(bool hit)"), "dead projectile keeps refreshing its silhouette"
    shark = volume.split("internal sealed class SandstormSharkVisual", 1)[1].split("internal sealed class SandstormGroundField", 1)[0]
    assert "new ParticleSystem.Particle[24]" in shark and "new ParticleSystem.Particle[6]" in shark, "sand dragon particle budget changed"
    assert "enabled = false;" in body(shark, "internal void Release()"), "sand dragon release must stop lifetime refresh"
    assert "_volume.Release();" in body(boss, "internal void Dissipate()"), "dead boss keeps refreshing particles"
    assert "_volume.Release();" in body(hazards, "internal void Dismiss()"), "expired tornado keeps refreshing particles"
    assert "enabled = false;" in body(volume, "internal void Release()"), "volume release must stop lifetime refresh"
    assert "if (_volume != null) _volume.Clear();" in body(boss, "private void ClearSandBody()"), "phase three must hide sand body"
    assert "new ParticleSystem.Particle[body ? 96 : 192]" in volume
    assert "new ParticleSystem.Particle[body ? 48 : 80]" in volume
    assert "new ParticleSystem.Particle[64]" in volume and "new ParticleSystem.Particle[96]" in volume
    for scope in (volume.split("internal sealed class SandstormGroundField")[0], volume.split("internal sealed class SandstormGroundField")[1]):
        update = body(scope, "private void LateUpdate()")
        assert "_nextFrame = Time.time + 1f / 30f;" in update, "missing visual sampling cap"
        assert not re.search(r"new\s+(?:ParticleSystem\.Particle\[|Material|Texture2D|GameObject|List<)", update), "visual tick allocates engine objects or buffers"
    assert "float envelope = SampleHeightDensity(h);" in body(volume, "private void LateUpdate()"), "density regression must exercise the rendered density function"
    assert "_nextFrame = Time.time + 1f / 30f;" in body(shark, "private void LateUpdate()"), "sand dragon sampling cap missing"
    assert "ParticleSystemSortMode.Distance" in volume, "alpha cloud layers need depth sorting"
    assert "static readonly" not in volume and "new Material" not in volume and "new Texture2D" not in volume, "volume must reuse session materials without another cache owner"
    print("SandstormChampionVisualGuard: PASS (geometry/ownership/budgets only; no visual quality claim)")


if __name__ == "__main__":
    main()
