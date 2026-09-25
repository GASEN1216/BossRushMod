// Mode E merchant runtime: ModeEShopInteractable.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using TMPro;
using HarmonyLib;
using SodaCraft.StringUtilities;

namespace BossRush
{
    public class ModeEShopInteractable : InteractableBase
    {
        /// <summary>关联的 StockShop 实例</summary>
        private StockShop _shop;

        /// <summary>显示名称（如"枪械"、"护甲"等）</summary>
        private string _displayName;

        /// <summary>
        /// 初始化交互选项
        /// </summary>
        public void Setup(StockShop shop, string displayName)
        {
            _shop = shop;
            _displayName = displayName;
            this.overrideInteractName = true;
            this._overrideInteractNameKey = displayName;
        }

        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                if (!string.IsNullOrEmpty(_displayName))
                    this._overrideInteractNameKey = _displayName;
            }
            catch { }
            try { base.Awake(); } catch { }
            try
            {
                // 禁用碰撞体（作为子交互选项不需要独立碰撞检测）
                this.interactCollider = GetComponent<Collider>();
                if (this.interactCollider != null)
                    this.interactCollider.enabled = false;
            }
            catch { }
            try { this.MarkerActive = false; } catch { }
        }

        protected override void Start()
        {
            try { base.Start(); } catch { }
            try
            {
                // Start 后重新设置名称（防止被 base.Start 覆盖）
                this.overrideInteractName = true;
                if (!string.IsNullOrEmpty(_displayName))
                    this._overrideInteractNameKey = _displayName;
            }
            catch { }
        }

        protected override bool IsInteractable()
        {
            if (_shop == null) return false;
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null) return false;
            return inst.GetModeEShellShopPatchDisposition(_shop) != ModeEShellShopPatchDisposition.Block;
        }

        /// <summary>
        /// 玩家选择此交互选项时，打开对应分类的商店 UI
        /// </summary>
        protected override void OnTimeOut()
        {
            System.Diagnostics.Stopwatch openStopwatch =
                System.Diagnostics.Stopwatch.StartNew();
            long readinessMilliseconds = 0L;
            long showUiMilliseconds = 0L;
            long attachMilliseconds = 0L;
            try
            {
                if (_shop == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] ModeEShopInteractable: _shop 为 null");
                    return;
                }

                ModBehaviour inst = ModBehaviour.Instance;
                ModeEShellShopPatchDisposition disposition = inst != null
                    ? inst.GetModeEShellShopPatchDisposition(_shop)
                    : ModeEShellShopPatchDisposition.Block;
                if (disposition == ModeEShellShopPatchDisposition.Block)
                {
                    if (inst != null) inst.ShowModeEShopLoadingFeedback();
                    return;
                }
                if (disposition == ModeEShellShopPatchDisposition.HandleModeE)
                {
                    long readinessStarted = openStopwatch.ElapsedMilliseconds;
                    bool samplesReady = inst.AreAllModeEShopOfficialSamplesReady(_shop);
                    readinessMilliseconds =
                        openStopwatch.ElapsedMilliseconds - readinessStarted;
                    if (!samplesReady)
                    {
                        inst.ShowModeEShopLoadingFeedback();
                        return;
                    }
                }

                long showUiStarted = openStopwatch.ElapsedMilliseconds;
                _shop.ShowUI();
                showUiMilliseconds = openStopwatch.ElapsedMilliseconds - showUiStarted;
                long attachStarted = openStopwatch.ElapsedMilliseconds;
                ModeEMerchantSellAllUI.Attach(_shop);
                attachMilliseconds = openStopwatch.ElapsedMilliseconds - attachStarted;

                if (openStopwatch.ElapsedMilliseconds >= 16L)
                {
                    int itemCount = _shop.entries != null ? _shop.entries.Count : 0;
                    ModBehaviour.DevLog(
                        "[ModeE] [Profile] shop open sync: merchant=" + _shop.MerchantID +
                        ", items=" + itemCount +
                        ", readinessMs=" + readinessMilliseconds +
                        ", showUiMs=" + showUiMilliseconds +
                        ", attachMs=" + attachMilliseconds +
                        ", totalMs=" + openStopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] ModeEShopInteractable.OnTimeOut 失败: " + e.Message);
            }
        }
    }
}
