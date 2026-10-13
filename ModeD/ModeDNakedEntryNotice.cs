using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 白手起家裸装判定 + 「为什么没进去」的玩家提示。
    /// 判定在入口点击、选图、进图各被调用一次以上；提示按拦截项去重，避免同一原因刷屏。
    /// 只在装备槽、武器槽全空、仅背包类容器挡路时提示：带着装备进来的普通挑战玩家不会看到它。
    /// </summary>
    internal static class ModeDNakedEntryNotice
    {
        private const float RepeatSeconds = 8f;
        private static NakedEntryBlockKind _lastKind;
        private static string _lastItem;
        private static float _lastShownAt = -1000f;

        internal static bool Check(ModBehaviour host, int ticketTypeId)
        {
            NakedEntryBlock block;
            bool naked = ModeEntryInventory.IsPlayerNakedWithAllowedItems("ModeD", ticketTypeId, -1, true, out block);
            if (!naked && block.OnlyContainersBlock) Notify(host, block);
            return naked;
        }

        private static void Notify(ModBehaviour host, NakedEntryBlock block)
        {
            if (host == null) return;
            float now = Time.unscaledTime;
            if (block.Kind == _lastKind && string.Equals(block.ItemName, _lastItem) && now - _lastShownAt < RepeatSeconds) return;
            _lastKind = block.Kind;
            _lastItem = block.ItemName;
            _lastShownAt = now;
            host.ShowMessage(Describe(block));
        }

        internal static string Describe(NakedEntryBlock block)
        {
            string item = string.IsNullOrEmpty(block.ItemName) ? L10n.T("东西", "items") : block.ItemName;
            switch (block.Kind)
            {
                case NakedEntryBlockKind.DogBackpack:
                    return L10n.T("狗子背包里还有「" + item + "」，这次不会进入白手起家（只有船票可以留着）",
                        "Your dog's pack still holds \"" + item + "\", so this run won't be From Scratch (only the ticket may stay)");
                case NakedEntryBlockKind.PetNestBackpack:
                    return L10n.T("出战的遗种崽背包里有东西，这次不会进入白手起家；先清空它的背包",
                        "Your deployed pet's pack has items, so this run won't be From Scratch. Empty its pack first");
                case NakedEntryBlockKind.Backpack:
                    return L10n.T("身上还带着「" + item + "」，这次不会进入白手起家（只有船票可以带）",
                        "You're still carrying \"" + item + "\", so this run won't be From Scratch (only the ticket is allowed)");
                default:
                    return L10n.T("狗子背包还在读取，稍等一下再出发才能进入白手起家",
                        "Your dog's pack is still loading. Wait a moment before departing to enter From Scratch");
            }
        }
    }
}
