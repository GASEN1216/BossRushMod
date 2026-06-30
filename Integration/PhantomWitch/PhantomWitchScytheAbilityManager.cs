// ============================================================================
// PhantomWitchScytheAbilityManager.cs - 幽灵女巫大镰右键技能管理器
// ============================================================================
// 模块说明：
//   管理右键技能「诅咒领域」的输入检测和执行。
//   - 检测 ADS（右键）输入，仅在玩家持镰刀时生效
//   - 冷却中 / 正在释放时提示气泡
//   - 屏蔽游戏原版 ADS 逻辑，避免持镰刀时触发瞄准
//
//   范式与 FrostmourneAbilityManager 保持一致。
// ============================================================================

using BossRush.Common.Equipment;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BossRush
{
    public class PhantomWitchScytheAbilityManager
        : EquipmentAbilityManager<PhantomWitchScytheConfig, PhantomWitchScytheAction>
    {
        private const float CooldownBubbleInterval = 0.35f;
        private readonly PhantomWitchScytheConfig configInstance = new PhantomWitchScytheConfig();
        private float nextStatusBubbleTime = -999f;

        protected override void Update()
        {
            if (!ModBehaviour.CanRunGameplayRuntimeCached())
            {
                return;
            }

            SuppressVanillaAdsIfNeeded();

            if (!abilityEnabled)
            {
                return;
            }

            HandleRealmInput();
        }

        private void LateUpdate()
        {
            if (!ModBehaviour.CanRunGameplayRuntimeCached())
            {
                return;
            }

            SuppressVanillaAdsIfNeeded();
        }

        protected override PhantomWitchScytheConfig GetConfig()
        {
            return configInstance;
        }

        protected override string GetInputActionName()
        {
            return "ADS";
        }

        protected override PhantomWitchScytheAction CreateAbilityAction()
        {
            return actionObject.AddComponent<PhantomWitchScytheAction>();
        }

        protected override bool IsInputPressedFallback()
        {
            return Input.GetMouseButtonDown(1);
        }

        protected override bool OnBeforeTryExecute()
        {
            return IsHoldingPhantomScythe(targetCharacter);
        }

        protected override void OnManagerInitialized()
        {
            PhantomWitchScytheAction.SetConfig(configInstance);
            LogIfVerbose("幽灵女巫大镰右键技能管理器已初始化");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        public override void OnSceneChanged()
        {
            base.OnSceneChanged();
        }

        public override void RebindToCharacter(CharacterMainControl character)
        {
            base.RebindToCharacter(character);
        }

        private void HandleRealmInput()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || targetCharacter == null || abilityAction == null)
            {
                return;
            }

            if (!IsGameplayInputAllowed())
            {
                return;
            }

            if (!OnBeforeTryExecute())
            {
                return;
            }

            bool adsPressed = IsAdsPressed();
            if (!adsPressed)
            {
                return;
            }

            float remainingCooldown = abilityAction.GetRemainingCooldownTime();
            if (remainingCooldown > 0.001f)
            {
                ShowCooldownBubble(player, remainingCooldown);
                LogIfVerbose("检测到右键输入，但技能仍在冷却中");
                return;
            }

            LogIfVerbose("检测到右键输入，尝试释放诅咒领域...");
            bool success = TryExecuteAbility();
            LogIfVerbose("诅咒领域结果: " + (success ? "成功" : "失败（冷却中或条件不满足）"));
        }

        private bool IsAdsPressed()
        {
            try
            {
                if (Input.GetMouseButtonDown(1))
                {
                    return true;
                }
            }
            catch
            {
            }

            if (!inputActionCached)
            {
                TryCacheInputAction();
            }

            return IsInputPressed();
        }

        private void ShowCooldownBubble(CharacterMainControl player, float remainingCooldown)
        {
            if (player == null || player.transform == null)
            {
                return;
            }

            if (Time.time < nextStatusBubbleTime)
            {
                return;
            }

            nextStatusBubbleTime = Time.time + CooldownBubbleInterval;

            try
            {
                int seconds = Mathf.Max(1, Mathf.CeilToInt(remainingCooldown));
                string bubbleText = L10n.T("冷却中" + seconds + "s...", "Cooldown " + seconds + "s...");
                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    bubbleText,
                    player.transform,
                    2.5f,
                    false,
                    false,
                    -1f,
                    1.5f
                );
            }
            catch (System.Exception e)
            {
                LogIfVerbose("显示冷却气泡异常: " + e.Message);
            }
        }

        /// <summary>
        /// 检查玩家是否持有幽灵女巫大镰
        /// </summary>
        internal static bool IsHoldingPhantomScythe(CharacterMainControl character)
        {
            if (character == null)
            {
                return false;
            }

            try
            {
                ItemAgent_MeleeWeapon melee = character.GetMeleeWeapon();
                if (melee != null &&
                    melee.Item != null &&
                    melee.Item.TypeID == PhantomWitchScytheIds.WeaponTypeId)
                {
                    return true;
                }

                DuckovItemAgent holdAgent = character.CurrentHoldItemAgent;
                if (holdAgent != null &&
                    holdAgent.Item != null &&
                    holdAgent.Item.TypeID == PhantomWitchScytheIds.WeaponTypeId)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsGameplayInputAllowed()
        {
            try
            {
                if (!InputManager.InputActived)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            if (Time.timeScale <= 0f)
            {
                return false;
            }

            if (Cursor.lockState == CursorLockMode.None)
            {
                return false;
            }

            try
            {
                if (Duckov.UI.View.ActiveView != null)
                {
                    return false;
                }
            }
            catch
            {
            }

            try
            {
                EventSystem eventSystem = EventSystem.current;
                if (eventSystem != null && eventSystem.IsPointerOverGameObject())
                {
                    return false;
                }
            }
            catch
            {
            }

            return true;
        }

        private void SuppressVanillaAdsIfNeeded()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }

            bool holdingScythe = IsHoldingPhantomScythe(player);
            if (!holdingScythe && !IsActionRunning)
            {
                return;
            }

            if (!IsGameplayInputAllowed())
            {
                return;
            }

            bool adsPressed = false;
            try
            {
                adsPressed = Input.GetMouseButtonDown(1) || Input.GetMouseButton(1);
            }
            catch
            {
            }

            if (!adsPressed && !IsActionRunning)
            {
                return;
            }

            try
            {
                if (LevelManager.Instance != null && LevelManager.Instance.InputManager != null)
                {
                    LevelManager.Instance.InputManager.SetAdsInput(false);
                }
            }
            catch
            {
            }
        }
    }
}
