// ============================================================================
// DuckNpcFactory.cs - 捏脸 NPC 生成核心
// ============================================================================
// 模块说明：
//   用官方角色管线 + 官方捏脸数据凭空造一个 NPC，不需要任何 AssetBundle。
//
//   这条链不是新发明的，是**死亡亡魂已经跑通的那条**：
//     LoadOrCreateCharacterItemInstance → CharacterCreator.CreateCharacter(item, 底模, pos, rot)
//     → CharacterModel.SetFaceFromData(捏脸数据)
//   见 Integration/DeathWraith/DeathWraithSpawnFlow.cs。本文件把它提炼成通用工厂，
//   并把亡魂那边对 LevelManager.characterModel 的反射换成官方 public 的
//   GameplayDataSettings.Prefabs.DefaultCharacterModel。
//
//   为什么**不**走 CharacterRandomPreset.CreateCharacterAsync：
//     - preset 的 characterModel / facePreset / aiController 都是 private SerializeField，
//       要改必须反射，凭空多三个反射绑定面。
//     - preset 路径会带上 AI、掉落箱、经验、灵魂、距离休眠一整套敌人语义，
//       交互型 NPC 需要的是「一个会站着的鸭子」，把这些再逐项关掉是负工作量。
//     - characterPreset 留 null 是安全的：官方所有消费方（HealthBar、
//       QuestTask_KillCount、BDSManager、DamageInfo）都有 null 守卫，
//       副作用只是没有官方名条和等级图标 —— 而交互 NPC 本来就用自己的气泡。
//
//   本文件只负责「造出来并交出去」，不管刷新点、不管场景门控、不管交互内容。
// ============================================================================

