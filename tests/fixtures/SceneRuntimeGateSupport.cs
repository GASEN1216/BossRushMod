// 仅供隔离 fixture 编译 SceneRuntimeGate；不模拟这些非本次测试目标的游戏服务。
public sealed class SceneLoader
{
    public static SceneLoader Instance;
    public static bool IsSceneLoading;
    public Duckov.Utilities.SceneReference defaultCurtainScene;
}
namespace Duckov.Scenes
{
    public sealed class MultiSceneCore
    {
        public static MultiSceneCore Instance;
        public bool IsLoading;
    }
}
namespace Duckov.Utilities
{
    public sealed class SceneReference { public string Name; }
    public sealed class SceneManagementSettings
    {
        public SceneReference MainMenuScene = new SceneReference { Name = "MainMenu" };
        public SceneReference FailLoadingScreenScene = new SceneReference { Name = "FailLoadingScreen" };
        public SceneReference EvacuateScreenScene = new SceneReference { Name = "EvacuateScreen" };
    }
    public static class GameplayDataSettings
    {
        public static SceneManagementSettings SceneManagement = new SceneManagementSettings();
    }
}
