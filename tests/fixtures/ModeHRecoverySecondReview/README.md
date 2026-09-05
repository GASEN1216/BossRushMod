# Mode H 第二轮恢复回归

兼容分类：`COMPAT`。覆盖 CR-2026-09-05-011/012/015。

在仓库根目录执行：

```powershell
dotnet run --project tests/fixtures/ModeHRecoverySecondReview/Review.csproj --configuration Release
python tests/ModeHRecoverySecondReviewGuard.py
```

每次编译前从当前生产源码准确提取 21 个完整方法到 `Build/modeh-recovery-second-review/generated/`，同步记录 SHA-256；直接编译生产 DTO、配置、状态机、run owner 和确定性随机实现。没有拷贝重写奖励算法或清理流程。

夹具自己的 `Directory.Build.props` 在项目评估早期将 `bin/obj` 统一定向到 `Build/modeh-recovery-second-review/`，源码目录只保留可提交的测试输入，不修改仓库其它项目的构建设置。

33 条执行断言覆盖：恢复后的套装/名声点击、真实奖励应用与归档、过期 owner/场次/operation/生命周期、重复点击、缓存陈旧、归档写失败；弃赛写成功/失败/异常、未归还押品、仅有持久赛季、命令关闭和逆序释放、单段清理异常继续释放；两个独立进程及一万次新赛季 ID、旧 ID 恢复、seed 保留、跨季冠军插入与同季去重。

边界替身包括 Unity 协程/租约/UI、押品服务、磁盘写入及归档后的下一场路由。写失败测试仅证明 owner、租约和恢复控件保留；协调器原有 pending DTO 与重试语义由其它持久化夹具负责，不伪造已写盘。异常清理断言证明后续阶段仍尝试，不保证抛错的宿主 API 自身成功。真实观战输入还原、竞技场重开、六场赛季及游戏磁盘保存仍需实机验证。

结构 guard 另含 9 个反向变异；它不能代替上述执行断言或正式 Windows 双配置编译。
