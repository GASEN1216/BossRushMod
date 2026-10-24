using System;
using System.Collections.Generic;
using BossRush.Utils;
using Duckov.Economy;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 场景内唯一战斗 owner。伤害事件只标记，胜利奖励在后续主线程 Tick 结算。
    /// 战斗不是站桩：远了扑跳砸过来、近了舌击；弹幕是真的飞出去的炮弹（吃够弹药会按新位置补第二波），
    /// 炸弹第二颗预判跑位；隔墙躲两轮它会直接跳过掩体；血越少出手越快，残血扑完接一记舌击。
    /// </summary>
    internal sealed class GreedyMimicEncounter : IDisposable
    {
        internal readonly Scene Scene;
        internal readonly GreedyMimicRules Rules = new GreedyMimicRules();
        private readonly ModBehaviour owner;
        private readonly GreedyMimicVisual visual;
        private readonly Vector3 origin;
        private readonly Vector3[] targets = new Vector3[5];
        private readonly int[] paidTypeIds = new int[3];
        private readonly int[] paidCounts = new int[3];
        private readonly List<GreedyMimicInteractable> interactions = new List<GreedyMimicInteractable>();
        private readonly Vector3[] shellPosition = new Vector3[GreedyMimicVisual.ShellCapacity];
        private readonly Vector3[] shellDirection = new Vector3[GreedyMimicVisual.ShellCapacity];
        private readonly float[] shellTravel = new float[GreedyMimicVisual.ShellCapacity];
        private readonly int[] shellWave = new int[GreedyMimicVisual.ShellCapacity];
        private readonly bool[] shellActive = new bool[GreedyMimicVisual.ShellCapacity];
        private readonly bool[] waveHit = new bool[2];
        private GameObject receiverRoot, interactionRoot;
        private BoxCollider bodyCollider;
        private Rigidbody receiverBody;
        private DamageReceiver receiver;
        private HealthSimpleBase health;
        private bool subscribed, fighting, pendingDeath, settled, disposed, busy, chainLash, hasPlayerSample;
        private float warningLeft, warningDuration, cooldown = 2.5f, flashLeft, recoveryDamage, languageClock, farewell;
        private float leapLeft;
        private Vector3 leapFrom, leapTo, lastPlayer, playerVelocity;
        private int attackTurn, targetCount, volleyWave, blockedTurns;
        private GreedyMimicAttack attack;
        internal bool Finished { get { return disposed || (settled && farewell <= 0f); } }
        internal void SyncHudVisibility() { if (visual != null) visual.SyncHudVisibility(); }
        /// <summary>宝箱当前所在位置；扑跳之后不再等于出生点。</summary>
        private Vector3 Center { get { return visual != null && visual.Root != null ? visual.Root.transform.position : origin; } }
        private float HealthFraction { get { return health != null ? health.HealthValue / Rules.MaximumHealth : 1f; } }

        internal GreedyMimicEncounter(ModBehaviour host, Scene scene, Vector3 position, CharacterMainControl player)
        {
            owner = host; Scene = scene; origin = position;
            try
            {
                visual = new GreedyMimicVisual(scene, position);
                CreateReceiver(); CreateInteractions();
            }
            catch { Dispose(); throw; }
        }

        private void CreateReceiver()
        {
            RemoveReceiver();
            receiverRoot = new GameObject("GreedyMimicDamageBody"); receiverRoot.SetActive(false);
            receiverRoot.transform.SetParent(visual.Facing, false);
            receiverRoot.transform.localPosition = new Vector3(0, 1.35f * Rules.Scale, 0);
            int layer = LayerMask.NameToLayer("DamageReceiver"); if (layer < 0) throw new InvalidOperationException("DamageReceiver 层缺失");
            receiverRoot.layer = layer;
            // 官方爆炸不会给轻量血量的多个 Collider 去重，主箱身和箱盖必须合成一个当帧包络。
            bodyCollider = receiverRoot.AddComponent<BoxCollider>();
            receiverBody = receiverRoot.AddComponent<Rigidbody>(); receiverBody.isKinematic = true; receiverBody.useGravity = false;
            receiver = receiverRoot.AddComponent<DamageReceiver>(); receiver.useSimpleHealth = true;
            receiver.OnHurtEvent = new UnityEvent<DamageInfo>(); receiver.OnDeadEvent = new UnityEvent<DamageInfo>();
            health = receiverRoot.AddComponent<HealthSimpleBase>(); health.team = Teams.wolf;
            health.maxHealthValue = Rules.MaximumHealth; health.damageMultiplierIfNotMainCharacter = 0f; health.dmgReceiver = receiver; receiver.simpleHealth = health;
            health.OnHurtEvent += OnHurt; health.OnDeadEvent += OnDead; subscribed = true;
            // 铁皮箱体：伤害进入官方轻量血量之前，闭嘴时按 ShellArmor 缩小（DamageReceiver.Hurt 前缀消费）。
            ReceiverDamageScaling.Register(receiver, info => visual != null && visual.MouthOpen ? 1f : GreedyMimicRules.ShellArmor);
            SyncReceiverGeometry();
            receiverRoot.SetActive(true); Physics.SyncTransforms();
            CharacterMainControl player = CharacterMainControl.Main;
            Collider playerCollider = player != null ? player.GetComponent<Collider>() : null;
            if (playerCollider != null) Physics.IgnoreCollision(bodyCollider, playerCollider, true);
        }

        private void SyncReceiverGeometry()
        {
            if (receiverBody == null || bodyCollider == null) return;
            Bounds bodyBounds, lidBounds;
            visual.GetDamageBounds(out bodyBounds, out lidBounds);
            bodyBounds.Encapsulate(lidBounds);
            Vector3 receiverOffset = new Vector3(0, 1.35f * Rules.Scale, 0);
            bodyCollider.center = bodyBounds.center - receiverOffset; bodyCollider.size = bodyBounds.size;
            // Rigidbody 的公开姿态接口直接发布给 PhysX，不能等下一次物理步或每帧全局 SyncTransforms。
            receiverBody.position = visual.Facing.TransformPoint(receiverOffset);
            receiverBody.rotation = visual.Facing.rotation;
        }

        private void CreateInteractions()
        {
            interactionRoot = new GameObject("GreedyMimicMenu"); interactionRoot.SetActive(false);
            interactionRoot.transform.SetParent(visual.Root.transform, false);
            SphereCollider sphere = interactionRoot.AddComponent<SphereCollider>(); sphere.radius = 2.6f; sphere.isTrigger = true;
            GreedyMimicInteractable start = interactionRoot.AddComponent<GreedyMimicInteractable>(); start.Bind(this, -1);
            List<InteractableBase> group = NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(start, "[GreedyMimic]");
            interactions.Add(start);
            for (int i = 0; i < 3; i++)
            {
                int action = i;
                GreedyMimicInteractable child = NPCInteractionGroupHelper.AddSubInteractable<GreedyMimicInteractable>(interactionRoot.transform, "Meal" + i, group, value => value.Bind(this, action));
                if (child != null) interactions.Add(child);
            }
            interactionRoot.SetActive(true);
        }

        internal bool CanInteract(int action)
        {
            if (disposed || fighting || pendingDeath || settled || busy || visual == null || visual.Root == null) return false;
            CharacterMainControl player; Scene scene;
            return FieldAdventureSupport.TryGetRaid(owner, out player, out scene) && scene.handle == Scene.handle
                && (player.transform.position - Center).sqrMagnitude <= 49f && (action < 0 || Rules.CanFeed((GreedyMimicFood)action));
        }
        internal void Interact(int action)
        {
            if (!CanInteract(action)) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (action < 0)
            {
                BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
                {
                    Title = L10n.T("叫醒这张大嘴？", "Wake this enormous mouth?"), Target = L10n.T("贪吃宝箱怪", "Gluttonous Mimic"),
                    Body = L10n.T("投喂次数：", "Meals fed: ") + Rules.Meals + "/3\n" + L10n.T("开席后停止投喂。它会扑过来、伸舌头，躲开红色预警，击败它取回加倍的食材与现金。", "Feeding ends when combat starts. It pounces and lashes its tongue; dodge the red warnings and defeat it for double the ingredients and cash."),
                    Warning = L10n.T("也可以开枪叫醒它。战斗后离场或死亡不会归还投喂。", "Shooting also wakes it. Leaving or dying forfeits its meals."),
                    ConfirmLabel = L10n.T("开席！", "Feast!"), CancelLabel = L10n.T("再喂一口", "Feed it first"), Anchor = interactionRoot,
                    OnConfirm = () => { if (CanInteract(-1)) BeginCombat(); }
                });
                return;
            }
            GreedyMimicFood food = (GreedyMimicFood)action; int slot;
            ItemStatsSystem.Item item = GreedyMimicInventory.Find(player, food, out slot);
            if (item == null)
            {
                FieldAdventureSupport.Notify("背包没有可投喂的食材：只收品质 1–3 的普通物品，跳过锁定、愿望清单和使用过的药品。弹药须同一堆至少 12 发。", "No eligible meal: ordinary quality 1–3 items only. Locked slots, wishlisted items and used medicine are excluded. Ammo needs a stack of 12.");
                return;
            }
            int capturedSlot = slot;
            string effect = action == 0 ? L10n.T("长出四根炮管，学会朝预警线齐射炮弹；喂到两口会紧接着补射第二波。", "Grows four cannons and fires shells down telegraphed lanes; a second meal adds a re-aimed follow-up volley.")
                : action == 1 ? L10n.T("背上鼓起炸弹瘤，学会抛出一簇巨型炸弹，其中一颗会预判你的走位。", "Grows bomb sacs and lobs a cluster of giant bombs, one aimed where you are heading.")
                : L10n.T("头顶长出巨型药瓶，学会蓄力回血；蓄力时造成 45 点伤害可打断。", "Grows a giant medicine bottle and learns to heal. Deal 45 damage during its charge to interrupt.");
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("这口会长什么？", "What will this meal grow?"), Target = item.DisplayName + " × " + GreedyMimicRules.Cost(food),
                Body = effect + "\n" + L10n.T("整只宝箱会膨胀，生命增加 240，扑跳落点也更大。击败后掉落本次食材的两倍。", "The entire chest swells, gains 240 health and lands bigger pounces. Defeating it drops twice this meal."),
                Warning = L10n.T("立即消耗上面这件食材。第三口咽下后会自动开战。", "Consumes the shown ingredient now. Its third meal starts combat automatically."),
                ConfirmLabel = L10n.T("喂给它", "Feed it"), CancelLabel = L10n.T("收回手", "Keep my hand"), Anchor = interactionRoot,
                OnConfirm = () => Feed(item, capturedSlot, food)
            });
        }

        private void Feed(ItemStatsSystem.Item item, int slot, GreedyMimicFood food)
        {
            if (!CanInteract((int)food)) return;
            busy = true;
            try
            {
                int typeId = item != null ? item.TypeID : 0;
                int meal = Rules.Meals;
                bool growthStarted = false;
                if (!GreedyMimicInventory.Consume(item, CharacterMainControl.Main, slot, food, () =>
                {
                    growthStarted = true;
                    Rules.Feed(food); paidTypeIds[meal] = typeId; paidCounts[meal] = GreedyMimicRules.Cost(food) * 2;
                    visual.Grow(Rules); CreateReceiver();
                }))
                { if (growthStarted) Dispose(); return; }
                if (Rules.Meals == GreedyMimicRules.MaximumMeals) BeginCombat();
                else FieldAdventureSupport.Notify("咕咚！宝箱又长大一圈。还能继续投喂，或选择开席。", "GULP! The chest swells again. Feed it more, or start the feast.");
            }
            finally { busy = false; }
        }

        private void BeginCombat()
        {
            if (fighting || disposed || pendingDeath || settled) return;
            fighting = true; cooldown = 1.6f;
            if (interactionRoot != null) interactionRoot.SetActive(false);
            FieldAdventureSupport.Notify("开席！它把你也写进了菜单。铁皮箱子很硬，趁它张嘴蓄力、吞咽或扑跳时打要害。",
                "FEAST! You have been added to the menu. Its iron shell shrugs off hits; strike while its mouth is open to charge, swallow or pounce.");
        }
        private void OnHurt(DamageInfo damage)
        {
            if (disposed || settled || damage.damageValue <= 0f || damage.fromCharacter == null || !damage.fromCharacter.IsMainCharacter) return;
            // HealthSimpleBase 已完成暴击判定与扣血；事件的 damageValue 仍是原值，crit 才是本次实际结果。
            float dealt = damage.damageValue * (damage.crit != 0 ? damage.critDamageFactor : 1f);
            if (dealt <= 0f) return;
            if (!fighting) BeginCombat();
            if (attack == GreedyMimicAttack.Recovery && warningLeft > 0f) recoveryDamage += dealt;
        }
        private void OnDead(DamageInfo damage) { if (!disposed && !settled) pendingDeath = true; }

        internal void Tick(float dt, CharacterMainControl player)
        {
            if (disposed || player == null || player.Health == null || player.Health.IsDead) { Dispose(); return; }
            if (pendingDeath && !settled) SettleVictory();
            if (settled)
            {
                farewell -= dt; visual.Tick(dt, player.transform.position, false, 0, 0, attack); return;
            }
            languageClock -= dt;
            if (languageClock <= 0f) { languageClock = 0.75f; for (int i = 0; i < interactions.Count; i++) if (interactions[i] != null) interactions[i].RefreshName(); }
            SamplePlayer(dt, player.transform.position);
            if (leapLeft > 0f) TickLeap(dt, player);
            visual.Tick(dt, player.transform.position, fighting, health != null ? health.HealthValue / Rules.MaximumHealth : 1, warningLeft, attack);
            SyncReceiverGeometry();
            TickShells(dt, player);
            if (!fighting) return;
            if (leapLeft > 0f) return;
            if ((player.transform.position - Center).sqrMagnitude > 45f * 45f) { warningLeft = 0; volleyWave = 0; chainLash = false; visual.ClearAttack(); cooldown = 2f; return; }
            if (flashLeft > 0f) { flashLeft -= dt; if (flashLeft <= 0f) visual.ClearAttack(); return; }
            if (warningLeft > 0f)
            {
                warningLeft = Mathf.Max(0, warningLeft - dt);
                if (attack == GreedyMimicAttack.Bombs)
                    for (int i = 0; i < targetCount; i++) visual.AnimateBomb(i, Center + Vector3.up * 2.5f, targets[i], 1f - warningLeft / warningDuration);
                if (attack == GreedyMimicAttack.Recovery && GreedyMimicRules.Interrupted(recoveryDamage))
                {
                    warningLeft = 0; cooldown = 2.6f; visual.ClearAttack(); visual.Burst(Center + Vector3.up * 3, 0.8f);
                    FieldAdventureSupport.Notify("药瓶噎住了！回血被打断。", "The bottle jams! Healing interrupted."); return;
                }
                if (warningLeft <= 0f) ResolveAttack(player);
                return;
            }
            if (chainLash) { chainLash = false; BeginTongue(player.transform.position, 0.55f); return; }
            cooldown -= dt;
            if (cooldown <= 0f) BeginAttack(player);
        }

        private void SamplePlayer(float dt, Vector3 position)
        {
            if (hasPlayerSample && dt > 0f)
            {
                Vector3 velocity = (position - lastPlayer) * (1f / dt);
                velocity.y = 0f;
                if (velocity.sqrMagnitude > 81f) velocity = Vector3.zero;
                playerVelocity = playerVelocity * 0.7f + velocity * 0.3f;
            }
            lastPlayer = position; hasPlayerSample = true;
        }

        private void BeginAttack(CharacterMainControl player)
        {
            Vector3 target = player.transform.position;
            if (!Unblocked(Center, target))
            {
                // 躲在掩体后面不是安全区：第二轮起直接跳过去。
                if (++blockedTurns >= 2 && TryBeginPounce(target)) return;
                cooldown = 0.8f; return;
            }
            blockedTurns = 0;
            attack = Rules.AttackAt(attackTurn++); recoveryDamage = 0;
            visual.ClearAttack();
            if (attack == GreedyMimicAttack.Bite)
            {
                if (PlanarDistance(Center, target) <= GreedyMimicRules.TongueRange) BeginTongue(target, GreedyMimicRules.WarningSeconds(GreedyMimicAttack.Bite));
                else if (!TryBeginPounce(target)) { attack = GreedyMimicAttack.Bite; cooldown = 0.6f; }
                return;
            }
            warningLeft = warningDuration = GreedyMimicRules.WarningSeconds(attack);
            if (attack == GreedyMimicAttack.Recovery)
            {
                targetCount = 0; visual.ShowCircle(0, Center, 3.5f, true);
                FieldAdventureSupport.Notify("巨型药瓶正在蓄力！攻击宝箱可以打断。", "The giant bottle is charging! Hit the chest to interrupt.");
            }
            else if (attack == GreedyMimicAttack.Volley)
            {
                volleyWave = 0;
                AimVolley(target);
            }
            else
            {
                targetCount = 2 + Rules.Level(GreedyMimicFood.Explosive);
                Vector3 lead = playerVelocity * (warningDuration * 0.8f);
                if (lead.sqrMagnitude > 49f) lead = lead.normalized * 7f;
                for (int i = 0; i < targetCount; i++)
                {
                    // 第一颗砸脚下，第二颗砸你要去的地方，其余散在四周封走位。
                    Vector3 offset = i == 0 ? Vector3.zero
                        : i == 1 && lead.sqrMagnitude > 1f ? lead
                        : Quaternion.Euler(0, i * 137.5f, 0) * Vector3.forward * 4.8f;
                    targets[i] = target + offset;
                    visual.ShowCircle(i, targets[i], 2.8f, false);
                }
            }
        }

        private void AimVolley(Vector3 target)
        {
            targetCount = 5; Vector3 direction = target - Center; direction.y = 0; direction.Normalize();
            for (int i = 0; i < targetCount; i++)
            { targets[i] = Center + Quaternion.Euler(0, (i - 2) * 12, 0) * direction * GreedyMimicRules.ShellRange; visual.ShowLane(i, Center, targets[i]); }
        }

        private void BeginTongue(Vector3 target, float warning)
        {
            attack = GreedyMimicAttack.Bite; targetCount = 1; targets[0] = target;
            warningLeft = warningDuration = warning;
            visual.ClearAttack();
            visual.ShowLane(0, Center, target);
        }

        private bool TryBeginPounce(Vector3 target)
        {
            Vector3 landing;
            if (!GreedyMimicTerrain.TryLanding(Center, target, 1.1f * Rules.Scale, GreedyMimicRules.MaximumLeap, out landing)) return false;
            attack = GreedyMimicAttack.Pounce; targetCount = 1; targets[0] = landing; blockedTurns = 0;
            warningLeft = warningDuration = GreedyMimicRules.WarningSeconds(GreedyMimicAttack.Pounce);
            visual.ClearAttack();
            visual.ShowCircle(0, landing, Rules.PounceRadius, false);
            return true;
        }

        private bool Unblocked(Vector3 source, Vector3 target)
        {
            int mask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask | Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
            return !Physics.Linecast(source + Vector3.up * 0.65f, target + Vector3.up * 0.65f, mask, QueryTriggerInteraction.Ignore);
        }
        private void ResolveAttack(CharacterMainControl player)
        {
            cooldown = GreedyMimicRules.Cooldown(HealthFraction); flashLeft = 0.4f;
            Vector3 center = Center;
            if (attack == GreedyMimicAttack.Recovery)
            {
                float heal = Rules.Recovery(health.HealthValue);
                if (heal > 0)
                {
                    // 官方轻量血量只有 Hurt；负伤害由同一公开事件管线增加血量。归属主玩家避免非主玩家乘数 0 吃掉回血。
                    DamageInfo recovery = new DamageInfo(player); recovery.damageValue = -heal; recovery.isFromBuffOrEffect = true;
                    receiver.Hurt(recovery);
                }
                visual.Heal(center + Vector3.up * 0.2f); return;
            }
            if (attack == GreedyMimicAttack.Pounce)
            {
                // 起跳：落地时才结算伤害，空中这段不判定。
                flashLeft = 0f; leapFrom = center; leapTo = targets[0]; leapLeft = GreedyMimicRules.LeapSeconds;
                visual.ClearAttack(); visual.ShowCircle(0, leapTo, Rules.PounceRadius, false);
                return;
            }
            if (attack == GreedyMimicAttack.Volley)
            {
                FireVolley(volleyWave);
                visual.Muzzle();
                if (volleyWave + 1 < Rules.VolleyWaves)
                {
                    // 第二波：短促重新瞄准你此刻的位置，站在第一波缝里不安全。
                    volleyWave++; flashLeft = 0f;
                    warningLeft = warningDuration = 0.6f;
                    visual.ClearAttack(); AimVolley(player.transform.position);
                }
                else volleyWave = 0;
                return;
            }
            Vector3 p = player.transform.position; bool hit = false;
            for (int i = 0; i < targetCount; i++)
            {
                if (attack == GreedyMimicAttack.Bite)
                    hit |= GreedyMimicRules.DistanceToSegmentSquared(p.x, p.z, center.x, center.z, targets[i].x, targets[i].z)
                        <= GreedyMimicRules.TongueHalfWidth * GreedyMimicRules.TongueHalfWidth && Mathf.Abs(p.y - center.y) < 2.5f && Unblocked(center, p);
                else
                    hit |= GreedyMimicRules.InCircle(p.x - targets[i].x, p.z - targets[i].z, 2.8f) && Mathf.Abs(p.y - targets[i].y) < 2.5f
                        && Unblocked(targets[i], p);
            }
            if (attack == GreedyMimicAttack.Bite) visual.StrikeTongue(targets[0]);
            else for (int i = 0; i < targetCount; i++) visual.Explode(targets[i], 2.8f);
            // 一轮弹幕/重叠爆炸最多结算一次，避免多圈重叠把预告伤害乘数倍。
            if (hit) HurtPlayer(player, Rules.Damage(attack), attack == GreedyMimicAttack.Bombs);
        }

        private void TickLeap(float dt, CharacterMainControl player)
        {
            leapLeft = Mathf.Max(0f, leapLeft - dt);
            float t = 1f - leapLeft / GreedyMimicRules.LeapSeconds;
            Vector3 position = Vector3.Lerp(leapFrom, leapTo, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * (2.2f + Rules.Scale));
            visual.Root.transform.position = position;
            Vector3 heading = leapTo - leapFrom; heading.y = 0f;
            if (heading.sqrMagnitude > 0.01f) visual.Facing.rotation = Quaternion.LookRotation(heading);
            visual.SetLeap(leapLeft > 0f ? t : -1f);
            if (leapLeft > 0f) return;
            visual.Root.transform.position = leapTo;
            visual.ClearAttack();
            float radius = Rules.PounceRadius;
            visual.Slam(leapTo, radius);
            Vector3 p = player.transform.position;
            if (GreedyMimicRules.InCircle(p.x - leapTo.x, p.z - leapTo.z, radius) && Mathf.Abs(p.y - leapTo.y) < 2.5f)
                HurtPlayer(player, Rules.Damage(GreedyMimicAttack.Pounce), false);
            cooldown = GreedyMimicRules.Cooldown(HealthFraction);
            // 残血时落地接一记舌击，不给喘息。
            chainLash = GreedyMimicRules.Enraged(HealthFraction) && PlanarDistance(leapTo, p) <= GreedyMimicRules.TongueRange;
        }

        private void FireVolley(int wave)
        {
            Vector3 muzzle = Center + Vector3.up * (1.5f * Rules.Scale);
            waveHit[wave & 1] = false;
            for (int i = 0; i < targetCount; i++)
            {
                int slot = FreeShell();
                if (slot < 0) return;
                Vector3 direction = targets[i] - Center; direction.y = 0f; direction.Normalize();
                shellPosition[slot] = muzzle; shellDirection[slot] = direction; shellTravel[slot] = 0f;
                shellWave[slot] = wave & 1; shellActive[slot] = true;
                visual.SetShell(slot, muzzle, true);
            }
        }

        private int FreeShell()
        {
            for (int i = 0; i < shellActive.Length; i++) if (!shellActive[i]) return i;
            return -1;
        }

        /// <summary>炮弹按真实速度飞：撞墙即碎；擦过玩家 0.8 m 内造成伤害，同一波最多结算一次。</summary>
        private void TickShells(float dt, CharacterMainControl player)
        {
            if (dt <= 0f) return;
            Vector3 p = player.transform.position;
            for (int i = 0; i < shellActive.Length; i++)
            {
                if (!shellActive[i]) continue;
                float step = GreedyMimicRules.ShellSpeed * dt;
                Vector3 from = shellPosition[i], to = from + shellDirection[i] * step;
                bool stop = false;
                if (!waveHit[shellWave[i]] && Mathf.Abs(p.y - (from.y - 1.2f)) < 2.5f
                    && GreedyMimicRules.DistanceToSegmentSquared(p.x, p.z, from.x, from.z, to.x, to.z)
                        <= GreedyMimicRules.ShellHalfWidth * GreedyMimicRules.ShellHalfWidth)
                {
                    waveHit[shellWave[i]] = true; stop = true;
                    HurtPlayer(player, Rules.Damage(GreedyMimicAttack.Volley), false);
                }
                else if (GreedyMimicTerrain.SegmentBlocked(from, to)) stop = true;
                shellTravel[i] += step;
                if (!stop && shellTravel[i] >= GreedyMimicRules.ShellRange) stop = true;
                shellPosition[i] = to;
                if (stop) { shellActive[i] = false; visual.ShellPop(to); }
                visual.SetShell(i, to, !stop);
            }
        }

        private void ClearShells()
        {
            for (int i = 0; i < shellActive.Length; i++)
            {
                if (shellActive[i] && visual != null) visual.SetShell(i, shellPosition[i], false);
                shellActive[i] = false;
            }
        }

        private static void HurtPlayer(CharacterMainControl player, float amount, bool explosion)
        {
            Vector3 p = player.transform.position;
            DamageInfo damage = new DamageInfo(null); damage.damageValue = amount; damage.damagePoint = p + Vector3.up;
            damage.damageNormal = Vector3.up; damage.isFromBuffOrEffect = true; damage.ignoreDifficulty = true;
            damage.isExplosion = explosion; player.Health.Hurt(damage);
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private void SettleVictory()
        {
            // 空中被打死：先落回锁定落点再崩开，不让箱子悬在半空。
            if (leapLeft > 0f && visual != null && visual.Root != null) { visual.Root.transform.position = leapTo; visual.SetLeap(-1f); }
            Vector3 center = Center;
            settled = true; farewell = 2f; warningLeft = flashLeft = leapLeft = 0f; chainLash = false; RemoveReceiver();
            ClearShells();
            if (interactionRoot != null) interactionRoot.SetActive(false);
            visual.Defeated();
            // 先封口再投递，每项最多一份实例；任何外部通知重入都不会再次结算。
            try { GreedyMimicInventory.Reward(EconomyManager.CashItemID, 600 + Rules.Meals * 400, center); }
            catch (Exception e) { Debug.LogWarning("[GreedyMimic] 现金掉落失败: " + e.Message); }
            for (int i = 0; i < Rules.Meals; i++)
            {
                int copies = paidCounts[i] == 2 ? 2 : 1;
                for (int copy = 0; copy < copies; copy++)
                    try { GreedyMimicInventory.Reward(paidTypeIds[i], copies == 1 ? paidCounts[i] : 1, center + new Vector3((i - 1) * 0.8f, 0, 0.6f + copy * 0.45f)); }
                    catch (Exception e) { Debug.LogWarning("[GreedyMimic] 食材掉落失败: " + e.Message); }
            }
            FieldAdventureSupport.Notify("撑爆了！双份食材和现金从它的大嘴里喷了出来。", "BURP! Double ingredients and cash spill from its enormous mouth.");
        }
        private void RemoveReceiver()
        {
            if (subscribed && health != null) { health.OnHurtEvent -= OnHurt; health.OnDeadEvent -= OnDead; }
            ReceiverDamageScaling.Unregister(receiver);
            subscribed = false; health = null; receiver = null;
            bodyCollider = null; receiverBody = null;
            if (receiverRoot != null) { receiverRoot.SetActive(false); UnityEngine.Object.Destroy(receiverRoot); }
            receiverRoot = null;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; RemoveReceiver();
            ClearShells();
            if (interactionRoot != null) interactionRoot.SetActive(false);
            interactions.Clear(); if (visual != null) visual.Dispose();
        }
    }
}
