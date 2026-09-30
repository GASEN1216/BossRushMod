"""天空岛阴影几何与生产 HLSL 的离线属性回归；不运行 Unity/Blender。

执行 Shader 中两个纯函数的实际语句及作者工程 URP ApplyShadowBias，验证
跨级联采样、反法线偏移和点光源方向；不以源码 token 存在冒充行为通过。
BOSSRUSH_GUARD_SOURCE_ONLY=1 仅跑仓库几何单元，成功返回 PARTIAL / 2；
完整模式要求作者 Shader、Builder、URP、图集与独立 C# 编译器，缺失即 FAIL / 1。
"""
import argparse
import copy
import json
import math
import os
from pathlib import Path
import random
import re
import sys
import subprocess
import tempfile
import unittest

SOURCE_ONLY = os.environ.get("BOSSRUSH_GUARD_SOURCE_ONLY") == "1"
ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--repo', type=Path, default=ROOT)
parser.add_argument('--project', type=Path)
ARGS = parser.parse_args()
sys.path.insert(0, str(ARGS.repo / 'tools'))
from sky_island_mesh_hygiene import clean_faces
from sky_island_surface_repair import clean_candidate_surfaces
if not SOURCE_ONLY and ARGS.project is None:
    from unity_project_path import find_unity_project
    project = find_unity_project()
    ARGS.project = Path(project) if project else None


class Vector(tuple):
    def __new__(cls, *values):
        return tuple.__new__(cls, values)

    def __add__(self, other):
        return Vector(*(a + b for a, b in zip(self, other)))

    def __sub__(self, other):
        return Vector(*(a - b for a, b in zip(self, other)))

    def __neg__(self):
        return Vector(*(-a for a in self))

    def __mul__(self, other):
        if isinstance(other, (int, float)):
            return Vector(*(a * other for a in self))
        return Vector(*(a * b for a, b in zip(self, other)))

    __rmul__ = __mul__
    x = property(lambda self: self[0])
    y = property(lambda self: self[1])
    z = property(lambda self: self[2])


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def normalize(v):
    return v * (1 / math.sqrt(dot(v, v)))


def function_body(source, name):
    source = re.sub(r'//[^\n]*|/\*.*?\*/', '', source, flags=re.S)
    match = re.search(r'\b' + re.escape(name) + r'\s*\([^{};]*\)\s*(?::\s*\w+\s*)?\{', source)
    if not match:
        raise AssertionError('Missing HLSL function: ' + name)
    start = match.end()
    depth = 1
    for index in range(start, len(source)):
        depth += (source[index] == '{') - (source[index] == '}')
        if depth == 0:
            return source[start:index]
    raise AssertionError('Unclosed HLSL function: ' + name)


def select_variant(body, macros):
    active, stack, lines = True, [], []
    for line in body.splitlines():
        line = line.strip()
        if line.startswith(('#if ', '#elif ')):
            condition = line.split(' ', 1)[1]
            condition = re.sub(r'defined\((\w+)\)', lambda m: str(m[1] in macros), condition)
            condition = condition.replace('&&', ' and ').replace('||', ' or ')
            condition = re.sub(r'!(?!=)', ' not ', condition)
            selected = bool(eval(condition, {'__builtins__': {}}, {}))
            if line.startswith('#if '):
                stack.append([active, selected])
            else:
                selected = selected and not stack[-1][1]
                stack[-1][1] |= selected
            active = stack[-1][0] and selected
        elif line == '#else':
            active = stack[-1][0] and not stack[-1][1]
            stack[-1][1] = True
        elif line == '#endif':
            active = stack.pop()[0]
        elif active and line:
            lines.append(line)
    assert not stack
    return lines


