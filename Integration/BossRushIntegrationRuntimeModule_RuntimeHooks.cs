using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.ItemUsage;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    /// <summary>购买回调、恢复扫描与龙息手持事件的 Integration runtime owner。</summary>
    internal sealed partial class IntegrationRuntimeModule
    {
        private bool _purchaseEventsSubscribed;
        private bool _dragonBreathEffectEventSubscribed;
        private CharacterMainControl _cachedMainCharForEffect;
        private Coroutine _runtimeStateMonitorCoroutine;
        private int _item105PurchaseCount;

        internal int Item105PurchaseCount
        {
            get { return _item105PurchaseCount; }
            set { _item105PurchaseCount = value; }
        }

        internal void StartRuntimeStateMonitor()
        {
            if (_runtimeStateMonitorCoroutine == null && _owner != null)
            {
                _runtimeStateMonitorCoroutine = _owner.StartCoroutine(MonitorLateRuntimeStateRestore());
            }
        }

        internal void StopRuntimeStateMonitor()
        {
            if (_runtimeStateMonitorCoroutine != null)
            {
                if (_owner != null)
                {
                    _owner.StopCoroutine(_runtimeStateMonitorCoroutine);
                }
                _runtimeStateMonitorCoroutine = null;
            }
        }

        internal void SubscribePurchaseEvents()
        {
            if (_purchaseEventsSubscribed) return;
            StockShop.OnItemPurchased += OnItemPurchased_Integration;
            _purchaseEventsSubscribed = true;
        }

        internal void UnsubscribePurchaseEvents()
        {
            if (!_purchaseEventsSubscribed) return;
            StockShop.OnItemPurchased -= OnItemPurchased_Integration;
            _purchaseEventsSubscribed = false;
        }

        private void OnItemPurchased_Integration(StockShop shop, Item item)
        {
            try
            {
                if (shop == null || item == null) return;

                // 仅在 BossRush 加油站中检测
                if (_owner == null || !_owner.IsIntegrationAmmoShop(shop)) return;

                // 检测是否购买了 ID 105 的物品
                if (item.TypeID == 105)
                {
                    _item105PurchaseCount++;

                    // 达到 10 个时显示横幅提示
                    if (_item105PurchaseCount == 10)
                    {
                        _owner.ShowBigBanner(L10n.T("喂喂，你这家伙来这进货了是吗(*´·д·)?", "Hey, are you here to stock up? (*´·д·)?"));
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 处理商店购买事件失败: " + e.Message);
            }
        }

        internal void HandleItemPurchased_Integration(StockShop shop, Item item)
        {
            OnItemPurchased_Integration(shop, item);
        }

        internal System.Collections.IEnumerator DelayedRestoreReforgeDataForInventory()
        {
            // 等待玩家角色可用
            float waitTime = 0f;
            while (CharacterMainControl.Main == null && waitTime < 10f)
            {
                yield return new UnityEngine.WaitForSeconds(0.5f);
                waitTime += 0.5f;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null) yield break;

            Inventory inventory = player.CharacterItem.Inventory;
            if (inventory == null) yield break;

            int restored = 0;
            try
            {
                foreach (Item item in inventory)
                {
                    if (item == null) continue;
                    if (CustomItemRuntimeStateHelper.RestoreRuntimeState(item, "PlayerInventory"))
                    {
                        restored++;
                    }
                }

                restored += RestoreRuntimeStateForSlots(player.CharacterItem, "CharacterSlots");
                restored += RestoreRuntimeStateForHoldAgent(player.CurrentHoldItemAgent, "CurrentHoldItemAgent");

                if (PlayerStorage.Inventory != null)
                {
                    foreach (Item item in PlayerStorage.Inventory)
                    {
                        if (item == null) continue;
                        if (CustomItemRuntimeStateHelper.RestoreRuntimeState(item, "PlayerStorage"))
                        {
                            restored++;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[Reforge] 主动恢复重铸数据异常: " + e.Message);
            }

            if (restored > 0)
            {
                ModBehaviour.DevLog("[Reforge] 场景切换后主动恢复了 " + restored + " 件物品的重铸数据");
            }
        }

        private System.Collections.IEnumerator MonitorLateRuntimeStateRestore()
        {
            WaitForSeconds wait = new WaitForSeconds(0.5f);

            while (true)
            {
                if (!ModBehaviour.CanRunGameplayRuntimeNow(SceneManager.GetActiveScene().name))
                {
                    yield return wait;
                    continue;
                }

                int restored = 0;

                try
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null && player.CharacterItem != null)
                    {
                        restored += RestoreRuntimeStateForInventory(player.CharacterItem.Inventory, "PlayerInventoryMonitor");
                        restored += RestoreRuntimeStateForSlots(player.CharacterItem, "CharacterSlotsMonitor");
                        restored += RestoreRuntimeStateForHoldAgent(player.CurrentHoldItemAgent, "CurrentHoldItemMonitor");
                    }

                    restored += RestoreRuntimeStateForInventory(PlayerStorage.Inventory, "PlayerStorageMonitor");
                }
                catch (System.Exception e)
                {
                    ModBehaviour.DevLog("[Reforge] 运行时状态监控异常: " + e.Message);
                }

                if (restored > 0)
                {
                    ModBehaviour.DevLog("[Reforge] 监控协程补恢复了 " + restored + " 件延迟实例化物品");
                }

                yield return wait;
            }
        }

        private static int RestoreRuntimeStateForInventory(Inventory inventory, string reason)
        {
            if (inventory == null)
            {
                return 0;
            }

            int restored = 0;
            foreach (Item item in inventory)
            {
                if (item == null)
                {
                    continue;
                }

                bool shouldRestore =
                    CustomItemRuntimeStateHelper.IsRuntimeConfiguredType(item.TypeID) ||
                    ReforgeDataPersistence.HasReforgeData(item);

                if (!shouldRestore)
                {
                    continue;
                }

                if (CustomItemRuntimeStateHelper.RestoreRuntimeState(item, reason))
                {
                    restored++;
                }
            }

            return restored;
        }

        private static int RestoreRuntimeStateForSlots(Item characterItem, string reason)
        {
            if (characterItem == null || characterItem.Slots == null)
            {
                return 0;
            }

            int restored = 0;
            foreach (Slot slot in characterItem.Slots)
            {
                if (slot == null || slot.Content == null)
                {
                    continue;
                }

                Item item = slot.Content;
                bool shouldRestore =
                    CustomItemRuntimeStateHelper.IsRuntimeConfiguredType(item.TypeID) ||
                    ReforgeDataPersistence.HasReforgeData(item);

                if (!shouldRestore)
                {
                    continue;
                }

                if (CustomItemRuntimeStateHelper.RestoreRuntimeState(item, reason + ":" + slot.Key))
                {
                    restored++;
                }
            }

            return restored;
        }

        private static int RestoreRuntimeStateForHoldAgent(DuckovItemAgent holdAgent, string reason)
        {
            if (holdAgent == null || holdAgent.Item == null)
            {
                return 0;
            }

            Item item = holdAgent.Item;
            bool shouldRestore =
                CustomItemRuntimeStateHelper.IsRuntimeConfiguredType(item.TypeID) ||
                ReforgeDataPersistence.HasReforgeData(item);

            if (!shouldRestore)
            {
                return 0;
            }

            return CustomItemRuntimeStateHelper.RestoreRuntimeState(item, reason) ? 1 : 0;
        }

        internal System.Collections.IEnumerator DelayedSubscribeDragonBreathEvents()
        {
            // 等待0.5秒确保玩家角色已初始化
            yield return _owner.IntegrationSharedWait05s;
            SubscribeDragonBreathEffectEvent();
        }

        internal System.Collections.IEnumerator DelayedApplyDragonGunAmmoOverride()
        {
            yield return new WaitForSeconds(0.6f);
            try
            {
                DragonKingBossGunRuntime.ReapplyAmmoAttributeOverrideForScene();
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 龙枪弹种属性覆盖异常: " + e.Message);
            }
        }

        internal void SubscribeDragonBreathEffectEvent()
        {
            try
            {
                if (LevelManager.Instance == null) return;
                var mainChar = LevelManager.Instance.MainCharacter;
                if (mainChar == null) return;

                // 如果已订阅同一个角色，跳过
                if (_dragonBreathEffectEventSubscribed && _cachedMainCharForEffect == mainChar) return;

                // 先取消之前的订阅
                UnsubscribeDragonBreathEffectEvent();

                // 订阅手持物品变更事件
                mainChar.OnHoldAgentChanged += OnPlayerHoldAgentChanged;

                _cachedMainCharForEffect = mainChar;
                _dragonBreathEffectEventSubscribed = true;

                ModBehaviour.DevLog("[DragonBreath] 已订阅手持物品变更事件（火焰特效）");

                // 始终订阅Buff事件（龙裔遗族Boss也会发射龙息子弹，需要触发龙焰灼烧Buff）
                DragonBreathBuffHandler.Subscribe();

                DuckovItemAgent currentHoldAgent = mainChar.CurrentHoldItemAgent;
                RestoreRuntimeStateForHoldAgent(currentHoldAgent, "SubscribeDragonBreathEffectEvent");

                // 检查当前手持的武器（处理玩家进入存档时已装备特殊武器的情况）
                if (currentHoldAgent != null &&
                    currentHoldAgent.Item != null &&
                    currentHoldAgent.Item.TypeID == FrostmourneIds.WeaponTypeId)
                {
                    FrostmourneWeaponConfig.TryAddIceEffectsToGraphic(currentHoldAgent.gameObject);
                }

                var currentGun = mainChar.GetGun();
                if (currentGun != null)
                {
                    // 添加火焰特效
                    DragonBreathWeaponConfig.TryAddFireEffectsToAgent(currentGun);
                    DragonKingBossGunRuntime.TryAddFireEffectsToAgent(currentGun);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreath] 订阅火焰特效事件失败: " + e.Message);
            }
        }

        internal void UnsubscribeDragonBreathEffectEvent()
        {
            CharacterMainControl subscribedCharacter = _cachedMainCharForEffect;
            try
            {
                if (!_dragonBreathEffectEventSubscribed || subscribedCharacter == null) return;

                subscribedCharacter.OnHoldAgentChanged -= OnPlayerHoldAgentChanged;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreath] [WARNING] 取消订阅火焰特效事件失败: " + e.Message);
            }
            finally
            {
                _cachedMainCharForEffect = null;
                _dragonBreathEffectEventSubscribed = false;
            }
        }

        private void OnPlayerHoldAgentChanged(DuckovItemAgent newAgent)
        {
            RestoreRuntimeStateForHoldAgent(newAgent, "OnHoldAgentChanged");

            bool isFrostmourne = newAgent != null &&
                                 newAgent.Item != null &&
                                 newAgent.Item.TypeID == FrostmourneIds.WeaponTypeId;
            var gunAgent = newAgent as ItemAgent_Gun;

            // 检查是否为龙息武器
            bool isDragonBreath = gunAgent != null &&
                                  gunAgent.Item != null &&
                                  gunAgent.Item.TypeID == DragonBreathConfig.WEAPON_TYPE_ID;
            bool isDragonKingBossGun = gunAgent != null &&
                                       gunAgent.Item != null &&
                                       gunAgent.Item.TypeID == DragonKingBossGunConfig.WeaponTypeId;

            if (isFrostmourne)
            {
                FrostmourneWeaponConfig.TryAddIceEffectsToGraphic(newAgent.gameObject);
            }
            else if (isDragonBreath)
            {
                // 装备龙息武器：添加火焰特效
                DragonBreathWeaponConfig.TryAddFireEffectsToAgent(gunAgent);
            }
            else if (isDragonKingBossGun)
            {
                DragonKingBossGunRuntime.TryAddFireEffectsToAgent(gunAgent);
            }
            // 不再在此处取消订阅Buff事件，因为Boss的龙息子弹也需要触发龙焰灼烧Buff
        }
    }
}
