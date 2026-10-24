using System;
using System.Collections.Generic;
using BossRush.Utils;
using Duckov.Economy;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>场景内唯一战斗 owner。伤害事件只标记，胜利奖励在后续主线程 Tick 结算。</summary>
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
        private GameObject receiverRoot, interactionRoot;
        private BoxCollider bodyCollider;
        private Rigidbody receiverBody;
        private DamageReceiver receiver;
        private HealthSimpleBase health;
        private bool subscribed, fighting, pendingDeath, settled, disposed, busy;
        private float warningLeft, warningDuration, cooldown = 2.5f, flashLeft, recoveryDamage, languageClock, farewell;
        private int attackTurn, targetCount;
        private GreedyMimicAttack attack;
        internal bool Finished { get { return disposed || (settled && farewell <= 0f); } }
        internal void SyncHudVisibility() { if (visual != null) visual.SyncHudVisibility(); }

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
                && (player.transform.position - origin).sqrMagnitude <= 49f && (action < 0 || Rules.CanFeed((GreedyMimicFood)action));
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
                    Body = L10n.T("投喂次数：", "Meals fed: ") + Rules.Meals + "/3\n" + L10n.T("开席后停止投喂。躲开红色预警，击败它取回加倍的食材与现金。", "Feeding ends when combat starts. Dodge red warnings and defeat it for double the ingredients and cash."),
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
            string effect = action == 0 ? L10n.T("长出四根炮管，学会预警扇形弹幕。", "Grows four cannons and learns a telegraphed fan volley.")
                : action == 1 ? L10n.T("背上鼓起炸弹瘤，学会抛出一簇巨型炸弹。", "Grows bomb sacs and learns to lob a cluster of giant bombs.")
                : L10n.T("头顶长出巨型药瓶，学会蓄力回血；蓄力时造成 45 点伤害可打断。", "Grows a giant medicine bottle and learns to heal. Deal 45 damage during its charge to interrupt.");
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("这口会长什么？", "What will this meal grow?"), Target = item.DisplayName + " × " + GreedyMimicRules.Cost(food),
                Body = effect + "\n" + L10n.T("整只宝箱会膨胀，生命增加 240。击败后掉落本次食材的两倍。", "The entire chest swells and gains 240 health. Defeating it drops twice this meal."),
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
            fighting = true; cooldown = 2.6f;
            if (interactionRoot != null) interactionRoot.SetActive(false);
            FieldAdventureSupport.Notify("开席！它把你也写进了菜单。", "FEAST! You have been added to the menu.");
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
            visual.Tick(dt, player.transform.position, fighting, health != null ? health.HealthValue / Rules.MaximumHealth : 1, warningLeft, attack);
            SyncReceiverGeometry();
            if (!fighting) return;
            if ((player.transform.position - origin).sqrMagnitude > 45f * 45f) { warningLeft = 0; visual.ClearAttack(); cooldown = 2f; return; }
            if (flashLeft > 0f) { flashLeft -= dt; if (flashLeft <= 0f) visual.ClearAttack(); return; }
            if (warningLeft > 0f)
            {
                warningLeft = Mathf.Max(0, warningLeft - dt);
                if (attack == GreedyMimicAttack.Bombs)
                    for (int i = 0; i < targetCount; i++) visual.AnimateBomb(i, origin + Vector3.up * 2.5f, targets[i], 1f - warningLeft / warningDuration);
                if (attack == GreedyMimicAttack.Recovery && GreedyMimicRules.Interrupted(recoveryDamage))
                {
                    warningLeft = 0; cooldown = 2.6f; visual.ClearAttack(); visual.Burst(origin + Vector3.up * 3, 0.8f);
                    FieldAdventureSupport.Notify("药瓶噎住了！回血被打断。", "The bottle jams! Healing interrupted."); return;
                }
                if (warningLeft <= 0f) ResolveAttack(player);
                return;
            }
            cooldown -= dt;
            if (cooldown <= 0f) BeginAttack(player);
        }

        private void BeginAttack(CharacterMainControl player)
        {
            if (!Unblocked(origin, player.transform.position)) { cooldown = 1f; return; }
            attack = Rules.AttackAt(attackTurn++); warningLeft = warningDuration = GreedyMimicRules.WarningSeconds(attack); recoveryDamage = 0;
            visual.ClearAttack(); Vector3 target = player.transform.position;
            if (attack == GreedyMimicAttack.Recovery)
            {
                targetCount = 0; visual.ShowCircle(0, origin, 3.5f, true);
                FieldAdventureSupport.Notify("巨型药瓶正在蓄力！攻击宝箱可以打断。", "The giant bottle is charging! Hit the chest to interrupt.");
            }
            else if (attack == GreedyMimicAttack.Volley)
            {
                targetCount = 5; Vector3 direction = target - origin; direction.y = 0; direction.Normalize();
                for (int i = 0; i < targetCount; i++)
                { targets[i] = origin + Quaternion.Euler(0, (i - 2) * 12, 0) * direction * 32; visual.ShowLane(i, origin, targets[i]); }
            }
            else
            {
                targetCount = attack == GreedyMimicAttack.Bombs ? 2 + Rules.Level(GreedyMimicFood.Explosive) : 1;
                for (int i = 0; i < targetCount; i++)
                {
                    Vector3 offset = i == 0 ? Vector3.zero : Quaternion.Euler(0, i * 137.5f, 0) * Vector3.forward * 4.8f;
                    targets[i] = target + offset;
                    visual.ShowCircle(i, targets[i], attack == GreedyMimicAttack.Bombs ? 2.8f : 2.4f, false);
                }
            }
        }
        private bool Unblocked(Vector3 source, Vector3 target)
        {
            int mask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask | Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
            return !Physics.Linecast(source + Vector3.up * 0.65f, target + Vector3.up * 0.65f, mask, QueryTriggerInteraction.Ignore);
        }
        private void ResolveAttack(CharacterMainControl player)
        {
            cooldown = 2.2f; flashLeft = 0.4f;
            if (attack == GreedyMimicAttack.Recovery)
            {
                float heal = Rules.Recovery(health.HealthValue);
                if (heal > 0)
                {
                    // 官方轻量血量只有 Hurt；负伤害由同一公开事件管线增加血量。归属主玩家避免非主玩家乘数 0 吃掉回血。
                    DamageInfo recovery = new DamageInfo(player); recovery.damageValue = -heal; recovery.isFromBuffOrEffect = true;
                    receiver.Hurt(recovery);
                }
                visual.Burst(origin + Vector3.up * 3, 0.75f); return;
            }
            Vector3 p = player.transform.position; bool hit = false;
            for (int i = 0; i < targetCount; i++)
            {
                if (attack == GreedyMimicAttack.Volley)
                    hit |= GreedyMimicRules.DistanceToSegmentSquared(p.x, p.z, origin.x, origin.z, targets[i].x, targets[i].z) <= 0.75f * 0.75f && Mathf.Abs(p.y - origin.y) < 2.5f && Unblocked(origin, p);
                else
                    hit |= GreedyMimicRules.InCircle(p.x - targets[i].x, p.z - targets[i].z, attack == GreedyMimicAttack.Bombs ? 2.8f : 2.4f) && Mathf.Abs(p.y - targets[i].y) < 2.5f
                        && Unblocked(attack == GreedyMimicAttack.Bite ? origin : targets[i], p);
            }
            if (attack == GreedyMimicAttack.Bite) visual.StrikeTongue(targets[0]);
            visual.Burst(attack == GreedyMimicAttack.Volley ? origin + Vector3.up * 2 : targets[0] + Vector3.up * 0.3f, 0.7f);
            // 一轮弹幕/重叠爆炸最多结算一次，避免多圈重叠把预告伤害乘数倍。
            if (hit)
            {
                DamageInfo damage = new DamageInfo(null); damage.damageValue = Rules.Damage(attack); damage.damagePoint = p + Vector3.up;
                damage.damageNormal = (p - origin).normalized; damage.isFromBuffOrEffect = true; damage.ignoreDifficulty = true;
                damage.isExplosion = attack == GreedyMimicAttack.Bombs; player.Health.Hurt(damage);
            }
        }
        private void SettleVictory()
        {
            settled = true; farewell = 2f; warningLeft = flashLeft = 0f; RemoveReceiver();
            if (interactionRoot != null) interactionRoot.SetActive(false);
            visual.Defeated();
            // 先封口再投递，每项最多一份实例；任何外部通知重入都不会再次结算。
            try { GreedyMimicInventory.Reward(EconomyManager.CashItemID, 600 + Rules.Meals * 400, origin); }
            catch (Exception e) { Debug.LogWarning("[GreedyMimic] 现金掉落失败: " + e.Message); }
            for (int i = 0; i < Rules.Meals; i++)
            {
                int copies = paidCounts[i] == 2 ? 2 : 1;
                for (int copy = 0; copy < copies; copy++)
                    try { GreedyMimicInventory.Reward(paidTypeIds[i], copies == 1 ? paidCounts[i] : 1, origin + new Vector3((i - 1) * 0.8f, 0, 0.6f + copy * 0.45f)); }
                    catch (Exception e) { Debug.LogWarning("[GreedyMimic] 食材掉落失败: " + e.Message); }
            }
            FieldAdventureSupport.Notify("撑爆了！双份食材和现金从它的大嘴里喷了出来。", "BURP! Double ingredients and cash spill from its enormous mouth.");
        }
        private void RemoveReceiver()
        {
            if (subscribed && health != null) { health.OnHurtEvent -= OnHurt; health.OnDeadEvent -= OnDead; }
            subscribed = false; health = null; receiver = null;
            bodyCollider = null; receiverBody = null;
            if (receiverRoot != null) { receiverRoot.SetActive(false); UnityEngine.Object.Destroy(receiverRoot); }
            receiverRoot = null;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; RemoveReceiver();
            if (interactionRoot != null) interactionRoot.SetActive(false);
            interactions.Clear(); if (visual != null) visual.Dispose();
        }
    }
}
