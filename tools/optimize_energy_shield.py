"""对 Unity 导出的能量盾网格做带 UV 误差的 QEM 简化；高模源保留。

离线依赖 pymeshlab==2025.7.post1，仅安装到构建目录。输出是 Unity 坐标，
不经 FBX 再变换。几何距离由 MeshLab Hausdorff 采样测量，不替代实机目检。
"""
from pathlib import Path
import argparse
import json
import sys


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--work', type=Path, required=True)
    p.add_argument('--triangles', type=int, default=40000)
    args=p.parse_args()
    sys.path.insert(0,str(args.work/'python'))
    import numpy as np
    import pymeshlab as ml
    data=json.loads((args.work/'shield-source.json').read_text(encoding='utf8'))
    assert data['submeshes']==1, 'material slots require individual simplification'
    v=np.array(data['vertices'],dtype=np.float64)
    f=np.array(data['triangles'],dtype=np.int32).reshape(-1,3)
    uv=np.array(data['uv'],dtype=np.float64)
    assert len(uv) in (0,len(v)) and np.isfinite(v).all() and np.isfinite(uv).all()
    textured=len(uv)>0
    mesh=ml.Mesh(vertex_matrix=v,face_matrix=f,**({'w_tex_coords_matrix':uv[f].reshape(-1,2)} if textured else {}))
    ms=ml.MeshSet(); ms.add_mesh(mesh,'source')
    # Unity splits vertices at normal/UV boundaries. Wedge UVs keep seams while
    # coincident geometry can be welded before simplifying the actual surface.
    ms.meshing_remove_duplicate_vertices()
    ms.meshing_remove_unreferenced_vertices()
    ms.generate_copy_of_current_mesh()
    options=dict(targetfacenum=args.triangles,qualitythr=0.5,preserveboundary=True,
        preservenormal=True,optimalplacement=False,planarquadric=True)
    if textured:
        ms.meshing_decimation_quadric_edge_collapse_with_texture(extratcoordw=10,**options)
    else:
        ms.meshing_decimation_quadric_edge_collapse(preservetopology=True,**options)
    result=ms.current_mesh()
    points=result.vertex_matrix(); faces=result.face_matrix()
    tex=result.wedge_tex_coord_matrix() if textured else np.zeros((len(faces)*3,2))
    normals=result.vertex_normal_matrix()
    # Split only where the resulting UV requires it; keep smooth surface normals.
    corners=np.concatenate((points[faces].reshape(-1,3),tex,normals[faces].reshape(-1,3)),axis=1).astype(np.float32)
    unique,indices=np.unique(corners,axis=0,return_inverse=True)
    lo,hi=points.min(axis=0),points.max(axis=0)
    size=v.max(axis=0)-v.min(axis=0)
    bounds_error=float(np.max(np.maximum(abs(lo-v.min(axis=0)),abs(hi-v.max(axis=0)))/np.maximum(size,1e-8)))
    assert len(faces)<=args.triangles and bounds_error<=0.01, (len(faces),bounds_error)
    metrics={'source_vertices':len(v),'source_triangles':len(f),'vertices':len(unique),'triangles':len(faces),
        'bounds_relative_error':bounds_error,'source_has_uv':textured,'uv_finite':bool(np.isfinite(tex).all())}
    for sampled,target,label in [(0,1,'source_to_reduced'),(1,0,'reduced_to_source')]:
        distances=ms.get_hausdorff_distance(sampledmesh=sampled,targetmesh=target,samplenum=100000,
            samplevert=True,sampleedge=False,sampleface=True)
        metrics[label]={k:float(val) for k,val in distances.items()}
        assert float(distances['max'])/float(np.linalg.norm(size))<0.01, distances
    (args.work/'shield-reduced.json').write_text(json.dumps({'vertices':unique[:,:3].tolist(),
        'uv':unique[:,3:5].tolist(),'normals':unique[:,5:8].tolist(),'triangles':indices.tolist()},separators=(',',':')),encoding='utf8')
    (args.work/'shield-metrics.json').write_text(json.dumps(metrics,indent=2),encoding='utf8')
    print(json.dumps(metrics,indent=2))


if __name__=='__main__':
    main()
