"""MutatorUiOverlaySuppressionGuard: modal UI must suppress the mutator IMGUI overlay."""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/Mutators/MutatorUI.cs")


def fail(message: str) -> None:
    print("MutatorUiOverlaySuppressionGuard: FAIL - " + message)
    sys.exit(1)


text = SOURCE.read_text(encoding="utf-8")
match = re.search(
    r"public\s+static\s+void\s+DrawGUI\s*\(\s*\)\s*\{(?P<body>.*?)\n\s{8}\}",
    text,
    re.DOTALL,
)
if match is None:
    fail("MutatorUI.DrawGUI was not found")

body = match.group("body")
suppression = "if (!InputManager.InputActived || Time.timeScale <= 0f)"
hover_reset = "_detailHoverRect = Rect.zero;"
draw_call = "Rect cornerRect = DrawCornerIcons(out hoveredIndex);"

if suppression not in body:
    fail("DrawGUI must stop drawing while gameplay input is blocked or time is paused by a modal UI")
if hover_reset not in body:
    fail("DrawGUI must clear stale hover details while the overlay is suppressed")
if draw_call not in body:
    fail("DrawGUI corner-list draw call was not found")
if body.index(suppression) > body.index(draw_call):
    fail("modal UI suppression must run before drawing the mutator list")

print("MutatorUiOverlaySuppressionGuard: PASS")
