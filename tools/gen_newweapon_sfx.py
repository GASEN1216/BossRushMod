#!/usr/bin/env python3
"""程序化合成 P0 五把新武器的触发音效，输出到 Assets/Sounds/NewWeapons/。

用法：
    python tools/gen_newweapon_sfx.py            # 生成 4 个 wav
    python tools/gen_newweapon_sfx.py --check    # 只检查文件是否齐全

产物（32 kHz 单声道 16-bit，峰值约 -6 dBFS，与 SetBonus 音效同一响度口径）：
    venom_burst.wav      毒蛇匕首满 5 层「毒性爆发」：低频闷响 + 黏稠气泡 + 嘶嘶残响
    soul_summon.wav      召唤法杖「灵魂召唤」：上行空灵泛音 + 气声，收尾一记实体化敲击
    shield_absorb.wav    能量盾正面吸收：金属质共振「叮」+ 快速上行的能量充能
    thunder_release.wav  雷电戒指满层释放：尖锐放电 + 低频冲击（比套装反震更重更短）

冰霜长矛没有专属音效：它的命中反馈每秒会响 1.3 次，加音效只会变成噪音；
减速本身已有官方 Cold buff 的表现。

Assets/ 整体被 .gitignore 忽略（local-only），本脚本进 git 保证可复现；
compile_official.bat 的部署段会把 Assets\\Sounds\\NewWeapons 拷到游戏目录。
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
OUT_DIR = os.path.join(ROOT, "Assets", "Sounds", "NewWeapons")
FILES = ("venom_burst.wav", "soul_summon.wav", "shield_absorb.wav", "thunder_release.wav")


# ---------------------------------------------------------------------------
# DSP 原语（与 tools/gen_setbonus_sfx.py 同款，保持两批音效的合成口径一致）
# ---------------------------------------------------------------------------

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
# 毒蛇匕首：毒性爆发
# ---------------------------------------------------------------------------

def venom_burst(rng: np.random.Generator) -> np.ndarray:
    """闷响开场 + 黏稠气泡 + 长嘶嘶尾巴，读起来是「一团毒雾炸开」而不是「爆炸」。"""
    t = _t(0.6)

    # 低频闷响：90->45 Hz 下滑，短促，给爆发一点分量但不抢枪声
    freq = 90.0 * np.exp(-t / 0.12) + 45.0
    thud = np.sin(2 * np.pi * np.cumsum(freq) / SAMPLE_RATE) * _env_exp(t, 0.1, attack=0.004)

    # 气泡：若干随机时刻的短促带通脉冲，模拟黏液翻涌
    bubbles = np.zeros_like(t)
    for center in rng.uniform(0.02, 0.4, size=9):
        idx = int(center * SAMPLE_RATE)
        width = int(0.035 * SAMPLE_RATE)
        end = min(idx + width, t.size)
        seg = np.arange(end - idx) / SAMPLE_RATE
        f = rng.uniform(320.0, 900.0)
        bubbles[idx:end] += np.sin(2 * np.pi * f * seg) * np.exp(-seg / 0.012)
    bubbles = _bandpass(bubbles, 250.0, 2000.0)

    # 嘶嘶：毒雾扩散的长尾窄带噪声，先起后落
    hiss = _bandpass(rng.standard_normal(t.size), 2200.0, 7000.0)
    hiss *= np.sin(np.pi * np.clip(t / 0.55, 0.0, 1.0)) ** 2

    mix = thud * 0.95 + bubbles * 0.7 + hiss * 0.45
    return _normalize(mix)


# ---------------------------------------------------------------------------
# 召唤法杖：灵魂召唤
# ---------------------------------------------------------------------------

def soul_summon(rng: np.random.Generator) -> np.ndarray:
    """上行空灵泛音 + 气声，末尾一记实体化敲击，对应「撕裂空间召唤」。"""
    t = _t(0.9)

    # 上行泛音簇：基频 220->660 Hz 缓慢滑上去，叠五度与八度
    base = 220.0 * (1.0 + 2.0 * np.clip(t / 0.6, 0.0, 1.0))
    phase = 2 * np.pi * np.cumsum(base) / SAMPLE_RATE
    swell = (np.sin(phase) + 0.6 * np.sin(1.5 * phase) + 0.4 * np.sin(2.0 * phase)) / 2.0
    swell *= np.sin(np.pi * np.clip(t / 0.72, 0.0, 1.0)) ** 1.5

    # 气声：中高频窄带噪声，跟着泛音一起涨
    breath = _bandpass(rng.standard_normal(t.size), 900.0, 4500.0)
    breath *= np.clip(t / 0.5, 0.0, 1.0) * np.exp(-np.clip(t - 0.55, 0.0, None) / 0.12)

    # 实体化敲击：0.62 秒处一记短促低频，表示「人到了」
    strike = np.zeros_like(t)
    idx = int(0.62 * SAMPLE_RATE)
    seg = np.arange(t.size - idx) / SAMPLE_RATE
    strike[idx:] = np.sin(2 * np.pi * 140.0 * seg) * np.exp(-seg / 0.06)

    mix = swell * 0.85 + breath * 0.4 + strike * 0.8
    return _normalize(mix)


# ---------------------------------------------------------------------------
# 能量盾：正面吸收
# ---------------------------------------------------------------------------

def shield_absorb(rng: np.random.Generator) -> np.ndarray:
    """金属共振「叮」+ 快速上行充能。这条 0.5 秒可能连响，必须短、亮、不拖泥带水。"""
    t = _t(0.35)

    # 共振：三条不谐和泛音，模拟能量护盾被打中
    ring = (
        np.sin(2 * np.pi * 1180.0 * t)
        + 0.55 * np.sin(2 * np.pi * 1790.0 * t)
        + 0.3 * np.sin(2 * np.pi * 2630.0 * t)
    ) / 1.85
    ring *= _env_exp(t, 0.09, attack=0.001)

    # 充能：频率快速上行的窄带噪声，表示伤害被转成生命
    charge_freq = 600.0 + 2600.0 * np.clip(t / 0.18, 0.0, 1.0)
    charge = np.sin(2 * np.pi * np.cumsum(charge_freq) / SAMPLE_RATE)
    charge *= _env_exp(t, 0.07, attack=0.006) * 0.6

    click = _highpass(rng.standard_normal(t.size), 3000.0) * _env_exp(t, 0.012)

    mix = ring * 1.0 + charge * 0.5 + click * 0.35
    return _normalize(mix)


# ---------------------------------------------------------------------------
# 雷电戒指：满层释放
# ---------------------------------------------------------------------------

def thunder_release(rng: np.random.Generator) -> np.ndarray:
    """比套装反震更重更短：一次攒了 5 层的放电，不是持续连锁。"""
    t = _t(0.45)

    # 放电：方波下滑，比 thunder_counter 起点更高、衰减更快
    zap_freq = 4200.0 * np.exp(-t / 0.04) + 620.0
    zap = np.sign(np.sin(2 * np.pi * np.cumsum(zap_freq) / SAMPLE_RATE)) * _env_exp(t, 0.038)
    zap = _bandpass(zap, 500.0, 7000.0)

    # 噼啪：几个随机短脉冲，让放电有颗粒感
    crackle = _highpass(rng.standard_normal(t.size), 2600.0) * _env_exp(t, 0.05)
    for center in rng.uniform(0.0, 0.12, size=4):
        idx = int(center * SAMPLE_RATE)
        width = int(0.003 * SAMPLE_RATE)
        end = min(idx + width, t.size)
        crackle[idx:end] += rng.standard_normal(end - idx) * 1.6

    # 冲击：落点的低频，比套装反震重一档
    thump = np.sin(2 * np.pi * 62.0 * t) * _env_exp(t, 0.15, attack=0.004)

    mix = zap * 0.75 + crackle * 0.55 + thump * 1.0
    return _normalize(mix)


GENERATORS = {
    "venom_burst.wav": venom_burst,
    "soul_summon.wav": soul_summon,
    "shield_absorb.wav": shield_absorb,
    "thunder_release.wav": thunder_release,
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="只检查产物是否齐全")
    args = parser.parse_args()

    if args.check:
        missing = [name for name in FILES if not os.path.isfile(os.path.join(OUT_DIR, name))]
        if missing:
            print("gen_newweapon_sfx: MISSING " + ", ".join(missing))
            return 1
        print("gen_newweapon_sfx: OK (" + OUT_DIR + ")")
        return 0

    rng = np.random.default_rng(20260907)
    for name, gen in GENERATORS.items():
        samples = gen(rng)
        path = os.path.join(OUT_DIR, name)
        _write_wav(path, samples)
        print("wrote %s (%.2fs, peak %.2f)" % (path, samples.size / SAMPLE_RATE, float(np.max(np.abs(samples)))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
