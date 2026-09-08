"""前哨地图必须归会话所有，复用官方 UI，禁止把临时标记写进地堡存档。"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
folder = ROOT / 'DebugAndTools/ArenaPrototype'
mapping = (folder / 'StoneOutpostMap.cs').read_text(encoding='utf-8-sig')
lease = (folder / 'StoneOutpostMapDataLease.cs').read_text(encoding='utf-8-sig')
session = (folder / 'ArenaPrototypeSession.cs').read_text(encoding='utf-8-sig')
errors = []
for token in ('map.Apply(arena, origin)', 'map.MarkSearched(marker.name)', 'map.Tick()', 'map.Dispose()'):
    if token not in session:
        errors.append('会话地图生命周期缺少 ' + token)
close = session.split('internal void Close(', 1)[1]
if close.index('map.Dispose()') > close.index('sceneLease.Release('):
    errors.append('地图数据须在场景卸载前返还')
for token in ('ReferenceEquals(target.maps, ownedMaps)', 'target.maps = previousMaps', 'settings = null',
              'target.combinedSprite = previousSprite', 'target.combinedCenter = previousCenter', 'target.combinedSize = previousSize'):
    if token not in lease:
        errors.append('地图租约缺少 ' + token)
for token in ('Active = null', 'pointsRoot.SetActive(false)', 'Object.Destroy(sprite)', 'Object.Destroy(texture)',
              'map.OwnsDisplay(__instance.Master)', 'map.PlacePin(world)', 'map.OwnsPoint(poi)', 'MAP_READY'):
    if token not in mapping:
        errors.append('地图资源/临时标记约束缺少 ' + token)
for token in ('MapMarkerManager.Request(', 'SavesSystem.Save', 'PlayerPrefs.Set', 'SceneInfoCollection.Entries.Add',
              'new Canvas', 'Resources.GetBuiltinResource'):
    if token in mapping + lease:
        errors.append('前哨地图不允许 ' + token)
compile_text = (ROOT / 'compile_official.bat').read_text(encoding='utf-8-sig')
for name in ('StoneOutpostMap.cs', 'StoneOutpostMapDataLease.cs', 'StoneOutpostMapRaster.cs'):
    if 'DebugAndTools\\ArenaPrototype\\' + name not in compile_text:
        errors.append('编译清单缺少 ' + name)
if errors:
    raise SystemExit('StoneOutpostMapOwnershipGuard: FAIL\n' + '\n'.join(errors))
print('StoneOutpostMapOwnershipGuard: PASS')
