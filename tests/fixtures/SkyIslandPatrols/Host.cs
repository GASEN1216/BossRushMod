using System;
using System.Collections.Generic;
using BossRush;

// 只替代 Actor/异步结果的所有权，不包含调度判据或状态机。
internal sealed class Actor
{
    internal int HP = 100;
    internal readonly object Equipment = new object();
    internal bool Active, Destroyed;
    internal int DestroyCalls;
    internal void Destroy()
    {
        if (Destroyed) return;
        Destroyed = true;
        Active = false;
        DestroyCalls++;
    }
}

internal sealed class Request
{
    internal readonly int Slot, Generation;
    internal Request(int slot, int generation) { Slot = slot; Generation = generation; }
}

internal sealed class Host
{
    internal readonly SkyIslandPatrolSchedule Schedule;
    internal readonly Dictionary<int, Actor> Actors = new Dictionary<int, Actor>();
    internal Host(int slots, int limit) { Schedule = new SkyIslandPatrolSchedule(slots, limit); }
    internal Request Begin(int slot)
    {
        return Schedule.TryReserve(slot) ? new Request(slot, Schedule.Generation) : null;
    }
    internal bool Complete(Request request, Actor actor, bool activate)
    {
        bool accepted = Schedule.CompleteSpawn(request.Slot, request.Generation, activate);
        if (!accepted) { actor.Destroy(); return false; }
        Actors.Add(request.Slot, actor);
        actor.Active = activate;
        return true;
    }
    internal bool Suspend(int slot)
    {
        if (!Schedule.Suspend(slot)) return false;
        Actors[slot].Active = false;
        return true;
    }
    internal bool Activate(int slot)
    {
        if (!Schedule.TryActivate(slot)) return false;
        Actors[slot].Active = true;
        return true;
    }
    internal bool Defeat(int slot)
    {
        if (!Schedule.MarkDefeated(slot)) return false;
        Actor actor;
        if (Actors.TryGetValue(slot, out actor)) { actor.Destroy(); Actors.Remove(slot); }
        return true;
    }
    internal void Close()
    {
        Schedule.Close();
        foreach (Actor actor in Actors.Values) actor.Destroy();
        Actors.Clear();
    }
}
