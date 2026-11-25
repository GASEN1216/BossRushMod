using System;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 止语钟罩的唯一 owner。装备事件决定驱动在不在：只有主角在出击场景实际佩戴时才建驱动（回充 + 头顶条 + 钟影），
    /// 卸下、死亡、切图立即销毁。护罩数值在 <see cref="SilenceBellRules"/>，最终伤害走现有致死前入口
    /// （ZombieModeDamageRuntime.ReduceFinalDamage，排在残日盾片之后）。
    /// </summary>
    internal static class SilenceBellRuntime
    {
        private static readonly SilenceBellRules rules = new SilenceBellRules();
        private static bool isSubscribed;
        private static bool levelInitializing;
        private static CharacterMainControl contextPlayer;
        private static CharacterMainControl owner;
        private static SilenceBellDriver driver;
        private static SilenceBellFx fx;

        internal static float Shield { get { return rules.Shield; } }
        internal static bool IsActive { get { return owner != null; } }

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

        /// <summary>换场景、死亡、宿主销毁：撤掉驱动并满罩。</summary>
        internal static void ResetStaticCaches()
        {
            Deactivate();
            contextPlayer = null;
            rules.ResetFull();
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
                && NewWeaponEquipState.IsTotemEquipped(BossRushItemIds.SilenceBell);
        }

        private static void RefreshContext()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (!ReferenceEquals(contextPlayer, player))
                {
                    Deactivate();
                    rules.ResetFull();
                    contextPlayer = player;
                }
                if (!IsUsableOwner(player)) { Deactivate(); return; }
                if (ReferenceEquals(owner, player) && driver != null) return;
                Deactivate();
                owner = player;
                EnsureDriver();
            }
            catch (Exception e)
            {
                Deactivate();
                ModBehaviour.DevLog(SilenceBellConfig.LogPrefix + " 装备绑定失败: " + e.Message);
            }
        }

        /// <summary>卸下 / 失效：只撤驱动与表现，护罩数值留着（再戴上接着用，不能靠穿脱补满）。</summary>
        private static void Deactivate()
        {
            owner = null;
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

        /// <summary>
        /// 必须在官方 finalDamage 首次写入之后、过量伤害钳制和扣血之前调用（与残日盾片同一入口、排在它后面）。
        /// 未佩戴时第一行就返回，受击热路径 O(1)。
        /// </summary>
        internal static void ModifyFinalDamage(Health health, ref DamageInfo info)
        {
            if (!isSubscribed || owner == null || health == null || health.IsDead
                || info.isFromBuffOrEffect || info.fromCharacter == null
                || !(info.finalDamage > 0f) || !SilenceBellRules.IsFinite(info.finalDamage)) return;
            if (!ReferenceEquals(health, owner.Health)) return;
            try
            {
                if (!ReferenceEquals(owner, CharacterMainControl.Main) || !IsUsableOwner(owner))
                { RefreshContext(); return; }
                if (BossRushUI.IsGamePaused() || !IsEnemy(info.fromCharacter)) return;
                bool broke;
                float absorbed = rules.Absorb(info.finalDamage, Time.time, out broke);
                if (!(absorbed > 0f)) return;
                // 改的是本击最终伤害，致死检查也消费减伤后的值；没有事后补血。
                info.finalDamage = Mathf.Max(0f, info.finalDamage - absorbed);
                if (fx == null) return;
                if (broke) fx.PlayShatter(owner.transform.position);
                else fx.PlayHit(owner.transform.position, Time.time);
            }
            catch (Exception e)
            {
                Deactivate();
                ModBehaviour.DevLog(SilenceBellConfig.LogPrefix + " 致死前伤害处理失败: " + e.Message);
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
            GameObject go = new GameObject("SilenceBell_Runtime");
            driver = go.AddComponent<SilenceBellDriver>();
            fx = SilenceBellFx.Create(go.transform);
            // 戴着就常驻的金钟罩跟着主角走（owner 2026-11-25）；驱动随卸下 / 死亡 / 切图销毁，钟跟着一起没
            if (fx != null && owner != null) fx.Follow(owner.transform);
        }

        internal static void Tick(SilenceBellDriver source)
        {
            if (!ReferenceEquals(source, driver)) return;
            try
            {
                if (!ReferenceEquals(owner, CharacterMainControl.Main) || !IsUsableOwner(owner))
                { RefreshContext(); return; }
                if (BossRushUI.IsGamePaused()) return;
                float now = Time.time;
                bool refilled = rules.Tick(now, Time.deltaTime);
                if (fx == null) return;
                fx.SetState(owner.transform.position, HeadHeight(owner), rules.Fraction, rules.Broken,
                    rules.IsRecharging(now));
                if (refilled) fx.PlayRefill(owner.transform.position);
            }
            catch (Exception e)
            {
                Deactivate();
                ModBehaviour.DevLog(SilenceBellConfig.LogPrefix + " 护罩更新失败: " + e.Message);
            }
        }

        /// <summary>与官方 HealthBar.RefreshOffset 同口径：头盔挂点高度，取不到按 1 米。</summary>
        private static float HeadHeight(CharacterMainControl player)
        {
            CharacterModel model = player.characterModel;
            Transform helmet = model != null ? model.HelmatSocket : null;
            return helmet != null ? Vector3.Distance(player.transform.position, helmet.position) : 1f;
        }

        private static void DestroyDriver()
        {
            SilenceBellDriver old = driver;
            SilenceBellFx view = fx;
            driver = null;
            fx = null;
            // 正在散的碎片与钟声自己收尾；驱动、头顶条、钟影立即销毁，不留 Update。
            if (view != null) view.Release();
            if (old == null) return;
            old.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(old.gameObject);
        }

        internal static void OnDriverDestroyed(SilenceBellDriver source)
        {
            if (!ReferenceEquals(source, driver)) return;
            driver = null;
            fx = null;
            owner = null;
        }
    }

    internal sealed class SilenceBellDriver : MonoBehaviour
    {
        private void Update() { SilenceBellRuntime.Tick(this); }
        private void OnDestroy() { SilenceBellRuntime.OnDriverDestroyed(this); }
    }
}
