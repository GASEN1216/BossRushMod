"""MutatorUiOverlaySuppressionGuard: modal UI must suppress the mutator overlay.

原实现是 IMGUI（OnGUI + Rect.Contains 手算悬停）。迁到 uGUI 后契约不变，
只是载体变了：抑制判定仍必须在显示之前跑，抑制期间仍必须清掉悬停详情，
并且 overlay 不能再回到 OnGUI。
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/Mutators/MutatorUI.cs")
HOST = Path("ModBehaviour.cs")


def fail(message: str) -> None:
    print("MutatorUiOverlaySuppressionGuard: FAIL - " + message)
    sys.exit(1)


text = SOURCE.read_text(encoding="utf-8")
host = HOST.read_text(encoding="utf-8")

match = re.search(
    r"public\s+static\s+void\s+Tick\s*\(\s*\)\s*\{(?P<body>.*?)\n\s{8}\}",
    text,
    re.DOTALL,
)
if match is None:
    fail("MutatorUI.Tick was not found")

body = match.group("body")
suppression = "suppressed = !InputManager.InputActived || Time.timeScale <= 0f;"
hover_reset = "SetHoveredIndex(-1);"
show_call = "SetCanvasVisible(true);"

if suppression not in body:
    fail("Tick must stop showing the overlay while gameplay input is blocked or time is paused by a modal UI")
if hover_reset not in body:
    fail("Tick must clear stale hover details while the overlay is suppressed")
if show_call not in body:
    fail("Tick overlay show call was not found")
if body.index(suppression) > body.index(show_call):
    fail("modal UI suppression must be evaluated before showing the mutator overlay")
if body.index(hover_reset) > body.index(show_call):
    fail("hover reset must run on the suppressed path, before the overlay is shown")

# 抑制判定必须容错：InputManager 在切场景期间可能抛，抛了要按“抑制”处理而不是崩掉。
if "catch" not in body:
    fail("Tick must defensively treat an InputManager failure as suppression")

# uGUI 迁移不得回退：overlay 不能再挂回 OnGUI。只看代码，注释里提及历史实现是允许的。
code_only = "\n".join(
    line for line in text.splitlines() if not line.lstrip().startswith("//")
)
for token in ("void OnGUI", "GUIStyle", "GUILayout", "GUI.Label", "GUI.Box"):
    if token in code_only:
        fail("mutator overlay must stay on uGUI; IMGUI drawing must not come back -> " + token)
if "MutatorUI.DrawGUI()" in host:
    fail("ModBehaviour.OnGUI must not draw the mutator overlay any more")
if "MutatorUI.Tick();" not in host:
    fail("ModBehaviour.Update must drive MutatorUI.Tick()")

# Canvas 与其他运行时缓存一样，必须挂在 OnDestroy 释放路径上。
if "MutatorUI.ResetStaticCaches" not in host:
    fail("MutatorUI canvas must be released from the OnDestroy reset path")

# 常驻 overlay 走统一层级表，不再用裸数值。
if "BossRushUILayers.HudOverlay" not in text:
    fail("mutator overlay must take its sorting order from the shared layer table")

print("MutatorUiOverlaySuppressionGuard: PASS")
