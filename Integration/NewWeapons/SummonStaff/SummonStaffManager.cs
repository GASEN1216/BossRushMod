// ============================================================================
// SummonStaffManager.cs - 召唤法杖右键技能管理器
// ============================================================================
// 模块说明：
//   管理右键技能「灵魂召唤」的输入检测和执行
//   检测 ADS（右键）输入，验证是否持有召唤法杖，触发召唤
// ============================================================================

using System.Collections;
using BossRush.Common.Equipment;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BossRush
{
    /// <summary>
    /// 召唤法杖右键技能管理器
    /// </summary>
    public class SummonStaffManager
        : EquipmentAbilityManager<SummonStaffConfig, SummonStaffAction>
    {
        private readonly SummonStaffConfig configInstance = new SummonStaffConfig();
        private bool holdEventSubscribed;
        private bool preparationComplete;
        private Coroutine preparation;

        // 单例访问（类型安全）
        public static new SummonStaffManager Instance
        {
            get { return EquipmentAbilityManager<SummonStaffConfig, SummonStaffAction>.Instance as SummonStaffManager; }
        }

        protected override void Update()
        {
            if (!ModBehaviour.CanRunGameplayRuntimeCached())
            {
                CancelPreparation();
                return;
            }

            SuppressVanillaAdsIfNeeded();

            if (!abilityEnabled) return;
            if (!preparationComplete && preparation == null && IsHoldingSummonStaff(targetCharacter))
                preparation = StartCoroutine(PrepareWhileHeld(targetCharacter));

            HandleSummonInput();
        }

        private void LateUpdate()
        {
            if (!ModBehaviour.CanRunGameplayRuntimeCached())
            {
                return;
            }

            SuppressVanillaAdsIfNeeded();
        }

        protected override SummonStaffConfig GetConfig()
        {
            return configInstance;
        }

        protected override string GetInputActionName()
        {
            return "ADS";
        }

        protected override SummonStaffAction CreateAbilityAction()
        {
            return actionObject.AddComponent<SummonStaffAction>();
        }

        protected override bool IsInputPressedFallback()
        {
            return Input.GetMouseButtonDown(1);
        }

        protected override bool OnBeforeTryExecute()
        {
            return IsHoldingSummonStaff(targetCharacter);
        }

        protected override void OnManagerInitialized()
        {
            SummonStaffAction.SetConfig(configInstance);
            if (!holdEventSubscribed)
            {
                CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += OnHoldItemChanged;
                holdEventSubscribed = true;
            }
            LogIfVerbose("召唤法杖右键技能管理器已初始化");
        }

        public override void OnSceneChanged()
        {
            CancelPreparation();
            SummonStaffAction.ResetPresetCache();
            base.OnSceneChanged();
        }

        protected override void OnDestroy()
        {
            if (holdEventSubscribed)
            {
                CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= OnHoldItemChanged;
                holdEventSubscribed = false;
            }
            CancelPreparation();
            base.OnDestroy();
        }

        protected override void OnAfterDeactivate()
        {
            CancelPreparation();
            base.OnAfterDeactivate();
        }

        private void OnHoldItemChanged(CharacterMainControl character, DuckovItemAgent agent)
        {
            if (character != CharacterMainControl.Main) return;
            CancelPreparation();
        }

        private void CancelPreparation()
        {
            if (preparation != null) StopCoroutine(preparation);
            preparation = null;
            preparationComplete = false;
        }

        private bool CanPrepare(CharacterMainControl player)
        {
            return abilityEnabled && player != null && player == targetCharacter
                && player.Health != null && !player.Health.IsDead && IsHoldingSummonStaff(player);
        }

        private IEnumerator PrepareWhileHeld(CharacterMainControl player)
        {
            // 两阶段各占一帧，入口仅为实际手持；右键回调不再做首次全局 preset 扫描。
            yield return null;
            if (!CanPrepare(player)) { preparation = null; yield break; }
            SummonStaffAction.PreparePreset();
            yield return null;
            if (!CanPrepare(player)) { preparation = null; yield break; }
            BossRushProceduralSprites.GetCircleSprite();
            BossRushProceduralSprites.GetRingSprite();
            BossRush.Common.Effects.RingParticleEffect.GetSharedParticleMaterial();
            preparationComplete = true;
            preparation = null;
        }

        private void HandleSummonInput()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || targetCharacter == null || abilityAction == null) return;

            if (!IsGameplayInputAllowed()) return;
            if (!OnBeforeTryExecute()) return;

            bool adsPressed = IsAdsPressed();
            if (adsPressed)
            {
                float remaining = abilityAction.GetRemainingCooldownTime();
                if (remaining > 0.001f)
                {
                    LogIfVerbose("技能冷却中");
                    return;
                }

                TryExecuteAbility();
            }
        }

        private bool IsAdsPressed()
        {
            try
            {
                if (Input.GetMouseButtonDown(1)) return true;
            }
            catch  { /* best-effort fallback intentionally ignored */ }

            if (!inputActionCached)
            {
                TryCacheInputAction();
            }

            return IsInputPressed();
        }

        /// <summary>
        /// 检查玩家是否持有召唤法杖。
        /// 判据走 NewWeaponEquipState 的共享缓存——与毒蛇匕首用同一份「当前手持是什么」，
        /// 不再各自 GetMeleeWeapon + CurrentHoldItemAgent 抄一遍。
        /// </summary>
        internal static bool IsHoldingSummonStaff(CharacterMainControl character)
        {
            if (character == null) return false;
            if (!ReferenceEquals(character, CharacterMainControl.Main)) return false;
            return NewWeaponEquipState.IsHolding(NewWeaponIds.SummonStaffTypeId);
        }

        private static bool IsGameplayInputAllowed()
        {
            try
            {
                if (!InputManager.InputActived) return false;
            }
            catch { return false; }

            if (Time.timeScale <= 0f) return false;
            if (Cursor.lockState == CursorLockMode.None) return false;

            try
            {
                if (Duckov.UI.View.ActiveView != null) return false;
            }
            catch  { /* best-effort fallback intentionally ignored */ }

            try
            {
                EventSystem eventSystem = EventSystem.current;
                if (eventSystem != null && eventSystem.IsPointerOverGameObject()) return false;
            }
            catch  { /* best-effort fallback intentionally ignored */ }

            return true;
        }

        private void SuppressVanillaAdsIfNeeded()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;

            bool holding = IsHoldingSummonStaff(player);
            if (!holding && !IsActionRunning) return;
            if (!IsGameplayInputAllowed()) return;

            bool adsPressed = false;
            try { adsPressed = Input.GetMouseButtonDown(1) || Input.GetMouseButton(1); }
            catch  { /* best-effort fallback intentionally ignored */ }

            if (!adsPressed && !IsActionRunning) return;

            try
            {
                if (LevelManager.Instance != null && LevelManager.Instance.InputManager != null)
                {
                    LevelManager.Instance.InputManager.SetAdsInput(false);
                }
            }
            catch  { /* best-effort fallback intentionally ignored */ }
        }
    }
}
