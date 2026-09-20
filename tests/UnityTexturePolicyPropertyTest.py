"""Exercise real texture policy transforms, platform overrides and byte stability."""
from pathlib import Path
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from apply_unity_texture_policy import iter_texture_metas, match_policy, rewrite


def main():
    template = '''fileFormatVersion: 2
guid: fixture
TextureImporter:
  maxTextureSize: 64
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 64
    crunchedCompression: 1
    overridden: 0
  - serializedVersion: 3
    buildTarget: Standalone
    maxTextureSize: 1024
    crunchedCompression: 1
    overridden: 1
  - serializedVersion: 3
    buildTarget: Android
    maxTextureSize: 2048
    crunchedCompression: 1
    overridden: 0
  spriteSheet:
    sprites: []
  userData: retain this exactly
'''
    for newline in ('\n', '\r\n'):
        original = template.replace('\n', newline)
        rewritten, changes = rewrite(original, 512, True)
        expected = original.replace('maxTextureSize: 64', 'maxTextureSize: 512').replace('maxTextureSize: 1024', 'maxTextureSize: 512')
        expected = expected.replace('crunchedCompression: 1', 'crunchedCompression: 0', 2)
        assert rewritten == expected, 'Only default/active settings may change; preserve every other byte'
        assert len(changes) == 5, 'Top size, two platform sizes and two crunch settings'
        assert rewrite(rewritten, 512, True) == (rewritten, []), 'Repeat transforms must be byte-stable'
        out = ROOT / 'Build/texture-policy-tests'
        out.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=out) as temporary:
            meta = Path(temporary) / 'fixture.png.meta'
            meta.write_bytes(original.encode('utf-8'))
            entries = list(iter_texture_metas(temporary))
            assert len(entries) == 1 and entries[0][2] == original, 'File reader must preserve CRLF too'

    for relative in ('AchievementIcons/new.png', 'MeshyImports/new/weapon.png',
                     'SkyIslandBossGear/Models/armor.png', 'Items/portable_safe_zone_device/icon.png'):
        size, disable_crunch, _ = match_policy(relative)
        assert 128 <= size <= 512 and disable_crunch, 'All item/gear paths must disable crunch: ' + relative
    assert match_policy('UI/ModeG/modeg_echo_emblem.png')[:2] == (256, True), 'Mode G builder requires a 256px emblem'
    assert match_policy('UI/ModeG/modeg_echo_banner.png')[:2] == (1024, True), 'Keep full banner resolution'
    for relative in ('SkyIsland/Textures/terrain.png', 'SkyIsland/Minimap/island.png'):
        assert match_policy(relative) is None, 'Environment policy remains independent'
    print('UnityTexturePolicyPropertyTest: PASS')


if __name__ == '__main__':
    main()
