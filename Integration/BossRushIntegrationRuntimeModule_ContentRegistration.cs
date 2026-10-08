using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>扩展本地化注册与装备加载后配置；沿用原 bootstrap 的调用位置和顺序。</summary>
    internal sealed partial class IntegrationRuntimeModule
    {
        internal void InjectLocalization_Extra_Integration()
        {
            LocalizationInjector.InjectUILocalization();
            LocalizationInjector.InjectMapNameLocalizations();
            LocalizationInjector.InjectCommonNPCLocalization();
            LocalizationInjector.InjectCourierNPCLocalization();
            LocalizationInjector.InjectGoblinNPCLocalization();
            LocalizationInjector.InjectNurseNPCLocalization();
            AwenCourierTokenConfig.InjectLocalization();
            LocalizationInjector.InjectColdQuenchFluidLocalization();
            LocalizationInjector.InjectBrickStoneLocalization();
            LocalizationInjector.InjectDiamondLocalization();
            LocalizationInjector.InjectDiamondRingLocalization();
            LocalizationInjector.InjectCalmingDropsLocalization();
            LocalizationInjector.InjectPeaceCharmLocalization();
            DingdangDrawingConfig.InjectLocalization();
            WildHornConfig.InjectLocalization();
            AwenLootSweepTokenConfig.InjectLocalization();
            FactionFlagConfig.InjectLocalization();
            // Mode H 的 BossRush_ModeH_ 键统一来自 Localization/ModeHLocalization.cs
            ModeHLocalization.Inject();
            // 遗种巢的 BossRush_PetNest_ 键统一来自 Localization/PetNestLocalization.cs
            PetNestLocalization.Inject();

            // 随机事件的 BossRush_RandomEvent_ 键：该模块绝大多数文案走内联 L10n.T，
            // 只有商人交互名走官方按 key 查表的 _overrideInteractNameKey，必须注入。
            RandomEventsLocalization.Inject();
            // 日报的 BossRush_DailyReport_ 键统一来自 Localization/DailyReportLocalization.cs
            DailyReportLocalization.Inject();
            // 鸭皇图鉴的 BossRush_Codex_ 键统一来自 Localization/CodexLocalization.cs
            CodexLocalization.Inject();
            // 词缀锻造的 BossRush_Affix 键统一来自 Localization/AffixForgeLocalization.cs
            AffixForgeLocalization.Inject();
            // 鸭王征程的建筑/交互/线索键统一来自 Localization/CampaignLocalization.cs
            CampaignLocalization.Inject();
            // 后山种子与出击餐的 DisplayNameRaw 注入（AGENTS.md 4.4）
            BackMountainItems.InjectLocalization();
            // 后山建筑与交互键统一来自 Localization/BackMountainLocalization.cs
            BackMountainLocalization.Inject();
            // 天空岛物品的 DisplayNameRaw 注入（AGENTS.md 4.4）
            SkyIslandItems.InjectLocalization();
            SkyIslandSceneReferenceBridge.InjectLocalization(); SkyIslandEnemyTiers.ReinjectNames();
            SkyIslandSearchPoint.InjectLocalizations();
            // 搜刮箱交互名「搜集 / Search」（官方 displayNameKey 按 key 查表，切语言时随这条链重注入）
            SkyIslandRewardCrate.InjectLocalizations();
            // 失落的航向仪（Jeff 序章的交付物）的 DisplayNameRaw 注入（AGENTS.md 4.4）
            SkyIslandNavInstrumentConfig.InjectLocalization();
            // 20 处见闻在官方笔记图鉴里的标题与正文（官方查 Note_{key}_Title / _Content）。
            // 文案不在那边另写一份，取的就是 SkyIslandPointText.Name / Lore。
            SkyIslandNoteBridge.InjectNoteKeys();
            // Jeff 序章与岛上三条主线的官方 Quest 标题与说明；任务进度由 Mod 分槽故事事实驱动。
            SkyIslandPreludeFlow.InjectLocalizations(); SkyIslandOfficialQuestTable.InjectLocalizations();
            RespawnItemConfig.InjectLocalization();
            LocalizationInjector.InjectZombieModeLocalization();
            InjectModeFItemLocalization();
            EquipmentLocalization.InjectAllEquipmentLocalizations();
            PhantomWitchScytheLocalization.Inject();
            NewWeaponPlaceholderRegistry.InjectLocalization();
            AstralStaffWeaponConfig.InjectLocalization();
            EmptyMagazineMineWeaponConfig.InjectLocalization();
            _owner.InjectReverseScaleLocalizationFromRuntimeModule();
            LocalizationInjector.InjectWeddingBuildingLocalization();
            ModBehaviour.DevLog("[BossRush] extension localization injected");
        }

        internal void LoadEquipmentContent()
        {
            int equipCount = EquipmentFactory.LoadedBundleCount;
            ModBehaviour.DevLog("[BossRush] 自动加载装备完成，共 " + equipCount + " 个");

            // P0 新武器、P1 套装占位符注册：必须在 EquipmentFactory.LoadAllEquipment 之后执行，
            // 因为它们要把飞行图腾(500010) / 逆鳞(500013) / 龙裔头盔(500003) 等真实装备
            // 作为克隆源；这些装备此时已通过 LoadAllEquipment 进入 ItemAssetsCollection。
            // NewWeaponPlaceholderRegistry 内部会在创建占位符后直接调用对应 WeaponConfig.TryConfigure，
            // 因此 AssetBundle 缺失时占位符也能拿到完整 Stats / 标签 / Buff 配置。
            // ConfigureNewWeaponsAfterLoad 只针对真正从 AssetBundle 加载到 ItemFactory.loadedItems 的物品生效，
            // 占位符不在那条路径上，因此两路互不冲突。
            try
            {
                NewWeaponPlaceholderRegistry.EnsureAllRegistered();
                SetBonusPlaceholderRegistry.EnsureAllRegistered();
            }
            catch (Exception placeholderEx)
            {
                ModBehaviour.DevLog("[BossRush] 装备占位符注册失败: " + placeholderEx.Message);
            }

            DragonKingBossGunRuntime.InitializeRuntime();

            try
            {
                Item fenHuangHalberd = ItemFactory.GetLoadedItem(FenHuangHalberdIds.WeaponTypeId);
                if (fenHuangHalberd != null)
                {
                    FenHuangHalberdWeaponConfig.TryConfigure(fenHuangHalberd, "FenHuangHalberd");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 绑定焚皇断界戟模型失败: " + e.Message);
            }

            try
            {
                Item frostmourne = ItemFactory.GetLoadedItem(FrostmourneIds.WeaponTypeId);
                if (frostmourne != null)
                {
                    FrostmourneWeaponConfig.TryConfigure(frostmourne, "Frostmourne");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 绑定霜之哀伤模型失败: " + e.Message);
            }

            // P0 新武器扩展：配置五把新武器
            NewWeaponRuntime.ConfigureAfterLoad();
        }
    }
}
