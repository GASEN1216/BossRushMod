# tests/AGENTS.md — 守卫、属性测试与执行回归

> 先读根目录 `AGENTS.md`。本目录有三类东西，都不能替代 Windows 编译和游戏内实机验证：
>
> - `tests/*Guard.py`：静态结构守卫，读源码文本断言不变式；
> - `tests/*PropertyTest.py`：离线属性测试，用真实数据复算几何、导航、落点、布局等；
> - `tests/fixtures/*/`：链接生产源码、用替身隔离宿主依赖的 C# 执行回归。

## 1. 运行守卫

```bash
python tools/run_guards.py                   # 全量；Windows 上 run_guards.bat 等价
python tools/run_guards.py --changed-only    # 只跑与当前 git 改动相关的（匹配不到时回退全量）
python tools/run_guards.py --filter ModeG    # 名字含 ModeG 的
python tools/run_guards.py --verbose         # 打印失败 guard 的完整输出
python tools/run_guards.py --list-red        # 只列当前失败项
python tests/SomeGuard.py                    # 单个
```

runner 全量跑不中断、聚合 PASS/FAIL、强制 UTF-8 输出。不要用 `for %f in (tests\*.py) do python %f`：它不聚合结果，
fail-fast 的写法还会让第一个红项遮蔽后面所有守卫。

- 已知红项登记在 `tests/known_red_guards.txt`：失败不计入退出码但会单独列出；登记后又转绿的报 STALE-BASELINE 并判失败，要及时移除。新写的守卫不进这里。
- CI（`.github/workflows/guards.yml`）跑 `--source-only`：依赖 local-only 制品（AssetBundle）的检查标 PARTIAL，清单见 runner 的 `EXTERNAL_ARTIFACT_GUARDS`。发布验证不要用这个参数。干净签出上缺少这些资源不算代码回归，不能据 PARTIAL 宣称发布资源通过。
- CI 不跑编译。只能在 Linux / WSL 跑时，写明「未做 Windows 编译验证」。

## 2. 执行回归

```bash
python tools/run_runtime_regressions.py --list
python tools/run_runtime_regressions.py --filter SkyIsland
python tools/run_runtime_regressions.py --jobs 1          # 串行排查
```

- 入口显式登记 `tests/fixtures/` 下的工程，聚合结果；日志与 `results.json` 写入 `Build/runtime-regressions/`。新增夹具要同步入口清单，并在夹具 README 里写清「真实生产逻辑」与「宿主替身」的边界。
- 需要本机 .NET SDK。依赖官方程序集的夹具这样找 DLL：`GAME_PATH`（游戏根目录；不设时读 Windows 编译生成的 `Build/BossRush.rsp`）；Harmony 类夹具另读 `BOSSRUSH_GAME_MANAGED`、`BOSSRUSH_HARMONY_DLL`。报「缺少官方 DLL」是环境没配好，不能据此宣称通过，也不算代码失败。
- 只用聚合入口跑，不要在夹具目录里直接 `dotnet run`：会留下 `bin/obj`。`tests/fixtures/Directory.Build.props` 已把夹具本地的 `obj/`、`bin/` 排除出编译（编辑器的设计时构建也会生成 `obj/Debug`，2026-09-25 曾让整组回归报 CS0579 特性重复）；自带 `Directory.Build.props` 的夹具（如 F3ValidationExecution）不继承它，仍要守这条。
- 中文断言名在输出里可能是乱码，按栈帧 `Program.cs:line N` 核对是哪一条。

Wiki 导航另跑 `npm --prefix wiki-site run test:navigation`，构建后的链接检查见 `wiki-site/AGENTS.md`。

## 3. 写守卫与属性测试

