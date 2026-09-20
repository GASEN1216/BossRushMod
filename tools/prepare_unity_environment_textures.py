"""保留天空岛手绘原图，生成 1024² 环境贴图交付副本。"""
from pathlib import Path
import hashlib
import json
from PIL import Image
from unity_project_path import find_unity_project


def main():
    project=Path(find_unity_project())
    profile=json.loads(Path(__file__).with_name('resource_optimization_profile.json').read_text(encoding='utf8'))
    report=[]
    for relative in profile['skyTextures']:
        source=project/relative
        dest=source.parent/'Production'/source.name
        dest.parent.mkdir(parents=True,exist_ok=True)
        before=hashlib.sha256(source.read_bytes()).hexdigest()
        with Image.open(source) as image:
            if image.size!=(1254,1254): raise ValueError('Environment source size changed: '+relative)
            # Unity 2022.3 import probes confirm BC7 requires a power-of-two size.
            # 1024 is the nearest standard tier to 1254; preserve aspect and UVs.
            image.resize((1024,1024),Image.Resampling.LANCZOS).save(dest)
        if hashlib.sha256(source.read_bytes()).hexdigest()!=before: raise RuntimeError('Source was modified')
        report.append({'source':relative,'sourceSha256':before,'generated':dest.relative_to(project).as_posix(),
            'size':[1024,1024],'sha256':hashlib.sha256(dest.read_bytes()).hexdigest()})
    print(json.dumps(report,indent=2))


if __name__=='__main__':
    main()
