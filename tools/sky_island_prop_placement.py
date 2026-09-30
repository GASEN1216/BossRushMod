"""Author-time furniture/hero-prop placement using final roads and actual model bounds.
No Blender or runtime dependency. All translations are baked into the original mesh.
"""
import math
from sky_island_dressing import inside, distance_to_segment


def corners(bounds):
    x0,z0,x1,z1=bounds
    return [(x0,z0),(x1,z0),(x1,z1),(x0,z1)]


def segment_distance(a,b,c,d):
    def cross(p,q,r):return (q[0]-p[0])*(r[1]-p[1])-(q[1]-p[1])*(r[0]-p[0])
    ca,cb,cc,cd=cross(a,b,c),cross(a,b,d),cross(c,d,a),cross(c,d,b)
    if ca*cb<=0 and cc*cd<=0 and (max(min(a[0],b[0]),min(c[0],d[0]))<=min(max(a[0],b[0]),max(c[0],d[0]))
            and max(min(a[1],b[1]),min(c[1],d[1]))<=min(max(a[1],b[1]),max(c[1],d[1]))):return 0.0
    return min(distance_to_segment(a,c,d),distance_to_segment(b,c,d),distance_to_segment(c,a,b),distance_to_segment(d,a,b))


def rectangle_segment_distance(bounds,a,b):
    x0,z0,x1,z1=bounds
    if any(x0<=p[0]<=x1 and z0<=p[1]<=z1 for p in (a,b)):return 0.0
    points=corners(bounds)
    return min(segment_distance(c,d,a,b) for c,d in zip(points,points[1:]+points[:1]))


def transformed_bounds(local,x,z,yaw):
    c,s=math.cos(math.radians(yaw)),math.sin(math.radians(yaw))
    points=[(x+px*c+pz*s,z-px*s+pz*c) for px,pz in corners(local)]
    return [min(p[0] for p in points),min(p[1] for p in points),max(p[0] for p in points),max(p[1] for p in points)]


def model_bounds(payload):
    import sky_island_botany
    parts=sky_island_botany.model(payload['meta']['name'],payload['meta']['bounds'])
    points=[p for data in parts.values() for p in data['v']] if parts is not None else payload['mesh']['v']
    if not points or not all(math.isfinite(v) for p in points for v in p):raise ValueError('Invalid placement geometry')
    return [min(p[0] for p in points),min(p[2] for p in points),max(p[0] for p in points),max(p[2] for p in points)]


class PlacementSpace:
    def __init__(self,g,layout,actual_bounds):
        self.islands={i['id']:i for i in layout['islands']}
        self.tracks=list(g.PAVING_TRACKS)
        self.markers=layout['markers']
        self.reserved=[{'id':o['id'],'island':o['island'],'bounds':actual_bounds.get(o['id'],o['bounds'])} for o in layout['obstacles']]
        self.records=[]

    def capture(self,g,sid,name,x,z,emit):
        before={key:len(data['v']) for key,data in g.GROUPS.items()}
        tracks=g.PAVING_TRACKS
        try:
            g.PAVING_TRACKS=[]
            emit()
        finally:g.PAVING_TRACKS=tracks
        points=[v for key,data in g.GROUPS.items() for v in data['v'][before.get(key,0):]]
        if not points:raise ValueError('Empty attached prop: '+name)
        local=[min(p[0] for p in points)-x,min(p[2] for p in points)-z,
               max(p[0] for p in points)-x,max(p[2] for p in points)-z]
        px,pz,_=self.place(sid,name,local,x,z,0,'building',reserve=name not in ('porch_garden','porch_hedge'))
        for key,data in g.GROUPS.items():
            start=before.get(key,0)
            data['v'][start:]=[(vx+px-x,vy,vz+pz-z) for vx,vy,vz in data['v'][start:]]
        return px,pz

    def nearest_road(self,sid,x,z):
        outline=self.islands[sid]['outline'];best=None
        for points,width in self.tracks:
            for a,b in zip(points,points[1:]):
                if not inside(((a[0]+b[0])/2,(a[1]+b[1])/2),outline):continue
                dx,dz=b[0]-a[0],b[1]-a[1];t=max(0,min(1,((x-a[0])*dx+(z-a[1])*dz)/max(dx*dx+dz*dz,1e-12)))
                point=(a[0]+t*dx,a[1]+t*dz);distance=math.dist((x,z),point)
                if best is None or distance<best[0]:best=(distance,point,width)
        return best

    def free(self,sid,bounds):
        island=self.islands[sid];points=corners(bounds);outline=island['outline']
        if not all(inside(p,outline) for p in points):return False
        for a,b in zip(outline,outline[1:]+outline[:1]):
            if rectangle_segment_distance(bounds,a,b)<.65:return False
        x0,z0,x1,z1=bounds
        for row in self.reserved:
            if row['island']!=sid:continue
            a,b,c,d=row['bounds']
            if x0<c+.45 and x1>a-.45 and z0<d+.45 and z1>b-.45:return False
        for marker in self.markers:
            if marker.get('island')!=sid or marker['kind']=='lamp':continue
            x,_,z=marker['position'];distance=math.hypot(max(x0-x,0,x-x1),max(z0-z,0,z-z1))
            if distance<1.1:return False
        for samples,width in self.tracks:
            half=width/2+.35
            for a,b in zip(samples,samples[1:]):
                if (min(a[0],b[0])>x1+half or max(a[0],b[0])<x0-half
                        or min(a[1],b[1])>z1+half or max(a[1],b[1])<z0-half):continue
                if rectangle_segment_distance(bounds,a,b)<half-1e-6:return False
        return True

    def place(self,sid,name,local,x,z,yaw=0,role='free',reserve=True):
        original=[x,self.islands[sid]['height'],z];chosen=None
        # Keep valid authored placements. Search only when real geometry blocks a
        # road/prop, facing the same nearest road for seating and shop fronts.
        candidates=[(x,z)]
        for radius in [i*.75 for i in range(1,41)]:
            for step in range(24):
                angle=step*math.tau/24;candidates.append((x+radius*math.cos(angle),z+radius*math.sin(angle)))
        for px,pz in candidates:
            heading=yaw
            if role=='roadside':
                nearest=self.nearest_road(sid,px,pz)
                if nearest:
                    ax,az=nearest[1];heading=math.degrees(math.atan2(-(ax-px),-(az-pz)))
            bounds=transformed_bounds(local,px,pz,heading)
            if self.free(sid,bounds):chosen=(px,pz,heading,bounds);break
        if chosen is None:raise ValueError('No clear authored placement: '+sid+'/'+name)
        px,pz,heading,bounds=chosen;identifier=sid+'_'+name+'_'+str(len(self.records))
        if reserve:self.reserved.append({'id':identifier,'island':sid,'bounds':bounds})
        self.records.append({'id':identifier,'island':sid,'model':name,'role':role,'sourcePosition':original,
                             'position':[px,self.islands[sid]['height'],pz],'yaw':heading,'bounds':bounds,
                             'movedMeters':math.dist((x,z),(px,pz)),'solidReservation':reserve})
        return px,pz,heading
