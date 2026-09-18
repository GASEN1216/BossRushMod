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

    ui = compact(read('Integration/BackMountain/ShowcaseUI.cs'))
    require('ZombieModeUIHelper.ClaimModalInput(_root,"TrophyShowcase")' in ui
            and '_modalLease.Release();' in ui, 'ShowcaseUI: 打开与关闭必须配对取得/释放共享输入租约')
    require('privatevoidOnDestroy(){ShowcaseUI.NotifyDestroyed(gameObject);}' in ui
            and 'if(ReferenceEquals(_root,root))Close();' in ui,
            'ShowcaseUI: 场景销毁释放输入，旧画布不得关闭新画布')
    require('if(Input.GetKeyDown(KeyCode.Escape)){Close();return;}' in ui,
            'ShowcaseUI: Esc 必须有关闭入口')
    require('if(ShowcaseService.CanDisplay(ResolveHeldItem(),outdisplayReason))ZombieModeUIHelper.CreateButton(' in ui
            and 'if(ResolveEquippedTrophy()!=null)ZombieModeUIHelper.CreateButton(' in ui
            and 'if(replacement!=null)ZombieModeUIHelper.CreateButton(' in ui,
            'ShowcaseUI: 可见动作须复用实际服务资格，不能挂无事可做的按钮')
    require('ShowcaseService.TryDisplay(item)' in ui and '"Backpack"' in ui,
            'ShowcaseUI: 登记须保留物品复核并可达背包装备')

    runtime = compact(read('Integration/BackMountain/BackMountainRuntimeModule.cs'))
    require('RaidUtilities.OnRaidEnd+=HandleRaidEnded;' in runtime
            and 'RaidUtilities.OnRaidEnd-=HandleRaidEnded;' in runtime,
            'Runtime: 出击结束订阅必须配对清理')
    require('RefreshFacilitiesForScene(context.SceneName);' in runtime,
            'Runtime: 基地 sceneLoaded 必须在 Garden.Start 前尝试按槽恢复')
    require('if(unlockAll!=_lastUnlockAll)' in runtime and 'ShowcaseUI.Tick();' in runtime,
            'Runtime: 偏好改变及面板输入必须接入宿主 tick')

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
            'RaidMealService.RegisterMeal(', 'RaidMealService.ClearRegisteredMeal(')),
            'F3: 后山观察用例不得写入虚构收藏或餐食')
    return errors


def main():
    errors = validate(ROOT)
    for error in errors:
        print('FAIL ' + error)
    print('BackMountainPlayabilityGuard: ' + ('FAIL' if errors else 'PASS'))
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
