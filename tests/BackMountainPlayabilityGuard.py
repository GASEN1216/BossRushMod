"""后山玩家入口、输入租约、设施恢复与资源 owner 的静态接线。行为由 BackMountainLifecycle 运行。"""
from pathlib import Path
import re
import sys
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def validate(root):
    def read(path):
        return clean_source((root / path).read_text(encoding='utf-8-sig'))

    def compact(source):
        return re.sub(r'\s+', '', source)

    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    # 2026-09-22 自建登记簿退役：陈列改接官方枪械展示架 / 假人（ShowcaseDisplayScanner 采集、ShowcaseTrophyCatalog 判战利品）。
    # 官方「陈列柜」（要 ShowCase 标签）是废弃建筑，建不了（owner 实机确认）：不得再给 Mod 物品补任何官方展示标签。
    interactable = compact(read('Integration/BackMountain/ShowcaseInteractable.cs'))
    require('NotificationText.Push(' in interactable and 'OpenShowcaseUI' not in interactable,
            'ShowcaseInteractable: 退役的自建柜只提示去官方柜摆放，不再开自绘面板')
    catalog = compact(read('Integration/BackMountain/ShowcaseTrophyCatalog.cs'))
    require('BossRushDynamicItemRegistry.GetPublishedTypeIds()' in catalog and 'BackMountainItems.GetDefinition(typeId)!=null' in catalog
            and 'ShowcaseDisplayJudges.ShouldCountAsTrophy(' in catalog,
            'ShowcaseTrophyCatalog: 枚举源必须是物品注册表、判据走纯函数 ShouldCountAsTrophy、排除后山自产种子/餐食')
    require('AddTagToItem(' not in catalog and '"ShowCase"' not in catalog,
            'ShowcaseTrophyCatalog: 官方陈列柜是废弃建筑，不得再给 Mod 物品补 ShowCase 一类展示标签')
    require('ItemAssetsCollection.GetPrefab(' not in catalog
            and 'BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring(typeId)' in catalog,
            'ShowcaseTrophyCatalog: 取 prefab 只能走 GetRegisteredPrefabWithoutEnsuring；补丁过的 GetPrefab 会对每个 TypeID 强制同步加载 bundle')
    scanner = compact(read('Integration/BackMountain/ShowcaseDisplayScanner.cs'))
    require('item.onSlotContentChanged+=HandleSlotContentChanged;' in scanner
            and 'onSlotContentChanged-=HandleSlotContentChanged;' in scanner
            and 'BuildingManager.OnBuildingBuilt+=HandleBuildingBuilt;' in scanner
            and 'BuildingManager.OnBuildingBuilt-=HandleBuildingBuilt;' in scanner,
            'ShowcaseDisplayScanner: 槽位事件与建筑建成事件必须用命名方法成对订阅/退订')
    require('internalstaticvoidFlushIfDirty(){if(!_dirty)return;' in scanner,
            'ShowcaseDisplayScanner: FlushIfDirty 首句必须 O(1) 早返')
    require('if(!inBaseScene||!unlocked)return;' in scanner,
            'ShowcaseDisplayScanner: 未解锁 / 局内不得扫描订阅')

    runtime = compact(read('Integration/BackMountain/BackMountainRuntimeModule.cs'))
    require('RaidUtilities.OnRaidEnd+=HandleRaidEnded;' in runtime
            and 'RaidUtilities.OnRaidEnd-=HandleRaidEnded;' in runtime,
            'Runtime: 出击结束订阅必须配对清理')
    require('RefreshFacilitiesForScene(context.SceneName);' in runtime,
            'Runtime: 基地 sceneLoaded 必须在 Garden.Start 前尝试按槽恢复')
    require('if(unlockAll!=_lastUnlockAll)' in runtime and 'ShowcaseDisplayScanner.FlushIfDirty();' in runtime,
            'Runtime: 偏好改变及陈列脏刷新必须接入宿主 tick')
    require('GardenSeedInjector.EnsureInjected();' in runtime and runtime.find('GardenSeedInjector.EnsureInjected();') < runtime.find('GardenConstructionSite.EnsureSiteOpen('),
            'Runtime: 菜地工地开门必须排在作物注入之后（Built 子树激活时官方 Garden.Start 会读作物表）')
    require('CampaignBaseObjectives.RegisterProvider(CampaignObjectiveKind.GardenBuilt,GardenConstructionSite.IsGardenBuilt);' in runtime
            and 'CampaignBaseObjectives.RegisterProvider(CampaignObjectiveKind.TrophyDisplayed,ShowcaseService.HasDisplayedTrophy);' in runtime
            and 'CampaignBaseObjectives.UnregisterProvider(CampaignObjectiveKind.GardenBuilt);' in runtime,
            'Runtime: 征程基地侧目标的事实提供者必须由后山登记并在关开关 / 销毁时撤销')
    require('ShowcaseDisplayScanner.ClearSubscriptions();' in runtime, 'Runtime: 场景切换 / 换槽 / 关开关必须退订展示建筑槽位事件')

    items = compact(read('Integration/BackMountain/BackMountainItems.cs'))
    require('map[all[i].LocKey+"_Desc"]=L10n.T(all[i].DescCN,all[i].DescEN);' in items,
            'Items: 官方派生 _Desc 键必须中英注入')
    require('ModeFItemConfigHelper.BindUsageUtilitiesToItem(item,usageUtils,1f);' in items,
            'Items: 使用行为须复用共享绑定，不能只 AddComponent')

    building = compact(read('Integration/BackMountain/ShowcaseBuildingBuilder.cs'))
    require('if(!InjectShowcaseBuildingData())return;' in building
            and 'if(!prefabsList.Contains(buildingComp))prefabsList.Add(buildingComp);' in building,
            'Building: 真实注入成功才锁存，跨槽不能重复追加 prefab')
    require('Shader.Find("UniversalRenderPipeline/Lit")' in building
            and 'renderer.sharedMaterial=material;' in building and '_materials.Clear();' in building
            and 'UnityEngine.Object.Destroy(_iconTexture);' in building,
            'Building: 使用 URP 材质，卸载释放材质/纹理 owner')

    f3 = compact(read('DebugAndTools/F3GameplayValidationBackMountain.cs'))
    require(not any(token in f3 for token in ('ShowcaseService.TryDisplay(', 'ShowcaseService.TryRemoveRecord(',
            'RaidMealService.RegisterMeal(', 'RaidMealService.ClearRegisteredMeal(', 'ShowcaseService.ApplyDisplaySnapshot(',
            'SetActive(', 'EnsureSiteOpen(', 'AddTagToItem(', 'Slot.Plug(', '.Unplug(')),
            'F3: 后山观察用例不得写入虚构收藏或餐食、不得激活工地、不得打标签、不得搬运槽位内容')
    judges = compact(read('Integration/BackMountain/GardenSiteJudges.cs'))
    require('using' not in judges.replace('usingSystem', ''), 'GardenSiteJudges: 纯判据不得引用 Unity / Duckov')
    site = compact(read('Integration/BackMountain/GardenConstructionSite.cs'))
    require('SavesSystem.Save' not in site and 'interactParent.SetActive(true);' in site,
            'GardenConstructionSite: 只激活付费交互的父物体，绝不写官方存档键')
    require('GardenSiteJudges.ShouldOpenSite(' in site and 'GardenSiteJudges.IsGardenBuilt(' in site,
            'GardenConstructionSite: 判据必须走 GardenSiteJudges')

    # 2026-09-23 owner 实测第 15 条：种子必须有稳定来源，且 Boss 掉落不受「Boss掉落随机化」开关左右。
    def body(source, signature):
        start = source.find(signature)
        if start < 0:
            return ''
        opening = source.find('{', start)
        depth = 0
        for index in range(opening, len(source)):
            depth += (source[index] == '{') - (source[index] == '}')
            if depth == 0:
                return source[opening:index + 1]
        return ''

    special = compact(read('LootAndRewards/LootAndRewardsSpecialLoot.cs'))
    require('TryAddBackMountainSeedLoot(inv,bossMain);' in body(special, 'privatevoidReturnPendingExtraLootToCharacterItem('),
            'SeedDrops: 官方箱路径（随机掉落关闭 / 未追踪 / 找不到模板）必须在 ReturnPendingExtraLootToCharacterItem 里投种子')
    random_loot = compact(read('LootAndRewards/LootAndRewardsRandomBossLoot.cs'))
    gate = random_loot.find('if(config==null||!config.enableRandomBossLoot){')
    gate_body = body(random_loot[gate:], 'if(') if gate >= 0 else ''
    require('ReturnPendingExtraLootToCharacterItem(bossMain);' in gate_body,
            'SeedDrops: 「Boss掉落随机化」关闭分支必须走 ReturnPendingExtraLootToCharacterItem（种子与额外掉落进官方箱）')
    mode_ef = random_loot.find('if(allowModeEFIndependentLoot){')
    mode_ef_body = body(random_loot[mode_ef:], 'if(') if mode_ef >= 0 else ''
    require('TryAddBackMountainSeedToCharacterItem(bossMain);return;' in mode_ef_body,
            'SeedDrops: Mode E/F 原生箱分支必须把种子放进 characterItem 再返回')
    hell = random_loot.find('if(infiniteHellMode){')
    hell_body = body(random_loot[hell:], 'if(') if hell >= 0 else ''
    require('TryDropBackMountainSeedIntoWorld(bossMain);FinalizeBossRushLootboxPathTracking(bossMain);' in hell_body,
            'SeedDrops: 无间炼狱没有箱子，种子必须在 Finalize 之前世界投放')
    integration = compact(read('Integration/BossRushIntegration.cs'))
    require('injectedCount+=BackMountainItems.TryInjectSeedsIntoShop(shop,this);'
            in body(integration, 'internalintTryInjectAllBossRushItemsIntoShop('),
            'SeedShop: 基地售货机注入管线必须挂上后山种子')
    shop = body(items, 'internalstaticintTryInjectSeedsIntoShop(')
    require('if(!inst.IsBaseHubNormalMerchantShop(shop))return0;' in shop and 'if(!inst.IsBackMountainConfiguredEnabled())return0;' in shop
            and 'if(!GardenSeedInjector.IsGardenAvailable())return0;' in shop and 'EnsureRuntimeRegistration(def.TypeId)' in shop,
            'SeedShop: 只挂基地普通商人、后山关闭或菜地未开放不挂、未注册的号不挂')
    seeds = compact(read('Integration/BackMountain/GardenSeedInjector.cs'))
    require('internalconststringStarterSeedsSaveKey="BossRush_BackMountain_StarterSeeds_v1";' in seeds,
            'StarterSeeds: 发放标记是存档键（docs/contracts.md），字面值不得改')
    grant = body(seeds, 'internalstaticboolTryGrantStarterSeeds(')
    require(grant.find('if(ReadFlag(StarterSeedsSaveKey))returnfalse;') >= 0
            and 0 <= grant.find('WriteFlag(StarterSeedsSaveKey,') < grant.find('TryGiveStarterSeed('),
            'StarterSeeds: 每槽一次，必须先落标记（回读核对）再发种子')
    refresh = body(runtime, 'privatevoidRefreshFacilitiesForScene(')
    require(0 <= refresh.find('GardenSeedInjector.EnsureInjected();') < refresh.find('GardenSeedInjector.TryGrantStarterSeeds(')
            and 'IsLevelAfterInit()' in refresh[refresh.find('GardenSeedInjector.TryGrantStarterSeeds('):],
            'StarterSeeds: 起步种子排在作物注入之后，且只在主角已就绪时发')
    require('if(starter!=null)Duckov.UI.NotificationText.Push(starter);' in body(runtime, 'privatestaticvoidFlushPendingNotice('),
            'StarterSeeds: 起步种子飘字必须经 FlushPendingNotice 等对话结束再弹')
    return errors


def main():
    errors = validate(ROOT)
    for error in errors:
        print('FAIL ' + error)
    print('BackMountainPlayabilityGuard: ' + ('FAIL' if errors else 'PASS'))
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
