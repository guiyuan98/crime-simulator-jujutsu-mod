using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CrimeJJK {
  // One cached, material-batched real mesh. Recasts reuse geometry instead of hundreds of objects.
  public sealed class ShrineModel : IDisposable {
    readonly Mesh[] meshes;
    readonly Material[] materials;
    readonly Texture2D[] textures;
    public readonly int Triangles;
    public readonly Bounds Bounds;
    public int Parts { get { return meshes.Length; } }
    public ShrineModel() {
      var batches=Enumerable.Range(0,8).Select(i=>new Geometry()).ToArray();
      Build(batches);
      meshes=batches.Select((g,i)=>g.Finish("JJK_Shrine_"+i)).ToArray();Triangles=meshes.Sum(m=>m.triangles.Length/3);
      var bounds=meshes[0].bounds;for(int i=1;i<meshes.Length;i++)bounds.Encapsulate(meshes[i].bounds);Bounds=bounds;
      Color[] colors={new Color(.31f,.32f,.34f),new Color(.18f,.19f,.205f),new Color(.31f,.052f,.038f),new Color(.76f,.69f,.54f),new Color(.2f,.13f,.07f),new Color(.012f,.001f,.002f),new Color(.18f,.021f,.015f),new Color(.65f,.012f,.003f)};
      textures=new Texture2D[8];materials=new Material[8];var shader=Shader.Find("Standard") ?? Shader.Find("Unlit/Texture");
      for(int i=0;i<8;i++) {
        textures[i]=Texture(i);var mat=new Material(shader);mat.name="JJK_shrine_material_"+i;mat.mainTexture=textures[i];mat.color=i==2?Color.white:colors[i];
        if(mat.HasProperty("_Glossiness"))mat.SetFloat("_Glossiness",i==4?.38f:.12f);
        if(mat.HasProperty("_Metallic"))mat.SetFloat("_Metallic",i==4?.55f:0);
        mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",colors[i]*(i==7?2.6f:i==5?.07f:i==2?.035f:.12f));materials[i]=mat;
      }
    }
    static Texture2D Texture(int kind) {
      var t=new Texture2D(256,256,TextureFormat.RGB24,true);t.name="JJK_shrine_surface_"+kind;t.wrapMode=TextureWrapMode.Repeat;
      if(kind==2) {
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("jjk.shrine.wood.png")) {
          if(stream==null)throw new InvalidDataException("Shrine wood texture missing");
          using(var bytes=new MemoryStream()){stream.CopyTo(bytes);if(!ImageConversion.LoadImage(t,bytes.ToArray(),false))throw new InvalidDataException("Shrine wood texture decode failed");}
        }
        t.wrapMode=TextureWrapMode.Repeat;return t;
      }
      for(int y=0;y<256;y++)for(int x=0;x<256;x++) {
        float n=Mathf.PerlinNoise(x*.075f+kind*7,y*.075f),fine=Mathf.PerlinNoise(x*.72f,y*.72f),v=.65f+n*.38f+fine*.12f;
        if(kind==2)v*=.65f+.35f*Mathf.PerlinNoise(x*.25f,y*.012f);
        if(kind==0)v*=Mathf.PerlinNoise(x*.035f,y*.035f)>.54f?.94f:.74f;
        if(kind==1)v*=.75f+.25f*Mathf.Abs(Mathf.Sin(x*.12f+Mathf.PerlinNoise(x*.04f,y*.04f)));
        if(kind==3)v=.86f+n*.12f+fine*.04f;
        if(kind==5)v*=.5f;
        t.SetPixel(x,y,new Color(v,v,v));
      }
      t.Apply();return t;
    }
    public void Instantiate(GameObject root) {
      for(int i=0;i<meshes.Length;i++) {
        var go=new GameObject("JJK_shrine_mesh_"+i);go.layer=root.layer;go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=meshes[i];
        var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=materials[i];r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;r.receiveShadows=true;
      }
      Light(root,new Vector3(-4,7,-5),new Color(1,.9f,.78f),2.6f,18,true);
      Light(root,new Vector3(4,6,3),new Color(.65f,.73f,1),2.1f,16);
      Light(root,new Vector3(0,1.9f,-.7f),new Color(1,.04f,.015f),1.8f,4);
    }
    static void Light(GameObject root,Vector3 p,Color color,float power,float range,bool shadows=false) {
      var go=new GameObject("JJK_shrine_lighting");go.layer=root.layer;go.transform.SetParent(root.transform,false);go.transform.localPosition=p;
      var l=go.AddComponent<Light>();l.type=LightType.Point;l.color=color;l.intensity=power;l.range=range;l.shadows=shadows?LightShadows.Soft:LightShadows.None;l.shadowStrength=.7f;l.shadowBias=.025f;l.shadowNormalBias=.12f;l.cullingMask=1<<root.layer;
    }
    static Vector3 V(float x,float y,float z){return new Vector3(x,y,z);}
    static Vector3[] Curve(Func<float,Vector3> point,int segments) {return Enumerable.Range(0,segments+1).Select(i=>point(i/(float)segments)).ToArray();}
    static float RoofY(float x,float z,float width,float depth,float y,float rise) {
      float p=Mathf.Abs(x)/width,q=Mathf.Abs(z)/depth;
      return y+rise*(1-p)+.72f*Mathf.Pow(p,7)+.22f*Mathf.Pow(q,8);
    }
    static Vector3 Maw(float angle,float radius) {
      float c=Mathf.Cos(angle),s=Mathf.Sin(angle);
      return V(Mathf.Sign(c)*Mathf.Pow(Mathf.Abs(c),.72f)*1.88f*radius,Mathf.Sign(s)*Mathf.Pow(Mathf.Abs(s),.72f)*1.55f*radius,(1-radius)*1.3f);
    }
    static void Roof(Geometry[] b,float width,float depth,float y,float rise) {
      const int nx=48,nz=24;
      for(int ix=0;ix<nx;ix++)for(int iz=0;iz<nz;iz++) {
        float x0=-width+2*width*ix/nx,x1=-width+2*width*(ix+1)/nx,z0=-depth+2*depth*iz/nz,z1=-depth+2*depth*(iz+1)/nz;
        var a=V(x0,RoofY(x0,z0,width,depth,y,rise),z0);var c=V(x1,RoofY(x1,z1,width,depth,y,rise),z1);
        var d=V(x1,RoofY(x1,z0,width,depth,y,rise),z0);var e=V(x0,RoofY(x0,z1,width,depth,y,rise),z1);
        b[1].Quad(a,e,c,d);b[2].Quad(d-Vector3.up*.2f,c-Vector3.up*.2f,e-Vector3.up*.2f,a-Vector3.up*.2f);
      }
      int lanes=(int)(depth*2/.26f);
      // Seal the eave thickness: an open edge looked like floating spikes from the side.
      for(int side=-1;side<=1;side+=2)for(int iz=0;iz<nz;iz++) {
        float x=side*width,z0=-depth+2*depth*iz/nz,z1=-depth+2*depth*(iz+1)/nz;
        var a=V(x,RoofY(x,z0,width,depth,y,rise)+.02f,z0);var c=V(x,RoofY(x,z1,width,depth,y,rise)+.02f,z1);
        if(side<0)b[1].Quad(a,a-Vector3.up*.35f,c-Vector3.up*.35f,c);else b[1].Quad(c,c-Vector3.up*.35f,a-Vector3.up*.35f,a);
      }
      for(int j=0;j<=lanes;j++) {
        float z=-depth+j*2*depth/lanes;
        for(int side=-1;side<=1;side+=2) {
          int s=side;
          b[1].Tube(Curve(t=>{float x=t*width*s;return V(x,RoofY(x,z,width,depth,y,rise)+.07f,z);},24),t=>.065f,6);
        }
      }
      for(int side=-1;side<=1;side+=2) {
        float z=depth*side;
        b[1].Tube(Curve(t=>{float x=(t*2-1)*width;return V(x,RoofY(x,z,width,depth,y,rise)+.09f,z);},48),t=>.13f,8);
        b[2].Tube(Curve(t=>{float x=(t*2-1)*width;return V(x,RoofY(x,z,width,depth,y,rise)-.2f,z+.04f*side);},48),t=>.14f,6);
        for(int x=-1;x<=1;x+=2) {
          int s=x;var start=V(s*width,RoofY(width,z,width,depth,y,rise)+.02f,z);
          b[3].Tube(Curve(t=>start+V(s*(t*.7f+.2f*Mathf.Sin(t*Mathf.PI)),t*t*1.05f,t*.24f*side),16),t=>.18f*Mathf.Pow(1-t,.8f)+.008f,9);
        }
        float front=side*(depth-.22f);
        b[2].Triangle(V(-width*.67f,y+.12f,front),V(0,y+rise-.14f,front),V(width*.67f,y+.12f,front),side<0);
        b[4].Tube(new[]{V(-width*.66f,y+.12f,front-.025f*side),V(0,y+rise-.1f,front-.025f*side),V(width*.66f,y+.12f,front-.025f*side)},t=>.035f,6);
      }
      b[1].Tube(new[]{V(0,y+rise+.12f,-depth),V(0,y+rise+.12f,depth)},t=>.15f,12);
      Skull(b,V(0,y+.5f,-depth-.12f),.27f);
    }
    static void Skull(Geometry[] b,Vector3 p,float size) {
      b[3].Ellipsoid(p+V(0,.2f,0)*size,V(.43f,.46f,.34f)*size,16,10);
      b[3].Ellipsoid(p+V(0,-.1f,-.23f)*size,V(.27f,.32f,.33f)*size,12,8);
      foreach(int side in new[]{-1,1}) {
        var eye=p+V(.22f*side,.21f,-.3f)*size;
        b[5].Ellipsoid(eye,V(.155f,.17f,.08f)*size,12,8);
        b[3].Tube(Curve(t=>eye+V(Mathf.Cos(t*Mathf.PI*2)*.18f,Mathf.Sin(t*Mathf.PI*2)*.19f,-.015f)*size,20),t=>.025f*size,5);
        var start=p+V(side*.33f,.45f,.025f)*size;
        b[3].Tube(Curve(t=>start+V(side*(t*.9f-.25f*t*t),t*t*1.1f,t*.2f)*size,15),t=>(.13f*(1-t)+.005f)*size,8);
      }
      b[5].Triangle(p+V(-.08f,-.08f,-.54f)*size,p+V(.08f,-.08f,-.54f)*size,p+V(0,.09f,-.54f)*size,false);
      for(int i=0;i<6;i++){float x=(i-2.5f)*.068f;var q=p+V(x,-.31f,-.45f)*size;b[3].Tube(new[]{q,q+Vector3.down*.17f*size},t=>(.029f*(1-t)+.003f)*size,5);}
    }
    static void Build(Geometry[] b) {
      // Dry-laid stone platform and real stair treads, not a flat effect image.
      for(int level=0;level<3;level++) {
        float w=5.1f-level*.23f,d=3.5f-level*.15f,y=.14f+level*.25f;
        b[0].Box(V(0,y,0),V(w*2,.27f,d*2));
        for(int i=0;i<14;i++){float x=-w+(i+.5f)*w*2/14;b[0].Box(V(x,y,-d-.025f),V(w*2/14-.035f,.23f,.22f));b[0].Box(V(x,y,d+.025f),V(w*2/14-.035f,.23f,.22f));}
        for(int side=-1;side<=1;side+=2)for(int i=0;i<9;i++)b[0].Box(V(side*(w+.02f),y,-d+(i+.5f)*2*d/9),V(.2f,.23f,2*d/9-.035f));
      }
      for(int i=0;i<5;i++)b[0].Box(V(0,.07f+i*.15f,-4.7f+i*.27f),V(4.6f,.16f,.58f));
      // Four cylindrical carved pillars and bracket stacks support BOTH roof tiers.
      for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2) {
        var p=V(x*2.8f,.76f,z*1.65f);
        b[0].Box(p+Vector3.up*.05f,V(.95f,.22f,.95f));
        b[2].Tube(new[]{p+Vector3.up*.18f,p+Vector3.up*3.65f},t=>.32f,18);
        foreach(float h in new[]{.27f,.54f,3.25f,3.54f})b[4].Tube(new[]{p+Vector3.up*h,p+Vector3.up*(h+.10f)},t=>.365f,18);
        for(int k=0;k<3;k++){
          b[2].Box(p+V(0,3.25f+k*.19f,0),V(1.1f+k*.3f,.16f,.42f));b[2].Box(p+V(0,3.36f+k*.19f,0),V(.4f,.15f,1+k*.27f));
        }
      }
      for(int z=-1;z<=1;z+=2)for(int k=0;k<3;k++)b[2].Box(V(0,4.15f+k*.18f,z*1.65f),V(6.2f,.2f,.32f));
      for(int x=-1;x<=1;x+=2)b[2].Box(V(x*2.8f,4.27f,0),V(.32f,.25f,4.5f));
      // Dense small corbels under the broad eaves produce an architectural, not blocky silhouette.
      for(int side=-1;side<=1;side+=2)for(int i=0;i<21;i++) {
        float x=(i-10)*.42f;b[2].Box(V(x,4.44f,side*2.45f),V(.13f,.18f,1.35f));b[4].Box(V(x,4.35f,side*2.9f),V(.11f,.16f,.14f));
      }
      Roof(b,5.15f,3.35f,4.48f,1.24f);
      b[2].Box(V(0,5.65f,0),V(5.2f,.5f,3.3f));
      for(int side=-1;side<=1;side+=2)for(int i=0;i<15;i++)b[2].Box(V((i-7)*.39f,5.98f,side*1.8f),V(.12f,.22f,.8f));
      Roof(b,3.7f,2.28f,6.08f,.94f);
      // The shrine has a deep physical body. These are shrine panels, not a domain barrier.
      b[2].Box(V(0,2.45f,1.95f),V(5.35f,3.38f,.26f));
      for(int side=-1;side<=1;side+=2) {
        b[2].Box(V(side*2.6f,2.45f,.25f),V(.18f,3.35f,3.6f));
        for(int i=0;i<9;i++)b[4].Box(V(side*2.71f,2.45f,-1.3f+i*.36f),V(.06f,3.1f,.045f));
      }
      for(int i=0;i<13;i++)b[4].Box(V((i-6)*.4f,2.45f,2.1f),V(.045f,3.14f,.06f));
      for(float h=1.15f;h<4;h+=1.25f)b[2].Box(V(0,h,2.12f),V(5.48f,.12f,.1f));
      // A deep three-dimensional mouth cavity, lip ring, separated fangs and throat cracks.
      var mouth=V(0,2.27f,-2.02f);
      for(int ring=0;ring<12;ring++)for(int j=0;j<48;j++) {
        float r0=1-ring/12f,r1=1-(ring+1)/12f,a=j*Mathf.PI*2/48,c=(j+1)*Mathf.PI*2/48;
        Func<float,float,Vector3> at=(r,t)=>mouth+Maw(t,r);
        b[5].Quad(at(r0,c),at(r0,a),at(r1,a),at(r1,c));
      }
      b[6].Tube(Curve(t=>{float a=t*Mathf.PI*2;var q=Maw(a,1.035f);q.z=Mathf.Sin(a*7)*.055f;return mouth+q;},64),t=>.12f+.025f*Mathf.Sin(t*Mathf.PI*14),10);
      for(int i=0;i<13;i++) {
        float a=.08f+(Mathf.PI-.16f)*i/12;var q=mouth+Maw(a,.99f);q.z=mouth.z-.04f;float length=.44f+.36f*Mathf.Sin(a)+.09f*Mathf.Sin(i*2.3f);
        b[3].Tube(Curve(t=>q+V(-Mathf.Cos(a)*t*.16f,-t*length,t*t*.22f),8),t=>(.11f+.04f*Mathf.Sin(a))*Mathf.Pow(1-t,.7f)+.003f,8);
      }
      for(int i=0;i<10;i++) {
        float a=Mathf.PI+.17f+(Mathf.PI-.34f)*i/9;var q=mouth+Maw(a,.98f);q.z=mouth.z-.015f;
        b[3].Tube(Curve(t=>q+V(-Mathf.Cos(a)*t*.08f,t*(.35f-.23f*Mathf.Sin(a)),t*t*.12f),7),t=>.12f*(1-t)+.003f,8);
      }
      for(int j=0;j<8;j++){
        float x=(j-3.5f)*.25f;b[7].Tube(Curve(t=>mouth+V(x+Mathf.Sin(t*17+j)*.055f,(t-.5f)*1.4f,1.12f+Mathf.Abs(t-.5f)*.12f),18),t=>.015f,5);
      }
      // Curved flank ribs and skull clusters, all actual geometry visible from side/back.
      for(int side=-1;side<=1;side+=2) {
        int s=side;
        for(int i=0;i<3;i++) {
          float z=-.95f+i*.92f;b[3].Tube(Curve(t=>V(s*(2.8f+1.05f*Mathf.Sin(Mathf.PI*t)),.8f+3.08f*t,z+.22f*Mathf.Sin(t*Mathf.PI)),24),t=>.19f*(1-t*.65f),10);
        }
        Skull(b,V(side*3.07f,1.1f,-2.39f),.93f);
        for(int i=0;i<3;i++)Skull(b,V(side*(3.75f+i*.17f),.93f,-1.4f+i*.9f),.42f+i*.045f);
        Skull(b,V(side*2.4f,.9f,2.5f),.5f);
      }
      Skull(b,V(0,7.15f,-2.3f),.4f);
      for(int side=-1;side<=1;side+=2) {
        var p=V(side*4.25f,.88f,-2.83f);b[0].Box(p,V(.44f,.2f,.44f));b[2].Box(p+Vector3.up*.43f,V(.12f,.7f,.12f));b[4].Box(p+Vector3.up*.86f,V(.45f,.48f,.45f));b[7].Box(p+V(0,.86f,-.24f),V(.19f,.29f,.012f));
        b[1].Box(p+Vector3.up*1.17f,V(.7f,.12f,.68f));
      }
    }
    public void Export(string directory) {
      Directory.CreateDirectory(directory);var culture=CultureInfo.InvariantCulture;var obj=new StringBuilder("mtllib shrine.mtl\n");var mtl=new StringBuilder();int offset=1;
      for(int i=0;i<meshes.Length;i++) {
        var m=meshes[i];obj.Append("o Shrine_").Append(i).Append("\nusemtl Surface_").Append(i).Append('\n');
        foreach(var v in m.vertices)obj.AppendFormat(culture,"v {0} {1} {2}\n",v.x,v.y,v.z);
        foreach(var uv in m.uv)obj.AppendFormat(culture,"vt {0} {1}\n",uv.x,uv.y);
        foreach(var n in m.normals)obj.AppendFormat(culture,"vn {0} {1} {2}\n",n.x,n.y,n.z);
        var indices=m.triangles;for(int j=0;j<indices.Length;j+=3){obj.Append('f');for(int k=0;k<3;k++){int index=indices[j+k]+offset;obj.Append(' ').Append(index).Append('/').Append(index).Append('/').Append(index);}obj.Append('\n');}
        offset+=m.vertexCount;var c=materials[i].color;mtl.AppendFormat(culture,"newmtl Surface_{0}\nKd {1} {2} {3}\nmap_Kd surface-{0}.png\n\n",i,c.r,c.g,c.b);File.WriteAllBytes(Path.Combine(directory,"surface-"+i+".png"),ImageConversion.EncodeToPNG(textures[i]));
      }
      File.WriteAllText(Path.Combine(directory,"shrine.obj"),obj.ToString());File.WriteAllText(Path.Combine(directory,"shrine.mtl"),mtl.ToString());
    }
    public void Dispose(){foreach(var m in meshes)Object.Destroy(m);foreach(var m in materials)Object.Destroy(m);foreach(var t in textures)Object.Destroy(t);}

    internal sealed class Geometry {
      readonly List<Vector3> vertices=new List<Vector3>();readonly List<int> indices=new List<int>();readonly List<Vector2> uvs=new List<Vector2>();
      int Add(Vector3 v,Vector2 uv){int i=vertices.Count;vertices.Add(v);uvs.Add(uv);return i;}
      static Vector2 U(Vector3 v){return new Vector2(v.x*.45f+v.z*.12f,v.y*.45f+v.z*.4f);}
      public void Triangle(Vector3 a,Vector3 b,Vector3 c,bool front){if(!front){var swap=a;a=c;c=swap;}int start=Add(a,U(a));Add(b,U(b));Add(c,U(c));indices.AddRange(new[]{start,start+1,start+2});}
      public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){int s=Add(a,U(a));Add(b,U(b));Add(c,U(c));Add(d,U(d));indices.AddRange(new[]{s,s+1,s+2,s,s+2,s+3});}
      public void QuadUV(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector2 ua,Vector2 ub,Vector2 uc,Vector2 ud){int s=Add(a,ua);Add(b,ub);Add(c,uc);Add(d,ud);indices.AddRange(new[]{s,s+1,s+2,s,s+2,s+3});}
      public void Box(Vector3 p,Vector3 size){var q=size*.5f;Vector3 a=p+V(-q.x,-q.y,-q.z),b=p+V(-q.x,q.y,-q.z),c=p+V(q.x,q.y,-q.z),d=p+V(q.x,-q.y,-q.z),e=p+V(-q.x,-q.y,q.z),f=p+V(-q.x,q.y,q.z),g=p+V(q.x,q.y,q.z),h=p+V(q.x,-q.y,q.z);Quad(a,b,c,d);Quad(h,g,f,e);Quad(e,f,b,a);Quad(d,c,g,h);Quad(b,f,g,c);Quad(e,a,d,h);}
      public void Tube(Vector3[] points,Func<float,float> radius,int sides) {
        int start=vertices.Count;
        for(int i=0;i<points.Length;i++) {
          var axis=(points[Math.Min(i+1,points.Length-1)]-points[Math.Max(0,i-1)]).normalized;
          var normal=Vector3.Cross(axis,Mathf.Abs(axis.y)>.9f?Vector3.forward:Vector3.up).normalized;var binormal=Vector3.Cross(axis,normal).normalized;
          for(int j=0;j<=sides;j++){float a=j*Mathf.PI*2/sides;Add(points[i]+(normal*Mathf.Cos(a)+binormal*Mathf.Sin(a))*radius(i/(float)(points.Length-1)),new Vector2(j/(float)sides,i/(float)(points.Length-1)*3));}
        }
        for(int i=0;i<points.Length-1;i++)for(int j=0;j<sides;j++){int a=start+i*(sides+1)+j,b=a+sides+1;indices.AddRange(new[]{a,a+1,b+1,a,b+1,b});}
        // Hard-normal end caps close roof tiles, ribs and horns at every viewing angle.
        int end=start+(points.Length-1)*(sides+1);
        for(int j=0;j<sides;j++){Triangle(points[0],vertices[start+j+1],vertices[start+j],true);Triangle(points[points.Length-1],vertices[end+j],vertices[end+j+1],true);}
      }
      public void Ellipsoid(Vector3 p,Vector3 scale,int segments,int rings) {
        int start=vertices.Count;
        for(int y=0;y<=rings;y++)for(int x=0;x<=segments;x++){float a=x*Mathf.PI*2/segments,t=y*Mathf.PI/rings;Add(p+Vector3.Scale(new Vector3(Mathf.Sin(t)*Mathf.Cos(a),Mathf.Cos(t),Mathf.Sin(t)*Mathf.Sin(a)),scale),new Vector2(x/(float)segments,y/(float)rings));}
        for(int y=0;y<rings;y++)for(int x=0;x<segments;x++){int a=start+y*(segments+1)+x,b=a+segments+1;indices.AddRange(new[]{a,a+1,b,a+1,b+1,b});}
      }
      public Mesh Finish(string name){var m=new Mesh();m.name=name;m.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;m.SetVertices(vertices);m.SetUVs(0,uvs);m.SetTriangles(indices,0);m.RecalculateNormals();m.RecalculateBounds();return m;}
    }
  }
}
