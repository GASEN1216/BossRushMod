// Shared mutable Unity/Core boundary for the protocol's simulated peers. Actual Core/token
// derivation is independently executed by CoopInstalledApi/SceneIdentity and CoopSession.
using System;
namespace Duckov.Scenes
{
    internal sealed class MultiSceneCore
    {
        internal static MultiSceneCore Instance { get { return BossRush.BossRushCoop.Current == null ? null : BossRush.BossRushCoop.Current.Core; } }
        internal readonly SceneOwner gameObject = new SceneOwner();
        internal bool Destroyed;
        public static bool operator ==(MultiSceneCore a, MultiSceneCore b)
        { bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an == bn : ReferenceEquals(a, b); }
        public static bool operator !=(MultiSceneCore a, MultiSceneCore b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    internal sealed class SceneOwner { internal SceneValue scene = new SceneValue { handle = 1, isLoaded = true }; }
    internal struct SceneValue { internal int handle; internal bool isLoaded; }
}
