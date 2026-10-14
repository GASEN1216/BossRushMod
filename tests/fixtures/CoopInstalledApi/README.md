只读反射本机安装的创意工坊主 DLL、Mod API 与 LiteNetLib 依赖，检查实际 API 签名、消息 payload 的 ReadOnlyMemory 转换、NetWriter 原始字节 framing，以及 Loot 注册/退订接口。

输出实际输入 DLL 的 SHA-256；不加载玩家存档、不启动 Unity 或游戏进程。此契约验证属于 L2，只证明当前安装版本可调用这些入口，不证明网络时序或游戏内行为。

运行 `python tools/run_runtime_regressions.py --filter CoopInstalledApi`。

同一入口还真编译并执行整个生产 `BossRushCoopTransport.cs`，使用本机安装的 Harmony 验证已批准 Base 返回的租约。API 注册、角色、场景和异步 Closure 继续按钮为可变宿主边界；没有恒真授权替身。先证未经批准及新投票不能通行，再证等待超过 15 秒、活人/死者、房主先入 Base 的慢客仍能继续，最后覆盖取消、新目标、native Clear/Authorize、换房、换档、换 body、同图新 handle、场景卸载和 Dispose。新的全局 SceneGeneration 不代表本客已离开原场景，因此不单独撤销其授权。

另从实际工坊 DLL 读取 `SceneNet.AuthorizeLocalSceneLoad` / `IsLocalSceneLoadAllowed` 的原始 IL，原分支和字段算法原样执行；只将字段存储映射到隔离 SceneNet、原生时钟映射到可控时钟，`Mathf.Max(float,float)` 映射到相同的 `Math.Max`。整组生产断言再跑一遍。检查实际 `TryPerformSceneLoad_Local` IL 的授权、Proxy/Loader、launched 写入与 finally 契约，但其 Unity 世界调用在 L2 不执行。结果记录工坊 DLL、生产源及本次实际 TargetPath 的 SHA-256；真实票决、Closure 和新活体入图归 L3。
