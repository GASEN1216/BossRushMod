"""F3 自动验收审阅工具 tools/autotest_review.py 的离线属性测试。

在仓库外的临时目录里，按 RenderManifest（DebugAndTools/F3GameplayValidationAutotestJudges.cs）的真实字段造几轮假结果目录
（纯色 PNG / JPG 截图、一张缺文件、一张未落盘、一张坏文件），用 subprocess 跑工具，断言：
- 退出码 0；review.md 第一段是还原失败提示（原样带 detail）；还原正常的一轮不标红；
- 红的排前面：md 分段 FAIL → SKIP → PASS、review.json 步骤顺序、拼图组内顺序（manifest 里 PASS 故意排在 FAIL 前）；
- 一步挂两个清单编号时两张拼图都生成；缺失 / 未落盘 / 打不开的截图画占位、写原因、不崩；
- --auto-previous 挑中「startedUtc 早于本轮、最晚」的一轮（更早、更晚、manifest 坏掉的兄弟目录都不选）；
- 对比段识别新 FAIL、已修复、仍 FAIL、本轮缺失的步骤，以及 p95_ms / weber 的前后值；
- 结果目录只读（review/ 之外逐字节不变、不多文件）；--output 指向仓库内时退出码 2 且不创建目录；
- 假 manifest 的键集合与 RenderManifest 源码逐一对应（游戏侧改字段时这里转红，提醒同步工具）。

没装 Pillow：打印 SKIP 原因并以退出码 2 结束（不记 PASS）。
"""
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

for _stream in (sys.stdout, sys.stderr):  # 直接运行时 Windows cp936 控制台不 mojibake
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

try:
    from PIL import Image
except ImportError as _error:
    print("AutotestReviewToolPropertyTest: SKIP（未安装 Pillow，造不了截图也跑不了审阅工具：%s）；不计 PASS" % _error)
    sys.exit(2)

ROOT = Path(__file__).resolve().parents[1]
TOOL = ROOT / "tools" / "autotest_review.py"
JUDGES = ROOT / "DebugAndTools" / "F3GameplayValidationAutotestJudges.cs"
NAME = "AutotestReviewToolPropertyTest"
ERRORS = []


def expect(condition, label):
    if not condition:
        ERRORS.append(label)
        print("  [FAIL] " + label)


# ---------------------------------------------------------------------------
# 造假结果目录（键顺序与 RenderManifest 一致）
# ---------------------------------------------------------------------------

def assertion(name, result, reason=None, metrics=None):
    return {"name": name, "result": result, "reason": reason, "metrics": metrics}


def shot(name, file, kind="ui", encoding="png", size=0, metrics="size=640x360,texts=3"):
    return {"name": name, "file": file, "kind": kind, "encoding": encoding, "bytes": size, "metrics": metrics}


def step(step_id, result, checklist, assertions=(), shots=(), reason=None, cls="auto"):
    return {"id": step_id, "title": step_id + " 的标题", "stage": "landing", "location": "PlayerSpawn", "class": cls,
            "language": "ChineseSimplified", "result": result, "reason": reason, "startedUtc": "2026-09-14T10:00:01.0000000Z",
            "durationMs": 1234, "checklist": list(checklist), "actions": ["assert:overflow_none"],
            "assertions": list(assertions), "shots": list(shots), "notes": []}


def manifest(run_id, started, steps, story="PASS", detail="", environment="PASS", coverage=None):
    return {
        "schema": "bossrush-autotest-manifest/1", "runId": run_id, "slot": 3, "mvid": "00000000-0000-0000-0000-000000000000",
        "language": "ChineseSimplified", "altLanguage": "English", "startedUtc": started, "endedUtc": started,
        "status": "FAIL", "reportLog": "C:/fake/BossRushValidation_%s.log" % run_id, "steamScreenshots": "not_used: 测试",
        "restore": {"story": story, "detail": detail, "environment": environment, "items": "ledger_ok", "money": "money_ok"},
        "shots": {"bytes": 4096, "budgetBytes": 314572800, "count": 3, "degraded": 0, "skipped": 1},
        "steps": steps, "coverage": coverage or [],
    }


