"""四建筑拓扑/坐标、发布接线与实际 bundle 资源契约；不代表实机观感。"""
from pathlib import Path
import json
import copy
import os
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from import_building_models import normalize, read_glb
from cs_source_util import clean_source


def validate_bundle(row, entry):
    from verify_unity_resource_release import validate
    errors = validate(row, entry['bundle'])
    assert not errors, errors


def main():
    profile = json.loads((ROOT / 'tools/building_model_manifest.json').read_text(encoding='utf8'))
    manifest = json.loads((ROOT / 'tools/resource_release_manifest.json').read_text(encoding='utf8'))
    assert len(profile['buildings']) == 4
    source_only = os.environ.get('BOSSRUSH_GUARD_SOURCE_ONLY') == '1'
    bootstrap = clean_source((ROOT / 'Integration/IntegrationDeferredBootstrap.cs').read_text(encoding='utf8'))
    for entry in profile['buildings']:
        relative = 'Assets/buildings/' + entry['bundle']
        assert relative in manifest['bundles'], 'missing release entry: ' + relative
        assert 'FactoryResourceLoading.RunSpecial(this, "' + relative + '"' in bootstrap, 'async bootstrap: ' + relative
        path = ROOT / 'output/building_concepts' / entry['source']
        if not source_only:
            assert path.is_file(), 'missing production source: ' + str(path)
            import hashlib
            assert hashlib.sha256(path.read_bytes()).hexdigest() == entry['sha256'], 'source changed'
            pos, normals, uv, indices, _ = read_glb(path)
            result = normalize(pos, normals, uv, indices, entry['box'])
            assert len(result['vertices']) == len(pos) and len(result['triangles']) == len(indices)
            assert len(indices) // 3 <= profile['maxTriangles']
            for a in range(3):
                low = min(v[a] for v in result['vertices']); high = max(v[a] for v in result['vertices'])
                assert high - low <= entry['box'][a] + 1e-6, 'outside footprint/height'
                assert abs(low if a == 1 else low + high) < 1e-6, 'ground-centered pivot'
            # 变换 determinant=-1：绕序翻转；UV V 翻转；不能靠双面材质掩盖轴向错误。
            assert result['triangles'][:3] == [indices[0], indices[2], indices[1]]
            assert result['uv'][0] == [uv[0][0], 1 - uv[0][1]]
            assert result['normals'][0] == [-normals[0][2], normals[0][1], -normals[0][0]]

        bundle = ROOT / relative
        if not source_only:
            assert bundle.is_file(), 'missing production bundle: ' + str(bundle)
            from verify_unity_resource_release import inspect
            from verify_sky_island_bundle_shaders import inspect as shaders
            validate_bundle(inspect(bundle), entry)
            passes, materials, lightmodes = shaders(bundle)
            assert materials.get('BossRush/SkyIsland/Environment') == 1, 'single shared environment material'
            assert passes.get('BossRush/SkyIsland/Environment', 0) > 0, 'compiled shader passes'
            assert 'UniversalGBuffer' in lightmodes['BossRush/SkyIsland/Environment'], 'Deferred GBuffer'
        # 同一生产判据的隔离反例：损坏格式/CPU 副本/超预算/丢别名必须转红。
        good = {'compression':[2], 'container':[entry['prefab']], 'textures':[{'name':'Albedo', 'width':1024, 'height':1024, 'format':'BC7', 'readable':False}],
                'meshes':[{'name':'Model', 'triangles':10000, 'readable':False}]}
        validate_bundle(good, entry)
        for key, value in [('compression',[1]), ('container',['wrong']), ('meshes',[dict(good['meshes'][0],triangles=10001)]),
                           ('textures',[dict(good['textures'][0],format='RGBA32')]), ('textures',[dict(good['textures'][0],width=2048)]),
                           ('meshes',[dict(good['meshes'][0],readable=True)])]:
            bad = copy.deepcopy(good); bad[key] = value
            try: validate_bundle(bad, entry)
            except AssertionError: pass
            else: raise AssertionError('negative case accepted: ' + key)
    for file, field in [('Campaign/CampaignBoardBuilder.cs', 'campaignBoardModelBundle'),
                        ('Integration/BackMountain/ShowcaseBuildingBuilder.cs', 'showcaseModelBundle')]:
        code = clean_source((ROOT / file).read_text(encoding='utf8'))
        assert 'if (!BuildingModelHelper.TryInstantiateBundle(' in code, 'bundle before fallback'
        assert 'TryUnload(' + field + ',' in code and field + ' = null;' in code, 'owner releases lease'
    if source_only:
        print('BaseBuildingResourcePropertyTest: PARTIAL - source checks passed; GLB/Unity bundles not verified')
        return 2
    print('BaseBuildingResourcePropertyTest: PASS (L1/L2 only)')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