def execute_hlsl(body, environment, macros=()):
    """只执行生产纯函数所需的标量/向量语句，未知语法直接失败。"""
    env = dict(environment)
    env.update(float4=Vector, dot=dot, normalize=normalize,
               saturate=lambda value: min(1, max(0, value)),
               splat=lambda value: Vector(*([value[0] if isinstance(value, tuple) else value] * 3)))

    def evaluate(expression):
        expression = expression.replace('&&', ' and ').replace('||', ' or ')
        expression = re.sub(r'\b(\w+)\.xxx\b', r'splat(\1)', expression)
        return eval(expression, {'__builtins__': {}}, env)

    for line in select_variant(body, set(macros)):
        if line.startswith('return '):
            return evaluate(line[7:].removesuffix(';'))
        if line.startswith('if '):
            match = re.fullmatch(r'if\s*\((.*)\)\s+(\w+)\s*=\s*(.*);', line)
            if match is None:
                raise AssertionError('Unsupported HLSL conditional: ' + line)
            if evaluate(match[1]):
                env[match[2]] = evaluate(match[3])
            continue
        match = re.fullmatch(r'(?:(?:float[234]?|half[234]?)\s+)?(\w+)\s*=\s*(.*);', line)
        if match is None:
            raise AssertionError('Unsupported HLSL statement: ' + line)
        env[match[1]] = evaluate(match[2])
    raise AssertionError('HLSL function did not return')


class MeshProperties(unittest.TestCase):
    def test_duplicate_and_atlas_preservation(self):
        # 位置完全相同的 UV 缝顶点；第四个面是不同画色，背面也必须保留。
        vertices = [(0, 0, 0), (2, 0, 0), (0, 2, 0)] * 3
        uv = [(0, 0), (1, 0), (0, 1)] * 2 + [(0.2, 0.2)] * 3
        faces = [(0, 1, 2), (4, 5, 3), (2, 1, 0), (6, 7, 8)]
        snapshot = copy.deepcopy((vertices, faces, uv))
        cleaned, smooth, removed, collapsed = clean_faces(vertices, faces, True, uv=uv)
        self.assertEqual(cleaned, [faces[0], faces[2], faces[3]], 'same-winding atlas duplicate survived')
        self.assertEqual((removed, collapsed), (1, 0))
        self.assertEqual(smooth, [True] * 3)
        self.assertEqual(clean_faces(vertices, faces, True)[0], faces, 'UV-less cleanup guessed seam identity')
        self.assertEqual((vertices, faces, uv), snapshot)
        # 未传 UV 的生产旧入口仍可安全清掉同索引循环重复面。
        self.assertEqual(clean_faces(vertices, [faces[0], (1, 2, 0)])[2], 1)
        self.assertEqual(clean_faces(vertices, faces[:2], [True, False], uv=uv)[2], 0)
        self.assertEqual(clean_faces(vertices, cleaned, smooth, uv=uv)[0], cleaned)

    def test_float32_and_degenerate_faces(self):
        vertices = [(256, 0, 0), (258, 0, 0), (256, 2, 0), (256 + 1e-7, 0, 0)]
        uv = [(0, 0), (1, 0), (0, 1), (0, 0)]
        cleaned, _, removed, collapsed = clean_faces(vertices, [(0, 1, 2), (3, 1, 2), (0, 3, 1)], uv=uv)
        self.assertEqual(cleaned, [(0, 1, 2)], 'float32 duplicate survived')
        self.assertEqual((removed, collapsed), (2, 1))

    def test_candidate_repair_wiring(self):
        mesh = {'v': [(0, 0, 0), (1, 0, 0), (0, 1, 0)],
                'uv': [(0, 0), (1, 0), (0, 1)],
                'f': [(0, 1, 2), (1, 2, 0), (0, 2, 1)], 'smooth': True}
        vertices, uv = copy.deepcopy((mesh['v'], mesh['uv']))
        proof = clean_candidate_surfaces(mesh)
        self.assertEqual(proof['removedFaces'], 1)
        self.assertEqual(mesh['f'], [(0, 1, 2), (0, 2, 1)])
        self.assertEqual((mesh['v'], mesh['uv']), (vertices, uv))

    def test_real_atlas_models(self):
        self.assertIsNotNone(ARGS.project, 'Full validation requires Unity author project')
        folder = ARGS.project / 'ArtSource/SkyIsland/tripo'
        self.assertTrue(folder.is_dir(), 'Full validation requires real atlas models')
        models, removed = 0, 0
        for path in sorted(folder.glob('*.json')):
            payload = json.loads(path.read_text(encoding='utf-8-sig'))
            if not isinstance(payload, dict) or not isinstance(payload.get('mesh'), dict):
                continue
            mesh = payload['mesh']
            snapshot = copy.deepcopy(mesh)
            faces, smooth, count, collapsed = clean_faces(mesh['v'], mesh['f'], mesh.get('smooth', False), uv=mesh['uv'])
            self.assertEqual(mesh, snapshot, path.name + ' source mutated')
            self.assertEqual(clean_faces(mesh['v'], faces, smooth, uv=mesh['uv'])[:3], (faces, smooth, 0))
            models += 1
            removed += count
        self.assertGreater(models, 0)
        print('ATLAS_MODELS', models, 'safeRemovedFaces', removed)


