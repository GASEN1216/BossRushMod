"""沙暴战斗使用官方移动与伤害接收体，猪鲨顺序和召唤物 owner 保持完整。

这是结构守卫：检查生产调用点和 owner 清理，不证明 ECM2 实机碰撞、
A* 场景图、弹幕可躲性或 NPC 近战能在真实游戏里正常推进。
"""
from pathlib import Path
import re

from cs_source_util import clean_source


ROOT = Path(__file__).resolve().parents[1]
BOSS = "Integration/SandstormChampion/SandstormChampionBoss.cs"
COMBAT = "Integration/SandstormChampion/SandstormChampionAbilityController.cs"
MINIONS = "Integration/SandstormChampion/SandstormChampionMinions.cs"
MARKER = "Integration/SandstormChampion/SandstormChampionMinionMarker.cs"
PATTERN = "Integration/SandstormChampion/SandstormChampionAttackPattern.cs"
HAZARDS = "Integration/SandstormChampion/SandstormChampionHazards.cs"
BODY = "Integration/SandstormChampion/SandstormChampionBody.cs"
CONFIG = "Integration/SandstormChampion/SandstormChampionConfig.cs"
DEATH = "Patches/Combat/CharacterOnDeadPatch.cs"


def compact(value):
    return re.sub(r"\s+", " ", value).strip()


def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def body(path, signature, scope=None):
    value = source(path)
    if scope is not None:
        start = value.index("{", value.index("class " + scope)) + 1
        end, depth = start, 1
        while depth:
            depth += (value[end] == "{") - (value[end] == "}")
            end += 1
        value = value[start:end - 1]
    assert value.count(signature) == 1, f"{path}: 方法签名应唯一: {signature}"
    start = value.index("{", value.index(signature)) + 1
    end, depth = start, 1
    # 本守卫选中的方法没有含花括号的字符串或字符字面量。
    while depth:
        assert end < len(value), f"{path}: 方法体未闭合: {signature}"
        depth += (value[end] == "{") - (value[end] == "}")
        end += 1
    return compact(value[start:end - 1])


