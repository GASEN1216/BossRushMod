// ============================================================================
// SkyIslandFieldcraftBossGear.cs - 头目 / 岛主的专属装备穿在玩家身上时，在岛上拿来做什么（R1–R4）
// ============================================================================
// 从 SkyIslandFieldcraft.cs 拆出来单独放：主文件只在 Tick、Craft、采集与 Dispose 各多一句。
//
// R1：
// - 星工两件套（星铜护目盔 / 星炉背甲 / 星炉背囊任意两件）：渡口工台的配方少耗 1 片残铜片
//   （SkyIslandFieldcraftRules.ForWearer；按下合成、打开面板时现读主角三个槽位，不在每帧路径上）。
// - 观星镜盔：戴着站定 2 秒，40 米内活着的敌人脚下亮一圈星标 4 秒，8 秒冷却。
//   只在主角头盔槽确实是一顶还起作用的观星镜盔时才做事（根 AGENTS §4.12）：没戴时每次推进读一次槽位就早返。
//   星标圈挂在敌人本体上：敌人倒下、被回收时一起消失；离岛时 Dispose 统一收。
// R2–R4（2026-09-15）：按采样节拍读一次主角五个装备槽，写进 <see cref="SkyIslandBossGearWorn"/>，别处只读快照：
// - 悬根猎装任两件：搜刮箱装填时特产机会翻倍（SkyIslandRewardCrate）；
// - 蓑衣农装任两件：割青穗草多一份（本文件的 WithSickleBonus，采集时读）；
// - 断风套任两件：站在桥与中继平台上时 WalkSpeed / RunSpeed 加成（与夜风同一次地面采样，离开即摘）；
// - 静听耳罩：所有头目 / 岛主预警更久（招式控制器读）；苔纱面罩：身边云蚋不躲枪口（SkyIslandGnats 读）；
// - 镜纹甲：写进剧情数据的运行时字段，折翎和解可以不带旧信与航路图（SkyIslandStoryRules）；
// - 旧邮包：信鸽这一趟多送一封（SkyIslandWorldStoryBosses 读）。
// 采样只读五个槽位、不分配；离岛与模块销毁时快照复位、加成摘除。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 主角此刻穿着哪些头目 / 岛主专属装备：由局内 owner（<see cref="SkyIslandFieldcraft"/>）按采样节拍写，招式控制器、剧情与搜刮箱只读。
    /// 离岛（owner Dispose）与模块销毁（SkyIslandBossForge.ResetStaticCaches）时复位。
    /// </summary>
    internal static class SkyIslandBossGearWorn
    {
        internal static int RootweavePieces, SicklePieces, WindbreakPieces;
        internal static bool Earmuffs, MossgauzeMask, MirrorPlate, Mailbag;

        internal static void Reset()
        {
            RootweavePieces = SicklePieces = WindbreakPieces = 0;
            Earmuffs = MossgauzeMask = MirrorPlate = Mailbag = false;
        }
    }

    internal sealed partial class SkyIslandFieldcraft
    {
        private const float SightStillSeconds = 2f;
        private const float SightRange = 40f;
        private const float SightMarkSeconds = 4f;
        private const float SightCooldown = 8f;
        /// <summary>两次推进之间挪动不超过这么远就算站着没动（米）。</summary>
        private const float SightStillTolerance = 0.35f;
        private const string StrideContext = "SkyIslandGalebreakerStride";
        private static readonly Color SightTint = new Color(0.62f, 0.86f, 1f, 1f);

        private readonly List<Transform> sightTargets = new List<Transform>();
        private readonly List<GameObject> sightRings = new List<GameObject>();
        private readonly List<ZombieModeAttributeModifierRecord> strideRecords = new List<ZombieModeAttributeModifierRecord>();
        private Vector3 sightAnchor;
        private float sightStill, sightReadyAt = -1f, sightClearAt = -1f, strideApplied;
        private bool sightExplained, strideExplained;

        /// <summary>主角身上穿着几件星工装备（头盔 / 护甲 / 背包三槽）。合成与合成面板按下时读一次。</summary>
        internal int StarworksPiecesWorn()
        {
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null) return 0;
            try
            {
                Item helm = main.GetHelmatItem();
                Item armor = main.GetArmorItem();
                Slot packSlot = SkyIslandBossForge.FindSlot(main.CharacterItem, "Backpack");
                Item pack = packSlot == null ? null : packSlot.Content;
                return SkyIslandBossRules.StarworksPiecesWorn(helm == null ? 0 : helm.TypeID, armor == null ? 0 : armor.TypeID,
                    pack == null ? 0 : pack.TypeID);
            }
            catch (Exception)
            {
                // 槽位读不到按没穿处理：少一个折扣，不影响合成本身
                return 0;
            }
        }

        /// <summary>由 <see cref="Tick"/> 按规则节拍调用（排在夜风之后：断风套要用同一次地面采样）：穿戴快照、断风套加成、观星镜盔。</summary>
        private void TickBossGear(CharacterMainControl player, float now, float elapsed)
        {
            SampleBossGear(player);
            TickGalebreakerStride(player);
            TickStargazerSight(player, now, elapsed);
        }

        private void SampleBossGear(CharacterMainControl player)
        {
            int helm = 0, armor = 0, pack = 0, mask = 0, headset = 0;
            try
            {
                Item item = player.CharacterItem;
                helm = TypeIn(item, "Helmat");
                armor = TypeIn(item, "Armor");
                pack = TypeIn(item, "Backpack");
                mask = TypeIn(item, "FaceMask");
                headset = TypeIn(item, "Headset");
            }
            catch (Exception)
            {
                // 槽位读不到按没穿处理：这一拍没有任何专属装备效果
                helm = armor = pack = mask = headset = 0;
            }
            SkyIslandBossGearWorn.RootweavePieces = SkyIslandBossRules.SetPiecesWorn(SkyIslandBossRules.RootweaveSet, helm, armor, pack, mask, headset);
            SkyIslandBossGearWorn.SicklePieces = SkyIslandBossRules.SetPiecesWorn(SkyIslandBossRules.SickleSet, helm, armor, pack, mask, headset);
            SkyIslandBossGearWorn.WindbreakPieces = SkyIslandBossRules.SetPiecesWorn(SkyIslandBossRules.WindbreakSet, helm, armor, pack, mask, headset);
            SkyIslandBossGearWorn.Earmuffs = headset == BossRushItemIds.SkyIslandRainhushEarmuffs;
            SkyIslandBossGearWorn.MossgauzeMask = mask == BossRushItemIds.SkyIslandMossgauzeMask;
            SkyIslandBossGearWorn.MirrorPlate = armor == BossRushItemIds.SkyIslandMirrorgrainPlate;
            SkyIslandBossGearWorn.Mailbag = pack == BossRushItemIds.SkyIslandOldMailbag;
            // 剧情数据的运行时字段（不进存档）：折翎认得镜纹甲。存档对象换了（接受新状态）也会在下一拍写回。
            SkyIslandStoryData data = story != null ? story.Current : null;
            if (data != null) data.wearsMirrorArmor = SkyIslandBossGearWorn.MirrorPlate;
        }

        private static int TypeIn(Item characterItem, string slotKey)
        {
            Slot slot = SkyIslandBossForge.FindSlot(characterItem, slotKey);
            return slot == null || slot.Content == null ? 0 : slot.Content.TypeID;
        }

        /// <summary>断风套（任两件）：站在桥与中继平台上时移动加成；幅度不变就不重挂，下桥、脱掉就摘。</summary>
        private void TickGalebreakerStride(CharacterMainControl player)
        {
            float bonus = SkyIslandBossRules.BridgeSpeedBonus(SkyIslandBossGearWorn.WindbreakPieces, windSample.OnBridge);
            if (Mathf.Approximately(bonus, strideApplied)) return;
            RuntimeStatModifierTracker.RemoveAll(strideRecords, StrideContext);
            strideApplied = 0f;
            if (bonus <= 0f) return;
            bool any = RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.WalkSpeed, bonus, modifierSource, strideRecords, StrideContext);
            any |= RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.RunSpeed, bonus, modifierSource, strideRecords, StrideContext);
            if (!any) return;
            strideApplied = bonus;
            if (strideExplained) return;
            strideExplained = true;
            session.Announce(L10n.T("断风套：一踏上桥，风从背后推着你走。", "Galebreaker set: the moment you step onto a bridge, the wind at your back carries you along."), false);
        }

        /// <summary>蓑衣农装（任两件）：割青穗草多一份。采集点每趟只结算一次（结果缓存在 pendingHarvest），所以只加一次。</summary>
        private static SkyIslandYield[] WithSickleBonus(SkyIslandGatherNode node, SkyIslandYield[] yields)
        {
            int bonus = SkyIslandBossRules.GrassBonus(SkyIslandBossGearWorn.SicklePieces);
            if (bonus <= 0 || node == null || node.Kind != SkyIslandGatherKind.Grass || yields == null) return yields;
            for (int i = 0; i < yields.Length; i++)
                if (yields[i].TypeId == BossRushItemIds.SkyIslandGreenearSheaf)
                    yields[i] = new SkyIslandYield(yields[i].TypeId, yields[i].Count + bonus);
            return yields;
        }

        /// <summary>观星镜盔的站定标敌。</summary>
        private void TickStargazerSight(CharacterMainControl player, float now, float elapsed)
        {
            if (sightClearAt > 0f && now >= sightClearAt) ClearSightRings();
            Item helm = null;
            try { helm = player.GetHelmatItem(); }
            catch (Exception)
            {
                // 槽位读不到按没戴处理
                helm = null;
            }
            if (SkyIslandBossForge.PieceBroken(helm, BossRushItemIds.SkyIslandStargazerLensHelm))
            {
                sightStill = 0f;
                return;
            }
            Vector3 position = player.transform.position;
            if ((position - sightAnchor).sqrMagnitude > SightStillTolerance * SightStillTolerance)
            {
                sightAnchor = position;
                sightStill = 0f;
                return;
            }
            sightStill += elapsed;
            if (sightStill < SightStillSeconds || now < sightReadyAt) return;
            sightStill = 0f;
            sightReadyAt = now + SightCooldown;
            int count = session.CopyLivingEnemies(position, SightRange, sightTargets);
            ClearSightRings();
            for (int i = 0; i < sightTargets.Count; i++)
            {
                Transform target = sightTargets[i];
                if (target == null) continue;
                try
                {
                    LineRenderer ring = SkyIslandGroundRing.Create(target, new Vector3(0f, 0.08f, 0f));
                    ring.gameObject.name = "SkyIslandStargazerSightRing";
                    SkyIslandGroundRing.SetShape(ring, 0.9f, 0.2f, SightTint);
                    sightRings.Add(ring.gameObject);
                }
                catch (Exception e) { Debug.LogWarning("[SkyIslandFieldcraft] 观星镜盔星标失败：" + e.Message); }
            }
            sightTargets.Clear();
            sightClearAt = now + SightMarkSeconds;
            if (sightExplained) return;
            sightExplained = true;
            session.Announce(count > 0
                ? string.Format(L10n.T("观星镜盔：40 米内有 {0} 名敌人，脚下亮起了星标。", "Stargazer's lens: {0} enemies within 40 m, marked with stars at their feet."), count)
                : L10n.T("观星镜盔：40 米内看不到敌人。站定一会儿，镜片会替你盯着。", "Stargazer's lens: no enemies within 40 m. Stand still a moment and the lens keeps watch for you."), false);
        }

        private void ClearSightRings()
        {
            for (int i = 0; i < sightRings.Count; i++)
                if (sightRings[i] != null) UnityEngine.Object.Destroy(sightRings[i]);
            sightRings.Clear();
            sightClearAt = -1f;
        }

        /// <summary>离岛：星标圈、断风套加成与穿戴快照一并收（Dispose 里调）。</summary>
        private void DisposeBossGear()
        {
            ClearSightRings();
            try { RuntimeStatModifierTracker.RemoveAll(strideRecords, StrideContext); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandFieldcraft] 断风套加成摘除失败：" + e.Message); }
            strideApplied = 0f;
            SkyIslandBossGearWorn.Reset();
        }
    }
}
