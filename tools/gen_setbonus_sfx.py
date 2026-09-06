#!/usr/bin/env python3
"""程序化合成冰霜/雷霆套装音效，输出到 Assets/Sounds/SetBonus/。

用法：
    python tools/gen_setbonus_sfx.py            # 生成 4 个 wav
    python tools/gen_setbonus_sfx.py --check    # 只检查文件是否齐全

产物（32 kHz 单声道 16-bit，峰值约 -6 dBFS）：
    thunder_chain.wav    引雷术：噼啪电弧 + 短促雷鸣
    thunder_counter.wav  雷霆反震：更尖的放电 + 低频冲击
    frost_nova.wav       冰葬：玻璃质高频簇 + 寒风
    frost_counter.wav    受击冻结：短促冰裂

Assets/ 整体被 .gitignore 忽略（local-only），本脚本进 git 保证可复现；
compile_official.bat 的部署段会把 Assets\\Sounds\\SetBonus 拷到游戏目录。
只依赖 numpy 与标准库 wave，不需要 scipy。
"""

from __future__ import annotations

import argparse
import os
import sys
import wave

import numpy as np

SAMPLE_RATE = 32000
PEAK = 0.5  # -6 dBFS

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "Assets", "Sounds", "SetBonus")
FILES = ("thunder_chain.wav", "thunder_counter.wav", "frost_nova.wav", "frost_counter.wav")


def _t(duration: float) -> np.ndarray:
    return np.arange(int(SAMPLE_RATE * duration)) / SAMPLE_RATE


def _env_exp(t: np.ndarray, tau: float, attack: float = 0.002) -> np.ndarray:
    env = np.exp(-t / tau)
    if attack > 0:
        env *= np.clip(t / attack, 0.0, 1.0)
    return env


def _one_pole_lowpass(x: np.ndarray, cutoff_hz: float) -> np.ndarray:
    dt = 1.0 / SAMPLE_RATE
    rc = 1.0 / (2 * np.pi * cutoff_hz)
    alpha = dt / (rc + dt)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc += alpha * (v - acc)
        y[i] = acc
    return y


def _highpass(x: np.ndarray, cutoff_hz: float) -> np.ndarray:
    return x - _one_pole_lowpass(x, cutoff_hz)


def _bandpass(x: np.ndarray, low_hz: float, high_hz: float) -> np.ndarray:
    return _highpass(_one_pole_lowpass(x, high_hz), low_hz)


def _normalize(x: np.ndarray, peak: float = PEAK) -> np.ndarray:
    m = float(np.max(np.abs(x))) if x.size else 0.0
    if m <= 1e-9:
        return x
    return x * (peak / m)


def _write_wav(path: str, samples: np.ndarray) -> None:
    pcm = np.clip(samples, -1.0, 1.0)
    pcm = (pcm * 32767.0).astype("<i2")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(SAMPLE_RATE)
        wf.writeframes(pcm.tobytes())


# ---------------------------------------------------------------------------
# 雷霆
# ---------------------------------------------------------------------------

def thunder_chain(rng: np.random.Generator) -> np.ndarray:
    t = _t(0.55)
    # 电弧噼啪：高通白噪声 + 极短脉冲串
    crackle = _highpass(rng.standard_normal(t.size), 1800.0) * _env_exp(t, 0.07)
    for center in rng.uniform(0.0, 0.18, size=6):
        idx = int(center * SAMPLE_RATE)
        width = int(0.004 * SAMPLE_RATE)
        crackle[idx:idx + width] += rng.standard_normal(min(width, t.size - idx)) * 1.5
    # 短促雷鸣：120→55 Hz 下滑正弦 + 低通噪声
    freq = 120.0 * np.exp(-t / 0.35) + 55.0
    phase = 2 * np.pi * np.cumsum(freq) / SAMPLE_RATE
    rumble = np.sin(phase) * _env_exp(t, 0.22, attack=0.01)
    rumble += _one_pole_lowpass(rng.standard_normal(t.size), 220.0) * _env_exp(t, 0.3, attack=0.02) * 1.2
    mix = crackle * 0.9 + rumble * 0.8
    return _normalize(mix)


def thunder_counter(rng: np.random.Generator) -> np.ndarray:
    t = _t(0.4)
    zap_freq = 3200.0 * np.exp(-t / 0.05) + 700.0
    zap = np.sign(np.sin(2 * np.pi * np.cumsum(zap_freq) / SAMPLE_RATE)) * _env_exp(t, 0.045)
    zap = _bandpass(zap, 400.0, 6000.0)
    hiss = _highpass(rng.standard_normal(t.size), 2500.0) * _env_exp(t, 0.06)
    thump = np.sin(2 * np.pi * 70.0 * t) * _env_exp(t, 0.12, attack=0.005)
    mix = zap * 0.7 + hiss * 0.6 + thump * 0.9
    return _normalize(mix)


# ---------------------------------------------------------------------------
# 冰霜
# ---------------------------------------------------------------------------

def frost_nova(rng: np.random.Generator) -> np.ndarray:
    t = _t(0.7)
    partials = (1800.0, 2700.0, 3600.0, 5400.0, 7200.0)
    glass = np.zeros_like(t)
    for i, f in enumerate(partials):
        detune = 1.0 + rng.uniform(-0.004, 0.004)
        glass += np.sin(2 * np.pi * f * detune * t + rng.uniform(0, 2 * np.pi)) * _env_exp(t, 0.28 - 0.03 * i, attack=0.004)
    glass /= len(partials)
    shimmer = _bandpass(rng.standard_normal(t.size), 3000.0, 9000.0) * _env_exp(t, 0.18, attack=0.003)
    wind = _bandpass(rng.standard_normal(t.size), 300.0, 1400.0)
    wind *= np.sin(np.pi * np.clip(t / 0.6, 0.0, 1.0)) ** 2  # 先起后落的寒风
    mix = glass * 1.0 + shimmer * 0.5 + wind * 0.45
    return _normalize(mix)


def frost_counter(rng: np.random.Generator) -> np.ndarray:
    t = _t(0.32)
    crack = _highpass(rng.standard_normal(t.size), 1500.0) * _env_exp(t, 0.025)
    ring = (np.sin(2 * np.pi * 2400.0 * t) + 0.5 * np.sin(2 * np.pi * 3650.0 * t)) * _env_exp(t, 0.14, attack=0.002)
    mix = crack * 0.9 + ring * 0.6
    return _normalize(mix)


GENERATORS = {
    "thunder_chain.wav": thunder_chain,
    "thunder_counter.wav": thunder_counter,
    "frost_nova.wav": frost_nova,
    "frost_counter.wav": frost_counter,
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="只检查产物是否齐全")
    args = parser.parse_args()

    if args.check:
        missing = [name for name in FILES if not os.path.isfile(os.path.join(OUT_DIR, name))]
        if missing:
            print("gen_setbonus_sfx: MISSING " + ", ".join(missing))
            return 1
        print("gen_setbonus_sfx: OK (" + OUT_DIR + ")")
        return 0

    rng = np.random.default_rng(20260906)
    for name, gen in GENERATORS.items():
        samples = gen(rng)
        path = os.path.join(OUT_DIR, name)
        _write_wav(path, samples)
        print("wrote %s (%.2fs, peak %.2f)" % (path, samples.size / SAMPLE_RATE, float(np.max(np.abs(samples)))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
