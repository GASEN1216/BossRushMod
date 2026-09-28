using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// 与本夹具的伤害合同无关的表现边界。生命周期调度由替身触发，Attach/OnDisable 使用生产实现。
namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r=r; this.g=g; this.b=b; this.a=a; }
    }
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        { return (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed) || ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object obj) { return ReferenceEquals(this, obj); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            GameObject go = value as GameObject;
            if (go != null) go.DestroyTree();
            else value.Destroyed = true;
        }
    }
    public class Component : Object { public GameObject gameObject; }
    public class MonoBehaviour : Component { }
    public partial class Transform
    {
        public GameObject gameObject;
        public Transform parent;
        public Vector3 localPosition;
        public void SetParent(Transform next, bool worldPositionStays)
        {
            if (parent != null) parent.gameObject.Children.Remove(gameObject);
            parent = next;
            if (parent != null) parent.gameObject.Children.Add(gameObject);
        }
    }
    public class GameObject : Object
    {
        public readonly Transform transform;
        public readonly List<GameObject> Children = new List<GameObject>();
        private readonly List<Component> components = new List<Component>();
        private bool active = true;
        public GameObject(string name) { transform = new Transform { gameObject=this }; }
        public T AddComponent<T>() where T : Component, new()
        { var value=new T { gameObject=this }; components.Add(value); return value; }
        public T GetComponent<T>() where T : Component
        { foreach (var value in components) if (value is T) return (T)value; return null; }
        public void SetActive(bool value)
        {
            if (active == value) return;
            active=value;
            if (value) return;
            foreach (var child in Children.ToArray()) child.SetActive(false);
            foreach (var component in components.ToArray())
            {
                MethodInfo disable=component.GetType().GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic);
                if (disable!=null) disable.Invoke(component,null);
            }
        }
        internal void DestroyTree()
        {
            foreach (var child in Children.ToArray()) child.DestroyTree();
            foreach (var component in components) component.Destroyed=true;
            Destroyed=true;
            if (transform.parent!=null) transform.parent.gameObject.Children.Remove(this);
        }
    }
    public class Material { }
    public class AnimationCurve { public static AnimationCurve Linear(float a,float b,float c,float d) { return new AnimationCurve(); } }
    public class TrailRenderer : Component
    {
        public float time,widthMultiplier,minVertexDistance;
        public int numCapVertices,numCornerVertices;
        public AnimationCurve widthCurve;
        public Material sharedMaterial;
        public Color startColor,endColor;
        public bool emitting,Cleared;
        public void Clear() { Cleared=true; }
    }
}
namespace BossRush
{
    internal enum ZombieModeRewardType { ProjectileRicochet }
    internal enum BossRushParticleShape { TrailStrip }
    internal enum BossRushFxBlend { Additive }
    internal static class BossRushFxKit
    {
        internal const float GainBright=1f;
        internal static bool Available=true;
        private static readonly Material shared=new Material();
        internal static Material GetShapeMaterial(BossRushParticleShape shape,BossRushFxBlend blend,float gain)
        { return Available ? shared : null; }
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        internal Color GetZombieModeRewardAccentColor(ZombieModeRewardType reward)
        { return new Color(0.2f,0.8f,0.6f,1f); }
    }
}
internal static class ProjectileVisualChecks
{
    internal static int Run(BossRush.ZombieModeRuntimeModule module)
    {
        Projectile bullet=LevelManager.Instance.BulletPool.Captured;
        if (bullet.gameObject.Children.Count!=1) { Console.WriteLine("FAIL production trail attaches to support projectile"); return 1; }
        TrailRenderer trail=bullet.gameObject.Children[0].GetComponent<TrailRenderer>();
        bullet.gameObject.SetActive(false);
        if (!trail.Cleared || !trail.Destroyed || bullet.gameObject.Children.Count!=0)
        { Console.WriteLine("FAIL production OnDisable clears and destroys support trail before pool reuse"); return 1; }
        bullet.gameObject.SetActive(true);
        if (bullet.gameObject.Children.Count!=0) { Console.WriteLine("FAIL pooled ordinary bullet inherits support trail"); return 1; }
        BossRush.BossRushFxKit.Available=false;
        bool spawned=module.Spawn(0.4f);
        BossRush.BossRushFxKit.Available=true;
        if (!spawned || LevelManager.Instance.BulletPool.Captured.gameObject.Children.Count!=0)
        { Console.WriteLine("FAIL missing visual material changes projectile gameplay"); return 1; }
        Console.WriteLine("PASS production support-trail cleanup and missing-material gameplay isolation");
        return 0;
    }
}
