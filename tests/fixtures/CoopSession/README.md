# 房间生命周期执行回归

原版 ModApi 的 byte[] 长度实际为 UInt16；新分片用例链接完整 `BossRushCoopPacketFrames` 及真实 Runtime SendRaw / OnPacket，验证 60000、65535、65536 至原有 262144 字节边界，逆序、重复、校验损坏、未认证来源、旧房间、原生发送失败及清理。传输替身按已核实的原版 ushort 截断语义处理发送字节；这是 L2 受控 transport，实际安装 LiteNetLib 编解码与真实游戏传输证据另记，不称实机网络。

链接完整生产 `BossRushCoopRuntimeModule.cs` 和 `BossRushCoopProtocol.cs`，构建时从门面逐字抽取完整 `BossRushCoopPlayer` 声明并记录源码 SHA，经真实 `OnAwake`、`OnUpdate` 和入站消息分发验证：原版 Direct 未发断线事件时的成员退场、轮询前快速重连、稳定参与者恢复、客户端断线就绪撤销、切房清空旧 roster/场景代次、乱序 hello 不回滚资产、延迟 welcome 不回滚房间。

Unity、原生传输、角色和世界服务是受控替身；入站 payload 和 envelope 直接调用生产 BossRushCoopJson，链接共享解析器与转义工具，生产判据未被替换。此夹具不运行真实网络，也不证明实际 2–16 人可玩。真实 API 字段由 `CoopInstalledApi` 另行核对，真实投递仍须在游戏进程验收。

就绪流量用例通过 15 次实际入站 hello 建立 16 人生产参与者表，逐帧重复声明同一准备事实，验证立即发送量、全员屏障、客端周期重发和新代次同名屏障重报。周期重发只观察实际编码后的发送记录，不把它称为真实网络丢包恢复；实际同图重载和迟加入仍需独立场景及 L3 证据。旧逐帧实现会在发送量断言转红。

运行 `python tools/run_runtime_regressions.py --filter CoopSession`。聚合入口编译并执行新的 TargetPath，记录源码和产物 SHA-256。

存活用例通过实际 16 人名单编码、生产入站解析和真实 player.IsAlive 验证房主死亡事实覆盖健康代理、本机生命优先、未知成员、代理旧死亡状态、非房主/旧房间/旧代次/旧版本拒绝、资产更换、实际换体、同图新 Scene handle 在房主代次到达前隔离、断开与重连等待新事实。独立 wire 输入 DTO 只生成前向兼容字段，没有替换生产 roster 接收器。原无 alive 字段生产和旧 IsAlive 实跑失败；这证明 L2 行为，实际网络时延与游戏代理生命周期仍须 L3。

2026-10-15 复核补入实际慢客回城缺陷：房主先进入新代次时，本地仍停留旧 Core；随后客端真实换 handle 后，要先绑定匹配官方 sceneId 的新 Core，再由下一份完整 roster 恢复远端生命事实。覆盖双方先后顺序、同图重载、错误地图、初次迟加入、代次跳跃及乱序中间快照；准备事实同样不能从未绑定的 Core 发出，客先加载的新 key 在绑定后再报。旧实现实跑在 host-first 断言失败。Unity Object 替身模拟销毁后等于 null、GameObject 销毁连带已登记组件；新用例实际销毁旧身体再替换。连续错过多个同名重载与真实原生旅行标识的对应仍须独立验证，不由这些受控顺序证明。

资产准备屏障用例链接完整 Runtime/Protocol：资产 A→B→A 使用单调 assetRevision，房主按资产变化/同代次新连接分配 readyEpoch；welcome 必须响应当前资产与连接后的 hello。真实入站 ready 不能用旧资产、revision 或 epoch 准备新本人，客户端广播按严格递增 revision 完整替换同 key 成员，并核对每个成员的资产与 epoch。覆盖空快照撤回、重复/乱序旧广播、roster 断线/资产变化、旧 welcome、同 Core 重连沿新握手重新声明保留事实、本机切槽清旧 key，及房主无实际 Core 时的发送与两类查询拒绝。旧 wire 缺字段不填生产默认值；既有用例由明确的 Welcome/ReadyFor 调用在入站当刻构造合法测试身份，新行为另核实际房主发送的嵌套 DTO。此为 L2 受控网络/场景/槽号事件，不读取正常存档内容，不证明实机。

连续旅行续审现链接完整 `BossRushCoopSceneIdentity.cs`，补入真实生产会话与场景服务的联合执行：pending 31 不能绑定真实加载 token 32，正确 32 到达无需额外重载；公开 generation 仅在 Core/token 匹配后推进；旧中间代次不能把已加载 34 冒充 pending 35；原生 Begin 在换 Core 前立即使绑定失效；新房间原生加载先于 runtime 首次更新/welcome 时仍保留精确身份。加入错误首包 token，避免“丢掉原生记录后偶然采用首份 roster”也通过测试。房主同一 handle 从初始化到稳定时只推进一次。

此处替换的是 Unity 事件、加载完成和原生网络投递，不替换生产 token 匹配方法。Scene 帮手明确调用真实 StageMarker/ObserveNativeBegin 或 ApproveHost，并触发模拟的官方加载事件，销毁旧 Core/GameObject 后换新对象；测试 wire token 按指定 generation 生成。完整安装版 RPC/Send IL 与真实 Harmony 的观察顺序由 `CoopInstalledApi/SceneIdentity` 独立执行。这些联合用例仍是 L2，不能替代真实多进程旅行。

2026-10-15 C-02/C-03：完整 Runtime 执行原生联网但绑定失败、角色未知及旧房主状态残留；实际 facade 抽取并执行，确认世界权限关闭且正常离线恢复。原生网络状态、时间和发送器为可变边界。缺失 Mod、版本/资源不一致以具体 peer 名称向全员通知，客端在 welcome 前仅接收真实 host 身份的诊断，恢复后清除阻断。另核缺失 Mod 的原生聊天路由调用，不用 BossRush 消息代替缺失方的可达路径。正常命令/收据延迟后的重试由真实 Channel/PersonalDelivery 的 CoopProtocol 回归独立覆盖；此处没有游戏内聊天渲染或真实联网证据。
