using UnityEngine;
using System.Collections.Generic;

// 仅替换游戏角色、宿主路径与迷雾边界；模型、材质包、动画、粒子与渲染使用真实生产实现和 Unity。
public sealed class CharacterMainControl : MonoBehaviour
{
    public readonly FieldVisualCharacterModel characterModel = new FieldVisualCharacterModel();
    public static CharacterMainControl Main;
    public bool IsMainCharacter, isVehicle, protectVehicleDriver;
    public bool Dashing;
    public Teams Team { get { return Teams.player; } }
    public Health Health;
    public void AddBuff(Duckov.Buffs.Buff buff, CharacterMainControl source, int value) { }
}
public sealed class FieldVisualCharacterModel { public void SyncHiddenToMainCharacter() { } }
public sealed class CharacterSubVisuals : MonoBehaviour
{
    public void SetRenderers() { }
    public void SetCharacter(CharacterMainControl character) { }
}
namespace Duckov
{
    // FMOD 本身不进入渲染夹具；真实 Unity 音源读取的设置由这些可控总线提供。
    public static class AudioManager
    {
        public sealed class Bus { public float Volume = 1f; public bool Mute; }
        private static readonly Bus master = new Bus(), sfx = new Bus();
        public static Bus GetBus(string path) { return path == "Master" ? master : path == "Master/SFX" ? sfx : null; }
    }
}
namespace BossRush
{
    internal sealed class ModBehaviour
    {
        internal static string GetModPath() { return FieldAdventureVisualRegression.Configuration.modRoot; }
        internal static void DevLog(string message) { Debug.Log(message); }
    }
    internal static class ResourceBundleLoader { internal static AssetBundle LoadFromFile(string path) { return AssetBundle.LoadFromFile(path); } }
    internal static class BossRushUI
    {
        internal static bool Paused, HudHidden;
        internal static bool IsGamePaused() { return Paused; }
        internal static bool IsOfficialHudHidden() { return HudHidden; }
        internal static float SmoothStep(float value) { return Mathf.SmoothStep(0f, 1f, value); }
    }
    internal static class ModEntityFogVisibility
    {
        internal static readonly Dictionary<GameObject, Transform> Samples = new Dictionary<GameObject, Transform>();
        internal static void Attach(GameObject root, Transform samplePoint = null) { Samples[root] = samplePoint != null ? samplePoint : root.transform; }
    }
}
