namespace BossRush
{
    /// <summary>
    /// 钟守的铜钟罩：与「钟守的旧物匣」并排的第二个交互项，同一好感门槛（5 级）、同一条贝壳兑换事务。
    /// 岛上不保证有 StockShopView，照旧用共享确认框展示单件兑换。
    /// </summary>
    internal sealed class SilenceBellShopInteractable : NPCShopInteractable
    {
        private const string ShopNameKey = "BossRush_SilenceBellShop";

        protected override void SetupInteractName()
        {
            overrideInteractName = true;
            _overrideInteractNameKey = ShopNameKey;
            InteractName = _overrideInteractNameKey;
            LocalizationHelper.InjectLocalization(_overrideInteractNameKey,
                L10n.T("钟守的铜钟罩", "Bell Keeper's Bronze Ward"));
        }

        protected override void DoInteract(CharacterMainControl character)
        {
            if (!CanTrade(character)) return;
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("钟守的铜钟罩", "Bell Keeper's Bronze Ward"),
                Target = L10n.T(SilenceBellConfig.DisplayNameCN, SilenceBellConfig.DisplayNameEN),
                Body = L10n.T("铜片上写着：它不出声，替你挨打时才响。\n每次兑换一件，只收背包里的贝壳。",
                    "Scratched on the brass: It stays silent. It only rings when it takes a hit for you.\nOne item per exchange; seashells must be in your backpack."),
                ConfirmLabel = L10n.T("兑换 · ", "Exchange · ") + SilenceBellConfig.ShellCost + L10n.T(" 贝壳", " seashells"),
                CancelLabel = L10n.T("把钟放回去", "Put the bell back"),
                Danger = false,
                Anchor = gameObject,
                OnConfirm = () => Exchange(character)
            });
        }

        private bool CanTrade(CharacterMainControl player)
        {
            return ReloadMetronomeTrade.CanTradeAt(this, npcId, SilenceBellConfig.VendorNpcId, player);
        }

        private void Exchange(CharacterMainControl player)
        {
            string message;
            ReloadMetronomeTrade.TryExchangeForShells(player, () => CanTrade(player), BossRushItemIds.SilenceBell,
                SilenceBellConfig.ShellCost, L10n.T("钟罩落在肩上，没出一声。", "The ward settles over you without a sound."),
                out message);
            if (player != null) player.PopText(message, 3f);
        }
    }
}
