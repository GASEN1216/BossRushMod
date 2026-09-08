using System;
using System.Collections.Generic;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>仅拥有试验场自己的 A* 图；不替换、不清空、不扫描原图。</summary>
    internal sealed class ArenaPrototypeNavigation : IDisposable
    {
        private AstarPath owner;
        private NavMeshGraph graph;
        private readonly Dictionary<GraphNode, bool> originalWalkability = new Dictionary<GraphNode, bool>();
        internal GraphMask Mask { get { return GraphMask.FromGraph(graph); } }

        internal IEnumerator<Progress> BeginScan(Mesh mesh, Vector3 origin)
        {
            owner = AstarPath.active;
            if (owner == null || owner.data == null || owner.isScanning)
                throw new InvalidOperationException("A* 未就绪或正在扫描，请稍后重试");
            if (mesh == null || !mesh.isReadable)
                throw new InvalidOperationException("试验场导航网格缺失或不可读");
            // 当前游戏 A* 未启用 ASTAR_RECAST_LARGER_TILES，单块顶点最多 4095。
            if (mesh.vertexCount > 4095)
                throw new InvalidOperationException("导航资源顶点数 " + mesh.vertexCount + " 超过游戏上限 4095，请更新场景资源包");
            graph = owner.data.AddGraph<NavMeshGraph>();
            graph.name = "BossRush_ArenaPrototype";
            if (graph.graphIndex >= 32)
                throw new InvalidOperationException("A* 图掩码已满，不能隔离测试敌人");
            graph.sourceMesh = mesh;
            graph.offset = origin;
            graph.rotation = Vector3.zero;
            graph.scale = 1f;
            return owner.ScanAsync(graph).GetEnumerator();
        }

        internal int CountWalkableNodes()
        {
            int count = 0;
            if (graph != null)
                graph.GetNodes((Action<GraphNode>)(node => { if (node.Walkable) count++; }));
            return count;
        }

        /// <summary>只修改本租约图。按整块三角面包围盒相交封门，避免长三角形中心落在门外而漏通。</summary>
        internal void SetBlockedAreas(Bounds[] areas)
        {
            if (graph == null || owner == null) return;
            owner.AddWorkItem((IWorkItemContext context) =>
            {
                graph.GetNodes((Action<GraphNode>)(node =>
                {
                    bool original;
                    if (!originalWalkability.TryGetValue(node, out original))
                    { original = node.Walkable; originalWalkability.Add(node, original); }
                    TriangleMeshNode triangle = node as TriangleMeshNode;
                    Bounds bounds = new Bounds((Vector3)node.position, Vector3.zero);
                    if (triangle != null)
                    {
                        bounds = new Bounds((Vector3)triangle.GetVertex(0), Vector3.zero);
                        bounds.Encapsulate((Vector3)triangle.GetVertex(1));
                        bounds.Encapsulate((Vector3)triangle.GetVertex(2));
                    }
                    bool blocked = false;
                    foreach (Bounds area in areas) if (bounds.Intersects(area)) { blocked = true; break; }
                    node.Walkable = original && !blocked;
                }));
                context.SetGraphDirty(graph);
            });
            owner.FlushWorkItems();
        }

        public void Dispose()
        {
            if (graph != null && owner != null && owner.data != null)
                owner.data.RemoveGraph(graph);
            graph = null;
            owner = null;
            originalWalkability.Clear();
        }
    }
}
