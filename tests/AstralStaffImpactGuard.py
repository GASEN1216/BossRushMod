"""星阙只在真实伤害后反馈，程序表现必须受手持 owner 和固定预算约束。

结构证据只能证明接线，不证明 Unity 中的观感、帧耗或近战判定可玩。
官方 DamageReceiver.Hurt 会转发请求而忽略 Health.Hurt 的拒绝结果，
因此本守卫同时要求普通 Health 与 HealthSimpleBase 的实际掉血判据。
"""
from pathlib import Path
import re

from cs_source_util import clean_source


ROOT = Path(__file__).resolve().parents[1]
BASE = "Integration/NewWeapons/AstralStaff/"
CONTROL = BASE + "AstralStaffController.cs"
FX = BASE + "AstralStaffFx.cs"
CONFIG = BASE + "AstralStaffConfig.cs"
PATCH = BASE + "AstralStaffAttackPatch.cs"


def compact(text):
    return re.sub(r"\s+", " ", text).strip()


def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def block(text, signature):
    assert text.count(signature) == 1, "方法/类型签名必须唯一: " + signature
    start = text.index("{", text.index(signature)) + 1
    end, depth = start, 1
    # 被检查的方法内不含带花括号的字符串或字符字面量。
    while depth:
        assert end < len(text), "未闭合方法: " + signature
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end - 1]


def body(path, signature, class_name=None):
    text = source(path)
    if class_name is not None:
        text = block(text, "internal sealed class " + class_name + " : MonoBehaviour")
    return compact(block(text, signature))


