"""Verify the worn-ground cleanup in the shipped scene and the author inputs."""
import argparse
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from clean_lastshift_ground import (ART, ATLAS, BUNDLE, ORIGINAL_ATLAS_SHA256, PROPS, check_bundle,
                                   fbx_colors, pixels, sha)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bundle', type=Path, default=ROOT / 'Assets/arenas' / BUNDLE)
    parser.add_argument('--atlas', type=Path, default=ART / 'export/tex' / ATLAS)
    parser.add_argument('--fbx', type=Path, default=ART / 'fbx' / PROPS)
    parser.add_argument('--fbx-addon', type=Path, default=Path('D:/blender/5.2/scripts/addons_core/io_scene_fbx'))
    args = parser.parse_args()
    if not all(p.is_file() for p in (args.bundle, args.atlas, args.fbx)):
        print('LastShiftGroundFinishPropertyTest: PARTIAL missing local author or release assets')
        return 2
    import numpy as np
    import UnityPy
    from rebuild_lastshift_fbx_layers import Group, load_blender_tools

    try:
        check_bundle(UnityPy.load(str(args.bundle)), pixels(args.atlas))
        if sha(args.atlas.read_bytes()) == ORIGINAL_ATLAS_SHA256:
            raise ValueError('Original atlas still contains the retired paint')
        if args.fbx_addon.is_dir():
            _, codec = load_blender_tools(args.fbx_addon)
            for name, data in fbx_colors(Group(args.fbx, codec)).items():
                if np.any(np.asarray(data)[3::4]):
                    raise ValueError('Author FBX still contains visible tint: ' + name)
        else:
            print('LastShiftGroundFinishPropertyTest: PARTIAL installed FBX codec missing')
            return 2
    except (ValueError, OSError) as error:
        print('LastShiftGroundFinishPropertyTest: FAIL ' + str(error))
        return 1
    print('LastShiftGroundFinishPropertyTest: PASS (scene, source atlas and FBX)')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
