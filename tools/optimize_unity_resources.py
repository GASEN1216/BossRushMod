"""Unity 资源审计/优化入口。使用固定工程版本；所有输出留在显式暂存目录。"""
from pathlib import Path
import argparse
import os
import shutil
import subprocess
from datetime import datetime
import hashlib
import json
from unity_project_path import find_unity_project, find_unity_editor

ROOT = Path(__file__).resolve().parents[1]


def build_receipt(output):
    profile = ROOT/('tools/production_icon_manifest.json' if (output/'production-icons.marker').exists() else 'tools/resource_optimization_profile.json')
    builds = json.loads(profile.read_text(encoding='utf8'))['builds']
    if (output/'production-icons.marker').exists(): builds = builds + [{'assetBundleName':'production_icons'}]
    return {'status':'complete','profileSha256':hashlib.sha256(profile.read_bytes()).hexdigest(),
        'bundles':{build['assetBundleName']:hashlib.sha256((output/'rebuilt'/build['assetBundleName']).read_bytes()).hexdigest() for build in builds}}


def validate_build_receipt(output):
    receipt = json.loads((output/'rebuilt-receipt.json').read_text(encoding='utf8'))
    if receipt != build_receipt(output):
        raise ValueError('Rebuilt bundle/profile changed or build is incomplete; build successfully before recompressing')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage', choices=['Inspect', 'Build', 'Recompress', 'TextureProbe', 'IconsBuild'], required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    project, editor = find_unity_project(), find_unity_editor()
    if not project or not editor:
        raise SystemExit('Unity project/editor not found; set BOSSRUSH_UNITY_PROJECT / BOSSRUSH_UNITY_EDITOR')
    project = Path(project)
    expected = (project/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split(': ',1)[1]
    # Editor version is additionally checked by Unity itself in the log.
    print('Project Unity version:', expected, flush=True)
    args.output = args.output.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    receipt_path=args.output/'rebuilt-receipt.json'
    if args.stage=='Recompress' and (args.output/'rebuilt').exists():
        validate_build_receipt(args.output)
    if args.stage in ('Build','IconsBuild'):
        receipt_path.write_text('{"status":"building"}',encoding='utf8')
    if args.stage == 'IconsBuild':
        from PIL import Image
        from apply_unity_texture_policy import rewrite
        profile = json.loads((ROOT/'tools/production_icon_manifest.json').read_text(encoding='utf8'))
        for row in profile['icons']:
            # Unity 2022.3 retains the legacy top-level field even after maxTextureSize's platform API changes.
            # Keep metadata policy and effective Default/Standalone settings in agreement before importing.
            meta = project/'Assets/ProductionIcons'/(row['path']+'.meta')
            if meta.is_file():
                text = meta.read_bytes().decode('utf8')
                updated, _ = rewrite(text, row['maxSize'], True)
                if updated != text: meta.write_bytes(updated.encode('utf8'))
            source = ROOT/row['path']
            target = args.output/'icon-source'/row['path']
            target.parent.mkdir(parents=True,exist_ok=True)
            with Image.open(source) as im:
                im = im.convert('RGBA')
                scale = min(1.0, row['maxSize']/max(im.size))
                size = tuple(max(4,round(v*scale/4)*4) for v in im.size)
                if size != im.size: im = im.resize(size,Image.Resampling.LANCZOS)
                im.save(target)
        (args.output/'production-icons.marker').write_text('production_icon_manifest.json',encoding='utf8')
        shutil.copyfile(ROOT/'tools/ProductionIconBuilder.cs.txt', project/'Assets/Editor/ProductionIconBuilder.cs')
    shutil.copyfile(ROOT/'tools/UnityResourceOptimizer.cs.txt', project/'Assets/Editor/UnityResourceOptimizer.cs')
    env = dict(os.environ, BOSSRUSH_RESOURCE_OUTPUT=str(args.output), BOSSRUSH_RESOURCE_REPO=str(ROOT))
    log = args.output/(args.stage.lower()+'-unity.log')
    if log.exists():
        shutil.copyfile(log,log.with_name(log.stem+'-'+datetime.now().strftime('%H%M%S')+'.log'))
    # Validate desktop texture formats against a real desktop graphics device.
    graphics = ['-nographics'] if args.stage == 'Recompress' else ['-force-d3d11']
    result=subprocess.call([editor, '-batchmode', *graphics, '-projectPath', str(project),
        '-executeMethod', ('BossRush.ProductionIconBuilder.BuildAndExit' if args.stage == 'IconsBuild' else 'BossRush.UnityResourceOptimizer.'+args.stage+'AndExit'), '-logFile', str(log)], env=env)
    if result==0 and args.stage in ('Build','IconsBuild'):
        receipt_path.write_text(json.dumps(build_receipt(args.output),indent=2),encoding='utf8')
    return result


if __name__ == '__main__':
    raise SystemExit(main())