class ShaderProperties(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if ARGS.project is None:
            raise AssertionError('Full validation requires Unity author project')
        cls.source = (ARGS.project / 'Assets/SkyIsland/Shaders/SkyIslandEnvironment.shader').read_text(encoding='utf-8-sig')
        package = ARGS.project / 'Library/PackageCache/com.unity.render-pipelines.universal@14.0.12'
        cls.urp = (package / 'ShaderLibrary/Shadows.hlsl').read_text(encoding='utf-8-sig')
        cls.bias_body = function_body(cls.urp, 'ApplyShadowBias')

    def receive(self, position, vertex, macros):
        # 两个 cascade 的 atlas 矩阵模拟；期望值由片元所在级联给出。
        return execute_hlsl(function_body(self.source, 'SkyIslandReceiveShadowCoord'), {
            'positionWS': position, 'vertexShadowCoord': vertex,
            'TransformWorldToShadowCoord': lambda p: Vector(p.x * 0.1 + (0 if p.x < 10 else 0.5), p.y, p.z, 0)
        }, macros)

    def biased(self, position, normal, direction, punctual=False):
        def apply(p, n, light):
            return execute_hlsl(self.bias_body, {'positionWS': p, 'normalWS': n,
                'lightDirection': light, '_ShadowBias': Vector(-0.02, -0.15, 0, 0)})
        return execute_hlsl(function_body(self.source, 'SkyIslandBiasedShadowPosition'), {
            'positionWS': position, 'normalWS': normal, '_LightDirection': direction,
            '_LightPosition': Vector(4, 5, 6), 'ApplyShadowBias': apply,
        }, ('_CASTING_PUNCTUAL_LIGHT_SHADOW',) if punctual else ())

    def test_cascade_fragment_sampling(self):
        a, b = Vector(8, 0, 0), Vector(12, 0, 0)
        for t in (0.1, 0.25, 0.49, 0.5, 0.51, 0.75, 0.9):
            pixel = a * (1 - t) + b * t
            interpolated = Vector(0.8, 0, 0, 0) * (1 - t) + Vector(1.7, 0, 0, 0) * t
            expected = Vector(pixel.x * 0.1 + (0 if pixel.x < 10 else 0.5), 0, 0, 0)
            result = self.receive(pixel, interpolated, ('MAIN_LIGHT_CALCULATE_SHADOWS', '_MAIN_LIGHT_SHADOWS_CASCADE'))
            self.assertEqual(result, expected, 'cascade sampled interpolated vertex matrices')
            self.assertNotEqual(interpolated, expected)
        pixel, vertex = Vector(1, 2, 3), Vector(0.2, 0.4, 0.6, 1)
        self.assertEqual(self.receive(pixel, vertex, ('REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR', 'MAIN_LIGHT_CALCULATE_SHADOWS')), vertex)
        self.assertEqual(self.receive(pixel, vertex, ()), Vector(0, 0, 0, 0))

    def test_double_sided_bias_equivalence(self):
        rng = random.Random(930)
        for _ in range(256):
            normal = normalize(Vector(*(rng.uniform(-1, 1) for _ in range(3))))
            light = normalize(Vector(*(rng.uniform(-1, 1) for _ in range(3))))
            position = Vector(100, 12, -160)
            self.assertEqual(self.biased(position, normal, light), self.biased(position, -normal, light),
                             'opposite normals wrote different caster depth')
        # 掠射光不能留下 exact zero 的双面例外。
        for normal in (Vector(1, 0, 0), Vector(0, 0, 1)):
            self.assertEqual(self.biased(Vector(0, 0, 0), normal, Vector(0, 1, 0)),
                             self.biased(Vector(0, 0, 0), -normal, Vector(0, 1, 0)))

    def test_punctual_direction_is_per_vertex(self):
        for position in (Vector(0, 0, 0), Vector(10, 2, -1), Vector(-3, 12, 5)):
            light = normalize(Vector(4, 5, 6) - position)
            expected = self.biased(position, Vector(0, 1, 0), light)
            self.assertEqual(self.biased(position, Vector(0, 1, 0), Vector(0, 0, 1), True), expected,
                             'punctual shadow used directional light vector')

    def test_paired_visible_face_selection(self):
        body = function_body(self.source, 'SkyIslandKeepVisibleFace')
        for front in (True, False):
            for paired in (0, 1):
                visible = execute_hlsl(body, {'frontFace': front, 'pairedSurface': paired})
                self.assertEqual(visible, front or not paired, 'paired backface competed with front atlas')

    def test_editor_pair_geometry_execution(self):
        compiler = csharp_compiler()
        self.assertTrue(compiler.is_file(), 'Full validation requires standalone Windows C# compiler')
        builder = (ARGS.project / 'Assets/Editor/SkyIslandBundleBuilder.cs').read_text(encoding='utf-8-sig')
        methods = ('private static void PreparePairedSurfaceRendering(MeshFilter filter) {\n'
                   + function_body(builder, 'PreparePairedSurfaceRendering') + '\n}\n'
                   + 'private static string OrientedSurfaceKey(int a,int b,int c) {\n'
                   + function_body(builder, 'OrientedSurfaceKey') + '\n}\n')
        # 生产构建器方法原文 + 最小 Unity Mesh/资产替身；不加载或启动 Unity。
        fixture = CS_MESH_STUBS + '\nclass Harness { const string WorkDir="Assets/SkyIsland";\n' + methods + CS_MESH_MAIN + '\n}'
        with tempfile.TemporaryDirectory(prefix='sky-shadow-csharp-') as folder:
            source, binary = Path(folder) / 'Probe.cs', Path(folder) / 'Probe.exe'
            source.write_text(fixture, encoding='utf-8-sig')
            build = subprocess.run([str(compiler), '/nologo', '/target:exe', '/r:System.Core.dll',
                                    '/out:' + str(binary), str(source)], capture_output=True)
            self.assertEqual(build.returncode, 0, build.stdout.decode('utf-8', errors='replace') + build.stderr.decode('utf-8', errors='replace'))
            result = subprocess.run([str(binary)], capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout.decode('utf-8', errors='replace') + result.stderr.decode('utf-8', errors='replace'))
            self.assertIn(b'PAIRED_GEOMETRY_PASS', result.stdout)

    def test_deferred_consumes_skyshade(self):
        fragment = re.sub(r'\s+', '', function_body(self.source, 'SkyGBufferFragment'))
        self.assertIn('half4color=SkyShade(input,frontFace);', fragment,
                      'GBuffer no longer consumes SkyShade shadow sampling')
        self.assertIn('SurfaceDataToGbuffer(surfaceData,inputData,color.rgb,kLightingInvalid)', fragment)
        self.assertRegex(self.source, r'"UniversalMaterialType"\s*=\s*"Unlit"')
        package = ARGS.project / 'Library/PackageCache/com.unity.render-pipelines.universal@14.0.12'
        gbuffer = (package / 'ShaderLibrary/UnityGBuffer.hlsl').read_text(encoding='utf-8-sig')
        body = re.sub(r'\s+', '', function_body(gbuffer, 'SurfaceDataToGbuffer'))
        self.assertIn('output.GBuffer3=half4(globalIllumination,1);', body)
        # Check official C# without comments/preprocessor-disabled tokens.
        from cs_source_util import clean_source
        pass_source = clean_source((package / 'Runtime/Passes/GBufferPass.cs').read_text(encoding='utf-8-sig'))
        compact = re.sub(r'\s+', '', pass_source)
        self.assertIn('s_ShaderTagValues[2]=s_ShaderTagUnlit;', compact)
        self.assertIn('s_RenderStateBlocks[2]=DeferredLights.OverwriteStencil(m_RenderStateBlock,(int)StencilUsage.MaterialMask,(int)StencilUsage.MaterialUnlit);', compact)
        self.assertIn('ShaderTagIdlightModeTag=s_ShaderTagUniversalGBuffer;', compact)
        deferred = clean_source((package / 'Runtime/DeferredLights.cs').read_text(encoding='utf-8-sig'))
        compact = re.sub(r'\s+', '', deferred)
        self.assertIn('this.GbufferAttachments[this.GBufferLightingIndex]=colorAttachment;', compact)
        self.assertIn('m_StencilDeferredMaterial.SetFloat(ShaderConstants._LitDirStencilRef,(float)StencilUsage.MaterialLit);', compact)
        stencil = (package / 'Shaders/Utils/StencilDeferred.shader').read_text(encoding='utf-8-sig')
        self.assertRegex(stencil, r'Ref\s*\[_LitDirStencilRef\]\s*ReadMask\s*\[_LitDirStencilReadMask\]')

    def test_production_pass_wiring(self):
        shade = re.sub(r'\s+', '', function_body(self.source, 'SkyShade'))
        self.assertIn('GetMainLight(SkyIslandReceiveShadowCoord(input.positionWS,input.shadowCoord))', shade)
        shadow = re.sub(r'\s+', '', function_body(self.source, 'ShadowVertex'))
        self.assertIn('TransformWorldToHClip(SkyIslandBiasedShadowPosition(positionWS,normalWS))', shadow)
        self.assertRegex(self.source, r'#pragma\s+multi_compile_vertex\s+_\s+_CASTING_PUNCTUAL_LIGHT_SHADOW')
        self.assertEqual(len(re.findall(r'"LightMode"\s*=\s*"ShadowCaster"', self.source)), 1)
        self.assertEqual(len(re.findall(r'"LightMode"\s*=\s*"UniversalGBuffer"', self.source)), 1)
        self.assertNotIn('"UniversalForwardOnly"', self.source)
        for function in ('SkyShade', 'DepthFragment'):
            body = re.sub(r'\s+', '', function_body(self.source, function))
            self.assertIn('clip(SkyIslandKeepVisibleFace(frontFace,input.pairedSurface)?1.0h:-1.0h)', body)
        self.assertIn('input.pairedSurface.w>0.5?input.pairedSurface.xyz:input.normalOS', shadow)
        builder = (ARGS.project / 'Assets/Editor/SkyIslandBundleBuilder.cs').read_text(encoding='utf-8-sig')
        self.assertIn('if(!cloud)PreparePairedSurfaceRendering(filter);', re.sub(r'\s+', '', function_body(builder, 'BuildResources')))


CS_MESH_STUBS = r"""
using System;
using System.Collections.Generic;
using System.Linq;
struct Vector3 : IEquatable<Vector3> {
 public float x,y,z;
 public Vector3(float a,float b,float c){x=a;y=b;z=c;}
 public float sqrMagnitude {get{return x*x+y*y+z*z;}}
 public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
 public static Vector3 Cross(Vector3 a,Vector3 b){return new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);}
 public void Normalize(){float n=(float)Math.Sqrt(sqrMagnitude);x/=n;y/=n;z/=n;}
 public bool Equals(Vector3 o){return x.Equals(o.x)&&y.Equals(o.y)&&z.Equals(o.z);}
 public override bool Equals(object o){return o is Vector3 && Equals((Vector3)o);}
 public override int GetHashCode(){return x.GetHashCode()^y.GetHashCode()^z.GetHashCode();}
}
struct Vector4 { public float x,y,z,w; public Vector4(float a,float b,float c,float d){x=a;y=b;z=c;w=d;} public static Vector4 zero {get{return new Vector4();}} }
struct Color32 {public byte r,g,b,a;}
enum IndexFormat { UInt16,UInt32 }
class Mesh {
 public string name="fixture";
 public Vector3[] vertices=new Vector3[0],normals=new Vector3[0];
 public Vector4[] tangents=new Vector4[0];
 public Color32[] colors32=new Color32[0];
 public IndexFormat indexFormat;
 public int subMeshCount;
 public Dictionary<int,int[]> faces=new Dictionary<int,int[]>();
 public Dictionary<int,List<Vector4>> uvs=new Dictionary<int,List<Vector4>>();
 public int[] GetTriangles(int sub){return (int[])faces[sub].Clone();}
 public void GetUVs(int channel,List<Vector4> target){List<Vector4> v;if(uvs.TryGetValue(channel,out v))target.AddRange(v);}
 public void SetVertices(List<Vector3> data){vertices=data.ToArray();}
 public void SetNormals(List<Vector3> data){normals=data.ToArray();}
 public void SetTangents(List<Vector4> data){tangents=data.ToArray();}
 public void SetColors(List<Color32> data){colors32=data.ToArray();}
 public void SetUVs(int channel,List<Vector4> data){uvs[channel]=new List<Vector4>(data);}
 public void SetTriangles(int[] data,int sub){faces[sub]=(int[])data.Clone();}
 public void RecalculateBounds(){}
}
class MeshFilter {public Mesh sharedMesh;public string name="fixture";}
static class Debug {public static void Log(string s){Console.WriteLine(s);}}
static class AssetDatabase {
 public static Dictionary<string,Mesh> saved=new Dictionary<string,Mesh>();
 public static T LoadAssetAtPath<T>(string p) where T:class {Mesh m;return saved.TryGetValue(p,out m)?m as T:null;}
 public static void CreateAsset(Mesh m,string p){saved.Add(p,m);}
}
static class EditorUtility {public static void CopySerialized(Mesh from,Mesh to){throw new Exception("unexpected rewrite");}}
namespace UnityEngine {static class Object {public static void DestroyImmediate(object o){}}}
"""

CS_MESH_MAIN = r"""
static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
static Mesh Sample(){
 var m=new Mesh();
 m.vertices=new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(0,0,1)};
 m.normals=Enumerable.Repeat(new Vector3(0,0,1),7).ToArray();
 m.tangents=Enumerable.Repeat(new Vector4(1,0,0,1),7).ToArray();
 m.colors32=Enumerable.Repeat(new Color32{r=42,g=100,b=255,a=255},7).ToArray();
 m.SetUVs(0,Enumerable.Range(0,7).Select(i=>new Vector4(i*.13f,i*.1f,0,0)).ToList());
 m.SetUVs(1,Enumerable.Range(0,7).Select(i=>new Vector4(i*.21f,i*.2f,0,0)).ToList());
 m.subMeshCount=2;m.SetTriangles(new[]{0,1,2,0,1,6},0);m.SetTriangles(new[]{5,4,3},1);
 return m;
}
static int Main(){try{Run();return 0;}catch(Exception e){Console.WriteLine("GEOMETRY_FAILURE "+e.Message);return 1;}}
static void Run(){
 Mesh original=Sample();var filter=new MeshFilter{sharedMesh=original};
 PreparePairedSurfaceRendering(filter);Mesh result=filter.sharedMesh;
 Check(!object.ReferenceEquals(result,original),"paired mesh not saved");
 Check(object.ReferenceEquals(result,AssetDatabase.saved.Values.Single()),"renderer references transient mesh");
 Check(result.vertices.Length==13,"wrong duplicated corner count");
 Check(original.vertices.Length==7&&original.faces[0].SequenceEqual(new[]{0,1,2,0,1,6}),"source mesh mutated");
 Check(result.faces[0].Skip(3).SequenceEqual(new[]{0,1,6}),"isolated triangle modified");
 Check(result.uvs[7].Take(7).All(v=>v.w==0)&&result.uvs[7].Skip(7).All(v=>v.w==1),"pair marker missing or spilled");
 for(int sub=0;sub<2;sub++)for(int i=0;i<3;i++){
  int old=original.faces[sub][i],updated=result.faces[sub][i];
  Check(result.vertices[updated].Equals(original.vertices[old]),"surface position changed");
  Check(result.normals[updated].Equals(original.normals[old]),"smooth normal changed");
  Check(result.tangents[updated].Equals(original.tangents[old])&&result.colors32[updated].Equals(original.colors32[old]),"vertex appearance changed");
  for(int uv=0;uv<2;uv++)Check(result.uvs[uv][updated].Equals(original.uvs[uv][old]),"atlas or lightmap seam changed");
  Vector4 mark=result.uvs[7][updated];Check(Math.Abs(mark.z)==1&&mark.x==0&&mark.y==0,"paired caster normal is not geometric");
 }
 Check(result.uvs[7][result.faces[0][0]].Equals(result.uvs[7][result.faces[1][0]]),"paired caster normals disagree across winding");
 var isolated=Sample();isolated.SetTriangles(new[]{3,4,5},1);
 var other=new MeshFilter{sharedMesh=isolated,name="isolated"};PreparePairedSurfaceRendering(other);
 Check(object.ReferenceEquals(other.sharedMesh,isolated),"same winding misidentified as reverse pair");
 var reserved=Sample();reserved.SetUVs(7,Enumerable.Repeat(new Vector4(1,2,3,4),7).ToList());
 var conflict=new MeshFilter{sharedMesh=reserved,name="reserved"};bool rejected=false;
 try{PreparePairedSurfaceRendering(conflict);}catch(Exception e){rejected=e.Message.Contains("UV8 already used");}
 Check(rejected&&object.ReferenceEquals(conflict.sharedMesh,reserved),"reserved channel overwritten");
 Console.WriteLine("PAIRED_GEOMETRY_PASS");
}
"""


def csharp_compiler():
    return Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'


def full_dependencies():
    if ARGS.project is None:
        return ['Unity author project not found; set BOSSRUSH_UNITY_PROJECT or --project']
    package = 'Library/PackageCache/com.unity.render-pipelines.universal@14.0.12/'
    required = [
        'Assets/SkyIsland/Shaders/SkyIslandEnvironment.shader',
        'Assets/Editor/SkyIslandBundleBuilder.cs',
        package + 'ShaderLibrary/Shadows.hlsl',
        package + 'ShaderLibrary/UnityGBuffer.hlsl',
        package + 'Runtime/Passes/GBufferPass.cs',
        package + 'Runtime/DeferredLights.cs',
        package + 'Shaders/Utils/StencilDeferred.shader',
    ]
    missing = [str(ARGS.project / path) for path in required if not (ARGS.project / path).is_file()]
    atlas = ARGS.project / 'ArtSource/SkyIsland/tripo'
    if not atlas.is_dir() or not any(atlas.glob('*.json')):
        missing.append(str(atlas) + ' (real atlas JSON models required)')
    if not csharp_compiler().is_file():
        missing.append(str(csharp_compiler()) + ' (standalone C# compiler required)')
    return missing


def main():
    loader = unittest.TestLoader()
    if SOURCE_ONLY:
        # Explicit subset: do not resolve/read local-only author assets in source CI.
        suite = unittest.TestSuite(MeshProperties(name) for name in loader.getTestCaseNames(MeshProperties)
                                   if name != 'test_real_atlas_models')
    else:
        missing = full_dependencies()
        if missing:
            print('FAIL: SkyIslandShadowStabilityPropertyTest full validation missing dependencies:')
            for path in missing:
                print('  ' + path)
            return 1
        suite = unittest.TestSuite([loader.loadTestsFromTestCase(MeshProperties),
                                   loader.loadTestsFromTestCase(ShaderProperties)])
    result = unittest.TextTestRunner(stream=sys.stdout, verbosity=2).run(suite)
    if not result.wasSuccessful() or result.skipped:
        print('FAIL: SkyIslandShadowStabilityPropertyTest incomplete or failed validation')
        return 1
    if SOURCE_ONLY:
        print('PARTIAL: SkyIslandShadowStabilityPropertyTest source geometry units passed; '
              'author Shader/Builder, URP Deferred wiring and real atlas models not validated')
        return 2
    print('PASS: SkyIslandShadowStabilityPropertyTest full offline validation; %d tests; '
          'Unity/GPU/in-game validation not performed' % result.testsRun)
    return 0


if __name__ == '__main__':
    sys.exit(main())
