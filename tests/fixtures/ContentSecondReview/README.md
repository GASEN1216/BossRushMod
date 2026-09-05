# 永久 NPC 后继请求与展示柜生命采样回归

兼容分类：`COMPAT`。对应 CR-2026-09-05-018 / -019。

运行：`python tests/fixtures/ContentSecondReview/run.py --negative-probes`。

脚本每次从当前生产文件提取完整方法及 NPC 请求字段，不复制修复算法；仅将异步返回类型从 UniTask 改为 Task。生成的 C#、.NET 8 编译产物、源文件 SHA-256 与日志均写入 `Build/content-second-review-fixture/`。两次负探针只改隔离副本，分别恢复 busy 时丢请求、摘除 modifier 后才采样的错误顺序，必须因对应行为断言失败。

覆盖连续 A→B→C（只创建 A 与最终 C）、同名不同 handle、没有 Destroy 回调的切场、Destroy 后无继任、重复请求、旧 finally 交接 busy、创建与登记异常、销毁/替换 owner、静态世代失效、婚姻变化与登记竞争。展示柜覆盖首次入场满血、已有加成满血、低于基础上限的受伤、处于基础与旧加成上限之间的受伤，以及重复刷新、空收藏、锁定、缺玩家/Health、读取和挂 modifier 失败。

宿主替身的约束：任务完成在同一线程内推进；只模拟 Unity 被销毁 owner 的空比较、场景 identity、角色登记与销毁、官方 Health 的实时最大值读取和独立当前血量。替身不验证 Unity PlayerLoop、捏脸/导航/交互组件、真实 modifier 事件与生命 UI。正式游戏程序集编译及游戏内连续切图、教堂/跟随回归、满血与受伤登记仍须另行验证。