def write_run(base, folder, data, images=(), raw_manifest=None):
    run_dir = base / folder
    (run_dir / "shots").mkdir(parents=True, exist_ok=True)
    text = raw_manifest if raw_manifest is not None else json.dumps(data, ensure_ascii=False, separators=(",", ":"))
    (run_dir / "manifest.json").write_text(text, encoding="utf-8")
    (run_dir / "summary.md").write_text("# 假 summary\n", encoding="utf-8")
    for rel, color, fmt in images:
        path = run_dir / rel
        if fmt == "garbage":
            path.write_bytes(b"not an image at all")
        else:
            Image.new("RGB", (640, 360), color).save(path, fmt)
    return run_dir


FRAME = "frame"
BANNER_ASSERT = "assert:contrast_min:SkyIslandAreaTitle/Overline:4.5"
DOCK_ASSERT = "assert:visible_min:ground_ring:0.3"
HUD_ASSERT = "assert:overflow_none"


def frame_metrics(p95, peak):
    return "samples=300,p95_ms=%s,max_ms=%s,living_enemies=2,night=False" % (p95, peak)


def contrast_metrics(measured):
    return ("path=SkyIslandHud/SkyIslandAreaTitle/Overline,rect=10,20,300x40,bg_Y=0.02,text_Y=0.9,measured=%s,"
            "declared=n/a,min=0,ring_samples=100,inside_samples=200" % measured)


