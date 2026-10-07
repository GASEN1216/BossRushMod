using System;
using System.Collections.Generic;
using BossRush;

// 唯一生产算法是逐字抽取的 ConfigureNavigation；这里只模拟 Unity inactive 子树查询与调用回执。
internal static class NavigationRegression
{
    internal static void Run()
    {
        var root = new UnityEngine.GameObject();
        var child = new UnityEngine.GameObject();
        root.Children.Add(child);
        var character = root.Add<CharacterMainControl>();
        var ai = child.Add<AICharacterController>(); ai.forceTracePlayerDistance = 70f;
        var seeker = child.Add<Pathfinding.Seeker>();
        var path = child.Add<AI_PathControl>();
        var owner = new SkyIslandPatrols(17);
        root.SetActive(false);
        if (character.GetComponentInChildren<AICharacterController>() != null)
            throw new Exception("ASSERT[inactive_child_query_boundary]");
        try { owner.ConfigureForTest(character); }
        catch (Exception e) { throw new Exception("ASSERT[inactive_child_navigation] initial staged patrol", e); }
        if (seeker.CancelCalls != 1 || seeker.graphMask.Value != 17 || path.StopCalls != 1
            || ai.forceTracePlayerDistance != 0f || !root.activeSelf)
            throw new Exception("ASSERT[staged_navigation_wired]");
        // 同一角色远区挂起后恢复，旧路径必须撤销，再接入当前图；不能把失败累计成该槽永久死亡。
        root.SetActive(false); ai.forceTracePlayerDistance = 80f; seeker.graphMask = new Pathfinding.GraphMask(2);
        try { owner.ConfigureForTest(character); }
        catch (Exception e) { throw new Exception("ASSERT[inactive_child_navigation] suspended patrol", e); }
        if (seeker.CancelCalls != 2 || path.StopCalls != 2 || seeker.graphMask.Value != 17
            || ai.forceTracePlayerDistance != 0f || !root.activeSelf)
            throw new Exception("ASSERT[suspended_navigation_wired]");
        var missing = new UnityEngine.GameObject().Add<CharacterMainControl>();
        bool rejected = false;
        try { owner.ConfigureForTest(missing); } catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new Exception("ASSERT[missing_navigation_rejected]");
        Console.WriteLine("SkyIslandPatrolNavigation: PASS (production staged and suspended navigation, inactive child query)");
    }
}

namespace UnityEngine
{
    internal class Component
    {
        internal GameObject gameObject;
        internal T GetComponentInChildren<T>(bool includeInactive = false) where T : Component
        { return gameObject.Find<T>(includeInactive, true, true); }
        internal T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>();
            gameObject.Collect(result, includeInactive, true, true);
            return result.ToArray();
        }
    }
    internal sealed class GameObject
    {
        internal bool activeSelf = true;
        internal readonly List<GameObject> Children = new List<GameObject>();
        private readonly List<Component> components = new List<Component>();
        internal void SetActive(bool active) { activeSelf = active; }
        internal T Add<T>() where T : Component, new()
        { var component = new T { gameObject = this }; components.Add(component); return component; }
        internal T Find<T>(bool includeInactive, bool root, bool parentActive) where T : Component
        {
            bool active = activeSelf && parentActive;
            if (!root && !includeInactive && !active) return null;
            foreach (Component component in components) if (component is T) return (T)component;
            foreach (GameObject child in Children)
            { T result = child.Find<T>(includeInactive, false, active); if (result != null) return result; }
            return null;
        }
        internal void Collect<T>(List<T> result, bool includeInactive, bool root, bool parentActive) where T : Component
        {
            bool active = activeSelf && parentActive;
            if (!root && !includeInactive && !active) return;
            foreach (Component component in components) if (component is T) result.Add((T)component);
            foreach (GameObject child in Children) child.Collect(result, includeInactive, false, active);
        }
    }
}
namespace Pathfinding
{
    internal struct GraphMask { internal int Value; internal GraphMask(int value) { Value = value; } }
    internal sealed class Seeker : UnityEngine.Component
    { internal GraphMask graphMask; internal int CancelCalls; internal void CancelCurrentPathRequest() { CancelCalls++; } }
}
internal enum Teams { wolf }
internal sealed class CharacterMainControl : UnityEngine.Component { internal void SetTeam(Teams team) { } }
internal sealed class AICharacterController : UnityEngine.Component { internal float forceTracePlayerDistance; }
internal sealed class AI_PathControl : UnityEngine.Component { internal int StopCalls; internal void StopMove() { StopCalls++; } }
namespace BossRush
{
    internal static class SpawnedEnemyActivationHelper
    { internal static void ReleaseFromPlayerDistanceSleep(CharacterMainControl character) { character.gameObject.SetActive(true); } }
    internal sealed partial class SkyIslandPatrols
    {
        private readonly Pathfinding.GraphMask graphMask;
        internal SkyIslandPatrols(int mask) { graphMask = new Pathfinding.GraphMask(mask); }
        internal void ConfigureForTest(CharacterMainControl character) { ConfigureNavigation(character); }
    }
}
