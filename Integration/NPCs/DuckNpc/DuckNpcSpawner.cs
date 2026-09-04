// ============================================================================
// DuckNpcSpawner.cs - 蓝图 → 实际 NPC
// ============================================================================
// 模块说明：
//   把一条 DuckNpcBlueprint 变成场上一只活着的 NPC。这是**新增 NPC 的唯一入口**：
//
//       await DuckNpcSpawner.SpawnAsync("my_npc_id", pos, facing);
//
//   职责边界：
//     - DuckNpcRegistry  管"有哪些 NPC、它们长什么样"（数据）
//     - DuckNpcFactory   管"怎么把一个角色造出来"（官方管线）
//     - DuckNpcOutfitter 管"穿什么"
//     - 本文件           管"把上面三个按蓝图串起来"，并保证失败不留半成品
//
//   本文件不管刷新点、不管场景门控、不管交互内容 —— 那是模块层的事。
// ============================================================================

using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 蓝图驱动的捏脸 NPC 生成入口。
    /// </summary>
    internal static class DuckNpcSpawner
    {
        private const string LogPrefix = "[DuckNpc]";

        /// <summary>
        /// 按蓝图 id 生成 NPC。失败返回 null（已自清理）。
        /// </summary>
        internal static async UniTask<CharacterMainControl> SpawnAsync(
            string blueprintId,
            Vector3 position,
            Vector3 facing)
        {
            DuckNpcBlueprint blueprint;
            if (!DuckNpcRegistry.TryGet(blueprintId, out blueprint))
            {
                ModBehaviour.DevLog(LogPrefix + " 找不到蓝图: " + blueprintId);
                return null;
            }

            return await SpawnAsync(blueprint, position, facing);
        }

        /// <summary>
        /// 按蓝图对象生成 NPC。失败返回 null（已自清理）。
        /// </summary>
        internal static async UniTask<CharacterMainControl> SpawnAsync(
            DuckNpcBlueprint blueprint,
            Vector3 position,
            Vector3 facing)
        {
            if (blueprint == null)
            {
                return null;
            }

            DuckNpcSpawnRequest request = BuildRequest(blueprint, position, facing);

            CharacterMainControl npc = await DuckNpcFactory.SpawnAsync(request);
            if (npc == null)
            {
                return null;
            }

            // 装备失败不算生成失败：一个没穿装备的 NPC 仍然可用，
            // 而把已经造好的角色因为一件头盔插不进去就销毁，是过度反应。
            await ApplyEquipmentAsync(npc, blueprint);

            return npc;
        }

        private static DuckNpcSpawnRequest BuildRequest(
            DuckNpcBlueprint blueprint,
            Vector3 position,
            Vector3 facing)
        {
            DuckNpcSpawnRequest request = new DuckNpcSpawnRequest();
            request.NpcId = blueprint.id;
            request.Position = position;
            request.Facing = facing;
            request.Team = blueprint.ResolveTeam();
            request.ModelScale = blueprint.modelScale;
            request.Invincible = blueprint.invincible;
            request.ShowHealthBar = blueprint.showHealthBar;
            request.PushCharacter = blueprint.pushCharacter;

            // 底模：解析失败时 BaseModelOverride 留 null，工厂会回落官方默认底模。
            // ResolveModelByName 已经在内部把"找不到"和"没挂 CustomFace"都记过日志。
            if (!string.IsNullOrEmpty(blueprint.baseModel))
            {
                request.BaseModelOverride = DuckNpcFaceCatalog.ResolveModelByName(blueprint.baseModel);
            }

            CustomFaceSettingData face;
            if (DuckNpcRegistry.TryResolveFace(blueprint, out face))
            {
                request.Face = face;
                request.HasFace = true;
            }

            return request;
        }

        private static async UniTask ApplyEquipmentAsync(CharacterMainControl npc, DuckNpcBlueprint blueprint)
        {
            if (!blueprint.HasEquipment)
            {
                return;
            }

            try
            {
                DuckNpcOutfitResult result;

                // 固定装备优先：写了明确 TypeID 就说明作者要的是确定造型，
                // 此时忽略 randomEquipment，不做"先随机再覆盖"这种半确定行为。
                if (blueprint.equipmentTypeIds != null && blueprint.equipmentTypeIds.Length > 0)
                {
                    result = await DuckNpcOutfitter.EquipByTypeIdsAsync(npc, blueprint.equipmentTypeIds);
                }
                else
                {
                    result = await DuckNpcOutfitter.EquipRandomSeededAsync(
                        npc, blueprint.equipmentSlots, blueprint.equipmentSeed);
                }

                ModBehaviour.DevLog(LogPrefix + " " + blueprint.id
                    + " 装备: 成功 " + result.EquippedCount + " 件, 跳过 " + result.Skipped.Count + " 项");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] " + blueprint.id + " 穿装备异常: " + e.Message);
            }
        }

        /// <summary>
        /// 销毁一只捏脸 NPC。幂等。
        /// </summary>
        internal static void Despawn(CharacterMainControl npc)
        {
            DuckNpcFactory.Despawn(npc);
        }
    }
}
