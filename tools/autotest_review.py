#!/usr/bin/env python3
"""F3 全自动实机验收结果目录的离线审阅工具（只读结果目录）。

游戏侧（Dev 构建）跑完自动验收后写 persistentDataPath/BossRushTestReports/<runId>/：
manifest.json（schema bossrush-autotest-manifest/1，字段以
DebugAndTools/F3GameplayValidationAutotestJudges.cs 的 RenderManifest 为准）、summary.md、shots/。
本工具读 manifest 与截图，产出：

  <输出目录>/review.md                 还原状态置顶、总计、FAIL > SKIP > PASS、按清单分组、覆盖统计、与上一轮对比
  <输出目录>/review.json               同样信息的机器可读版（schema bossrush-autotest-review/1）
  <输出目录>/sheets/<清单编号>.jpg      每个清单编号一张缩略图拼图，组内红的排前面
  <输出目录>/thumbs/<步骤>__<截图>.jpg  单张缩略图

用法：
  python tools/autotest_review.py <结果目录> [--previous <上一轮结果目录> | --auto-previous]
                                  [--output <输出目录>] [--thumb 360]

约束：
- 除输出目录外不写任何文件；截图路径越出结果目录时不读、画占位块。
- 输出目录默认 <结果目录>/review/；解析后落在仓库根目录之内时拒绝（结果不进仓库）。
- 需要 Pillow；字体找不到时退回 Pillow 默认字体，不因字体崩。

退出码：0 = 审阅已生成（本轮有没有红不影响退出码）；2 = 缺 Pillow、参数或输入有问题、输出目录落在仓库内。
离线测试：tests/AutotestReviewToolPropertyTest.py。
"""

import argparse
import datetime
import json
import math
import os
import re
import sys

try:
    from PIL import Image, ImageDraw, ImageFont
    PIL_IMPORT_ERROR = None
except ImportError as _error:  # 没装 Pillow：main() 里给中文提示并以 2 退出
    Image = ImageDraw = ImageFont = None
    PIL_IMPORT_ERROR = _error


def _force_utf8_output():
    """输出中文；Windows cp936 控制台下不强制 UTF-8 会 mojibake。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass


REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MANIFEST_SCHEMA = "bossrush-autotest-manifest/1"
REVIEW_SCHEMA = "bossrush-autotest-review/1"

# 排序：红的排前面。review.md 的分段、review.json 的步骤顺序、拼图组内顺序都只认这一处。
RESULT_RANK = {"FAIL": 0, "SKIP": 1, "PASS": 2}
UNKNOWN_RESULT_RANK = 1  # 游戏侧只写 PASS / FAIL / SKIP；万一出现别的值，和 SKIP 一起排在 PASS 前面

# 还原状态里不需要处理的值（游戏侧 RestoreNeedsAttention 只把 FAIL / NOT_RUN / PENDING* 标红，
# 这里取更保守的口径：除这两个之外一律置顶标红，未知值也不放过）。
RESTORE_OK_VALUES = ("PASS", "NOT_NEEDED")

# 与上一轮对比时在 review.md 里单列的数值键；review.json 里列出全部数值键的变化。
WATCH_METRIC_KEYS = ("ratio", "p95_ms", "max_ms", "weber", "alpha", "distance_m", "killed",
                     "measured", "wcag", "overflowing", "truncated", "living_enemies")
WATCH_METRIC_SUFFIXES = ("_ms", "_m", "ratio")

RESULT_COLORS = {"FAIL": (217, 48, 37), "SKIP": (242, 183, 5), "PASS": (30, 142, 62)}
UNKNOWN_COLOR = (128, 128, 128)
SHEET_COLUMNS = 4
MAX_TILES_PER_SHEET = 120
MD_METRICS_CHARS = 200
MD_METRIC_ROWS = 200

_KEY_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
_ISO_RE = re.compile(r"^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?(Z|[+-]\d{2}:?\d{2})?$")


class ReviewError(Exception):
    """参数、输入或环境问题：打印原因并以退出码 2 结束。"""


# ---------------------------------------------------------------------------
# 基础工具
# ---------------------------------------------------------------------------

def text_of(obj, key):
    """manifest 里的字符串字段；BossRushJsonWriter 把 null 写成字面量 null，这里统一成空串。"""
    if not isinstance(obj, dict):
        return ""
    value = obj.get(key)
    if value is None:
        return ""
    return value if isinstance(value, str) else str(value)


def list_of(obj, key):
    value = obj.get(key) if isinstance(obj, dict) else None
    return value if isinstance(value, list) else []


def dict_of(obj, key):
    value = obj.get(key) if isinstance(obj, dict) else None
    return value if isinstance(value, dict) else {}


def int_of(obj, key):
    value = obj.get(key) if isinstance(obj, dict) else None
    if isinstance(value, bool):
        return int(value)
    if isinstance(value, (int, float)) and math.isfinite(value):
        return int(value)
    try:
        return int(str(value).strip())
    except (TypeError, ValueError):
        return 0


def truncate(text, limit):
    text = (text or "").replace("\r", " ").replace("\n", " ")
    return text if len(text) <= limit else text[:max(0, limit - 1)] + "…"


def md_cell(text):
    return (text or "").replace("|", "/").replace("\r", " ").replace("\n", " ")


def result_rank(result):
    return RESULT_RANK.get(result, UNKNOWN_RESULT_RANK)


def result_bucket(result):
    return result if result in RESULT_RANK else "OTHER"


def restore_needs_attention(value):
    return (value or "") not in RESTORE_OK_VALUES


def parse_metrics(text):
    """`k=v,k=v` → 有序 dict。值里本身带逗号（rect=12,34,56x78）时，没有等号的段接回上一个值；
    开头就没有 `k=` 的内容（capture_failed:… 这类）放在键 `_` 下。"""
    result = {}
    if not text:
        return result
    last = None
    for part in text.split(","):
        eq = part.find("=")
        key = part[:eq].strip() if eq > 0 else ""
        if eq > 0 and _KEY_RE.match(key):
            result[key] = part[eq + 1:]
            last = key
        elif last is not None:
            result[last] += "," + part
        else:
            result["_"] = part if "_" not in result else result["_"] + "," + part
    return result


def as_number(value):
    if not isinstance(value, str):
        return None
    try:
        number = float(value.strip())
    except ValueError:
        return None
    return number if math.isfinite(number) else None


def is_watched_key(key):
    return key in WATCH_METRIC_KEYS or any(key.endswith(suffix) for suffix in WATCH_METRIC_SUFFIXES)


def parse_utc(text):
    """C# DateTime.UtcNow.ToString("O")（七位小数 + Z）→ aware datetime；解析不了返回 None。"""
    match = _ISO_RE.match((text or "").strip())
    if not match:
        return None
    try:
        year, month, day, hour, minute, second = (int(g) for g in match.groups()[:6])
        micro = int((match.group(7) or "0")[:6].ljust(6, "0"))
        value = datetime.datetime(year, month, day, hour, minute, second, micro, tzinfo=datetime.timezone.utc)
    except ValueError:
        return None
    zone = match.group(8)
    if zone and zone != "Z":
        digits = zone[1:].replace(":", "")
        offset = datetime.timedelta(hours=int(digits[:2]), minutes=int(digits[2:]))
        value = value - offset if zone[0] == "+" else value + offset
    return value


