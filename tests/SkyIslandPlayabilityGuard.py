"""天空岛可玩性复审（2026-09-09）钉住的不变式。

覆盖六条本轮修掉的缺陷。**结构守卫证明不了玩家真的按得到、躲得开**，
实机 smoke 仍按 `Assets/Data/GameplayCoverage.json` 的 M_SKY_ISLAND_* 逐条走。
"""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def main():
    def read(path):
        return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))

    world = read("DebugAndTools/SkyIsland/SkyIslandWorldStory.cs")
    guide = read("DebugAndTools/SkyIsland/SkyIslandGuideInteractable.cs")
    crate = read("DebugAndTools/SkyIsland/SkyIslandRewardCrate.cs")
    patch = read("DebugAndTools/SkyIsland/SkyIslandExplosionObstaclePatch.cs")
    lease = read("DebugAndTools/SkyIsland/SkyIslandRaidLease.cs")
    boss = read("DebugAndTools/SkyIsland/SkyIslandStormBoss.cs")
    enc = read("DebugAndTools/SkyIsland/SkyIslandEncounters.cs")
    rules = read("DebugAndTools/SkyIsland/SkyIslandStoryRules.cs")
    session = read("DebugAndTools/SkyIsland/SkyIslandSession.cs")
    errors = []

    def need(source, label, *tokens):
        for token in tokens:
            if token not in source:
                errors.append(label + " 缺少 " + token)

    def forbid(source, label, *tokens):
        for token in tokens:
            if token in source:
                errors.append(label + " 不得出现 " + token)

    # ---- 1. 同点交互：官方 CA_Interact 按「距离严格小于」取唯一目标 ----
    # 纪念物盖住 Search_D / Search_G / Search_E / Search_H 就等于 K1/K2/K3 与敲钟永久点不到。
    beacon = world.split("private void Beacon(string marker", 1)[1].split("internal void Hide()", 1)[0]
    need(beacon, "完成纪念物落点",
         "SkyIslandRewardCrate.TryFindCratePosition(",
         "SkyIslandRewardCrate.InteractableSeparation",
         "SkyIslandStoryInteractable.Create(root.transform, spot,")
    # 反例：纪念物一旦又摆回锚点自身位置，这条不变式就失效了。
    forbid(beacon, "完成纪念物", "SkyIslandStoryInteractable.Create(root.transform, point.position")
    # 找不到净空必须只留光、不挂交互体，绝不退回原点。
    if "out spot)) return;" not in beacon:
        errors.append("完成纪念物在无净空落点时必须放弃挂交互体，而不是退回锚点")

    attach = guide.split("internal static void Attach(", 1)[1].split("private static Vector3 GuideOffset(", 1)[0]
    need(attach, "航路图落点", "GuideOffset(root.transform, marker)")
    forbid(attach, "航路图落点", '"SkyIsland_TravelGuide", Vector3.zero')
    need(guide, "航路图偏移", "SkyIslandRewardCrate.TryFindCratePosition(",
         "SkyIslandRewardCrate.InteractableSeparation", "SkyIslandLootTables.StableHash(marker.name)")

    # 间距必须大于两侧触发盒半边之和（纪念物 3/2=1.5 + 见闻点 2.2/2=1.1 = 2.6）。
    sep = None
    for line in crate.splitlines():
        if "InteractableSeparation" in line and "=" in line and "const" in line:
            sep = float(line.split("=", 1)[1].strip().rstrip(";").rstrip("fF"))
    if sep is None:
        errors.append("缺少共享间距常量 SkyIslandRewardCrate.InteractableSeparation")
    elif sep < 2.7:
        errors.append("交互体间距 %.2f 小于两侧触发盒半边之和 2.6，同点歧义会复现" % sep)

    # ---- 2. 官方爆炸遮挡：y 硬编码 0.5，天空岛岛面在 0-62 米 ----
    need(patch, "爆炸遮挡补丁",
         '[HarmonyPatch(typeof(ExplosionManager), "CheckObsticle")]',
         "internal static float FlattenHeight(float startY, float endY)",
         "Mathf.Min(startY, endY) + EyeHeightAboveLower",
         "if (!armed) return true;",
         "SceneManager.GetActiveScene().handle != armedSceneHandle")
    # 官方在 y=0 平地上等效 0.5：min(0.2, 0.6) + 0.3。改了这个常量就不再与原版逐位一致。
    eye = None
    for line in patch.splitlines():
        if "EyeHeightAboveLower" in line and "const" in line and "=" in line:
            eye = float(line.split("=", 1)[1].strip().rstrip(";").rstrip("fF"))
    if eye is None or abs(eye - 0.3) > 1e-6:
        errors.append("EyeHeightAboveLower 必须为 0.3，才能在 y=0 平地上复现官方的 0.5")
    # 层掩码必须与官方 obsticleLayers 同一组合，少一层就等于放行一整类遮挡物。
    need(patch, "爆炸遮挡层",
         "GameplayDataSettings.Layers.wallLayerMask.value",
         "GameplayDataSettings.Layers.groundLayerMask.value")
    # 只在天空岛生效：租约负责登记与撤销，撤销必须多于一处（正常卸载 + 释放兜底）。
    need(lease, "遮挡补丁生命周期", "SkyIslandExplosionObstaclePatch.Arm(scene)")
    if lease.count("SkyIslandExplosionObstaclePatch.Disarm()") < 2:
        errors.append("遮挡补丁必须在场景卸载与租约释放两处撤销")

    # ---- 3. 噬风相位提速不得复利 ----
    forbid(boss, "相位提速", "baseReactionTime /=", "reactionTime /=", "shootDelay /=", "/= 1.25f")
    need(boss, "相位提速",
         "internal static float PhaseSpeedup(int phase)",
         "float speedup = PhaseSpeedup(phase);",
         "brain.baseReactionTime = baseReactionTime / speedup;",
         "brain.reactionTime = baseCurrentReactionTime / speedup;",
         "brain.shootDelay = baseShootDelay / speedup;")
    cap = None
    for line in boss.splitlines():
        if "MaxPhaseSpeedup" in line and "const" in line and "=" in line:
            cap = float(line.split("=", 1)[1].strip().rstrip(";").rstrip("fF"))
    if cap is None or not 1.0 < cap <= 2.0:
        errors.append("MaxPhaseSpeedup 必须在 (1, 2] 内封顶，否则末相位没有反应窗口")
    # 基线必须在 Bind 里取，且取的是已含档次倍率的值。
    bind = boss.split("internal void Bind(", 1)[1].split("internal static int PhaseForFraction", 1)[0]
    need(bind, "相位基线", "baseReactionTime = brain.baseReactionTime;",
         "baseCurrentReactionTime = brain.reactionTime;", "baseShootDelay = brain.shootDelay;")

    # ---- 4. 敌人 preset 必须按遭遇身份取，不能全岛同一个 ----
    forbid(enc, "遭遇 preset", "sources[i % sources.Count]")
    need(enc, "遭遇 preset",
         "sources[PresetIndex(encounter.Id, i)]",
         "private int PresetIndex(string encounterId, int index)",
         "SkyIslandLootTables.StableHash(encounterId")
    # 跨机稳定：不得退回 string.GetHashCode（Mono 与 .NET Core 口径不同）。
    forbid(enc, "遭遇 preset", "encounterId.GetHashCode()")

    # ---- 5. 钟守物证路线认「折翎已了结」，不只认和解 ----
    bell = rules.split("case SkyIslandStoryAction.ReconcileBellKeeper:", 1)[1] \
        .split("case SkyIslandStoryAction.BellKeeperDefeated:", 1)[0]
    need(bell, "钟守物证路线", "source.ZhelingResolved")
    forbid(bell, "钟守物证路线", "SkyIslandStoryFlag.ZhelingReconciled")
    # 提示文案必须说明战胜也算，否则玩家仍会去找一个已不可能达成的条件。
    if "和解或战胜都算" not in bell:
        errors.append("钟守失败提示必须说明战胜折翎同样算作了结")

    # ---- 6. 剧情面板选项随状态刷新 ----
    need(world, "面板刷新",
         "private Action reopen;",
         "private string Refreshed(bool changed, string message)",
         "reopen = delegate { ReadPoint(key, recorded); };",
         "reopen = delegate { Talk(id, speaker); };",
         "Refreshed(contract.TryAccept(kind, out message), message)",
         "Refreshed(contract.TryAbandon(out message), message)",
         "Refreshed(story.TryApply(action, out message), message)")
    if "reopen = null;" not in world:
        errors.append("会话销毁时必须放开 reopen 捕获的说话人引用")

    # ---- 7. 存档落盘门按半径，不按全图 ----
    # 自动组按出击刷新后，全图口径等于把落盘门永久关上。
    forbid(session, "落盘门", "story.Tick(encounters == null || !encounters.HasLivingEnemies)")
    need(session, "落盘门",
         "HasLivingEnemiesWithin(player.transform.position, SaveQuietRadius)",
         "private const float SaveQuietRadius")
    need(enc, "半径口径", "internal bool HasLivingEnemiesWithin(Vector3 point, float radius)")

    # ---- 8. 撤离点必须有地面标识（CR-2026-09-09-012） ----
    # 作者场景里 Exit / BellExtraction 只是 Blender Empty，自绘地图删除后官方小地图也不标撤离点，
    # 而 F3 面板与中英 Wiki 三处都写着「蓝环 / 绿环」。钟庭那个是终章后才开放、离最近地标 14 m，
    # 没有标识就是让玩家去找一个不存在的东西。
    ring = read("DebugAndTools/SkyIsland/SkyIslandGroundRing.cs")
    need(ring, "撤离环 owner",
         "internal sealed class SkyIslandExtractionRings",
         "BossRushUIColors.Accent",
         "BossRushUIColors.SuccessText",
         "internal void Apply(bool bellUnlocked)",
         # 布局 v2：两处航标广场撤离环，与钟庭环同一套建造与翻转口径。
         "internal void AddBeaconRings(Transform root, Transform wind, Transform star, float radius, int groundMask)",
         "internal void ApplyBeacons(bool windUnlocked, bool starUnlocked)")
    need(session, "撤离环接线",
         "extractionRings = new SkyIslandExtractionRings(root.transform, exitMarker, bellExit,",
         "ExtractionRadius, groundMask);",
         "extractionRings.AddBeaconRings(root.transform, windExit, starExit, ExtractionRadius, groundMask);")
    # 装配时一次 + 每帧同步一次；两处都必须读同一个解锁事实，写死 true 会提前露出还没开放的撤离点。
    if session.count("extractionRings.Apply(BellExitIfUnlocked() != null);") < 2:
        errors.append("撤离环必须在装配与每帧两处都按 BellExitIfUnlocked() 同步解锁状态")
    if session.count("extractionRings.ApplyBeacons(WindExitIfUnlocked() != null, StarExitIfUnlocked() != null);") < 2:
        errors.append("航标广场撤离环必须在装配与每帧两处都按 Wind/StarExitIfUnlocked() 同步解锁状态")
    if 'Safe("extraction_rings", delegate { if (extractionRings != null) extractionRings.Dispose(); });' not in session:
        errors.append("撤离环必须在 Cleanup 里销毁")
    if 'Safe("map_markers", delegate { if (mapMarkers != null) mapMarkers.Dispose(); });' not in session:
        errors.append("官方地图指引点必须在 Cleanup 里销毁")
    # 圈的半径必须就是判定半径：画一个大小对不上的圈比不画更坏。
    if "SkyIslandGroundRing.SetShape(ring, radius, RingWidth, color)" not in ring:
        errors.append("撤离环必须按传入的判定半径画，不得自带缩放")
    # 钟庭环与 BellExitIfUnlocked() 同一事实源，双航标点亮前不能露出。
    if "bellRing.gameObject.SetActive(false)" not in ring:
        errors.append("钟庭环必须默认隐藏，直到双航标点亮")
    for name in ("windRing", "starRing"):
        if name + ".gameObject.SetActive(false)" not in ring:
            errors.append("航标广场撤离环必须默认隐藏，直到对应航标点亮：" + name)

    # ---- 9. 贴地圆环复用同一建造点，材质必须显式销毁 ----
    # renderer.material 会给每个 LineRenderer 实例化一份副本且需调用方自行销毁；
    # 噬风每次脉冲建一次圈，用 .material 就是每次泄漏一份。
    forbid(ring, "共享圆环", "line.material =")
    need(ring, "共享圆环", "line.sharedMaterial = material;",
         "UnityEngine.Object.Destroy(shared);")
    need(boss, "噬风预警圈", "SkyIslandGroundRing.Create(boss.transform",
         "SkyIslandGroundRing.SetShape(line, radius,")
    if "internal static void ResetStaticCaches() { SkyIslandGroundRing.ResetStaticCaches(); }" not in boss:
        errors.append("噬风的静态复位必须委托给共享圆环，避免两处各留一份材质")

    # ---- 10. 每帧路径不得产生垃圾（AGENTS 4.12） ----
    module = read("DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs")
    update = module.split("public override void OnUpdate(", 1)[1].split("private void CreateSign(", 1)[0]
    # Scene.name 每次调用都新建托管字符串；模块 OnUpdate 在所有场景每帧都跑。
    if "GetActiveScene().name" in update:
        errors.append("模块 OnUpdate 不得每帧调用 Scene.name（每次分配一个托管字符串）")
    need(module, "基地判定缓存", "private bool InBaseHubScene()",
         "active.handle != cachedSceneHandle", "cachedSceneHandle = 0;")
    if module.count("cachedSceneHandle = 0;") < 2:
        errors.append("场景缓存必须在开始切图与关卡就绪两处作废，句柄复用才兜得住")

    # ---- 11. 光照不得每帧重写（RenderSettings.ambient* 在 Trilight 下会重算环境球） ----
    lighting = read("DebugAndTools/SkyIsland/SkyIslandLighting.cs")
    need(lighting, "光照写入阈值",
         "internal static bool Similar(Color a, Color b)",
         "Mathf.Abs(intensity - appliedIntensity) < IntensityEpsilon",
         "Quaternion.Angle(rotation, appliedRotation) < RotationEpsilonDegrees",
         "dirty = true;")
    for token in ("ColorEpsilon", "IntensityEpsilon", "RotationEpsilonDegrees"):
        value = None
        for line in lighting.splitlines():
            if token in line and "const" in line and "=" in line:
                value = float(line.split("=", 1)[1].strip().rstrip(";").rstrip("fF"))
        if value is None or not 0 < value <= 0.2:
            errors.append("%s 必须是感知阈以下的正数，否则不是省写入而是丢过渡" % token)

    # ---- 12. HUD 的物资分母必须够得到 ----
    scav = read("DebugAndTools/SkyIsland/SkyIslandScavenging.cs")
    placed = scav.split("internal int PlacedPoints", 1)[1].split("internal int OpenedPoints", 1)[0]
    if "!points[i].Failed" not in placed:
        errors.append("PlacedPoints 必须排除建箱失败的点，否则 HUD 分母永远够不到")

    # ---- 编译清单：新增 .cs 必须登记，否则源码在但不参与编译 ----
    manifest = (ROOT / "compile_official.bat").read_text(encoding="utf-8-sig")
    for produced in ("SkyIslandExplosionObstaclePatch.cs", "SkyIslandGroundRing.cs"):
        if produced not in manifest:
            errors.append(produced + " 未登记进 compile_official.bat")

    print("SkyIslandPlayabilityGuard: " + ("FAIL\n" + "\n".join(errors) if errors else "PASS"))
    return bool(errors)


if __name__ == "__main__":
    raise SystemExit(main())