using System;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 一次捏脸 NPC 生成请求。字段默认值按「友好交互 NPC」调好，
    /// 调用方通常只需要填 NpcId / Face / Position。
    /// </summary>
    internal sealed class DuckNpcSpawnRequest
    {
        /// <summary>NPC 标识符，会写进运行时标记，供交互和好感度反查。</summary>
        public string NpcId;

        /// <summary>捏脸数据。必须已经过 DuckNpcFaceCodec.Normalize 处理。</summary>
        public CustomFaceSettingData Face;

        /// <summary>是否应用 Face。false 时沿用底模自带的脸。</summary>
        public bool HasFace;

        public Vector3 Position;

        /// <summary>朝向。零向量时朝 Vector3.back（与官方 CreateCharacterAsync 的兜底一致）。</summary>
        public Vector3 Facing;

        /// <summary>阵营。默认玩家方 —— 这是清场豁免和不被攻击的根基。</summary>
        public Teams Team = Teams.player;

        /// <summary>模型缩放。1 为原尺寸。</summary>
        public float ModelScale = 1f;

        /// <summary>是否无敌。交互 NPC 默认无敌，避免被流弹打死后服务消失。</summary>
        public bool Invincible = true;

        /// <summary>是否显示官方血条。交互 NPC 默认不显示。</summary>
        public bool ShowHealthBar = false;

        /// <summary>死亡是否掉灵魂方块。交互 NPC 默认不掉。</summary>
        public bool HasSoul = false;

        /// <summary>
        /// 底模覆盖。null 时用 GameplayDataSettings.Prefabs.DefaultCharacterModel。
        /// 传进来的底模必须挂了 CustomFaceInstance，否则 Face 会被静默忽略。
        /// </summary>
        public CharacterModel BaseModelOverride = null;

        /// <summary>
        /// 是否把角色登记到当前场景（多场景父物体归位）。
        /// 登记时**始终**关掉官方距离休眠，见 DuckNpcFactory 内注释。
        /// </summary>
        public bool RegisterRelatedScene = true;

        /// <summary>
        /// 是否挤压其他角色（官方 CharacterRandomPreset.pushCharacter 的等价项）。
        /// </summary>
        /// <remarks>
        /// 这是裸造路线相对官方 CreateCharacterAsync **唯一漏调的一处配置**：
        /// 官方每次生怪都会 movementControl.SetPushCharacter(preset.pushCharacter)，
        /// 我们之前一次都没调过，于是 ECM2 的 AllowPushCharacters 停在预制体默认值上。
        /// 2026-09-04 的物理诊断显示探针与官方角色的碰撞体配置完全一致
        /// （同 layer、同 enabled、同 kinematic），差异只剩这一处，
        /// 所以把它显式暴露出来，而不是继续让它听天由命。
        /// </remarks>
        public bool PushCharacter = true;
    }

    /// <summary>
    /// 捏脸 NPC 生成工厂。
    /// </summary>
    internal static class DuckNpcFactory
    {
        private const string LogPrefix = "[DuckNpc]";

        /// <summary>
        /// 造一个捏脸 NPC。失败返回 null 并已完成自清理（不留半成品角色）。
        /// </summary>
        internal static async UniTask<CharacterMainControl> SpawnAsync(DuckNpcSpawnRequest request)
        {
            if (request == null)
            {
                ModBehaviour.DevLog(LogPrefix + " 生成请求为空");
                return null;
            }

            LevelManager level = ResolveLevelManager();
            if (level == null)
            {
                return null;
            }

            CharacterModel modelPrefab = ResolveBaseModel(request);
            if (modelPrefab == null)
            {
                ModBehaviour.DevLog(LogPrefix + " 取不到底模预制体，放弃生成");
                return null;
            }

            Item characterItem = await CreateCharacterItemAsync(level);
            if (characterItem == null)
            {
                return null;
            }

            Vector3 facing = request.Facing.sqrMagnitude > 0f
                ? request.Facing.normalized
                : Vector3.back;

            CharacterMainControl npc = null;
            try
            {
                npc = await level.CharacterCreator.CreateCharacter(
                    characterItem,
                    modelPrefab,
                    request.Position,
                    Quaternion.LookRotation(facing, Vector3.up));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " CharacterCreator.CreateCharacter 异常: " + e.Message);
                DestroyItemTree(characterItem);
                return null;
            }

            if (npc == null)
            {
                // CreateCharacter 内部在 item 为空时会自毁角色并返回 null；
                // 这里 item 非空，走到 null 说明官方链路出错，物品树仍需自己收。
                ModBehaviour.DevLog(LogPrefix + " CharacterCreator.CreateCharacter 返回空");
                DestroyItemTree(characterItem);
                return null;
            }

            // 从这里开始，任何失败都不能把角色留在场上当孤儿。
            try
            {
                ConfigureSpawnedNpc(npc, request, facing);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " 配置 NPC 失败，销毁半成品: " + e.Message);
                Despawn(npc);
                return null;
            }

            ModBehaviour.DevLog(LogPrefix + " 生成成功 id=" + Safe(request.NpcId)
                + ", 底模=" + modelPrefab.name
                + ", team=" + request.Team
                + ", pos=" + request.Position);
            return npc;
        }

        /// <summary>
        /// 销毁一个捏脸 NPC。幂等，可对 null 调用。
        /// </summary>
        internal static void Despawn(CharacterMainControl npc)
        {
            if (npc == null)
            {
                return;
            }

            try
            {
                // CharacterMainControl.OnDestroy 会自己 DestroyTree 物品树并退订事件，
                // 这里只需要销毁 GameObject，不要手动拆物品树。
                GameObject go = npc.gameObject;
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 销毁 NPC 失败: " + e.Message);
            }
        }

        // ====================================================================
        // 各步骤
        // ====================================================================

        private static LevelManager ResolveLevelManager()
        {
            try
            {
                LevelManager level = LevelManager.Instance;
                if (level == null || level.CharacterCreator == null)
                {
                    ModBehaviour.DevLog(LogPrefix + " LevelManager/CharacterCreator 不可用");
                    return null;
                }
                return level;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " 取 LevelManager 异常: " + e.Message);
                return null;
            }
        }

        private static CharacterModel ResolveBaseModel(DuckNpcSpawnRequest request)
        {
            if (request.BaseModelOverride != null)
            {
                return request.BaseModelOverride;
            }
            return DuckNpcFaceCatalog.ResolveDefaultCharacterModel();
        }

        private static async UniTask<Item> CreateCharacterItemAsync(LevelManager level)
        {
            try
            {
                int typeId = GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID;
                Item item = await level.CharacterCreator.LoadOrCreateCharacterItemInstance(typeId);
                if (item == null)
                {
                    ModBehaviour.DevLog(LogPrefix + " 角色物品实例创建失败");
                    return null;
                }

                // 与官方 CreateCharacterAsync 对齐：物品树归主场景，
                // 否则子场景卸载时会把 NPC 的物品树一起带走。
                try
                {
                    MultiSceneCore.MoveToMainScene(item.gameObject);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(LogPrefix + " [WARNING] 物品树归主场景失败: " + e.Message);
                }

                return item;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " 角色物品实例创建异常: " + e.Message);
                return null;
            }
        }

        private static void ConfigureSpawnedNpc(
            CharacterMainControl npc,
            DuckNpcSpawnRequest request,
            Vector3 facing)
        {
            ApplyFace(npc, request);
            ApplyScene(npc, request);
            ApplyMovement(npc, request);
            ApplyTeamAndSurvivability(npc, request);
            ApplyModelScale(npc, request);
            AttachMarker(npc, request);

            try
            {
                npc.SetPosition(request.Position);
                npc.SetAimPoint(request.Position + facing * 10f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 落点/朝向设置失败: " + e.Message);
            }
        }

        private static void ApplyFace(CharacterMainControl npc, DuckNpcSpawnRequest request)
        {
            if (!request.HasFace)
            {
                return;
            }

            try
            {
                if (npc.characterModel == null)
                {
                    ModBehaviour.DevLog(LogPrefix + " [WARNING] characterModel 为空，捏脸未应用");
                    return;
                }

                // 底模没挂 CustomFaceInstance 时 SetFaceFromData 会静默返回 ——
                // 这是最容易「以为生效了其实没生效」的地方，显式记一行。
                if (npc.characterModel.CustomFace == null)
                {
                    ModBehaviour.DevLog(LogPrefix + " [WARNING] 底模没有 CustomFaceInstance，捏脸数据被忽略: "
                        + npc.characterModel.name);
                    return;
                }

                npc.characterModel.SetFaceFromData(request.Face);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 应用捏脸数据失败: " + e.Message);
            }
        }

        private static void ApplyScene(CharacterMainControl npc, DuckNpcSpawnRequest request)
        {
            if (!request.RegisterRelatedScene)
            {
                return;
            }

            try
            {
                int sceneIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                // setActiveByPlayerDistance 必须传 false。传 true 会把 NPC 注册进官方
                // SetActiveByPlayerDistance，该组件每帧无条件 SetActive(距玩家 < 100m)，
                // 玩家跑远后 NPC 被静默关掉 —— 服务凭空消失且没有任何报错。
                // 这正是 AGENTS.md 第 14 节记录过的那个坑，别在这里放开。
                npc.SetRelatedScene(sceneIndex, false);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 场景登记失败: " + e.Message);
            }
        }

        private static void ApplyMovement(CharacterMainControl npc, DuckNpcSpawnRequest request)
        {
            try
            {
                if (npc.movementControl == null)
                {
                    ModBehaviour.DevLog(LogPrefix + " [WARNING] movementControl 为空，跳过挤压配置");
                    return;
                }
                npc.movementControl.SetPushCharacter(request.PushCharacter);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 设置角色挤压失败: " + e.Message);
            }
        }

        private static void ApplyTeamAndSurvivability(CharacterMainControl npc, DuckNpcSpawnRequest request)
        {
            try
            {
                npc.SetTeam(request.Team);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 设置阵营失败: " + e.Message);
            }

            try
            {
                Health health = npc.Health;
                if (health == null)
                {
                    return;
                }

                health.hasSoul = request.HasSoul;
                health.showHealthBar = request.ShowHealthBar;
                if (request.Invincible)
                {
                    health.SetInvincible(true);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 设置生存属性失败: " + e.Message);
            }
        }

        private static void ApplyModelScale(CharacterMainControl npc, DuckNpcSpawnRequest request)
        {
            if (request.ModelScale <= 0f || Mathf.Approximately(request.ModelScale, 1f))
            {
                return;
            }

            try
            {
                Transform modelRoot = npc.modelRoot;
                if (modelRoot == null)
                {
                    return;
                }
                float scale = request.ModelScale;
                modelRoot.localScale = new Vector3(scale, scale, scale);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 模型缩放失败: " + e.Message);
            }
        }

        private static void AttachMarker(CharacterMainControl npc, DuckNpcSpawnRequest request)
        {
            try
            {
                DuckNpcRuntimeMarker marker = npc.gameObject.GetComponent<DuckNpcRuntimeMarker>();
                if (marker == null)
                {
                    marker = npc.gameObject.AddComponent<DuckNpcRuntimeMarker>();
                }
                marker.Bind(request.NpcId, npc);
            }
            catch (Exception e)
            {
                // 标记挂不上是硬失败：没有它，Mode H 清场会把 NPC 当原生敌人销毁。
                // 抛出去让 SpawnAsync 走销毁半成品分支，而不是留一个会被清掉的 NPC。
                throw new InvalidOperationException("挂载 DuckNpcRuntimeMarker 失败: " + e.Message, e);
            }
        }

        private static void DestroyItemTree(Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                item.DestroyTree();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 回收角色物品树失败: " + e.Message);
            }
        }

        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "(空)" : value;
        }
    }
}
