// ============================================================================
// SummonStaffManager.cs - 召唤法杖右键技能管理器
// ============================================================================
// 模块说明：
//   管理右键技能「灵魂召唤」的输入检测和执行
//   检测 ADS（右键）输入，验证是否持有召唤法杖，触发召唤
// ============================================================================

using BossRush.Common.Equipment;
using ItemStatsSystem;
using ItemStatsSystem.Items;
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

        // 单例访问（类型安全）
        public static new SummonStaffManager Instance
        {
            get { return EquipmentAbilityManager<SummonStaffConfig, SummonStaffAction>.Instance as SummonStaffManager; }
        }

        protected override void Update()
        {
            if (!ModBehaviour.CanRunGameplayRuntimeCached())
            {
                return;
            }

            SuppressVanillaAdsIfNeeded();

            if (!abilityEnabled) return;

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
            LogIfVerbose("召唤法杖右键技能管理器已初始化");
        }

        public override void OnSceneChanged()
        {
            SummonStaffAction.ResetPresetCache();
            base.OnSceneChanged();
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
        /// 检查玩家是否持有召唤法杖
        /// </summary>
        internal static bool IsHoldingSummonStaff(CharacterMainControl character)
        {
            if (character == null) return false;

            try
            {
                ItemAgent_MeleeWeapon melee = character.GetMeleeWeapon();
                if (melee != null && melee.Item != null &&
                    melee.Item.TypeID == NewWeaponIds.SummonStaffTypeId)
                {
                    return true;
                }

                DuckovItemAgent holdAgent = character.CurrentHoldItemAgent;
                if (holdAgent != null && holdAgent.Item != null &&
                    holdAgent.Item.TypeID == NewWeaponIds.SummonStaffTypeId)
                {
                    return true;
                }
            }
            catch  { /* best-effort fallback intentionally ignored */ }

            return false;
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
