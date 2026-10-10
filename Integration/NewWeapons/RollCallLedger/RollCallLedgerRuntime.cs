using System;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>教导主任的点名册：装备事件绑定 owner，名单存在时才创建短命逐帧驱动。</summary>
    internal static class RollCallLedgerRuntime
    {
        private static readonly RollCallLedgerRules rules = new RollCallLedgerRules(
            RollCallLedgerConfig.TargetCount, RollCallLedgerConfig.WindowSeconds, RollCallLedgerConfig.CooldownSeconds);
        private static readonly Health[] markedTargets = new Health[RollCallLedgerConfig.TargetCount];
        private static bool isSubscribed;
        private static bool hurtSubscribed;
        private static bool levelInitializing;
        private static CharacterMainControl contextPlayer;
        private static CharacterMainControl owner;
        private static RollCallLedgerDriver driver;
        private static Health[] pendingTargets;
        private static RollCallLedgerEntry[] pendingEntries;
        private static int pendingFrame;
        private static int generation;

        internal static int CurrentMarkCount { get { return rules.Count; } }
        internal static bool HasPendingSettlement { get { return pendingTargets != null; } }
        internal static float NextTriggerAt { get { return rules.NextTriggerAt; } }

        internal static void Subscribe()
        {
            if (isSubscribed) return;
            isSubscribed = true;
            levelInitializing = false;
            CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += OnHoldChanged;
            CharacterMainControl.OnMainCharacterSlotContentChangedEvent += OnSlotChanged;
            Health.OnDead += OnAnyDead;
            SceneLoader.onStartedLoadingScene += OnSceneLoadingStarted;
            SceneLoader.onFinishedLoadingScene += OnSceneLoadingFinished;
            LevelManager.OnLevelBeginInitializing += OnLevelBeginInitializing;
            LevelManager.OnAfterLevelInitialized += OnAfterLevelInitialized;
            RefreshContext();
        }

        internal static void Unsubscribe()
        {
            if (isSubscribed)
            {
                isSubscribed = false;
                CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= OnHoldChanged;
                CharacterMainControl.OnMainCharacterSlotContentChangedEvent -= OnSlotChanged;
                Health.OnDead -= OnAnyDead;
                SceneLoader.onStartedLoadingScene -= OnSceneLoadingStarted;
                SceneLoader.onFinishedLoadingScene -= OnSceneLoadingFinished;
                LevelManager.OnLevelBeginInitializing -= OnLevelBeginInitializing;
                LevelManager.OnAfterLevelInitialized -= OnAfterLevelInitialized;
            }
            ResetStaticCaches();
        }

        internal static void ResetStaticCaches()
        {
            Deactivate();
            rules.ResetAll();
            contextPlayer = null;
        }

        /// <summary>宿主场景接线可能晚于官方初始化事件；清旧名单后立即重新绑定已佩戴的主角。</summary>
        internal static void SetupForScene()
        {
            ResetStaticCaches();
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }

        private static bool IsUsableOwner(CharacterMainControl player)
        {
            return isSubscribed && !levelInitializing && !SceneLoader.IsSceneLoading
                && player != null && player.IsMainCharacter && player.Health != null && !player.Health.IsDead
                && LevelManager.Instance != null && !LevelManager.Instance.IsBaseLevel
                && NewWeaponEquipState.IsTotemEquipped(BossRushItemIds.RollCallLedger);
        }

        private static void RefreshContext()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                // 即使旧玩家已经卸下装备，也不能把旧冷却带到替换后的主角身上。
                if (!ReferenceEquals(contextPlayer, player))
                {
                    Deactivate();
                    rules.ResetAll();
                    contextPlayer = player;
                }
                if (!IsUsableOwner(player)) { Deactivate(); return; }
                owner = player;
                if (!hurtSubscribed)
                {
                    Health.OnHurt += OnHurt;
                    hurtSubscribed = true;
                }
            }
            catch (Exception e)
            {
                Deactivate();
                ModBehaviour.DevLog(RollCallLedgerConfig.LogPrefix + " 装备绑定失败: " + e.Message);
            }
        }

        private static void Deactivate()
        {
            if (hurtSubscribed)
            {
                Health.OnHurt -= OnHurt;
                hurtSubscribed = false;
            }
            owner = null;
            generation++;
            pendingTargets = null;
            pendingEntries = null;
            pendingFrame = 0;
            ClearMarks();
            rules.ResetSequence();
            DestroyDriver();
        }

        private static void OnSlotChanged(CharacterMainControl player, Slot slot)
        {
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }

        private static void OnHoldChanged(CharacterMainControl player, DuckovItemAgent agent)
        {
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }

        private static void OnAnyDead(Health health, DamageInfo info)
        {
            if (health != null && health.IsMainCharacterHealth) ResetStaticCaches();
        }

        private static void OnSceneLoadingStarted(SceneLoadingContext context) { ResetStaticCaches(); }

        private static void OnSceneLoadingFinished(SceneLoadingContext context)
        {
            // LevelInitialized 可能先于加载遮罩完成；完成事件解除 IsSceneLoading 门后再补绑定。
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }

        private static void OnLevelBeginInitializing()
        {
            levelInitializing = true;
            ResetStaticCaches();
        }

        private static void OnAfterLevelInitialized()
        {
            levelInitializing = false;
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }

        private static bool IsEnemy(Health target, CharacterMainControl player, bool requireAlive)
        {
            if (target == null || target.IsMainCharacterHealth || (requireAlive && target.IsDead)) return false;
            if (PetNestCompanionAgent.IsCompanionHealth(target)) return false;
            CharacterMainControl character = target.TryGetCharacter();
            return character != null && !ReferenceEquals(character, player) && Team.IsEnemy(player.Team, character.Team);
        }

        private static void OnHurt(Health target, DamageInfo info)
        {
            // 第一层是 O(1) 归因；召唤物、持续伤害和本装备追加伤害都不能开启第二轮点名。
            if (info.isFromBuffOrEffect || info.fromCharacter == null
                || !ReferenceEquals(info.fromCharacter, CharacterMainControl.Main)) return;
            try
            {
                RefreshContext();
                if (owner == null || BossRushUI.IsGamePaused() || !IsEnemy(target, owner, false)) return;
                // 官方致死顺序为 OnDead -> OnHurt；允许本击刚死亡者报名，结算时再排除死者。
                if (rules.Expire(Time.time)) ClearMarks();
                int ordinal;
                RollCallLedgerEntry[] completed;
                if (!rules.ObserveHit(target.GetInstanceID(), info.finalDamage, Time.time, out ordinal, out completed)) return;
                markedTargets[ordinal - 1] = target;
                EnsureDriver();
                RollCallLedgerFx.ShowMark(target, ordinal);
                if (completed == null) return;

                pendingTargets = (Health[])markedTargets.Clone();
                pendingEntries = completed;
                pendingFrame = Time.frameCount;
                ClearMarks();
            }
            catch (Exception e)
            {
                Deactivate();
                ModBehaviour.DevLog(RollCallLedgerConfig.LogPrefix + " 点名事件失败: " + e.Message);
            }
        }

        private static void EnsureDriver()
        {
            if (driver != null) return;
            driver = new GameObject("RollCallLedger_Runtime").AddComponent<RollCallLedgerDriver>();
        }

        internal static void Tick(RollCallLedgerDriver source)
        {
            if (!ReferenceEquals(source, driver)) return;
            try
            {
                if (!ReferenceEquals(owner, CharacterMainControl.Main)) { ResetStaticCaches(); return; }
                if (!IsUsableOwner(owner)) { Deactivate(); return; }
                if (BossRushUI.IsGamePaused()) return;
                if (pendingTargets != null && Time.frameCount > pendingFrame) Settle();
                if (rules.Expire(Time.time)) ClearMarks();
                if (rules.Count == 0 && pendingTargets == null) DestroyDriver();
            }
            catch (Exception e)
            {
                Deactivate();
                ModBehaviour.DevLog(RollCallLedgerConfig.LogPrefix + " 点名结算失败: " + e.Message);
            }
        }

        private static void Settle()
        {
            Health[] targets = pendingTargets;
            RollCallLedgerEntry[] entries = pendingEntries;
            CharacterMainControl attackOwner = owner;
            int attackGeneration = generation;
            // 先摘除全局待结算，再调用 Hurt。其它死亡处理可能立即换场景、脱装备或杀死玩家。
            pendingTargets = null;
            pendingEntries = null;
            RollCallLedgerFx.ShowComplete(attackOwner);
            for (int i = 0; i < targets.Length; i++)
            {
                if (attackGeneration != generation || !ReferenceEquals(owner, attackOwner)
                    || !ReferenceEquals(attackOwner, CharacterMainControl.Main) || !IsUsableOwner(attackOwner)) break;
                Health target = targets[i];
                if (!IsEnemy(target, attackOwner, true)) continue;
                float bonus = RollCallLedgerRules.CalculateBonus(entries[i].HitDamage,
                    RollCallLedgerConfig.BonusBaseDamage, RollCallLedgerConfig.BonusHitDamageRatio);
                if (bonus <= 0f) continue;
                DamageInfo damage = new DamageInfo(attackOwner);
                damage.damageValue = bonus;
                damage.damageType = DamageTypes.normal;
                damage.isFromBuffOrEffect = true;
                damage.fromWeaponItemID = 0;
                damage.AddElementFactor(ElementTypes.physics, 1f);
                damage.damagePoint = target.transform.position;
                RollCallLedgerFx.PlayStamp(target);
                try { target.Hurt(damage); }
                catch (Exception e) { ModBehaviour.DevLog(RollCallLedgerConfig.LogPrefix + " 单目标结算失败: " + e.Message); }
            }
        }

        private static void ClearMarks() { Array.Clear(markedTargets, 0, markedTargets.Length); }

        private static void DestroyDriver()
        {
            RollCallLedgerDriver old = driver;
            driver = null;
            if (old == null) return;
            old.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(old.gameObject);
        }

        internal static void OnDriverDestroyed(RollCallLedgerDriver source)
        {
            if (!ReferenceEquals(source, driver)) return;
            driver = null;
            generation++;
            pendingTargets = null;
            pendingEntries = null;
            ClearMarks();
            rules.ResetSequence();
        }
    }

    /// <summary>仅在点名窗口或下一帧结算存在时驻留，冷却本身不产生 Update。</summary>
    internal sealed class RollCallLedgerDriver : MonoBehaviour
    {
        private void Update() { RollCallLedgerRuntime.Tick(this); }
        private void OnDestroy() { RollCallLedgerRuntime.OnDriverDestroyed(this); }
    }
}
