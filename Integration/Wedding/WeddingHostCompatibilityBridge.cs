using System;
using System.Reflection;
using BossRush.Utils;
using UnityEngine;

namespace BossRush
{
    /// <summary>保留旧 ModBehaviour 入口并把 Wedding 调用转交唯一运行时模块。</summary>
    public partial class ModBehaviour
    {
        // Other legacy building partials still compile as ModBehaviour and historically
        // shared these private helpers from WeddingBuildingInjector.cs.
        private static Type FindGameType(string fullTypeName) { return BuildingInjectionHelper.FindGameType(fullTypeName); }
        private static Type GetBuildingManagerType() { return BuildingInjectionHelper.GetBuildingManagerType(); }
        private static MethodInfo GetBuildingManagerAnyMethod() { return BuildingInjectionHelper.GetBuildingManagerAnyMethod(); }
        private static MethodInfo GetBuildingDataMethod() { return BuildingInjectionHelper.GetBuildingDataMethod(); }
        private static Type GetBuildingType() { return BuildingInjectionHelper.GetBuildingType(); }
        private static PropertyInfo GetBuildingIdProperty() { return BuildingInjectionHelper.GetBuildingIdProperty(); }
        private static void AssignBuildingContainerField(FieldInfo field, Component buildingComp, Transform container)
        {
            BuildingInjectionHelper.AssignBuildingContainerField(field, buildingComp, container);
        }

        public bool HasWeddingBuildingPlaced() { return WeddingRuntime != null && WeddingRuntime.HasWeddingBuildingPlaced(); }
        public Transform GetWeddingNpcTransform() { return WeddingRuntime != null ? WeddingRuntime.GetWeddingNpcTransform() : null; }
        public Transform TrySpawnMarriedNpcAtWeddingPoint() { return WeddingRuntime != null ? WeddingRuntime.TrySpawnMarriedNpcAtWeddingPoint() : null; }
        public bool CanCurrentSpouseFollowPlayer(string npcId) { return WeddingRuntime != null && WeddingRuntime.CanCurrentSpouseFollowPlayer(npcId); }
        public bool IsSpouseFollowerInstance(string npcId, Transform npcTransform) { return WeddingRuntime != null && WeddingRuntime.IsSpouseFollowerInstance(npcId, npcTransform); }
        public bool ShouldShowSpouseFollowOption(string npcId, Transform npcTransform) { return WeddingRuntime != null && WeddingRuntime.ShouldShowSpouseFollowOption(npcId, npcTransform); }
        public bool ShouldShowSpouseDivorceOption(string npcId, Transform npcTransform) { return WeddingRuntime != null && WeddingRuntime.ShouldShowSpouseDivorceOption(npcId, npcTransform); }
        public bool ShouldShowSpouseHomeOption(string npcId, Transform npcTransform) { return WeddingRuntime != null && WeddingRuntime.ShouldShowSpouseHomeOption(npcId, npcTransform); }
        public float AdjustDialogueStayDurationForSpouseFollow(string npcId, Transform npcTransform, float requestedStayDuration)
        {
            return WeddingRuntime != null ? WeddingRuntime.AdjustDialogueStayDurationForSpouseFollow(npcId, npcTransform, requestedStayDuration) : requestedStayDuration;
        }
        public bool TryStartSpouseFollowingPlayer(string npcId) { return WeddingRuntime != null && WeddingRuntime.TryStartSpouseFollowingPlayer(npcId); }
        public bool TryHandleSpouseFollowRequest(string npcId, Transform dialogueTarget)
        {
            return WeddingRuntime != null && WeddingRuntime.TryHandleSpouseFollowRequest(npcId, dialogueTarget);
        }
        public bool SendSpouseHome(string npcId, bool showMessage = true)
        {
            return WeddingRuntime != null && WeddingRuntime.SendSpouseHome(npcId, showMessage);
        }
        public void HandleSpouseFollowAffinityLoss(string npcId)
        {
            if (WeddingRuntime != null) WeddingRuntime.HandleSpouseFollowAffinityLoss(npcId);
        }
        public void ScheduleRestoreFollowingSpouse(string expectedSceneName, string context)
        {
            if (WeddingRuntime != null) WeddingRuntime.ScheduleRestoreFollowingSpouse(expectedSceneName, context);
        }
        public void RefreshSpouseInteractionOptionsForNpc(string npcId)
        {
            if (WeddingRuntime != null) WeddingRuntime.RefreshSpouseInteractionOptionsForNpc(npcId);
        }
        public bool IsWeddingNpcInstance(Transform npcTransform)
        {
            return WeddingRuntime != null && WeddingRuntime.IsWeddingNpcInstance(npcTransform);
        }
        public void HandleDivorceNpcRelocation(string npcId)
        {
            if (WeddingRuntime != null) WeddingRuntime.HandleDivorceNpcRelocation(npcId);
        }

        public void InitWeddingBuilding()
        {
            if (WeddingRuntime != null) WeddingRuntime.InitWeddingBuilding();
        }
        internal void TryInitializeWeddingBuildingEarly()
        {
            if (WeddingRuntime != null) WeddingRuntime.TryInitializeWeddingBuildingEarly();
        }
        public void CleanupWeddingBuilding()
        {
            if (WeddingRuntime != null) WeddingRuntime.CleanupWeddingBuilding();
        }
        public void RestoreWeddingBuildingNPC()
        {
            if (WeddingRuntime != null) WeddingRuntime.RestoreWeddingBuildingNPC();
        }
        internal void RequestBaseBuildingAreaRepaint(string source)
        {
            if (WeddingRuntime != null) WeddingRuntime.RequestBaseBuildingAreaRepaint(source);
        }
        internal GameObject GetSpouseInstance(string spouseNpcId)
        {
            return WeddingRuntime != null ? WeddingRuntime.GetSpouseInstance(spouseNpcId) : null;
        }

        internal GameObject GetWeddingGoblinNpcInstance() { return goblinNPCInstance; }
        internal GameObject GetWeddingNurseNpcInstance() { return nurseNPCInstance; }
        internal void DestroyWeddingGoblinNpc() { DestroyGoblinNPC(); }
        internal void DestroyWeddingNurseNpc() { DestroyNurseNPC(); }
        internal void SpawnWeddingGoblinNpc(Vector3? position, bool stayStillOnSpawn, bool forceSpawn)
        {
            SpawnGoblinNPC(position, stayStillOnSpawn, forceSpawn);
        }
        internal void SpawnWeddingNurseNpc(Vector3? position, bool stayStillOnSpawn, bool forceSpawn)
        {
            SpawnNurseNPC(position, stayStillOnSpawn, forceSpawn);
        }
    }
}
