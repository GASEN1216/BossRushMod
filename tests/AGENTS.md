# tests/AGENTS.md — Python 守卫专项规则

> 先读根目录 `AGENTS.md`。本目录的 Python 脚本是静态守卫，不是 C# 单元测试。

## 规则

- 守卫脚本直接位于 `tests/*.py`，不要新建 `tests/guards/` 子目录。
- 改动被 guard 断言的结构时同步 guard，不要删除断言逃避失败。
- 新 guard 应聚焦一个明确 invariant，失败信息要指出文件和缺失模式。
- 白名单只能解释既有债务；新增代码默认不进白名单。
- 属性/随机测试脚本也应能在普通 Python 环境运行，不依赖游戏进程。

## 运行

全量（推荐入口）：

```bash
python tools/run_guards.py
```

Windows 上等价：`run_guards.bat`。

这个 runner 全量跑不中断、聚合 PASS/FAIL、打印失败清单与耗时，并强制 UTF-8 输出。
**不要再用 `for %f in (tests\*.py) do python %f`**：那个写法不聚合结果，而且仓库里存在
既有红项时会让人误以为「跑过了」；fail-fast 的循环更糟——第一个红项会永久遮蔽它之后的
所有 guard（既有红项按字母序排在 D，后面还有 300+ 个从来没被跑到）。

常用参数：

```bash
python tools/run_guards.py --changed-only   # 只跑与当前 git 改动相关的 guard
python tools/run_guards.py --filter ModeG   # 只跑名字含 ModeG 的
python tools/run_guards.py --verbose        # 打印失败 guard 的输出
```

单个：

```bash
python tests/SomeGuard.py
```

已知红项登记在 `tests/known_red_guards.txt`，失败不计入退出码但会单独列出；
登记后又转绿的条目会被报成 STALE-BASELINE 并判失败，必须及时从基线移除。

CI（`.github/workflows/guards.yml`）跑的就是这个 runner。CI **不跑编译**——
`compile_official.bat` 需要游戏程序集，只能在装有《鸭科夫》的 Windows 机器上跑。

如果只能在 Linux/WSL 跑，需要说明这不是 Windows 编译验证。

## 语法探针

本机没装游戏时，可以用 Roslyn 做语法层检查（**不等于编译通过**）：

```bash
python tools/verify_syntax.py --with-bcl
```

它只抓 CS1xxx 词法/语法错误；类型不存在、签名不匹配、重载歧义必须真编译才能发现。
