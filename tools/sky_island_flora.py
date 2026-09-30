"""Small authored flowers and grasses, bounded by their existing placement assets.

Static geometry only: curved tapered stems, folded leaves and cupped petals.
The original selected OBJ supplies the placement envelope and material vocabulary.
"""
import math

TAU=math.tau


class Geometry:
    def __init__(self):
        self.groups={}

    def mesh(self, material, vertices, faces):
        group=self.groups.setdefault(material,{'v':[],'f':[]})
        offset=len(group['v']);group['v'].extend(vertices)
        group['f'].extend(tuple(offset+i for i in face) for face in faces)

    def tube(self, material, points, radii, sides=5):
        vertices=[]
        for (x,y,z),radius in zip(points,radii):
            vertices.extend((x+radius*math.cos(i*TAU/sides),y,z+radius*math.sin(i*TAU/sides)) for i in range(sides))
        faces=[]
        for ring in range(len(points)-1):
            for i in range(sides):
                a=ring*sides+i;b=ring*sides+(i+1)%sides;c=b+sides;d=a+sides
                faces.extend(((a,b,c),(a,c,d)))
        vertices.extend((points[0],points[-1]));first=len(vertices)-2;last=len(vertices)-1
        for i in range(sides):
            faces.extend(((first,(i+1)%sides,i),(last,(len(points)-1)*sides+i,(len(points)-1)*sides+(i+1)%sides)))
        self.mesh(material,vertices,faces)

    def blade(self, material, base, angle, length, width, rise, curl=.04, fold=.025):
        """Closed lancet with a raised midrib; no paper-thin crossing triangles."""
        radial=(math.cos(angle),math.sin(angle));side=(-radial[1],radial[0])
        vertices=[base];rings=[]
        for t in (.24,.52,.78):
            half=width*math.sin(math.pi*t)**.8
            center=(base[0]+radial[0]*length*t,base[1]+rise*t+curl*t*t,base[2]+radial[1]*length*t)
            indices=[]
            for fraction in (-1,0,1):
                indices.append(len(vertices))
                vertices.append((center[0]+side[0]*half*fraction,center[1]+(fold*math.sin(math.pi*t) if fraction==0 else 0),center[2]+side[1]*half*fraction))
            rings.append(indices)
        tip=len(vertices);vertices.append((base[0]+radial[0]*length,base[1]+rise+curl,base[2]+radial[1]*length))
        faces=[(0,rings[0][1],rings[0][0]),(0,rings[0][2],rings[0][1])]
        for left,right in zip(rings,rings[1:]):
            for column in range(2):
                faces.extend(((left[column],left[column+1],right[column+1]),(left[column],right[column+1],right[column])))
        faces.extend(((rings[-1][0],rings[-1][1],tip),(rings[-1][1],rings[-1][2],tip)))
        underside={}
        for ring in rings:
            old=ring[1];underside[old]=len(vertices)
            v=vertices[old];vertices.append((v[0],v[1]-.012,v[2]))
        faces+= [tuple(underside.get(i,i) for i in reversed(face)) for face in list(faces)]
        self.mesh(material,vertices,faces)


def _flower(name):
    geo=Geometry();seed=sum((i+1)*ord(c) for i,c in enumerate(name));phase=(seed%37)*.17
    bloom='colorPurple' if 'purple' in name else 'colorYellow' if 'yellow' in name else 'colorRed'
    center=(.055*math.cos(phase),.79,.055*math.sin(phase))
    points=[(.018*math.sin(phase)*t*t,.79*t,.055*math.sin(phase)*t*t) for t in (0,.25,.5,.75,1)]
    points[-1]=center
    geo.tube('grass',points,(.016,.014,.012,.01,.008))
    for i in range(3):
        t=.23+i*.16;base=(points[2][0]*t,.79*t,points[2][2]*t)
        geo.blade('leafsGreen' if i%2 else 'grass',base,phase+i*2.39996,.23-i*.02,.05,.09,curl=-.025,fold=.022)
    count=9 if 'yellow' in name else 6 if 'purple' in name else 5
    for i in range(count):
        angle=phase+i*TAU/count
        geo.blade(bloom,center,angle,.255*(1+.055*math.sin(i*2.4+phase)),.057 if count==9 else .092,
                  .045,curl=.045 if 'purple' in name else -.01,fold=.026)
    if name.endswith('B'):
        for i in range(5):
            geo.blade(bloom,(center[0],center[1]+.025,center[2]),phase+.32+i*TAU/5,.16,.058,.055,curl=.012,fold=.018)
    geo.tube('colorWhite',[(center[0],center[1]-.009,center[2]),(center[0],center[1]+.035,center[2]),
                              (center[0],center[1]+.055,center[2])],(.053,.05,.022),8)
    return geo.groups


def _grass(name):
    geo=Geometry();tall=name.endswith('Tall');count=7 if tall else 9
    for i in range(count):
        angle=i*2.39996;length=.19+.055*(i%4);rise=.55+.08*(i%5)
        base=(.03*math.cos(angle),0,.03*math.sin(angle))
        geo.blade('grass' if i%3 else 'leafsGreen',base,angle,length,.019 if name.startswith('grass') else .045,
                  rise,curl=-.10,fold=.013)
    return geo.groups


def refine_model(name, original):
    if name.startswith('flower_'):
        groups=_flower(name)
    elif name in ('grass_leafs','plant_flatShort','plant_flatTall'):
        groups=_grass(name)
    else:
        return None
    old=[v for part in original for v in part.verts]
    vertices=[v for group in groups.values() for v in group['v']]
    lo=[min(v[a] for v in vertices) for a in range(3)];hi=[max(v[a] for v in vertices) for a in range(3)]
    old_radius=max(math.hypot(v[0],v[2]) for v in old)
    radius=max(math.hypot(v[0],v[2]) for v in vertices)
    # Keep the precise pre-existing planting radius: meadow selection consumes RNG
    # after footprint rejection, so a changed radius would move subsequent plants.
    scale=old_radius/radius
    result=[]
    for material,group in groups.items():
        points=tuple((v[0]*scale,(v[1]-lo[1])/(hi[1]-lo[1]),v[2]*scale) for v in group['v'])
        result.append((material,points,tuple(group['f'])))
    return tuple(result)
