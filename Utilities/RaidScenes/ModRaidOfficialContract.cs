using System;
using Duckov;
using Duckov.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// WIRE+：只读验证 Mod 独立出击地图的官方场景合同，不替代死亡、角色恢复或存档实现
    /// （2026-11-05 照 `SkyIslandOfficialContract` 抽成通用版，判据一致；天空岛仍用自己的那份）。
    /// </summary>
    internal static class ModRaidOfficialContract
    {
        internal static bool VerifyBeforeActivation(ModRaidProfile profile, Scene scene, GameObject services, GameObject world, out string reason)
        {
            reason = null;
            string map = MapName(profile);
            try
            {
                if (!scene.IsValid() || !scene.isLoaded || services == null || world == null || services == world ||
                    services.scene.handle != scene.handle || world.scene.handle != scene.handle || services.activeSelf)
                    return Fail(map + "：独立关卡必须包含尚未激活的关卡根节点与本场景地形", out reason);
                LevelConfig config = services.GetComponent<LevelConfig>();
                MultiSceneCore core = services.GetComponent<MultiSceneCore>();
                if (config == null || !config.enabled || core == null || !core.enabled)
                    return Fail(map + "：独立关卡配置或 MultiSceneCore 缺失/禁用", out reason);
                // 这两项由租约在激活前注入，作者场景里本来就是空的；与「包坏了」分开报。
                if (config.timeOfDayConfig == null || config.startBuffPrefabs == null)
                    return Fail(map + "：独立关卡天气或起始 Buff 未注入", out reason);
                if (core.SubScenes == null || core.SubScenes.Count != 1 || core.SubScenes[0] == null ||
                    core.SubScenes[0].sceneID != profile.Identity.SceneId)
                    return Fail(map + "：必须声明唯一的自身子场景", out reason);
                SubSceneEntry entry = core.SubScenes[0];
                if (entry.cachedLocations == null || entry.cachedTeleporters == null)
                    return Fail(map + "：子场景位置表未装配", out reason);
                SceneLocationsProvider[] providers = services.GetComponentsInChildren<SceneLocationsProvider>(true);
                if (providers.Length != 1 || !providers[0].enabled || providers[0].gameObject.scene.handle != scene.handle)
                    return Fail(map + "：必须包含唯一且启用的真实 SceneLocationsProvider", out reason);
                Transform spawn = world.transform.Find(profile.SpawnMarkerPath);
                Transform location = providers[0].GetLocation(profile.SpawnLocation);
                if (spawn == null || location == null || (spawn.position - location.position).sqrMagnitude > 0.0001f)
                    return Fail(map + "：官方出生层级与地形出生点不一致", out reason);
                int spawnEntries = 0;
                foreach (SubSceneEntry.Location cached in entry.cachedLocations)
                {
                    if (cached == null) return Fail(map + "：位置表存在空记录", out reason);
                    if (cached.path != profile.SpawnLocation) continue;
                    spawnEntries++;
                    if ((cached.position - spawn.position).sqrMagnitude > 0.0001f)
                        return Fail(map + "：出生缓存与真实位置不一致", out reason);
                }
                if (spawnEntries != 1) return Fail(map + "：出生记录缺失或重复", out reason);
                foreach (string path in profile.RequiredWorldPaths)
                    if (world.transform.Find(path) == null) return Fail(map + "：场景缺少 " + path, out reason);
                Transform nav = world.transform.Find("Navigation");
                MeshFilter filter = nav == null ? null : nav.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable ||
                    filter.sharedMesh.vertexCount == 0 || filter.sharedMesh.vertexCount > 4095)
                    return Fail(map + "：导航网格缺失、不可读或超出官方顶点上限", out reason);
                return true;
            }
            catch (Exception e) { return Fail(map + "：激活前合同验证异常：" + e.Message, out reason); }
        }

        internal static bool Verify(ModRaidProfile profile, Scene scene, out string reason)
        {
            reason = null;
            string map = MapName(profile);
            string sceneId = profile.Identity.SceneId;
            try
            {
                if (!scene.IsValid() || !scene.isLoaded || SceneManager.GetActiveScene().handle != scene.handle)
                    return Fail(map + "：独立场景尚未成为活跃场景", out reason);
                LevelManager level = LevelManager.Instance;
                LevelConfig config = LevelConfig.Instance;
                MultiSceneCore core = MultiSceneCore.Instance;
                if (level == null || config == null || core == null)
                    return Fail(map + "：独立关卡缺少 LevelConfig、LevelManager 或 MultiSceneCore", out reason);
                if (level.gameObject.scene.handle != scene.handle || config.gameObject.scene.handle != scene.handle
                    || core.gameObject.scene.handle != scene.handle)
                    return Fail(map + "：关卡服务仍属于其他地图，拒绝借用原场景服务", out reason);
                if (!LevelManager.AfterInit || !LevelManager.LevelInited)
                    return Fail(map + "：官方关卡初始化尚未完成", out reason);
                if (!level.IsRaidMap || level.IsBaseLevel)
                    return Fail(map + "：必须使用独立 Raid 规则", out reason);
                // SpawnTomb 只决定「墓碑箱 / 原地掉落」，存档只看 SaveCharacter；「死亡不掉落」类 Mod 关掉 SpawnTomb 照常放行。
                if (!LevelConfig.SaveCharacter)
                    return Fail(map + "：官方角色保存被关闭（SaveCharacter=false），撤离和死亡都没法写回存档", out reason);
                if (!LevelConfig.SpawnTomb)
                    Debug.LogWarning(profile.Identity.LogTag + " SpawnTomb=false（可能装了死亡不掉落类 Mod）：死亡不生成墓碑，按该设置继续");
                if (config.timeOfDayConfig == null || config.startBuffPrefabs == null)
                    return Fail(map + "：官方时间配置或起始 Buff 列表缺失", out reason);
                if (level.CharacterCreator == null || level.InputManager == null || level.GameCamera == null
                    || level.GameCamera.renderCamera == null)
                    return Fail(map + "：官方角色、输入或相机服务缺失", out reason);
                CharacterMainControl player = level.MainCharacter;
                if (player == null || player != CharacterMainControl.Main || player.CharacterItem == null
                    || player.Health == null || player.gameObject.scene.handle != scene.handle)
                    return Fail(map + "：主角未由本图官方关卡重新创建", out reason);
                if (!string.Equals(MultiSceneCore.ActiveSubSceneID, sceneId, StringComparison.Ordinal))
                    return Fail(map + "：子图身份不一致，死亡记录无法安全归属", out reason);
                Scene? activeSubScene = MultiSceneCore.ActiveSubScene;
                if (!activeSubScene.HasValue || activeSubScene.Value.handle != scene.handle)
                    return Fail(map + "：官方子场景未绑定当前地图实例", out reason);
                if (SceneInfoCollection.GetSceneInfo(sceneId) == null || SceneLocationsProvider.GetProviderOfScene(scene) == null)
                    return Fail(map + "：地图身份或出生点提供器缺失", out reason);
                if (DeadBodyManager.Instance == null)
                    return Fail(map + "：官方墓碑管理器未就绪", out reason);
                return true;
            }
            catch (Exception e)
            {
                return Fail(map + "：独立场景合同验证异常：" + e.Message, out reason);
            }
        }

        private static string MapName(ModRaidProfile profile)
        {
            try { return profile.DisplayName != null ? profile.DisplayName() : profile.Identity.SceneId; }
            catch (Exception) { return profile.Identity.SceneId; }
        }

        private static bool Fail(string message, out string reason)
        {
            reason = message;
            return false;
        }
    }
}
