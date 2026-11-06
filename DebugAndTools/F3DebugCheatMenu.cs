using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private enum F3DebugCheatPage
        {
            Teleport,
            PlayerStats,
            Resources,
            Battle,
            NpcStory,
            SceneDebug,
            Validation,
            Recording
        }

        private sealed class F3DebugCheatPlayerState
        {
            public float maxHealthMultiplier = 1f;
            public float gunDamageMultiplier = 1f;
            public float meleeDamageMultiplier = 1f;
            public float? headArmorOverride;
            public float? bodyArmorOverride;

            public void Reset()
            {
                maxHealthMultiplier = 1f;
                gunDamageMultiplier = 1f;
                meleeDamageMultiplier = 1f;
                headArmorOverride = null;
                bodyArmorOverride = null;
            }
        }

        private sealed class F3DebugCheatRuntimeBindings
        {
            public Stat maxHealthStat;
            public Modifier maxHealthModifier;

            public Stat gunDamageStat;
            public Modifier gunDamageModifier;

            public Stat meleeDamageStat;
            public Modifier meleeDamageModifier;

            public Stat headArmorStat;
            public Modifier headArmorModifier;

            public Stat bodyArmorStat;
            public Modifier bodyArmorModifier;

            public void Clear()
            {
                maxHealthStat = null;
                maxHealthModifier = null;
                gunDamageStat = null;
                gunDamageModifier = null;
                meleeDamageStat = null;
                meleeDamageModifier = null;
                headArmorStat = null;
                headArmorModifier = null;
                bodyArmorStat = null;
                bodyArmorModifier = null;
            }
        }

        private GameObject f3DebugCheatMenuRoot;
        private bool f3DebugCheatMenuVisible = false;
        private F3DebugCheatPage f3DebugCheatCurrentPage = F3DebugCheatPage.Teleport;
        private Transform f3DebugCheatContentRoot;
        private Text f3DebugCheatSummaryText;
        private Text f3DebugCheatStatusText;
        private readonly Dictionary<F3DebugCheatPage, Image> f3DebugCheatNavButtonImages = new Dictionary<F3DebugCheatPage, Image>();

        private InputField f3ItemIdInputField;
        private InputField f3ItemCountInputField;
        private InputField f3MoneyInputField;
        private InputField f3MaxHealthMultiplierInputField;
        private InputField f3GunDamageMultiplierInputField;
        private InputField f3MeleeDamageMultiplierInputField;
        private InputField f3HeadArmorInputField;
        private InputField f3BodyArmorInputField;

        private readonly F3DebugCheatPlayerState f3DebugCheatPlayerState = new F3DebugCheatPlayerState();
        private readonly F3DebugCheatRuntimeBindings f3DebugCheatRuntimeBindings = new F3DebugCheatRuntimeBindings();
        private float f3DebugCheatSummaryNextRefreshTime = -1f;
        private float f3DebugCheatPlayerNextApplyTime = -1f;
        private bool f3DebugCheatPlayerApplyPending = false;
        private string f3DebugCheatPlayerApplyReason = string.Empty;
        private float f3DebugCheatPreviousTimeScale = 1f;
        private bool f3DebugCheatPreviousCursorVisible = false;
        private CursorLockMode f3DebugCheatPreviousCursorLockState = CursorLockMode.Locked;
        private bool f3DebugCheatPresentationStateCaptured = false;
        private InputManager f3DebugCheatInputOwner;

        private void CheckF3DebugCheatMenuHotkey()
        {
            if (!DevModeEnabled)
            {
                if (f3DebugCheatMenuVisible)
                {
                    HideF3DebugCheatMenu();
                }
                return;
            }

            F3GameplayValidationRunner.EnsureAttached(this);

            if (UnityEngine.Input.GetKeyDown(KeyCode.F3))
            {
                ToggleF3DebugCheatMenu();
            }
        }

        private void TickF3DebugCheatMenu()
        {
            if (f3DebugCheatMenuVisible && (SceneLoader.IsSceneLoading || UnityEngine.Input.GetKeyDown(KeyCode.Escape)))
            {
                HideF3DebugCheatMenu();
                return;
            }

            if (f3DebugCheatMenuVisible && Time.unscaledTime >= f3DebugCheatSummaryNextRefreshTime)
            {
                RefreshF3DebugCheatSummary();
                RefreshF3GameplayValidationStatus();
                f3DebugCheatSummaryNextRefreshTime = Time.unscaledTime + 0.25f;
            }

            if (HasActiveF3PlayerCheatConfig() && f3DebugCheatPlayerApplyPending && Time.unscaledTime >= f3DebugCheatPlayerNextApplyTime)
            {
                ApplyPlayerCheatParameters(true);
            }
        }

        private void F3DebugCheatMenuLateUpdate()
        {
            if (!f3DebugCheatMenuVisible)
            {
                return;
            }

            ApplyF3DebugCheatPresentationState();
        }

        private void OnSceneLoaded_F3DebugCheatMenu(Scene scene, LoadSceneMode mode)
        {
            RemovePlayerCheatRuntimeModifiers();
            QueuePlayerCheatApply("scene_loaded");

            if (f3DebugCheatMenuRoot != null)
            {
                RefreshF3DebugCheatSummary();
            }
        }

        private void OnDestroy_F3DebugCheatMenu()
        {
            F3GameplayValidationRunner.Shutdown(this);
            RemovePlayerCheatRuntimeModifiers();
            f3DebugCheatRuntimeBindings.Clear();
            DestroyF3DebugCheatMenuUI();
        }

        private void ToggleF3DebugCheatMenu()
        {
            if (f3DebugCheatMenuVisible)
            {
                HideF3DebugCheatMenu();
            }
            else
            {
                ShowF3DebugCheatMenu();
            }
        }

        private void ShowF3DebugCheatMenu()
        {
            if (SceneLoader.IsSceneLoading || BossRushUIKit.CurrentInputManager == null) return;
            try
            {
                if (f3DebugCheatMenuRoot == null || f3DebugCheatContentRoot == null || f3DebugCheatSummaryText == null || f3DebugCheatStatusText == null)
                {
                    DestroyF3DebugCheatMenuUI();
                    CreateF3DebugCheatMenuUI();
                }

                if (f3DebugCheatMenuRoot == null)
                {
                    SetF3DebugCheatStatus(L10n.T("调试菜单创建失败", "Failed to create debug menu"), true);
                    return;
                }

                if (npcTeleportUIVisible) HideNPCTeleportUI();
                InventoryInspector inspector = GetComponent<InventoryInspector>();
                if (inspector != null) inspector.HideWindow();
                SkyIslandSession.HideMapBeforeF3(this);
                CaptureF3DebugCheatPresentationState();
                f3DebugCheatMenuRoot.SetActive(true);
                f3DebugCheatMenuVisible = true;
                ApplyF3DebugCheatPresentationState();
                DisableF3DebugCheatInput();

                RefreshF3DebugCheatSummary();
                RefreshF3DebugCheatPage();
                f3DebugCheatSummaryNextRefreshTime = Time.unscaledTime + 0.25f;
            }
            catch (Exception e)
            {
                HideF3DebugCheatMenu();
                DevLog("[BossRush] F3 调试菜单显示失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("打开调试菜单失败", "Failed to open debug menu"), true);
            }
        }

        internal void CloseF3BeforeDebugPanel() { if (f3DebugCheatMenuVisible) HideF3DebugCheatMenu(); }

        private void HideF3DebugCheatMenu()
        {
            f3DebugCheatMenuVisible = false;
            try
            {
                EnableF3DebugCheatInput();
                if (f3DebugCheatMenuRoot != null)
                {
                    f3DebugCheatMenuRoot.SetActive(false);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 调试菜单隐藏失败: " + e.Message);
            }
            finally
            {
                try { RestoreF3DebugCheatPresentationState(); }
                catch (Exception e) { DevLog("[BossRush] F3 恢复时速和光标失败: " + e.Message); }
            }
        }

        private void CaptureF3DebugCheatPresentationState()
        {
            if (f3DebugCheatPresentationStateCaptured)
            {
                return;
            }

            f3DebugCheatPreviousTimeScale = Time.timeScale;
            f3DebugCheatPreviousCursorVisible = Cursor.visible;
            f3DebugCheatPreviousCursorLockState = Cursor.lockState;
            f3DebugCheatPresentationStateCaptured = true;
        }

        private void ApplyF3DebugCheatPresentationState()
        {
            Time.timeScale = 0f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void RestoreF3DebugCheatPresentationState()
        {
            if (!f3DebugCheatPresentationStateCaptured)
            {
                return;
            }

            f3DebugCheatPresentationStateCaptured = false;
            Time.timeScale = f3DebugCheatPreviousTimeScale;
            Cursor.visible = f3DebugCheatPreviousCursorVisible;
            Cursor.lockState = f3DebugCheatPreviousCursorLockState;
        }

        private void DisableF3DebugCheatInput()
        {
            InputManager manager = BossRushUIKit.CurrentInputManager;
            if (manager == null || f3DebugCheatInputOwner != null || f3DebugCheatMenuRoot == null) return;
            InputManager.DisableInput(f3DebugCheatMenuRoot);
            f3DebugCheatInputOwner = manager;
        }

        private void EnableF3DebugCheatInput()
        {
            InputManager manager = f3DebugCheatInputOwner;
            f3DebugCheatInputOwner = null;
            try
            {
                if (manager != null && manager == BossRushUIKit.CurrentInputManager && f3DebugCheatMenuRoot != null)
                    InputManager.ActiveInput(f3DebugCheatMenuRoot);
            }
            catch (Exception e) { DevLog("[BossRush] F3 调试菜单释放输入失败: " + e.Message); }
        }

    }
}
