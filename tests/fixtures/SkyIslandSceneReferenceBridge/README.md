# 天空岛场景桥接与黑幕恢复回归

入口：`python tools/run_runtime_regressions.py --filter SkyIslandSceneReferenceBridge`，设置现装 `BOSSRUSH_HARMONY_DLL`。Windows .NET Framework 执行真实 Harmony，编译完整生产桥接文件，不启动游戏或访问玩家存档。

Unity、场景与 UniTask 调度是隔离替身；加载器保留现装宿主的两对 BlackScreen Show / Hide、四处 NextFrame、LevelInited 查询和目标 AsyncOperation。黑幕替身保留引用计数，旧实现会在取消后留下未配对的一层。测试涵盖本次加载失败后的补偿、其他 owner 的黑幕保留、scene 绑定前取消、早期读取循环的取消与激活释放、operation 完成前不得归还资源、最终黑幕结束前不得卸载桥接。现装 DLL 的对应结构由 `SkyIslandOfficialContract` 元数据回归验证。

这是 L2 逻辑与 Harmony 接点证据；真实画面、Unity 原生场景队列与玩家输入仍须实机验证。

2026-10-06 独立深审新增：加载替身按官方顺序实际设置两次 allowSceneActivation，覆盖目标已到 0.9、跳过全部早期 NextFrame、第二次黑幕后事件抛错。生产桥必须在首次关闭激活时登记 operation，在任务异常结束后放行原生队列，且入岛与返航均保留 pending owner。失败返航重试只抑制目标场景的新角色保存，原岛上角色与死亡保存保持原生路径。现装 DLL 的两个 setter 调用由 OfficialContract 校验。
