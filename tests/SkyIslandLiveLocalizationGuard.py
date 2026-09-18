"""天空岛语言刷新入口接线；文字/缓存行为由 SkyIslandInteraction 执行真实方法。

这里只守不能在纯 UI 替身中执行的生命周期接线，不把结构断言当成运行时证明。
反向验证须分别移除招牌推进、居民推进、出生后的现取姓名、稳定 key 注入。
"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
SKY = ROOT / "DebugAndTools/SkyIsland"
errors = []


def read(name):
    return clean_source((SKY / name).read_text(encoding="utf-8-sig"))


def body(source, signature):
    hits = list(re.finditer(r"^([ \t]*)" + re.escape(signature), source, re.M))
    if len(hits) != 1:
        errors.append("成员锚点必须唯一: " + signature)
        return ""
    hit = hits[0]
    opening = source.index("{", hit.end())
    end = re.search(r"^" + re.escape(hit.group(1)) + r"\}", source[opening:], re.M)
    if end is None:
        errors.append("成员未闭合: " + signature)
        return ""
    return re.sub(r"\s+", " ", source[opening:opening + end.end()])


def need(ok, message):
    if not ok:
        errors.append(message)


runtime = read("SkyIslandRuntimeModule.cs")
residents = read("SkyIslandResidents.cs")
prelude = read("SkyIslandPreludeFlow.cs")
late = body(runtime, "public override void OnLateUpdate()")
create = body(runtime, "private void CreateSign(")
clear = body(runtime, "private void ClearEntry()")
need("RefreshSignText();" in late, "已有船点的 LateUpdate 必须刷新招牌语言")
need("signText = text; RefreshSignText();" in create, "招牌创建必须绑定原 TMP 并立即取当前语言")
need("signText = null;" in clear and "signChinese = null;" in clear, "清理必须释放招牌引用和语言缓存")
tick = body(residents, "internal void Tick(")
need("RefreshLocalizedNames();" in tick and tick.index("RefreshLocalizedNames();") < tick.find("bool busy"),
     "居民语言刷新必须接在 Tick 的暂停/气泡冷却门前")
spawn = body(residents, "private async UniTask SpawnOneAsync(")
fresh = "displayName = SkyIslandWorldStory.ResidentName(id);"
need(fresh in spawn and spawn.index(fresh) > spawn.find("await DuckNpcSpawner.SpawnAsync(")
     and spawn.index(fresh) < spawn.find("NPCNameTagHelper.RegisterOriginalHealthBarName("),
     "异步生成落地后、登记前必须现取姓名")
inject = body(prelude, "internal static void InjectLocalizations()")
for name in ("DepartureNameKey", "ObjectiveNameKey", "InstrumentNameKey"):
    need("LocalizationHelper.InjectLocalization(" + name + "," in inject, name + " 必须接入语言注入链")
need("SimplePointOfInterest.Create(position, GroundZeroScene, ObjectiveNameKey, null, false);" in
     body(prelude, "private void CreateMapMarker("), "仪器地图标记必须使用会刷新语言的稳定 key")
need("return SkyIslandPreludeFlow.DepartureNameKey;" in runtime, "船点交互须使用统一稳定 key")
need("return SkyIslandPreludeFlow.InstrumentNameKey;" in prelude, "仪器交互须使用统一稳定 key")
integration = clean_source((ROOT / "Integration/BossRushIntegration_StartAndScene.cs").read_text(encoding="utf-8-sig"))
need("SkyIslandPreludeFlow.InjectLocalizations();" in integration, "统一语言注入链必须调用序章入口")
print("SkyIslandLiveLocalizationGuard: " + ("FAIL\n" + "\n".join(errors) if errors else "PASS"))
raise SystemExit(bool(errors))
