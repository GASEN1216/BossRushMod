"""运行真实部署脚本到隔离目录：缺包阻止写入、复制/备份/哈希、只读核验。"""
from pathlib import Path
import json
import os
import subprocess
import uuid

ROOT=Path(__file__).resolve().parents[1]


def main():
    if os.name!='nt':
        print('ResourceDeploymentPropertyTest: PARTIAL (Windows PowerShell required)')
        return
    directory=ROOT/'Build/rdt'/uuid.uuid4().hex[:8]
    source=directory/'source';target=directory/'target'
    manifest=json.loads((ROOT/'tools/resource_release_manifest.json').read_text(encoding='utf8'))
    def run(*args):
        return subprocess.run(['powershell','-NoProfile','-ExecutionPolicy','Bypass','-File',
            str(ROOT/'tools/Deploy-ResourceBundles.ps1'),'-SourceRoot',str(source),'-TargetRoot',str(target),*args],
            stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    missing=manifest['bundles'][-1]
    for rel in manifest['bundles'][:-1]:
        p=source/rel;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'UnityFS\0'+rel.encode())
    # A missing source must be rejected before any destination writes.
    assert run().returncode!=0
    assert not target.exists()
    p=source/missing;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'UnityFS\0last')
    stale=target/manifest['bundles'][0];stale.parent.mkdir(parents=True,exist_ok=True);stale.write_bytes(b'previous')
    result=run();assert result.returncode==0,result.stdout
    for rel in manifest['bundles']: assert (source/rel).read_bytes()==(target/rel).read_bytes(),rel
    backups=list((source/'Build/resource-release-backups').rglob(stale.name))
    assert len(backups)==1 and backups[0].read_bytes()==b'previous'
    result=run('-VerifyOnly');assert result.returncode==0,result.stdout
    # Reject old history, disguised extensions and bundles outside Assets, before any writes.
    for rel,payload in [('Assets/arenas/sky_island_world',b'UnityFS\0history'),
                        ('Assets/ui/hidden.png',b'UnityFS\0disguised'),
                        ('nested/extra.bundle',b'UnityRaw\0old'),
                        ('Assets/broken',b'')]:
        extra=target/rel;extra.parent.mkdir(parents=True,exist_ok=True);extra.write_bytes(payload)
        # A listed bundle also needs copying: rejecting only after deployment would corrupt this sentinel.
        stale.write_bytes(b'untouched-by-failed-preflight')
        before={p:p.read_bytes() for p in target.rglob('*') if p.is_file()}
        for options in [(),('-VerifyOnly',)]:
            result=run(*options)
            assert result.returncode!=0 and b'Unlisted AssetBundle' in result.stdout,result.stdout
            assert all(p.read_bytes()==data for p,data in before.items()),'failed preflight modified target'
        extra.unlink()  # Only this test's newly created file in its UUID fixture directory.
    stale.write_bytes(b'changed')
    result=run('-VerifyOnly');assert result.returncode!=0
    assert stale.read_bytes()==b'changed'
    print('ResourceDeploymentPropertyTest: PASS (isolated files, no game directory access)')


if __name__=='__main__':
    main()