def build_fixture(base):
    older = manifest("run_older", "2026-09-14T08:00:00.0000000Z", [
        step("SKY_AUTO_LAND_BANNER", "FAIL", ["2.14.1", "2.14.2"], [assertion(BANNER_ASSERT, "FAIL", "contrast_below_min", contrast_metrics("1.0"))]),
        step("SKY_AUTO_LAND_HUD_CARD", "PASS", ["2.14.4", "2.17.4"], [assertion(HUD_ASSERT, "PASS", None, "inspected=12,overflowing=0")]),
    ])
    prev = manifest("run_prev", "2026-09-14T09:00:00.1234567Z", [
        step("SKY_AUTO_BASE_MODEG_BUTTON", "PASS", ["2.14.14"], [assertion(FRAME, "PASS", None, frame_metrics("41.50", "80.00"))]),
        step("SKY_AUTO_LAND_BANNER", "PASS", ["2.14.1", "2.14.2"], [assertion(BANNER_ASSERT, "PASS", None, contrast_metrics("13.2"))]),
        step("SKY_AUTO_LAND_HUD_CARD", "FAIL", ["2.14.4", "2.17.4"],
             [assertion(HUD_ASSERT, "FAIL", "text_draws_outside_its_box", "inspected=12,overflowing=1,truncated=0,overflow_list=Tracker/Objective")],
             reason="text_draws_outside_its_box"),
        step("SKY_AUTO_LAND_DOCK_WORLD", "FAIL", ["2.11.1", "2.14.15"],
             [assertion(DOCK_ASSERT, "FAIL", "visibility_below_min", "object_Y=0.2,neighbor_Y=0.18,weber=0.12,wcag=1.1")],
             reason="visibility_below_min", cls="shot"),
        step("SKY_AUTO_GONE_STEP", "PASS", ["2.10.1"], [assertion("assert:flag:Foo", "PASS", None, "flags=1")]),
    ])
    cur_steps = [
        # manifest 顺序故意让 PASS 排在 FAIL 前面：不排序就会把红的放到后面去。
        step("SKY_AUTO_ALT_BANNER", "PASS", ["2.14.2"], [assertion("assert:language_is:English", "PASS", None, "language=English")],
             [shot("alt_banner", "shots/SKY_AUTO_ALT_BANNER__alt_banner.png", size=1000)], cls="shot"),
        step("SKY_AUTO_BASE_MODEG_BUTTON", "PASS", ["2.14.14"], [assertion(FRAME, "PASS", None, frame_metrics("63.25", "120.00"))],
             [shot("modeg_confirm", "shots/SKY_AUTO_BASE_MODEG_BUTTON__modeg_confirm.png", size=1000)], cls="shot"),
        step("SKY_AUTO_LAND_HUD_CARD", "PASS", ["2.14.4", "2.17.4"], [assertion(HUD_ASSERT, "PASS", None, "inspected=12,overflowing=0")],
             [shot("hud_card", "shots/SKY_AUTO_LAND_HUD_CARD__hud_card.jpg", encoding="jpg", size=800),
              shot("hud_card_bad", "shots/SKY_AUTO_LAND_HUD_CARD__hud_card_bad.png", size=19)]),
        step("SKY_AUTO_BASE_PREV_QUIT_LOG", "SKIP", ["2.16.D5"], [assertion("assert:prev_log_quit", "SKIP", "not_in_base", None)],
             reason="not_in_base"),
        step("SKY_AUTO_LAND_BANNER", "FAIL", ["2.14.1", "2.14.2"],
             [assertion(BANNER_ASSERT, "FAIL", "contrast_below_min", contrast_metrics("2.13"))],
             [shot("landing_banner", "shots/SKY_AUTO_LAND_BANNER__landing_banner.png", size=1000),
              shot("landing_banner_zoom", "shots/SKY_AUTO_LAND_BANNER__landing_banner_zoom.png", size=1000)],
             reason="contrast_below_min"),
        step("SKY_AUTO_LAND_DOCK_WORLD", "FAIL", ["2.11.1", "2.14.15"],
             [assertion(DOCK_ASSERT, "FAIL", "visibility_below_min", "object_Y=0.2,neighbor_Y=0.17,weber=0.18,wcag=1.2")],
             [shot("dock", None, kind="world", encoding="skipped", metrics="overlay_visible:F3DebugCheatMenu")],
             reason="visibility_below_min", cls="shot"),
    ]
    coverage = [
        {"checklist": "2.14.1", "class": "auto", "reason": None, "status": "FAIL", "evidence": ["SKY_AUTO_LAND_BANNER"]},
        {"checklist": "2.16.D5", "class": "auto", "reason": None, "status": "SKIP", "evidence": ["SKY_AUTO_BASE_PREV_QUIT_LOG"]},
        {"checklist": "2.14.14", "class": "shot", "reason": None, "status": "PASS", "evidence": ["SKY_AUTO_BASE_MODEG_BUTTON"]},
        {"checklist": "2.17.9", "class": "manual", "reason": "手感：跑图节奏只能人工 | 原样保留", "status": "MANUAL", "evidence": []},
    ]
    cur = manifest("run_cur", "2026-09-14T10:00:00.0000000Z", cur_steps, story="FAIL",
                   detail="codec_decode_rejected:slot=3\n第二行原样", environment="FAIL:language_not_restored", coverage=coverage)
    later = manifest("run_later", "2026-09-14T11:00:00.0000000Z", [
        step("SKY_AUTO_LAND_BANNER", "FAIL", ["2.14.1"], [assertion(BANNER_ASSERT, "FAIL", "contrast_below_min", contrast_metrics("1.5"))]),
        step("SKY_AUTO_LAND_HUD_CARD", "FAIL", ["2.14.4"], [assertion(HUD_ASSERT, "FAIL", "x", "overflowing=2")]),
    ], story="NOT_NEEDED", environment="PASS")

    write_run(base, "20260914_080000_older", older)
    write_run(base, "20260914_090000_prev", prev)
    write_run(base, "20260914_095000_broken", None, raw_manifest="{ 写到一半")
    (base / "notes_without_manifest").mkdir()
    cur_dir = write_run(base, "20260914_100000_cur", cur, images=[
        ("shots/SKY_AUTO_ALT_BANNER__alt_banner.png", (30, 90, 200), "PNG"),
        ("shots/SKY_AUTO_BASE_MODEG_BUTTON__modeg_confirm.png", (40, 160, 60), "PNG"),
        ("shots/SKY_AUTO_LAND_HUD_CARD__hud_card.jpg", (200, 200, 40), "JPEG"),
        ("shots/SKY_AUTO_LAND_HUD_CARD__hud_card_bad.png", None, "garbage"),
        ("shots/SKY_AUTO_LAND_BANNER__landing_banner.png", (220, 60, 50), "PNG"),
    ])
    later_dir = write_run(base, "20260914_110000_later", later)
    return cur_dir, base / "20260914_090000_prev", later_dir, cur


