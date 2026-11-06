"""Read the *shipped* scene bundles; compare indoor volumes, roof membership and layout (L2, no game/slot)."""
import argparse
import json
import math
from pathlib import Path

import UnityPy
import numpy as np
from UnityPy.helpers.MeshHelper import MeshHandler

ROOT = Path(__file__).resolve().parents[1]


class Scene:
    def __init__(self, path):
        self.env = UnityPy.load(str(path))
        self.nodes, self.named = {}, {}
        for obj in self.env.objects:
            if obj.type.name != "Transform":
                continue
            t = obj.read()
            g = t.m_GameObject.read()
            self.nodes[obj.path_id] = (g, t)
            self.named.setdefault(g.m_Name, []).append(obj.path_id)

    def children(self, root):
        return [p.path_id for p in self.nodes[root][1].m_Children]

    def descendants(self, root):
        yield root
        for t in self.children(root):
            yield from self.descendants(t)

    def components(self, root, kind):
        return [c.component for t in self.descendants(root) for c in self.nodes[t][0].m_Component
                if c.component.type.name == kind]

    def unique(self, name):
        vals = self.named[name]
        assert len(vals) == 1, (name, vals)
        return vals[0]

    def indexed(self, name):
        children = self.children(self.unique(name))
        result = {self.nodes[t][0].m_Name: t for t in children}
        assert len(result) == len(children)
        return result


def close(left, right):
    return all(math.isclose(x, y, abs_tol=.002) for x, y in zip(left, right))


def xyz(v):
    return (v.x, v.y, v.z)


def footprint(s, node, center, size):
    g, t = s.nodes[node]
    colliders = [r.component for r in g.m_Component if r.component.type.name == 'BoxCollider']
    assert len(colliders) == 1, g.m_Name
    box = colliders[0].read()
    assert (g.m_Layer == 2 and box.m_Enabled and box.m_IsTrigger and close(xyz(box.m_Center), (0, 0, 0))
            and close(xyz(box.m_Size), size) and close(xyz(t.m_LocalPosition), center)), (g.m_Name, center, size)


