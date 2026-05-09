// ============================================================================
// FrostmourneAbilityManager.cs - 霜之哀伤右键技能管理器
// ============================================================================
// 模块说明：
//   管理右键技能「亡灵召唤」的输入检测和执行
//   检测 ADS（右键）输入，验证是否持有霜之哀伤，触发召唤
// ============================================================================

using BossRush.Common.Equipment;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BossRush
{
    public class FrostmourneAbilityManager
        : EquipmentAbilityManager<FrostmourneConfig, FrostmourneAction>
    {
        private const float CooldownBubbleInterval = 0.35f;
        private readonly FrostmourneConfig configInstance = new FrostmourneConfig();
        private float nextStatusBubbleTime = -999f;

        protected override void Update()
        {
            if (!ModBehaviour.CanRunGameplayRuntimeNow(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
            {
                return;
            }

            SuppressVanillaAdsIfNeeded();

            if (!abilityEnabled)
            {
                return;
            }

            HandleSummonInput();
        }

        private void LateUpdate()
        {
            if (!ModBehaviour.CanRunGameplayRuntimeNow(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
            {
                return;
            }

            SuppressVanillaAdsIfNeeded();
        }

        protected override FrostmourneConfig GetConfig()
        {
            return configInstance;
        }

        protected override string GetInputActionName()
        {
            return "ADS";
        }

        protected override FrostmourneAction CreateAbilityAction()
        {
            return actionObject.AddComponent<FrostmourneAction>();
        }

        protected override bool IsInputPressedFallback()
        {
            return Input.GetMouseButtonDown(1);
        }

        protected override bool OnBeforeTryExecute()
        {
            return IsHoldingFrostmourne(targetCharacter);
        }

        protected override void OnManagerInitialized()
        {
            FrostmourneAction.SetConfig(configInstance);
            LogIfVerbose("霜之哀伤右键技能管理器已初始化");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        public override void OnSceneChanged()
        {
            FrostmourneAction.ResetPresetCache();
            base.OnSceneChanged();
        }

        public override void RebindToCharacter(CharacterMainControl character)
        {
            base.RebindToCharacter(character);
        }

        private void HandleSummonInput()
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
            if (adsPressed)
            {
                float remainingCooldown = abilityAction.GetRemainingCooldownTime();
                if (remainingCooldown > 0.001f)
                {
                    ShowCooldownBubble(player, remainingCooldown);
                    LogIfVerbose("检测到右键输入，但技能仍在冷却中");
                    return;
                }

                if (FrostmourneAction.IsSummonCapReached())
                {
                    ShowSummonLimitBubble(player);
                    LogIfVerbose("检测到右键输入，但当前亡灵仆从数量已满");
                    return;
                }

                LogIfVerbose("检测到右键输入，尝试召唤亡灵...");
                bool success = TryExecuteAbility();
                LogIfVerbose("召唤结果: " + (success ? "成功" : "失败（冷却中或条件不满足）"));
            }
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
            catch { }

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

        private void ShowSummonLimitBubble(CharacterMainControl player)
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
                string bubbleText = L10n.T("亡灵仆从已满(5/5)", "Undead servants full (5/5)");
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
                LogIfVerbose("显示亡灵已满气泡异常: " + e.Message);
            }
        }

        /// <summary>
        /// 检查玩家是否持有霜之哀伤
        /// </summary>
        internal static bool IsHoldingFrostmourne(CharacterMainControl character)
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
                    melee.Item.TypeID == FrostmourneIds.WeaponTypeId)
                {
                    return true;
                }

                DuckovItemAgent holdAgent = character.CurrentHoldItemAgent;
                if (holdAgent != null &&
                    holdAgent.Item != null &&
                    holdAgent.Item.TypeID == FrostmourneIds.WeaponTypeId)
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static bool IsGameplayInputAllowed()
        {
            try
            {
                if (!InputManager.InputActived)
                    return false;
            }
            catch { return false; }

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
                    return false;
            }
            catch { }

            try
            {
                EventSystem eventSystem = EventSystem.current;
                if (eventSystem != null && eventSystem.IsPointerOverGameObject())
                {
                    return false;
                }
            }
            catch { }

            return true;
        }

        private void SuppressVanillaAdsIfNeeded()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;

            bool holdingFrostmourne = IsHoldingFrostmourne(player);
            if (!holdingFrostmourne && !IsActionRunning) return;

            if (!IsGameplayInputAllowed()) return;

            bool adsPressed = false;
            try { adsPressed = Input.GetMouseButtonDown(1) || Input.GetMouseButton(1); }
            catch { }

            if (!adsPressed && !IsActionRunning) return;

            try
            {
                if (LevelManager.Instance != null && LevelManager.Instance.InputManager != null)
                {
                    LevelManager.Instance.InputManager.SetAdsInput(false);
                }
            }
            catch { }
        }
    }
}
