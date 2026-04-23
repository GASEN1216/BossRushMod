from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
ASSET_MANAGER = ROOT / "Integration" / "PhantomWitch" / "PhantomWitchAssetManager.cs"
AMBIENT = ROOT / "Integration" / "PhantomWitch" / "PhantomWitchAmbientPresence.cs"
VFX = ROOT / "Integration" / "PhantomWitch" / "PhantomWitchVfxRedesign.cs"


def fail(message: str) -> None:
    print(f"PhantomWitchCameraCacheGuard: FAIL - {message}")
    sys.exit(1)


asset_manager_text = ASSET_MANAGER.read_text(encoding="utf-8")
ambient_text = AMBIENT.read_text(encoding="utf-8")
vfx_text = VFX.read_text(encoding="utf-8")

if "internal static Camera CurrentCamera" not in asset_manager_text:
    fail("PhantomWitchFxRuntime 缺少 CurrentCamera 缓存入口")

for path, text in ((AMBIENT, ambient_text), (VFX, vfx_text)):
    if re.search(r"\bCamera\.main\b", text):
        fail(f"{path.name} 仍在直接使用 Camera.main")

if "PhantomWitchFxRuntime.CurrentCamera" not in ambient_text:
    fail("PhantomWitchAmbientPresence 未接入缓存相机")

if "PhantomWitchFxRuntime.CurrentCamera" not in vfx_text:
    fail("PhantomWitchVfxRedesign 未接入缓存相机")

print("PhantomWitchCameraCacheGuard: PASS")
