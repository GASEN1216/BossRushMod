#!/usr/bin/env python3
"""
PetNestPersistenceGuard — 遗种巢存档管线守卫（实施计划 步骤 2）。

不变式：
- 三个 key 一律 `SavesSystem.Save<string>` JSON 整存，禁 typed `Save<T>`
  （ES3 会把 assembly-qualified 类型名写进存档，mod 重构即读不回来）；
- envelope 固定为 {schemaVersion, payload}，写入前打版本、读取时校验版本；
- schemaVersion 不符 -> 写屏障（只读不覆盖），不得静默按默认值覆盖玩家档；
- OnCollectSaveData / OnSetFile / OnSaveDeleted 幂等订阅且成对退订；
- 存档路径全 no-throw；
- 持久化层**不得**调用 SavesSystem.SaveFile（那是协调器的唯一职责）；
- 编解码手写、不用反射（字段名是存档契约，必须可 grep）；
- 解码后必须 Normalize()，容器不留 null；
- 配置侧：petNestEnabled 默认 false + 唯一 no-throw getter + ModConfig 镜像键。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import (  # noqa: E402
    read_petnest,
    read_text,
    repo_path,
    report,
    strip_cs_comments,
)

GUARD = "PetNestPersistenceGuard"


def check_persistence(errors):
    text = read_petnest("PetNestPersistence.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestPersistence.cs")
        return
    code = strip_cs_comments(text)

    # 1. 整存字符串，禁 typed Save<T>
    if "SavesSystem.Save<string>(" not in code:
        errors.append("[整存] 必须使用 SavesSystem.Save<string> 整存 JSON")
    if "SavesSystem.Load<string>(" not in code:
        errors.append("[整存] 必须使用 SavesSystem.Load<string> 读取")
    for m in re.finditer(r"SavesSystem\.(?:Save|Load)<(\w+)>", code):
        if m.group(1) != "string":
            errors.append("[整存] 禁止 typed 存档 API: SavesSystem.*<" + m.group(1) + ">")

    # 2. envelope 与版本校验
    if not re.search(r'Int\("schemaVersion", PetNestTuning\.CurrentSchemaVersion\)', code):
        errors.append("[envelope] 写入必须打 schemaVersion")
    if not re.search(r'GetInt\("schemaVersion", -1\)', code):
        errors.append("[envelope] 读取必须校验 schemaVersion")
    if not re.search(r'Raw\("payload"', code):
        errors.append("[envelope] payload 必须内联为 envelope 的成员")
    if not re.search(r'GetObject\("payload"\)', code):
        errors.append("[envelope] 读取必须从 envelope 取 payload")

    # 3. 版本不符 -> 写屏障
    barrier = re.search(r"if \(version != PetNestTuning\.CurrentSchemaVersion\)[\s\S]{0,600}?\n                \}", code)
    if barrier is None:
        errors.append("[写屏障] 缺少 schemaVersion 不符的处理分支")
    elif "_writeBarrier = true;" not in barrier.group(0):
        errors.append("[写屏障] schemaVersion 不符必须进入写屏障，不得覆盖玩家档")

    # 4. Store 必须被写屏障与故障标记挡住
    store = re.search(r"internal bool Store\(T value\)[\s\S]{0,900}?\n        \}", code)
    if store is None:
        errors.append("[写屏障] 缺少 Store(T value) 入口")
    else:
        body = store.group(0)
        if "if (_storeFaulted) return false;" not in body:
            errors.append("[写屏障] Store 必须被 StoreFaulted 挡住")
        if "if (HasWriteBarrier) return false;" not in body:
            errors.append("[写屏障] Store 必须被写屏障挡住")

    # 5. 幂等订阅 + 成对退订
    for evt in ["OnCollectSaveData", "OnSetFile", "OnSaveDeleted"]:
        if ("SavesSystem." + evt + " +=") not in code:
            errors.append("[订阅] 缺少订阅: SavesSystem." + evt)
        if ("SavesSystem." + evt + " -=") not in code:
            errors.append("[订阅] 缺少退订: SavesSystem." + evt)
    if not re.search(r"if \(_subscribed\) return;", code):
        errors.append("[订阅] EnsureSubscribed 缺少幂等早返")
    if not re.search(r"if \(!_subscribed\) return;", code):
        errors.append("[订阅] ShutdownSubscription 缺少幂等早返")

    # 6. 持久化层不得直接 SaveFile
    if "SavesSystem.SaveFile" in code:
        errors.append("[唯一写点] 持久化层不得调用 SavesSystem.SaveFile，必须走协调器")

    # 7. 回读核对
    if "readback" not in code:
        errors.append("[完整性] flush 后必须回读核对")

    # 8. 跨存档槽自校验：关开关会把 OnSetFile 一起退订，之后玩家在主菜单换档
    # 没人清缓存，重开开关时缓存仍是上一个档的数据，一写就把 A 档覆盖到 B 档。
    # 记槽位并在命中缓存 / flush 前自校验，无论订阅是否还在都安全。
    if "SavesSystem.CurrentSlot" not in code:
        errors.append("[跨档] 缓存必须记录所属存档槽（SavesSystem.CurrentSlot）")
    load = re.search(r"internal T LoadOrInit\(\)[\s\S]{0,4200}?\n            \}", code)
    if load is None:
        errors.append("[跨档] 无法解析 LoadOrInit")
    else:
        body = load.group(0)
        if "if (_cacheSlot == slot) return _cache;" not in body:
            errors.append("[跨档] 命中缓存前必须校验槽位，不一致时自失效重载")
        if "_cacheSlot = slot;" not in body:
            errors.append("[跨档] 加载后必须记下缓存所属的槽位")
    flush = re.search(r"internal bool FlushPending\(\)[\s\S]{0,2000}?\n        \}", code)
    if flush is None:
        errors.append("[跨档] 无法解析 FlushPending")
    elif "_cacheSlot != ReadCurrentSlotOrCached()" not in flush.group(0):
        errors.append("[跨档] pending 属于入队时的那个档，落盘前必须校验槽位，"
                      "否则官方采集会把旧档数据写进新档")


def check_codec(errors):
    text = read_petnest("PetNestPersistenceCodec.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestPersistenceCodec.cs")
        return
    code = strip_cs_comments(text)

    # 手写编解码，不用反射
    for forbidden in ["GetType()", "typeof(", "FieldInfo", "PropertyInfo", "System.Reflection"]:
        if forbidden in code:
            errors.append("[手写] 编解码不得使用反射: " + forbidden)

    # 三个 payload 的编解码齐全
    for name in ["EncodeNest", "DecodeNest", "EncodeExpedition", "DecodeExpedition",
                 "EncodeMuseum", "DecodeMuseum"]:
        if ("internal static " not in code) or (name not in code):
            errors.append("[编解码] 缺少入口: " + name)

    # 解码后 Normalize
    for decoder in ["DecodeNest", "DecodeExpedition", "DecodeMuseum"]:
        block = re.search(r"internal static \w+ " + decoder + r"\(PetNestJsonNode payload\)[\s\S]*?\n        \}", code)
        if block and "data.Normalize();" not in block.group(0):
            errors.append("[兜底] " + decoder + " 解码后必须 Normalize()")

    # 关键契约字段名必须出现（存档字段名冻结）
    for field in ['"deployedPetId"', '"soulLedger"', '"lineageKey"', '"riskTier"',
                  '"deathRate"', '"settled"', '"revealed"', '"memorials"']:
        if field not in code:
            errors.append("[字段冻结] 缺少存档字段: " + field)


def check_json(errors):
    text = read_petnest("PetNestJson.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestJson.cs")
        return
    code = strip_cs_comments(text)

    # 不 import ModeH 的 JsonValue
    if "ModeHJsonValue" in code:
        errors.append("[解耦] 不得复用 ModeH 的 JsonValue（两套系统不互为升级阻塞项）")
    # 文化无关的数字读写
    if "CultureInfo.InvariantCulture" not in code:
        errors.append("[本地化] 数字读写必须用 InvariantCulture")
    # 解析失败返回 null
    if not re.search(r"internal static PetNestJsonNode Parse\(string text\)", code):
        errors.append("[解析] 缺少 Parse(string) 入口")
    if "return null;" not in code:
        errors.append("[解析] 解析失败必须返回 null 供上层 fail-closed")
    # 深度上限，防恶意/损坏档爆栈
    if "MaxDepth" not in code:
        errors.append("[健壮性] 递归解析必须有深度上限")


def check_config(errors):
    # 开关接线拆在 Config/ConfigPetNest.cs（同一 partial class），只为单文件行数预算
    main_text = read_text(repo_path("Config", "Config.cs"))
    part_text = read_text(repo_path("Config", "ConfigPetNest.cs"))
    if main_text is None:
        errors.append("[File] 缺少 Config/Config.cs")
        return
    if part_text is None:
        errors.append("[File] 缺少 Config/ConfigPetNest.cs")
        return
    main_code = strip_cs_comments(main_text)
    code = strip_cs_comments(part_text)

    if not re.search(r"public bool petNestEnabled = false;", code):
        errors.append("[配置] 缺少 petNestEnabled 字段且默认必须为 false")
    if not re.search(r"internal bool IsPetNestConfiguredEnabled\(\)", code):
        errors.append("[配置] 缺少唯一 no-throw getter IsPetNestConfiguredEnabled()")
    getter = re.search(r"internal bool IsPetNestConfiguredEnabled\(\)[\s\S]{0,400}?\n        \}", code)
    if getter and "catch (Exception)" not in getter.group(0):
        errors.append("[配置] getter 必须 no-throw")

    # 唯一 getter：整个仓库只允许这一处定义
    if "IsPetNestConfiguredEnabled" in main_code:
        errors.append("[配置] IsPetNestConfiguredEnabled 只能定义在 Config/ConfigPetNest.cs")

    # 镜像键与三处接线
    if 'PetNestModConfigKeySuffix = "_PetNestEnabled"' not in code:
        errors.append("[配置] 缺少 ModConfig 镜像键常量 _PetNestEnabled")
    for call in ["LoadPetNestEnabledFromModConfig(boolLoadMethod)",
                 "TryLoadPetNestSingleModConfigValue(changedKey, loadMethod)",
                 "RegisterPetNestModConfigOption(addBoolMethod)"]:
        if call not in main_code:
            errors.append("[配置] Config.cs 缺少接线调用: " + call)
        if call.split("(")[0] not in code:
            errors.append("[配置] ConfigPetNest.cs 缺少实现: " + call.split("(")[0])


def main():
    errors = []
    check_persistence(errors)
    check_codec(errors)
    check_json(errors)
    check_config(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
