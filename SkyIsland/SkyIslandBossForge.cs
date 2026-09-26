using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>头目 / 岛主控制器从遭遇 owner 拿到的环境：地图根、地面层、会话是否仍有效、字幕通道、叫帮手与头顶气泡。</summary>
    internal sealed class SkyIslandBossContext
    {
        internal Transform Root;
        internal int GroundMask;
        internal Func<bool> Valid;
        internal Action<string, bool> Report;
        /// <summary>
        /// 穗镰「谷仓叫人」（R3）：把某一组还活着的人拉到给定位置附近并盯上主角，返回拉过来几个。
        /// 手动组、已清场的组不叫；不另开生成路径（SkyIslandEncounters.CallGroup）。
        /// </summary>
        internal Func<string, Vector3, int> CallGroup;
        /// <summary>
        /// 头顶气泡（<see cref="SkyIslandBossVoice"/> 用）。与 <see cref="Report"/> 是两条通道：
        /// Report 是屏幕下方的机制字幕（必须读到），Bark 是头顶演出（漏读不影响打得过）。
        /// 敌人侧的气泡预算由遭遇 owner 持有，控制器不直接拿 `SkyIslandChatter`。
        /// </summary>
        internal SkyIslandBarkDelegate Bark;
    }

    /// <summary>已按档案装配过的一次性标记。随角色销毁，不需要额外清理。</summary>
    internal sealed class SkyIslandBossMark : MonoBehaviour { }

    /// <summary>
    /// COMPAT：把一名刚生成的官方拾荒者装配成头目 / 岛主（<see cref="SkyIslandBossRules"/> 的档案）。
    ///
    /// 由 `SkyIslandEncounters.ApplyIdentity` 在普通档次装饰之前分派；遭遇 owner 已经做完克隆 preset、
    /// 本图 graphMask、解除距离休眠与 `SetTeam(Teams.wolf)`，这里不另开生成路径。
    ///
    /// 五层都是**可失败的装饰**（口径同 <see cref="SkyIslandEnemyTiers"/>），任一失败都不影响这场战斗能不能打完：
    /// 1. 名字、血条名与 Boss 图标（改克隆 preset，不污染官方角色池）；
    /// 2. 数值与反应（乘法，<see cref="SkyIslandBossMark"/> 挡重入）；
    /// 3. 配装事务（照 Mode H 装配器：先问 prefab、换下来的官方随机装备销毁、任一件失败整批回收）；
    /// 4. 只缩放 `characterModel`，不动角色 transform（碰撞体与导航半径保持官方口径）；
    /// 5. 专属掉落组件与招式控制器。
    ///
    /// 不染色：MaterialPropertyBlock 会连装备模型一起染，专属装备的颜色就是它的辨识度。
    /// </summary>
    internal static class SkyIslandBossForge
    {
        /// <summary>
        /// 头目 / 岛主倒下。由天空岛剧情 owner 订阅（记首杀手记、回话），订阅方必须幂等并在销毁时退订。
        /// 带档案与倒下位置；只由招式控制器的死亡回调派发。
        /// </summary>
        internal static event Action<SkyIslandBossProfile, Vector3> Defeated;

        internal static bool TryApply(CharacterMainControl created, string encounterId, int index, SkyIslandBossContext context)
        {
            if (created == null || context == null) return false;
            SkyIslandBossProfile profile = SkyIslandBossRules.Find(encounterId, index);
            if (profile == null) return false;
            if (created.GetComponent<SkyIslandBossMark>() != null)
            {
                Debug.LogWarning("[SkyIslandBoss] 档案已装配过，跳过重复应用：" + created.name);
                return true;
            }
            created.gameObject.AddComponent<SkyIslandBossMark>();
            ApplyIdentity(created, profile);
            ApplyStats(created, profile);
            ApplyReaction(created, profile);
            if (!string.IsNullOrEmpty(profile.FaceId)) SkyIslandResidents.ApplyBattleFace(created, profile.FaceId);
            string reason;
            if (!TryEquip(created, profile, out reason))
                Debug.LogWarning("[SkyIslandBoss] 配装失败，" + profile.Id + " 以无专属装备形态参战：" + reason);
            ApplyScale(created, profile);
            try
            {
                int seed = unchecked(Environment.TickCount ^ created.GetInstanceID() ^ SkyIslandLootTables.StableHash(profile.Id));
                created.gameObject.AddComponent<SkyIslandBossLoot>().Bind(created, profile, seed);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 专属掉落组件装配失败（原版掉落照常）：" + e.Message); }
            BindVoice(created, profile, null, context);
            BindController(created, profile, context);
            return true;
        }

        /// <summary>
        /// 头顶台词（<see cref="SkyIslandBossVoice"/>）。与掉落、招式一样是**可失败的装饰**：
        /// 装不上只是这一位不说话，这场仗照打。具名剧情对手（折翎战斗体、失控的守钟装置）
        /// 不在档案表里，由遭遇 owner 直接调这一个入口。
        /// </summary>
        internal static void BindVoice(CharacterMainControl created, SkyIslandBossProfile profile,
            string championId, SkyIslandBossContext context)
        {
            if (created == null || context == null || context.Bark == null) return;
            try
            {
                SkyIslandBossVoice voice = created.gameObject.AddComponent<SkyIslandBossVoice>();
                if (profile != null) voice.Bind(created, profile, context);
                else voice.BindChampion(created, championId, context);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 头顶台词装配失败（只是这一位不说话）：" + e.Message); }
        }

        private static void ApplyIdentity(CharacterMainControl created, SkyIslandBossProfile profile)
        {
            try
            {
                LocalizationHelper.InjectLocalization(profile.NameKey, SkyIslandBossRules.Name(profile));
                CharacterRandomPreset preset = created.characterPreset;
                if (preset == null) return;
                // 这是遭遇 owner 本次生成克隆出来的 preset，改它不污染官方角色池。
                // 改 nameKey 意味着击杀记在自己的键下（官方 SavesCounter），这是有意的：头目与岛主是模组内容。
                preset.nameKey = profile.NameKey;
                preset.showName = true;
                preset.showHealthBar = true;
                if (BossRushEagerReflectionCache.CharacterRandomPreset_CharacterIconType != null)
                    BossRushEagerReflectionCache.CharacterRandomPreset_CharacterIconType.SetValue(preset, CharacterIconTypes.boss);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 名字与图标失败：" + e.Message); }
        }

        private static void ApplyStats(CharacterMainControl created, SkyIslandBossProfile profile)
        {
            try
            {
                Item item = created.CharacterItem;
                if (item == null) return;
                Stat maxHealth = item.GetStat("MaxHealth".GetHashCode());
                if (maxHealth != null) maxHealth.BaseValue *= profile.Health;
                // 先改上限再同步当前血量，否则角色以旧血量出场。
                if (created.Health != null) created.Health.SetHealth(created.Health.MaxHealth);
                float damage = Mathf.Min(profile.Damage, 3f);
                Stat gun = item.GetStat("GunDamageMultiplier".GetHashCode());
                if (gun != null) gun.BaseValue *= damage;
                Stat melee = item.GetStat("MeleeDamageMultiplier".GetHashCode());
                if (melee != null) melee.BaseValue *= damage;
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 数值倍率失败：" + e.Message); }
        }

        /// <summary>
        /// 反应速度。遭遇 owner 在分派前已按档次跑过 `SkyIslandEnemyTiers.ApplyAi`：新档次的反应倍率走默认分支（1），
        /// 追踪距离取精英以上的 85 米，所以这里只乘档案自己的倍率，不会复利。
        /// </summary>
        private static void ApplyReaction(CharacterMainControl created, SkyIslandBossProfile profile)
        {
            try
            {
                AICharacterController ai = created.GetComponentInChildren<AICharacterController>();
                if (ai == null || Mathf.Approximately(profile.Reaction, 1f)) return;
                ai.baseReactionTime /= profile.Reaction;
                ai.reactionTime /= profile.Reaction;
                ai.shootDelay /= profile.Reaction;
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] AI 调参失败：" + e.Message); }
        }

        private static void ApplyScale(CharacterMainControl created, SkyIslandBossProfile profile)
        {
            try
            {
                if (Mathf.Approximately(profile.Scale, 1f) || created.characterModel == null) return;
                // 只放大模型：挂在模型 socket 上的专属装备跟着一起放大，碰撞体与导航半径不变。
                created.characterModel.transform.localScale = Vector3.one * profile.Scale;
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 体型缩放失败：" + e.Message); }
        }

        /// <summary>
        /// 配装事务：逐件先问 prefab、实例化、插进对应槽位；被顶下来的官方随机装备直接销毁（它没穿在 Boss 身上，不该出现在箱子里）。
        /// 任一件失败就把本事务已插上的专属装备整批逆序回收。武器、弹药与背包物品保持官方 preset 原样，这就是「其余走原版掉落」。
        /// </summary>
        internal static bool TryEquip(CharacterMainControl created, SkyIslandBossProfile profile, out string reason)
        {
            reason = null;
            Item characterItem = null;
            try { characterItem = created.CharacterItem; }
            catch (Exception)
            {
                // 角色刚创建时 CharacterItem 可能尚未装配好，按失败处理
                characterItem = null;
            }
            if (characterItem == null) { reason = "character_item_missing"; return false; }
            List<Item> plugged = new List<Item>();
            List<Slot> slots = new List<Slot>();
            for (int i = 0; i < profile.Gear.Length; i++)
            {
                if (TryPlugPiece(characterItem, profile.Gear[i], plugged, slots, out reason)) continue;
                for (int j = plugged.Count - 1; j >= 0; j--)
                {
                    try { plugged[j].Detach(); plugged[j].DestroyTree(); }
                    catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 配装回收失败：" + e.Message); }
                }
                return false;
            }
            // Slot.Plug 本身会触发 onSlotContentChanged；再显式刷一次（死亡亡魂同款），不重复调 SetItem 累积订阅。
            for (int i = 0; i < slots.Count; i++)
            {
                try { slots[i].ForceInvokeSlotContentChangedEvent(); }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 装备模型刷新失败：" + e.Message); }
            }
            return true;
        }

        private static bool TryPlugPiece(Item characterItem, SkyIslandBossGearPiece piece, List<Item> plugged, List<Slot> slots,
            out string reason)
        {
            reason = null;
            Item prefab = null;
            try { prefab = ItemAssetsCollection.GetPrefab(piece.TypeId); }
            catch (Exception)
            {
                // prefab 查询失败按资源缺失处理
                prefab = null;
            }
            // 缺资源时官方 InstantiateSync 回一个带同样 TypeID 的空壳，回读分辨不出来：必须先问 prefab（contracts §7.1）。
            if (prefab == null) { reason = "prefab_missing:" + piece.TypeId; return false; }
            Slot slot = FindSlot(characterItem, piece.Slot);
            if (slot == null) { reason = "slot_missing:" + piece.Slot; return false; }
            Item created = null;
            try { created = ItemAssetsCollection.InstantiateSync(piece.TypeId); }
            catch (Exception)
            {
                // 实例化异常按失败处理
                created = null;
            }
            if (created == null || created.TypeID != piece.TypeId)
            {
                if (created != null) created.DestroyTree();
                reason = "instantiate_failed:" + piece.TypeId;
                return false;
            }
            try
            {
                Item previous = slot.Content;
                if (previous != null) { previous.Detach(); previous.DestroyTree(); }
            }
            catch (Exception)
            {
                // 原装备可能已被官方回收；清空失败不阻断，Plug 给最终结论
            }
            bool ok;
            try
            {
                Item unplugged;
                ok = slot.Plug(created, out unplugged);
                if (unplugged != null) unplugged.DestroyTree();
            }
            catch (Exception) { ok = false; }
            if (!ok)
            {
                // 槽位的 requireTags 不满足时官方只打一行 Debug.Log：不能让这件留在背包里被当成掉落物掉出来。
                created.DestroyTree();
                reason = "plug_rejected:" + piece.TypeId;
                return false;
            }
            plugged.Add(created);
            slots.Add(slot);
            return true;
        }

        internal static Slot FindSlot(Item characterItem, string key)
        {
            if (characterItem == null || string.IsNullOrEmpty(key)) return null;
            try { return characterItem.Slots == null ? null : characterItem.Slots.GetSlot(key); }
            catch (Exception)
            {
                // 槽名不存在时 GetSlot 会抛：视作这个角色不支持这个槽位
                return null;
            }
        }

        private static void BindController(CharacterMainControl created, SkyIslandBossProfile profile, SkyIslandBossContext context)
        {
            try
            {
                switch (profile.Kind)
                {
                    case SkyIslandBossKind.Foreman:
                        created.gameObject.AddComponent<SkyIslandForemanBoss>().Bind(created, profile, context);
                        break;
                    case SkyIslandBossKind.Stargazer:
                        created.gameObject.AddComponent<SkyIslandStargazerChief>().Bind(created, profile, context);
                        break;
                    case SkyIslandBossKind.RootHunter:
                        created.gameObject.AddComponent<SkyIslandRootHunterBoss>().Bind(created, profile, context);
                        break;
                    case SkyIslandBossKind.Waylayer:
                        created.gameObject.AddComponent<SkyIslandWaylayerChief>().Bind(created, profile, context);
                        break;
                    case SkyIslandBossKind.Sickle:
                        created.gameObject.AddComponent<SkyIslandSickleBoss>().Bind(created, profile, context);
                        break;
                    case SkyIslandBossKind.Listener:
                        created.gameObject.AddComponent<SkyIslandListenerChief>().Bind(created, profile, context);
                        break;
                    case SkyIslandBossKind.Piper:
                        created.gameObject.AddComponent<SkyIslandPiperChief>().Bind(created, profile, context);
                        break;
                    case SkyIslandBossKind.Mirror:
                        created.gameObject.AddComponent<SkyIslandMirrorChief>().Bind(created, profile, context);
                        break;
                    case SkyIslandBossKind.Windhunter:
                        created.gameObject.AddComponent<SkyIslandWindhunterChief>().Bind(created, profile, context);
                        break;
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 招式控制器装配失败，只剩官方 AI：" + e.Message); }
        }

        /// <summary>这一组的带队是「只在夜里出来」的头目（蚋笛翁、镜中客）。遭遇 owner 装配时读一次。</summary>
        internal static bool IsNightLead(string encounterId)
        {
            return SkyIslandBossRules.LeadIsNightOnly(encounterId);
        }

        /// <summary>
        /// 这一组的带队现在该不该继续等夜：是夜限定头目、而此刻不是夜里（判夜只有一个口径：SkyIslandNight + SkyIslandLighting.ClockHours）。
        /// 遭遇 owner 据此白天只刷随从、把带队位留到夜里补刷；不在这里跳过装配——自动组每个位置每趟只刷一次。
        /// </summary>
        internal static bool LeadWaitsForNight(string encounterId)
        {
            return SkyIslandBossRules.LeadIsNightOnly(encounterId) && !SkyIslandNight.IsNight(SkyIslandLighting.ClockHours());
        }

        /// <summary>这一组整组换成另一阵营（断风游猎）。遭遇 owner 装配时读一次。</summary>
        internal static bool IsRivalFaction(string encounterId)
        {
            return SkyIslandBossRules.IsRivalFaction(encounterId);
        }

        internal static void RaiseDefeated(SkyIslandBossProfile profile, Vector3 position)
        {
            Action<SkyIslandBossProfile, Vector3> handler = Defeated;
            if (handler == null || profile == null) return;
            try { handler(profile, position); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 击败回调失败：" + e.Message); }
        }

        // ====================================================================
        // 各位 Boss 共用的表现与伤害（不改 SkyIslandStormBoss；R2–R4 另有 SkyIslandBossProps）
        // ====================================================================

        /// <summary>
        /// 贴地预警圈：挂在地图根上、放在世界坐标 <paramref name="world"/>。半径由 <see cref="SetRing"/> 画，必须等于真实判定范围。
        /// 圈上挂 <see cref="SkyIslandBossRingFx"/>：圈内随蓄力长满的填充（计时读数）、出现淡入、收起淡出（<see cref="ReleaseRing"/>）。
        /// </summary>
        internal static LineRenderer CreateGroundRing(Transform root, Vector3 world)
        {
            LineRenderer line = SkyIslandGroundRing.Create(root, LocalOf(root, world));
            line.gameObject.name = "SkyIslandBossRing";
            try { SkyIslandBossRingFx.Attach(line); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 预警圈填充失败（只剩描边）：" + e.Message); }
            return line;
        }

        /// <summary>收起一个 Boss 贴地圈：挂了 <see cref="SkyIslandBossRingFx"/> 的淡出后自毁，其余直接销毁。调用方随即丢掉引用。</summary>
        internal static void ReleaseRing(LineRenderer line)
        {
            if (line == null) return;
            SkyIslandBossRingFx fx;
            if (line.TryGetComponent(out fx)) fx.Release();
            else UnityEngine.Object.Destroy(line.gameObject);
        }

        internal static void PlaceRing(LineRenderer line, Transform root, Vector3 world)
        {
            if (line != null) line.transform.localPosition = LocalOf(root, world);
        }

        private static Vector3 LocalOf(Transform root, Vector3 world)
        {
            Vector3 local = root != null ? root.InverseTransformPoint(world) : world;
            return local + Vector3.up * SkyIslandGroundRing.GroundLift;
        }

        /// <summary>
        /// 按半径与蓄力度（0..1）重画圈：蓄力只影响线宽与不透明度，**不影响半径**。
        /// 圈内的填充（<see cref="SkyIslandBossRingFx"/>）随蓄力从圆心长到判定半径，这是给玩家读「还剩多久」的，边界圈不动。
        /// </summary>
        internal static void SetRing(LineRenderer line, float radius, float charge, Color tint)
        {
            if (line == null) return;
            Color solid = new Color(tint.r, tint.g, tint.b, Mathf.Lerp(0.45f, 1f, charge));
            SkyIslandGroundRing.SetShape(line, radius, Mathf.Lerp(0.18f, 0.55f, charge), solid);
            SkyIslandBossRingFx fx;
            if (line.TryGetComponent(out fx)) fx.Charge(radius, charge, tint, solid);
        }

        /// <summary>
        /// 常驻的一块地（穗镰漫开之后的泥，VB-22）：圈压到 0.6 不透明、带宽 0.3，圈内铺满一层 <paramref name="surface"/> 色的面，
        /// 泥本身看得见。半径仍是判定半径。
        /// </summary>
        internal static void SetPatch(LineRenderer line, float radius, Color tint, Color surface)
        {
            if (line == null) return;
            Color solid = new Color(tint.r, tint.g, tint.b, 0.6f);
            SkyIslandGroundRing.SetShape(line, radius, 0.3f, solid);
            SkyIslandBossRingFx fx;
            if (line.TryGetComponent(out fx)) fx.Surface(radius, surface, solid);
        }

        /// <summary>两点直线（光柱、供能线）：借贴地圈的建造点拿共享材质，再改成世界坐标下的两点线，不另建材质。</summary>
        internal static LineRenderer StraightLine(Transform parent, string name, float width, Color color)
        {
            LineRenderer line = SkyIslandGroundRing.Create(parent, Vector3.zero);
            line.gameObject.name = name;
            line.transform.localRotation = Quaternion.identity;
            line.loop = false;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.alignment = LineAlignment.View;
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;
            return line;
        }

        /// <summary>
        /// 桩身光柱（供能桩 / 根桩，VB-24）：底部满宽、往上收到 35%，顶端淡到透明，不再是一根上下都硬断的平色条。
        /// 位置由调用方写（第 0 点在地面、第 1 点在顶端）。
        /// </summary>
        internal static LineRenderer ColumnLine(Transform parent, string name, float width, Color color)
        {
            LineRenderer line = StraightLine(parent, name, width, color);
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f));
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.Lerp(color, Color.white, 0.25f), 0f), new GradientColorKey(color, 0.6f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a * 0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
            line.colorGradient = gradient;
            line.numCapVertices = 0;
            return line;
        }

        /// <summary>
        /// 桩上的小灯（VB-24）：与光柱同色，强度 1.2、半径 2.5 m，0.2 s 淡入。挂在桩自己身上，桩被打空停用时一起灭。
        /// </summary>
        internal static void PylonLight(Transform parent, Color color)
        {
            try
            {
                GameObject go = new GameObject("PylonLight");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.up * 0.4f;
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.range = 2.5f;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                SkyIslandLightFade.FadeTo(light, 1.2f, 0.2f, false);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 桩灯失败：" + e.Message); }
        }

        /// <summary>把一个点吸附到本图地面上（向下射线 + 墙体胶囊检查，口径同遭遇落点）。</summary>
        internal static bool SnapToGround(Vector3 point, SkyIslandBossContext context, float clearance, out Vector3 ground)
        {
            ground = point;
            if (context == null) return false;
            RaycastHit hit;
            if (!Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out hit, 7f, context.GroundMask, QueryTriggerInteraction.Ignore))
                return false;
            if (context.Root != null && !hit.transform.IsChildOf(context.Root)) return false;
            if (clearance > 0f && Physics.CheckCapsule(hit.point + Vector3.up * 0.6f, hit.point + Vector3.up * 1.5f, clearance,
                GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore)) return false;
            ground = hit.point;
            return true;
        }

        /// <summary>
        /// 范围伤害，口径照抄 `SkyIslandStormBoss.Detonate`：官方爆炸（无距离衰减，圈内吃满）、`canHurtSelf:false`
        /// （官方默认 true 时 selfTeam=Teams.all，同阵营的桩与随从也会被炸）、buff/effect 通道不计武器击杀。
        ///
        /// 表现（VB-21，判定半径与数值一字不动）：
        /// - <paramref name="blast"/>：真的是爆炸的招式（星焰、星火）留官方火球（normal）；镰扫、落石、换位、冲步、伏击、绊索传 false，
        ///   走 custom（官方分支不生成任何特效）——手雷火球和这些招式毫无关系，而且大小固定、不跟判定半径走；
        /// - <paramref name="shake"/>：官方震屏强度（30 m 内生效）。一轮齐落的几圈只给第一发，其余传 0；
        /// - 伤害结算后在原地放 <see cref="SkyIslandImpactFx.Play"/>：一圈先闪再扩散淡出的余波 + 圈沿扬尘，颜色取招式自己的预警色。
        /// </summary>
        internal static void Detonate(CharacterMainControl source, Vector3 origin, float radius, float damageValue, bool blast, float shake, Color tint)
        {
            if (source == null || LevelManager.Instance == null || LevelManager.Instance.ExplosionManager == null) return;
            DamageInfo damage = new DamageInfo(source);
            damage.damageValue = damageValue;
            damage.isExplosion = true;
            damage.isFromBuffOrEffect = true;
            damage.fromWeaponItemID = 0;
            ExplosionFxTypes fx = blast ? ExplosionFxTypes.normal : ExplosionFxTypes.custom;
            LevelManager.Instance.ExplosionManager.CreateExplosion(origin, radius, damage, fx, shake, false);
            SkyIslandImpactFx.Play(source.transform.parent, origin, radius, tint, 0);
        }

        /// <summary>这件专属装备是否已经不起作用：没穿、被换掉、或耐久打空（官方耐久归零后属性修饰也失效）。</summary>
        internal static bool PieceBroken(Item worn, int typeId)
        {
            return worn == null || worn.TypeID != typeId || (worn.UseDurability && worn.Durability <= 0f);
        }

#if BOSSRUSH_DEV
        /// <summary>
        /// Dev 演练 SKY_DRILL_BOSS_LOADOUT 专用：只做名字、配装、缩放与专属掉落组件，**不挂招式控制器**（不放星焰、不立桩），
        /// 让演练在玩家身边安全地核对「穿上全套 → 结算只留一件」。配装失败时 <paramref name="reason"/> 写原因，照常返回掉落组件。
        /// </summary>
        internal static SkyIslandBossLoot DevLoadoutForDrill(CharacterMainControl created, SkyIslandBossProfile profile, out string reason)
        {
            reason = null;
            if (created == null || profile == null) { reason = "character_or_profile_missing"; return null; }
            if (created.GetComponent<SkyIslandBossMark>() == null) created.gameObject.AddComponent<SkyIslandBossMark>();
            ApplyIdentity(created, profile);
            string equipReason;
            if (!TryEquip(created, profile, out equipReason)) reason = "equip_failed:" + equipReason;
            ApplyScale(created, profile);
            SkyIslandBossLoot loot = created.gameObject.AddComponent<SkyIslandBossLoot>();
            loot.Bind(created, profile, SkyIslandLootTables.StableHash(profile.Id));
            return loot;
        }
#endif

        /// <summary>由 SkyIslandRuntimeModule.OnDestroy 调用：清掉静态事件的残留订阅与主角穿戴快照。</summary>
        internal static void ResetStaticCaches()
        {
            Defeated = null;
            SkyIslandBossGearWorn.Reset();
            SkyIslandImpactFx.ResetStaticCaches();
        }
    }
}