def main():
    checks = 0

    def check(condition, label):
        nonlocal checks
        assert condition, label
        checks += 1

    def require(path, signature, *statements):
        value = body(path, signature)
        for statement in statements:
            check(compact(statement) in value, f"{path} / {signature}: 缺少 {statement}")
        return value

    def ordered(value, label, *statements):
        previous = -1
        for statement in statements:
            position = value.find(compact(statement), previous + 1)
            check(position >= 0, f"{label}: 顺序缺失 {statement}")
            previous = position

    spawn = require(BOSS, "internal static async UniTask<CharacterMainControl> SpawnAsync(",
                    "boss.transform.localScale = Vector3.one;", "RemoveOriginalCombat(boss);")
    ordered(spawn, BOSS + " 创建前剥离原技能",
            "runtimePreset = UnityEngine.Object.Instantiate(source);",
            "runtimePreset.hasSkill = false;", "runtimePreset.skillPfb = null;",
            "runtimePreset.defaultWeaponOut = false;", "boss = await runtimePreset.CreateCharacterAsync(",
            "RemoveOriginalCombat(boss);", "controller.Initialize(")
    require(BOSS, "private static void RemoveOriginalCombat(",
                       "boss.CancleSkill();", "boss.Trigger(false, false, true);",
                       "boss.ChangeHoldItem(null);", "RemoveWeapon(boss.PrimWeaponSlot());",
                       "RemoveWeapon(boss.SecWeaponSlot());", "RemoveWeapon(boss.MeleeWeaponSlot());",
                       "StopVanillaAI(boss);")
    original = require(BOSS, "internal static void StopVanillaAI(",
                       "original.enabled = false;", "original.gameObject.SetActive(false);")
    for tree in ("patrolTree", "alertTree", "combatTree", "combat_Attack_Tree"):
        check(f"if (original.{tree} != null) original.{tree}.Stop(true);" in original,
              BOSS + ": 原版 Graph 未停机: " + tree)
    ordered(original, BOSS + " 停树 owner 后停 AI 子物体",
            "treeOwner.StopBehaviour(true);", "treeOwner.enabled = false;",
            "original.gameObject.SetActive(false);")
    require(BOSS, "private static void RemoveWeapon(", "Item weapon = slot.Unplug();", "weapon.DestroyTree();")

    require(COMBAT, "internal void Initialize(", "boss.movementControl.MovementEnabled = true;",
            "boss.movementControl.SetGravityFactor(1f);", "_fightRoutine = RunFight();")
    require(COMBAT, "private IEnumerator RunFight()", "routines.Push(Fight());",
            "moved = routine.MoveNext();", "if (moved) current = routine.Current;",
            "IEnumerator nested = current as IEnumerator;", "if (nested != null) routines.Push(nested);",
            "else yield return current;")
    motion = require(COMBAT, "private bool CommandMotion(", "_boss.SetForceMoveVelocity(velocity);",
                     "_lastMotionPosition = from;", "_motionCommanded = true;")
    for forbidden in ("SetPosition(", "ForceSetPosition(", "TryResolveReachableFrom("):
        check(forbidden not in motion, COMBAT + ": 连续移动重新接入逐帧瞬移或稀疏导航拒绝: " + forbidden)
    require(COMBAT, "private void MoveToward(", "RequestNavigation(target);",
            "CommandMotion(direction.normalized * Mathf.Min(speed, direction.magnitude / Mathf.Max(0.01f, Time.deltaTime)));")
    for signature in ("private IEnumerator Hover(", "private IEnumerator OrbitAndOrbs(",
                      "private IEnumerator BubbleBelch("):
        value = body(COMBAT, signature)
        check(re.search(r"\bMoveToward\([^;]+\);", value) is not None,
              COMBAT + " / " + signature + ": 未调用连续移动")
        check("StopMotion();" in value, COMBAT + " / " + signature + ": 动作结束未停移动")
    dash = require(COMBAT, "private IEnumerator Dashes(",
                   "if (!CommandMotion(direction * speed)) { strikeBlocked = true; break; }", "distance += actual.magnitude;",
                   "StopMotion();")
    ordered(dash, COMBAT + " 冲锋按宿主实际位移计程",
            "if (!CommandMotion(direction * speed)) { strikeBlocked = true; break; }", "yield return null;",
            "Vector3 actual = _boss.transform.position - previous;", "distance += actual.magnitude;")
    require(COMBAT, "private void StopMotion()", "_motionCommanded = false;",
            "_boss.SetMoveInput(Vector3.zero);", "_boss.SetForceMoveVelocity(Vector3.zero);")
    require(COMBAT, "private void RequestNavigation(", "int revision = _navigationRevision;",
            "if (this == null || revision != _navigationRevision || !IsFighting) return;",
            "_navigationPath.Add(path.vectorPath[i]);")
    require(COMBAT, "private void CancelNavigation()", "_navigationRevision++;",
            "_navigationPath.Clear();", "_navigator.CancelCurrentPathRequest(true);")
    cleanup = body(COMBAT, "internal void StopBattle(")
    ordered(cleanup, COMBAT + " 战斗 owner 清理", "_stopped = true;", "StopMotion();",
            "CancelNavigation();", "if (_minions != null) _minions.Shutdown();", "StopAllCoroutines();")
    combat_source = source(COMBAT)
    # owner 2026-10-08：末阶段冲锋穿墙。SetPosition 只许出现在换侧与穿墙冲锋专用的 PhaseTo 两处。
    check(len(re.findall(r"\b_boss\.SetPosition\(", combat_source)) == 2,
          COMBAT + ": SetPosition 应仅用于第三阶段换侧与穿墙冲锋")
    require(COMBAT, "private IEnumerator Reposition()", "_boss.SetPosition(safe);")
    require(COMBAT, "private void PhaseTo(", "_boss.SetPosition(position);")
    require(COMBAT, "private IEnumerator Dashes(", "bool phasing = _phase == 3;",
            "if (phasing) { if (!PhaseDashStep(ref direction, speed, !hit)) { strikeBlocked = true; break; } }",
            "if (phasing) SettlePhaseDash();")
    require(COMBAT, "internal void Initialize(", "_fightRoutine = RunFight();")
    require(COMBAT, "private void Update()", "TickFight();")
    require(COMBAT, "private void TickFight()", "_fightRoutine.MoveNext()")
    require(COMBAT, "private IEnumerator WaitForFightSeconds(", "while (Time.time < until && IsFighting) yield return null;")
    for method in ("Fight()", "Dashes(", "SummonTornadoes(", "Transition()", "Reposition()"):
        check("new WaitForSeconds" not in body(COMBAT, "private IEnumerator " + method),
              COMBAT + ": Update 驱动的战斗不得交回 Unity 等待: " + method)

    fight = require(COMBAT, "private IEnumerator Fight()",
                    "_attackPattern.UpdateState(ratio, _enraged)",
                    "SandstormChampionAttack attack = _attackPattern.Next();",
                    "yield return Dashes(attack.Count);", "yield return BubbleBelch(attack.Count);",
                    "yield return OrbitAndOrbs(attack.Count);", "StartCoroutine(SummonMinions());")
    check("EscortMinions" not in fight and "HasActiveMinions" not in fight,
          COMBAT + ": 沙卫不得暂停猪鲨攻击序列")
    summon = body(COMBAT, "private IEnumerator SummonMinions()")
    transition = body(COMBAT, "private IEnumerator Transition()")
    for signature, value in (("SummonMinions", summon), ("Transition", transition)):
        check("ClearHazards();" not in value, COMBAT + ": " + signature + " 不得顶掉沙暴自身 TTL")
    check(len(re.findall(r"\bClearHazards\(\);", combat_source)) == 1,
          COMBAT + ": 场上危险物只应在 StopBattle 统一清理")
    require(COMBAT, "private IEnumerator Transition()", "_boss.Health.SetInvincible(true);",
            "_boss.Health.SetInvincible(false);", "_body.SetEyesOnly(true);")
    require(COMBAT, "private void ApplyPhaseDefense()", "_phase == 3 ? 0f : _phase == 2 ? 0.8f : 1f",
            "_bodyArmorModifier.Value = factor - 1f;", "_headArmorModifier.Value = factor - 1f;")
    require(COMBAT, "internal void Initialize(",
            "new Modifier(ModifierType.PercentageMultiply, 0f, true, int.MaxValue, this);")
    require(COMBAT, "internal void StopBattle(", "_bodyArmorModifier.RemoveFromTarget();",
            "_headArmorModifier.RemoveFromTarget();")
    for signature in ("private IEnumerator BubbleBelch(", "private IEnumerator OrbitAndOrbs("):
        require(COMBAT, signature, "while (emitted < count && elapsed >= emitted * duration / count)")
    volley = body(COMBAT, "internal void SpawnTornadoVolley(")
    check("HasActiveMinions" not in volley and "HasPhaseTransition" not in volley,
          COMBAT + ": 既有旋风发射不得因沙卫或转阶段停机")
    require(COMBAT, "private IEnumerator SummonTornadoes(",
            "SpawnSeed(_boss.transform.position + Vector3.up * 1.4f, center, true);",
            "SpawnSeed(_boss.transform.position + lateral * (side * 1.5f) + Vector3.up * 1.4f, center, false);")
    seed_spawn = body(HAZARDS, "internal static SandstormCycloneSeed Spawn(")
    check("Vector3 initial = GroundPoint(at);" in seed_spawn,
          HAZARDS + ": 沙圈必须从 Boss 的发射点贴地出生，不得取玩家目标位置")
    require(COMBAT, "private void ClearHazards()", "_seeds.Clear();", "_tornadoes.Clear();", "_orbs.Clear();")

    state = body(PATTERN, "internal bool UpdateState(")
    check("healthRatio < 0.15f ? 3 : healthRatio < 0.5f ? 2 : 1" in state,
          PATTERN + ": 专家/大师/传奇阈值必须是严格小于 50% / 15%")
    pattern = body(PATTERN, "internal SandstormChampionAttack Next()")
    final = pattern[pattern.index("if (Phase == 3)"):pattern.index("if (Enraged)")]
    # owner 2026-10-08（两次实测）：末阶段每组一/二/三冲后轮换双生沙卷、绕圈吐泡、大沙暴。
    check("SandstormChampionAttackKind.TeleportDashes" in final
          and "SandstormChampionAttackKind.TwinTornadoSeeds" in final
          and "SandstormChampionAttackKind.SpiralBubbles" in final
          and "SandstormChampionAttackKind.HomingCycloneSeed" in final,
          PATTERN + ": 末阶段应为换侧一/二/三冲 + 轮换三种技能")
    dashes = body(COMBAT, "private IEnumerator Dashes(")
    check("AimDash(from, speed, lead, windup - waited, out direction, out length);" in dashes
          and "if (waited < lockAt)" in dashes and "_warning.Retarget(" in dashes,
          COMBAT + ": 冲锋前摇须预判玩家走位、预警带跟随并在出手前锁定")
    check("SandstormChampionConfig.DashLeadP1" in dashes,
          COMBAT + ": 一阶段冲锋也要预判（owner 2026-10-08）")

    init = body(HAZARDS, "private void Init(SandstormChampionController owner, Vector3 direction,")
    ordered(init, HAZARDS + " 可击破泡/鲨的官方接收体",
            "receiver.OnHurtEvent = new UnityEvent<DamageInfo>();", "_health = gameObject.AddComponent<HealthSimpleBase>();",
            "_health.maxHealthValue = homing ? 1f : SandstormChampionConfig.SharkHealth;",
            "_health.dmgReceiver = receiver;", "receiver.simpleHealth = _health;",
            "_health.OnDeadEvent += OnShotDown;")
    require(HAZARDS, "private void OnShotDown(", "Pop(true);")
    require(HAZARDS, "private void OnDestroy()", "_health.OnDeadEvent -= OnShotDown;")
    tornado = body(HAZARDS, "private void Update()", "SandstormTornado")
    check("_volleys < (_cyclone ? 12 : 6)" in tornado and "_owner.SpawnTornadoVolley(pos, _cyclone);" in tornado,
          HAZARDS + ": 每柱沙鲨数必须保持 6 / 12")
    check("transform.position =" not in tornado, HAZARDS + ": 旋风落地后必须固定，不能整个柱追玩家")
    require(HAZARDS, "private void Land(", "_owner.ResolveCycloneSeed(target, _cyclone);")
    seed = body(HAZARDS, "private void Update()", "SandstormCycloneSeed")
    check("Vector3 target = player.transform.position;" in seed
          and "Vector3.MoveTowards(from, flatTarget, speed * Time.deltaTime)" in seed,
          HAZARDS + ": 双生沙圈须持续追踪当前玩家位置")
    check("if (contact || _life >= SandstormChampionConfig.CycloneSeedLifetime) Land(next);" in seed
          and seed.count("Land(") == 1,
          HAZARDS + ": 只能接触或计时到期凝柱，墙体不得提前引爆")
    check("Vector3.Dot(offset, step)" in seed and "contact = false;" in seed,
          HAZARDS + ": 追踪接触需要扫掠检测并拒绝隔墙接触")
    require(MINIONS, "private async UniTask SpawnOneAsync(",
            'SetCharacterStat(character, "WalkSpeed", SandstormChampionConfig.MinionWalkSpeed);')
    require(MARKER, "internal void Activate()", "Time.time + SandstormChampionConfig.MinionFirstAttackDelay;")
    require(MARKER, "private void TickCombat()", "Time.time + SandstormChampionConfig.MinionRepathSeconds;",
            "Time.time + SandstormChampionConfig.MinionAttackCooldown;")
    require(MARKER, "internal void Activate()", "SandstormChampionBody.Attach(_character, 0.56f, true);")
    require(MARKER, "internal void Despawn()", "if (_body != null) Destroy(_body.gameObject);")
    require(BODY, "internal void SetEyesOnly(", "if (value) ClearSandBody();")
    require(BODY, "private void ApplyRates()", "_eyesOnly ? (_charging ? 0.7f : 0f) : 1f")
    config = {key: float(value) for key, value in re.findall(
        r"internal const (?:float|int) (\w+) = ([\d.]+)f?;", source(CONFIG))}
    check(config["Phase2HealthRatio"] == 0.5 and config["Phase3HealthRatio"] == 0.15,
          CONFIG + ": 控制器中断条件必须与生产节奏引擎阈值一致")
    check(config["CycloneHeight"] == config["TornadoHeight"] * 2,
          CONFIG + ": 大沙暴核高度必须是小沙卷的两倍")
    check(config["TornadoRadius"] >= 3.4 and config["CycloneRadius"] >= 5.6
          and config["TornadoHeight"] >= 12 and config["CycloneHeight"] >= 24,
          CONFIG + ": 本轮沙暴半径与高度均须至少翻倍")
    check(config["MinionWalkSpeed"] >= 6.2 and 0 < config["MinionRepathSeconds"] <= 0.16
          and 0 < config["MinionFirstAttackDelay"] <= 0.2 and 0 < config["MinionAttackCooldown"] <= 0.68,
          CONFIG + ": 沙卫移动、寻路反应和近战出手需提速")
    check(config["TornadoLifetime"] == 9 and config["CycloneLifetime"] == 14,
          CONFIG + ": 旋风必须保留 9 / 14 秒自身 TTL")
    check(config["MaxLiveOrbs"] >= 31 + config["MaxLiveTornadoes"] * 12,
          CONFIG + ": 弹幕预算不得吞掉 31 泡与既有沙鲨")
    check(0 < config["CycloneVolleySeconds"] < config["TornadoVolleySeconds"] < 1.65,
          CONFIG + ": 大/小柱喷弹必须比旧 1.65 秒间隔更频繁")
    check(26 < config["DashSpeedP1"] < config["DashSpeedP2"] < config["DashSpeedP3"],
          CONFIG + ": 三阶段冲速必须随阶段递增并超过旧一阶段 26 m/s")
    check(config["DashDamageP3"] < config["DashDamageP2"],
          CONFIG + ": 第三阶段接触伤必须从二阶段回落")

    minion_spawn = body(MINIONS, "private async UniTask SpawnOneAsync(")
    ordered(minion_spawn, MINIONS + " 创建窗口无掉落身份",
            "preset.name = SandstormChampionMinionMarker.PresetName;",
            "preset.nameKey = SandstormChampionMinionMarker.NameKey;", "preset.isBoss = false;",
            "preset.dropBoxOnDead = false;", "preset.hasSoul = false;", "preset.exp = 0;",
            "preset.hasCashChance = 0f;", "preset.hasSkill = false;",
            "character = await preset.CreateCharacterAsync(", "character.dropBoxOnDead = false;",
            "character.Health.hasSoul = false;", 'character.CharacterItem.SetInt("Exp", 0, true);',
            "SandstormChampionBoss.StopVanillaAI(character);", "DestroyGeneratedLoadout(character.CharacterItem);",
            "character.isBossCharacter = false;")
    ordered(minion_spawn, MINIONS + " 临时星阙交给角色物品树",
            "staff = ItemAssetsCollection.InstantiateSync(BossRushItemIds.AstralStaff);",
            "slot.Plug(staff, out replaced);", "character.ChangeHoldItem(staff);",
            "AstralStaffWeaponConfig.PrepareSummonedMinionHoldAgentVisual(melee.gameObject, marker);",
            "marker.Activate();", "character.gameObject.SetActive(true);",
            "SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(character);")
    for forbidden in ("RegisterBossRandomLootTracking(", "TryTrack(", ".Drop(", "DropAllItems("):
        check(forbidden not in compact(source(MINIONS) + source(MARKER)),
              MINIONS + ": 小弟不得接入掉落或随机战利品: " + forbidden)
    identity = require(MARKER, "internal static bool IsSummonedMinion(",
                       "string.Equals(character.characterPreset.name, PresetName, StringComparison.Ordinal)",
                       "character.GetComponent<SandstormChampionMinionMarker>() != null;")
    check("if (character == null) return false;" in identity, MARKER + ": 身份查询缺判空")
    require(MARKER, "internal void Despawn()", "_despawning = true;", "_active = false;",
            "if (_character != null) _character.dropBoxOnDead = false;", "_seeker.CancelCurrentPathRequest(true);",
            "gameObject.SetActive(false);", "Destroy(gameObject);")
    require(MINIONS, "internal void DismissWave()", "_generation++;", "marker.Despawn();", "_minions.Clear();")
    prefix = body(DEATH, "public static void Prefix(")
    check(prefix.startswith("if (SandstormChampionMinionMarker.IsSummonedMinion(__instance)) return;"),
          DEATH + ": 小弟必须在全部额外掉落 handler 之前早返，保留原版 void Prefix 协议")

    # owner 2026-10-08：棍卫随机使出星阙三档重击。公平性与 owner 约束钉在结构上。
    arts_path = "Integration/SandstormChampion/SandstormChampionMinionArts.cs"
    strike = require(arts_path, "private bool Strike(", "if (player == null || BossSkillDamageRules.IsDodging(player)) return false;",
                     "_controller.HurtPlayer(damage, to);")
    ordered(strike, arts_path + " 棍卫重击先判翻滚与隔墙再结算", "BossSkillDamageRules.IsDodging(player)",
            "Physics.Linecast(from, to, wall, QueryTriggerInteraction.Ignore)", "_controller.HurtPlayer(damage, to);")
    for signature in ("private IEnumerator Sweep()", "private IEnumerator Spin()", "private IEnumerator Starfall()"):
        check("Warning" in body(arts_path, signature), arts_path + " / " + signature + ": 每招都要有地面预警")
    require(MARKER, "private void TickCombat()", "if (_arts != null && _arts.Running)", "_arts.TryStart(player, towards.magnitude, sight)")
    require(MARKER, "internal void Despawn()", "if (_arts != null) _arts.Release();")
    print(f"SandstormChampionCombatGuard: PASS ({checks} assertions; structure only, no game physics proof)")


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, ValueError, FileNotFoundError) as error:
        print("SandstormChampionCombatGuard: FAIL " + str(error))
        raise SystemExit(1)
