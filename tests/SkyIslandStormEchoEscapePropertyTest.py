# -*- coding: utf-8 -*-
u"""R-12 离线复算：噬风的风暴圈要多快才逃得出，以及噬风·回响有没有比首战更难逃。

生产常量全部从 `DebugAndTools/SkyIsland/SkyIslandStormBoss.cs` 读，不在这里写第二份：
`PulseRadius`、`PulseTelegraph`、`PulseWaves`、`WaveGap`、`RadiusForWave` 的每波增量，以及 `Detonate` 的圆心取法。

模型（写明假设，结论只到 L2）：
- 玩家在圈心被预警，立刻沿直线往外跑，速度 vp；官方爆炸没有距离衰减，第 k 波在 t_k = 预警 + k × 间隔 时引爆，
  玩家离圆心必须超过 R_k 才不吃伤害；
- **首战**：`Detonate` 每一波重读本体当前位置，本体沿同一直线以 vb 追人（最坏情况：正对着追）。
  需要的速度 vp(vb) = vb + max_k R_k / t_k（R-12：纸面上的 5 m/s 只在本体不追人时成立）；
- **回响**：圆心钉在预警开始时的位置，三波之后在原地再响一声（半径同最后一波、再晚一个间隔）。
  需要的速度 vp = max(max_k R_k / t_k, R_last / t_echo)，与本体追不追人无关；
- 忽略地形、墙体遮挡（墙挡得住这一击，只会让逃圈更容易）与玩家转向、加速的时间。

判据：
1. 解析式与逐帧模拟（dt = 0.005 s，二分求最低速度）一致；
2. 对 0–8 m/s 的每一种追人速度，回响需要的速度都不高于首战（「回响不许比首战更难逃圈」）；
3. 回响需要的速度与本体追人速度无关（圆心钉住了），且不高于首战的纸面上限 5.5 m/s（与 SkyIslandContentExpansionGuard 同一条线）；
4. 回响那一声的半径不超过最后一波、时间晚于最后一波。

破坏探针：圆心改回跟着本体、回响那一声半径放大、回响那一声提前到最后一波同时——判据都必须红。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

BOSS = (ROOT / "DebugAndTools/SkyIsland/SkyIslandStormBoss.cs").read_text(encoding="utf-8-sig").replace("\r\n", "\n")
SPEED_CAP = 5.5
CHASE_SPEEDS = [x * 0.5 for x in range(0, 17)]


def const(pattern, label, source=BOSS):
    match = re.search(pattern, source)
    assert match, "SkyIslandStormBoss.cs 里读不到 " + label
    return float(match.group(1))


def echo_after_gap(squashed):
    """回响那一声晚不晚一个间隔：波循环体里没有 break / continue，以无条件的 WaveGap 等待收尾，循环一结束紧接着就是回响那一声。"""
    head = "for (int wave = 0; wave < PulseWaves && !Aborted(); wave++) {"
    start = squashed.find(head)
    if start < 0:
        return False
    depth, i = 0, start + len(head) - 1
    for i in range(start + len(head) - 1, len(squashed)):
        if squashed[i] == "{":
            depth += 1
        elif squashed[i] == "}":
            depth -= 1
            if depth == 0:
                break
    body, after = squashed[start + len(head):i].strip(), squashed[i + 1:].lstrip()
    wait = "float waveUntil = Time.time + WaveGap; while (Time.time < waveUntil && !Aborted()) yield return null;"
    return (body.endswith(wait) and "break;" not in body and "continue;" not in body
            and after.startswith("if (echo && !Aborted())"))


def read_model(source=BOSS):
    squashed = re.sub(r"\s+", " ", clean_source(source))
    model = {
        "radius": const(r"internal const float PulseRadius = ([\d.]+)f;", "PulseRadius", source),
        "telegraph": const(r"internal const float PulseTelegraph = ([\d.]+)f;", "PulseTelegraph", source),
        "waves": int(const(r"internal const int PulseWaves = (\d+);", "PulseWaves", source)),
        "gap": const(r"internal const float WaveGap = ([\d.]+)f;", "WaveGap", source),
        "step": const(r"return PulseRadius \+ wave \* ([\d.]+)f;", "RadiusForWave 的每波增量", source),
        # 首战与回响的圆心取法都从 Detonate 读：回响钉在 eyeOrigin、首战读本体位置。
        "echo_anchored": "Vector3 origin = echo ? eyeOrigin : boss.transform.position;" in squashed,
        # 回响那一声：循环之后在原地再引爆最后一波的半径（时间 = 循环结束 = 预警 + 波数 × 间隔）。
        "echo_wave": "if (echo && !Aborted()) { try { Detonate(PulseWaves - 1); }" in squashed,
        "echo_wave_index": None,
        "echo_after_gap": echo_after_gap(squashed),
    }
    match = re.search(r"if \(echo && !Aborted\(\)\) \{ try \{ Detonate\(PulseWaves - (\d+)\); \}", squashed)
    model["echo_wave_index"] = model["waves"] - int(match.group(1)) if match else None
    return model


def waves(model, echo):
    rows = [(model["telegraph"] + k * model["gap"], model["radius"] + k * model["step"], True) for k in range(model["waves"])]
    if echo and model["echo_wave"] and model["echo_wave_index"] is not None:
        slots = model["waves"] if model["echo_after_gap"] else model["waves"] - 1
        rows.append((model["telegraph"] + slots * model["gap"],
                     model["radius"] + model["echo_wave_index"] * model["step"], model["echo_anchored"]))
    return rows


def analytic(model, echo, chase):
    need = 0.0
    for time, radius, anchored_row in waves(model, echo):
        follows = not (echo and model["echo_anchored"])
        need = max(need, radius / time + (chase if follows else 0.0))
    return need


def simulate_escapes(model, echo, chase, speed, dt=0.005):
    """逐帧：玩家从圆心沿 +x 以 speed 跑；本体从圆心以 chase 追（不越过玩家）。每一波引爆时核对是否已经在圈外。"""
    anchored = echo and model["echo_anchored"]
    schedule = sorted(waves(model, echo))
    player, boss, t, index = 0.0, 0.0, 0.0, 0
    while index < len(schedule):
        t_next, radius, _ = schedule[index]
        if t + dt >= t_next:
            step = t_next - t
            player += speed * step
            boss = min(player, boss + chase * step)
            t = t_next
            centre = 0.0 if anchored else boss
            if player - centre <= radius:
                return False
            index += 1
            continue
        player += speed * dt
        boss = min(player, boss + chase * dt)
        t += dt
    return True


def simulated(model, echo, chase):
    low, high = 0.0, 40.0
    for _ in range(60):
        mid = (low + high) / 2.0
        if simulate_escapes(model, echo, chase, mid):
            high = mid
        else:
            low = mid
    return high


def violations(model):
    errors = []
    if not model["echo_anchored"]:
        errors.append("回响的圆心没有钉在风眼（Detonate 仍读本体位置）：追人时回响和首战一样难逃")
    if not model["echo_wave"] or model["echo_wave_index"] is None:
        errors.append("找不到回响那一声（循环之后 Detonate(PulseWaves - 1)）")
    echo_rows = waves(model, True)
    if len(echo_rows) == model["waves"] + 1:
        last_time, last_radius, _ = waves(model, False)[-1]
        echo_time, echo_radius, _ = echo_rows[-1]
        if echo_radius > last_radius + 1e-9 or echo_time <= last_time:
            errors.append("回响那一声必须不大于最后一波、并晚于最后一波（%.2f m @ %.2f s，最后一波 %.2f m @ %.2f s）"
                          % (echo_radius, echo_time, last_radius, last_time))
    base = analytic(model, True, 0.0)
    for chase in CHASE_SPEEDS:
        first = analytic(model, False, chase)
        echo = analytic(model, True, chase)
        if echo > first + 1e-6:
            errors.append("本体追人 %.1f m/s 时回响要 %.2f m/s，比首战的 %.2f m/s 更难逃" % (chase, echo, first))
        if abs(echo - base) > 1e-6:
            errors.append("回响需要的逃圈速度随本体追人速度变化（%.1f m/s 时 %.2f，静止时 %.2f）：圆心没钉住" % (chase, echo, base))
    if base > SPEED_CAP + 1e-9:
        errors.append("回响需要 %.2f m/s，高于首战的纸面上限 %.1f m/s" % (base, SPEED_CAP))
    return errors


def main():
    model = read_model()
    errors = violations(model)
    for chase in (0.0, 1.5, 3.0, 4.5):
        for echo in (False, True):
            a, s = analytic(model, echo, chase), simulated(model, echo, chase)
            if abs(a - s) > 0.05:
                errors.append("解析式与逐帧模拟对不上（echo=%s, chase=%.1f）：%.3f vs %.3f" % (echo, chase, a, s))

    # 破坏探针：前三条改源码再读模型，最后一条直接改模型
    probes = {
        "圆心改回跟着本体": BOSS.replace("Vector3 origin = echo ? eyeOrigin : boss.transform.position;",
                                   "Vector3 origin = boss.transform.position;", 1),
        "回响那一声找不到或不是最后一波": BOSS.replace("try { Detonate(PulseWaves - 1); }", "try { Detonate(PulseWaves + 1); }", 1),
        "回响那一声提前到最后一波同时": BOSS.replace("                float waveUntil = Time.time + WaveGap;\n",
                                          "                if (wave + 1 == PulseWaves) break;\n                float waveUntil = Time.time + WaveGap;\n", 1),
    }
    for label, text in probes.items():
        if text == BOSS or not violations(read_model(text)):
            errors.append("破坏探针失效：" + label)
    if not violations(dict(model, echo_wave_index=model["waves"] + 2)):
        errors.append("破坏探针失效：回响那一声半径放大两档")

    if errors:
        print("SkyIslandStormEchoEscapePropertyTest: FAIL")
        for error in errors:
            print("  - " + error)
        raise SystemExit(1)
    table = " / ".join("追%.1f→首战%.2f·回响%.2f" % (c, analytic(model, False, c), analytic(model, True, c)) for c in (0.0, 1.5, 3.0, 4.5))
    print("PASS SkyIslandStormEchoEscapePropertyTest（第一圈 %.1f m / 预警 %.1f s / 间隔 %.2f s；需要的逃圈速度 m/s：%s；"
          "解析式与逐帧模拟一致；4 个破坏探针被拒）" % (model["radius"], model["telegraph"], model["gap"], table))


if __name__ == "__main__":
    main()
