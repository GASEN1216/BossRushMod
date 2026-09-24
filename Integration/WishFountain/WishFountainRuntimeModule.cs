using System.Collections;
using System.Reflection;
using BossRush.Utils;
using UnityEngine;

namespace BossRush
{
    /// <summary>许愿台建筑运行时状态与宿主生命周期归属。</summary>
    internal sealed partial class WishFountainRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour _owner;
        private bool _builtEventSubscribed;
        private bool _destroyedEventSubscribed;

        public override string ModuleName { get { return "WishFountain"; } }
        internal bool HasAssetBundle { get { return starwishAssetBundle != null; } }
        internal GameObject ModelPrefab { get { return starwishModelPrefab; } }

        public override void OnAwake(ModBehaviour owner)
        {
            _owner = owner;
        }

        public override void OnDestroy()
        {
            CleanupWishFountainBuilding();
            _owner = null;
        }

        private Coroutine StartCoroutine(IEnumerator routine)
        {
            return _owner != null ? _owner.StartCoroutine(routine) : null;
        }

        private void StopCoroutine(Coroutine coroutine)
        {
            if (_owner != null && coroutine != null)
            {
                _owner.StopCoroutine(coroutine);
            }
        }

        private static void DevLog(string message) { ModBehaviour.DevLog(message); }
        private static bool IsBaseHubSceneName(string sceneName) { return ModBehaviour.IsBaseHubSceneName(sceneName); }

        private void RequestBaseBuildingAreaRepaint(string source)
        {
            if (_owner != null)
            {
                _owner.RequestBaseBuildingAreaRepaint(source);
            }
        }

        private static System.Type FindGameType(string fullTypeName)
        {
            return BuildingInjectionHelper.FindGameType(fullTypeName);
        }

        private static System.Type GetBuildingManagerType() { return BuildingInjectionHelper.GetBuildingManagerType(); }
        private static MethodInfo GetBuildingManagerAnyMethod() { return BuildingInjectionHelper.GetBuildingManagerAnyMethod(); }
        private static MethodInfo GetBuildingDataMethod() { return BuildingInjectionHelper.GetBuildingDataMethod(); }
        private static System.Type GetBuildingType() { return BuildingInjectionHelper.GetBuildingType(); }
        private static PropertyInfo GetBuildingIdProperty() { return BuildingInjectionHelper.GetBuildingIdProperty(); }
        private static void AssignBuildingContainerField(FieldInfo field, Component buildingComp, Transform container)
        {
            BuildingInjectionHelper.AssignBuildingContainerField(field, buildingComp, container);
        }
    }
}
