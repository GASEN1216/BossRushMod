"""生产压缩图标、异步接线与 Dev 只读采样的结构契约。行为另由 ResourceProduction 夹具执行。"""
from pathlib import Path
import json
import re
from cs_source_util import clean_source

ROOT=Path(__file__).resolve().parents[1]
def source(path): return clean_source((ROOT/path).read_text(encoding='utf8'))
def main():
    errors=[]
    def require(code,tokens,label):
        for token in tokens:
            if token not in code: errors.append(label+': '+token)
    loader=source('Utilities/ResourceBundleLoader.cs')
    require(loader,['AssetBundle.LoadFromFileAsync(path)','operation.Request.completed += operation.Complete;',
        'Assets.completed +=','epoch != generation','operation.Cancel(','consumer();','if (Bundle != null && !Taken) Bundle.Unload(true);'], 'loader lifecycle')
    bootstrap=source('Integration/FactoryResourceLoading.cs')
    require(bootstrap,['yield return ProductionIconCache.Prepare(', 'yield return ItemFactory.LoadAllItemsAsync(owner);',
        'SceneManager.GetActiveScene().handle != scene','yield return ResourceBundleLoader.Prepare(path, true, cancelled, guardedConsume);'], 'factory async wiring')
    icon=source('Integration/ProductionIconCache.cs')
    require(icon,['bundle.LoadAsset<Sprite>(key)','return bundle == null;','sprites.Clear();','bundle.Unload(true);'], 'icon ownership')
    item=source('Integration/ItemFactory.cs')
    require(item,['ProductionIconCache.Get(relativePath)','if (!ProductionIconCache.AllowRawFallback) return null;','if (!retained)'], 'raw fallback')
    require(item,['if (acquired && !retained && bundle != null) bundle.Unload(true);'], 'unpublished item bundle cleanup')
    require(source('Integration/EquipmentFactory.cs'),['if (!retained && bundle != null) bundle.Unload(true);'], 'unpublished equipment bundle cleanup')
    # 看图器回退路径（2026-09-24）：旧写法每次看图都反射调 LoadFromFile、从不 Unload，同一 bundle 第二次加载被 Unity 拒绝。
    # 现在同一个文件只打开一次：先查自己的缓存 -> 再借已打开的同名 bundle -> 才经 ResourceBundleLoader 打开，并按此顺序写回缓存；
    # 卸载只 Unload(false) 自己打开的（不销毁正在用的图），且挂在 Integration 的销毁路径上。
    viewer=source('Integration/UI/ImageViewerUI.cs')
    start=viewer.find('private static AssetBundle AcquireFallbackBundle(')
    acquire=viewer[start:viewer.find('\n        }\n',start)] if start>=0 else ''
    steps=[r'if \(fallbackBundles\.TryGetValue\(key, out entry\)\)\s*\{\s*if \(entry != null && entry\.Bundle != null\) return entry\.Bundle;',
        r'AssetBundle bundle = ItemFactory\.FindAlreadyLoadedAssetBundle\(bundleName\);',
        r'if \(bundle == null\)\s*\{\s*bundle = ResourceBundleLoader\.LoadFromFile\(bundlePath\);\s*owned = bundle != null;',
        r'fallbackBundles\[key\] = new FallbackBundle \{ Bundle = bundle, Owned = owned \};']
    positions=[(lambda m: m.start() if m else -1)(re.search(step,acquire)) for step in steps]
    if -1 in positions or positions!=sorted(positions):
        errors.append('image viewer fallback: cache -> borrow loaded -> load once -> store, in order: '+repr(positions))
    if 'AcquireFallbackBundle(bundlePath, bundleName)' not in viewer: errors.append('image viewer fallback: bundle must come from AcquireFallbackBundle')
    if viewer.count('LoadFromFile(')!=1 or '"LoadFromFile"' in viewer:
        errors.append('image viewer fallback: the only LoadFromFile is ResourceBundleLoader.LoadFromFile inside AcquireFallbackBundle')
    if 'Unload(true)' in viewer: errors.append('image viewer fallback: never Unload(true), the shown sprite would be destroyed')
    reset=viewer[viewer.find('internal static void ResetStaticCaches()'):]
    require(reset,['if (entry == null || !entry.Owned || entry.Bundle == null) continue;','entry.Bundle.Unload(false);',
        'fallbackBundles.Clear();','fallbackCreatedSprites.Clear();'], 'image viewer cache release')
    hooks=source('Integration/IntegrationRuntimeHooks.cs')
    cleanup=hooks[hooks.find('internal void CleanupIntegrationRuntimeOnDestroy()'):]
    if 'ImageViewerUI.ResetStaticCaches()' not in cleanup[:cleanup.find('\n        }')]:
        errors.append('image viewer cache release must run in CleanupIntegrationRuntimeOnDestroy')
    sampling=source('DebugAndTools/F3GameplayValidationResourcePerformance.cs')
    require(sampling,['FrameTimingManager.CaptureFrameTimings();','FrameTimingManager.GetLatestTimings(1, timings)',
        'Time.realtimeSinceStartupAsDouble - started < 10','ResourcePerformanceMetrics.IsComplete(',
        'ResourceBundleLoader.Snapshot()','"resource-performance.json"','"manifest.json"','"summary.md"'], 'performance capture')
    fields=['fps','cpuFrameTime','gpuFrameTime','gcAlloc','monoMemory','totalMemory','textureNativeMemory','meshNativeMemory',
        'batches','setPassCalls','rendererCount','meshCount','colliderCount','assetBundleLoads','cancelled','sceneChanged','read_only',
        'hadLoadCancellation','hadLoadFailure','runId']
    for field in fields:
        if not re.search(r'\b'+field+r'\b',sampling): errors.append('missing metric field: '+field)
    for token in ['SavesSystem','SkyIslandStoryService','Teleport(','Spawn(','SetPosition(','ForceNight','SetInvincible','timeScale =']:
        if token in sampling: errors.append('performance sampler mutates state: '+token)
    for path in ['DebugAndTools/F3GameplayValidationResourcePerformance.cs','DebugAndTools/ResourcePerformanceMetrics.cs']:
        raw=(ROOT/path).read_text(encoding='utf8').strip()
        if not raw.startswith('#if BOSSRUSH_DEV') or not raw.endswith('#endif'): errors.append('missing whole-file Dev guard: '+path)
    manifest=json.loads((ROOT/'tools/resource_release_manifest.json').read_text(encoding='utf8'))
    if manifest.get('targetBundlePolicy',{}).get('rejectUnlisted') is not True: errors.append('release target must reject unlisted bundles')
    if 'Assets/ui/production_icons' not in manifest['bundles']: errors.append('compressed icon bundle not released')
    for path in ['DebugAndTools/F3GameplayValidationRunner.cs','DebugAndTools/F3GameplayValidationSkyIsland.cs']:
        if 'yield return SampleResourcePerformance("RESOURCE_' not in source(path): errors.append('F3 sampling not wired: '+path)
    print('ResourceProductionGuard: '+('FAIL\n'+'\n'.join(errors) if errors else 'PASS'))
    return bool(errors)
if __name__=='__main__': raise SystemExit(main())
