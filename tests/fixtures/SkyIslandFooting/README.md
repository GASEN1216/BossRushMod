# 天空岛落脚点与连捞恢复执行回归

从仓库根目录通过统一入口运行：

```powershell
py -3.13 tools/run_runtime_regressions.py --filter SkyIslandFooting
```

需要 Windows .NET Framework 与 .NET SDK；不需要游戏 DLL、Harmony 或 Pillow。
每次将当前 `SkyIsland/SkyIslandSessionFooting.cs` 按字节快照到独立输出目录，完整编译原样生产 partial。
`SkyIslandSession.cs` 的 `VerifyGround` 也按唯一相邻方法签名逐字提取；它用于旧版反例，不在测试里另写地面验证算法。
正式落脚选择、胶囊查询参数、同点连捞计数和第三次救援的 `SetPosition` 都执行生产方法。

11 项覆盖：标记高于 / 低于地面、实际地面附近障碍、出生点无地面 / 外部地面 / 被占用、有效出生点吸地、最近有效锚点、
外部地标地面、空锚点集合与第三次连捞传送。最近锚点用例还覆盖 null 和已销毁标记；连捞用例检查计数及救援后的计时复位。
宿主提供明确注入的碰撞层、射线返回值、低位障碍、父子关系、已销毁对象 null 语义及传送接收端。
`Destroy(GameObject)` 同时销毁本夹具唯一组件 Transform。替身不加载 Unity，不模拟完整 PhysX、真实角色位移或网格边缘支撑。
这是 L2 数据流回归，不能证明游戏内碰撞表现，也不启动游戏或访问玩家存档。

## 旧版反向验证

已确认缺陷的旧版基线为 `99a8796bc39e37b8f8d38056701101304e2d40dd`，它的预期结果为 11 项中 8 项失败，runner 返回非零。
仅通过 `git show` 读取旧版到本次独立输出目录，不替换工作区生产源码；失败正常上报，不把旧版失败转换成 PASS。
若浅克隆未包含该提交，Git 取源应失败，不能当成有效反向验证。

```powershell
$previous = $env:BOSSRUSH_SKY_FOOTING_BASELINE_REF
try {
    $env:BOSSRUSH_SKY_FOOTING_BASELINE_REF = '99a8796bc39e37b8f8d38056701101304e2d40dd'
    py -3.13 tools/run_runtime_regressions.py --filter SkyIslandFooting
    if ($LASTEXITCODE -eq 0) { throw '旧版反例意外通过，请检查回归是否失效' }
} finally {
    $env:BOSSRUSH_SKY_FOOTING_BASELINE_REF = $previous
}
```

随后在未设置基线变量时运行第一条命令，确认当前源码全部通过。
证据归档在 `Build/runtime-regressions/SkyIslandFooting/working-tree-*` 或 `baseline-*`，
每次保留生产与夹具源码快照、`sources.json` 的源码 SHA-256、`compile.rsp`、`compile.log`、`execution.log`、
含编译 / 执行返回码和本次二进制 SHA-256 的 `result.json`。聚合日志为 `Build/runtime-regressions/SkyIslandFooting.log`；
聚合 `results.json` 会被后续运行覆盖，逐次归档不覆盖。