# ---------------------------------------------------------------------------
# 工具调用与检查
# ---------------------------------------------------------------------------

def run_tool(*args):
    env = dict(os.environ, PYTHONIOENCODING="utf-8", PYTHONUTF8="1")
    proc = subprocess.run([sys.executable, str(TOOL)] + [str(a) for a in args], cwd=str(ROOT), capture_output=True,
                          text=True, encoding="utf-8", errors="replace", env=env, timeout=180)
    return proc.returncode, (proc.stdout or "") + (proc.stderr or "")


def require_exit(code, output, wanted, label):
    if code != wanted:
        print("  [FAIL] %s：退出码 %s（期望 %s）\n%s" % (label, code, wanted, output))
        print("%s: FAIL（工具退出码不对，后续检查无法进行）" % NAME)
        sys.exit(1)


def snapshot(directory, skip_dir):
    result = {}
    for path in sorted(directory.rglob("*")):
        if path.is_file() and skip_dir not in path.parents:
            result[str(path.relative_to(directory))] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def first_paragraph(text):
    for chunk in text.split("\n\n"):
        if chunk.strip():
            return chunk
    return ""


def collect_keys(value, keys):
    if isinstance(value, dict):
        for key, child in value.items():
            keys.add(key)
            collect_keys(child, keys)
    elif isinstance(value, list):
        for child in value:
            collect_keys(child, keys)
    return keys


def check_manifest_fields(sample):
    """假 manifest 的键必须与 RenderManifest 写出的键逐一对应。"""
    if not JUDGES.is_file():
        return "未核对字段（%s 不存在）" % JUDGES.relative_to(ROOT)
    source = JUDGES.read_text(encoding="utf-8", errors="replace")
    start = source.find("internal static string RenderManifest(")
    end = source.find("private static void StringArray(", start)
    expect(start >= 0 and end > start, "Judges.cs 里找不到 RenderManifest 方法体")
    body = source[start:end]
    written = set(re.findall(r'\.(?:Str|Int|Long)\("(\w+)"', body))
    written |= set(re.findall(r'Begin(?:Object|Array)\("(\w+)"\)', body))
    written |= set(re.findall(r'StringArray\(w, "(\w+)"', body))
    faked = collect_keys(sample, set())
    expect(written == faked, "假 manifest 与 RenderManifest 字段不一致：源码多 %s，假数据多 %s"
           % (sorted(written - faked), sorted(faked - written)))
    return "字段与 RenderManifest 一致（%d 个键）" % len(written)