def world_matrix(scene, node):
    transform = scene.nodes[node][1]
    q = transform.m_LocalRotation
    x,y,z,w = q.x,q.y,q.z,q.w
    rot = np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)], [2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],
                    [2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
    m = np.eye(4); m[:3,:3] = rot @ np.diag(xyz(transform.m_LocalScale)); m[:3,3] = xyz(transform.m_LocalPosition)
    parent = transform.m_Father.path_id
    return world_matrix(scene,parent) @ m if parent else m


def ray_hits(verts, tri, origin, direction):
    a,b,c = (verts[tri[:,i]] for i in range(3))
    e1,e2 = b-a,c-a
    cross = np.cross(direction,e2); det = (cross*e1).sum(axis=1)
    valid = np.abs(det)>1e-7; inv = np.zeros_like(det); inv[valid]=1/det[valid]
    diff=origin-a; u=(diff*cross).sum(axis=1)*inv; q=np.cross(diff,e1)
    v=(q*direction).sum(axis=1)*inv; t=(q*e2).sum(axis=1)*inv
    return np.flatnonzero(valid & (u>=0) & (v>=0) & (u+v<=1) & (t>0)),t


def wall_faces(scene, layout):
    """Verify structural masks after Unity import, including the negative-z line of sight at the foreman locker."""
    raw = json.loads((ROOT/'ArtSource/Tidegate/fbx/tidegate_wall_faces.json').read_text('utf-8-sig'))
    total=rest=0; rays={}
    for b in layout['buildings']:
        id=b['id']; n=0; unmarked=0
        parent=scene.unique('BLD_'+id+'_'+b['style']); roof=scene.unique('BLD_'+id+'_Roof')
        for node in set(scene.descendants(parent))-set(scene.descendants(roof)):
            g=scene.nodes[node][0]
            for ref in g.m_Component:
                if ref.component.type.name!='MeshFilter':continue
                m=ref.component.read().m_Mesh.read()
                data=MeshHandler(m); data.process(); idx=np.asarray(data.get_triangles()[0],dtype=int)
                uv=np.asarray(data.m_UV1)
                assert uv.ndim==2 and len(uv), (id,m.m_Name,'no wall UV')
                values=uv[idx,0]; assert np.all(np.abs(values-values[:,:1])<1e-5),(id,m.m_Name,'mixed mask in a triangle')
                wall=values[:,0]>.5
                n+=int(wall.sum());unmarked+=int((~wall).sum())
                if id=='wh2':
                    mat=world_matrix(scene,node); verts=np.asarray(data.m_Vertices)@mat[:3,:3].T+mat[:3,3]
                    direction=np.array([0.,math.sin(math.radians(55)),-math.cos(math.radians(55))])
                    for z in [-25.5,-25.8,-25.9]:
                        for y in [1.75,.75]:
                            origin=np.array([-53.5,y,z]);hits,t=ray_hits(verts,idx,origin,direction)
                            for i in hits:
                                point=origin+direction*t[i]
                                if point[1] <= b['floorY']+.9:continue
                                rays.setdefault((z,y),[]).append((m.m_Name,int(i),bool(wall[i]),round(float(point[1]),2)))
        assert n == len(raw[id]) and unmarked>0, (id,n,len(raw[id]),unmarked)
        total+=n;rest+=unmarked
    assert len(rays)==6 and all(any('103' in h[0] for h in hits) and all(h[2] for h in hits)
                                for hits in rays.values()),rays
    print(f"Tidegate: {total} exact wall triangles, {rest} unmasked base triangles; all 6 wh2 locker head/foot rays clear the wall above 0.9 m: {rays[(-25.5,1.75)]}")


def tide(layout, path, check_walls=True):
    s = Scene(path)
    rooms = s.indexed("TidegateIndoorRooms")
    assert set(rooms) == {b['id'] for b in layout['buildings']}
    renderers = 0
    for b in layout['buildings']:
        name = b['id']
        x0, z0, x1, z1 = b['footprint']
        footprint(s, rooms[name], ((x1+x0)/2, b['floorY']+1.5, -(z1+z0)/2), (x1-x0, 4, z1-z0))
        roof = s.unique('BLD_'+name+'_Roof')
        refs = s.components(roof, 'MeshRenderer')
        if name == 'vault':  # An open ruin: high concrete wall, separate lower furnishing batch.
            assert not refs and len(s.components(s.unique('BLD_vault_vault'), 'MeshRenderer')) == 2
        else:
            assert refs, 'Roof missing '+name
            assert all(r.read().m_CastShadows for r in refs), name
            renderers += len(refs)
    if check_walls: wall_faces(s,layout)
    print(f'Tidegate: {len(rooms)} original footprints, {renderers} roof/upper renderers, open bank checked')


def sky(layout, path):
    s = Scene(path)
    volumes = s.indexed("SkyPavilionRooms")
    assert set(volumes) == {'pavilion0', 'pavilion1'}
    supports = sorted((b for b in layout['obstacles'] if b['id'].startswith('F_PavilionSupport')), key=lambda b:b['id'])
    assert len(supports) == 8
    for r, label in enumerate(sorted(volumes)):
        bs = supports[4*r:4*r+4]
        xmin, xmax = min(b['center'][0] for b in bs), max(b['center'][0] for b in bs)
        zmin, zmax = min(b['center'][2] for b in bs), max(b['center'][2] for b in bs)
        floor = bs[0]['center'][1]-bs[0]['size'][1]/2
        footprint(s, volumes[label], ((xmin+xmax)/2, floor+1.5, (zmax+zmin)/2), (xmax-xmin+1.2,4,zmax-zmin+1.2))
        roof = s.unique('BLD_'+label+'_Roof')
        assert len(s.components(roof, 'MeshRenderer')) == 2
        bound = {f.read().m_Mesh.read().m_Name for f in s.components(roof, 'MeshFilter')}
        assert bound == {'SkyRoom_VIS_F_Teal_'+str(r+1), 'SkyRoom_VIS_F_Brass_'+str(r+1)}, bound
    print('SkyIsland: both four-column pavilions keep separate tile/brass roofs and their own exact footprints')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--negative-check', action='store_true')
    args = parser.parse_args()
    tg = json.loads((ROOT/'ArtSource/Tidegate/export/tidegate_layout.json').read_text('utf-8'))
    path = ROOT/'Assets/arenas/tidegate_raid'
    tide(tg, path)
    if args.negative_check:
        # Enlarging the warehouse towards its neighbouring room must be rejected by actual packed geometry.
        next(b for b in tg['buildings'] if b['id']=='wh1')['footprint'][2] += 9
        try:
            tide(tg, path, False)
        except AssertionError as err:
            assert 'wh1' in str(err), err
        else:
            raise AssertionError('enlarged warehouse went unnoticed')
        print('Negative: 9 m warehouse encroachment correctly rejected')
    sky(json.loads((ROOT/'ArtSource/SkyIsland/layout.json').read_text('utf-8-sig')), ROOT/'Assets/arenas/sky_island_raid')


if __name__ == '__main__':
    main()
