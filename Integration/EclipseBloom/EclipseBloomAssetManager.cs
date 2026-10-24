using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BossRush
{
    /// <summary>花冠作者资源按租约借用；实例仅拥有地面动态网格，不销毁共享材质与作者网格。</summary>
    internal sealed class EclipseBloomAssetManager : IDisposable
    {
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private bool _disposed;
        private bool _authoredLease;

        internal Material Obsidian { get; private set; }
        internal Material Gold { get; private set; }
        internal Mesh Petal { get; private set; }
        internal Mesh Core { get; private set; }
        internal Mesh Fragment { get; private set; }

        internal EclipseBloomAssetManager(CharacterMainControl source)
        {
            try
            {
                EclipseBloomAuthoredAssets.Acquire();
                _authoredLease = true;
                // 正式模型只来自 Blender MCP 作者源打出的资源包；缺失时取消生成，不能用未验收模型冒充。
                Petal = EclipseBloomAuthoredAssets.Petal;
                Core = EclipseBloomAuthoredAssets.Core;
                Fragment = EclipseBloomAuthoredAssets.Fragment;
                Obsidian = EclipseBloomAuthoredAssets.Obsidian;
                Gold = EclipseBloomAuthoredAssets.Gold;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal T Own<T>(T resource) where T : UnityEngine.Object
        {
            if (resource != null) _owned.Add(resource);
            return resource;
        }

        internal static MeshRenderer AddMesh(string name, Transform parent, Mesh mesh, Material material)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            if (material == null) renderer.enabled = false;
            return renderer;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = _owned.Count - 1; i >= 0; i--)
                if (_owned[i] != null) UnityEngine.Object.Destroy(_owned[i]);
            _owned.Clear();
            if (_authoredLease) EclipseBloomAuthoredAssets.Release();
            _authoredLease = false;
        }
    }
}
