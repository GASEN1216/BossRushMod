# GardenHarvestNotice

运行 `python tools/run_runtime_regressions.py --filter GardenHarvestNotice`。需要 .NET SDK 和本机 Harmony；优先读 `BOSSRUSH_HARMONY_DLL`，未设置时从正式编译的 `Build/BossRush.rsp` 读取其实际引用路径。只有 .NET 10 运行时时可设置 `DOTNET_ROLL_FORWARD=Major`。产物和源码哈希写入 `Build/garden-harvest-notice/`。

真实生产逻辑：完整链接 `Integration/BackMountain/GardenHarvestNoticePatch.cs`，执行 Transpiler、Observe 与异步完成检查。合成 IL 使用安装的 Harmony `CodeInstruction`、真实反射 `MethodInfo` 与 `OpCodes`，核对官方发货调用和 Forget 的保留、唯一包装、NOP 兼容以及失配拒绝；不对游戏程序集做 detour。

宿主替身：UniTask 用 .NET Task 的异步 builder 表达完成、挂起、异常与取消；Unity 对象模拟销毁后等于 null，销毁 GameObject 同时销毁组件。Crop 在销毁后拒绝读取 Info，确认提示使用事先捕获的产物数据。角色、宿主、场景、存档槽、语言、物品元数据与横幅出口均由内存替身提供，不读取或修改玩家数据。

覆盖：官方与 Mod 号段；同步与异步成功；未完成不提示；原发货异常/取消继续可观察；缺 owner/主角、模块停用、无效快照仍透传原任务；正常作物销毁不丢提示；换槽、过图、换主角、卸载或停用抑制迟发；连续同作物每次一条；完成时解析当前语言；仓库与自提点去向说明；取名和横幅异常不改变交付结果。

反向验证：`python tests/fixtures/GardenHarvestNotice/negative_probes.py` 在 `Build/ghn-negative/` 的稀疏副本中分别制造提前提示、吞发货异常、缺换槽门、缺切图门、遗漏观察调用和接受多个发货调用，仍通过聚合入口执行夹具，要求红在预期断言；最后按字节还原并比对 SHA-256。记录在 `Build/garden-harvest-notice/negative-probes.json`，不在共享工作区修改生产文件。

边界：这套回归不执行官方 Cost.Return 或 PlayerStorage.Push，不证明实际入库数量、Unity 主线程调度、Harmony 在真实 Mono 进程安装成功或横幅屏幕效果。代码认为官方交付任务正常完成后才允许提示；真实收获数量与视觉仍需 L3。

2026-09-25：增加后山产物优先背包的真实 Return 参数布局检查、官方作物路线不变、未注册产物在 Harvest 前保留成熟作物。原发货仍只调用一次；不模拟实际库存容积或资源加载。
