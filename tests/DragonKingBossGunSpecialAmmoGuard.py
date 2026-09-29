"""Guard: 焚天龙铳的大型能量弹「虚空奇点」与水弹「灵潮水漂」（2026-09-29）。

钉四件事：

1. 数据：大型能量弹 #918（口径 PWL）是空间属性、带奇点；水球 #1630（口径 WaterBall）是灵能属性、
   打水漂 3 次、挂官方浸水、留灵泉水洼。
2. 命中标记编码：弹种 ID 占 ShotMarkerStageScale 以内的一段，MaxProfileId 必须覆盖最大枚举值且小于它，
   否则新弹种命中不叠龙焰印记（TryDecodeShotMarker 会拒掉）。
3. 主文件的五处钩子都在对应方法体里：清缓存、拖尾、奇点按准星落点、反弹、死亡。主文件在大文件冻结名单上，
   逻辑都在 DragonKingBossGunSpecialAmmo.cs，钩子丢一处，对应弹种就静默退化成普通弹。
4. 行为不变式：打水漂触地要把地面从 damagedObjects 拿掉（否则第二跳穿地）；奇点逐帧用官方强制位移拽人、
   不拽 Boss、同屏有上限；浸水按 Buff_Water 找、找不到退回官方水球枪（#1628）的命中 Buff；
   灵泉水洼（ghost 区）挂浸水。

文本守卫只证结构；拽人手感、水漂弹跳高度与特效观感只能实机看。
"""

from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

PROFILES = Path("Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs")
RUNTIME = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs")
AGENT = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")
SPECIAL = Path("Integration/DragonKing/Weapons/DragonKingBossGunSpecialAmmo.cs")
ZONES = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileZones.cs")
COMPILE_LIST = Path("compile_official.bat")


def fail(message: str) -> int:
    print("DragonKingBossGunSpecialAmmoGuard: FAIL - " + message)
    return 1


def read_clean(path: Path) -> str:
    return clean_source(path.read_text(encoding="utf-8-sig").replace("\r\n", "\n"))


def extract_block(text: str, signature: str):
    start = text.find(signature)
    if start < 0:
        return None
    brace = text.find("{", start)
    if brace < 0:
        return None
    depth = 0
    for idx in range(brace, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace:idx + 1]
    return None


def profile_block(text: str, profile_id: str):
    start = text.find("Id = DragonKingBossGunProfileId." + profile_id + ",")
    if start < 0:
        return None
    end = text.find("new DragonKingBossGunShotProfile", start)
    return text[start:end if end >= 0 else len(text)]


