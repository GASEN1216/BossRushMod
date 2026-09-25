using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Duckov.Economy;

namespace BossRush
{
    internal sealed partial class ModeERuntimeModule
    {
        private ModeEFMerchantRuntime merchantRuntime;
        internal ModeEFMerchantRuntime MerchantRuntime { get { return merchantRuntime; } }
        private List<StockShop> modeEMerchantShops { get { return merchantRuntime.Shops; } }
        public InteractableBase ModeEMerchantMainInteract { get { return merchantRuntime.ModeEMerchantMainInteract; } }

        /// <summary>Mode E 其他分类商店的固定商品列表</summary>
        private static readonly int[] modeEMerchantOtherItemIds = new int[]
        {
            388,
            RespawnItemConfig.TAUNT_SMOKE_TYPE_ID,
            RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID,
            RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID,
            RespawnItemConfig.ALL_KINGS_BANNER_TYPE_ID
        };

        private void BindMerchantRuntime()
        {
            merchantRuntime = new ModeEFMerchantRuntime(merchantCatalog, modeEMerchantOtherItemIds, new ModeEFMerchantPolicy
            {
                IsSpawnSessionValid = IsModeEOrModeFSpawnSessionStillValid,
                ShellEconomyAvailable = () => modeEShellEconomyAvailable,
                VerifyShellPatchInstallation = VerifyModeEShellPatchInstallation,
                SetShellEconomyUnavailable = SetModeEShellEconomyUnavailable,
                PlayerFaction = () => modeEPlayerFaction,
                SetMerchantHealth = SetModeEMerchantHealth,
                IsModeEActive = () => modeEActive,
                IsModeFActive = () => modeEHost.IsModeFActive,
                BeginShellMerchantGeneration = BeginModeEShellMerchantGeneration,
                RegisterShellMerchantShop = RegisterModeEShellMerchantShop,
                InjectModeFItems = ModBehaviour.TryInjectModeFItemsIntoMerchantShop,
                WarmShellShops = shops => CacheAllModeEShopItemInstancesAsync(modeEShellSessionToken,
                    modeEShellSessionScene, modeEShellSessionGeneration, modeEShellMerchantGeneration, shops).Forget(),
                StartCoroutine = routine => modeEHost.StartCoroutine(routine),
                ClearPetCache = ModeEPetSpawner.ClearCache,
                HasShellSessionState = HasModeEShellSessionState,
                InvalidateShellMerchantGeneration = InvalidateModeEShellMerchantGeneration,
                CreateShopInteraction = CreateMerchantShopInteraction
            });
        }

        private static InteractableBase CreateMerchantShopInteraction(GameObject shopObject, StockShop shop, string locKey)
        {
            var interact = shopObject.AddComponent<ModeEShopInteractable>();
            interact.Setup(shop, locKey);
            return interact;
        }

        internal UniTaskVoid SpawnModeEMerchant(int modeFSessionToken = 0, int modeFRelatedScene = -1,
            int modeESessionToken = 0, int modeESessionRelatedScene = -1)
        { return merchantRuntime.SpawnModeEMerchant(modeFSessionToken, modeFRelatedScene, modeESessionToken, modeESessionRelatedScene); }

        internal void CleanupModeEMerchant() { merchantRuntime.CleanupModeEMerchant(); }

        private System.Collections.IEnumerator WarmModeEMerchantCachesAsync()
        { return merchantCatalog.WarmModeEMerchantCachesAsync(); }

        /// <summary>
        /// Mode E 商人静态缓存兜底清理 — 由 IBossRushRuntimeModule.OnDestroy 统一调用。
        /// 作为 CleanupModeEMerchant 的上位兜底，确保模组/场景销毁时所有静态缓存被完整释放。
        /// </summary>
        internal static void ResetModeEMerchantStaticCaches()
        {
            ModeEFMerchantCatalog.ResetStaticCaches();

            ModeEPetSpawner.ClearCache();
            ModeEMerchantSellAllUI.ResetStaticCaches();
        }
    }
}