def normalized(path):
    return os.path.normcase(os.path.realpath(os.path.abspath(path)))


def is_within(child, parent):
    child_n, parent_n = normalized(child), normalized(parent)
    try:
        return os.path.commonpath([child_n, parent_n]) == parent_n
    except ValueError:  # 不同盘符
        return False


def safe_file_name(value, limit=120):
    """与游戏侧 AutotestFileName 同口径：字母数字、下划线、连字符之外换成下划线。"""
    cleaned = "".join(c if (c.isalnum() and c.isascii()) or c in "_-" else "_" for c in (value or ""))
    return (cleaned or "_")[:limit]


def checklist_file_name(checklist):
    cleaned = re.sub(r"[^A-Za-z0-9._-]", "_", checklist or "").strip(".")
    return cleaned or "_"


def checklist_sort_key(checklist):
    return [(0, int(part)) if part.isdigit() else (1, part) for part in re.split(r"(\d+)", checklist or "")]


# ---------------------------------------------------------------------------
# 读结果目录
# ---------------------------------------------------------------------------

def load_manifest(result_dir):
    path = os.path.join(result_dir, "manifest.json")
    if not os.path.isfile(path):
        raise ReviewError("结果目录里没有 manifest.json：" + path)
    try:
        with open(path, "r", encoding="utf-8-sig") as handle:
            data = json.load(handle)
    except (OSError, ValueError) as error:
        raise ReviewError("manifest.json 读不出来（游戏可能正在写入，稍后重跑）：%s：%s" % (path, error))
    if not isinstance(data, dict):
        raise ReviewError("manifest.json 顶层不是对象：" + path)
    return data


def headline_assertion(assertions):
    """拼图与列表里的一行缩写：第一条失败断言；没有失败就取第一条带 metrics 的断言。"""
    for assertion in assertions:
        if assertion["result"] == "FAIL":
            return assertion
    for assertion in assertions:
        if assertion["metrics"]:
            return assertion
    return None


def build_run(result_dir, manifest, warnings):
    schema = text_of(manifest, "schema")
    if schema != MANIFEST_SCHEMA:
        warnings.append("manifest schema 是 %r，本工具按 %s 读取，结论可能不全" % (schema, MANIFEST_SCHEMA))
    steps = []
    for index, raw in enumerate(list_of(manifest, "steps")):
        if not isinstance(raw, dict):
            warnings.append("steps[%d] 不是对象，已跳过" % index)
            continue
        assertions = [{"name": text_of(a, "name"), "result": text_of(a, "result"),
                       "reason": text_of(a, "reason"), "metrics": text_of(a, "metrics")}
                      for a in list_of(raw, "assertions") if isinstance(a, dict)]
        shots = [{"name": text_of(s, "name"), "file": text_of(s, "file"), "kind": text_of(s, "kind"),
                  "encoding": text_of(s, "encoding"), "bytes": int_of(s, "bytes"), "metrics": text_of(s, "metrics"),
                  "thumb": None, "placeholder": None}
                 for s in list_of(raw, "shots") if isinstance(s, dict)]
        step = {
            "index": index,
            "id": text_of(raw, "id") or "(no id #%d)" % index,
            "title": text_of(raw, "title"), "stage": text_of(raw, "stage"), "location": text_of(raw, "location"),
            "class": text_of(raw, "class"), "language": text_of(raw, "language"),
            "result": text_of(raw, "result"), "reason": text_of(raw, "reason"),
            "startedUtc": text_of(raw, "startedUtc"), "durationMs": int_of(raw, "durationMs"),
            "checklist": [str(c) for c in list_of(raw, "checklist") if c],
            "actions": [str(a) for a in list_of(raw, "actions") if a is not None],
            "assertions": assertions, "shots": shots,
            "notes": [str(n) for n in list_of(raw, "notes") if n is not None],
        }
        step["headline"] = headline_assertion(assertions)
        steps.append(step)
    return {
        "dir": os.path.abspath(result_dir), "manifest": manifest,
        "runId": text_of(manifest, "runId"), "startedUtc": text_of(manifest, "startedUtc"), "steps": steps,
    }


def order_steps(steps):
    """红的排前面：FAIL → SKIP（及未知结果）→ PASS，同档保持 manifest 里的执行顺序。"""
    return sorted(steps, key=lambda step: (result_rank(step["result"]), step["index"]))