- 守卫直接放 `tests/*.py`，不要建 `tests/guards/` 子目录。一个守卫聚焦一条不变式，失败信息指出文件和缺失的模式。
- 改动被守卫断言的结构时同步守卫。**不要为了变绿删除或放宽断言**；白名单只解释既有债务，新代码不进白名单。
- 剥 C# 注释用 `tests/cs_source_util.py` 的 `clean_source()`，不要用正则（char 字面量 `'"'` 与逐字字符串会让正则失步，假绿假红都出过）。它会剥掉 `#if` 禁用区块：要检查「某写入只出现在 `#if BOSSRUSH_DEV` 里」就扫原文。
- 子串判断 `x in src` 挡不住注释掉和改名。先规范空白、切出方法体、按完整语句匹配；同一句在两处出现时钉住具体那一处。
- 钉**数值**，不只钉赋值语句（`Value = value;` 在位不代表价目表不是 0）。只断言方法定义存在不够，调用点单独断言。
- 文本守卫有结构性上限：防不住「保留 token、杀掉执行路径」（`if (false)` 包住、方法体首行 `return;`、挪进没人调的方法）。行为正确性交给执行回归或属性测试，文档里不要把守卫说成能防住一切。
- 结构守卫证明不了「玩家走过去有东西」「解析真的把两种形态都读出来了」：几何与可达性写属性测试，解析与状态机写执行回归。
- 属性测试在普通 Python 环境运行，不依赖游戏进程；尽量 import 生产侧已有的复算实现，不另写第二份。
- 判据在当前环境下恒真时记 SKIP，不记 PASS。

## 4. 反向验证

新增或修改守卫、属性测试后做反向验证：人为破坏 → 实跑确认转红、而且红在预期的那条断言上 → 按字节还原并核对 sha256。

- 先跑基线确认全绿；破坏用的锚点字符串必须恰好出现一次。
- 优先在稀疏签出副本上做，不在多个会话共用的工作区里留半截破坏。副本里跑执行回归同样要设 `GAME_PATH`；Git Bash 下 `MSYS_NO_PATHCONV=1` 时，clone 源路径写 `D:/...`。
- 避开**等价变异**：探针要打在判据真正依赖的那一行。没打到就如实记录、换探针，不要改断言求红。

## 5. 夹具替身

- 生产代码在运行时注入的字段，替身里**不要给非空默认值**，否则合同判据在夹具里永远红不了。夹具应在调用当刻记录真实装配状态，断言的是时序。
- 替身的 `UnityEngine.Object` 至少模拟两条语义：`==` / `!=` 把已销毁对象当 null；`Destroy(GameObject)` 连带销毁组件。过图必然发生的销毁写成必经步骤，而不是可选用例。

## 6. 语法探针

本机没装游戏时可以跑 `python tools/verify_syntax.py --with-bcl`（或 `verify_syntax.bat --with-bcl`）。它**不等于编译通过**：缺游戏程序集时 Roslyn 解析不出类型，就不分析迭代器方法体，CS16xx 一类错误根本不会产出。探针 PASS 只代表词法 / 语法层没问题，交付时写「语法通过，未正式编译」。

探针检查哪些文件，取决于它怎么读 `compile_official.bat` 的源码清单。**清单解析只有 `tools/compile_list.py` 一份实现**，`tools/verify_syntax.py`、`tests/OfficialCompileListFileExistenceGuard.py`、`tools/gameplay_coverage.py` 都从它取，新增消费者一律 import，不要再写第二套正则。理由是踩过的坑：2026-09-23 实测探针自带的 `echo(...)` 正则吃不下清单里残留的 `^` 续行写法（cmd 会把几行拼成一条 echo，csc 响应文件按空白切参数，所以正式构建照常编译），`SkyIslandJournal.cs` 因此从来没被离线语法检查过，而且探针既不报错也不显示 979 与 980 的差。现在探针在启动 csc 前核对「写进响应文件的集合 == 清单集合」，`tests/SyntaxProbeCompileListParityGuard.py` 再从外面钉同一条等式，并用 AST 挡住「探针重新长出自己的 `.cs` 正则」和「核对被摘掉」。
