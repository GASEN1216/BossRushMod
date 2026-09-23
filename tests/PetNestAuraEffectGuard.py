"""遗种巢崽身上的炫彩 / 异色特效：映射、门控、预算、资源生命周期与材质口径。

owner 2026-09-22 实测第 9 条「异色太廉价」、第 12 条「不同炫彩弄不同的粒子特效，异色最豪华」
之后整套重做（PetNest/PetNestAuraEffect.cs + PetNestAuraRecipes.cs + PetNestAuraTextures.cs）。
本守卫钉住重做后的结构不变式：

1. 调色板里每一种颜色都**显式**映射到一个专属元素（不许靠 default 兜底），十色互不相同；
   每个元素在主元素 switch 里有分支（Radiant 走 default），点缀 switch 里同样有分支。
2. 普通崽零对象：AttachChromaAura 里「既不异色也不炫彩就 return」出现在 Attach 调用之前；
   Detach 调 Dispose；CleanupOnce 先 Detach 再销毁角色。
3. 发射只走粒子系统自带的固定速率 / burst：特效文件里没有 LateUpdate、没有 .Emit(，
   也不再继承 RingParticleEffect（旧配方每帧手撒、密度随帧率走）。
4. 预算钉数值：ChromaParticleBudget <= 60、ShinyParticleBudget <= 120；
   maxParticles 只在两个经 TakeBudget 扣预算的工厂里赋值；
   按配方里的 Scaled(N, f) / 字面上限复算：任一元素主层 + 点缀 <= 炫彩预算，
   异色本体 + 0.6 强度的最重炫彩组合 <= 异色预算（否则运行时会被预算静默截掉最后的点缀）。
5. 异色一整套都在：两圈符文环、金星、星芒、光冠、金尘、入场绽放、点光。
6. 资源生命周期：new Material / new Texture2D / new Mesh 全部直接包在 Own( 里；
   OnDestroy 遍历 _ownedAssets 逐个 Destroy；特效文件里没有静态 Material / Texture / Mesh 字段。
7. 材质只从共享模板派生（RingParticleEffect.GetSharedParticleMaterial()），特效文件里没有 Shader.Find(。
8. 特效根挂在角色根下；只有异色（符文环 / 点光）才启用 Update。

文本守卫的上限：挡不住「保留 token、废掉执行路径」。粒子到底好不好看、HDR 是否出光，
只能 owner 实机看（清单见交付报告）。
"""
from pathlib import Path
import math
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
EFFECT = ROOT / "PetNest/PetNestAuraEffect.cs"
RECIPES = ROOT / "PetNest/PetNestAuraRecipes.cs"
TEXTURES = ROOT / "PetNest/PetNestAuraTextures.cs"
CHROMA = ROOT / "PetNest/PetNestChroma.cs"
SPAWNER = ROOT / "PetNest/PetNestCompanionSpawner.cs"

CHROMA_BUDGET_MAX = 60
SHINY_BUDGET_MAX = 120

# 元素 → 主层配方方法（多个时取上限最大的那条路径：赤色有官方克隆与程序化兜底两条）
ELEMENT_BUILDERS = {
    "Flame": ["TryCloneOfficialFire", "BuildFlameFallback"],
    "Forge": ["BuildForge"],
    "Thunder": ["BuildThunder"],
    "Verdant": ["BuildVerdant"],
    "Frost": ["BuildFrost"],
    "Tide": ["BuildTide"],
    "Arcane": ["BuildArcane"],
    "Radiant": ["BuildRadiant"],
    "Umbra": ["BuildUmbra"],
    "Glimmer": ["BuildGlimmer"],
}
SHINY_PARTS = ["BuildShinyHalo", "BuildShinyStars", "BuildShinyGlints", "BuildShinyCrown",
               "BuildShinyDust", "BuildShinyFlourish", "BuildShinyLight"]


def norm(text):
    return re.sub(r"\s+", " ", text)


def method_body(code, name):
    """按名字切出方法体（含外层花括号）；找不到返回 None。只认声明，不认调用。"""
    pattern = re.compile(
        r"(?:private|internal|public|protected)\s+(?:static\s+)?[\w<>\[\],\s]+?\b"
        + re.escape(name) + r"\s*(?:<[^>]*>)?\s*\([^)]*\)\s*(?:where[^{;]*)?\{")
    m = pattern.search(code)
    if not m:
        return None
    start = m.end() - 1
    depth = 0
    for i in range(start, len(code)):
        ch = code[i]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return code[start:i + 1]
    return None


def switch_cases(body):
    """switch 里出现过的 case 元素名。"""
    return set(re.findall(r"case\s+PetNestAuraElement\.(\w+)\s*:", body))