def main() -> int:
    profiles = read_clean(PROFILES)
    runtime = read_clean(RUNTIME)
    agent = read_clean(AGENT)
    special = read_clean(SPECIAL)
    zones = read_clean(ZONES)

    # 1. 数据
    expectations = {
        "VoidOrb": [
            "TypeIds = new[] { 918 }",
            'Calibers = new[] { "PWL" }',
            "Element = ElementTypes.space",
            "UseSingularity = true",
            "RequiresCustomMovement = true",
        ],
        "WaterBall": [
            "TypeIds = new[] { 1630 }",
            'Calibers = new[] { "WaterBall" }',
            "Element = ElementTypes.ghost",
            "UseSkipSplash = true",
            "ApplyWaterSoak = true",
            "Bounce = 3",
            "Arc = DragonKingBossGunArcMode.Low",
            "UseGroundZone = true",
            "GroundZoneElement = ElementTypes.ghost",
        ],
    }
    for profile_id, snippets in expectations.items():
        block = profile_block(profiles, profile_id)
        if block is None:
            return fail("missing profile " + profile_id)
        for snippet in snippets:
            if snippet not in block:
                return fail(profile_id + " profile lost -> " + snippet)

    # 2. 命中标记编码
    enum_body = extract_block(profiles, "internal enum DragonKingBossGunProfileId")
    enum_values = [int(v) for v in re.findall(r"=\s*(\d+)", enum_body or "")]
    max_profile = re.search(r"private const int MaxProfileId = (\d+);", runtime)
    stage_scale = re.search(r"private const int ShotMarkerStageScale = (\d+);", runtime)
    if not enum_values or not max_profile or not stage_scale:
        return fail("cannot read profile enum / MaxProfileId / ShotMarkerStageScale")
    if int(max_profile.group(1)) < max(enum_values):
        return fail(f"MaxProfileId {max_profile.group(1)} < largest profile id {max(enum_values)}: new ammo marks are dropped")
    if int(max_profile.group(1)) >= int(stage_scale.group(1)):
        return fail("MaxProfileId must stay below ShotMarkerStageScale or hit stage decoding collides")

    # 3. 主文件钩子
    hooks = [
        ("internal static void ClearStaticCaches()", "ClearSpecialAmmoStaticCaches();"),
        ("public void Initialize(Projectile projectileInstance", "customTrailInstance = CreateSpecialAmmoTrail();"),
        ("private float ResolveAirburstDistance()", "profile.UseSingularity"),
        ("private void ManualMoveAndCheck()", "profile.UseSingularity && traveledDistanceRef(projectile) >= airburstDistance"),
        ("private void Bounce(GameObject hitObject", "OnSpecialAmmoBounce(hitObject, hitPoint, hitNormal);"),
        ("private void HandleDeath()", "HandleSpecialAmmoDeath(resolvedDeathPoint);"),
    ]
    for signature, snippet in hooks:
        body = extract_block(agent, signature)
        if body is None:
            return fail("missing agent method " + signature)
        if snippet not in body:
            return fail("agent hook missing in " + signature + " -> " + snippet)

    # 4. 行为不变式
    bounce = extract_block(special, "private void OnSpecialAmmoBounce(")
    if bounce is None or "projectile.damagedObjects.Remove(hitObject);" not in bounce:
        return fail("skip splash must drop the ground from damagedObjects, or the second skip falls through the floor")
    if "DragonKingBossGunRuntime.IsGroundSurface(hitObject, hitNormal)" not in bounce:
        return fail("skip damping must only apply on ground contacts")

    pull = extract_block(special, "private void ApplyPull()")
    if pull is None or "character.SetForceMoveVelocity(velocity);" not in pull:
        return fail("singularity pull must use the official per-frame SetForceMoveVelocity")
    refresh = extract_block(special, "private void RefreshPullTargets()")
    if refresh is None or "IsBoss(character)" not in refresh or "sourceContext.realFromCharacter" not in refresh:
        return fail("singularity must not pull bosses or the shooter")
    if "private const int MaxActive = 4;" not in special or "while (activeSingularities.Count >= MaxActive)" not in special:
        return fail("singularity count cap is gone")

    soak = extract_block(special, "internal static Buff ResolveWaterSoakBuff()")
    if soak is None or "WaterSoakBuffKey" not in soak or "OfficialWaterGunTypeId" not in soak:
        return fail("water soak lookup must try Buff_Water first and fall back to the official water gun")
    if 'private const string WaterSoakBuffKey = "Buff_Water";' not in special or "private const int OfficialWaterGunTypeId = 1628;" not in special:
        return fail("water soak lookup constants changed")

    clear = extract_block(special, "private static void ClearSpecialAmmoStaticCaches()")
    if clear is None or "DragonKingBossGunSingularity.ClearStaticCaches();" not in clear or "waterSoakBuffLookupDone = false;" not in clear:
        return fail("special ammo static caches are not cleared on scene change")

    zone_buff = extract_block(zones, "private Buff GetZoneBuff()")
    if zone_buff is None or "case ElementTypes.ghost:" not in zone_buff or "DragonKingBossGunProjectileAgent.ResolveWaterSoakBuff()" not in zone_buff:
        return fail("ghost puddle must apply the water soak buff")

    compile_text = COMPILE_LIST.read_text(encoding="utf-8", errors="ignore")
    if "Integration\\DragonKing\\Weapons\\DragonKingBossGunSpecialAmmo.cs" not in compile_text:
        return fail("DragonKingBossGunSpecialAmmo.cs is not in compile_official.bat")

    print("DragonKingBossGunSpecialAmmoGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
