using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CrimeJJK {
  // Reuse the existing mesh builder; all recasts share these cached, genuinely 3D meshes.
  public sealed class FugaModel : IDisposable {
    readonly Mesh[] bow,arrow,burst;
    readonly Material[] materials;
    public readonly Texture2D FlameTexture;
    public int BowTriangles { get { return bow.Sum(m=>m.triangles.Length/3); } }
    public int ArrowTriangles { get { return arrow.Sum(m=>m.triangles.Length/3); } }
    public int BurstTriangles { get { return burst.Sum(m=>m.triangles.Length/3); } }
    public Bounds BowBounds { get { var b=bow[0].bounds;foreach(var m in bow)b.Encapsulate(m.bounds);return b; } }
    static Vector3 V(float x,float y,float z){return new Vector3(x,y,z);}
    static Vector3[] Curve(Func<float,Vector3> fn,int n){return Enumerable.Range(0,n+1).Select(i=>fn(i/(float)n)).ToArray();}
    static ShrineModel.Geometry[] Batches(){return Enumerable.Range(0,4).Select(i=>new ShrineModel.Geometry()).ToArray();}
    static Mesh[] Finish(ShrineModel.Geometry[] g,string name) {
      return g.Select((p,i)=>{var mesh=p.Finish("JJK_Fuga_"+name+"_"+i);mesh.colors=Enumerable.Repeat(Color.white,mesh.vertexCount).ToArray();return mesh;}).ToArray();
    }
    public FugaModel(Shader shader) {
      FlameTexture=new Texture2D(2,2,TextureFormat.RGBA32,true);
      using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("jjk.fuga.flame.png")) {
        if(stream==null)throw new InvalidDataException("Fuga flame texture missing");
        using(var bytes=new MemoryStream()){stream.CopyTo(bytes);if(!ImageConversion.LoadImage(FlameTexture,bytes.ToArray(),false))throw new InvalidDataException("Fuga flame texture decode failed");}
      }
      FlameTexture.wrapMode=TextureWrapMode.Clamp;
      Color[] colors={new Color(3.2f,2.6f,1.4f,.98f),new Color(1.8f,.85f,.1f,.19f),new Color(1,.15f,.006f,.08f),new Color(1,1,1,.88f)};
      materials=colors.Select((color,i)=>{var m=new Material(shader);m.name="JJK_fuga_surface_"+i;m.mainTexture=i==3?FlameTexture:Texture2D.whiteTexture;m.color=color;return m;}).ToArray();
      var b=Batches();BuildBow(b);bow=Finish(b,"bow");
      b=Batches();BuildArrow(b);arrow=Finish(b,"arrow");
      b=Batches();BuildBurst(b);burst=Finish(b,"burst");
    }
    static Vector3 Limb(float t,int side){return V(.012f*Mathf.Sin(t*9),side*.73f*t,.24f*Mathf.Sin(t*Mathf.PI)+.27f*Mathf.Pow(t,4));}
    // Volumetric tapered flame sheets curve in space. Back faces are explicit, no camera-facing billboard.
    static void Ribbon(ShrineModel.Geometry g,Func<float,Vector3> path,Vector3 across,float width,int n) {
      for(int i=0;i<n;i++) {
        float t=i/(float)n,u=(i+1)/(float)n;
        float w=width*Mathf.Pow(1-t,.8f)*(.55f+.45f*Mathf.Sin(Mathf.PI*t));
        float next=width*Mathf.Pow(1-u,.8f)*(.55f+.45f*Mathf.Sin(Mathf.PI*u));
        var a=path(t)-across*w;var b=path(t)+across*w;var c=path(u)+across*next;var d=path(u)-across*next;
        g.QuadUV(a,b,c,d,new Vector2(0,t),new Vector2(1,t),new Vector2(1,u),new Vector2(0,u));
        g.QuadUV(d,c,b,a,new Vector2(0,u),new Vector2(1,u),new Vector2(1,t),new Vector2(0,t));
      }
    }
    static void BuildBow(ShrineModel.Geometry[] b) {
      foreach(int side in new[]{-1,1}) {
        int s=side;var path=Curve(t=>Limb(t,s),48);
        b[0].Tube(path,t=>.013f*(1-t*.72f),9);
        b[1].Tube(path,t=>.026f*(1-t*.74f),10);
        b[2].Tube(path,t=>.044f*(1-t*.8f),10);
        Ribbon(b[3],t=>Limb(t,s),Vector3.right,.12f,48);
        Ribbon(b[3],t=>Limb(t,s),Vector3.forward,.12f,48);
        for(int k=0;k<7;k++) {
          float at=.12f+k*.12f;var p=Limb(at,s);float sign=k%2==0?1:-1;
          Func<float,Vector3> w=t=>p+V(sign*(.02f+Mathf.Sin(t*Mathf.PI)*.075f),s*t*.30f,-t*.13f)+Vector3.right*Mathf.Sin(t*7+k)*.016f;
          Ribbon(b[3],w,Vector3.right,.085f,13);
        }
        var tip=Limb(1,s);
        b[1].Tube(Curve(t=>tip+V(.025f*Mathf.Sin(t*5),s*t*.12f,-t*t*.1f),16),t=>.01f*(1-t)+.001f,7);
      }
      for(int j=0;j<3;j++) {
        float phase=j*Mathf.PI*2/3;
        b[1].Tube(Curve(t=>V(Mathf.Cos(t*16+phase)*.03f,(t-.5f)*.22f,Mathf.Sin(t*16+phase)*.03f),30),t=>.008f,6);
      }
    }
    static void BuildArrow(ShrineModel.Geometry[] b) {
      var shaft=Curve(t=>V(0,0,-.55f+t*1.82f),32);
      b[0].Tube(shaft,t=>.01f,9);b[1].Tube(shaft,t=>.02f,9);b[2].Tube(shaft,t=>.038f,10);
      Ribbon(b[3],t=>V(.012f*Mathf.Sin(t*13),.02f*Mathf.Sin(t*17),1.28f-t*1.88f),Vector3.up,.11f,36);
      for(int j=0;j<4;j++) {
        float phase=j*Mathf.PI*.5f;
        Func<float,Vector3> spiral=t=>V(Mathf.Cos(t*14+phase+Mathf.Sin(t*17)*.3f)*(.025f+Mathf.Sin(t*Mathf.PI)*.045f),Mathf.Sin(t*14+phase)*(.025f+Mathf.Sin(t*Mathf.PI)*.045f),-.5f+t*1.78f);
        Ribbon(b[3],spiral,V(Mathf.Cos(phase),Mathf.Sin(phase),0),.065f,38);
        var radial=V(Mathf.Cos(phase),Mathf.Sin(phase),0);
        Func<float,Vector3> head=t=>radial*(Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.16f))*Mathf.Pow(1-t,.7f)*.19f)+Vector3.forward*(1.02f+t*.65f);
        b[0].Tube(Curve(head,18),t=>.008f*(1-t)+.001f,6);
        b[1].Tube(Curve(head,18),t=>.021f*(1-t)+.001f,8);
        Ribbon(b[2],head,Vector3.Cross(radial,Vector3.forward),.046f,18);
        var tail=V(0,0,-.39f);
        Ribbon(b[3],t=>tail+radial*(Mathf.Sin(t*Mathf.PI)*.12f)+Vector3.back*t*.34f,Vector3.Cross(radial,Vector3.forward),.04f,16);
      }
      b[0].Tube(new[]{V(0,0,1.1f),V(0,0,1.7f)},t=>.018f*(1-t)+.0005f,10);
      b[0].Tube(Curve(t=>V(0,0,1.04f+t*.66f),12),t=>.075f*Mathf.Pow(1-t,1.2f)+.0005f,4);
    }
    static void BuildBurst(ShrineModel.Geometry[] b) {
      for(int k=0;k<12;k++) {
        float a=k*Mathf.PI*2/12;var radial=V(Mathf.Cos(a),0,Mathf.Sin(a));
        Func<float,Vector3> flame=t=>radial*(.12f+t*.8f+Mathf.Sin(t*8+k)*t*.12f)+Vector3.up*(t*(1.9f+.4f*Mathf.Sin(k*2.1f)));
        b[0].Tube(Curve(flame,20),t=>.01f*(1-t)+.001f,7);
        b[1].Tube(Curve(flame,20),t=>.027f*(1-t)+.001f,9);
        Ribbon(b[3],flame,Vector3.Cross(radial,Vector3.up),.32f,20);
      }
    }
    FugaMotion Draw(GameObject root,Mesh[] meshes,int mode) {
      var parts=new Renderer[meshes.Length];
      for(int i=0;i<meshes.Length;i++) {
        var go=new GameObject("JJK_fuga_mesh_"+mode+"_"+i);go.layer=root.layer;go.transform.SetParent(root.transform,false);
        go.AddComponent<MeshFilter>().sharedMesh=meshes[i];var render=go.AddComponent<MeshRenderer>();render.sharedMaterial=materials[i];render.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;render.receiveShadows=false;parts[i]=render;
      }
      var motion=root.AddComponent<FugaMotion>();motion.Mode=mode;motion.Parts=parts;return motion;
    }
    public void Arrow(GameObject root,bool trail) {
      Draw(root,arrow,1);
      if(trail) {
        var r=root.AddComponent<TrailRenderer>();r.sharedMaterial=materials[1];r.time=.28f;r.minVertexDistance=.09f;r.startWidth=.13f;r.endWidth=0;
        var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(1,.92f,.45f),0),new GradientColorKey(new Color(1,.14f,.005f),1)},new[]{new GradientAlphaKey(.7f,0),new GradientAlphaKey(0,1)});r.colorGradient=gradient;
      }
      var light=root.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.4f,.03f);light.range=5;light.intensity=1.4f;light.shadows=LightShadows.None;
    }
    public void Bow(GameObject root) {
      var motion=Draw(root,bow,0);
      var child=new GameObject("JJK_fuga_nocked_arrow");child.layer=root.layer;child.transform.SetParent(root.transform,false);Arrow(child,false);
      var cord=new GameObject("JJK_fuga_energy_string");cord.layer=root.layer;cord.transform.SetParent(root.transform,false);var line=cord.AddComponent<LineRenderer>();line.sharedMaterial=materials[0];line.useWorldSpace=false;line.positionCount=3;line.startWidth=line.endWidth=.006f;line.startColor=line.endColor=Color.white;motion.String=line;motion.Nocked=child.transform;
    }
    public void SetCharge(GameObject root,float progress){var m=root.GetComponent<FugaMotion>();if(m)m.Progress=Mathf.Clamp01(progress);}
    public void Impact(GameObject root){Draw(root,burst,2);}
    public void Export(string directory) {
      Directory.CreateDirectory(directory);WriteObj(Path.Combine(directory,"fire-bow.obj"),bow);WriteObj(Path.Combine(directory,"fire-arrow.obj"),arrow);WriteObj(Path.Combine(directory,"fire-burst.obj"),burst);
      var text=new StringBuilder();for(int i=0;i<materials.Length;i++){var c=materials[i].color;text.AppendFormat(CultureInfo.InvariantCulture,"newmtl Flame_{0}\nKd {1} {2} {3}\nKe {1} {2} {3}\nd {4}\n",i,c.r,c.g,c.b,c.a);if(i==3)text.Append("map_Kd flame.png\nmap_d flame.png\n");text.Append('\n');}File.WriteAllText(Path.Combine(directory,"fuga.mtl"),text.ToString());File.WriteAllBytes(Path.Combine(directory,"flame.png"),ImageConversion.EncodeToPNG(FlameTexture));
    }
    static void WriteObj(string path,Mesh[] meshes) {
      var text=new StringBuilder("mtllib fuga.mtl\n");int offset=1;
      for(int i=0;i<meshes.Length;i++) {
        text.Append("o Flame_").Append(i).Append("\nusemtl Flame_").Append(i).Append('\n');var m=meshes[i];
        foreach(var v in m.vertices)text.AppendFormat(CultureInfo.InvariantCulture,"v {0} {1} {2}\n",v.x,v.y,v.z);
        foreach(var uv in m.uv)text.AppendFormat(CultureInfo.InvariantCulture,"vt {0} {1}\n",uv.x,uv.y);
        foreach(var n in m.normals)text.AppendFormat(CultureInfo.InvariantCulture,"vn {0} {1} {2}\n",n.x,n.y,n.z);
        var ids=m.triangles;for(int j=0;j<ids.Length;j+=3){text.Append('f');for(int k=0;k<3;k++){int id=ids[j+k]+offset;text.Append(' ').Append(id).Append('/').Append(id).Append('/').Append(id);}text.Append('\n');}offset+=m.vertexCount;
      }
      File.WriteAllText(path,text.ToString());
    }
    public void Dispose(){foreach(var m in bow.Concat(arrow).Concat(burst))Object.Destroy(m);foreach(var mat in materials)Object.Destroy(mat);Object.Destroy(FlameTexture);}
  }
  public sealed class FugaMotion : MonoBehaviour {
    public int Mode;public float Progress;public Renderer[] Parts;public LineRenderer String;public Transform Nocked;
    readonly MaterialPropertyBlock block=new MaterialPropertyBlock();float born;
    void Start(){born=Time.time;}
    void Update() {
      float t=Time.time-born;float fade=Mode==2?1-Mathf.SmoothStep(0,1,t/.75f):1;
      if(Mode==2 && t>.8f){foreach(var r in Parts)if(r)Object.Destroy(r.gameObject);enabled=false;return;}
      for(int i=0;i<Parts.Length;i++)if(Parts[i]) {
        var color=Parts[i].sharedMaterial.color;color.a*=fade*(.9f+.1f*Mathf.Sin(t*18+i));block.SetColor("_Color",color);Parts[i].SetPropertyBlock(block);
        if(Mode==2)Parts[i].transform.localScale=Vector3.one*Mathf.Lerp(.3f,1.35f,Mathf.Clamp01(t/.4f));
        else if(i==3)Parts[i].transform.localRotation=Quaternion.Euler(0,0,Mode==1?t*210:Mathf.Sin(t*13)*1.5f);
      }
      if(String){String.SetPosition(0,new Vector3(0,.73f,.27f));String.SetPosition(1,new Vector3(0,0,Mathf.Lerp(-.1f,-.55f,Progress)));String.SetPosition(2,new Vector3(0,-.73f,.27f));}
      if(Nocked)Nocked.localPosition=new Vector3(0,0,Mathf.Lerp(.45f,0,Progress));
    }
  }
}