def main():
    tmp = Path(tempfile.mkdtemp(prefix="autotest_review_"))
    try:
        if str(tmp.resolve()).lower().startswith(str(ROOT).lower()):
            print("%s: FAIL（临时目录落在仓库内：%s）" % (NAME, tmp))
            return 1
        base = tmp / "BossRushTestReports"
        base.mkdir()
        cur_dir, prev_dir, later_dir, cur_manifest = build_fixture(base)
        field_note = check_manifest_fields(cur_manifest)
        review_dir = cur_dir / "review"
        before = snapshot(cur_dir, review_dir)

        # 1) 默认输出 + --auto-previous
        code, output = run_tool(cur_dir, "--auto-previous")
        require_exit(code, output, 0, "默认输出 + --auto-previous")
        expect(snapshot(cur_dir, review_dir) == before, "结果目录在 review/ 之外被改动或多了文件")
        md = (review_dir / "review.md").read_text(encoding="utf-8")
        data = json.loads((review_dir / "review.json").read_text(encoding="utf-8"))

        head = first_paragraph(md)
        expect("需要处理" in head and "剧情还原 FAIL" in head, "review.md 第一段不是剧情还原失败提示")
        expect("环境还原 FAIL:language_not_restored" in head, "第一段没带环境还原状态")
        expect("codec_decode_rejected:slot=3" in head and "第二行原样" in head, "第一段没原样带上 restore.detail")
        expect(data["restore"]["attention"] is True, "review.json restore.attention 不是 true")

        fail_at, skip_at, pass_at = md.find("## 红项（FAIL）"), md.find("## SKIP"), md.find("## PASS")
        expect(0 <= fail_at < skip_at < pass_at, "review.md 分段不是 FAIL → SKIP → PASS")
        expect(0 <= md.find("`SKY_AUTO_LAND_BANNER`") < md.find("`SKY_AUTO_ALT_BANNER`"), "review.md 里 FAIL 步骤没排在 PASS 前")
        rank = {"FAIL": 0, "SKIP": 1, "PASS": 2}
        order = [rank.get(s["result"], 1) for s in data["steps"]]
        expect(order == sorted(order) and data["steps"][0]["result"] == "FAIL", "review.json 步骤顺序不是红在前：%s" % order)
        groups = {g["checklist"]: g for g in data["checklists"]}
        g2 = groups.get("2.14.2", {"tiles": [], "steps": []})
        expect([t["stepId"] for t in g2["tiles"]][:1] == ["SKY_AUTO_LAND_BANNER"], "拼图 2.14.2 组内 FAIL 没排在最前")
        expect(set(g2["steps"]) == {"SKY_AUTO_LAND_BANNER", "SKY_AUTO_ALT_BANNER"}, "拼图 2.14.2 的步骤不对")

        for checklist in ("2.14.1", "2.14.2", "2.14.4", "2.17.4", "2.16.D5"):
            sheet = review_dir / "sheets" / (checklist + ".jpg")
            ok = sheet.is_file()
            if ok:
                with Image.open(sheet) as image:
                    ok = image.width > 0 and image.height > 0
            expect(ok, "拼图 sheets/%s.jpg 没生成或打不开" % checklist)
        expect((review_dir / "thumbs" / "SKY_AUTO_LAND_BANNER__landing_banner.jpg").is_file(), "缩略图没生成")

        shots = {(s["id"], sh["name"]): sh for s in data["steps"] for sh in s["shots"]}
        placeholder = lambda key: (shots.get(key) or {}).get("placeholder") or ""
        expect("文件不存在" in placeholder(("SKY_AUTO_LAND_BANNER", "landing_banner_zoom")), "缺失截图没记占位原因")
        expect("未落盘" in placeholder(("SKY_AUTO_LAND_DOCK_WORLD", "dock")), "未落盘截图没记占位原因")
        expect("打不开" in placeholder(("SKY_AUTO_LAND_HUD_CARD", "hud_card_bad")), "坏截图没记占位原因")
        expect("（占位：文件不存在" in md, "review.md 分组表没写缺失截图的占位")

        cmp_ = data.get("comparison") or {}
        expect(cmp_.get("previousRunId") == "run_prev", "--auto-previous 没挑中 run_prev：%s" % cmp_.get("previousRunId"))
        ids = lambda key: [e["id"] for e in cmp_.get(key, [])]
        expect(ids("newFail") == ["SKY_AUTO_LAND_BANNER"], "新 FAIL 识别不对：%s" % ids("newFail"))
        expect(ids("fixed") == ["SKY_AUTO_LAND_HUD_CARD"], "已修复识别不对：%s" % ids("fixed"))
        expect(ids("stillFail") == ["SKY_AUTO_LAND_DOCK_WORLD"], "仍 FAIL 识别不对：%s" % ids("stillFail"))
        expect("SKY_AUTO_GONE_STEP" in ids("otherChanges"), "本轮缺失的步骤没列出")
        p95 = [c for c in cmp_.get("metricChanges", []) if c["stepId"] == "SKY_AUTO_BASE_MODEG_BUTTON" and c["key"] == "p95_ms"]
        expect(len(p95) == 1 and p95[0]["previous"] == 41.5 and p95[0]["current"] == 63.25 and p95[0]["watched"],
               "p95_ms 前后值没识别：%s" % p95)
        new_fail_block = md[md.find("### 新 FAIL"):md.find("### 已修复")]
        expect("`SKY_AUTO_LAND_BANNER`" in new_fail_block and "### 新 FAIL（1）" in new_fail_block, "review.md 新 FAIL 段不对")
        expect("`SKY_AUTO_LAND_HUD_CARD`" in md[md.find("### 已修复"):md.find("### 仍 FAIL")], "review.md 已修复段不对")
        expect("| p95_ms | 41.5 | 63.25 |" in md and "| weber | 0.12 | 0.18 |" in md, "review.md 数值变化表缺 p95_ms / weber 前后值")
        expect(any("跳过" in w for w in data["warnings"]), "manifest 坏掉的兄弟目录没记警告")

        by_class = data["coverage"]["byClass"]
        expect([by_class[c]["rows"] for c in ("auto", "shot", "manual")] == [2, 1, 1], "覆盖按 class 统计不对")
        expect("- `2.17.9`：手感：跑图节奏只能人工 | 原样保留" in md, "manual 行理由没原样列出")

        # 2) --previous + --output 指到仓库外
        out_explicit = tmp / "out_explicit"
        code, output = run_tool(cur_dir, "--previous", prev_dir, "--output", out_explicit, "--thumb", "200")
        require_exit(code, output, 0, "--previous + --output")
        explicit = json.loads((out_explicit / "review.json").read_text(encoding="utf-8"))
        expect((explicit.get("comparison") or {}).get("previousRunId") == "run_prev", "--previous 没生效")
        expect((out_explicit / "sheets" / "2.14.1.jpg").is_file(), "--output 目录里没有拼图")

        # 3) 还原正常的一轮不标红（NOT_NEEDED 也算正常）
        out_later = tmp / "out_later"
        code, output = run_tool(later_dir, "--output", out_later)
        require_exit(code, output, 0, "还原正常的一轮")
        later_md = (out_later / "review.md").read_text(encoding="utf-8")
        expect(later_md.startswith("# 自动验收审阅 run_later") and "需要处理" not in later_md, "还原正常时仍被标红或标题不在第一段")

        # 4) --output 指向仓库内：退出码 2，且不创建目录
        inside = ROOT / "tests" / "__autotest_review_probe_should_not_exist__"
        if inside.exists():
            print("%s: FAIL（探针路径已存在，先人工确认后删除：%s）" % (NAME, inside))
            return 1
        code, output = run_tool(cur_dir, "--output", inside)
        expect(code == 2, "--output 指向仓库内时退出码是 %s（期望 2）" % code)
        expect(not inside.exists(), "--output 指向仓库内时仍创建了目录")
        expect("仓库" in output, "--output 指向仓库内时没说明原因")

        if ERRORS:
            print("%s: FAIL（%d 条）" % (NAME, len(ERRORS)))
            return 1
        print("%s: PASS（4 次调用；%s）" % (NAME, field_note))
        return 0
    finally:
        if os.environ.get("AUTOTEST_REVIEW_KEEP_TMP") == "1":
            print("保留临时目录：" + str(tmp))
        else:
            shutil.rmtree(str(tmp), ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
