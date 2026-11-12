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
    /// 押运长（2026-11-12 owner「特效塑料、机制简单；血可以减 1/3，机制要有趣」后重做）。平时的选敌、交火与追击全交给官方行为树，
    /// 这里只在官方 AI 已认准玩家、视线可达时轮流加两招站务招式，再加半血后的变化与专属掉落：
    /// - 「封线」：站定、举红旗、两声短哨，脚下朝玩家铺出一条黄黑斜纹的封线带（半宽 1.3 m、≤ 40 m，在第一处墙层 / 车厢处截断）；
    ///   预警 1.5 s 后沿带扫射 1.5 s，每 0.3 s 一跳 10 伤害（玩家在带内、两人胸口之间没有墙层才中）。带子锁定后不跟人转，
    ///   侧移出带、躲到墙或车厢后面就能避开；进站的车厢是墙层碰撞（<see cref="LastShiftTrains"/>），车身挡枪，车尾让开后恢复。检修蒸汽只挡起手不挡已锁定的扫射。
    /// - 「响墩」：一声长哨，朝玩家脚下（按走位提前 0.6 s）和身侧扔出 2 颗响墩，飞 0.55 s 落地，地上一圈红光与预警圈，
    ///   引信 1.3 s 后官方爆炸（半径 2.2 m、25 伤害，走官方 ExplosionManager：墙挡、不伤同队）。封线逼人进掩体，响墩把人从掩体后面炸出来。
    /// - 半血（≤ 50%）：三声短哨、换手提红灯；之后封线一次两条（第二条朝玩家横移那侧偏 28°，站在两条中间或掩体后）、预警 1.2 s，
    ///   响墩一次 3 颗横排、引信 1.05 s，两招之间的间隔从 3.2 s 缩到 2.2 s。
    /// 表现在 <see cref="LastShiftChiefFx"/>（独立类型）；只在押运长活着、离玩家 60 m 内时跑，远了每 0.3 s 一次 O(1) 早返（§4.12）。
    /// 精确数值只写在 Wiki，游戏里喊话只写人话。
    /// </summary>
    internal sealed class LastShiftEscortChief : MonoBehaviour
    {
        internal const string SourceName = "EnemyPreset_Boss_Roadblock";
        /// <summary>owner 2026-11-12：原 1500（与潮闸镇沙老大同档）减 1/3。伤害倍率与前摇照旧，不吃普通敌人的倍率。</summary>
        internal const float BossHealth = 1000f, BossDamage = 1.25f;
        // 封线
        internal const float LaneReach = 36f, LaneMaxLength = 40f, LaneHalfWidth = 1.3f, LaneWarning = 1.5f, LaneWarningRaged = 1.2f;
        internal const float LaneVolley = 1.5f, LaneTick = .3f, LaneDamage = 10f, LaneSplay = 28f, LaneDraw = .45f;
        // 响墩
        internal const float CapReach = 26f, CapFlight = .55f, CapFuse = 1.3f, CapFuseRaged = 1.05f, CapRadius = 2.2f, CapDamage = 25f;
        internal const float CapSpacing = 3.6f, CapLead = .6f, CapStagger = .12f, CapApex = 2.2f;
        internal const int CapCount = 2, CapCountRaged = 3;
        // 节奏
        internal const float Rest = 3.2f, RestRaged = 2.2f, RageAt = .5f, ActiveRange = 60f;
        private static readonly Color CapTint = new Color(1f, .34f, .16f, 1f), RageTint = new Color(1f, .3f, .14f, 1f);

        private enum Move { None, Lane, Caps }

        private CharacterMainControl boss;
        private AICharacterController ai;
        private AI_PathControl path;
        private Func<bool> valid;
        private Func<int> spare;
        private Action defeated;
        private RaidHalfCover.GroundProbe ground;
        private LastShiftChiefFx fx;
        private bool subscribed, settled, raged, planted, laneFired;
        private Move move = Move.None, last = Move.Caps;
        private float moveStart, cooldown, nextThink, nextTick, nextImpact, nextBark, nextCut, laneWarn, capFuse;
        private int lanes, caps, barkIndex, shake;
        private Vector3 laneOrigin;
        private readonly Vector3[] laneDir = new Vector3[LastShiftChiefFx.LaneCount];
        private readonly float[] laneLength = new float[LastShiftChiefFx.LaneCount], laneShown = new float[LastShiftChiefFx.LaneCount];
        private readonly Vector3[] capFrom = new Vector3[LastShiftChiefFx.CapCount], capTo = new Vector3[LastShiftChiefFx.CapCount];
        private readonly float[] capLaunch = new float[LastShiftChiefFx.CapCount];
        private readonly bool[] capBlown = new bool[LastShiftChiefFx.CapCount];
        private readonly LineRenderer[] capRing = new LineRenderer[LastShiftChiefFx.CapCount];

        internal static void Tune(CharacterRandomPreset clone)
        {
            clone.health = BossHealth; clone.forceTracePlayerDistance = 0f;
            clone.patrolRange = 3f; clone.damageMultiplier = BossDamage; clone.shootDelay = .3f;
            clone.setMeleeDamageMultiplier = true; clone.meleeDamageMultiplier = BossDamage;
            clone.specialAttachmentBases = new List<AISpecialAttachmentBase>(); // 不带参照 Boss 的独有招式，压力来自封线与响墩。
        }

        /// <param name="pickSpare">玩家已带着钢印时换成的押运私货（返回 TypeID，0 表示不可用就照旧给钢印）。</param>
        /// <param name="groundAt">本图地面查询（响墩落点贴地）。</param>
        internal void Bind(CharacterMainControl character, AICharacterController controller, bool fromTrain,
            Func<bool> sessionValid, Func<int> pickSpare, Action onDefeated, RaidHalfCover.GroundProbe groundAt)
        {
            if (subscribed) return;
            boss = character; ai = controller; valid = sessionValid; spare = pickSpare; defeated = onDefeated; ground = groundAt;
            path = character.GetComponentInChildren<AI_PathControl>(true);
            fx = new LastShiftChiefFx(transform.parent, transform, character.characterModel != null ? character.characterModel.transform : transform);
            cooldown = Time.time + (fromTrain ? 4f : 1f);
            boss.BeforeCharacterSpawnLootOnDead += BeforeLoot;
            subscribed = true;
        }

        private void Update()
        {
            float now = Time.time;
            if (move == Move.None && now < nextThink) return;
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (boss == null || boss.Health == null || boss.Health.IsDead || player == null || player.Health == null || player.Health.IsDead
                    || ai == null || valid == null || !valid()) { Stop(now); nextThink = now + .3f; return; }
                Vector3 to = player.transform.position - transform.position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance > ActiveRange) { Stop(now); fx.Lamp(false, 0f); nextThink = now + .3f; return; }
                if (!raged && boss.Health.CurrentHealth <= boss.Health.MaxHealth * RageAt) Rage(now);
                if (raged) fx.Lamp(true, now);
                if (move == Move.Lane) { TickLane(now, player); return; }
                if (move == Move.Caps) { TickCaps(now); return; }
                nextThink = now + (raged ? .1f : .3f);
                if (now < cooldown || ai.searchedEnemy == null || ai.searchedEnemy != player.mainDamageReceiver || distance < .5f) return;
                if (distance > LaneReach || SightBlocked(Chest(transform.position), Chest(player.transform.position))) return;
                // 封线的预警全靠地上那条带子：特效画不出来就不出这一招（响墩的预警圈由天空岛地面圈自己核材质，画不出来抛错作废）。
                bool drawn = fx.Available;
                Move pick = !drawn ? Move.Caps : distance > CapReach ? Move.Lane : distance < 5f ? Move.Caps : last == Move.Lane ? Move.Caps : Move.Lane;
                if (pick == Move.Lane) StartLane(now, player, to / distance, distance);
                else if (!StartCaps(now, player, to / distance) && drawn) StartLane(now, player, to / distance, distance);
            }
            catch (Exception e) { Stop(now); cooldown = now + Rest; Debug.LogWarning(LastShiftRaidScene.LogTag + " 押运长招式取消：" + e.Message); }
        }

        // ---------------- 半血 ----------------
        private void Rage(float now)
        {
            raged = true;
            SkyIslandImpactFx.PhaseBurst(transform.parent, transform.position, RageTint);
            LastShiftSounds.Play(LastShiftSounds.ChiefWhistleThree, gameObject);
            Say(L10n.T("全线封锁！", "Shut down every line!"), now, true);
            if (move == Move.None) cooldown = Mathf.Max(cooldown, now + 1.2f);
            Debug.Log(LastShiftRaidScene.LogTag + " CHIEF_RAGE hp=" + boss.Health.CurrentHealth);
        }

        // ---------------- 封线 ----------------
        private void StartLane(float now, CharacterMainControl player, Vector3 direction, float distance)
        {
            move = Move.Lane; last = Move.Lane;
            // 预警时长在起手时定下：半血正好发生在预警中途，也不会把这一条的预警砍短。
            laneWarn = raged ? LaneWarningRaged : LaneWarning;
            moveStart = now; laneFired = false; nextTick = now + laneWarn; nextImpact = now + laneWarn; nextCut = 0f; shake = 0;
            laneOrigin = transform.position;
            lanes = raged ? 2 : 1;
            laneDir[0] = direction;
            if (lanes > 1)
            {
                // 第二条朝玩家正在横移的那一侧偏开；站着不动就随机一侧。站在两条中间、或躲进掩体都能避开。
                Vector3 side = new Vector3(-direction.z, 0f, direction.x), velocity = player.Velocity;
                velocity.y = 0f;
                float lateral = Vector3.Dot(velocity, side);
                float sign = Mathf.Abs(lateral) > .5f ? Mathf.Sign(lateral) : (UnityEngine.Random.value < .5f ? -1f : 1f);
                laneDir[1] = Quaternion.Euler(0f, -sign * LaneSplay, 0f) * direction;
            }
            for (int i = 0; i < lanes; i++) { laneLength[i] = Mathf.Min(LaneMaxLength, distance + 6f); laneShown[i] = laneLength[i]; }
            Cut();
            Plant(true);
            LastShiftSounds.Play(LastShiftSounds.ChiefWhistleTwo, gameObject);
            Say(LaneLine(barkIndex++), now, false);
            fx.Flag(true, now);
        }

        private void TickLane(float now, CharacterMainControl player)
        {
            float age = now - moveStart, warn = laneWarn;
            if (now >= nextCut) { nextCut = now + .1f; Cut(); }
            bool firing = age >= warn;
            float draw = Mathf.Clamp01(age / LaneDraw), charge = Mathf.Clamp01(age / warn);
            float alpha = firing ? Mathf.Clamp01((warn + LaneVolley + .25f - age) / .35f) : BossRushUI.SmoothStep(Mathf.Clamp01(age / .2f));
            for (int i = 0; i < lanes; i++)
                fx.Lane(i, laneOrigin, laneOrigin + laneDir[i] * (laneShown[i] * (firing ? 1f : draw)), LaneHalfWidth, charge, firing, alpha, now);
            fx.Flag(!firing, now);
            fx.Tick(now);
            if (firing)
            {
                if (!laneFired)
                {
                    laneFired = true;
                    SkyIslandImpactFx.Flash(transform.parent, Muzzle(laneDir[0]), 6f, LastShiftChiefFx.Amber, 1.8f, .25f);
                    SkyIslandImpactFx.Shake(transform.position, .25f);
                }
                if (now >= nextImpact)
                {
                    nextImpact = now + .06f;
                    int i = shake++ % lanes;
                    float along = UnityEngine.Random.Range(1.2f, Mathf.Max(1.3f, laneShown[i] - .3f));
                    float across = UnityEngine.Random.Range(-.85f, .85f) * LaneHalfWidth;
                    Vector3 dir = laneDir[i], hit = laneOrigin + dir * along + new Vector3(-dir.z, 0f, dir.x) * across;
                    SkyIslandImpactFx.Sparks(transform.parent, hit + Vector3.up * .12f, LastShiftChiefFx.Amber, 3);
                    SkyIslandImpactFx.Puff(transform.parent, hit, .25f, 2);
                    Vector3 muzzle = Muzzle(dir);
                    fx.Tracer(muzzle, hit + Vector3.up * .25f, now);
                    if ((shake & 1) == 0) fx.Muzzle(muzzle, dir);
                }
                if (now >= nextTick)
                {
                    nextTick = now + LaneTick;
                    // 判定画到哪里就打到哪里：带内（截断之前）且两人胸口之间没有墙层（含进站车厢）。
                    Vector3 feet = player.transform.position;
                    for (int i = 0; i < lanes; i++)
                        if (InLane(laneOrigin, laneDir[i], laneShown[i] + .5f, LaneHalfWidth, feet) && !Blocked(Chest(laneOrigin), Chest(feet)))
                        { Hit(player, laneDir[i]); break; }
                }
            }
            if (age >= warn + LaneVolley) End(now);
        }

        /// <summary>每条带画到第一处墙层（车厢、墙、掩体）为止：列车开进来，带子当场被车身截短。</summary>
        private void Cut()
        {
            Vector3 eye = Chest(laneOrigin);
            int mask = GameplayDataSettings.Layers.wallLayerMask.value;
            for (int i = 0; i < lanes; i++)
            {
                RaycastHit hit;
                laneShown[i] = Physics.Raycast(eye, laneDir[i], out hit, laneLength[i], mask, QueryTriggerInteraction.Ignore)
                    ? Mathf.Max(.6f, hit.distance) : laneLength[i];
            }
        }

        /// <summary>点在带内：沿方向 0..<paramref name="length"/>、横向不超过 <paramref name="half"/>（只看水平面）。</summary>
        internal static bool InLane(Vector3 origin, Vector3 direction, float length, float half, Vector3 point)
        {
            Vector3 offset = point - origin;
            offset.y = 0f;
            float along = Vector3.Dot(offset, direction);
            float across = Mathf.Abs(direction.x * offset.z - direction.z * offset.x);
            return along >= 0f && along <= length && across < half;
        }

        private Vector3 Muzzle(Vector3 direction) { return transform.position + Vector3.up * 1.25f + direction * .55f; }

        private void Hit(CharacterMainControl player, Vector3 direction)
        {
            var damage = new DamageInfo(boss);
            damage.damageValue = LaneDamage;
            damage.fromWeaponItemID = 0;
            damage.damagePoint = player.transform.position + Vector3.up;
            damage.damageNormal = -direction;
            player.Health.Hurt(damage);
        }

        // ---------------- 响墩 ----------------
        private bool StartCaps(float now, CharacterMainControl player, Vector3 direction)
        {
            Vector3 feet = player.transform.position, velocity = player.Velocity;
            velocity.y = 0f;
            Vector3 lead = velocity * CapLead;
            if (lead.sqrMagnitude > 3.5f * 3.5f) lead = lead.normalized * 3.5f;
            Vector3 side = new Vector3(-direction.z, 0f, direction.x);
            float lateral = Vector3.Dot(velocity, side);
            float sign = Mathf.Abs(lateral) > .5f ? Mathf.Sign(lateral) : (UnityEngine.Random.value < .5f ? -1f : 1f);
            int want = raged ? CapCountRaged : CapCount;
            capFuse = raged ? CapFuseRaged : CapFuse;
            Vector3 center = feet + lead, hand = transform.position + direction * .35f + Vector3.up * 1.4f;
            caps = 0;
            for (int i = 0; i < want; i++)
            {
                // 两颗：脚下 + 横移那一侧；三颗：以脚下为中横排一线。
                float slot = want == 3 ? i - 1 : i * sign;
                Vector3 target = center + side * (slot * CapSpacing);
                float y;
                if (ground == null || !ground(target, out y)) continue;
                target.y = y;
                Vector3 reach = target - transform.position;
                reach.y = 0f;
                if (reach.sqrMagnitude > (CapReach + 4f) * (CapReach + 4f)) continue;
                if (Physics.CheckSphere(target + Vector3.up * .6f, .3f, GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore)) continue;
                capFrom[caps] = hand; capTo[caps] = target; capLaunch[caps] = now + CapStagger * caps; capBlown[caps] = false;
                caps++;
            }
            if (caps == 0) return false;
            move = Move.Caps; last = Move.Caps; moveStart = now; shake = 0;
            LastShiftSounds.Play(LastShiftSounds.ChiefWhistleLong, gameObject);
            Say(CapLine(barkIndex++), now, false);
            return true;
        }

        private void TickCaps(float now)
        {
            float fuse = capFuse;
            bool pending = false;
            for (int i = 0; i < caps; i++)
            {
                if (capBlown[i]) continue;
                pending = true;
                float t = now - capLaunch[i];
                if (t < 0f) continue;
                if (t < CapFlight)
                {
                    float u = t / CapFlight;
                    fx.Cap(i, Vector3.Lerp(capFrom[i], capTo[i], u) + Vector3.up * (4f * CapApex * u * (1f - u)), false, 0f, now);
                    continue;
                }
                float f = (t - CapFlight) / fuse;
                if (capRing[i] == null)
                {
                    capRing[i] = SkyIslandBossForge.CreateGroundRing(transform.parent, capTo[i]);
                    SkyIslandImpactFx.Puff(transform.parent, capTo[i], .2f, 3);
                }
                if (f < 1f)
                {
                    SkyIslandBossForge.SetRing(capRing[i], CapRadius, f, CapTint);
                    fx.Cap(i, capTo[i], true, f, now);
                    continue;
                }
                capBlown[i] = true;
                fx.HideCap(i);
                SkyIslandBossForge.ReleaseRing(capRing[i]);
                capRing[i] = null;
                // 官方爆炸：墙挡、不伤同队、圈内吃满（口径同天空岛头目与潮闸镇沙老大）；一轮只震第一发。
                SkyIslandBossForge.Detonate(boss, capTo[i], CapRadius, CapDamage, true, shake++ == 0 ? .35f : 0f, CapTint);
            }
            if (!pending) End(now);
        }

        // ---------------- 收招 ----------------
        /// <summary>出招时站定：停掉官方寻路组件并清移动输入（AI 本身照常瞄准、开枪）；收招时还回去。</summary>
        private void Plant(bool on)
        {
            if (on == planted) return;
            planted = on;
            if (path == null) return;
            if (on)
            {
                try { ai.StopMove(); } catch (Exception e) { ModBehaviour.DevLog(LastShiftRaidScene.LogTag + " 押运长站定失败：" + e.Message); }
                path.enabled = false;
                if (boss != null) boss.SetMoveInput(Vector3.zero);
            }
            else path.enabled = true;
        }

        private void End(float now)
        {
            Clean();
            cooldown = now + (raged ? RestRaged : Rest);
        }

        /// <summary>中途作废（押运长没了、玩家跑远、会话结束）：没炸的响墩连圈一起收，不补发。</summary>
        private void Stop(float now)
        {
            if (move == Move.None && !planted) return;
            Clean();
            cooldown = Mathf.Max(cooldown, now + 1.5f);
        }

        private void Clean()
        {
            move = Move.None;
            Plant(false);
            for (int i = 0; i < capRing.Length; i++)
            {
                if (capRing[i] != null) SkyIslandBossForge.ReleaseRing(capRing[i]);
                capRing[i] = null;
            }
            if (fx != null) fx.Clear();
        }

        private static Vector3 Chest(Vector3 feet) { return feet + Vector3.up * 1.1f; }

        private static bool Blocked(Vector3 a, Vector3 b)
        {
            return Physics.Linecast(a, b, GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>起手要看得见：墙层（含车厢）或检修蒸汽（官方 FowBlock 层）挡着都不出招；已锁定的扫射不再看蒸汽。</summary>
        private static bool SightBlocked(Vector3 a, Vector3 b)
        {
            int mask = GameplayDataSettings.Layers.wallLayerMask.value | GameplayDataSettings.Layers.fowBlockLayers.value;
            return Physics.Linecast(a, b, mask, QueryTriggerInteraction.Collide);
        }

        private static string LaneLine(int index)
        {
            switch (index % 3)
            {
                case 0: return L10n.T("封线！", "Line's closed!");
                case 1: return L10n.T("站住！谁放你过线的？", "Stop! Who let you over the line?");
                default: return L10n.T("别动，点名了。", "Hold still. Roll call.");
            }
        }

        private static string CapLine(int index)
        {
            switch (index % 2)
            {
                case 0: return L10n.T("听个响！", "Hear that?");
                default: return L10n.T("压响墩！", "Torpedoes down!");
            }
        }

        /// <summary>喊话气泡：不是每招都喊，至少隔 7 秒（<paramref name="force"/> 时照喊）。</summary>
        private void Say(string text, float now, bool force)
        {
            if (!force && now < nextBark) return;
            nextBark = now + 7f;
            try { DialogueBubblesManager.Show(text, transform, 2.2f, false, false, -1f, 1.2f).Forget(); }
            catch (Exception e) { ModBehaviour.DevLog(LastShiftRaidScene.LogTag + " 押运长喊话气泡失败：" + e.Message); }
        }

        /// <summary>
        /// 专属掉落进尸体库存：押运钢印；玩家身上已带着一枚时换成一件押运私货（Q5 70% / Q6 30%，护具 40% / 武器 30% / 值钱物 30%）。
        /// 官方「路障」自带的尸体掉落照旧。返回基地、销毁不经过死亡事件，重入也只发一次。
        /// </summary>
        private void BeforeLoot(DamageInfo damage)
        {
            if (settled || !subscribed) return;
            settled = true;
            Stop(Time.time);
            if (fx != null) fx.Lamp(false, 0f);
            if (defeated != null) { try { defeated(); } catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 押运长倒下回调失败：" + e.Message); } }
            if (valid == null || !valid()) return;
            SkyIslandImpactFx.DefeatBurst(transform.parent, transform.position, LastShiftChiefFx.Amber);
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

        private void OnDisable()
        {
            Stop(Time.time);
            if (fx != null) fx.Lamp(false, 0f);
        }

        private void OnDestroy()
        {
            if (subscribed && boss != null) boss.BeforeCharacterSpawnLootOnDead -= BeforeLoot;
            subscribed = false;
            Stop(Time.time);
            if (fx != null) { fx.Dispose(); fx = null; }
        }
    }
}
