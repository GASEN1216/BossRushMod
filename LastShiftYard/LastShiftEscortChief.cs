using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.UI.DialogueBubbles;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;
using Cat = BossRush.SkyIslandLootCategory;

namespace BossRush
{
    /// <summary>
    /// 押运长「封线点名」（设计稿第三批，原型 sim.mjs 的 bossCombat）：原版 AI 已发现玩家、视线可达、42 m 内时，
    /// 举旗、两声短哨、沿锁定方向画出火线；预警 2.2 秒后到 4 秒每 0.4 秒扫一段，玩家离火线 1.45 m 内且两人之间没有墙层遮挡才中。
    /// 进站的车厢是墙层碰撞（<see cref="LastShiftTrains"/>），所以车身挡枪，尾车让开后恢复。火线一旦锁定不跟人转。
    /// 平时的选敌、交火与追击全交给官方行为树；这里只加这一招和专属掉落。
    /// </summary>
    internal sealed class LastShiftEscortChief : MonoBehaviour
    {
        internal const string SourceName = "EnemyPreset_Boss_Roadblock";
        /// <summary>与潮闸镇沙老大同一档（1500 生命、伤害 ×1.25、前摇 0.3），不吃普通敌人的倍率。</summary>
        internal const float BossHealth = 1500f, BossDamage = 1.25f;
        /// <summary>扫射每一段的伤害（原型 7 / 段，按原型守卫 2–6 与官方枪伤的比例放大）。实机按击杀时间与躲闪手感再调。</summary>
        internal const float PulseDamage = 12f;
        internal const float Reach = 42f, MaxLength = 48f, HalfWidth = 1.45f, Warning = 2.2f, Duration = 4f, PulseGap = .4f, Rest = 3.2f;
        private static readonly Color WarnTint = new Color(1f, .79f, .36f, .75f), FireTint = new Color(.98f, .3f, .18f, .95f);

        private CharacterMainControl boss;
        private AICharacterController ai;
        private Func<bool> valid;
        private Func<int> spare;
        private Action defeated;
        private LineRenderer line, flag;
        private bool aiming, subscribed, settled, secondWhistle;
        private Vector3 origin, direction;
        private float length, aimStart, nextPulse, cooldown, nextThink;

        internal static void Tune(CharacterRandomPreset clone)
        {
            clone.health = BossHealth; clone.forceTracePlayerDistance = 0f;
            clone.patrolRange = 3f; clone.damageMultiplier = BossDamage; clone.shootDelay = .3f;
            clone.setMeleeDamageMultiplier = true; clone.meleeDamageMultiplier = BossDamage;
            clone.specialAttachmentBases = new List<AISpecialAttachmentBase>(); // 不带参照 Boss 的独有招式，压力来自封线。
        }

        /// <param name="pickSpare">玩家已带着钢印时换成的押运私货（返回 TypeID，0 表示不可用就照旧给钢印）。</param>
        internal void Bind(CharacterMainControl character, AICharacterController controller, bool fromTrain,
            Func<bool> sessionValid, Func<int> pickSpare, Action onDefeated)
        {
            if (subscribed) return;
            boss = character; ai = controller; valid = sessionValid; spare = pickSpare; defeated = onDefeated;
            cooldown = Time.time + (fromTrain ? 4f : 1f);
            boss.BeforeCharacterSpawnLootOnDead += BeforeLoot;
            subscribed = true;
        }

