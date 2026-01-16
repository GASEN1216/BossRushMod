# BossRush 装备能力系统使用指南

本文档说明如何使用装备能力系统为新增装备或NPC添加能力。

---

## 目录

1. [系统概述](#系统概述)
2. [架构说明](#架构说明)
3. [快速开始：添加新装备能力](#快速开始添加新装备能力)
4. [复用现有飞行能力](#复用现有飞行能力)
5. [API参考](#api参考)

---

## 系统概述

装备能力系统是一套可复用的基础框架，用于实现装备触发的特殊能力（如飞行、冲刺、滑翔等）。

### 核心优势

- **代码复用**：新装备只需约300行代码
- **多级缓存**：反射结果缓存，低端机友好
- **自动管理**：装备/卸载自动激活/停用能力
- **统一配置**：所有参数集中在配置类

### 已实现的能力

| 能力 | 配置类 | 动作类 | TypeID |
|------|--------|--------|--------|
| 飞行1阶 | FlightConfig | CA_Flight | 500010 |

---

## 架构说明

系统由4个基类和4个实现类组成：

```
基类层（可复用）:
├── EquipmentAbilityConfig      - 配置基类
├── EquipmentAbilityAction      - 动作基类（继承 CharacterActionBase）
├── EquipmentAbilityManager     - 管理器基类（处理输入、注册角色）
└── EquipmentEffectManager      - 效果管理器基类（监听装备变化）

实现层（具体能力）:
├── FlightConfig                - 飞行配置
├── CA_Flight                   - 飞行动作
├── FlightAbilityManager        - 飞行管理器
└── FlightTotemEffectManager    - 飞行效果管理器
```

### 工作流程

```
装备物品 → EquipmentEffectManager 监听到装备变化
         ↓
         调用 EquipmentAbilityManager.RegisterAbility()
         ↓
         创建 EquipmentAbilityAction 实例并注册到角色
         ↓
         玩家按下输入键（如 Dash）
         ↓
         EquipmentAbilityManager 拦截输入，启动 Action
         ↓
         Action 执行能力逻辑（飞行、冲刺等）
```

---

## 快速开始：添加新装备能力

### 步骤1：创建配置类

创建 `YourEquipmentConfig.cs`：

```csharp
using BossRush.Common.Equipment;

namespace BossRush
{
    public class YourEquipmentConfig : EquipmentAbilityConfig
    {
        private static YourEquipmentConfig _instance;
        public static YourEquipmentConfig Instance
        {
            get
            {
                if (_instance == null) _instance = new YourEquipmentConfig();
                return _instance;
            }
        }

        private YourEquipmentConfig() { }

        // ========== 物品基础信息（必须） ==========

        public override int ItemTypeId => 500020;  // 分配一个新的TypeID

        public override string DisplayNameCN => "你的装备";
        public override string DisplayNameEN => "Your Equipment";

        public override string DescriptionCN => "装备后的效果描述";
        public override string DescriptionEN => "Effect description when equipped";

        public override int ItemQuality => 3;  // 1-5，物品品质

        public override string[] ItemTags => new string[] { "Equipment" };

        public override string IconAssetName => "vanilla_icon_name";  // 使用原版图标

        public override string LogPrefix => "[YourEquipment]";

        // ========== 能力参数（必须） ==========

        public override float CooldownTime => 0.5f;              // 冷却时间（秒）
        public override float StartupStaminaCost => 10f;         // 启动体力消耗
        public override float StaminaDrainPerSecond => 30f;      // 持续消耗/秒

        // ========== 音效配置（可选） ==========

        public override string StartSFX => "Char/Footstep/dash";  // 开始音效
        public override string LoopSFX => null;                    // 循环音效
        public override string EndSFX => null;                     // 结束音效

        // ========== 自定义参数（可选） ==========

        public float YourCustomParameter => 5f;
    }
}
```

### 步骤2：创建动作类

创建 `CA_YourAbility.cs`：

```csharp
using UnityEngine;
using BossRush.Common.Equipment;

namespace BossRush
{
    /// <summary>
    /// 你的能力动作类
    /// </summary>
    public class CA_YourAbility : EquipmentAbilityAction
    {
        private YourEquipmentConfig config => YourEquipmentConfig.Instance;

        protected override EquipmentAbilityConfig GetConfig() => config;

        // ========== 能力启动时调用 ==========
        protected override bool OnAbilityStart()
        {
            // 自定义启动逻辑
            LogIfVerbose("能力启动！");
            return true;  // 返回 false 会阻止启动
        }

        // ========== 能力每帧调用 ==========
        protected override void OnAbilityUpdate(float deltaTime)
        {
            // 自定义持续逻辑
            // 例如：移动角色、消耗体力、检测条件等

            // 获取移动输入
            Vector2 moveInput = GetMovementInput();

            // 获取角色当前位置
            Vector3 currentPos = characterController.transform.position;

            // 你的能力逻辑...
        }

        // ========== 能力停止时调用 ==========
        protected override void OnAbilityStop()
        {
            LogIfVerbose("能力停止");
        }

        // ========== 可选：控制是否自动消耗体力 ==========
        protected override bool ShouldAutoConsumeStamina()
        {
            // 返回 false 表示你自己处理体力消耗
            return false;
        }

        // ========== 可选：能力激活时是否可以使用手（射击） ==========
        protected override bool CanUseHandWhileActive()
        {
            return true;  // 允许在能力期间射击
        }

        // ========== 可选：体力耗尽时的处理 ==========
        protected override void OnStaminaDepleted()
        {
            // 默认行为是停止能力，这里可以自定义
            LogIfVerbose("体力耗尽");
        }
    }
}
```

### 步骤3：创建管理器类

创建 `YourAbilityManager.cs`：

```csharp
using UnityEngine;
using BossRush.Common.Equipment;

namespace BossRush
{
    public class YourAbilityManager : EquipmentAbilityManager<YourEquipmentConfig, CA_YourAbility>
    {
        private YourEquipmentConfig config => YourEquipmentConfig.Instance;

        protected override YourEquipmentConfig GetConfig() => config;

        // 指定要拦截的输入动作
        protected override string GetInputActionName() => "Dash";

        protected override CA_YourAbility CreateAbilityAction()
        {
            return actionObject.AddComponent<CA_YourAbility>();
        }

        // ========== 单例实现 ==========
        private static YourAbilityManager _instance;

        public new static YourAbilityManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    YourAbilityManager[] instances = FindObjectsOfType<YourAbilityManager>();
                    if (instances.Length > 0)
                    {
                        _instance = instances[0];
                        for (int i = 1; i < instances.Length; i++)
                            Destroy(instances[i].gameObject);
                    }
                    else
                    {
                        GameObject go = new GameObject("YourAbilityManager");
                        _instance = go.AddComponent<YourAbilityManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        protected override void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            OnManagerInitialized();
        }

        protected override void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            base.OnDestroy();
        }
    }
}
```

### 步骤4：创建效果管理器类

创建 `YourEffectManager.cs`：

```csharp
using UnityEngine;
using ItemStatsSystem.Items;
using BossRush.Common.Equipment;

namespace BossRush
{
    public class YourEffectManager : EquipmentEffectManager<YourEquipmentConfig, YourAbilityManager>
    {
        private YourEquipmentConfig config => YourEquipmentConfig.Instance;

        protected override YourEquipmentConfig GetConfig() => config;

        protected override YourAbilityManager GetAbilityManager() => YourAbilityManager.Instance;

        // ========== 单例实现 ==========
        private static YourEffectManager _instance;

        public new static YourEffectManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    YourEffectManager[] instances = FindObjectsOfType<YourEffectManager>();
                    if (instances.Length > 0)
                    {
                        _instance = instances[0];
                        for (int i = 1; i < instances.Length; i++)
                            Destroy(instances[i].gameObject);
                    }
                    else
                    {
                        GameObject go = new GameObject("YourEffectManager");
                        _instance = go.AddComponent<YourEffectManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        protected override void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            OnManagerInitialized();
        }

        protected override void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            base.OnDestroy();
        }

        // ========== 装备检测 ==========
        protected override bool IsMatchingItem(Item item)
        {
            // 方法1：通过 TypeID 匹配（推荐）
            return CheckItemByTypeId(item, YourEquipmentConfig.Instance.ItemTypeId);

            // 方法2：通过名称匹配
            // return CheckItemByName(item, "装备名称");

            // 方法3：通过 Bool 标记匹配
            // return CheckItemByBoolFlag(item, "YourFlagName");
        }
    }
}
```

### 步骤5：初始化系统

在你的启动代码中（如 `Main.cs` 或 `Bootstrap.cs`）：

```csharp
// 确保管理器实例存在
YourEffectManager.EnsureInstance();
YourAbilityManager.EnsureInstance();
```

---

## 复用现有飞行能力

### 场景1：新装备使用相同飞行能力

如果你想让新装备触发和飞行图腾完全相同的能力：

```csharp
public class NewFlightItemConfig : EquipmentAbilityConfig
{
    // 使用相同的 TypeID 范围或复用现有
    public const int NewFlightItemId = 500015;

    public override int ItemTypeId => NewFlightItemId;
    public override string DisplayNameCN => "飞行之翼";
    // ... 其他配置

    // 飞行参数复用
    public float MaxUpwardSpeed => FlightConfig.Instance.MaxUpwardSpeed;
    public float UpwardAcceleration => FlightConfig.Instance.UpwardAcceleration;
    // ...
}
```

```csharp
public class NewFlightEffectManager : EquipmentEffectManager<NewFlightItemConfig, FlightAbilityManager>
{
    protected override NewFlightItemConfig GetConfig() => NewFlightItemConfig.Instance;

    protected override FlightAbilityManager GetAbilityManager() => FlightAbilityManager.Instance;

    protected override bool IsMatchingItem(Item item)
    {
        return CheckItemByTypeId(item, NewFlightItemConfig.NewFlightItemId);
    }
}
```

### 场景2：创建飞行能力的变体

如果需要修改飞行行为，继承 `CA_Flight`：

```csharp
public class CA_EnhancedFlight : CA_Flight
{
    private EnhancedFlightConfig config => EnhancedFlightConfig.Instance;

    protected override EquipmentAbilityConfig GetConfig() => config;

    // 重写需要修改的部分
    protected override void OnAbilityUpdate(float deltaTime)
    {
        // 添加增强逻辑，如加速时产生粒子效果
        base.OnAbilityUpdate(deltaTime);

        if (UpwardSpeed > 3f)
        {
            // 产生拖尾粒子
        }
    }
}
```

### 场景3：NPC使用飞行能力

为NPC添加飞行能力，只需在NPC初始化时注册：

```csharp
public class NPCFlightController : MonoBehaviour
{
    private CharacterMainControl npcCharacter;
    private CA_Flight flightAction;

    void Start()
    {
        npcCharacter = GetComponent<CharacterMainControl>();

        // 创建飞行动作
        GameObject actionObject = new GameObject("NPCFlightAction");
        flightAction = actionObject.AddComponent<CA_Flight>();
        flightAction.characterController = npcCharacter;
    }

    void Update()
    {
        // NPC的AI逻辑决定何时飞行
        if (ShouldFly())
        {
            if (flightAction.IsReady())
            {
                npcCharacter.StartAction(flightAction);
            }
        }
    }
}
```

---

## API参考

### EquipmentAbilityAction 基类

#### 可重写的钩子方法

| 方法 | 调用时机 | 返回值 | 说明 |
|------|----------|--------|------|
| `OnAbilityStart()` | 能力启动时 | bool | 返回false阻止启动 |
| `OnAbilityUpdate(float deltaTime)` | 每帧更新 | void | 主要逻辑实现 |
| `OnAbilityStop()` | 能力停止时 | void | 清理逻辑 |
| `ShouldAutoConsumeStamina()` | 检查是否自动消耗体力 | bool | 默认true |
| `CanUseHandWhileActive()` | 检查是否可用手 | bool | 默认false |
| `OnStaminaDepleted()` | 体力耗尽时 | void | 默认停止能力 |
| `IsReadyInternal()` | 额外就绪检查 | bool | 默认true |

#### 可用的辅助方法

| 方法 | 说明 |
|------|------|
| `GetMovementInput()` | 获取WASD/方向键输入（归一化） |
| `IsOnGround()` | 检查角色是否在地面上 |
| `PauseGroundConstraint(float duration)` | 暂停地面约束 |
| `PlaySound(string soundKey)` | 播放音效 |
| `LogIfVerbose(string message)` | 输出日志 |

#### 可用的成员变量

| 变量 | 类型 | 说明 |
|------|------|------|
| `characterController` | CharacterMainControl | 角色控制器 |
| `Running` | bool | 动作是否正在运行 |
| `actionElapsedTime` | float | 动作已运行时间 |

### EquipmentAbilityManager 基类

#### 公开方法

| 方法 | 说明 |
|------|------|
| `RegisterAbility(CharacterMainControl character)` | 注册能力到角色 |
| `UnregisterAbility()` | 注销能力 |
| `TryExecuteAbility()` | 尝试执行能力 |
| `OnSceneChanged()` | 场景切换时调用 |
| `RebindToCharacter(CharacterMainControl character)` | 重新绑定到新角色 |

#### 公开属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsAbilityEnabled` | bool | 能力是否已启用 |
| `IsActionRunning` | bool | 能力动作是否正在运行 |

### EquipmentEffectManager 基类

#### 辅助方法

| 方法 | 说明 |
|------|------|
| `CheckItemByTypeId(Item item, int typeId)` | 通过TypeID匹配物品 |
| `CheckItemByName(Item item, string nameContains)` | 通过名称匹配物品 |
| `CheckItemByBoolFlag(Item item, string flagName)` | 通过Bool标记匹配物品 |

---