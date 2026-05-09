using UnityEngine.SceneManagement;

namespace BossRush
{
    internal struct SceneRuntimeContext
    {
        public readonly Scene Scene;
        public readonly LoadSceneMode LoadMode;
        public readonly string SceneName;
        public readonly int SceneBuildIndex;

        public SceneRuntimeContext(Scene scene, LoadSceneMode loadMode)
        {
            Scene = scene;
            LoadMode = loadMode;
            SceneName = scene.name;
            SceneBuildIndex = scene.buildIndex;
        }
    }
}
