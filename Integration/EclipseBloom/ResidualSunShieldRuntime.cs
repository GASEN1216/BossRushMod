using System;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>装备事件拥有翻滚订阅；护盾和充能存在时才创建短命驱动。最终伤害走现有致死前入口。</summary>
    internal static class ResidualSunShieldRuntime
    {
        private static readonly ResidualSunShieldRules rules = new ResidualSunShieldRules();
        private static bool isSubscribed;
        private static bool levelInitializing;
        private static CharacterMainControl contextPlayer;
        private static CharacterMainControl owner;
        private static Vector3 guardFacing;
        private static ResidualSunShieldDriver driver;
        private static ResidualSunShieldFx fx;

        internal static bool HasGuard { get { return rules.HasGuard(Time.time); } }
        internal static bool HasCharge { get { return rules.HasCharge(Time.time); } }
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
            contextPlayer = null;
            rules.ResetAll();
        }

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
                && NewWeaponEquipState.IsTotemEquipped(BossRushItemIds.ResidualSunShield);
        }

        private static void RefreshContext()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (!ReferenceEquals(contextPlayer, player))
                {
                    Deactivate();
                    rules.ResetAll();
                    contextPlayer = player;
                }
                if (!IsUsableOwner(player)) { Deactivate(); return; }
                if (ReferenceEquals(owner, player)) return;
                Deactivate();
                owner = player;
                owner.OnActionStartEvent += OnActionStarted;
            }
            catch (Exception e)
            {
                Deactivate();
                ModBehaviour.DevLog(ResidualSunShieldConfig.LogPrefix + " 装备绑定失败: " + e.Message);
            }
        }

        private static void Deactivate()
        {
            if (!ReferenceEquals(owner, null)) owner.OnActionStartEvent -= OnActionStarted;
            owner = null;
            guardFacing = Vector3.zero;
            rules.CancelPending();
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
        { if (health != null && health.IsMainCharacterHealth) ResetStaticCaches(); }

        private static void OnSceneLoadingStarted(SceneLoadingContext context) { ResetStaticCaches(); }
        private static void OnSceneLoadingFinished(SceneLoadingContext context)
        { NewWeaponEquipState.MarkDirty(); RefreshContext(); }
        private static void OnLevelBeginInitializing() { levelInitializing = true; ResetStaticCaches(); }
        private static void OnAfterLevelInitialized()
        { levelInitializing = false; NewWeaponEquipState.MarkDirty(); RefreshContext(); }

        private static void OnActionStarted(CharacterActionBase action)
        {
            if (!(action is CA_Dash) || owner == null) return;
            try
            {
                if (!ReferenceEquals(owner, CharacterMainControl.Main) || !IsUsableOwner(owner))
                { Deactivate(); return; }
                if (BossRushUI.IsGamePaused()) return;
                // 与官方 CA_Dash.OnStart 同口径：有移动输入就沿输入，否则沿实际模型朝向。
                // DashCanControl 为 true 时官方不会转模型，不能只读 CurrentAimDirection。
                Vector3 facing = owner.MoveInput;
                if (facing.sqrMagnitude <= 0f) facing = owner.CurrentAimDirection;
                facing.y = 0f;
                if (!ResidualSunShieldRules.IsFinite(facing.x) || !ResidualSunShieldRules.IsFinite(facing.z)
                    || facing.sqrMagnitude < 0.0001f || !rules.TryStartGuard(Time.time)) return;
                guardFacing = facing.normalized;
                EnsureDriver();
                UpdateFx();
            }
            catch (Exception e)
            {
                Deactivate();
                ModBehaviour.DevLog(ResidualSunShieldConfig.LogPrefix + " 翻滚护盾失败: " + e.Message);
            }
        }

        /// <summary>必须在官方 finalDamage 首次写入之后、过量伤害钳制和扣血之前调用。</summary>
        internal static void ModifyFinalDamage(Health health, ref DamageInfo info)
        {
            if (!isSubscribed || owner == null || health == null || health.IsDead
                || info.isFromBuffOrEffect || info.isExplosion || info.fromCharacter == null
                || !(info.finalDamage > 0f) || !ResidualSunShieldRules.IsFinite(info.finalDamage)) return;
            try
            {
                if (!ReferenceEquals(owner, CharacterMainControl.Main) || !IsUsableOwner(owner))
                { RefreshContext(); return; }
                if (BossRushUI.IsGamePaused()) return;
                if (ReferenceEquals(health, owner.Health))
                {
                    if (!rules.HasGuard(Time.time) || !IsEnemy(info.fromCharacter)) return;
                    Vector3 delta = info.fromCharacter.transform.position - owner.transform.position;
                    if (!ResidualSunShieldRules.IsFrontal(guardFacing.x, guardFacing.z, delta.x, delta.z)) return;
                    float absorbed = rules.TryBlock(info.finalDamage, Time.time);
                    if (!(absorbed > 0f)) return;
                    // 改的是本击最终伤害，致死检查也消费减伤后的值；没有事后补血。
                    info.finalDamage = Mathf.Max(0f, info.finalDamage - absorbed);
                    UpdateFx();
                    if (fx != null) fx.Flash();
                    ResidualSunShieldFx.PlayAbsorb(owner.transform.position + Vector3.up * 0.9f + guardFacing * 0.75f);
                }
                else if (rules.HasCharge(Time.time) && ReferenceEquals(info.fromCharacter, owner)
                    && IsEnemy(health.TryGetCharacter()))
                {
                    float bonus = rules.TryConsumeCharge(info.finalDamage, Time.time);
                    if (!(bonus > 0f)) return;
                    info.finalDamage += bonus;
                    UpdateFx();
                    ResidualSunShieldFx.PlayRiposte(health.transform.position + Vector3.up * 0.8f);
                }
            }
            catch (Exception e)
            {
                Deactivate();
                ModBehaviour.DevLog(ResidualSunShieldConfig.LogPrefix + " 致死前伤害处理失败: " + e.Message);
            }
        }

        private static bool IsEnemy(CharacterMainControl character)
        {
            return character != null && !ReferenceEquals(character, owner)
                && !PetNestCompanionAgent.IsCompanionHealth(character.Health)
                && Team.IsEnemy(owner.Team, character.Team);
        }

        private static void EnsureDriver()
        {
            if (driver != null) return;
            GameObject go = new GameObject("ResidualSunShield_Runtime");
            driver = go.AddComponent<ResidualSunShieldDriver>();
            fx = ResidualSunShieldFx.Create(go.transform);
        }

        private static void UpdateFx()
        {
            if (fx != null && owner != null)
                fx.SetState(owner.transform.position, guardFacing, rules.HasGuard(Time.time), rules.HasCharge(Time.time));
        }

        internal static void Tick(ResidualSunShieldDriver source)
        {
            if (!ReferenceEquals(source, driver)) return;
            try
            {
                if (!ReferenceEquals(owner, CharacterMainControl.Main) || !IsUsableOwner(owner))
                { RefreshContext(); return; }
                if (BossRushUI.IsGamePaused()) return;
                if (!rules.HasGuard(Time.time) && !rules.HasCharge(Time.time))
                { rules.CancelPending(); DestroyDriver(); return; }
                UpdateFx();
            }
            catch (Exception e)
            {
                Deactivate();
                ModBehaviour.DevLog(ResidualSunShieldConfig.LogPrefix + " 护盾更新失败: " + e.Message);
            }
        }

        private static void DestroyDriver()
        {
            ResidualSunShieldDriver old = driver;
            ResidualSunShieldFx view = fx;
            driver = null;
            fx = null;
            // 表现先脱离驱动，收拢淡出后自毁；驱动本身立即销毁，不留 Update。
            if (view != null) view.Release();
            if (old == null) return;
            old.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(old.gameObject);
        }

        internal static void OnDriverDestroyed(ResidualSunShieldDriver source)
        {
            if (!ReferenceEquals(source, driver)) return;
            driver = null;
            fx = null;
            rules.CancelPending();
        }
    }

    internal sealed class ResidualSunShieldDriver : MonoBehaviour
    {
        private void Update() { ResidualSunShieldRuntime.Tick(this); }
        private void OnDestroy() { ResidualSunShieldRuntime.OnDriverDestroyed(this); }
    }
}
