通过 `python tools/run_runtime_regressions.py --filter AuditCoreParsing` 执行。

链接完整的生产地图和共享 JSON 代码，逐字抽取黑名单解析入口，验证错误类型、空/缺分量、浮点溢出、合法原点及默认回退。Unity Vector3、日志和中英文选择为替身；不启动游戏、不读取玩家存档，证据为 L2。

2026-11-19：`MapSpawnPointRegistry` 的标量字段改从解析树取。`run.py` 用 Python json 独立读出 `Assets/SpawnPoints/*.json` 每个文件的 sceneName / sceneID / 显示名 / 预览图 / sortOrder / beaconIndex（口径：字符串缺失、null 或非字符串为空串，预览图为 null；整数缺失或非数字取默认），写进 `spawn-scalars.tsv`；C# 侧对每个文件跑生产 `DeserializeFromJson` 逐字段比对，并验证只认顶层键、按标准 JSON 解转义。

2026-10-25：补充 Int64 上边界、下边界、临界可表示 double、非有限构造 token 与既有四舍五入语义。旧生产代码在 JSON `9223372036854775808` 的 fallback 断言实跑失败；修复后保留精确整数 `long.MaxValue`，只拒绝越界浮点转换。
