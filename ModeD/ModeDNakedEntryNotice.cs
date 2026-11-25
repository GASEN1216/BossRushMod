using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 白手起家裸装判定 + 「为什么没进去」的玩家提示。
    /// 判定在入口点击、选图、进图各被调用一次以上；提示按拦截项去重，避免同一原因刷屏。
    /// 只在护甲、头盔、背包槽与武器槽全空，仅背包类容器或饰品挡路时提示：带着装备进来的普通挑战玩家不会看到它。
    /// 2026-11-25：背包里带着营旗（划地为营）或船票 + 血猎收发器（血猎追击）却没脱干净时，
    /// 改按那个模式说清卡在哪（任何拦截项都提示，信物本身就说明玩家想进它）；只提示、不拦，入场照常往下判。
    /// </summary>
    internal static class ModeDNakedEntryNotice
    {
        private const float RepeatSeconds = 8f;
        private static NakedEntryBlockKind _lastKind;
        private static string _lastItem;
        private static bool _lastIntent;
        private static float _lastShownAt = -1000f;
        // 营旗 / 收发器的提示刚说过：同一轮入场判定后面不再补白手起家那句
        private static float _intentShownAt = -1000f;

        internal static bool Check(ModBehaviour host, int ticketTypeId)
        {
            NakedEntryBlock block;
            bool naked = ModeEntryInventory.IsPlayerNakedWithAllowedItems("ModeD", ticketTypeId, -1, true, out block);
            if (naked) return true;
            // 划地为营与白手起家同一套裸装判据（只多放行营旗），带着营旗就按划地为营说
            if (CarriesFactionFlag()) Notify(host, block, true, false);
            else if (block.OnlyContainersBlock && Time.unscaledTime - _intentShownAt >= RepeatSeconds) Notify(host, block, false, false);
            return false;
        }

        /// <summary>船票 + 血猎收发器在身但不满足血猎追击的裸装判据：按血猎追击的判据说清卡在哪。</summary>
        internal static void NotifyBloodHuntBlocked(ModBehaviour host, int ticketTypeId)
        {
            NakedEntryBlock block;
            if (ModeEntryInventory.IsPlayerNakedWithAllowedItems("ModeF", ticketTypeId, BloodhuntTransponderConfig.TYPE_ID, false, out block)) return;
            Notify(host, block, true, true);
        }

        private static bool CarriesFactionFlag()
        {
            int[] flagIds = FactionFlagConfig.ALL_FLAG_TYPE_IDS;
            for (int i = 0; i < flagIds.Length; i++)
            {
                if (ModeEntryInventory.FindFirstPlayerInventoryItemByTypeId(flagIds[i]) != null) return true;
            }
            return false;
        }

        private static void Notify(ModBehaviour host, NakedEntryBlock block, bool intent, bool bloodHunt)
        {
            if (host == null) return;
            float now = Time.unscaledTime;
            if (intent && now - _intentShownAt < RepeatSeconds) return;
            if (block.Kind == _lastKind && string.Equals(block.ItemName, _lastItem) && intent == _lastIntent && now - _lastShownAt < RepeatSeconds) return;
            _lastKind = block.Kind;
            _lastItem = block.ItemName;
            _lastIntent = intent;
            _lastShownAt = now;
            if (intent) _intentShownAt = now;
            host.ShowMessage(intent ? DescribeIntent(block, bloodHunt) : Describe(block));
        }

        /// <summary>划地为营 / 血猎追击：哪一件挡路 + 这次不会进入该模式 + 该模式只许带什么。</summary>
        internal static string DescribeIntent(NakedEntryBlock block, bool bloodHunt)
        {
            string item = string.IsNullOrEmpty(block.ItemName) ? L10n.T("东西", "something") : block.ItemName;
            string cn;
            string en;
            switch (block.Kind)
            {
                case NakedEntryBlockKind.EquipmentSlot:
                    cn = "身上还穿着「" + item + "」"; en = "You're still wearing \"" + item + "\""; break;
                case NakedEntryBlockKind.WeaponSlot:
                    cn = "武器栏里还有「" + item + "」"; en = "Your weapon slot still holds \"" + item + "\""; break;
                case NakedEntryBlockKind.AccessorySlot:
                    cn = "身上还戴着「" + item + "」"; en = "You still have \"" + item + "\" on"; break;
                case NakedEntryBlockKind.Backpack:
                    cn = "背包里还有「" + item + "」"; en = "Your bag still holds \"" + item + "\""; break;
                case NakedEntryBlockKind.DogBackpack:
                    cn = "狗子背包里还有「" + item + "」"; en = "Your dog's pack still holds \"" + item + "\""; break;
                case NakedEntryBlockKind.PetNestBackpack:
                    cn = "出战的遗种崽背包里有东西"; en = "Your deployed pet's pack has items"; break;
                default:
                    return bloodHunt
                        ? L10n.T("狗子背包还在读取，稍等一下再出发才能进入血猎追击", "Your dog's pack is still loading. Wait a moment before departing to enter Blood Hunt")
                        : L10n.T("狗子背包还在读取，稍等一下再出发才能进入划地为营", "Your dog's pack is still loading. Wait a moment before departing to enter Faction War");
            }
            return bloodHunt
                ? L10n.T(cn + "，这次不会进入血猎追击（只能带船票和血猎收发器，装备、饰品和随从背包都要清空）",
                    en + ", so this run won't be Blood Hunt (bring only the ticket and the Bloodhunt Transponder; empty all gear, accessories and companion packs)")
                : L10n.T(cn + "，这次不会进入划地为营（只能带船票和营旗，装备、饰品和随从背包都要清空）",
                    en + ", so this run won't be Faction War (bring only the ticket and a banner; empty all gear, accessories and companion packs)");
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
                case NakedEntryBlockKind.AccessorySlot:
                    return L10n.T("身上还戴着「" + item + "」，这次不会进入白手起家（面罩、耳机、图腾也要摘掉）",
                        "You're still wearing \"" + item + "\", so this run won't be From Scratch (take off masks, headsets and totems too)");
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
