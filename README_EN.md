# Boss Rush Mod for Escape from Duckov (鸭科夫)

**English** | **[中文](README.md)**

<p align="center">
  <img src="preview.png" alt="Boss Rush Mod Preview" width="400">
</p>

[![Steam Workshop](https://img.shields.io/badge/Steam%20Workshop-3612465423-blue?logo=steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=3612465423)
[![Game](https://img.shields.io/badge/Game-鸭科夫%20Duckov-orange)](https://store.steampowered.com/app/3167020)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## 📖 Introduction

Boss Rush Mod adds multiple challenge modes to Escape from Duckov, allowing you to battle wave after wave of bosses in a dedicated arena!

## ✨ Features

### 🎮 Game Modes

| Mode | Description |
|------|-------------|
| **Easy** | 1 Boss per wave, perfect for beginners |
| **Normal** | 3 Bosses per wave, standard challenge |
| **Infinite Hell** | Endless waves, configurable boss count, test your limits! |
| **Rags to Riches** | Endless waves, configurable boss count, enter naked, random gear |

### ⚙️ Configurable Options

Adjust via [ModConfig](https://steamcommunity.com/sharedfiles/filedetails/?id=XXXXXXX) or local config file:

- Wave interval time (2-60 seconds)
- Boss loot randomization
- Infinite Hell bosses per wave (1-10)
- Boss global stat multiplier (0.1-10x)
- Rags to Riches enemies per wave (1-10)
- Loot box as cover (blocks bullets)

## 🛠️ Building from Source

### Requirements

- Windows OS
- .NET Framework 4.7.2 or .NET Standard 2.1
- Escape from Duckov game (for assembly references)

### Required Assemblies

Get from game directory `Duckov_Data\Managed\`:

```
Assembly-CSharp.dll
TeamSoda.Duckov.Core.dll
UnityEngine.dll
UnityEngine.CoreModule.dll
UnityEngine.UI.dll
Unity.TextMeshPro.dll
UniTask.dll
```

## 📁 Project Structure

```
BossRushMod/
├── Assets/              # Assets (icons, textures)
├── Build/               # Build output
├── Config/              # Configuration system
├── DebugAndTools/       # Debug utilities
├── Injection/           # Game system injection
├── Integration/         # Game integration logic
├── Interactables/       # Interactive objects
├── LootAndRewards/      # Loot and reward system
├── ModeD/               # Rags to Riches mode
├── UIAndSigns/          # UI and signage
├── Utilities/           # Utility functions
├── WavesArena/          # Wave and arena management
├── ModBehaviour.cs      # Main entry point (partial class)
├── info.ini             # Mod metadata
└── compile_official.bat # Build script
```

## 🔧 Configuration File

Location: `StreamingAssets/BossRushModConfig.txt`

```json
{
  "waveIntervalSeconds": 15,
  "enableRandomBossLoot": true,
  "useInteractBetweenWaves": false,
  "lootBoxBlocksBullets": false,
  "infiniteHellBossesPerWave": 3,
  "bossStatMultiplier": 1.0,
  "modeDEnemiesPerWave": 3
}
```

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

<p align="center">
  Made with ❤️ for 鸭科夫 community
</p>
