# PermanentDuckNpcDialogue — 永久捏脸 NPC 台词的中英对照回归

对应 `CODE_REVIEW_FINDINGS.md` 的 `CR-2026-09-12-016`（可玩性评估 R-6）。

## 为什么要有这份

R-6：晴禾与苇白的 46 条好感 / 婚姻台词此前只有中文，整份 `DuckNpcs.json` schema 里唯一的
英文字段是 `displayNameEn`——英文玩家和她们聊天、送礼、婚后对白读到的全是中文。
修法是 `SCHEMA+`：台词的每一句既可以是裸字符串（老写法），也可以是 `{"cn": ..., "en": ...}`。

**为什么不能只靠结构守卫**：`DuckNpcInvariantGuard` 能证明「代码里有 `ReadLine` 这几个 token」，
证明不了解析真的把两种形态都读出来了。而这一层恰恰最容易静默坏掉——
档位判据写错会让**整组台词丢失**：不编译报错、不抛异常，只表现为「这个 NPC 突然不说话了」。
所以另做一份执行回归，链接真的 `PermanentDuckNpcData` 与 `BossRushJsonValue`，
只替身 `L10n` / `ModBehaviour.DevLog` / `UnityEngine.Random` 三处。

## 覆盖什么（162 条断言）

1. **老写法行为一个字不变**：裸中文字符串在中英两种语言下都原样读出（`duck_npc_xiaoman`
   这类未译蓝图不能因为 schema 扩展而读成空串）。
2. 新写法 `{cn, en}` 在中英下各取对的那一半。
3. 两种形态**混在同一个数组里**也都读得出来。
4. 单档写成 `[{cn, en}]` 时整组不得丢失——这是 `IsTierObject` 按 `lines` 键判断（而不是只看
   `Kind`）的存在理由，也是这次最容易踩的坑。
5. 好感度分档、婚后台词、气泡三条路径都吃这套对照。
6. 缺 `en` 时回落中文，而不是显示空串。
7. 语言在**取用时**解析：切语言之后同一份数据读出另一种语言（气泡的按语言缓存必须失效重建）。
8. 真实 `Assets/Data/DuckNpcs.json` 里两位天空岛居民的每一条台词中英都非空、且**互不相同**
   （相同就说明那一句其实没译，只是把中文抄了一遍）。

## 跑法

```bash
python tools/run_runtime_regressions.py --filter PermanentDuckNpc
```

工作目录必须是仓库根：第 8 组断言读真实的 `Assets/Data/DuckNpcs.json`。

## 它不证明什么

不证明译文读起来通顺、不出框、语气对——那要切英文进游戏看
（`GameplayCoverage.json` 的 `M_SKY_ISLAND_09`，本轮追加「找晴禾 / 苇白聊天、送礼、婚后对白」）。
