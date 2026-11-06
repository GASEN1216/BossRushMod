using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static int assertions;
    private static void Check(bool ok, string message) { assertions++; if (!ok) throw new Exception(message); }
    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static void Near(float actual, float expected, string label) { Check(Finite(actual) && Math.Abs(actual-expected)<0.006f,label+": "+actual+" expected "+expected); }
    private static Transform Child(Transform parent, string name) { var go=new GameObject(name); go.transform.SetParent(parent,false); return go.transform; }

    private static int Main()
    {
        string input=Path.Combine(AppContext.BaseDirectory,"..","..","..","lagoon-mask.bin");
        // run_project_fixture 使用 run-*/bin/Release/net8.0；输入在本次夹具输出根，不依赖默认 bin。
        DirectoryInfo search=new DirectoryInfo(AppContext.BaseDirectory);
        while(search!=null && !File.Exists(Path.Combine(search.FullName,"lagoon-mask.bin"))) search=search.Parent;
        if(search==null) throw new Exception("missing real lagoon input");
        input=Path.Combine(search.FullName,"lagoon-mask.bin");
        Texture2D texture; Vector4 rect;
        using(var reader=new BinaryReader(File.OpenRead(input)))
        {
            int width=reader.ReadInt32(),height=reader.ReadInt32();
            rect=new Vector4(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
            texture=new Texture2D { width=width,height=height,Pixels=new Color[width*height] };
            for(int i=0;i<texture.Pixels.Length;i++) texture.Pixels[i]=new Color(0,reader.ReadSingle(),0);
        }
        var root=new GameObject("TidegateWorld");
        Transform markers=Child(root.transform,"Markers");
        Child(markers,"PlayerSpawn"); Child(Child(markers,"Exits"),"Exit_road");
        Child(Child(markers,"Loot"),"LOOT_test"); Child(Child(markers,"Spawns"),"SPAWN_test");
        Renderer renderer=Child(Child(root.transform,"Visual"),"VIS_Water").gameObject.AddComponent<Renderer>();
        renderer.sharedMaterial=new Material();
        renderer.sharedMaterial.Values["_HeightTex"]=texture;
        renderer.sharedMaterial.Values["_HeightRect"]=rect;
        var world=new TidegateWorld(root);
        Check(texture.GetPixelsCalls==1,"mask must be fetched once");
        Check(world.InLagoon(new Vector3(62,0,20)),"real inner lagoon must use lagoon level");
        Check(!world.InLagoon(new Vector3(-60,0,-40)),"external sea must use sea level");
        float[] mask=texture.Pixels.Select(pixel=>pixel.g).ToArray();
        // 由实际 RGHalf 遮罩按着色器归一化 uv 独立计算的 GPU 参照值；旧 CPU 坐标会将前两点的海 / 湖判反。
        CheckMask(world,mask,texture,rect,56.75f,-16f,.492541504f,"shader boundary sea");
        CheckMask(world,mask,texture,rect,80.75f,25.5f,.848585644f,"shader boundary lagoon");
        CheckMask(world,mask,texture,rect,56.75f,-17f,0f,"shader margin sea");
        CheckMask(world,mask,texture,rect,56.75f,-15f,.992218018f,"shader margin lagoon");
        CheckMask(world,mask,texture,rect,80.75f,24.5f,1f,"shader second margin lagoon");
        CheckMask(world,mask,texture,rect,40f,33f,.191692577f,"old shore 1 G");
        CheckMask(world,mask,texture,rect,41f,32f,.191743326f,"old shore 2 G");
        CheckMask(world,mask,texture,rect,42f,30f,.192476878f,"old shore 3 G");
        CheckMask(world,mask,texture,rect,43f,28f,.193201204f,"old shore 4 G");
        int samples;
        var clock=Stopwatch.StartNew();
        List<TidegateWaterRegion> regions=world.CreateWaterRegions(out samples);
        clock.Stop();
        Check(samples>0 && samples<250000,"scan must stay near active mask, samples="+samples);
        Check(regions.Count<600,"mask must be merged, regions="+regions.Count);
        Check(texture.GetPixelsCalls==1,"region generation must use cached pixels");
        Check(regions.Any(r=>r.Lagoon) && regions.Any(r=>!r.Lagoon),"both water regions exist simultaneously");
        double area=0;
        foreach(TidegateWaterRegion region in regions)
        {
            Check(region.MaxX>region.MinX && region.MaxZ>region.MinZ,"positive area");
            area+=(region.MaxX-region.MinX)*(region.MaxZ-region.MinZ);
        }
        Check(Math.Abs(area-rect.z*rect.w)<0.02,"water partition must cover all map, area="+area);
        for(int i=0;i<regions.Count;i++) for(int j=i+1;j<regions.Count;j++)
        {
            TidegateWaterRegion a=regions[i],b=regions[j];
            float x=Math.Min(a.MaxX,b.MaxX)-Math.Max(a.MinX,b.MinX),z=Math.Min(a.MaxZ,b.MaxZ)-Math.Max(a.MinZ,b.MinZ);
            Check(x<=0 || z<=0,"regions have overlapping interiors "+i+" "+j);
        }
        // 手写 2x2 例子约束 GPU 纹素中心：uv=.25 是首像素中心，uv=.5 是四像素的中点；边缘 Clamp。
        Near(TidegateWaterRegions.Sample(new[]{0f,1f,1f,0f},2,2,.5f,.5f),.5f,"GPU bilinear center");
        Near(TidegateWaterRegions.Sample(new[]{0f,1f,1f,0f},2,2,.25f,.25f),0,"GPU first texel center");
        Near(TidegateWaterRegions.Sample(new[]{0f,1f,1f,0f},2,2,0f,1f),1,"GPU edge clamp");
        int emptySamples;
        var empty=TidegateWaterRegions.Build(new float[4],2,2,0,0,2,2,out emptySamples);
        Check(empty.Count==1 && !empty[0].Lagoon && emptySamples==0,"empty mask stays one sea volume");

        var tide=new TidegateTide { Level=.45f,LagoonLevel=.62f };
        var water=new TidegateWater(world,tide,null,()=>true);
        BoxCollider[] boxes=root.GetComponentsInChildren<BoxCollider>(true);
        Check(boxes.Length==regions.Count,"actual production water boxes must match full partition");
        Check(boxes.Select(b=>b.gameObject).Distinct().Count()==2,"all volumes share two objects");
        Check(boxes.All(b=>b.isTrigger && b.enabled),"all spatial volumes must remain queryable");
        CheckTops(boxes,.45f,.62f,"first Tick");
        CheckSpace(world,new Vector3(-60,-1.6f,-40),false,.45f,"sea at first frame");
        CheckSpace(world,new Vector3(62,-1.6f,20),true,.62f,"lagoon at first frame");
        foreach(float[] levels in new[]{new[]{-1.5f,.62f},new[]{-1.5f,-1.5f},new[]{.45f,.45f},new[]{-.7f,-.2f}})
        {
            tide.Level=levels[0];tide.LagoonLevel=levels[1];water.Tick();
            CheckTops(boxes,levels[0],levels[1],"tidal change");
            CheckSpace(world,new Vector3(-60,-2,-40),false,levels[0],"simultaneous sea");
            CheckSpace(world,new Vector3(62,-2,20),true,levels[1],"simultaneous lagoon");
        }
        tide.Level=-1.5f;tide.LagoonLevel=.62f;water.Tick();
        CheckSpace(world,new Vector3(56.75f,-1.6f,-17f),false,-1.5f,"shader margin physical sea");
        CheckSpace(world,new Vector3(56.75f,-1.6f,-15f),true,.62f,"shader margin physical lagoon");
        CheckSpace(world,new Vector3(80.75f,-1.6f,25.5f),true,.62f,"shader corrected physical lagoon");
        CheckSpace(world,new Vector3(80.75f,-1.6f,24.5f),true,.62f,"shader second margin physical lagoon");
        foreach(Vector3 p in new[]{new Vector3(40,.075f,33),new Vector3(41,.0375f,32),new Vector3(42,.0375f,30),new Vector3(43,.0375f,28)})
        {
            Check(!world.InLagoon(p),"former AABB false lagoon must be outside mask "+p.x+","+p.z);
            CheckSpace(world,new Vector3(p.x,-1.6f,p.z),false,-1.5f,"former AABB false lagoon submerged query");
            var hits=new Collider[boxes.Length];
            Check(Physics.OverlapSphereNonAlloc(p+Vector3.up*.35f,.2f,hits,1<<4)==0,"dry shore must not trigger swimming "+p.x+","+p.z);
        }
        water.Dispose();
        Check(boxes.All(b=>b==null),"disposing volumes destroys every collider");
        Check(texture.GetPixelsCalls==1,"all ticks share cached mask");
        Console.WriteLine("TidegateWater PASS assertions="+assertions+" boxes="+regions.Count+" sea="+regions.Count(r=>!r.Lagoon)
            +" lagoon="+regions.Count(r=>r.Lagoon)+" samples="+samples+" region_ms="+clock.Elapsed.TotalMilliseconds.ToString("F2"));
        return 0;
    }

    private static void CheckTops(BoxCollider[] boxes,float sea,float lagoon,string label)
    {
        foreach(BoxCollider box in boxes)
        {
            bool lake=box.gameObject.name=="WaterVolume_Lagoon";
            Near(box.bounds.max.y,lake?lagoon:sea,label+" top "+box.gameObject.name);
            Near(box.bounds.min.y,-14,label+" bottom "+box.gameObject.name);
        }
    }

    private static void CheckMask(TidegateWorld world,float[] mask,Texture2D texture,Vector4 rect,float x,float z,float expected,string label)
    {
        float actual=TidegateWaterRegions.Sample(mask,texture.width,texture.height,(x-rect.x)/rect.z,(z-rect.y)/rect.w);
        Check(Finite(actual) && Math.Abs(actual-expected)<0.00002f,label+": "+actual+" expected "+expected);
        Check(world.InLagoon(new Vector3(x,0,z))==(expected>.5f),label+" world classifier");
        Console.WriteLine("GPU_MASK x="+x+" z="+z+" g="+actual.ToString("R"));
    }

    private static void CheckSpace(TidegateWorld world,Vector3 point,bool lagoon,float top,string label)
    {
        Check(world.InLagoon(point)==lagoon,label+" classifier");
        var hits=new Collider[1024];
        int found=Physics.OverlapSphereNonAlloc(point,.2f,hits,1<<4);
        Check(found>0 && found<=hits.Length,label+" missing physical volume");
        for(int i=0;i<found;i++)
        {
            Check(hits[i].gameObject.name==(lagoon?"WaterVolume_Lagoon":"WaterVolume_Sea"),label+" wrong water region");
            Near(hits[i].bounds.max.y,top,label+" actual queried top");
        }
    }
}
