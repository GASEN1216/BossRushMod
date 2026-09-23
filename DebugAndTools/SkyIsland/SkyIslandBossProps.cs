// ============================================================================
// SkyIslandBossProps.cs - 头目 / 岛主招式共用的场上小件与动作（R2–R4）
// ============================================================================
// 七位新 Boss 的招式都由这几样拼出来，不另起第二套（残星匠首与瞭台观星手 R1 已实机验过，原样不动）：
// - 可以打的轻量接收体（根桩、倒影）：配方照供能桩 / 云蚋——先失活，伤害接收体层非触发球 + 运动学刚体 +
//   DamageReceiver（useSimpleHealth）+ HealthSimpleBase，再激活；死亡看 activeSelf（DebugAndTools/SkyIsland/AGENTS.md §4）。
// - 倒影：把角色此刻的蒙皮网格烤成静态网格，不克隆角色（克隆会把身上的装备 Item / ItemAgent 一起复制出来）。
// - 玩家减速：官方 WalkSpeed / RunSpeed 的 PercentageAdd（RuntimeStatModifierTracker；官方只有这两个与 Moveability 是移动 stat，
//   "MoveSpeed" 是动画参数）。到时、出圈、Boss 倒下、销毁都要摘。
// - 换位：先掐寻路请求（StopMove 不取消在途请求，回调会把旧路径装回来），暂停官方 AI，再落位、同步物理；
//   硬直结束由 BossAIController.Resume 重新认人。
// - 面罩 / 耳机「被打穿」：官方 Health.Hurt 只在 Raid 图上磨头盔与护甲，这两个槽照同一口径（暴击、非真实伤害、
//   不无视护甲时按 armorBreak 扣）由招式控制器在自己的受击回调里扣。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    /// <summary>玩家身上的一种减速（绊索、烂泥）：同一来源只挂一份、同幅度只顺延；到时或出圈就摘，Boss 倒下与销毁时由控制器 Release。</summary>
    internal sealed class SkyIslandPlayerSlow
    {
        private readonly List<ZombieModeAttributeModifierRecord> records = new List<ZombieModeAttributeModifierRecord>();
        private readonly object source = new object();
        private readonly string context;
        private float applied, until = -1f;

        internal SkyIslandPlayerSlow(string context) { this.context = context; }

        /// <summary>现在挂着没有。</summary>
        internal bool Active { get { return records.Count > 0; } }

        /// <summary>挂上 <paramref name="percent"/>（负数，-0.4 即慢四成）；<paramref name="seconds"/> ≤ 0 表示一直挂到 <see cref="Release"/>。</summary>
        internal void Apply(CharacterMainControl player, float percent, float seconds)
        {
            if (player == null || percent >= 0f) return;
            float now = Time.time;
            if (Active && Mathf.Approximately(applied, percent))
            {
                until = seconds > 0f ? Mathf.Max(until, now + seconds) : -1f;
                return;
            }
            Release();
            bool any = RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.WalkSpeed, percent, source, records, context);
            any |= RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.RunSpeed, percent, source, records, context);
            if (!any) return;
            applied = percent;
            until = seconds > 0f ? now + seconds : -1f;
        }

        /// <summary>按时摘（控制器的节流推进里调）。</summary>
        internal void Tick(float now)
        {
            if (Active && until > 0f && now >= until) Release();
        }

        internal void Release()
        {
            if (records.Count > 0) RuntimeStatModifierTracker.RemoveAll(records, context);
            applied = 0f;
            until = -1f;
        }
    }

    /// <summary>头目 / 岛主招式共用的场上小件与动作。全部是可失败的表现层：出错只记警告，不影响这场战斗能不能打完。</summary>
    internal static class SkyIslandBossProps
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        /// <summary>倒影最多烤这么多块渲染器（角色本体 + 挂点上的装备模型），挡住异常模型把一帧烤爆。</summary>
        private const int DecoyPartLimit = 32;

        /// <summary>
        /// 建一个可以打的轻量接收体，**未激活**：调用方挂好表现（圈、光柱、烤好的网格）之后调 <see cref="Activate"/>。
        /// 阵营跟 <paramref name="owner"/> 一致（换了阵营的 Boss 自己的范围伤害不炸自己的道具）。失败返回 null。
        /// </summary>
        internal static GameObject CreateReceiver(Transform root, string name, Vector3 world, float radius, float health, CharacterMainControl owner)
        {
            GameObject go = null;
            try
            {
                go = new GameObject(name);
                go.SetActive(false);
                if (root != null) go.transform.SetParent(root, true);
                go.transform.position = world;
                int layer = LayerMask.NameToLayer("DamageReceiver");
                if (layer >= 0) go.layer = layer;
                SphereCollider collider = go.AddComponent<SphereCollider>();
                collider.radius = radius;
                collider.isTrigger = false;
                Rigidbody body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                DamageReceiver receiver = go.AddComponent<DamageReceiver>();
                receiver.useSimpleHealth = true;
                if (receiver.OnHurtEvent == null) receiver.OnHurtEvent = new UnityEvent<DamageInfo>();
                if (receiver.OnDeadEvent == null) receiver.OnDeadEvent = new UnityEvent<DamageInfo>();
                // HealthSimpleBase.Awake 立刻取 dmgReceiver 的事件：整套在失活状态下装好，激活时一次到位。
                HealthSimpleBase simple = go.AddComponent<HealthSimpleBase>();
                simple.team = owner != null ? owner.Team : Teams.wolf;
                simple.maxHealthValue = health;
                simple.dmgReceiver = receiver;
                receiver.simpleHealth = simple;
                return go;
            }
            catch (Exception e)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                Debug.LogWarning("[SkyIslandBoss] 接收体创建失败：" + e.Message);
                return null;
            }
        }

        /// <summary>激活接收体并屏蔽它与主角、Boss 的物理接触（必须在 SetActive 之后：失活再激活会清掉忽略对）。</summary>
        internal static void Activate(GameObject receiver, CharacterMainControl owner)
        {
            if (receiver == null) return;
            try
            {
                receiver.SetActive(true);
                Collider collider = receiver.GetComponent<Collider>();
                IgnoreContact(collider, CharacterMainControl.Main);
                IgnoreContact(collider, owner);
                Physics.SyncTransforms();
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 接收体激活失败：" + e.Message); }
        }

        /// <summary>接收体还在不在（官方 HealthSimpleBase 打空后停用根物体）。</summary>
        internal static bool Alive(GameObject receiver)
        {
            return receiver != null && receiver.activeSelf;
        }

        private static void IgnoreContact(Collider collider, CharacterMainControl character)
        {
            if (collider == null || character == null) return;
            try
            {
                Collider other = character.GetComponent<Collider>();
                if (other != null) Physics.IgnoreCollision(collider, other, true);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 接收体接触屏蔽失败：" + e.Message); }
        }

        /// <summary>
        /// 倒影：把 <paramref name="source"/> 此刻的样子烤成静态网格挂在一个接收体下面（会吸准星、能打碎），**已激活**。
        /// 蒙皮网格用 BakeMesh 快照当前姿势（带缩放），不带骨骼、动画与脚本；挂点上的普通网格（装备模型）共享原网格。
        /// 共享原材料、只用 MaterialPropertyBlock 调色，不 new 材质；烤出的 Mesh 记进 <paramref name="baked"/>，由调用方在倒影销毁时 Destroy。
        /// </summary>
        internal static GameObject CreateDecoy(Transform root, CharacterMainControl source, float health, Color tint, List<Mesh> baked)
        {
            if (source == null || source.characterModel == null) return null;
            GameObject go = CreateReceiver(root, "SkyIslandMirrorDecoy", source.transform.position + Vector3.up * 1.0f, 0.55f, health, source);
            if (go == null) return null;
            try
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                int parts = 0;
                SkinnedMeshRenderer[] skins = source.characterModel.GetComponentsInChildren<SkinnedMeshRenderer>(false);
                for (int i = 0; i < skins.Length && parts < DecoyPartLimit; i++)
                {
                    SkinnedMeshRenderer skin = skins[i];
                    if (skin == null || !skin.enabled || skin.sharedMesh == null) continue;
                    Mesh mesh = new Mesh();
                    skin.BakeMesh(mesh, true);
                    if (baked != null) baked.Add(mesh);
                    AddDecoyPart(go.transform, skin.transform, mesh, skin.sharedMaterials, false, tint, block);
                    parts++;
                }
                MeshRenderer[] meshes = source.characterModel.GetComponentsInChildren<MeshRenderer>(false);
                for (int i = 0; i < meshes.Length && parts < DecoyPartLimit; i++)
                {
                    MeshRenderer renderer = meshes[i];
                    MeshFilter filter = renderer == null ? null : renderer.GetComponent<MeshFilter>();
                    if (renderer == null || !renderer.enabled || filter == null || filter.sharedMesh == null) continue;
                    AddDecoyPart(go.transform, renderer.transform, filter.sharedMesh, renderer.sharedMaterials, true, tint, block);
                    parts++;
                }
                if (parts == 0) throw new InvalidOperationException("角色模型上没有可烤的渲染器");
                Activate(go, source);
                return go;
            }
            catch (Exception e)
            {
                UnityEngine.Object.Destroy(go);
                Debug.LogWarning("[SkyIslandBoss] 倒影创建失败：" + e.Message);
                return null;
            }
        }

        private static void AddDecoyPart(Transform parent, Transform from, Mesh mesh, Material[] materials, bool copyScale, Color tint,
            MaterialPropertyBlock block)
        {
            GameObject part = new GameObject("DecoyPart");
            // 渲染层沿用源渲染器（角色层），不跟接收体走伤害接收体层——那一层相机不一定画。
            part.layer = from.gameObject.layer;
            part.transform.SetParent(parent, false);
            part.transform.SetPositionAndRotation(from.position, from.rotation);
            if (copyScale) part.transform.localScale = from.lossyScale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            block.Clear();
            Material first = materials != null && materials.Length > 0 ? materials[0] : null;
            if (first != null && first.HasProperty(BaseColorId)) block.SetColor(BaseColorId, tint);
            if (first != null && first.HasProperty(ColorId)) block.SetColor(ColorId, tint);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>销毁倒影与它烤出来的网格。</summary>
        internal static void DestroyDecoy(GameObject decoy, List<Mesh> baked)
        {
            if (decoy != null) UnityEngine.Object.Destroy(decoy);
            if (baked == null) return;
            for (int i = 0; i < baked.Count; i++) if (baked[i] != null) UnityEngine.Object.Destroy(baked[i]);
            baked.Clear();
        }

        /// <summary>
        /// 换位落点：掐掉在途寻路请求 → 暂停官方 AI（给了控制器时）→ 官方 SetPosition 落位，回读不到位就直接贴 transform
        /// （口径同 PhantomWitch 的瞬移）→ 同步物理。暂停的由调用方在硬直结束时 Resume（Resume 自带重新认人）。
        /// </summary>
        internal static bool Teleport(CharacterMainControl character, Vector3 target, BossAIController pause)
        {
            if (character == null) return false;
            try
            {
                AI_PathControl path = character.GetComponentInChildren<AI_PathControl>();
                if (path != null && path.seeker != null) path.seeker.CancelCurrentPathRequest(true);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 换位掐寻路失败：" + e.Message); }
            try { if (pause != null && !pause.IsPaused) pause.Pause(); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 换位暂停 AI 失败：" + e.Message); }
            bool moved = false;
            try { character.SetPosition(target); moved = true; }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 换位 SetPosition 失败：" + e.Message); }
            try
            {
                if (!moved || (character.transform.position - target).sqrMagnitude > 0.0001f) character.transform.position = target;
                character.SetMoveInput(Vector2.zero);
                Physics.SyncTransforms();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandBoss] 换位落位失败：" + e.Message);
                return false;
            }
        }

        /// <summary>让一名 AI 立刻盯上主角（被喊来的帮手、没暂停就挪了位置的随从）。</summary>
        internal static void NoticePlayer(CharacterMainControl character)
        {
            try
            {
                AICharacterController ai = character == null ? null : character.GetComponentInChildren<AICharacterController>();
                CharacterMainControl main = CharacterMainControl.Main;
                if (ai == null || main == null || main.mainDamageReceiver == null) return;
                ai.searchedEnemy = main.mainDamageReceiver;
                ai.noticed = true;
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 帮手认人失败：" + e.Message); }
        }

        /// <summary>
        /// 面罩 / 耳机被打穿（只给 Boss 用）：照官方 `Health.Hurt` 磨头盔的口径——暴击、不是真实伤害、不无视护甲时按 armorBreak 扣耐久。
        /// 招式控制器在自己的 `Health.OnHurtEvent` 回调里调；耐久打空之后 <see cref="SkyIslandBossForge.PieceBroken"/> 为 true。
        /// </summary>
        internal static void WearSoftPiece(CharacterMainControl boss, DamageInfo damage, string slotKey, int typeId)
        {
            // DamageInfo 是值类型，没有 null 可判。
            if (boss == null || damage.crit <= 0 || damage.damageType == DamageTypes.realDamage || damage.ignoreArmor) return;
            try
            {
                Item worn = WornIn(boss, slotKey);
                if (worn == null || worn.TypeID != typeId || !worn.UseDurability) return;
                worn.Durability = Mathf.Max(0f, worn.Durability - Mathf.Max(0f, damage.armorBreak));
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 面罩 / 耳机磨损失败：" + e.Message); }
        }

        /// <summary>角色某个槽位里现在是哪件（槽名不存在、读不到时为 null）。</summary>
        internal static Item WornIn(CharacterMainControl character, string slotKey)
        {
            if (character == null) return null;
            Slot slot = SkyIslandBossForge.FindSlot(character.CharacterItem, slotKey);
            return slot == null ? null : slot.Content;
        }

        /// <summary>作者标记的世界坐标（逃点、洞口这类现成的点）。标记不在时返回 false。</summary>
        internal static bool TryMarker(Transform root, string name, out Vector3 position)
        {
            position = Vector3.zero;
            if (root == null || string.IsNullOrEmpty(name)) return false;
            Transform marker = root.Find(name);
            if (marker == null) return false;
            position = marker.position;
            return true;
        }

        /// <summary>在 <paramref name="point"/> 附近找能站的地面：先试原点，再在 <paramref name="jitter"/> 米的圈上试八个方向。</summary>
        internal static bool SnapNear(Vector3 point, SkyIslandBossContext context, float clearance, float jitter, out Vector3 ground)
        {
            if (SkyIslandBossForge.SnapToGround(point, context, clearance, out ground)) return true;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.25f;
                Vector3 probe = point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * jitter;
                if (SkyIslandBossForge.SnapToGround(probe, context, clearance, out ground)) return true;
            }
            ground = point;
            return false;
        }

        /// <summary>
        /// 一组贴地预警圈按时蓄力（协程，给招式控制器 `yield return`）。只改线宽与不透明度，**不改半径**：半径就是判定范围。
        /// <paramref name="aborted"/> 为 true 时提前结束；圈由调用方销毁。
        /// </summary>
        internal static IEnumerator ChargeRings(List<LineRenderer> lines, float radius, float seconds, Color tint, Func<bool> aborted)
        {
            float started = Time.time;
            float total = Mathf.Max(0.01f, seconds);
            while (Time.time - started < seconds && (aborted == null || !aborted()))
            {
                float charge = Mathf.Clamp01((Time.time - started) / total);
                for (int i = 0; i < lines.Count; i++) SkyIslandBossForge.SetRing(lines[i], radius, charge, tint);
                yield return null;
            }
        }

        /// <summary>
        /// 收起一组圈并清空列表：Boss 圈 0.16 s 淡出后自毁（<see cref="SkyIslandBossForge.ReleaseRing"/>），不再一帧消失。
        /// 结算那一刻的「闪一下再扩散」另由 <see cref="SkyIslandImpactFx.Play"/> 在原地放一圈余波。
        /// </summary>
        internal static void DestroyRings(List<LineRenderer> lines)
        {
            if (lines == null) return;
            for (int i = 0; i < lines.Count; i++) SkyIslandBossForge.ReleaseRing(lines[i]);
            lines.Clear();
        }
    }
}
