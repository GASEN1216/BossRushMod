# 怪件快递执行回归

入口：`python tools/run_runtime_regressions.py --filter FieldAdventureDeliveryRuntime`。反向验证：`python tests/fixtures/FieldAdventureDeliveryRuntime/run.py --negative-probes`。

完整执行生产快递 owner、交互体、规则与共享建筑基类；异步方法只适配返回载体，Forget 仍非阻塞地等待任务。叫声创建、播放、音量桥接和喷发方法从当前生产文件逐字提取。官方 CA_Interact、CharacterActionBase、InteractHUD、InteractSelectionHUD 完整执行；角色朝向、交互组、名称查询与 InteractableBase 的 StartInteract、UpdateInteract、CheckInteractable、FinishInteract、StopInteract 等动作方法从当前官方源码逐字提取，F 入口使用真实 StartActionByCharacter / UpdateAction / StopAction。反编译 HUD 的 ref 输出局部变量补默认初始化，以通过 C# 的明确赋值检查。源文件 SHA-256、每次全新构建的 TargetPath 与 SHA-256、执行日志写到 Build/runtime-regressions/FieldAdventureDeliveryRuntime。

覆盖交互完成入口的抱起/放下、全方向站立/移动与瞬时转身、持续同一目标的压力/完整度与喷发、组选中保留及语言切换、主/音效音量和 Mute、运行中与暂停设置更新、独立 AI 声源、过图销毁后的对象失效。

CourierAvailability.cs 专门覆盖阿稳子项的可用性：首次可接、接单退组、首单取消/签收后恢复、第二单与额度耗尽；菜单持续显示及离开后重新靠近；晚挂入当前主目标；移除前项时按对象保留其它服务、移除选中项后收敛到阿稳主项、选中事件次数与下一帧 HUD 索引缓存；隐藏 HUD 祖先期间首次挂入、退组、恢复及重显；真实 Running / InputActived 门隐藏与重新打开菜单后的正常选中事件；暂停期间死亡、重复清理、未完成对话跨 generation 返回。未完成对话用 TaskCompletionSource 控制返回时刻，走完整生产 ShowChoices 的 finally；旧任务在新 generation 已打开新对话后才完成，检查旧选择与 finally 都不能修改新 owner。

反向验证只修改隔离副本：恢复旧携带几何与旧持续挂组行为，杀掉 HUD 内容刷新、按对象恢复选择、保留选中事件抑制、索引缓存、对话 finally 同步、owner 退组、两单门、隐藏 HUD 查询、隐藏选择保留与两处 generation 守门，再检查旧有文字和音量桥接。每项要求在预期行为断言转红，不能由编译错误充当命中；隔离源码按基线原字节还原并记录 SHA-256，共享生产输入按 SHA-256 核对未变。可用 `--evidence-dir <目录>` 将本轮独立日志留在指定 Build 目录。

替身边界：Unity 对象、球体碰撞和 UI 池、声音播放、对话后端、地面查询、经济、角色/关卡与 AI 听觉后端均为确定性内存替身；官方交互体的 Awake / Start、必需物品分支与进度/图标查询仍为替身。Unity 对象实现销毁假 null、根物体连带组件销毁与含 inactive 的子树查询，HUD 挂在 LevelManager / HUDCanvas 父链内，与已核实的本机资源归属一致。UI 池按官方 PrefabPool 的释放语义停用旧行并留在父树，按 LIFO 复用，缩短列表时保留 inactive 旧行；断言检查行对象确实复用、inactive 余行确实存在，选中事件以总调用次数核对。外观模型和渲染不在本夹具范围，另由 FieldAdventureVisuals 真实 Unity 夹具覆盖。本夹具不证明游戏内 PhysX、地形、屏幕排版或真实声音效果，也不启动游戏或读写玩家存档。
