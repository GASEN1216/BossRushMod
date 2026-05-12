// ============================================================================
// CharacterOnDeadPatch.cs - 角色死亡前缀 Patch
// ============================================================================
// 说明：在角色死亡时处理额外追加掉落（霜之哀伤蓝色 Boss、噬魂挽歌）
// ============================================================================

using HarmonyLib;

namespace BossRush
{
    [HarmonyPatch(typeof(CharacterMainControl), "OnDead")]
    public static class BossRushCharacterOnDeadPatch
    {
        [HarmonyPrefix]
        public static void Prefix(CharacterMainControl __instance)
        {
            FrostmourneBlueBossDropHandler.TryHandleBlueBossDeath(__instance);
            PhantomWitchScytheBossDropHandler.TryHandlePhantomWitchDeath(__instance);
        }
    }
}
