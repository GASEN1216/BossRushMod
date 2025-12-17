// ============================================================================
// ModeDInteractables.cs - Mode D 交互组件
// ============================================================================
// 模块说明：
//   定义 Mode D 模式专用的交互组件，包括：
//   - 冲下一波：开始下一波敌人
//   - 清空所有箱子：清理场地上所有 BossRush 生成的掉落箱
//   - 清空空箱子：仅清理已被搜刮过的空箱子
//   
// 主要功能：
//   - 提供 Mode D 专用的路牌交互选项
//   - 管理路牌状态切换（难度选择 -> Mode D 选项）
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// Mode D "冲下一波" 交互选项
    /// <para>玩家通过此选项手动触发下一波敌人</para>
    /// </summary>
    public class ModeDNextWaveInteractable : InteractableBase
    {
        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_ModeD_NextWave";
            }
            catch { }
            try { base.Awake(); } catch { }
            try
            {
                this.interactCollider = GetComponent<Collider>();
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = false;
                }
            }
            catch { }
            try { this.MarkerActive = false; } catch { }
        }

        protected override void Start()
        {
            try { base.Start(); } catch { }
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_ModeD_NextWave";
            }
            catch { }
        }

        protected override bool IsInteractable()
        {
            return true;
        }

        protected override void OnTimeOut()
        {
            try
            {
                var mod = ModBehaviour.Instance;
                if (mod == null || !mod.IsModeDActive)
                {
                    Debug.LogWarning("[ModeD] ModeDNextWaveInteractable: Mode D 未激活");
                    return;
                }

                mod.ModeDStartNextWave();

                // 交互后隐藏此选项，直到波次完成
                gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] ModeDNextWaveInteractable.OnTimeOut 失败: " + e.Message);
            }
        }
    }

    /// <summary>
    /// Mode D "清空所有箱子" 交互选项
    /// <para>清理场地上所有由 BossRush 生成的掉落箱（无论是否为空）</para>
    /// </summary>
    public class ModeDClearAllLootboxesInteractable : InteractableBase
    {
        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_ClearAllLootboxes";
            }
            catch { }
            try { base.Awake(); } catch { }
            try
            {
                this.interactCollider = GetComponent<Collider>();
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = false;
                }
            }
            catch { }
            try { this.MarkerActive = false; } catch { }
        }

        protected override void Start()
        {
            try { base.Start(); } catch { }
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_ClearAllLootboxes";
            }
            catch { }
        }

        protected override bool IsInteractable()
        {
            return true;
        }

        protected override void OnTimeOut()
        {
            try
            {
                int removed = 0;
                InteractableLootbox[] boxes = null;

                try
                {
                    boxes = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>();
                }
                catch {}

                if (boxes != null)
                {
                    for (int i = 0; i < boxes.Length; i++)
                    {
                        InteractableLootbox box = boxes[i];
                        if (box == null)
                        {
                            continue;
                        }

                        BossRushLootboxMarker marker = null;
                        try
                        {
                            marker = box.GetComponent<BossRushLootboxMarker>();
                        }
                        catch {}

                        if (marker == null)
                        {
                            continue;
                        }

                        try
                        {
                            if (box.gameObject != null)
                            {
                                removed++;
                                UnityEngine.Object.Destroy(box.gameObject);
                            }
                        }
                        catch {}
                    }
                }

                try
                {
                    var main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        string msg = L10n.T(
                            "已清空 " + removed + " 个箱子",
                            "Cleared " + removed + " lootboxes");
                        main.PopText(msg, -1f);
                    }
                }
                catch {}
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] 清空所有箱子失败: " + e.Message);
            }
        }
    }

    /// <summary>
    /// Mode D "清空空箱子" 交互选项
    /// <para>仅清理已被搜刮过的空掉落箱，保留有物品的箱子</para>
    /// </summary>
    public class ModeDClearEmptyLootboxesInteractable : InteractableBase
    {
        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_ClearEmptyLootboxes";
            }
            catch { }
            try { base.Awake(); } catch { }
            try
            {
                this.interactCollider = GetComponent<Collider>();
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = false;
                }
            }
            catch { }
            try { this.MarkerActive = false; } catch { }
        }

        protected override void Start()
        {
            try { base.Start(); } catch { }
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_ClearEmptyLootboxes";
            }
            catch { }
        }

        protected override bool IsInteractable()
        {
            return true;
        }

        protected override void OnTimeOut()
        {
            try
            {
                int removed = 0;
                InteractableLootbox[] boxes = null;

                try
                {
                    boxes = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>();
                }
                catch {}

                if (boxes != null)
                {
                    for (int i = 0; i < boxes.Length; i++)
                    {
                        InteractableLootbox box = boxes[i];
                        if (box == null)
                        {
                            continue;
                        }

                        BossRushLootboxMarker marker = null;
                        try
                        {
                            marker = box.GetComponent<BossRushLootboxMarker>();
                        }
                        catch {}

                        if (marker == null)
                        {
                            continue;
                        }

                        bool isEmpty = false;
                        try
                        {
                            Inventory inv = box.Inventory;
                            if (inv == null)
                            {
                                isEmpty = true;
                            }
                            else
                            {
                                isEmpty = inv.IsEmpty();
                            }
                        }
                        catch {}

                        if (!isEmpty)
                        {
                            continue;
                        }

                        try
                        {
                            if (box.gameObject != null)
                            {
                                removed++;
                                UnityEngine.Object.Destroy(box.gameObject);
                            }
                        }
                        catch {}
                    }
                }

                try
                {
                    var main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        string msg = L10n.T(
                            "已清空 " + removed + " 个空箱子",
                            "Cleared " + removed + " empty lootboxes");
                        main.PopText(msg, -1f);
                    }
                }
                catch {}
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] 清空空箱子失败: " + e.Message);
            }
        }
    }

    /// <summary>
    /// Mode D 路牌交互扩展
    /// <para>管理 Mode D 模式下路牌的交互选项</para>
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode D 交互选项引用
        
        /// <summary>Mode D "冲下一波" 选项 GameObject</summary>
        private GameObject modeDNextWaveOption = null;
        
        /// <summary>Mode D "清空所有箱子" 选项 GameObject</summary>
        private GameObject modeDClearAllOption = null;
        
        /// <summary>Mode D "清空空箱子" 选项 GameObject</summary>
        private GameObject modeDClearEmptyOption = null;
        
        #endregion
        
        /// <summary>
        /// 设置路牌为 Mode D 模式（隐藏难度选项，显示 Mode D 选项）
        /// </summary>
        public void SetupSignForModeD()
        {
            try
            {
                if (bossRushSignInteract == null)
                {
                    DevLog("[ModeD] SetupSignForModeD: 路牌未找到");
                    return;
                }

                // 移除原有难度选项
                bossRushSignInteract.RemoveDifficultyOptions();

                // 添加 Mode D 专用选项
                AddModeDOptionsToSign();

                DevLog("[ModeD] 路牌已设置为 Mode D 模式");
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] SetupSignForModeD 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 添加 Mode D 选项到路牌
        /// </summary>
        private void AddModeDOptionsToSign()
        {
            try
            {
                if (bossRushSignInteract == null) return;

                Transform signTransform = bossRushSignInteract.transform;

                // 获取路牌的 otherInterablesInGroup 列表
                var field = typeof(InteractableBase).GetField("otherInterablesInGroup",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field == null)
                {
                    Debug.LogError("[ModeD] 无法获取 otherInterablesInGroup 字段");
                    return;
                }

                var list = field.GetValue(bossRushSignInteract) as List<InteractableBase>;
                if (list == null)
                {
                    list = new List<InteractableBase>();
                    field.SetValue(bossRushSignInteract, list);
                }

                // 确保交互组启用
                bossRushSignInteract.interactableGroup = true;

                // 添加"冲下一波"选项
                if (modeDNextWaveOption == null)
                {
                    modeDNextWaveOption = new GameObject("ModeD_NextWave");
                    modeDNextWaveOption.transform.SetParent(signTransform);
                    modeDNextWaveOption.transform.localPosition = Vector3.zero;
                    var nextWaveInteract = modeDNextWaveOption.AddComponent<ModeDNextWaveInteractable>();
                    list.Add(nextWaveInteract);
                }
                modeDNextWaveOption.SetActive(true);

                // 添加"清空所有箱子"选项
                if (modeDClearAllOption == null)
                {
                    modeDClearAllOption = new GameObject("ModeD_ClearAllLootboxes");
                    modeDClearAllOption.transform.SetParent(signTransform);
                    modeDClearAllOption.transform.localPosition = Vector3.zero;
                    var clearAllInteract = modeDClearAllOption.AddComponent<ModeDClearAllLootboxesInteractable>();
                    list.Add(clearAllInteract);
                }
                modeDClearAllOption.SetActive(true);

                // 添加"清空空箱子"选项
                if (modeDClearEmptyOption == null)
                {
                    modeDClearEmptyOption = new GameObject("ModeD_ClearEmptyLootboxes");
                    modeDClearEmptyOption.transform.SetParent(signTransform);
                    modeDClearEmptyOption.transform.localPosition = Vector3.zero;
                    var clearEmptyInteract = modeDClearEmptyOption.AddComponent<ModeDClearEmptyLootboxesInteractable>();
                    list.Add(clearEmptyInteract);
                }
                modeDClearEmptyOption.SetActive(true);

                DevLog("[ModeD] 已添加 Mode D 选项到路牌，当前选项数: " + list.Count);
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] AddModeDOptionsToSign 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 显示 Mode D "冲下一波" 选项
        /// </summary>
        public void ShowModeDNextWaveOption()
        {
            if (modeDNextWaveOption != null)
            {
                modeDNextWaveOption.SetActive(true);
            }
        }

        /// <summary>
        /// 隐藏 Mode D "冲下一波" 选项（波次进行中）
        /// </summary>
        public void HideModeDNextWaveOption()
        {
            if (modeDNextWaveOption != null)
            {
                modeDNextWaveOption.SetActive(false);
            }
        }

        /// <summary>
        /// 清空所有 BossRush 产生的箱子
        /// </summary>
        public void ClearAllBossRushLootboxes()
        {
            try
            {
                int count = 0;
                var allLootboxes = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>();
                foreach (var lootbox in allLootboxes)
                {
                    if (lootbox == null) continue;

                    // 只清理 BossRush 标记的箱子
                    if (lootbox.GetComponent<BossRushLootboxMarker>() != null)
                    {
                        UnityEngine.Object.Destroy(lootbox.gameObject);
                        count++;
                    }
                }

                DevLog("[ModeD] 已清空 " + count + " 个 BossRush 箱子");
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] ClearAllBossRushLootboxes 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清空空的 BossRush 箱子（已被搜刮过的）
        /// </summary>
        public void ClearEmptyBossRushLootboxes()
        {
            try
            {
                int count = 0;
                var allLootboxes = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>();
                foreach (var lootbox in allLootboxes)
                {
                    if (lootbox == null) continue;

                    // 只清理 BossRush 标记的箱子
                    if (lootbox.GetComponent<BossRushLootboxMarker>() == null) continue;

                    // 检查箱子是否为空
                    Inventory inv = lootbox.Inventory;
                    if (inv == null || inv.Content == null || inv.Content.Count == 0)
                    {
                        UnityEngine.Object.Destroy(lootbox.gameObject);
                        count++;
                    }
                }

                DevLog("[ModeD] 已清空 " + count + " 个空的 BossRush 箱子");
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] ClearEmptyBossRushLootboxes 失败: " + e.Message);
            }
        }
    }
}