        private void Update()
        {
            float now = Time.time;
            if (now < nextThink) return;
            nextThink = now + (aiming ? .05f : .3f);
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (boss == null || boss.Health == null || boss.Health.IsDead || player == null || player.Health == null || player.Health.IsDead
                    || ai == null || valid == null || !valid()) { Stop(now); return; }
                if (aiming) { TickSweep(now, player); return; }
                if (now < cooldown || ai.searchedEnemy == null || ai.searchedEnemy != player.mainDamageReceiver) return;
                Vector3 to = player.transform.position - transform.position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance > Reach || distance < .5f || SightBlocked(Chest(transform.position), Chest(player.transform.position))) return;
                origin = transform.position; direction = to / distance; length = Mathf.Min(MaxLength, distance + 4f);
                aiming = true; aimStart = now; nextPulse = now + Warning; secondWhistle = false;
                line = SkyIslandBossForge.StraightLine(transform.parent, "LastShiftChiefFiringLine", .35f, WarnTint);
                flag = SkyIslandBossForge.StraightLine(transform.parent, "LastShiftChiefFlag", .22f, FireTint);
                SkyIslandBossSfx.Play(transform.parent, SkyIslandBossCue.Telegraph, transform.position);
                Say(L10n.T("举旗——哔！哔！封线！", "Flag up. Two short whistles. Line closed!"));
                DrawLine(false);
            }
            catch (Exception e) { Stop(now); Debug.LogWarning(LastShiftRaidScene.LogTag + " 押运长封线取消：" + e.Message); }
        }

        private void TickSweep(float now, CharacterMainControl player)
        {
            float age = now - aimStart;
            if (!secondWhistle && age >= .35f) { secondWhistle = true; SkyIslandBossSfx.Play(transform.parent, SkyIslandBossCue.Telegraph, transform.position); }
            bool firing = age >= Warning;
            DrawLine(firing);
            if (firing && now >= nextPulse)
            {
                nextPulse = now + PulseGap;
                Vector3 offset = player.transform.position - origin;
                offset.y = 0f;
                float along = Vector3.Dot(offset, direction);
                float across = Mathf.Abs(direction.x * offset.z - direction.z * offset.x);
                // 车厢、墙和掩体（墙层）挡住两人之间就不中；离开火线带也不中。
                if (along >= 0f && along <= length && across < HalfWidth && !Blocked(Chest(origin), Chest(player.transform.position)))
                    Hit(player);
            }
            if (age >= Duration) { Stop(now); cooldown = now + Rest; }
        }

        private void Hit(CharacterMainControl player)
        {
            var damage = new DamageInfo(boss);
            damage.damageValue = PulseDamage;
            damage.fromWeaponItemID = 0;
            damage.damagePoint = player.transform.position + Vector3.up;
            damage.damageNormal = -direction;
            player.Health.Hurt(damage);
        }

        /// <summary>火线画到第一处墙层遮挡为止，玩家看得见车厢把它截断；旗杆跟着押运长。</summary>
        private void DrawLine(bool firing)
        {
            if (line != null)
            {
                Vector3 start = origin + Vector3.up * .25f, end = start + direction * length;
                RaycastHit hit;
                if (Physics.Raycast(Chest(origin), direction, out hit, length, GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore))
                    end = start + direction * hit.distance;
                line.SetPosition(0, start); line.SetPosition(1, end);
                Color c = firing ? FireTint : WarnTint;
                line.startColor = line.endColor = c;
                line.widthMultiplier = firing ? .55f : .3f + .1f * Mathf.PingPong(Time.time * 6f, 1f);
            }
            if (flag != null)
            {
                Vector3 top = transform.position + Vector3.up * 3.4f;
                flag.SetPosition(0, transform.position + Vector3.up * 1.9f); flag.SetPosition(1, top);
            }
        }

        private static Vector3 Chest(Vector3 feet) { return feet + Vector3.up * 1.1f; }

        private static bool Blocked(Vector3 a, Vector3 b)
        {
            return Physics.Linecast(a, b, GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>起手要看得见：墙层（含车厢）或检修蒸汽（官方 FowBlock 层）挡着都不点名；已锁定的扫射不再看蒸汽。</summary>
        private static bool SightBlocked(Vector3 a, Vector3 b)
        {
            int mask = GameplayDataSettings.Layers.wallLayerMask.value | GameplayDataSettings.Layers.fowBlockLayers.value;
            return Physics.Linecast(a, b, mask, QueryTriggerInteraction.Collide);
        }

        private void Say(string text) { try { DialogueBubblesManager.Show(text, transform, 2.2f, false, false, -1f, 1.2f).Forget(); } catch { } }

        private void Stop(float now)
        {
            aiming = false;
            if (line != null) Destroy(line.gameObject);
            if (flag != null) Destroy(flag.gameObject);
            line = flag = null;
        }

        /// <summary>
        /// 专属掉落进尸体库存：押运钢印；玩家身上已带着一枚时换成一件押运私货（Q5 70% / Q6 30%，护具 40% / 武器 30% / 值钱物 30%）。
        /// 官方「路障」自带的尸体掉落照旧。返回基地、销毁不经过死亡事件，重入也只发一次。
        /// </summary>
        private void BeforeLoot(DamageInfo damage)
        {
            if (settled || !subscribed) return;
            settled = true;
            if (defeated != null) { try { defeated(); } catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 押运长倒下回调失败：" + e.Message); } }
            if (valid == null || !valid()) return;
            Item item = null;
            try
            {
                int typeId = BossRushItemIds.LastShiftStamp;
                if (spare != null && TidegateInventory.Carries(CharacterMainControl.Main, BossRushItemIds.LastShiftStamp))
                {
                    int alternative = spare();
                    if (alternative > 0 && ItemAssetsCollection.GetPrefab(alternative) != null) typeId = alternative;
                }
                var inventory = boss.CharacterItem != null ? boss.CharacterItem.Inventory : null;
                if (inventory == null || ItemAssetsCollection.GetPrefab(typeId) == null) throw new InvalidOperationException("尸体库存或掉落预制件不可用");
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null || item.TypeID != typeId || !InteractableLootboxInventoryHelper.TryAddExtraItem(inventory, item))
                    throw new InvalidOperationException("专属掉落未进入尸体库存");
                item = null;
                Debug.Log(LastShiftRaidScene.LogTag + " CHIEF_DROP type=" + typeId);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 押运长专属掉落：" + e.Message); }
            finally { SkyIslandInventoryTransaction.DestroyUnowned(item); }
        }

        /// <summary>押运私货：玩家带着旧钢印再打倒押运长时的那一件（设计稿「搜刮品质分布」Boss 行）。目录不可用时返回 0，照旧给钢印。</summary>
        internal static int RollSpare(TidegateLoot.PrizeCatalog catalog, System.Random random)
        {
            if (catalog == null || random == null) return 0;
            int roll = random.Next(100);
            Cat category = roll < 40 ? Cat.Gear : roll < 70 ? Cat.Weapon : Cat.Valuable;
            return catalog.Pick(category, random.Next(100) < 70 ? 5 : 6, 5, 6, random, Cat.General);
        }

        private void OnDisable() { Stop(Time.time); }

        private void OnDestroy()
        {
            if (subscribed && boss != null) boss.BeforeCharacterSpawnLootOnDead -= BeforeLoot;
            subscribed = false;
            Stop(Time.time);
        }
    }
}
