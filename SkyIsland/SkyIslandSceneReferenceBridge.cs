using System;
using Cysharp.Threading.Tasks;
using Eflatun.SceneReference;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// WIRE+：天空岛的官方场景身份。补丁、GUID 表与初始化令牌由多图共用的 <see cref="ModRaidSceneBridge"/> 持有
    /// （2026-11-05 抽出，潮闸镇是第二张图）；这里只登记天空岛自己的身份并保留原有入口。
    /// BuildIndex 始终保留 Unity 返回的 -1；不向全局 Entries 注入会匹配所有 -1 的条目。
    /// </summary>
    internal static class SkyIslandSceneReferenceBridge
    {
        internal const string SceneId = "BossRush_SkyIsland";
        internal const string ScenePath = "Assets/SkyIsland/SkyIslandRaid.unity";
        internal const string SceneName = "SkyIslandRaid";
        internal const string SpawnLocation = "StartPoints/PlayerSpawn";
        private const string SceneGuid = "9b118e66b9444c87aaa49a5e36ba8c31";
        private static readonly ModRaidSceneIdentity Identity =
            new ModRaidSceneIdentity(SceneId, ScenePath, SceneGuid, "BossRush_SkyIsland_SceneName", InjectLocalization, "[SkyIsland]");

        internal static SceneReference SceneReference
        {
            get
            {
                if (!EnsureRegistered()) throw new InvalidOperationException("天空岛官方场景桥接尚未就绪");
                return ModRaidSceneBridge.ReferenceOf(Identity);
            }
        }

        /// <summary>这个场景是不是天空岛。热路径，判定顺序见 <see cref="ModRaidSceneBridge.IsScene"/>。</summary>
        internal static bool IsScene(Scene scene) { return ModRaidSceneBridge.IsScene(Identity, scene); }

        internal static bool IsActiveScene
        {
            get { return IsScene(SceneManager.GetActiveScene()); }
        }

        internal static bool EnsureRegistered() { return ModRaidSceneBridge.Register(Identity); }

        /// <summary>与场景注册分离；宿主切语言时可刷新名称，不重装场景补丁。</summary>
        internal static void InjectLocalization()
        {
            LocalizationHelper.InjectLocalization("BossRush_SkyIsland_SceneName", L10n.T("天空岛 · 晴岚群岛", "Sky Islands · Qinglan"));
        }

        internal static void BeginInitialization(object owner) { ModRaidSceneBridge.BeginInitialization(owner, Identity); }
        internal static void BindInitializationScene(object owner, Scene scene) { ModRaidSceneBridge.BindInitializationScene(owner, scene); }
        internal static void AbortInitialization(object owner, string reason) { ModRaidSceneBridge.AbortInitialization(owner, reason); }
        internal static void EndInitialization(object owner) { ModRaidSceneBridge.EndInitialization(owner); }
        internal static bool HasPendingInitializationLoad(object owner) { return ModRaidSceneBridge.HasPendingInitializationLoad(owner); }

        internal static void BeginLoadVisuals(object owner, SceneReference target, bool preservePreviousTargetSnapshot = false)
        { ModRaidSceneBridge.BeginLoadVisuals(owner, target, preservePreviousTargetSnapshot); }

        internal static UniTask EndLoadVisuals(object owner) { return ModRaidSceneBridge.EndLoadVisuals(owner); }

        /// <summary>注销天空岛身份；岛图仍在加载或未卸载时推迟到卸载后。最后一张 Mod 地图注销时才撤补丁。</summary>
        internal static void Shutdown() { ModRaidSceneBridge.Unregister(Identity); }
    }
}
