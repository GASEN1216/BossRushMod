只读反射本机安装的创意工坊主 DLL、Mod API 与 LiteNetLib 依赖，检查实际 API 签名、消息 payload 的 ReadOnlyMemory 转换、NetWriter 原始字节 framing，以及 Loot 注册/退订接口。

输出实际输入 DLL 的 SHA-256；不加载玩家存档、不启动 Unity 或游戏进程。此契约验证属于 L2，只证明当前安装版本可调用这些入口，不证明网络时序或游戏内行为。

运行 `python tools/run_runtime_regressions.py --filter CoopInstalledApi`。

2026-10-18：整类 Transport 新增丧尸共享掉落租约辅助方法，SceneLoadLease 的 Item 宿主替身按官方组件继承补齐 gameObject，用于编译整个生产类；本子组不执行拾取业务，拾取/合堆/原生登记删除的完整执行边界见 `ZombieCoopDropClaims`。

2026-10-15 C-01/C-03：完整生产 Transport 连续发送不同载荷，验证委托仅在绑定时构建、嵌套回调恢复外层载荷、发送异常清理和 Dispose；实际安装 API 的 BuildPayload 同步调用一次 writer 并保留精确字节。联网探测以可变反射对象覆盖缺失/重命名 singleton、角色与启动字段、getter 抛错和明确离线，角色无法判定时不能获得房主权限。C-02 另调用实际安装 ChatMessageRpc 的序列化器，并核对原生泛型 SendRpc 的 by-ref 签名；真正聊天发送走生产反射方法、以记录 sink 隔离原生网络。正常玩家存档与游戏进程均未访问，聊天实际可见性仍须 L3。

同一入口还真编译并执行整个生产 `BossRushCoopTransport.cs`，使用本机安装的 Harmony 验证已批准 Base 返回的租约。API 注册、角色、场景和异步 Closure 继续按钮为可变宿主边界；没有恒真授权替身。先证未经批准及新投票不能通行，再证等待超过 15 秒、活人/死者、房主先入 Base 的慢客仍能继续，最后覆盖取消、新目标、native Clear/Authorize、换房、换档、换 body、同图新 handle、场景卸载和 Dispose。新的全局 SceneGeneration 不代表本客已离开原场景，因此不单独撤销其授权。

另从实际工坊 DLL 读取 `SceneNet.AuthorizeLocalSceneLoad` / `IsLocalSceneLoadAllowed` 的原始 IL，原分支和字段算法原样执行；只将字段存储映射到隔离 SceneNet、原生时钟映射到可控时钟，`Mathf.Max(float,float)` 映射到相同的 `Math.Max`。整组生产断言再跑一遍。检查实际 `TryPerformSceneLoad_Local` IL 的授权、Proxy/Loader、launched 写入与 finally 契约，但其 Unity 世界调用在 L2 不执行。结果记录工坊 DLL、生产源及本次实际 TargetPath 的 SHA-256；真实票决、Closure 和新活体入图归 L3。

死客分支另执行安装 DLL 的完整 `RPCScene.HandleSceneBeginLoad` IL，包括 `IsServer` 早返、观战标志判断、清理 vote/ready、`EndSpectatorAndShowClosure(false)` 后提前返回及普通 `Client_OnBeginSceneLoad` 分支。仅映射本地字段存储、context 和 Unity 对象比较，Closure 展示与后续场景加载为可变宿主端点；没有用 `Approve` 替代该分支。覆盖前一地图留下 `launched=true`、本次没有任何 native Authorize、当前 host peer/service 身份、真实消息目标、普通投票无授权、中途取消/新投票/换角色，以及获准后换档/换房/换 host peer/场景卸载。这里只证明真实 native 控制流与生产租约接线；是否成功显示结算并加载新活体，仍须游戏内复验。

普通分支也执行实际 DLL 的完整 `Client_OnBeginSceneLoad` IL：复制消息目标、参数与锚点，原授权和 TryPerform 调用、投票/成员/ready 清理均保留，地图条目查找、传送及 Unity 场景加载为可变边界。覆盖死客获准后重复同 Base Begin、没有观战标志的首次普通 Begin、超时后继续及取消。两份 native handler 的隔离方法原始体都直接抛异常，缺少真实 IL 装配会失败；不再以简化 `Approve` 体替代普通 handler。该用例检查重复消息的兼容性，不声称实机曾出现重复 Begin。

`SpectatorRegistration` 子组逐字抽取生产 Transport 的原版 RPC 注册修复，链接实际安装的 COOP/Harmony。在独立非游戏进程中真正执行安装 DLL 的 `RpcRegistry.Initialize` / `GetDescriptor<SpectatorForceEndRpc>` / `Register<T>`，先复现初始化后仍缺 descriptor，再验证原处理器、ReliableOrdered/ServerToClient、幂等、重复 opcode 拒绝与冲突原子性。可失败的 registry 边界另覆盖非 missing 异常、初始化失败、null descriptor、未来版本已有回调和无此消息的旧版本；不以恒真替身宣称 native API 正确。

该子组还用真实 Harmony 完整执行已安装 `RPCScene.HandleSpectatorForceEnd` 的全部原 IL，只把 context/观战对象存储及 Closure 展示端点映射到可变宿主边界，保留 server 早返、null spectator 与 `force=true`。若未装配实际 handler IL，替身方法直接抛异常。原生 UI、网络传输、Unity 生命周期与真正全员死亡仍须 L3；此组不启动游戏、不读取玩家存档。所有编译和执行均使用新目录中的实际 exe，记录生产/原版/目标 SHA-256。

2026-10-15：`SceneIdentity` 子组链接整个生产身份服务，逐字抽取 Transport 的完整场景 bridge，以真实 Harmony 执行安装 DLL 的完整 Server_BroadcastBeginSceneLoad、Client_OnBeginSceneLoad、RPCScene.HandleSceneBeginLoad 与 RPCModApi.HandleModApiMessage IL（350/390/106/34 字节）。只映射 native 字段存储、发送和 Unity 加载等宿主边界，原分支、早返、Closure、参数与调用次序保留；未装配 IL 的替身直接抛错。57 条断言覆盖同名重载、漏 marker/重复 Begin、错误 sender/service、pre-pump 与 welcome 前 Begin、首载无 Core、错 loader 事件、Core 引用/handle、同房重放及退役房间、取消与嵌套新投票、仅首次稳定 Core 的 token 认领。安装 API/IL 另证明原生消息没有唯一 travel token、两种消息实际都走 ReliableOrdered channel 0。首个房间的 token/bootstrap、真正 Core 到达和原生异步 UI 仍须最终候选 L3；离线结果不能代替实机。
