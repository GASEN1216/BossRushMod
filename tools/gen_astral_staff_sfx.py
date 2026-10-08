#!/usr/bin/env python3
"""程序化合成星阙（500104）的棍势与打击音效，输出到 Assets/Sounds/NewWeapons/astral_*.wav。

用法：
    python tools/gen_astral_staff_sfx.py            # 生成全部
    python tools/gen_astral_staff_sfx.py --check    # 只检查产物是否齐全、格式是否对

设计参照（owner 2026-10-08：「模仿黑神话悟空，攒了一豆就有清脆的声音，三豆很爽，触发时很爽的打击音效」）：
  棍势是三段递进的反馈——一豆、二豆是一声比一声高的清脆「叮」（敲击金属 / 玉片那种非谐分音、起音极快、
  尾巴干净）；三豆满是带低频托底和长余韵的「铮——」；松手出招先有一记蓄满放出的爆发，打中再给重拳。
  打击声的「爽」靠四层：起音瞬间的裂响、胸口那一下的次低频、饱和失真带来的颗粒、短混响尾巴把空间撑开。

  astral_bean_1.wav        一豆：清脆的「叮」（A6）
  astral_bean_2.wav        二豆：再高一档的「叮」（D7），带一点上扬
  astral_bean_full.wav     三豆满：低频托底 + 金属「铮」+ 上扬扫光 + 长余韵
  astral_release.wav       一、二豆出招：蓄满放出的爆发（扫风 + 能量裂响）
  astral_release_max.wav   三豆星陨出招：更厚更长的爆发，带上冲的呼啸
  astral_hit.wav           轻击命中：脆裂 + 闷响 + 星光尾
  astral_hit_finisher.wav  连招收尾：更沉、更长、带混响
  astral_heavy_hit.wav     横扫 / 回旋命中：重拳 + 饱和碎裂 + 金属余震 + 混响
  astral_slam.wav          星陨落地：次低频轰鸣 + 碎地噼啪 + 钟尾 + 大混响（空砸也响）
  astral_kill.wav          击杀「星散」：上行四音琶音
  astral_swing.wav         回旋破风
  astral_fizzle.wav        体力不足：短促发闷的哑音

产物：32 kHz 单声道 16-bit。Assets/ 被 .gitignore 忽略（local-only），本脚本进 git 保证可复现；
compile_official.bat 部署整个 Assets\\Sounds 树。只依赖 numpy 与标准库 wave。
"""

from __future__ import annotations

import argparse
import os
import sys
import wave

import numpy as np

SAMPLE_RATE = 32000

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "Assets", "Sounds", "NewWeapons")


# ---------------------------------------------------------------------------
# DSP 原语
# ---------------------------------------------------------------------------

def _t(duration: float) -> np.ndarray:
    return np.arange(int(SAMPLE_RATE * duration)) / SAMPLE_RATE


def _env(t: np.ndarray, tau: float, attack: float = 0.0015) -> np.ndarray:
    env = np.exp(-t / tau)
    if attack > 0:
        env *= np.clip(t / attack, 0.0, 1.0)
    return env


def _delayed(t: np.ndarray, delay: float) -> tuple[np.ndarray, np.ndarray]:
    local = t - delay
    on = local >= 0
    return np.where(on, local, 0.0), on


def _lowpass(x: np.ndarray, cutoff_hz: float) -> np.ndarray:
    alpha = 1.0 / (1.0 + SAMPLE_RATE / (2 * np.pi * cutoff_hz))
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc += alpha * (v - acc)
        y[i] = acc
    return y


def _highpass(x: np.ndarray, cutoff_hz: float) -> np.ndarray:
    return x - _lowpass(x, cutoff_hz)


def _band(x: np.ndarray, low_hz: float, high_hz: float) -> np.ndarray:
    return _highpass(_lowpass(x, high_hz), low_hz)


def _glide(t: np.ndarray, start: float, end: float, tau: float) -> np.ndarray:
    """频率从 start 指数滑到 end 的正弦（相位积分，滑音不跳）。"""
    freq = end + (start - end) * np.exp(-t / tau)
    return np.sin(2 * np.pi * np.cumsum(freq) / SAMPLE_RATE)


