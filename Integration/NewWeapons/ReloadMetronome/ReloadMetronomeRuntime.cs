using System;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>仅实际佩戴且成功开始换弹时持有会话；不轮询装备、不修改弹药或枪械属性。</summary>
    internal static class ReloadMetronomeRuntime
    {
        private static readonly ReloadMetronomeRules rules = new ReloadMetronomeRules();
        private static bool isSubscribed;
        private static bool levelInitializing;
        private static CharacterMainControl owner;
        private static ItemAgent_Gun gun;
        private static bool finishPending;
        private static bool hudAttempted;
        private static ReloadMetronomeHud hud;

        internal static ReloadMetronomeResult Result { get { return rules.Result; } }

        internal static void Subscribe()
        {
            if (isSubscribed) return;
            isSubscribed = true;
            levelInitializing = false;
            CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += OnHoldChanged;
            CharacterMainControl.OnMainCharacterSlotContentChangedEvent += OnSlotChanged;
            Health.OnDead += OnDead;
            SceneLoader.onStartedLoadingScene += OnSceneLoading;
            LevelManager.OnLevelBeginInitializing += OnLevelInitializing;
            LevelManager.OnAfterLevelInitialized += OnLevelInitialized;
        }

        internal static void Unsubscribe()
        {
            if (isSubscribed)
            {
                isSubscribed = false;
                CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= OnHoldChanged;
                CharacterMainControl.OnMainCharacterSlotContentChangedEvent -= OnSlotChanged;
                Health.OnDead -= OnDead;
                SceneLoader.onStartedLoadingScene -= OnSceneLoading;
                LevelManager.OnLevelBeginInitializing -= OnLevelInitializing;
                LevelManager.OnAfterLevelInitialized -= OnLevelInitialized;
            }
            ResetStaticCaches();
        }

        internal static void ResetStaticCaches()
        {
            rules.Reset();
            finishPending = false;
            hudAttempted = false;
            gun = null;
            owner = null;
            if (hud != null) UnityEngine.Object.Destroy(hud.gameObject);
            hud = null;
        }

        private static bool IsUsable(CharacterMainControl player)
        {
            return isSubscribed && !levelInitializing && !SceneLoader.IsSceneLoading
                && player != null && ReferenceEquals(player, CharacterMainControl.Main) && player.IsMainCharacter
                && player.Health != null && !player.Health.IsDead && LevelManager.Instance != null
                && NewWeaponEquipState.IsTotemEquipped(BossRushItemIds.ReloadMetronome);
        }

        internal static void OnReloadStarted(ItemAgent_Gun source)
        {
            if (source == null || !IsUsable(source.Holder)) return;
            ResetStaticCaches();
            if (!ReferenceEquals(source.Holder.CurrentHoldItemAgent, source) || source.GunItemSetting == null
                || source.GunItemSetting.reloadMode != ItemSetting_Gun.ReloadModes.fullMag) return;
            rules.Begin(source.ReloadTime);
            if (!rules.Active) return;
            owner = source.Holder;
            gun = source;
        }

        internal static void OnReloadCancelled(ItemAgent_Gun source)
        {
            if (ReferenceEquals(source, gun)) ResetStaticCaches();
        }

        internal static bool TryGetProgress(out float current, out float total)
        {
            current = total = 0f;
            if (!rules.Active) return false;
            if (!IsUsable(owner) || gun == null || !ReferenceEquals(owner.CurrentHoldItemAgent, gun)
                || gun.GunItemSetting == null
                || gun.GunItemSetting.reloadMode != ItemSetting_Gun.ReloadModes.fullMag)
            {
                ResetStaticCaches();
                return false;
            }
            if (!gun.IsReloading()) { FinishReload(); return false; }
            var progress = gun.GetReloadProgress();
            current = progress.current;
            total = progress.total;
            if (!ReloadMetronomeRules.IsSupportedDuration(total)
                || float.IsNaN(current) || float.IsInfinity(current) || current < 0f)
            {
                ResetStaticCaches();
                return false;
            }
            if (!progress.inProgress || current >= total) { FinishReload(); return false; }
            return true;
        }

        private static void FinishReload()
        {
            bool showFinish = rules.Result == ReloadMetronomeResult.Perfect && !finishPending && hud != null;
            rules.Reset();
            finishPending = false;
            gun = null;
            if (showFinish) hud.FinishReload();
            else ResetStaticCaches();
        }

        internal static void OnReloadRequested(CharacterMainControl player, Item preferredAmmo)
        {
            // 原版开始键也经过这里，此时还没有 BeginReload 会话，不会误算第一次按键。
            // 右键指定弹药、暂停和背包内的操作不参与节拍判定。
            if (!ReferenceEquals(player, owner) || preferredAmmo != null
                || BossRushUI.IsGamePaused() || BossRushUI.IsOfficialHudHidden()) return;
            float current, total;
            if (!TryGetProgress(out current, out total)) return;
            ReloadMetronomeResult result = rules.Press(current, total);
            if (result == ReloadMetronomeResult.Inactive) return;
            finishPending = result == ReloadMetronomeResult.Perfect;
            // 先提交判定，反馈异常不能变成无限尝试或吞掉成功。
            try
            {
                if (hud != null) hud.SetResult(result);
            }
            catch (Exception e) { ModBehaviour.DevLog("[ReloadMetronome] 反馈失败: " + e.Message); }
        }

        internal static void BeforeGunUpdate(ItemAgent_Gun source, ref float stateTimer)
        {
            // 所有枪都会进此补丁，未命中的路径只有两个字段比较。
            if (!finishPending || !ReferenceEquals(source, gun)) return;
            if (BossRushUI.IsGamePaused()) return;
            float current, total;
            if (!TryGetProgress(out current, out total)) return;
            finishPending = false;
            // 只推进原版计时。后续 StartLoadBullets、缺弹处理、OnLoadedEvent 与动作结束全由官方执行。
            stateTimer = Math.Max(stateTimer, total);
        }

        internal static void Present(AimMarker marker)
        {
            if (!rules.Active || marker == null || marker.aimMarkerUI == null
                || BossRushUI.IsGamePaused() || BossRushUI.IsOfficialHudHidden()) return;
            float current, total;
            if (!TryGetProgress(out current, out total)) return;
            if (hud == null && !hudAttempted)
            {
                hudAttempted = true;
                // 原版只移动 aimMarkerUI；组件根节点是覆盖全屏的容器。
                hud = ReloadMetronomeHud.Create(marker.aimMarkerUI);
                if (hud != null) hud.SetResult(rules.Result);
            }
        }

        private static void OnHoldChanged(CharacterMainControl player, DuckovItemAgent agent)
        {
            NewWeaponEquipState.MarkDirty();
            ResetStaticCaches();
        }

        private static void OnSlotChanged(CharacterMainControl player, Slot slot)
        {
            NewWeaponEquipState.MarkDirty();
            if (!IsUsable(owner)) ResetStaticCaches();
        }

        private static void OnDead(Health health, DamageInfo info)
        {
            if (health != null && health.IsMainCharacterHealth) ResetStaticCaches();
        }

        private static void OnSceneLoading(SceneLoadingContext context) { ResetStaticCaches(); }
        private static void OnLevelInitializing() { levelInitializing = true; ResetStaticCaches(); }
        private static void OnLevelInitialized() { levelInitializing = false; ResetStaticCaches(); }
    }
}
