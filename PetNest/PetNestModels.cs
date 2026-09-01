// ============================================================================
// PetNestModels.cs - 遗种巢纯数据模型（实施计划 步骤 1）
// ============================================================================
// 硬约束（tests/PetNestModelsGuard.py 守卫）：
//   - 本文件**不得**出现 `using UnityEngine`：DTO 是可被 guard、单测与 JSON 层
//     独立处理的纯数据，一旦掺进 Unity 类型就无法脱离宿主推理；
//   - 不得有静态可变状态：DTO 只描述形状，任何"当前巢"之类的所有权归 Service；
//   - 不写字段初始化器：容器由 Normalize() 统一兜底，避免"构造出来非空、
//     反序列化出来是 null"的两套真相（同 ModeG DTO 纪律）。
//
// 存档形态：Bundle_v2 以 Save<string> JSON 聚合 nest / expedition / museum；
//   三个 v1 key 只读保留用于迁移。编解码在 PetNest/PetNestJson.cs，
//   落盘在 PetNest/PetNestPersistence.cs。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>崽的生命周期状态。序列化用 int，禁止调整既有取值。</summary>
    internal enum PetNestPetState
    {
        /// <summary>在巢待命。</summary>
        InNest = 0,
        /// <summary>已设为出战席位（进局时生成）。</summary>
        Deployed = 1,
        /// <summary>派出远征中（锁定：不可出战、不可陈列）。</summary>
        OnExpedition = 2,
        /// <summary>本局已重伤退场（回基地后自动复位为 InNest）。</summary>
        Downed = 3,
    }

    /// <summary>远征风险档位。序列化用 int，禁止调整既有取值。</summary>
    internal enum PetNestRiskTier
    {
        /// <summary>平安：0 死亡率，最多空手。</summary>
        Safe = 0,
        /// <summary>风浪：低死亡率，可能负伤。</summary>
        Rough = 1,
        /// <summary>亡命：真实死亡率，押命换稀有产出。</summary>
        Desperate = 2,
    }

    /// <summary>一条战痕。崽的履历，不是单纯惩罚。</summary>
    [Serializable]
    internal sealed class PetNestScarRecord
    {
        /// <summary>留疤时间（UTC ticks）。</summary>
        public long ticks;
        /// <summary>留疤地图（场景名或远征目的地 id）。</summary>
        public string place;
        /// <summary>凶手显示名。</summary>
        public string killer;
        /// <summary>永久 Modifier 的 stat key。</summary>
        public string statKey;
        /// <summary>永久 Modifier 的百分比值（负数表示减益）。</summary>
        public float percent;
    }

    /// <summary>一条出身天赋。数据形态镜像官方 EndowmentEntry 的 ModifierDescription。</summary>
    [Serializable]
    internal sealed class PetNestTalentEntry
    {
        /// <summary>天赋稳定 id（本地化 key 后缀）。</summary>
        public string id;
        /// <summary>作用的 stat key。</summary>
        public string statKey;
        /// <summary>数值（正数为增益）。</summary>
        public float value;
        /// <summary>是否按百分比生效（false 表示直接加常量，如背包格子）。</summary>
        public bool percentage;
    }

    /// <summary>
    /// 成年体快照。首版**只存数据、零 UI、零玩法**，为未来斗蛐蛐（Mode H）留接口。
    /// </summary>
    [Serializable]
    internal sealed class PetNestAdultSnapshot
    {
        /// <summary>快照时间（UTC ticks）。</summary>
        public long ticks;
        /// <summary>成年时等级。</summary>
        public int level;
        /// <summary>成年时最大生命。</summary>
        public float maxHealth;
        /// <summary>成年时伤害系数。</summary>
        public float damageFactor;
        /// <summary>性格 id。</summary>
        public string personalityId;
        /// <summary>生涯场次。</summary>
        public int careerCount;
        /// <summary>成年时的战痕条数。</summary>
        public int scarCount;
    }

    /// <summary>一只崽。</summary>
    [Serializable]
    internal sealed class PetNestPetRecord
    {
        /// <summary>稳定 id（孵化时生成，全局唯一）。</summary>
        public string id;
        /// <summary>血脉 key（官方 preset nameKey 或自定义 Boss 常量）。</summary>
        public string lineageKey;
        /// <summary>玩家起的名字（空表示用血脉默认名）。</summary>
        public string displayName;
        /// <summary>孵化时间（UTC ticks）。</summary>
        public long birthTicks;
        /// <summary>等级。</summary>
        public int level;
        /// <summary>经验。</summary>
        public int exp;
        /// <summary>是否异色（孵化即锁定）。</summary>
        public bool shiny;
        /// <summary>性格 id（孵化即锁定）。</summary>
        public string personalityId;
        /// <summary>生命周期状态。</summary>
        public int state;
        /// <summary>锁定它的远征 id（state==OnExpedition 时非空）。</summary>
        public string lockedByExpeditionId;
        /// <summary>生涯场次（进局 + 远征）。</summary>
        public int careerCount;
        /// <summary>远征次数。</summary>
        public int expeditionCount;
        /// <summary>被合并掉的旧伤条数（战痕上限溢出计数）。</summary>
        public int mergedOldScarCount;
        /// <summary>出身天赋（孵化即锁定，固定 2 条）。</summary>
        public List<PetNestTalentEntry> talents;
        /// <summary>战痕列表（上限见 PetNestTuning.MaxScarsPerPet）。</summary>
        public List<PetNestScarRecord> scars;
        /// <summary>成年体快照（未成年为 null）。</summary>
        public PetNestAdultSnapshot adultSnapshot;

        /// <summary>容器兜底，反序列化后必须调用。</summary>
        public void Normalize()
        {
            if (talents == null) talents = new List<PetNestTalentEntry>();
            if (scars == null) scars = new List<PetNestScarRecord>();
            if (level < 1) level = 1;
            if (level > PetNestTuning.PetMaxLevel) level = PetNestTuning.PetMaxLevel;
            if (exp < 0) exp = 0;
            if (careerCount < 0) careerCount = 0;
        }
    }

    /// <summary>同血脉遗魂账本的一条。</summary>
    [Serializable]
    internal sealed class PetNestSoulLedgerEntry
    {
        /// <summary>血脉 key。</summary>
        public string lineageKey;
        /// <summary>已攒遗魂数。</summary>
        public int souls;
    }

    /// <summary>巢的整体状态（v2 聚合包中的 nest；v1 key 仅作迁移输入）。</summary>
    [Serializable]
    internal sealed class PetNestNestData
    {
        /// <summary>巢里的崽。</summary>
        public List<PetNestPetRecord> pets;
        /// <summary>出战席位（单席；空表示不带崽）。</summary>
        public string deployedPetId;
        /// <summary>遗魂账本。</summary>
        public List<PetNestSoulLedgerEntry> soulLedger;
        /// <summary>巢容量（里程碑可提升）。</summary>
        public int capacity;
        /// <summary>命名序号（生成默认名用）。</summary>
        public int nameSerial;

        /// <summary>容器兜底，反序列化后必须调用。</summary>
        public void Normalize()
        {
            if (pets == null) pets = new List<PetNestPetRecord>();
            if (soulLedger == null) soulLedger = new List<PetNestSoulLedgerEntry>();
            for (int i = 0; i < pets.Count; i++)
            {
                if (pets[i] != null) pets[i].Normalize();
            }
            if (capacity <= 0) capacity = PetNestTuning.DefaultNestCapacity;
        }
    }

    /// <summary>一次远征（存档 key: BossRush_PetNest_Expedition_v1）。</summary>
    [Serializable]
    internal sealed class PetNestExpeditionRecord
    {
        /// <summary>稳定 id。</summary>
        public string id;
        /// <summary>派出的崽 id。</summary>
        public string petId;
        /// <summary>
        /// 出发时固化的崽显示名。真死结算会把 PetRecord 从巢里移除，
        /// 之后 TryGetPet 必然返回 null——恰好就是最需要显示名字的那张黑边卡。
        /// 老档缺失时回落 petId。
        /// </summary>
        public string petDisplayName;
        /// <summary>目的地 id。</summary>
        public string destinationId;
        /// <summary>风险档位（PetNestRiskTier 的 int）。</summary>
        public int riskTier;
        /// <summary>出发时间（现实 UTC ticks）。</summary>
        public long departTicks;
        /// <summary>可结算时间（现实 UTC ticks）。</summary>
        public long returnTicks;
        /// <summary>
        /// 出发时固化的死亡率（0-1）。**随出发记录固化**：
        /// 供纪念碑刻档与"出发前明示"的事后追溯，后续调数值不影响已出发的远征。
        /// </summary>
        public float deathRate;
        /// <summary>出发时固化的成功率（含元素亲和加成，0-1）。</summary>
        public float successRate;
        /// <summary>是否已结算（结算结果先落档，翻牌只回放）。</summary>
        public bool settled;
        /// <summary>是否已翻牌展示过。</summary>
        public bool revealed;
        /// <summary>
        /// 奖励是否已发放。与 settled 分开的第二个标记：
        /// "先落档再发奖"意味着落档成功而发奖失败（或中途崩溃）是可能的，
        /// 只有独立的已发放标记才能让补发既幂等又可恢复。
        /// 老档缺失时回落 false，回基地时会补发一次。
        /// </summary>
        public bool rewardsGranted;
        /// <summary>
        /// 现金是否已到账。**按条目记账的第一格**：EconomyManager.Add 在
        /// Instance==null 时返回 false 而不抛异常，整体重试会让已到账的现金再发一次。
        /// 有了这一格，补发只重做真正失败的部分。
        /// 无现金产出（outcomeCash&lt;=0）时同样置 true，语义是"这一格没有欠账"。
        /// </summary>
        public bool cashGranted;
        /// <summary>
        /// 已成功投递的战利品「件数」游标（按 outcomeLootCounts 展开后的件数计）。
        /// 补发从这里续投，已投出去的绝不重发。
        /// </summary>
        public int grantedLootUnits;
        /// <summary>
        /// 发奖尝试次数（诊断与退避用）。它不再是放弃奖励的上限；欠账会一直保留到成功。
        /// </summary>
        public int rewardGrantAttempts;
        /// <summary>结算结果：崽是否阵亡。</summary>
        public bool outcomeDead;
        /// <summary>结算结果：崽是否负伤（留战痕）。</summary>
        public bool outcomeInjured;
        /// <summary>结算结果：战利品 TypeID 列表。</summary>
        public List<int> outcomeLootTypeIds;
        /// <summary>结算结果：战利品数量列表（与上表等长）。</summary>
        public List<int> outcomeLootCounts;
        /// <summary>结算结果：现金。</summary>
        public long outcomeCash;

        /// <summary>容器兜底，反序列化后必须调用。</summary>
        public void Normalize()
        {
            if (outcomeLootTypeIds == null) outcomeLootTypeIds = new List<int>();
            if (outcomeLootCounts == null) outcomeLootCounts = new List<int>();
        }
    }

    /// <summary>远征总状态（进行中 + 已结算未翻牌）。</summary>
    [Serializable]
    internal sealed class PetNestExpeditionData
    {
        /// <summary>全部远征记录（已翻牌的在结算后移除）。</summary>
        public List<PetNestExpeditionRecord> records;
        /// <summary>id 序号。</summary>
        public int idSerial;

        /// <summary>容器兜底，反序列化后必须调用。</summary>
        public void Normalize()
        {
            if (records == null) records = new List<PetNestExpeditionRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] != null) records[i].Normalize();
            }
        }
    }

    /// <summary>阵亡纪念碑的一行。风险档位**一定要刻**：那是玩家自己按下的选择。</summary>
    [Serializable]
    internal sealed class PetNestMemorialEntry
    {
        /// <summary>崽的名字。</summary>
        public string displayName;
        /// <summary>血脉 key。</summary>
        public string lineageKey;
        /// <summary>殁于何处（远征目的地 id）。</summary>
        public string destinationId;
        /// <summary>当时选的风险档位（PetNestRiskTier 的 int）。</summary>
        public int riskTier;
        /// <summary>出发时固化的死亡率。</summary>
        public float deathRate;
        /// <summary>阵亡时间（UTC ticks）。</summary>
        public long deathTicks;
        /// <summary>生涯场次。</summary>
        public int careerCount;
        /// <summary>是否异色。</summary>
        public bool shiny;
    }

    /// <summary>一个血脉的图鉴统计。</summary>
    [Serializable]
    internal sealed class PetNestLineageStats
    {
        /// <summary>血脉 key。</summary>
        public string lineageKey;
        /// <summary>击杀数。</summary>
        public int kills;
        /// <summary>孵化数。</summary>
        public int hatched;
        /// <summary>异色获得数。</summary>
        public int shinyHatched;
        /// <summary>最高养成等级。</summary>
        public int maxLevel;
        /// <summary>远征次数。</summary>
        public int expeditions;
        /// <summary>是否已解锁图鉴页（首次孵化解锁）。</summary>
        public bool unlocked;
    }

    /// <summary>博物馆总状态（v2 聚合包中的 museum；v1 key 仅作迁移输入）。</summary>
    [Serializable]
    internal sealed class PetNestMuseumData
    {
        /// <summary>按血脉的图鉴统计。</summary>
        public List<PetNestLineageStats> lineages;
        /// <summary>阵亡纪念碑（上限见 PetNestTuning.MaxMemorialEntries）。</summary>
        public List<PetNestMemorialEntry> memorials;
        /// <summary>溢出上限后转入"碑林"的计数。</summary>
        public int mergedMemorialCount;

        /// <summary>容器兜底，反序列化后必须调用。</summary>
        public void Normalize()
        {
            if (lineages == null) lineages = new List<PetNestLineageStats>();
            if (memorials == null) memorials = new List<PetNestMemorialEntry>();
        }
    }

    /// <summary>
    /// 遗种巢 v2 权威聚合状态。三个业务根在一次字符串写入中提交，避免多 key 半成功。
    /// </summary>
    [Serializable]
    internal sealed class PetNestBundleData
    {
        public int generation;
        public PetNestNestData nest;
        public PetNestExpeditionData expedition;
        public PetNestMuseumData museum;

        public void Normalize()
        {
            if (generation < 0) generation = 0;
            if (nest == null) nest = PetNestCodec.CreateDefaultNest();
            if (expedition == null) expedition = PetNestCodec.CreateDefaultExpedition();
            if (museum == null) museum = PetNestCodec.CreateDefaultMuseum();
            nest.Normalize();
            expedition.Normalize();
            museum.Normalize();
        }
    }
}