# ---------------------------------------------------------------------------
# 缩略图
# ---------------------------------------------------------------------------

THUMB_BG = (24, 25, 28)
SHEET_BG = (32, 34, 37)
TILE_BG = (43, 45, 49)
PLACEHOLDER_BG = (70, 58, 58)
TEXT_MAIN = (235, 235, 235)
TEXT_DIM = (190, 190, 190)


def _resample():
    holder = getattr(Image, "Resampling", Image)
    return getattr(holder, "LANCZOS", getattr(Image, "BICUBIC", 3))


def resolve_shot_source(result_dir, shot):
    """返回 (截图绝对路径, 占位原因)，两者恰有一个非空。只读结果目录之内的文件。"""
    file_rel = shot["file"]
    if not file_rel:
        detail = truncate(shot["metrics"], 60)
        return None, "截图未落盘（encoding=%s）%s" % (shot["encoding"] or "未知", ("：" + detail) if detail else "")
    candidate = os.path.join(result_dir, file_rel.replace("/", os.sep))
    if not is_within(candidate, result_dir):
        return None, "文件路径越出结果目录：" + file_rel
    if not os.path.isfile(candidate):
        return None, "文件不存在：" + file_rel
    return candidate, None


def make_thumb_box(source, width, height):
    with Image.open(source) as opened:
        try:
            opened.draft("RGB", (width * 2, height * 2))  # 大 JPG 先按块降采样解码，省时间
        except Exception:
            pass
        image = opened.convert("RGB")
    image.thumbnail((width, height), _resample())
    box = Image.new("RGB", (width, height), THUMB_BG)
    box.paste(image, ((width - image.width) // 2, (height - image.height) // 2))
    return box


def prepare_thumbs(run, output_dir, thumb_w, thumb_h, stats):
    """每张截图做一次缩略图（拼图复用）；缺失、未落盘、打不开的只记占位原因，不崩。"""
    thumbs_dir = os.path.join(output_dir, "thumbs")
    used = set()
    for step in run["steps"]:
        for shot in step["shots"]:
            source, placeholder = resolve_shot_source(run["dir"], shot)
            if source is None:
                shot["placeholder"] = placeholder
                stats["placeholders"] += 1
                continue
            base = safe_file_name(step["id"] + "__" + (shot["name"] or "shot"))
            name, serial = base, 2
            while name.lower() in used:
                name, serial = "%s_%d" % (base, serial), serial + 1
            used.add(name.lower())
            try:
                box = make_thumb_box(source, thumb_w, thumb_h)
                os.makedirs(thumbs_dir, exist_ok=True)
                box.save(os.path.join(thumbs_dir, name + ".jpg"), "JPEG", quality=85)
                shot["thumb"] = "thumbs/" + name + ".jpg"
                stats["thumbs"] += 1
            except Exception as error:
                # 异常信息里的绝对路径换回 manifest 里的相对路径，占位块上才读得到真正的原因
                message = str(error).replace(repr(source)[1:-1], shot["file"]).replace(source, shot["file"])
                shot["placeholder"] = "打不开：%s: %s" % (type(error).__name__, truncate(message, 80))
                stats["placeholders"] += 1


# ---------------------------------------------------------------------------
# 字体与文字（任何一步失败都退回，不因字体崩）
# ---------------------------------------------------------------------------

_FONT_CACHE = {}


def font_candidates():
    windir = os.environ.get("WINDIR") or os.environ.get("SystemRoot") or r"C:\Windows"
    paths = [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\simhei.ttf",
             os.path.join(windir, "Fonts", "msyh.ttc"), os.path.join(windir, "Fonts", "simhei.ttf"),
             "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
             "/usr/share/fonts/truetype/wqy/wqy-microhei.ttc",
             "/System/Library/Fonts/PingFang.ttc"]
    unique = []
    for path in paths:
        if path not in unique:
            unique.append(path)
    return unique


def load_font(size):
    """返回 (font, 来源)。font 可能是 None（连默认字体都拿不到时让 Pillow 自己兜底）。"""
    if size in _FONT_CACHE:
        return _FONT_CACHE[size]
    result = None
    for path in font_candidates():
        if not os.path.isfile(path):
            continue
        try:
            result = (ImageFont.truetype(path, size), path)
            break
        except Exception:
            continue
    if result is None:
        try:
            result = (ImageFont.load_default(size=size), "pillow-default")
        except Exception:
            try:
                result = (ImageFont.load_default(), "pillow-default-bitmap")
            except Exception:
                result = (None, "none")
    _FONT_CACHE[size] = result
    return result


def text_width(draw, text, font):
    try:
        return draw.textlength(text, font=font)
    except Exception:
        try:
            box = draw.textbbox((0, 0), text, font=font)
            return box[2] - box[0]
        except Exception:
            return len(text) * 7


def draw_text(draw, xy, text, font, fill):
    try:
        draw.text(xy, text, font=font, fill=fill)
    except Exception:  # 位图默认字体画不了中文：退成 ASCII 替换字符
        try:
            draw.text(xy, text.encode("ascii", "replace").decode("ascii"), font=font, fill=fill)
        except Exception:
            pass


def _longest_prefix(draw, text, font, max_width, suffix=""):
    lo, hi = 0, len(text)
    while lo < hi:
        mid = (lo + hi + 1) // 2
        if text_width(draw, text[:mid] + suffix, font) <= max_width:
            lo = mid
        else:
            hi = mid - 1
    return lo


def fit_text(draw, text, font, max_width):
    text = (text or "").replace("\r", " ").replace("\n", " ")
    if text_width(draw, text, font) <= max_width:
        return text
    return text[:_longest_prefix(draw, text, font, max_width, "…")] + "…"


def wrap_text(draw, text, font, max_width, max_lines):
    lines, rest = [], (text or "").replace("\r", " ").replace("\n", " ")
    while rest and len(lines) < max_lines:
        if len(lines) == max_lines - 1:
            lines.append(fit_text(draw, rest, font, max_width))
            break
        cut = max(1, _longest_prefix(draw, rest, font, max_width))
        lines.append(rest[:cut])
        rest = rest[cut:]
    return lines


# ---------------------------------------------------------------------------
# 按清单编号分组与拼图
# ---------------------------------------------------------------------------

def assertion_brief(assertion):
    if not assertion:
        return ""
    name = assertion["name"]
    if name.startswith("assert:"):
        name = name[len("assert:"):]
    text = " ".join(part for part in (name, assertion["result"], assertion["reason"]) if part)
    return text + (" | " + assertion["metrics"] if assertion["metrics"] else "")


def count_results(steps):
    counts = {"PASS": 0, "FAIL": 0, "SKIP": 0, "OTHER": 0}
    for step in steps:
        counts[result_bucket(step["result"])] += 1
    return counts


def build_checklist_groups(steps):
    """清单编号 → 步骤（一步挂多个编号就在每个编号下都出现），组内沿用 order_steps 的红在前顺序。"""
    members = {}
    for step in order_steps(steps):
        for checklist in dict.fromkeys(step["checklist"] or ["(无清单编号)"]):
            members.setdefault(checklist, []).append(step)
    groups, used = [], set()
    for checklist in sorted(members, key=checklist_sort_key):
        tiles = []
        for step in members[checklist]:
            caption = assertion_brief(step["headline"]) or step["reason"]
            if not step["shots"]:
                tiles.append({"stepId": step["id"], "result": step["result"], "shot": None, "thumb": None,
                              "placeholder": "本步没有截图（class=%s）" % (step["class"] or "?"), "caption": caption})
            for shot in step["shots"]:
                tiles.append({"stepId": step["id"], "result": step["result"], "shot": shot["name"],
                              "thumb": shot["thumb"], "placeholder": shot["placeholder"], "caption": caption})
        base = checklist_file_name(checklist)
        name, serial = base, 2
        while name.lower() in used:
            name, serial = "%s_%d" % (base, serial), serial + 1
        used.add(name.lower())
        groups.append({"checklist": checklist, "sheet": "sheets/" + name + ".jpg", "members": members[checklist],
                       "counts": count_results(members[checklist]), "tiles": tiles,
                       "truncatedTiles": max(0, len(tiles) - MAX_TILES_PER_SHEET)})
    return groups


def paste_tile_image(sheet, draw, tile, x, y, width, height, output_dir, font, line_h):
    reason = tile["placeholder"]
    if tile["thumb"]:
        try:
            with Image.open(os.path.join(output_dir, tile["thumb"].replace("/", os.sep))) as thumb:
                sheet.paste(thumb.convert("RGB").crop((0, 0, width, height)), (x, y))
            return
        except Exception as error:
            reason = "缩略图读不出来：%s" % type(error).__name__
    draw.rectangle([x, y, x + width - 1, y + height - 1], fill=PLACEHOLDER_BG, outline=(150, 90, 90))
    draw.line([x, y, x + width - 1, y + height - 1], fill=(95, 75, 75))
    draw.line([x, y + height - 1, x + width - 1, y], fill=(95, 75, 75))
    lines = wrap_text(draw, "占位：" + (reason or "无截图"), font, width - 16, 4)
    ty = y + max(6, (height - line_h * len(lines)) // 2)
    for line in lines:
        draw_text(draw, (x + 8, ty), line, font, TEXT_MAIN)
        ty += line_h


def render_sheet(group, output_dir, thumb_w, thumb_h):
    font_size = max(11, thumb_w // 26)
    font = load_font(font_size)[0]
    header_font = load_font(font_size + 5)[0]
    line_h = font_size + 6
    tile_h = thumb_h + 6 + 6 + line_h * 4 + 4
    margin, gap, header_h = 12, 8, font_size + 5 + 16
    shown = group["tiles"][:MAX_TILES_PER_SHEET]
    cols = max(1, min(SHEET_COLUMNS, len(shown)))
    rows = max(1, (len(shown) + cols - 1) // cols)
    width = margin * 2 + cols * thumb_w + (cols - 1) * gap
    height = margin * 2 + header_h + rows * tile_h + (rows - 1) * gap
    sheet = Image.new("RGB", (width, height), SHEET_BG)
    draw = ImageDraw.Draw(sheet)
    counts = group["counts"]
    header = "清单 %s · 步骤 %d · FAIL %d / SKIP %d / PASS %d" % (
        group["checklist"], len(group["members"]), counts["FAIL"], counts["SKIP"] + counts["OTHER"], counts["PASS"])
    if group["truncatedTiles"]:
        header += " · 只画前 %d 张，另 %d 张见 review.json" % (len(shown), group["truncatedTiles"])
    draw_text(draw, (margin, margin), fit_text(draw, header, header_font, width - 2 * margin), header_font, TEXT_MAIN)
    for i, tile in enumerate(shown):
        x = margin + (i % cols) * (thumb_w + gap)
        y = margin + header_h + (i // cols) * (tile_h + gap)
        draw.rectangle([x, y, x + thumb_w - 1, y + tile_h - 1], fill=TILE_BG)
        paste_tile_image(sheet, draw, tile, x, y, thumb_w, thumb_h, output_dir, font, line_h)
        color = RESULT_COLORS.get(tile["result"], UNKNOWN_COLOR)
        bar_y = y + thumb_h
        draw.rectangle([x, bar_y, x + thumb_w - 1, bar_y + 5], fill=color)
        text_x, text_w, ty = x + 6, thumb_w - 12, bar_y + 10
        draw_text(draw, (text_x, ty), fit_text(draw, tile["stepId"], font, text_w), font, TEXT_MAIN)
        ty += line_h
        label = (tile["result"] or "?") + ("  · " + tile["shot"] if tile["shot"] else "")
        draw_text(draw, (text_x, ty), fit_text(draw, label, font, text_w), font, color)
        ty += line_h
        for line in wrap_text(draw, tile["caption"], font, text_w, 2):
            draw_text(draw, (text_x, ty), line, font, TEXT_DIM)
            ty += line_h
    os.makedirs(os.path.join(output_dir, "sheets"), exist_ok=True)
    sheet.save(os.path.join(output_dir, group["sheet"].replace("/", os.sep)), "JPEG", quality=85)


# ---------------------------------------------------------------------------
# 清单覆盖与上一轮对比
# ---------------------------------------------------------------------------

def summarize_coverage(manifest):
    classes, manual, not_passing = {}, [], []
    for row in list_of(manifest, "coverage"):
        if not isinstance(row, dict):
            continue
        cls, status = text_of(row, "class") or "?", text_of(row, "status") or "?"
        entry = classes.setdefault(cls, {"rows": 0, "statuses": {}})
        entry["rows"] += 1
        entry["statuses"][status] = entry["statuses"].get(status, 0) + 1
        if cls == "manual":
            manual.append({"checklist": text_of(row, "checklist"), "reason": text_of(row, "reason")})
        elif status not in ("PASS", "OFFLINE"):
            # OFFLINE：证据只有守卫 / 执行回归，这一轮游戏里本来就看不到结论，不算没通过（按类统计里照样计数）。
            not_passing.append({"checklist": text_of(row, "checklist"), "class": cls, "status": status,
                                "evidence": [str(e) for e in list_of(row, "evidence")]})
    by_class = {cls: classes.pop(cls, {"rows": 0, "statuses": {}}) for cls in ("auto", "shot", "manual")}
    by_class.update(classes)  # 三类之外的归类原样保留，不吞
    return {"byClass": by_class, "manual": manual, "notPassing": not_passing}


def keyed_entries(items):
    """同名断言 / 截图可能出现多次：按 (名字, 第几次) 配对。"""
    seen, result = {}, {}
    for item in items:
        n = seen.get(item["name"], 0)
        seen[item["name"]] = n + 1
        result[(item["name"], n)] = item
    return result


def diff_metrics(step, before):
    changes = []
    for source, now_items, old_items in (("assertion", step["assertions"], before["assertions"]),
                                         ("shot", step["shots"], before["shots"])):
        old_map = keyed_entries(old_items)
        for key, item in keyed_entries(now_items).items():
            old = old_map.get(key)
            if old is None:
                continue
            now_metrics, old_metrics = parse_metrics(item["metrics"]), parse_metrics(old["metrics"])
            for metric, raw in now_metrics.items():
                prev_value = as_number(old_metrics.get(metric))
                value = as_number(raw)
                if prev_value is None or value is None or abs(value - prev_value) <= 1e-9:
                    continue
                changes.append({"stepId": step["id"], "source": source, "name": key[0], "occurrence": key[1],
                                "key": metric, "previous": prev_value, "current": value,
                                "delta": value - prev_value, "watched": is_watched_key(metric)})
    return changes


def first_failure(step):
    for assertion in step["assertions"]:
        if assertion["result"] == "FAIL":
            return assertion_brief(assertion)
    return step["reason"] if step["result"] == "FAIL" else ""


def compare_runs(current, previous):
    old_steps = {}
    for step in previous["steps"]:
        old_steps.setdefault(step["id"], step)
    seen = set()
    result = {"previousDir": previous["dir"], "previousRunId": previous["runId"],
              "previousStartedUtc": previous["startedUtc"],
              "newFail": [], "fixed": [], "stillFail": [], "otherChanges": [], "metricChanges": []}
    for step in order_steps(current["steps"]):
        if step["id"] in seen:
            continue
        seen.add(step["id"])
        before = old_steps.get(step["id"])
        entry = {"id": step["id"], "checklist": step["checklist"],
                 "previous": before["result"] if before else None, "current": step["result"],
                 "failure": first_failure(step), "previousFailure": first_failure(before) if before else ""}
        now_fail = step["result"] == "FAIL"
        was_fail = before is not None and before["result"] == "FAIL"
        if now_fail and not was_fail:
            result["newFail"].append(entry)
        elif now_fail:
            result["stillFail"].append(entry)
        elif was_fail and step["result"] == "PASS":
            result["fixed"].append(entry)
        elif before is None:
            result["otherChanges"].append(dict(entry, change="本轮新增的步骤"))
        elif before["result"] != step["result"]:
            result["otherChanges"].append(dict(entry, change="结果变化"))
        if before is not None:
            result["metricChanges"].extend(diff_metrics(step, before))
    for step in previous["steps"]:
        if step["id"] not in seen:
            seen.add(step["id"])
            result["otherChanges"].append({"id": step["id"], "checklist": step["checklist"], "previous": step["result"],
                                           "current": None, "failure": "", "previousFailure": first_failure(step),
                                           "change": "本轮没有这一步"})
    return result


# ---------------------------------------------------------------------------
# review.md / review.json
# ---------------------------------------------------------------------------

SECTION_TITLES = {"FAIL": "红项（FAIL）", "SKIP": "SKIP", "PASS": "PASS"}


def section_of(result):
    return result if result in ("FAIL", "PASS") else "SKIP"


def fmt_number(value):
    return "{:.6g}".format(value)


def fmt_bytes(value):
    return "%.1f MB" % (value / (1024.0 * 1024.0))


def quote_block(text):
    return (text or "（空）").replace("\r\n", "\n").replace("\r", "\n").replace("\n", "\n> ")


def step_lines(step, section):
    checklist = ", ".join(step["checklist"]) or "无清单编号"
    label = "" if step["result"] == section else "（结果 %s）" % (step["result"] or "空")
    if section == "FAIL":
        lines = ["- `%s`（%s）%s：%s" % (step["id"], checklist, truncate(step["title"], 60), step["reason"] or "（无 reason）")]
        failed = [a for a in step["assertions"] if a["result"] == "FAIL"]
        for assertion in failed[:8]:
            lines.append("  - `%s`：%s | %s" % (assertion["name"], assertion["reason"] or "（无 reason）",
                                               truncate(assertion["metrics"], MD_METRICS_CHARS) or "（无 metrics）"))
        if len(failed) > 8:
            lines.append("  - …另 %d 条失败断言见 review.json" % (len(failed) - 8))
        return lines
    if section == "SKIP":
        reason = step["reason"] or next((a["reason"] for a in step["assertions"] if a["result"] == "SKIP" and a["reason"]), "")
        return ["- `%s`（%s）%s：%s" % (step["id"], checklist, label, reason or "（无 reason）")]
    brief = assertion_brief(step["headline"])
    return ["- `%s`（%s）%s" % (step["id"], checklist, (" — " + truncate(brief, 120)) if brief else "")]


def render_markdown(ctx):
    run, restore, manifest = ctx["current"], ctx["restore"], ctx["current"]["manifest"]
    lines = []
    if restore["attention"]:
        # 还原没完成：红字放第一段（与游戏侧 summary.md 顶部同口径），detail 原样带上。
        lines.append('> <span style="color:#d93025">**【需要处理】测试档还原没有完成：剧情还原 %s，环境还原 %s**</span>'
                     % (restore["story"] or "（空）", restore["environment"] or "（空）"))
        lines.append("> 剧情还原 detail（原样）：" + quote_block(restore["detail"]))
        if restore["storyNeedsAttention"]:
            lines.append("> 不要在这个槽上继续玩；处理步骤见同目录 summary.md 顶部（回基地重启读档自动还原，仍失败按 story_snapshot.json 手工写回）。")
        if restore["environmentNeedsAttention"]:
            lines.append("> 环境还原（语言 / 强制夜里 / 时间流速 / 无敌）没有复原：继续玩之前先确认这几项。")
        lines.append("")
    lines += ["# 自动验收审阅 " + (run["runId"] or "（无 runId）"), ""]
    if not restore["attention"]:
        lines += ["还原状态：剧情 %s，环境 %s；detail：%s" % (restore["story"], restore["environment"], restore["detail"] or "（空）"), ""]
    counts, shots, stats = ctx["counts"], dict_of(manifest, "shots"), ctx["stats"]
    lines += [
        "- 结果目录：`%s`；审阅输出：`%s`" % (run["dir"], ctx["outputDir"]),
        "- 整轮状态：**%s**；槽位 %s；语言 %s → 复拍 %s；开始 %s，结束 %s；DLL MVID %s" % (
            text_of(manifest, "status"), int_of(manifest, "slot"), text_of(manifest, "language"),
            text_of(manifest, "altLanguage"), run["startedUtc"], text_of(manifest, "endedUtc"), text_of(manifest, "mvid")),
        "- 物品记账：%s；金钱：%s" % (restore["items"] or "（空）", restore["money"] or "（空）"),
        "- 步骤：共 %d；PASS=%d FAIL=%d SKIP=%d%s" % (len(run["steps"]), counts["PASS"], counts["FAIL"], counts["SKIP"],
                                                  "；其它结果=%d" % counts["OTHER"] if counts["OTHER"] else ""),
        "- 截图（manifest）：%d 张，%s / 预算 %s；降级 %d，超预算只分析不落盘 %d" % (
            int_of(shots, "count"), fmt_bytes(int_of(shots, "bytes")), fmt_bytes(int_of(shots, "budgetBytes")),
            int_of(shots, "degraded"), int_of(shots, "skipped")),
        "- 本次审阅：缩略图 %d 张，占位 %d 个，拼图 %d 张；字体 %s" % (
            stats["thumbs"], stats["placeholders"], len([g for g in ctx["groups"] if g["sheet"]]), ctx["font"]),
        "- 对比：" + ("上一轮 `%s`" % ctx["comparison"]["previousRunId"] if ctx["comparison"] else ctx["previousNote"]),
    ]
    lines += ["- 注意：" + warning for warning in ctx["warnings"]]

    ordered = ctx["ordered"]
    if not any(step["result"] == "FAIL" for step in ordered):
        lines += ["", "## %s（0）" % SECTION_TITLES["FAIL"], "", "- 无"]
    current_section = None
    for step in ordered:
        section = section_of(step["result"])
        if section != current_section:
            total = sum(1 for s in ordered if section_of(s["result"]) == section)
            lines += ["", "## %s（%d）" % (SECTION_TITLES[section], total), ""]
            current_section = section
        lines += step_lines(step, section)

    lines += ["", "## 按清单编号分组", "", "| 清单 | 步骤 | 结果 | 缩略图 | 拼图 |", "| --- | --- | --- | --- | --- |"]
    for group in ctx["groups"]:
        for step in group["members"]:
            cells = [shot["thumb"] or "（占位：%s）" % shot["placeholder"] for shot in step["shots"]] or ["（本步没有截图）"]
            lines.append("| %s | `%s` | %s | %s | %s |" % (md_cell(group["checklist"]), step["id"], step["result"],
                                                        md_cell("; ".join(cells)), group["sheet"] or "（拼图生成失败）"))

    coverage = ctx["coverage"]
    lines += ["", "## 清单覆盖", "", "| 归类 | 行数 | 本轮状态 |", "| --- | --- | --- |"]
    for cls, entry in coverage["byClass"].items():
        statuses = " · ".join("%s %d" % item for item in sorted(entry["statuses"].items())) or "—"
        lines.append("| %s | %d | %s |" % (md_cell(cls), entry["rows"], statuses))
    lines += ["", "### 只能人工（%d 行，理由原样）" % len(coverage["manual"]), ""]
    lines += ["- `%s`：%s" % (row["checklist"], row["reason"]) for row in coverage["manual"]] or ["- 无"]
    lines += ["", "### 自动 / 截图行里本轮没过的（%d 行）" % len(coverage["notPassing"]), ""]
    lines += ["- `%s`（%s，%s）：%s" % (row["checklist"], row["class"], row["status"], ", ".join(row["evidence"]) or "（无证据）")
              for row in coverage["notPassing"]] or ["- 无"]

    lines += ["", "## 与上一轮对比", ""]
    comparison = ctx["comparison"]
    if not comparison:
        lines.append(ctx["previousNote"])
    else:
        lines += ["上一轮：`%s`（开始 %s，目录 `%s`）" % (comparison["previousRunId"], comparison["previousStartedUtc"],
                                                comparison["previousDir"]), ""]

        def change_list(title, entries, render):
            block = ["### %s（%d）" % (title, len(entries)), ""]
            block += [render(e) for e in entries] or ["- 无"]
            return block + [""]

        lines += change_list("新 FAIL", comparison["newFail"], lambda e: "- `%s`：上一轮 %s → 本轮 FAIL（%s）；%s" % (
            e["id"], e["previous"] or "没有这一步", ", ".join(e["checklist"]), truncate(e["failure"], MD_METRICS_CHARS)))
        lines += change_list("已修复", comparison["fixed"], lambda e: "- `%s`：上一轮 FAIL → 本轮 PASS（%s）；上一轮失败：%s" % (
            e["id"], ", ".join(e["checklist"]), truncate(e["previousFailure"], MD_METRICS_CHARS)))
        lines += change_list("仍 FAIL", comparison["stillFail"], lambda e: "- `%s`：本轮 %s；上一轮 %s" % (
            e["id"], truncate(e["failure"], MD_METRICS_CHARS), truncate(e["previousFailure"], MD_METRICS_CHARS)))
        lines += change_list("其它结果变化", comparison["otherChanges"], lambda e: "- `%s`：%s → %s（%s）" % (
            e["id"], e["previous"] or "—", e["current"] or "—", e["change"]))
        watched = [c for c in comparison["metricChanges"] if c["watched"]]
        lines += ["### 数值变化（关注键 %d 条；全部 %d 条见 review.json）" % (len(watched), len(comparison["metricChanges"])), ""]
        if watched:
            lines += ["| 步骤 | 断言 / 截图 | 键 | 上一轮 | 本轮 | 差值 |", "| --- | --- | --- | --- | --- | --- |"]
            for change in watched[:MD_METRIC_ROWS]:
                lines.append("| `%s` | %s | %s | %s | %s | %s |" % (
                    change["stepId"], md_cell(change["name"] + ("#%d" % change["occurrence"] if change["occurrence"] else "")),
                    change["key"], fmt_number(change["previous"]), fmt_number(change["current"]),
                    "{:+.6g}".format(change["delta"])))
            if len(watched) > MD_METRIC_ROWS:
                lines.append("")
                lines.append("…另 %d 条见 review.json" % (len(watched) - MD_METRIC_ROWS))
        else:
            lines.append("- 无")
    return "\n".join(lines).rstrip() + "\n"


def render_json(ctx):
    run, manifest = ctx["current"], ctx["current"]["manifest"]
    steps = []
    for step in ctx["ordered"]:
        steps.append({key: step[key] for key in ("id", "title", "stage", "location", "class", "language", "result", "reason",
                                                 "startedUtc", "durationMs", "checklist", "actions", "assertions", "notes")})
        steps[-1]["headline"] = step["headline"]
        steps[-1]["failedAssertions"] = [a for a in step["assertions"] if a["result"] == "FAIL"]
        steps[-1]["shots"] = [dict(shot) for shot in step["shots"]]
    groups = [{"checklist": g["checklist"], "sheet": g["sheet"], "steps": [s["id"] for s in g["members"]],
               "counts": g["counts"], "tiles": g["tiles"], "truncatedTiles": g["truncatedTiles"]} for g in ctx["groups"]]
    return {
        "schema": REVIEW_SCHEMA, "manifestSchema": text_of(manifest, "schema"),
        "generatedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "resultDir": run["dir"], "outputDir": ctx["outputDir"], "font": ctx["font"], "warnings": ctx["warnings"],
        "run": {key: manifest.get(key) for key in ("runId", "slot", "mvid", "language", "altLanguage", "startedUtc",
                                                   "endedUtc", "status", "reportLog")},
        "restore": ctx["restore"],
        "totals": {"steps": len(run["steps"]), "results": ctx["counts"], "manifestShots": dict_of(manifest, "shots"),
                   "review": dict(ctx["stats"], sheets=len([g for g in ctx["groups"] if g["sheet"]]))},
        "steps": steps, "checklists": groups, "coverage": ctx["coverage"],
        "comparison": ctx["comparison"], "previousNote": None if ctx["comparison"] else ctx["previousNote"],
    }


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------

def find_auto_previous(result_dir, started_text, warnings):
    """父目录里其它含 manifest.json 的兄弟目录，取 startedUtc 早于本轮、最晚的一个。返回 (目录, 说明)。"""
    started = parse_utc(started_text)
    if started is None:
        return None, "本轮 startedUtc（%r）解析不了，--auto-previous 挑不出上一轮" % started_text
    parent = os.path.dirname(result_dir)
    try:
        names = sorted(os.listdir(parent))
    except OSError as error:
        return None, "--auto-previous 读不了父目录 %s：%s" % (parent, error)
    best = None
    for name in names:
        candidate = os.path.join(parent, name)
        if normalized(candidate) == normalized(result_dir) or not os.path.isfile(os.path.join(candidate, "manifest.json")):
            continue
        try:
            other = load_manifest(candidate)
        except ReviewError as error:
            warnings.append("--auto-previous 跳过 %s：%s" % (name, error))
            continue
        when = parse_utc(text_of(other, "startedUtc"))
        if when is not None and when < started and (best is None or when > best[0]):
            best = (when, candidate)
    if best is None:
        return None, "--auto-previous 在 `%s` 里没找到 startedUtc 早于本轮的结果目录" % parent
    return best[1], None


def parse_args(argv):
    parser = argparse.ArgumentParser(description="F3 全自动实机验收结果目录的离线审阅：缩略图拼图、红项置顶、与上一轮对比。")
    parser.add_argument("result_dir", help="结果目录（含 manifest.json）")
    previous = parser.add_mutually_exclusive_group()
    previous.add_argument("--previous", help="上一轮结果目录")
    previous.add_argument("--auto-previous", action="store_true",
                          help="在结果目录的父目录里找 startedUtc 早于本轮、最晚的一轮")
    parser.add_argument("--output", help="输出目录（默认 <结果目录>/review/；不许落在仓库内）")
    parser.add_argument("--thumb", type=int, default=360, help="缩略图宽度，像素（默认 360）")
    return parser.parse_args(argv)


def run_review(args):
    if PIL_IMPORT_ERROR is not None:
        raise ReviewError("需要 Pillow 才能生成缩略图与拼图，当前 Python（%s）没有安装：%s。安装：%s -m pip install Pillow"
                          % (sys.executable, PIL_IMPORT_ERROR, sys.executable))
    if not 64 <= args.thumb <= 2048:
        raise ReviewError("--thumb 取 64–2048，当前 %d" % args.thumb)
    result_dir = os.path.abspath(args.result_dir)
    if not os.path.isdir(result_dir):
        raise ReviewError("结果目录不存在：" + result_dir)
    output_dir = os.path.abspath(args.output) if args.output else os.path.join(result_dir, "review")
    if is_within(output_dir, REPO_ROOT):
        raise ReviewError("输出目录 %s 落在仓库 %s 之内：审阅结果不进仓库，用 --output 指到仓库外"
                          % (normalized(output_dir), normalized(REPO_ROOT)))

    warnings = []
    current = build_run(result_dir, load_manifest(result_dir), warnings)
    previous, previous_note = None, "未指定上一轮（加 --previous <目录> 或 --auto-previous）。"
    previous_dir = os.path.abspath(args.previous) if args.previous else None
    if args.auto_previous:
        previous_dir, previous_note = find_auto_previous(result_dir, current["startedUtc"], warnings)
    if previous_dir:
        if not os.path.isdir(previous_dir):
            raise ReviewError("上一轮结果目录不存在：" + previous_dir)
        previous_warnings = []
        previous = build_run(previous_dir, load_manifest(previous_dir), previous_warnings)
        warnings += ["上一轮：" + warning for warning in previous_warnings]

    restore_raw = dict_of(current["manifest"], "restore")
    restore = {key: text_of(restore_raw, key) for key in ("story", "detail", "environment", "items", "money")}
    restore["storyNeedsAttention"] = restore_needs_attention(restore["story"])
    restore["environmentNeedsAttention"] = restore_needs_attention(restore["environment"])
    restore["attention"] = restore["storyNeedsAttention"] or restore["environmentNeedsAttention"]

    thumb_w = args.thumb
    thumb_h = int(round(thumb_w * 9 / 16.0))
    os.makedirs(output_dir, exist_ok=True)
    stats = {"thumbs": 0, "placeholders": 0}
    prepare_thumbs(current, output_dir, thumb_w, thumb_h, stats)
    groups = build_checklist_groups(current["steps"])
    for group in groups:
        try:
            render_sheet(group, output_dir, thumb_w, thumb_h)
        except Exception as error:  # 单张拼图失败不拖垮整份审阅
            warnings.append("拼图 %s 生成失败：%s: %s" % (group["checklist"], type(error).__name__, error))
            group["sheet"] = None

    ctx = {
        "current": current, "restore": restore, "ordered": order_steps(current["steps"]),
        "counts": count_results(current["steps"]), "groups": groups, "stats": stats,
        "coverage": summarize_coverage(current["manifest"]),
        "comparison": compare_runs(current, previous) if previous else None,
        "previousNote": previous_note or "", "warnings": warnings, "outputDir": output_dir,
        "font": load_font(max(11, thumb_w // 26))[1],
    }
    md_path, json_path = os.path.join(output_dir, "review.md"), os.path.join(output_dir, "review.json")
    with open(json_path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(render_json(ctx), handle, ensure_ascii=False, indent=2)
        handle.write("\n")
    with open(md_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(render_markdown(ctx))

    counts = ctx["counts"]
    print("autotest_review: %s 步骤 PASS=%d FAIL=%d SKIP=%d；缩略图 %d、占位 %d；拼图 %d 张%s" % (
        current["runId"] or "(无 runId)", counts["PASS"], counts["FAIL"], counts["SKIP"] + counts["OTHER"],
        stats["thumbs"], stats["placeholders"], len([g for g in groups if g["sheet"]]),
        "；还原需要处理" if restore["attention"] else ""))
    print("  对比：" + ("上一轮 " + previous["runId"] if previous else previous_note))
    print("  review.md   -> " + md_path)
    print("  review.json -> " + json_path)
    return 0


def main(argv=None):
    _force_utf8_output()
    args = parse_args(argv)
    try:
        return run_review(args)
    except ReviewError as error:
        print("autotest_review: 错误：" + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
