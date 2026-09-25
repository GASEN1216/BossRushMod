using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 引导只读采集：由 CampaignRuntimeModule 每半秒调用，仅采集已接且尚未体验的项目。
    /// 物品树仅在基地确有装备任务时读取一次；不扫描场景、不触发任何装备预热。
    /// </summary>
    internal static class CampaignGuideFacts
    {
        internal static void ObserveAccepted(ModBehaviour owner)
        {
            CampaignSaveData data = CampaignPersistence.Current;
            if (owner == null || data == null || data.acceptedGuides == null || data.acceptedGuides.Length == 0) return;
            List<Item> items = null;
            bool itemsRead = false;
            for (int i = 0; i < data.acceptedGuides.Length; i++)
            {
                string id = data.acceptedGuides[i];
                if (CampaignGuideTable.IsExperienced(id) || CampaignGuideTable.IsCompleted(id)) continue;
                bool done = false;
                try
                {
                    switch (id)
                    {
                        case CampaignGuideTable.ModeD: done = owner.ResolveCampaignCurrentMode() == CampaignContentCatalog.ModeModeD; break;
                        case CampaignGuideTable.ModeE: done = owner.ResolveCampaignCurrentMode() == CampaignContentCatalog.ModeModeE; break;
                        case CampaignGuideTable.ModeF: done = owner.ResolveCampaignCurrentMode() == CampaignContentCatalog.ModeModeF; break;
                        case CampaignGuideTable.Zombie: done = owner.ResolveCampaignCurrentMode() == CampaignContentCatalog.ModeZombie; break;
                        case CampaignGuideTable.ModeG: done = ModeGRuntimeGates.IsModeGRunInProgress; break;
                        case CampaignGuideTable.ModeH:
                            done = owner.ModeHRuntime != null && owner.ModeHRuntime.IsMatchInProgress;
                            break;
                        case CampaignGuideTable.PetNest: done = PetNestService.PetCount > 0 || PetNestCompanionRuntime.HasCompanion; break;
                        case CampaignGuideTable.RandomEvents:
                            RandomEventDirector director = owner.RandomEventsRuntime != null ? owner.RandomEventsRuntime.Director : null;
                            done = director != null && director.EventsFiredThisRun > 0;
                            break;
                        case CampaignGuideTable.Garden: done = CampaignBaseObjectives.IsDone(CampaignObjectiveKind.GardenBuilt); break;
                        case CampaignGuideTable.Trophy: done = CampaignBaseObjectives.IsDone(CampaignObjectiveKind.TrophyDisplayed); break;
                        case CampaignGuideTable.DailyReport: done = CampaignGuideTable.InBase() && DailyReportService.IsSignedToday; break;
                        case CampaignGuideTable.SkyIslandGear:
                        case CampaignGuideTable.AffixForge:
                        case CampaignGuideTable.Reforge:
                            if (!CampaignGuideTable.InBase()) break;
                            if (!itemsRead)
                            {
                                itemsRead = true;
                                CharacterMainControl main = CharacterMainControl.Main;
                                if (main != null && main.CharacterItem != null) items = main.CharacterItem.GetAllChildren(true, true);
                            }
                            done = HasItemEvidence(items, id);
                            break;
                    }
                    // 失败时不置本地 seen 闩；下一拍可以重试。
                    if (done) CampaignPersistence.TryAdvanceGuide(id, 2);
                }
                catch (Exception) { }
            }
        }

        private static bool HasItemEvidence(List<Item> items, string id)
        {
            if (items == null) return false;
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item == null) continue;
                if (id == CampaignGuideTable.SkyIslandGear && SkyIslandBossRules.GearSpec(item.TypeID) != null) return true;
                if (id == CampaignGuideTable.Reforge && ReforgeDataPersistence.HasReforgeData(item)) return true;
                if (id == CampaignGuideTable.AffixForge)
                {
                    for (int slot = 1; slot <= AffixDefinitions.MaxSlots; slot++)
                    {
                        AffixSlotView affix;
                        if (AffixItemData.TryReadSlot(item, slot, out affix) && !affix.IsEmpty) return true;
                    }
                }
            }
            return false;
        }
    }
}