def main():
    checks = 0

    def check(condition, label):
        nonlocal checks
        assert condition, label
        checks += 1

    def require(path, signature, *statements, class_name=None):
        value = body(path, signature, class_name)
        for statement in statements:
            check(compact(statement) in value, path + " / " + signature + ": 缺少 " + statement)
        return value

    def ordered(value, label, *statements):
        previous = -1
        for statement in statements:
            position = value.find(compact(statement), previous + 1)
            check(position >= 0, label + ": 顺序缺失 " + statement)
            previous = position

    hurt = require(CONTROL, "private static bool HurtTarget(",
                   "float healthBefore = receiver.useSimpleHealth ? simple.HealthValue : health.CurrentHealth;",
                   "if (simple == null || simple.HealthValue >= healthBefore) return false;",
                   "else if (health == null || health.CurrentHealth >= healthBefore) return false;",
                   "if (playContact) AstralStaffFx.PlayContact(point, flat, tier);")
    ordered(hurt, "伤害回执与实际掉血先于接触反馈", "bool accepted = receiver.Hurt(info);",
            "if (!accepted) return false;", "if (receiver.useSimpleHealth)",
            "else if (health == null || health.CurrentHealth >= healthBefore) return false;",
            "if (playContact) AstralStaffFx.PlayContact(point, flat, tier);", "return true;")

    damage = require(CONTROL, "private int DealDamage(",
                     "if (!Team.IsEnemy(player.Team, receiver.Team)) continue;",
                     "if (receiver.simpleHealth == null || receiver.simpleHealth.HealthValue <= 0f) continue;",
                     "int id = receiver.useSimpleHealth ? receiver.simpleHealth.GetInstanceID() : receiver.health.GetInstanceID();",
                     "if (victim != null && victim.Dashing) continue;",
                     "if (hitHealthIds.Contains(id)) continue;",
                     "hits < AstralStaffConfig.MaxHitFxPerAttack",
                     "Array.Clear(OverlapBuffer, 0, OverlapBuffer.Length);")
    ordered(damage, "敌对/视线/去重先于伤害，确认伤害先于计数和击退",
            "if (!Team.IsEnemy(player.Team, receiver.Team)) continue;",
            "if (wallMask != 0 && Physics.Linecast(center, targetPos, wallMask, QueryTriggerInteraction.Ignore)) continue;",
            "hitHealthIds.Add(id);",
            "if (!HurtTarget(player, melee, receiver, targetPos, flat, multiplier, tier, hits < AstralStaffConfig.MaxHitFxPerAttack)) continue;",
            "hits++;", "AddKnockback(victim, push * knockback);")
    feedback = require(CONTROL, "private static void PlayImpactFeedback(",
                       "if (hits <= 0) return;", "GameManager.TimeScaleManager.EnterBulletTime(stop);")
    ordered(feedback, "空挥不得顿帧或震屏", "if (hits <= 0) return;",
            "GameManager.TimeScaleManager.EnterBulletTime(stop);", "Shake(")
    control = source(CONTROL)
    check(len(re.findall(r"\bPlayImpactFeedback\([^;]+;", control)) == 5,
          "三档四次判定都必须走同一个命中反馈门（加一个方法声明）")
    check("killmarker" not in control and "hitmarker" not in control,
          "星阙不得重复官方 HitMarker，或在非击杀/空砸时伪造 marker 声音")
    check(not re.search(r"\bTime\.timeScale\s*=", control), "顿帧必须复用官方时间管理器")
    require(CONTROL, "private static void OnAnyHurt(", "info.finalDamage <= 0f",
            "info.fromWeaponItemID != AstralStaffConfig.TypeId", "info.isFromBuffOrEffect",
            "info.fromCharacter != player", "!Team.IsEnemy(player.Team, target.team)",
            "if (self.creditedSwing == self.swingSerial) return;")
    require(CONTROL, "private void StartHeavy(", "if (player.CurrentStamina < HeavyStamina(level)) return;")
    require(CONTROL, "private IEnumerator ExecuteHeavy(",
            "if (PlayerGone(player) || player.CurrentStamina < HeavyStamina(level)) yield break;")
    require(CONTROL, "private IEnumerator Guard(", "current = top.Current;",
            "if (nested != null) { stack.Push(nested); continue; }", "yield return current;")
    require(CONTROL, "private void ResetState()", "StopCoroutine(heavyRoutine);", "knocks.Clear();",
            "if (hud != null) hud.HideAll();", "transientFxRoot.gameObject.SetActive(false);",
            "Destroy(transientFxRoot.gameObject);")
    require(CONTROL, "internal Transform GetTransientFxRoot()",
            "if (!wasHolding || !NewWeaponEquipState.IsHolding(AstralStaffConfig.TypeId)) return null;",
            "return transientFxRoot.childCount < 64 ? transientFxRoot : null;")
    config = compact(source(CONFIG))
    check("internal const int MaxHitFxPerAttack = 6;" in config, "同次攻击接触点必须封顶为六个")

    hand = require(FX, "private void LateUpdate()", "if (!active)",
                   "player.CurrentHoldItemAgent.gameObject == gameObject;",
                   "(_summonedMinion != null && _summonedMinion.IsCombatActive ? _summonedMinion.Character : null);",
                   "if (mainActive) current = this;", "if (_root == null)",
                   class_name="AstralStaffHandVisual")
    ordered(hand, "装备专属构建先过实际持握门", "if (!active)", "return;", "if (_root == null)")
    require(FX, "internal void BindSummonedMinion(",
            "!SandstormChampionMinionMarker.IsSummonedMinion(marker.Character)", class_name="AstralStaffHandVisual")
    require(FX, "internal static GameObject CreateTransient(", "if (root == null) return null;",
            "go.transform.SetParent(root, false);")
    require(FX, "internal static void PlayBurst(", "if (root == null) return;",
            "if (burst != null) burst.transform.SetParent(root, true);")
    require(FX, "internal static void Play(", "int count = tier >= 3 ? 6 : Mathf.Clamp(tier + 2, 3, 4);",
            class_name="AstralStaffSigil")
    require(PATCH, "public static void Postfix(",
            "if (character == null || character != CharacterMainControl.Main) return;", "AstralStaffFx.PlayStroke(")
    check("NewWeaponSwingFx.PlayAt" not in source(PATCH), "轻击光轨也必须归当前手持 owner")
    print(f"AstralStaffImpactGuard: PASS ({checks} assertions; structure only, no visual/performance proof)")


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, ValueError, FileNotFoundError) as error:
        print("AstralStaffImpactGuard: FAIL " + str(error))
        raise SystemExit(1)