def caps_in(body):
    """配方体里声明的发射器上限：Scaled(N, f) 取 N（f=1 时的上限）。"""
    return [int(n) for n in re.findall(r"Scaled\(\s*(\d+)\s*,\s*f\s*\)", body)]


def main():
    errors = []
    for path in (EFFECT, RECIPES, TEXTURES, CHROMA, SPAWNER):
        if not path.exists():
            print("PetNestAuraEffectGuard: FAIL - missing source " + path.as_posix())
            return 1

    effect_raw = EFFECT.read_text(encoding="utf-8-sig")
    recipes_raw = RECIPES.read_text(encoding="utf-8-sig")
    textures_raw = TEXTURES.read_text(encoding="utf-8-sig")
    effect = clean_source(effect_raw)
    recipes = clean_source(recipes_raw)
    textures = clean_source(textures_raw)
    spawner = clean_source(SPAWNER.read_text(encoding="utf-8-sig"))
    chroma = clean_source(CHROMA.read_text(encoding="utf-8-sig"))
    fx_all = effect + "\n" + recipes + "\n" + textures

    # ---- 1. 映射 ----
    palette_ids = re.findall(r'Make\("([a-z]+)",', chroma)
    if len(palette_ids) < 10:
        errors.append("调色板解析到的颜色太少（%d）：PetNestChroma.Palette 格式变了？" % len(palette_ids))
    resolver = method_body(recipes, "ResolveElement")
    if resolver is None or "switch (colorId)" not in resolver:
        errors.append("缺少 PetNestAuraStyles.ResolveElement(string colorId) 的 switch 映射")
        mapping = {}
    else:
        mapping = dict(re.findall(r'case\s+"([a-z]+)"\s*:\s*return\s+PetNestAuraElement\.(\w+)\s*;', resolver))
    for color_id in palette_ids:
        if color_id not in mapping:
            errors.append("调色板颜色 %s 没有显式映射到专属元素（不许靠 default 兜底）" % color_id)
    used = [mapping[c] for c in palette_ids if c in mapping]
    if len(set(used)) != len(used):
        errors.append("炫彩颜色的元素有重复：%s（owner：不同炫彩弄不同的粒子特效）" % ", ".join(used))
    enum_body = re.search(r"enum\s+PetNestAuraElement\s*\{([^}]*)\}", recipes)
    enum_names = set(re.findall(r"(\w+)\s*=", enum_body.group(1))) if enum_body else set()
    for element in used:
        if element not in enum_names:
            errors.append("映射到了不存在的元素 " + element)

    primary = method_body(recipes, "BuildChromaPrimary")
    accent = method_body(recipes, "BuildChromaAccent")
    if primary is None or accent is None:
        errors.append("缺少 BuildChromaPrimary / BuildChromaAccent")
    else:
        primary_cases = switch_cases(primary)
        accent_cases = switch_cases(accent)
        for element in set(used):
            if element == "Radiant":
                # 圣光是 default 分支（也是未知颜色的兜底）
                if "default: BuildRadiant(" not in norm(primary):
                    errors.append("主元素 switch 的 default 必须是 BuildRadiant")
                continue
            if element not in primary_cases:
                errors.append("主元素 switch 缺少 %s 分支" % element)
            if element not in accent_cases:
                errors.append("点缀 switch 缺少 %s 分支" % element)
            for builder in ELEMENT_BUILDERS.get(element, []):
                if method_body(recipes, builder) is None:
                    errors.append("元素 %s 的配方方法 %s 不存在" % (element, builder))

    # ---- 2. 门控与接线 ----
    attach = method_body(spawner, "AttachChromaAura")
    detach = method_body(spawner, "DetachChromaAura")
    cleanup = method_body(spawner, "CleanupOnce")
    if attach is None:
        errors.append("PetNestCompanionSpawner 缺少 AttachChromaAura")
    else:
        a = norm(attach)
        gate = a.find("if (!pet.shiny && !chroma) return;")
        call = a.find("PetNestAuraEffect.Attach(handle.Character, pet.shiny,")
        if gate < 0:
            errors.append("AttachChromaAura 缺少普通崽早返「if (!pet.shiny && !chroma) return;」")
        if call < 0:
            errors.append("AttachChromaAura 没有调用 PetNestAuraEffect.Attach(handle.Character, pet.shiny, ...)")
        if gate >= 0 and call >= 0 and gate > call:
            errors.append("普通崽早返必须在创建特效之前")
        if "handle.Aura = PetNestAuraEffect.Attach(" not in a:
            errors.append("特效句柄必须记在 handle.Aura 上，回收才找得到")
    if detach is None or "aura.Dispose();" not in norm(detach):
        errors.append("DetachChromaAura 必须调 aura.Dispose()")
    if cleanup is None:
        errors.append("缺少 CleanupOnce(PetNestCompanionHandle)")
    else:
        c = norm(cleanup)
        d = c.find("DetachChromaAura(handle);")
        k = c.find("UnityEngine.Object.Destroy(handle.Character.gameObject);")
        if d < 0 or k < 0 or d > k:
            errors.append("CleanupOnce 必须先 DetachChromaAura(handle) 再销毁角色")
    tryactivate = method_body(spawner, "TryActivate")
    if tryactivate is None or "AttachChromaAura(handle, pet);" not in tryactivate:
        errors.append("TryActivate 必须在激活时 AttachChromaAura(handle, pet)")

    # ---- 3. 发射方式 ----
    if re.search(r"\bvoid\s+LateUpdate\s*\(", fx_all):
        errors.append("特效不应有 LateUpdate（旧配方每帧手撒，密度随帧率走）")
    if ".Emit(" in fx_all:
        errors.append("特效不应手动 Emit：发射只走粒子系统的固定速率与 burst")
    if re.search(r"class\s+PetNestAuraEffect\s*:\s*RingParticleEffect", fx_all):
        errors.append("PetNestAuraEffect 不应再继承 RingParticleEffect（那是霜雾配方）")

    # ---- 4. 预算 ----
    chroma_budget = re.search(r"const\s+int\s+ChromaParticleBudget\s*=\s*(\d+)\s*;", effect)
    shiny_budget = re.search(r"const\s+int\s+ShinyParticleBudget\s*=\s*(\d+)\s*;", effect)
    under_shiny = re.search(r"const\s+float\s+ChromaUnderShinyIntensity\s*=\s*([0-9.]+)f\s*;", effect)
    if not chroma_budget or int(chroma_budget.group(1)) > CHROMA_BUDGET_MAX:
        errors.append("ChromaParticleBudget 缺失或超过 %d" % CHROMA_BUDGET_MAX)
    if not shiny_budget or int(shiny_budget.group(1)) > SHINY_BUDGET_MAX:
        errors.append("ShinyParticleBudget 缺失或超过 %d" % SHINY_BUDGET_MAX)
    if not under_shiny or not (0.0 < float(under_shiny.group(1)) < 1.0):
        errors.append("ChromaUnderShinyIntensity 必须在 (0,1) 之间（异色叠炫彩时炫彩层降强度）")
    budget_start = norm(method_body(effect, "Build") or "")
    if "_particleBudgetLeft = shiny ? ShinyParticleBudget : ChromaParticleBudget;" not in budget_start:
        errors.append("Build 必须按异色 / 炫彩给出预算")

    # 任何变量名上的 .maxParticles 写入都算（反向验证时 m2.maxParticles = 500 曾从只认 main. 的写法下漏过）
    assigns = len(re.findall(r"\.maxParticles\s*=", fx_all))
    new_emitter = method_body(effect, "NewEmitter")
    clone_official = method_body(recipes, "CloneOfficial")
    factories_ok = True
    for name, body in (("NewEmitter", new_emitter), ("CloneOfficial", clone_official)):
        if body is None:
            errors.append("缺少发射器工厂 " + name)
            factories_ok = False
            continue
        b = norm(body)
        if "int cap = TakeBudget(maxParticles);" not in b or "main.maxParticles = cap;" not in b:
            errors.append("%s 必须经 TakeBudget 扣预算并把 cap 写进 maxParticles" % name)
            factories_ok = False
    if factories_ok and assigns != 2:
        errors.append("maxParticles 只允许在 NewEmitter / CloneOfficial 两处赋值（实际 %d 处）" % assigns)
    take = method_body(effect, "TakeBudget")
    if take is None or "Mathf.Min(Mathf.Max(0, requested), _particleBudgetLeft)" not in norm(take):
        errors.append("TakeBudget 必须按剩余预算封顶")

    # 数值复算
    accent_caps = caps_in(accent or "")
    accent_cap = max(accent_caps) if accent_caps else 0
    if accent_cap <= 0:
        errors.append("点缀层没有解析到 Scaled(N, f) 上限")
    worst_primary = 0
    for element in set(used):
        per_path = []
        for builder in ELEMENT_BUILDERS.get(element, []):
            body = method_body(recipes, builder)
            if body is None:
                continue
            caps = caps_in(body)
            if not caps:
                errors.append("配方 %s 没有解析到 Scaled(N, f) 上限" % builder)
            per_path.append(sum(caps))
        if not per_path:
            continue
        heaviest = max(per_path)
        worst_primary = max(worst_primary, heaviest)
        if chroma_budget and heaviest + accent_cap > int(chroma_budget.group(1)):
            errors.append("元素 %s 主层 %d + 点缀 %d 超过炫彩预算 %s，点缀会被静默截掉"
                          % (element, heaviest, accent_cap, chroma_budget.group(1)))
    shiny_total = 0
    for part in SHINY_PARTS:
        body = method_body(recipes, part)
        if body is None:
            continue
        for n in re.findall(r"NewEmitter\([^;]*?,\s*(\d+)\s*,\s*new Vector3", norm(body)):
            shiny_total += int(n)
    if shiny_total <= 0:
        errors.append("异色发射器上限没有解析到（NewEmitter(..., N, new Vector3...)）")
    if shiny_budget and under_shiny:
        factor = float(under_shiny.group(1))
        layered = shiny_total + math.ceil(worst_primary * factor) + math.ceil(accent_cap * factor) + 4
        if layered > int(shiny_budget.group(1)):
            errors.append("异色本体 %d + 0.6 强度炫彩（约 %d）超过异色预算 %s"
                          % (shiny_total, layered - shiny_total, shiny_budget.group(1)))

    # ---- 5. 异色全套 ----
    shiny = method_body(recipes, "BuildShiny")
    if shiny is None:
        errors.append("缺少 BuildShiny")
    else:
        for part in SHINY_PARTS:
            if part + "(" not in shiny:
                errors.append("异色缺少 " + part + " 调用")
    halo = method_body(recipes, "BuildShinyHalo")
    if halo is None or "PetNestAuraTexture.RuneOuter" not in halo or "PetNestAuraTexture.RuneInner" not in halo:
        errors.append("异色符文环必须内外两圈（RuneOuter + RuneInner）")
    light = method_body(recipes, "BuildShinyLight")
    if light is None or "light.shadows = LightShadows.None;" not in light:
        errors.append("异色点光必须存在且不投影")

    # ---- 6. 资源生命周期 ----
    for ctor in ("new Material(", "new Texture2D(", "new Mesh("):
        total = fx_all.count(ctor)
        owned = fx_all.count("Own(" + ctor)
        if total != owned:
            errors.append("%s 必须直接包在 Own( 里登记销毁（%d 处中只有 %d 处登记）" % (ctor, total, owned))
    on_destroy = method_body(effect, "OnDestroy")
    if on_destroy is None:
        errors.append("缺少 OnDestroy")
    else:
        o = norm(on_destroy)
        if "for (int i = 0; i < _ownedAssets.Count; i++)" not in o or "Destroy(asset);" not in o:
            errors.append("OnDestroy 必须遍历 _ownedAssets 逐个 Destroy")
    own = method_body(effect, "Own")
    if own is None or "_ownedAssets.Add(asset);" not in own:
        errors.append("Own( 必须把资源登记进 _ownedAssets")
    if re.search(r"\bstatic\s+(?:readonly\s+)?(?:Material|Texture2D|Mesh|Texture|Sprite)\b", fx_all):
        errors.append("特效文件里不应有静态材质 / 贴图 / 网格（没有静态缓存就不需要 ResetStaticCaches）")

    # ---- 7. 材质口径 ----
    if "Shader.Find(" in fx_all:
        errors.append("特效文件不应自己 Shader.Find：只从共享模板派生（已实证能在正式包里渲染的那条链）")
    attach_fx = method_body(effect, "Attach")
    if attach_fx is None or "RingParticleEffect.GetSharedParticleMaterial()" not in attach_fx:
        errors.append("Attach 必须以 RingParticleEffect.GetSharedParticleMaterial() 为材质模板")
    get_material = method_body(effect, "GetMaterial")
    if get_material is None or "Own(new Material(_template))" not in get_material:
        errors.append("GetMaterial 必须从模板复制材质")

    # ---- 8. 挂载与逐帧 ----
    if attach_fx is None or "root.transform.SetParent(character.transform, false);" not in attach_fx:
        errors.append("特效根必须挂在角色根下（随崽销毁）")
    if attach_fx is not None and "root.SetActive(false);" not in attach_fx:
        errors.append("特效必须先失活再搭建（否则新 ParticleSystem 按默认参数先喷一下）")
    build = norm(method_body(effect, "Build") or "")
    if "enabled = _haloOuter != null || _haloInner != null || _light != null;" not in build:
        errors.append("只有异色（符文环 / 点光）才启用 Update，纯炫彩崽零脚本帧成本")

    if errors:
        print("PetNestAuraEffectGuard: FAIL (%d errors)" % len(errors))
        for error in errors:
            print("  - " + error)
        return 1
    print("PetNestAuraEffectGuard: PASS (%d colors -> %d elements, worst chroma %d+%d/%s, shiny %d/%s)"
          % (len(palette_ids), len(set(used)), worst_primary, accent_cap,
             chroma_budget.group(1), shiny_total, shiny_budget.group(1)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
