// ============================================================================
// RuntimeStatModifierTracker.cs - 运行时临时 Stat Modifier 跟踪 helper
// ============================================================================
// 模块说明：
//   ZombieMode / ModeE 等模式内多次出现的"加 Modifier + 记录 + 后续移除"模式
//   （Hunter Frenzy / Player Slow / Reward Attribute / Adaptive Affix 共 6 处）
//   收口到此处，避免 6 处副本导致：
//     - ModifierType 选错（Add vs PercentageAdd 不一致 → 减速/加速被装备稀释）
//     - 字段命名漂移（CharacterItem / Stat / Modifier / StatName）
//     - 日志格式不统一
//
// 与 EquipmentHelper.AddModifierToItem 的区别：
//   - EquipmentHelper 加的是装备 ModifierDescription（持久词条）
//   - 本类加的是运行时 Modifier（短命 Buff，不会被序列化）
//
// 注：调用方仍然自带 List<ZombieModeAttributeModifierRecord> 容器，
//     因为 SkillState / Run state / Pollution affix 各自有不同的生命周期；
//     helper 只负责"如何加 + 如何统一移除"两个机械动作。
// ============================================================================

using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    internal static class RuntimeStatModifierTracker
    {
        /// <summary>
        /// 加 Modifier 到 character 的指定 stat 上，记录到 records 容器。
        /// percent 是百分比变化量（0.30 = +30%，-0.50 = -50%），统一用 PercentageAdd
        /// 以避免装备倍率 modifier 把 Add 类型常量稀释（见审查 §3.2）。
        /// </summary>
        internal static bool TryAdd(
            CharacterMainControl character,
            string statName,
            float percent,
            object source,
            List<ZombieModeAttributeModifierRecord> records,
            string context)
        {
            if (character == null || character.CharacterItem == null ||
                string.IsNullOrEmpty(statName) || records == null || source == null)
            {
                return false;
            }

            if (System.Math.Abs(percent) < 0.0001f)
            {
                return false;
            }

            try
            {
                Stat stat = character.CharacterItem.GetStat(statName);
                if (stat == null)
                {
                    return false;
                }

                Modifier modifier = new Modifier(ModifierType.PercentageAdd, percent, source);
                stat.AddModifier(modifier);

                ZombieModeAttributeModifierRecord record = new ZombieModeAttributeModifierRecord();
                record.CharacterItem = character.CharacterItem;
                record.Stat = stat;
                record.Modifier = modifier;
                record.StatName = statName;
                records.Add(record);
                return true;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[RuntimeStatModifier] " + context + " add 失败: " + statName + ", " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 移除 records 容器内全部 Modifier，并清空容器。
        /// 反向迭代以容忍部分失败；失败用 DevLog 记。
        /// </summary>
        internal static void RemoveAll(
            List<ZombieModeAttributeModifierRecord> records,
            string context)
        {
            if (records == null)
            {
                return;
            }

            for (int i = records.Count - 1; i >= 0; i--)
            {
                ZombieModeAttributeModifierRecord record = records[i];
                if (record == null || record.Modifier == null)
                {
                    continue;
                }

                try
                {
                    Stat stat = record.Stat;
                    if (stat == null && record.CharacterItem != null && !string.IsNullOrEmpty(record.StatName))
                    {
                        stat = record.CharacterItem.GetStat(record.StatName);
                    }

                    if (stat != null)
                    {
                        stat.RemoveModifier(record.Modifier);
                    }
                }
                catch (System.Exception e)
                {
                    ModBehaviour.DevLog("[RuntimeStatModifier] " + context + " remove 失败: " + e.Message);
                }
            }

            records.Clear();
        }
    }
}
