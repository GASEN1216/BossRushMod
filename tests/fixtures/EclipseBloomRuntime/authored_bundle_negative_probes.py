"""在独立资源副本破坏最终 Unity bundle/发布清单，直接运行正式二进制守卫。"""
import hashlib
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
import UnityPy
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / 'Build/eclipse-bloom-bundle-negatives'
GUARD = ROOT / 'tests/EclipseBloomAuthoredBundleGuard.py'
BLOOM = 'Assets/ui/eclipse_bloom_visuals'
SHIELD = 'Assets/Equipment/residual_sun_shield_model'
ICON = 'Assets/Items/residual_sun_shield_icon.png'
FILES = [BLOOM, SHIELD, ICON, 'tools/resource_release_manifest.json']

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def rewrite(root, name, edit):
    path = root / name
    env = UnityPy.load(str(path))
    edit(env)
    path.write_bytes(env.file.save(packer='lz4'))

def missing_manifest(root):
    path = root / FILES[-1]
    data = json.loads(path.read_text(encoding='utf-8-sig'))
    data['bundles'].remove(SHIELD)
    path.write_text(json.dumps(data), encoding='utf-8')

def open_mesh(root):
    def edit(env):
        mesh = env.container['assets/eclipsebloom/meshes/eclipsebloom_petal.asset'].read()
        mesh.m_SubMeshes[0].indexCount -= 3
        mesh.save()
    rewrite(root, BLOOM, edit)

def invert_mesh(root):
    def edit(env):
        mesh = env.container['assets/eclipsebloom/meshes/eclipsebloom_petal.asset'].read()
        stride = 2 if mesh.m_IndexFormat == 0 else 4
        values = bytearray(mesh.m_IndexBuffer)
        for i in range(0, len(values), stride * 3):
            a = bytes(values[i:i+stride]); values[i:i+stride] = values[i+stride:i+2*stride]; values[i+stride:i+2*stride] = a
        mesh.m_IndexBuffer = list(values)
        mesh.save()
    rewrite(root, BLOOM, edit)

def wrong_anchor(root):
    def edit(env):
        for obj in env.objects:
            if obj.type.name != 'Transform': continue
            data = obj.read()
            if data.m_GameObject.read().m_Name == 'GroundPoint':
                data.m_LocalPosition.y += .25
                data.save()
                return
        raise AssertionError('ground anchor absent in baseline')
    rewrite(root, SHIELD, edit)

def wrong_container(root):
    def edit(env):
        for obj in env.objects:
            if obj.type.name != 'AssetBundle': continue
            data = obj.read_typetree()
            key = 'assets/eclipsebloom/meshes/eclipsebloom_core.asset'
            assert sum(path == key for path, info in data['m_Container']) == 1
            data['m_Container'] = [(path.replace('eclipsebloom_core.asset','eclipsebloom_lost.asset'), info) for path, info in data['m_Container']]
            obj.save_typetree(data)
            return
    rewrite(root, BLOOM, edit)

def missing_deferred(root):
    def replace(value):
        if isinstance(value, str): return value.replace('UniversalGBuffer', 'DisabledGBuffer')
        if isinstance(value, dict): return {k:replace(v) for k,v in value.items()}
        if isinstance(value, list): return [replace(v) for v in value]
        if isinstance(value, tuple): return tuple(replace(v) for v in value)
        return value
    def edit(env):
        count = 0
        for obj in env.objects:
            if obj.type.name == 'Shader':
                data = obj.read_typetree()
                if data['m_ParsedForm'].get('m_Name') != 'BossRush/EclipseBloom/Metal': continue
                assert 'UniversalGBuffer' in json.dumps(data['m_ParsedForm'])
                data['m_ParsedForm'] = replace(data['m_ParsedForm'])
                obj.save_typetree(data)
                count += 1
        assert count == 1
    rewrite(root, BLOOM, edit)

def empty_icon(root):
    Image.new('RGBA',(512,512),(0,0,0,0)).save(root / ICON)

def run(root):
    env = dict(os.environ, PYTHONUTF8='1')
    return subprocess.run([sys.executable,str(GUARD),str(root)],capture_output=True,text=True,encoding='utf-8',env=env)

baseline = run(ROOT)
if baseline.returncode:
    raise SystemExit('Baseline not green: ' + baseline.stdout + baseline.stderr)
before = {name:sha(ROOT/name) for name in FILES + ['tests/EclipseBloomAuthoredBundleGuard.py']}
results = []
for name, change, expected in [
    ('unlisted-shield',missing_manifest,'发布清单未唯一登记'),
    ('open-petal',open_mesh,'网格不封闭'),
    ('inside-out-petal',invert_mesh,'总体积非正'),
    ('floating-ground-anchor',wrong_anchor,'地面锚点'),
    ('missing-core-address',wrong_container,'缺少运行时网格路径'),
    ('missing-gbuffer',missing_deferred,'缺少 URP Deferred pass'),
    ('blank-icon',empty_icon,'透明背景或实心主体'),
]:
    target = OUTPUT / name
    for relative in FILES:
        (target/relative).parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(ROOT/relative,target/relative)
    change(target)
    result = run(target)
    output = result.stdout + result.stderr
    (target/'result.txt').write_text(output,encoding='utf-8')
    ok = result.returncode == 1 and expected in output
    results.append({'name':name,'expected':expected,'exit':result.returncode,'pass':ok})
    print(name, 'PASS' if ok else 'FAIL', flush=True)
    if not ok: print(output,flush=True)
after = {name:sha(ROOT/name) for name in before}
assert before == after, 'Original production artifacts changed'
report = {'baseline':'PASS','results':results,'productionShaUnchanged':True,'sha256':after}
(OUTPUT/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
raise SystemExit(0 if all(r['pass'] for r in results) else 1)
