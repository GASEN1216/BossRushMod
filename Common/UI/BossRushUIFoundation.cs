// 全 Mod 字体、缩放与模态输入租约的唯一实现。ZombieModeUIHelper 仅保留兼容转发。
using System;
using System.Reflection;
using Duckov.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal static partial class BossRushUIKit
    {
        private static TMP_FontAsset _cachedFont;
        private static int _modalInputLeaseCount;
        private static float _modalPreviousTimeScale = 1f;
        private static bool _modalPreviousCursorVisible;
        private static CursorLockMode _modalPreviousCursorLockState;

        internal static bool IsModalInputPaused
        {
            get { return _modalInputLeaseCount > 0; }
        }

        /// <summary>只读诊断计数。用于跨场景清理和 F3 验收，不改变租约语义。</summary>
        internal static int ModalInputLeaseCount
        {
            get { return _modalInputLeaseCount; }
        }

        internal sealed class ModalInputLease
        {
            internal readonly GameObject InputToken;
            internal readonly string OwnerLabel;
            internal bool InputClaimed;
            private bool active;

            internal ModalInputLease(GameObject inputToken, string ownerLabel)
            {
                InputToken = inputToken;
                OwnerLabel = string.IsNullOrEmpty(ownerLabel) ? "ZombieModeModal" : ownerLabel;
                active = true;
            }

            internal void Release()
            {
                if (!active)
                {
                    return;
                }

                active = false;
                BossRushUIKit.ReleaseModalInput(this);
            }
        }

        internal static ModalInputLease ClaimModalInput(GameObject inputToken, string ownerLabel)
        {
            ModalInputLease lease = new ModalInputLease(inputToken, ownerLabel);
            if (_modalInputLeaseCount == 0)
            {
                _modalPreviousTimeScale = Time.timeScale;
                _modalPreviousCursorVisible = Cursor.visible;
                _modalPreviousCursorLockState = Cursor.lockState;

                Time.timeScale = 0f;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }

            _modalInputLeaseCount++;

            try
            {
                if (inputToken != null)
                {
                    InputManager.DisableInput(inputToken);
                    lease.InputClaimed = true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] " + lease.OwnerLabel + " 输入占用失败: " + e.Message);
            }

            return lease;
        }

        internal static void EnforceModalInputPause()
        {
            if (_modalInputLeaseCount <= 0)
            {
                return;
            }

            Time.timeScale = 0f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private static void ReleaseModalInput(ModalInputLease lease)
        {
            if (lease == null)
            {
                return;
            }

            if (lease.InputClaimed && lease.InputToken != null)
            {
                try
                {
                    InputManager.ActiveInput(lease.InputToken);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ZombieMode] " + lease.OwnerLabel + " 输入释放失败: " + e.Message);
                }
            }

            lease.InputClaimed = false;
            _modalInputLeaseCount = Mathf.Max(0, _modalInputLeaseCount - 1);
            if (_modalInputLeaseCount > 0)
            {
                return;
            }

            Time.timeScale = _modalPreviousTimeScale;
            Cursor.visible = _modalPreviousCursorVisible;
            Cursor.lockState = _modalPreviousCursorLockState;
        }

        /// <summary>
        /// 获取游戏 TMP 字体资产（带缓存），与 HealthBar 名字显示使用相同字体。
        /// </summary>
        internal static TMP_FontAsset GetGameFont()
        {
            if (_cachedFont != null)
            {
                return _cachedFont;
            }

            // 优先从 TMP 全局默认字体获取
            try
            {
                if (TMP_Settings.defaultFontAsset != null)
                {
                    _cachedFont = TMP_Settings.defaultFontAsset;
                    return _cachedFont;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] UIHelper TMP_Settings 读取失败: " + e.Message);
            }

            // 回退：从 HealthBar prefab 的 nameText 获取
            try
            {
                Duckov.UI.HealthBarManager manager = Duckov.UI.HealthBarManager.Instance;
                if (manager != null && manager.healthBarPrefab != null)
                {
                    FieldInfo nameTextField = BossRush.Common.Utils.ReflectionCache.GetField(
                        typeof(Duckov.UI.HealthBar),
                        "nameText",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (nameTextField != null)
                    {
                        TMP_Text nameText = nameTextField.GetValue(manager.healthBarPrefab) as TMP_Text;
                        if (nameText != null && nameText.font != null)
                        {
                            _cachedFont = nameText.font;
                            return _cachedFont;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] UIHelper HealthBar 字体读取失败: " + e.Message);
            }

            // 回退：从场景中任意已有的 TMP_Text 获取
            try
            {
                TMP_Text existing = UnityEngine.Object.FindObjectOfType<TMP_Text>();
                if (existing != null && existing.font != null)
                {
                    _cachedFont = existing.font;
                    return _cachedFont;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] UIHelper TMP_Text 扫描失败: " + e.Message);
            }

            // 最后回退：从已加载的资源中查找
            try
            {
                _cachedFont = ObjectCache.GetFirstTmpFont();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] UIHelper 字体资源回退失败: " + e.Message);
            }

            return _cachedFont;
        }

        /// <summary>
        /// 配置 CanvasScaler 为统一参考分辨率
        /// </summary>
        internal static void ConfigureCanvasScaler(CanvasScaler scaler)
        {
            if (scaler == null)
            {
                return;
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            // 保证参考布局在 4:3、16:10 和超宽屏上完整可见，不沿短边裁掉按钮。
            // Expand 下 matchWidthOrHeight 不参与运算，留着只会让人以为它还在生效。
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }

        internal static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>与 Expand 缩放一致的逻辑视口，供 Canvas 创建前的面板尺寸计算。</summary>
        internal static Vector2 GetReferenceViewportSize()
        {
            float scale = Mathf.Min(Screen.width / ReferenceResolution.x, Screen.height / ReferenceResolution.y);
            return scale > 0f ? new Vector2(Screen.width / scale, Screen.height / scale) : ReferenceResolution;
        }

    }
}