def _strike(t: np.ndarray, delay: float, freq: float, tau: float, bend: float = 0.0) -> np.ndarray:
    """敲击金属片 / 玉片：非谐分音（自由梁 1 : 2.76 : 5.40 : 8.93）、起音 1 ms、高分音衰减更快。
    bend > 0 时起音瞬间音高略低、几十毫秒内回正，听起来「往上挑」。"""
    local, on = _delayed(t, delay)
    pitch = 1.0 - bend * np.exp(-local / 0.025)
    phase = 2 * np.pi * np.cumsum(freq * pitch) / SAMPLE_RATE
    partials = ((1.0, 1.0), (2.76, 0.5), (5.40, 0.22), (8.93, 0.1))
    # 超过 0.8 倍奈奎斯特的分音直接不要，免得折叠成刺耳的混叠
    tone = sum(a * np.sin(phase * k) * np.exp(-local / (tau / (1 + i * 0.9)))
               for i, (k, a) in enumerate(partials) if freq * k < SAMPLE_RATE * 0.4)
    return np.where(on, tone * np.clip(local / 0.001, 0.0, 1.0), 0.0)


def _bell(t: np.ndarray, delay: float, freq: float, tau: float) -> np.ndarray:
    local, on = _delayed(t, delay)
    partials = ((1.0, 1.0), (2.01, 0.45), (2.76, 0.28), (4.07, 0.14))
    tone = sum(a * np.sin(2 * np.pi * freq * k * local) * np.exp(-local / (tau / (1 + i * 0.6)))
               for i, (k, a) in enumerate(partials))
    return np.where(on, tone * np.clip(local / 0.002, 0.0, 1.0), 0.0)


def _saturate(x: np.ndarray, drive: float) -> np.ndarray:
    """软削波：给打击声加颗粒与密度，听起来更「实」。"""
    return np.tanh(x * drive) / np.tanh(drive)


def _reverb(x: np.ndarray, seconds: float, mix: float, rng, damp_hz: float = 4500.0) -> np.ndarray:
    """指数衰减噪声脉冲响应的 FFT 卷积：短促的空间尾巴，不拖泥带水。"""
    ir_t = _t(seconds)
    ir = rng.standard_normal(ir_t.size) * np.exp(-ir_t / (seconds / 5.0))
    ir = _lowpass(ir, damp_hz)
    ir[: int(0.008 * SAMPLE_RATE)] = 0.0  # 8 ms 预延迟，干声的起音不被糊掉
    ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    n = x.size + ir.size - 1
    size = 1 << (n - 1).bit_length()
    wet = np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size)[: x.size]
    wet *= np.max(np.abs(x)) / (np.max(np.abs(wet)) + 1e-9)
    return x + wet * mix


def _finish(x: np.ndarray, peak: float) -> np.ndarray:
    x = x - np.mean(x)
    m = float(np.max(np.abs(x)))
    if m > 1e-9:
        x = x * (peak / m)
    fade = int(0.015 * SAMPLE_RATE)
    x[-fade:] *= np.linspace(1.0, 0.0, fade)
    return x


# ---------------------------------------------------------------------------
# 棍势
# ---------------------------------------------------------------------------

def bean_1(rng) -> np.ndarray:
    t = _t(0.6)
    ding = _strike(t, 0.0, 1760.0, 0.32, bend=0.015)
    tick = _highpass(rng.standard_normal(t.size), 5000.0) * _env(t, 0.0025, 0.0003) * 0.6
    air = _band(rng.standard_normal(t.size), 6000.0, 11000.0) * _env(t, 0.12, 0.004) * 0.05
    return _finish(_reverb(ding + tick + air, 0.5, 0.18, rng, 7000.0), 0.42)


def bean_2(rng) -> np.ndarray:
    t = _t(0.7)
    ding = _strike(t, 0.0, 2349.3, 0.34, bend=0.03) + _strike(t, 0.035, 3520.0, 0.18) * 0.35
    tick = _highpass(rng.standard_normal(t.size), 5500.0) * _env(t, 0.0025, 0.0003) * 0.6
    air = _band(rng.standard_normal(t.size), 6000.0, 11000.0) * _env(t, 0.15, 0.004) * 0.06
    return _finish(_reverb(ding + tick + air, 0.55, 0.2, rng, 7500.0), 0.45)


def bean_full(rng) -> np.ndarray:
    """三豆满：「嗡」的低频托底一下，紧接金属「铮」，一道上扬的扫光，长余韵里亮晶晶地散开。"""
    t = _t(1.6)
    whomp = _glide(t, 95.0, 55.0, 0.12) * _env(t, 0.16, 0.012) * 0.9
    shing = (_strike(t, 0.0, 1318.5, 0.9, bend=0.02) + _strike(t, 0.0, 1975.5, 0.75) * 0.7
             + _strike(t, 0.012, 2637.0, 0.6) * 0.5 + _strike(t, 0.03, 3951.1, 0.35) * 0.3)
    sweep_freq = 1800.0 + 5200.0 * np.clip(t / 0.35, 0.0, 1.0)
    sweep = np.sin(2 * np.pi * np.cumsum(sweep_freq) / SAMPLE_RATE) * np.sin(np.pi * np.clip(t / 0.35, 0, 1)) ** 2 * 0.12
    shimmer = _band(rng.standard_normal(t.size), 5000.0, 12000.0) * np.sin(np.pi * np.clip(t / 1.2, 0, 1)) ** 2 * 0.08
    dry = whomp + shing * 0.55 + sweep + shimmer
    return _finish(_reverb(dry, 1.2, 0.35, rng, 6500.0), 0.55)


