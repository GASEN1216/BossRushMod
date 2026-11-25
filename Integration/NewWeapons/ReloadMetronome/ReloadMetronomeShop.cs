using System;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>岛上不保证有 StockShopView。复用通用商店的好感显隐，用共享弹窗展示单件兑换。</summary>
    internal sealed class ReloadMetronomeShopInteractable : NPCShopInteractable
    {
        protected override void SetupInteractName()
        {
            overrideInteractName = true;
            _overrideInteractNameKey = "BossRush_ReloadMetronomeShop";
            InteractName = _overrideInteractNameKey;
            LocalizationHelper.InjectLocalization(_overrideInteractNameKey,
                L10n.T("钟守的旧物匣", "Bell Keeper's Keepsakes"));
        }

        protected override void DoInteract(CharacterMainControl character)
        {
            if (!CanTrade(character)) return;
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("钟守的旧物匣", "Bell Keeper's Keepsakes"),
                Target = L10n.T(ReloadMetronomeConfig.NameCN, ReloadMetronomeConfig.NameEN),
                Body = L10n.T("铜牌上写着：把耳朵贴近贝壳，里面也有钟声。\n装备在图腾槽，换弹时踩准金色窗口，再按换弹键即可提前完成。每次兑换一件，只收背包里的贝壳。",
                    "The brass plate reads: Hold a shell to your ear. There are bells inside, too.\nEquip in a totem slot and tap reload in the gold window to finish early. One item per exchange; seashells must be in your backpack."),
                ConfirmLabel = L10n.T("兑换 · ", "Exchange · ") + ReloadMetronomeConfig.ShellCost + L10n.T(" 贝壳", " Seashells"),
                CancelLabel = L10n.T("合上匣子", "Close the box"),
                Danger = false,
                Anchor = gameObject,
                OnConfirm = () => Exchange(character)
            });
        }

        private bool CanTrade(CharacterMainControl player)
        {
            return ReloadMetronomeTrade.CanTradeAt(this, npcId, ReloadMetronomeConfig.VendorNpcId, player);
        }

        private void Exchange(CharacterMainControl player)
        {
            string message;
            ReloadMetronomeTrade.TryExchange(player, () => CanTrade(player), out message);
            if (player != null) player.PopText(message, 3f);
        }
    }

    /// <summary>钟守的两只匣子（旧物匣 · 节拍器、铜钟罩 · 止语钟罩）共用的贝壳兑换：同一道门槛、同一条事务。</summary>
    internal static class ReloadMetronomeTrade
    {
        // 库存回调可以重入交互；同一帧也只能结算一份兑换（两只匣子共用这把锁）。
        private static bool busy;

        /// <summary>本人在场景里、6 米内、同场景、未在读图，且好感已解锁钟守的商店。</summary>
        internal static bool CanTradeAt(NPCShopInteractable shop, string shopNpcId, string vendorNpcId, CharacterMainControl player)
        {
            return shop != null && shop.isActiveAndEnabled && shopNpcId == vendorNpcId
                && player != null && ReferenceEquals(player, CharacterMainControl.Main)
                && player.Health != null && !player.Health.IsDead && !SceneLoader.IsSceneLoading
                && player.gameObject.scene == shop.gameObject.scene
                && Vector3.Distance(player.transform.position, shop.transform.position) <= 6f
                && NPCShopSystem.IsShopUnlocked(shopNpcId);
        }

        private static bool TryDeliverToBackpack(Item output, Inventory pack)
        {
            // 通用 SendToPlayer 在满包时会落地；商店只交付进背包，失败时归还预留贝壳。
            try { pack.AddItem(output); }
            catch (Exception e) { Debug.LogWarning("[ReloadMetronome] 入包通知失败: " + e.Message); }
            // 官方先改库存再通知，通知异常不能把已交付商品误算成失败并退款。
            return pack.Content.Contains(output) || SkyIslandInventoryTransaction.HasOwner(output);
        }

        internal static bool TryExchange(CharacterMainControl player, Func<bool> valid, out string message)
        {
            return TryExchangeForShells(player, valid, BossRushItemIds.ReloadMetronome, ReloadMetronomeConfig.ShellCost,
                L10n.T("收下节拍器，试着踩准下一拍。", "Take the metronome. Catch the next beat."), out message);
        }

        /// <summary>预留背包贝壳 → 交付进背包 → 提交；任何一步失败都退回贝壳、销毁未交付的商品。</summary>
        internal static bool TryExchangeForShells(CharacterMainControl player, Func<bool> valid, int typeId, int shellCost,
            string successMessage, out string message)
        {
            message = L10n.T("现在无法兑换。", "Cannot trade right now.");
            if (busy || valid == null || !valid() || player == null || player.CharacterItem == null
                || player.CharacterItem.Inventory == null) return false;
            busy = true;
            Item output = null;
            SkyIslandInventoryTransaction payment = null;
            try
            {
                int shellId = ItemAssetsCollection.TryGetIDByName(SkyIslandShellLottery.ShellItemName, false);
                if (shellId <= 0) return false;
                output = ItemAssetsCollection.InstantiateSync(typeId);
                if (output == null) return false;
                if (!SkyIslandInventoryTransaction.TryReserve(player,
                    new[] { new SkyIslandIngredient(shellId, shellCost) }, out payment))
                {
                    message = L10n.T("背包里的贝壳不够。", "Not enough seashells in your backpack.");
                    return false;
                }
                if (!valid()) return false;
                if (!TryDeliverToBackpack(output, player.CharacterItem.Inventory))
                {
                    message = L10n.T("背包放不下，贝壳已退回。", "No room in your pack. Seashells returned.");
                    return false;
                }
                output = null;
                payment.Commit();
                message = successMessage;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ReloadMetronome] 兑换失败: " + e.Message);
                return false;
            }
            finally
            {
                SkyIslandInventoryTransaction.DestroyUnowned(output);
                try { if (payment != null) payment.Dispose(); }
                finally { busy = false; }
            }
        }
    }
}
