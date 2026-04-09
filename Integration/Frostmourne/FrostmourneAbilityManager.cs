// ============================================================================
// FrostmourneAbilityManager.cs - 霜之哀伤右键技能管理器
// ============================================================================
// 模块说明：
//   管理右键技能「亡灵召唤」的输入检测和执行
//   检测 ADS（右键）输入，验证是否持有霜之哀伤，触发召唤
// ============================================================================

using System.Reflection;
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
        private float nextCooldownBubbleTime = -999f;

        protected override void Update()
        {
            SuppressVanillaAdsIfNeeded();

            if (!abilityEnabled)
            {
                return;
            }

            HandleSummonInput();
        }

        private void LateUpdate()
        {
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
            return IsHoldingFrostmourne(CharacterMainControl.Main);
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

            if (Time.time < nextCooldownBubbleTime)
            {
                return;
            }

            nextCooldownBubbleTime = Time.time + CooldownBubbleInterval;

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
        /// 检查玩家是否持有霜之哀伤
        /// </summary>
        internal static bool IsHoldingFrostmourne(CharacterMainControl character)
        {
            if (character == null) return false;

            try
            {
                Item characterItem = character.CharacterItem;
                if (characterItem == null) return false;

                foreach (Slot slot in characterItem.Slots)
                {
                    if (slot == null || slot.Content == null) continue;
                    if (slot.Content.TypeID == FrostmourneIds.WeaponTypeId)
                    {
                        // 检查是否在手持槽位
                        if (slot.Key != null &&
                            (slot.Key.Contains("Hand") || slot.Key.Contains("hand") ||
                             slot.Key.Contains("Weapon") || slot.Key.Contains("weapon")))
                        {
                            return true;
                        }
                    }
                }

                // 备用检查：当前手持物品
                try
                {
                    var handheldField = typeof(CharacterMainControl).GetField("currentHandheldItem",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                    if (handheldField != null)
                    {
                        object handheld = handheldField.GetValue(character);
                        if (handheld != null)
                        {
                            Item handheldItem = handheld as Item;
                            if (handheldItem == null)
                            {
                                var itemProp = handheld.GetType().GetProperty("Item",
                                    BindingFlags.Public | BindingFlags.Instance);
                                if (itemProp != null)
                                {
                                    handheldItem = itemProp.GetValue(handheld) as Item;
                                }
                            }
                            if (handheldItem != null && handheldItem.TypeID == FrostmourneIds.WeaponTypeId)
                            {
                                return true;
                            }
                        }
                    }
                }
                catch { }
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