# ---------------------------------------------------------------------------
# 出招与命中
# ---------------------------------------------------------------------------

def release(rng, big: bool) -> np.ndarray:
    """出招爆发：风从身后抽过来（扫频噪声），出手瞬间一记能量裂响，低频推一下。"""
    seconds = 0.95 if big else 0.5
    t = _t(seconds)
    # 爆点对齐出招：一、二豆的横扫 0.1 秒 / 回旋首段 0.16 秒出手；星陨在空中抡棍的 0.4 秒左右。
    rise = 0.38 if big else 0.11
    shape = np.where(t < rise, (t / rise) ** 2, np.exp(-(t - rise) / (0.22 if big else 0.12)))
    lo = _band(rng.standard_normal(t.size), 250.0, 1500.0)
    hi = _band(rng.standard_normal(t.size), 1800.0, 7000.0)
    mixk = np.clip(t / rise, 0, 1)
    wind = (lo * (1 - mixk) + hi * mixk) * shape
    snap_t, on = _delayed(t, rise)
    snap = np.where(on, _highpass(rng.standard_normal(t.size), 2500.0) * np.exp(-snap_t / 0.012), 0.0)
    push = np.where(on, np.sin(2 * np.pi * (70.0 if big else 95.0) * snap_t) * np.exp(-snap_t / (0.12 if big else 0.07)), 0.0)
    zing = np.where(on, _strike(t, rise, 1568.0 if big else 2093.0, 0.25) * 0.25, 0.0)
    dry = wind * 0.9 + snap * 0.8 + push * (1.2 if big else 0.8) + zing
    return _finish(_reverb(_saturate(dry, 1.6), 0.6 if big else 0.4, 0.22, rng), 0.58 if big else 0.5)


def hit(rng) -> np.ndarray:
    t = _t(0.38)
    crack = _highpass(rng.standard_normal(t.size), 1800.0) * _env(t, 0.007, 0.0004)
    body = _band(rng.standard_normal(t.size), 250.0, 2200.0) * _env(t, 0.028)
    thump = _glide(t, 180.0, 80.0, 0.03) * _env(t, 0.06, 0.0008)
    sparkle = _strike(t, 0.01, 2093.0, 0.12) * 0.18
    dry = _saturate(crack * 0.9 + body * 0.6 + thump * 1.2, 2.2) + sparkle
    return _finish(_reverb(dry, 0.3, 0.12, rng), 0.5)


def hit_finisher(rng) -> np.ndarray:
    t = _t(0.6)
    crack = _highpass(rng.standard_normal(t.size), 1500.0) * _env(t, 0.012, 0.0004)
    body = _band(rng.standard_normal(t.size), 180.0, 2200.0) * _env(t, 0.05)
    thump = _glide(t, 150.0, 55.0, 0.05) * _env(t, 0.11, 0.0008)
    ring = (_strike(t, 0.015, 1568.0, 0.25) + _strike(t, 0.03, 2349.3, 0.2) * 0.6) * 0.22
    dry = _saturate(crack * 0.9 + body * 0.7 + thump * 1.4, 2.6) + ring
    return _finish(_reverb(dry, 0.45, 0.2, rng), 0.55)


def heavy_hit(rng) -> np.ndarray:
    t = _t(0.9)
    boom = _glide(t, 120.0, 42.0, 0.07) * _env(t, 0.17, 0.0015)
    crunch = _band(rng.standard_normal(t.size), 300.0, 3000.0) * _env(t, 0.06, 0.0008)
    crack = _highpass(rng.standard_normal(t.size), 2200.0) * _env(t, 0.009, 0.0004)
    ring = (_strike(t, 0.02, 1318.5, 0.4) + _strike(t, 0.04, 1975.5, 0.32) * 0.7) * 0.2
    dry = _saturate(boom * 1.5 + crunch * 0.7 + crack * 0.8, 2.8) + ring
    return _finish(_reverb(dry, 0.7, 0.3, rng, 3800.0), 0.6)


