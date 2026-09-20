"""Guard: 官方地图标记的场景参数必须走共享解析，不能直接传 Unity 场景名。

官方 `MiniMapDisplay.HandlePointOfInterest` 只绘制
`targetSceneIndex == MultiSceneCore.ActiveSubScene.Value.buildIndex` 的标记，其中 targetSceneIndex 取自
`SimplePointOfInterest.OverrideScene` = `SceneInfoCollection.GetBuildIndex(overrideSceneID)`。
这个参数要的是官方场景表里的**场景 ID**，不是 Unity 场景资源名。传错时 `GetBuildIndex` 返回 -1，
`OverrideScene` 退化成对象自己所在的场景，而 `SimplePointOfInterest.Create` 又会把对象搬进主场景，
于是和当前子场景永远对不上——地图上整条标记不显示，而且**没有任何报错**。

2026-09-20 从 `Duckov_Data/resources.assets` 实读官方 SceneInfoCollection 核对：多数条目的 id 与场景资源名
相同（零号区三条都是），但至少有三条不同，基地的子场景就在其中：

    id `Base_SceneV2_2`     -> Assets/Scenes/Base_SceneV2_Sub_01.unity
    id `Level_Factory_Main` -> Assets/Scenes/Factory/Factory_Main.unity
    id `Prepare`            -> Assets/Scenes/PREPARE.unity

所以「传场景名也能工作」只是碰巧，不是可以依赖的规律。本守卫要求每个
`SimplePointOfInterest.Create(` 调用点的场景参数都来自 `MapPointSceneResolver`（Utilities/ 的唯一实现），
并且不得出现新的私有副本。
"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
RESOLVER = Path("Utilities/MapPointSceneResolver.cs")
COMPILE = Path("compile_official.bat")

# 每个调用点：文件 -> 该文件里 Create( 的场景参数必须命中的写法
CALL_SITES = {
    Path("ModeF/ModeFExtraction.cs"): "MapPointSceneResolver.Resolve()",
    Path("ZombieMode/ZombieModeExtractionController.cs"): "MapPointSceneResolver.Resolve()",
    Path("DebugAndTools/SkyIsland/SkyIslandPreludeFlow.cs"): "MapPointSceneResolver.Resolve(GroundZeroScene)",
}

# 只允许 Utilities 里的那一份直接反查官方场景表；别处再写一遍就是第二份实现。
# （`MultiSceneCore.ActiveSubSceneID` 不列进来：撤离记录 EvacuationInfo 等地方也合法地用它。）
RESOLVER_INTERNALS = ("SceneInfoCollection.GetSceneID(",)

RE_CREATE = re.compile(r"SimplePointOfInterest\.Create\(\s*([^;]*?)\)\s*;", re.S)


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def check(sources):
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    resolver = sources[RESOLVER]
    require("internal static string Resolve()" in resolver,
            str(RESOLVER) + " 缺少共享解析入口 Resolve()")
    require("SceneInfoCollection.GetSceneID(active.buildIndex)" in resolver,
            str(RESOLVER) + " 没有按 buildIndex 反查官方场景 ID")
    require("MultiSceneCore.ActiveSubSceneID" in resolver,
            str(RESOLVER) + " 没有保留 ActiveSubSceneID 退路")
    require("Utilities\\MapPointSceneResolver.cs" in sources[COMPILE],
            str(COMPILE) + " 未登记 " + str(RESOLVER))

    for path, expected in CALL_SITES.items():
        body = strip_comments(sources[path])
        calls = RE_CREATE.findall(body)
        require(len(calls) >= 1, str(path) + " 找不到 SimplePointOfInterest.Create( 调用点（锚点失效）")
        require(expected in body,
                str(path) + " 的地图标记没有走共享场景解析（应出现 " + expected + "）")
        for call in calls:
            args = [a.strip() for a in call.split(",")]
            require(len(args) >= 2 and "SceneManager.GetActiveScene().name" not in args[1]
                    and ".name" not in args[1],
                    str(path) + " 把 Unity 场景名直接当官方场景 ID 传给了地图标记：" + call.strip()[:80])

        # 调用点自己不许再写一份解析
        for token in RESOLVER_INTERNALS:
            require(token not in body,
                    str(path) + " 重新实现了一份场景解析（" + token + "）：唯一实现在 " + str(RESOLVER))
    return errors


def main():
    paths = [RESOLVER, COMPILE] + list(CALL_SITES.keys())
    sources = {p: (ROOT / p).read_text(encoding="utf-8-sig") for p in paths}
    errors = check(sources)

    probes = (
        (Path("ModeF/ModeFExtraction.cs"), "MapPointSceneResolver.Resolve()",
         "UnityEngine.SceneManagement.SceneManager.GetActiveScene().name"),
        (Path("DebugAndTools/SkyIsland/SkyIslandPreludeFlow.cs"),
         "MapPointSceneResolver.Resolve(GroundZeroScene)", "SceneManager.GetActiveScene().name"),
        (Path("ZombieMode/ZombieModeExtractionController.cs"), "MapPointSceneResolver.Resolve()",
         "SceneManager.GetActiveScene().name"),
        (RESOLVER, "SceneInfoCollection.GetSceneID(active.buildIndex)", "null"),
    )
    for path, before, after in probes:
        if before not in sources[path]:
            errors.append("反向检查锚点失效：" + str(path) + " -> " + before)
            continue
        altered = dict(sources)
        altered[path] = altered[path].replace(before, after, 1)
        if not check(altered):
            errors.append("未拦截旧写法：" + str(path) + " -> " + before)

    if errors:
        print("MapPointSceneIdGuard: FAIL\n  " + "\n  ".join(errors))
        return 1
    print("MapPointSceneIdGuard: PASS（%d 个 Create 调用点走同一份场景解析；%d 个反向检查）"
          % (len(CALL_SITES), len(probes)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
