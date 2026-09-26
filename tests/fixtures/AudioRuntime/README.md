# AudioRuntime 执行回归

运行 `python tools/run_runtime_regressions.py --filter AudioRuntime`。构建、临时音频文件与记录只写 `Build/runtime-regressions/AudioRuntime/`，不启动游戏、不读取玩家存档。

夹具直接编译完整 `Audio/BossRushAudioRuntimeService.cs` 与兼容桥 `Audio/BossRushAudioHooks.cs`。从当前生产源抽取根实例字段、注册入口最前的查询绑定、RandomEvents 与 Arena 缓存桥，以及 AlwaysOn 卸载中 MagicBlend → 音频清缓存 → NPC/Dialogue 清缓存的实际语句。输入文件 SHA-256 记录在输出目录。

真实执行的逻辑包括按需读取、Assets/root 下 ngm 的选择、普通音效的大小写无关正负文件缓存、委托与反射 fallback、静态共享下蛋配置、玩家 fallback、生成/碰撞/Init 顺序、原参数与异常边界。文件存在检查使用真实临时文件。三个独立进程分别覆盖官方非 void 返回形态、兼容 void 委托形态和 AudioManager 不存在的形态。

Unity 对象、Resources、Instantiate、Physics 与官方音频播放是记录替身。替身模拟 destroyed-null 及销毁 GameObject 连带销毁组件，过图销毁是必经执行用例。生产注入查询在替身中没有非空默认值，使用实际根绑定代码装配。官方音频的非 void 返回只模拟委托兼容性，不执行 FMOD；蛋孵化、实际音量/听感、真实资源扫描与游戏完整卸载仍需 L3。

人工验收 `AUDIO-EGG-01`：在竞技场初始入口路牌选择“哎哟~你干嘛~”，按画面提示的交互键完成一次，再重复一次。观察玩家脚下蛋的位置、原预设延时后的孵化与 ngm 声音；无蛋、每次多出蛋或重复音效、位置与朝向异常为不合格。`AUDIO-EGG-02`：正常离开再回到竞技场，重复相同步骤；旧场景引用导致无蛋或异常为不合格。巡游鸭清怪豁免另随 RandomEvents/Arena 的现有实机用例核验。本次未执行这些 L3 步骤。