def slam(rng) -> np.ndarray:
    t = _t(2.0)
    sub = _glide(t, 90.0, 32.0, 0.15) * _env(t, 0.3, 0.002)
    rumble = _lowpass(rng.standard_normal(t.size), 380.0) * _env(t, 0.28, 0.003) * 2.2
    crack = _highpass(rng.standard_normal(t.size), 1600.0) * _env(t, 0.025, 0.0004)
    debris = np.zeros_like(t)
    for center in rng.uniform(0.04, 0.6, size=16):
        idx = int(center * SAMPLE_RATE)
        width = int(0.004 * SAMPLE_RATE)
        end = min(idx + width, t.size)
        debris[idx:end] += rng.standard_normal(end - idx) * np.exp(-center / 0.3)
    debris = _highpass(debris, 1200.0)
    tail = (_bell(t, 0.06, 523.25, 0.9) + _bell(t, 0.08, 783.99, 0.8) * 0.7
            + _bell(t, 0.1, 1046.5, 0.7) * 0.5 + _bell(t, 0.14, 1568.0, 0.55) * 0.35) * 0.18
    # 只压起音那一截：饱和放在冲击上，尾巴保留动态，听起来是「一砸」而不是一段持续的轰鸣。
    hitpart = (sub * 1.6 + crack * 0.9) * _env(t, 0.12, 0.0)
    dry = _saturate(hitpart, 2.0) + sub * 0.5 * (1 - _env(t, 0.12, 0.0)) + rumble * 0.7 + debris * 0.6 + tail
    return _finish(_reverb(dry, 1.2, 0.25, rng, 3200.0), 0.7)


def kill(rng) -> np.ndarray:
    t = _t(0.9)
    notes = (1046.5, 1318.5, 1568.0, 2093.0)
    arp = sum(_strike(t, 0.04 * i, f, 0.3) * (1.0 - 0.1 * i) for i, f in enumerate(notes))
    air = _band(rng.standard_normal(t.size), 2500.0, 8000.0) * np.sin(np.pi * np.clip(t / 0.45, 0, 1)) ** 2 * 0.1
    return _finish(_reverb(arp * 0.6 + air, 0.6, 0.25, rng, 7000.0), 0.48)


def swing(rng) -> np.ndarray:
    t = _t(0.38)
    shape = np.sin(np.pi * np.clip(t / 0.3, 0, 1)) ** 2
    lo = _band(rng.standard_normal(t.size), 350.0, 1400.0)
    hi = _band(rng.standard_normal(t.size), 1600.0, 5200.0)
    sweep = np.clip(t / 0.3, 0, 1)
    wind = (lo * (1 - sweep) + hi * sweep) * shape
    glint = _glide(t, 1400.0, 2800.0, 0.2) * shape * 0.12
    return _finish(wind + glint, 0.45)


def fizzle(rng) -> np.ndarray:
    t = _t(0.25)
    dull = _glide(t, 260.0, 150.0, 0.06) * _env(t, 0.06, 0.002)
    puff = _lowpass(rng.standard_normal(t.size), 900.0) * _env(t, 0.05, 0.003) * 2.0
    return _finish(dull + puff * 0.5, 0.35)


GENERATORS = {
    "astral_bean_1.wav": bean_1,
    "astral_bean_2.wav": bean_2,
    "astral_bean_full.wav": bean_full,
    "astral_release.wav": lambda rng: release(rng, False),
    "astral_release_max.wav": lambda rng: release(rng, True),
    "astral_hit.wav": hit,
    "astral_hit_finisher.wav": hit_finisher,
    "astral_heavy_hit.wav": heavy_hit,
    "astral_slam.wav": slam,
    "astral_kill.wav": kill,
    "astral_swing.wav": swing,
    "astral_fizzle.wav": fizzle,
}


def _write(path: str, samples: np.ndarray) -> None:
    pcm = (np.clip(samples, -1.0, 1.0) * 32767.0).astype("<i2")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(SAMPLE_RATE)
        wf.writeframes(pcm.tobytes())


def check() -> int:
    bad = []
    for name in GENERATORS:
        path = os.path.join(OUT_DIR, name)
        if not os.path.isfile(path):
            bad.append(name + " missing")
            continue
        with wave.open(path, "rb") as wf:
            if (wf.getnchannels(), wf.getsampwidth(), wf.getframerate()) != (1, 2, SAMPLE_RATE) or wf.getnframes() == 0:
                bad.append(name + " wrong format")
    if bad:
        print("gen_astral_staff_sfx: FAIL " + ", ".join(bad))
        return 1
    print("gen_astral_staff_sfx: OK (%d files in %s)" % (len(GENERATORS), OUT_DIR))
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true")
    if parser.parse_args().check:
        return check()
    rng = np.random.default_rng(20261008)
    for name, gen in GENERATORS.items():
        samples = gen(rng)
        _write(os.path.join(OUT_DIR, name), samples)
        print("wrote %s (%.2fs, peak %.2f)" % (name, samples.size / SAMPLE_RATE, float(np.max(np.abs(samples)))))
    return check()


if __name__ == "__main__":
    sys.exit(main())
