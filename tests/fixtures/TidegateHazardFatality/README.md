直接链接生产 `TidegateHazards.cs` 和 `BossLethalHealthProtectionPatch.cs`，使用本机游戏实际安装的 Harmony，在隔离进程运行逐字提取的官方 `Health.Hurt`、`CurrentHealth`、`SetInvincible`、`DamageInfo.GenerateDescription`。提取生产会话已有的返回、就绪、暂停和危险区分发语句，避免重写成测试自己的开关。每次在独立输出目录编译，清单记录输入 SHA-256。

分别击中泄洪口/吊货，叠加无敌、逆鳞和零物理受伤系数；断言官方死亡/广播恰一次，两种死因在离开作用域后仍可显示。主动在官方 `OnHealthChange` 回调里对同一角色和另一个角色嵌套普通伤害，验证只锁命中者、普通保命有效。另测无命中、暂停、F3、返回、未就绪、关闭、加载、基地、错误主角与抛异常后无敌和作用域恢复。

两个设施的命中结果、装备的保护状态和 Unity 事件宿主为替身；不证明危险区空间判定、实机图层或真实死亡存档。运行 `python tools/run_runtime_regressions.py --filter TidegateHazardFatality`；只使用反编译代码及已安装的 Harmony，不启动游戏、不读写玩家档。
