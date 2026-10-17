// ============================================================================
// NPCShopStockLedger.cs - NPC 商店库存的存档规则（纯逻辑，不碰 Unity）
// ============================================================================
// 为什么需要它（2026-10-17，P1-1）：
//   NPCShopSystem 每次开店都新建 StockShop 并把库存设回 MaxStock，关店即销毁，
//   于是「限量 1 枚」的钻石戒指关店重开就能接着买，带 Possibility 的商品也能反复开店重抽。
//   现在每只 NPC 的剩余库存跟着好感存档走（AffinityData.shopStockTokens），按游戏日补货。
//
// 规则：
//   - 每件商品一条记录：剩余数量、上次补货的游戏日、本轮是否上架；
//   - RestockDays > 0：距上次补货满 RestockDays 个游戏日（或游戏日倒退）时补满并重掷上架；
//   - RestockDays == 0：终身限量，永不补货（钻石戒指）；
//   - 没有记录的商品（旧档 / 新上架）当作刚补满，旧档行为不变；
//   - MaxStock 下调后剩余数量按新上限截断。
// 记录格式：`typeId:remaining:refillDay:shown`，逗号分隔；坏 token 跳过，不拖垮整份。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace BossRush
{
    /// <summary>一件商品的库存记录。</summary>
    internal struct NPCShopStockSlot
    {
        public int TypeId;
        public int Remaining;
        public int RefillDay;
        public bool Shown;
    }

    /// <summary>NPC 商店库存的解析、补货判定与编码。</summary>
    internal static class NPCShopStockLedger
    {
        /// <summary>RestockDays 取这个值表示终身限量、永不补货。</summary>
        internal const int LifetimeLimited = 0;

        internal static Dictionary<int, NPCShopStockSlot> Parse(string tokens)
        {
            var result = new Dictionary<int, NPCShopStockSlot>();
            if (string.IsNullOrEmpty(tokens)) return result;

            string[] parts = tokens.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string[] fields = parts[i].Split(':');
                int typeId, remaining, refillDay, shown;
                if (fields.Length != 4
                    || !int.TryParse(fields[0], out typeId) || typeId <= 0
                    || !int.TryParse(fields[1], out remaining) || remaining < 0
                    || !int.TryParse(fields[2], out refillDay)
                    || !int.TryParse(fields[3], out shown) || (shown != 0 && shown != 1))
                {
                    continue;
                }

                result[typeId] = new NPCShopStockSlot
                {
                    TypeId = typeId,
                    Remaining = remaining,
                    RefillDay = refillDay,
                    Shown = shown == 1
                };
            }
            return result;
        }

        internal static string Serialize(Dictionary<int, NPCShopStockSlot> slots)
        {
            if (slots == null || slots.Count == 0) return string.Empty;

            var typeIds = new List<int>(slots.Keys);
            typeIds.Sort();
            var builder = new StringBuilder();
            for (int i = 0; i < typeIds.Count; i++)
            {
                NPCShopStockSlot slot = slots[typeIds[i]];
                if (builder.Length > 0) builder.Append(',');
                builder.Append(slot.TypeId).Append(':')
                    .Append(Math.Max(0, slot.Remaining)).Append(':')
                    .Append(slot.RefillDay).Append(':')
                    .Append(slot.Shown ? 1 : 0);
            }
            return builder.ToString();
        }

        /// <summary>
        /// 按当前游戏日决定这件商品本次开店的库存。roll 只在补货时调用一次，返回 [0,1)；
        /// Possibility 的判定与官方 StockShop.DoRefreshStock 一致（只有 0 &lt; p &lt; 1 才会掷）。
        /// </summary>
        internal static NPCShopStockSlot Resolve(bool hasRecord, NPCShopStockSlot record, int typeId,
            int maxStock, int restockDays, float possibility, int currentDay, Func<float> roll)
        {
            int cap = Math.Max(0, maxStock);
            bool refill = !hasRecord
                || (restockDays > 0 && (currentDay < record.RefillDay || currentDay - record.RefillDay >= restockDays));

            if (!refill)
            {
                record.TypeId = typeId;
                record.Remaining = Math.Min(Math.Max(0, record.Remaining), cap);
                return record;
            }

            bool shown = !(possibility > 0f && possibility < 1f && roll != null && roll() > possibility);
            return new NPCShopStockSlot
            {
                TypeId = typeId,
                Remaining = shown ? cap : 0,
                RefillDay = currentDay,
                Shown = shown
            };
        }
    }
}
