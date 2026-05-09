namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void TickDebugTools(float deltaTime, float unscaledDeltaTime)
        {
            UpdateFpsCounter();
            UpdateMapClickDebug();
            CheckBossPoolWindowHotkey();
            CheckItemSpawnerHotkey();
            CheckF3DebugCheatMenuHotkey();
            TickF3DebugCheatMenu();
        }

        internal void LateUpdateDebugTools()
        {
            BossPoolLateUpdate();
            NPCTeleportUILateUpdate();
            F3DebugCheatMenuLateUpdate();
        }
    }
}
