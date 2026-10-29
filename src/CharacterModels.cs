using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CrimeJJK {
  // Authored skinned heads/hair/robe; native hand, trouser and fitted-jacket topology is copied in memory.
  // No original game mesh is rewritten or exported. Native renderers retain their visibility/animation roles.
  public sealed class CharacterModels : IDisposable {
    sealed class Saved {
      public SkinnedMeshRenderer R;public Mesh Mesh;public Material[] Materials;public Transform[] Bones;public Transform Root;public Bounds Bounds;public bool Offscreen;public bool Body,Hand;
      public Saved(SkinnedMeshRenderer r,bool body,bool hand){R=r;Mesh=r.sharedMesh;Materials=r.sharedMaterials;Bones=r.bones;Root=r.rootBone;Bounds=r.localBounds;Offscreen=r.updateWhenOffscreen;Body=body;Hand=hand;}
      public void Restore(){if(!R)return;R.sharedMesh=Mesh;R.sharedMaterials=Materials;R.bones=Bones;R.rootBone=Root;R.localBounds=Bounds;R.updateWhenOffscreen=Offscreen;}
    }
    sealed class Rig {
      public Transform Root;public Transform[] Bones;public Matrix4x4[] Rest;public Dictionary<Transform,int> Indices;
      public Dictionary<HumanBodyBones,int> Human=new Dictionary<HumanBodyBones,int>();
      public Rig(FPP_Player p,IEnumerable<SkinnedMeshRenderer> body){
        Root=p.characterAnimator.transform;var list=new List<Transform>();
        for(int i=0;i<(int)HumanBodyBones.LastBone;i++){var b=p.characterAnimator.GetBoneTransform((HumanBodyBones)i);if(!b)continue;if(!list.Contains(b))list.Add(b);Human[(HumanBodyBones)i]=list.IndexOf(b);}
        foreach(var s in body)foreach(var b in s.bones)if(b && b.IsChildOf(Root) && !list.Contains(b))list.Add(b);
        Bones=list.ToArray();Indices=Bones.Select((b,i)=>new{b,i}).ToDictionary(x=>x.b,x=>x.i);
        var skeleton=p.characterAnimator.avatar.humanDescription.skeleton.GroupBy(b=>b.name).ToDictionary(g=>g.Key,g=>g.First());
        Rest=Bones.Select(b=>RestOf(b,Root,skeleton)).ToArray();
        if(P(HumanBodyBones.Head).y<1.25f || P(HumanBodyBones.LeftHand).x>-.2f)throw new InvalidOperationException("Unexpected humanoid rest pose");
      }
      static Matrix4x4 RestOf(Transform t,Transform root,Dictionary<string,SkeletonBone> map){if(!t || t==root)return Matrix4x4.identity;SkeletonBone b;var local=map.TryGetValue(t.name,out b)?Matrix4x4.TRS(b.position,b.rotation,b.scale):Matrix4x4.TRS(t.localPosition,t.localRotation,t.localScale);return RestOf(t.parent,root,map)*local;}
      public Vector3 P(HumanBodyBones id){return Rest[Human[id]].MultiplyPoint3x4(Vector3.zero);}
      public BoneWeight One(HumanBodyBones id){return new BoneWeight{boneIndex0=Human[id],weight0=1};}
      public BoneWeight Blend(HumanBodyBones a,HumanBodyBones b,float t){t=Mathf.Clamp01(t);return new BoneWeight{boneIndex0=Human[a],boneIndex1=Human[b],weight0=1-t,weight1=t};}
      public BoneWeight Torso(Vector3 v){
        float spine=P(HumanBodyBones.Spine).y,chest=P(HumanBodyBones.Chest).y;
        if(v.y<spine)return Blend(HumanBodyBones.Hips,HumanBodyBones.Spine,Mathf.InverseLerp(P(HumanBodyBones.Hips).y,spine,v.y));
        if(v.y<chest)return Blend(HumanBodyBones.Spine,HumanBodyBones.Chest,Mathf.InverseLerp(spine,chest,v.y));
        var upper=Human.ContainsKey(HumanBodyBones.UpperChest)?HumanBodyBones.UpperChest:HumanBodyBones.Chest;float top=P(upper).y;
        if(v.y<top)return Blend(HumanBodyBones.Chest,upper,Mathf.InverseLerp(chest,top,v.y));
        return Blend(upper,HumanBodyBones.Neck,Mathf.InverseLerp(top,P(HumanBodyBones.Neck).y,v.y));
      }
    }
    sealed class Skin {
      public readonly List<Vector3> V=new List<Vector3>(),N=new List<Vector3>();public readonly List<Vector2> U=new List<Vector2>();public readonly List<BoneWeight> W=new List<BoneWeight>();public readonly List<int>[] F;
      public Skin(int materials){F=Enumerable.Range(0,materials).Select(i=>new List<int>()).ToArray();}
      public void Part(ShrineModel.Geometry geometry,int material,Func<Vector3,BoneWeight> weight){var m=geometry.Finish("JJK_character_part");int start=V.Count;var vertices=m.vertices;var normals=m.normals;if(material==0 || material==1 || material==2 || material==3 || material==8){var sums=new Dictionary<Vector3Int,Vector3>();for(int i=0;i<vertices.Length;i++){var v=vertices[i];var key=new Vector3Int(Mathf.RoundToInt(v.x*100000),Mathf.RoundToInt(v.y*100000),Mathf.RoundToInt(v.z*100000));Vector3 n;sums.TryGetValue(key,out n);sums[key]=n+normals[i];}for(int i=0;i<vertices.Length;i++){var v=vertices[i];normals[i]=sums[new Vector3Int(Mathf.RoundToInt(v.x*100000),Mathf.RoundToInt(v.y*100000),Mathf.RoundToInt(v.z*100000))].normalized;}}V.AddRange(vertices);N.AddRange(normals);U.AddRange(m.uv);W.AddRange(vertices.Select(weight));F[material].AddRange(m.triangles.Select(i=>i+start));Object.Destroy(m);}
      public Mesh Finish(string name,Matrix4x4[] bind){
        foreach(var p in V)if(float.IsNaN(p.sqrMagnitude) || float.IsInfinity(p.sqrMagnitude))throw new InvalidOperationException("Non-finite avatar vertex");
        foreach(var w in W)if(Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1)>.002f)throw new InvalidOperationException("Avatar skin weights do not sum to1");
        var m=new Mesh();m.name=name;m.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;m.SetVertices(V);m.SetNormals(N);m.SetUVs(0,U);m.boneWeights=W.ToArray();m.bindposes=bind;m.subMeshCount=F.Length;for(int i=0;i<F.Length;i++)m.SetTriangles(F[i],i);m.RecalculateBounds();return m;
      }
    }
    sealed class Model {public Mesh Upper,Lower;public Material[] Materials;public int HeadVertices;}
    readonly List<Saved> saved=new List<Saved>();readonly List<Mesh> owned=new List<Mesh>();readonly List<Material> materials=new List<Material>();
    readonly List<Saved> corpseSaved=new List<Saved>();readonly List<GameObject> corpseParts=new List<GameObject>();RagdollBody corpse;
    readonly Dictionary<string,Mesh> prepared=new Dictionary<string,Mesh>();readonly Dictionary<string,Mesh> handCopies=new Dictionary<string,Mesh>();
    FPP_Player player;Rig rig;Model[] roles;Saved upper,lower;int role=-1;Texture2D fabric;readonly List<Texture2D> textures=new List<Texture2D>();
    public int CurrentRole {get{return role;}}public int BodyBones {get{return rig==null?0:rig.Bones.Length;}}
    public SkinnedMeshRenderer UpperRenderer {get{return upper==null?null:upper.R;}}public SkinnedMeshRenderer LowerRenderer {get{return lower==null?null:lower.R;}}
    public int ModifiedHands {get{return handCopies.Count;}}public int GpuHandCopies;
    public int NativeHeadVertices {get{return roles[Mathf.Max(0,role)].HeadVertices;}}
    public int HandCandidates {get{return saved.Count(s=>s.Hand);}}
    public bool Bound {get{return player && rig!=null && roles!=null;}}
    public CharacterModels(FPP_Player actor){
      try {
      player=actor;var root=actor.characterAnimator.transform;
      foreach(var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(r && r.sharedMesh){bool body=r.transform.parent==root || r.transform.parent && (r.transform.parent.name=="Decals" || r.transform.parent.name=="Radio") && r.transform.parent.parent==root;bool hand=false;if(r.name.StartsWith("Hands_",StringComparison.OrdinalIgnoreCase))for(var t=r.transform;t && t!=actor.transform;t=t.parent)if(t.name=="Camera_Pivot"){hand=true;break;}if(body || hand)saved.Add(new Saved(r,body,hand));}
      var bodySources=saved.Where(s=>s.Body).Select(s=>s.R).ToArray();rig=new Rig(actor,bodySources);
      fabric=new Texture2D(64,64,TextureFormat.RGB24,false);for(int y=0;y<64;y++)for(int x=0;x<64;x++){float v=.91f+((x+y)%2)*.06f+.025f*Mathf.Sin(x*2.1f+y*3.7f);fabric.SetPixel(x,y,new Color(v,v,v));}fabric.Apply();fabric.wrapMode=TextureWrapMode.Repeat;
      roles=new[]{Build(0),Build(1)};
      }catch{Dispose();throw;}
    }
    static Vector3 V(float x,float y,float z){return new Vector3(x,y,z);}static float H(int n){return Mathf.Repeat(Mathf.Sin(n*127.1f+19.7f)*43758.5453f,1);}
    static Vector3[] Curve(Func<float,Vector3> fn,int n){return Enumerable.Range(0,n+1).Select(i=>fn(i/(float)n)).ToArray();}
    Material Mat(string name,Color color,bool cloth,float gloss){var m=new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color"));m.name="JJK_character_"+name;m.color=color;m.SetFloat("_Glossiness",gloss);if(cloth){m.mainTexture=fabric;m.mainTextureScale=new Vector2(20,20);}materials.Add(m);return m;}
    Texture2D FaceTexture(int c){var t=new Texture2D(2,2,TextureFormat.RGB24,true);using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(c==0?"jjk.gojo.face.png":"jjk.sukuna.face.png")){if(stream==null)throw new InvalidDataException("Character albedo missing");using(var bytes=new MemoryStream()){stream.CopyTo(bytes);if(!ImageConversion.LoadImage(t,bytes.ToArray(),false))throw new InvalidDataException("Character albedo decode failed");}}t.wrapMode=TextureWrapMode.Clamp;textures.Add(t);return t;}
    Saved Source(string name){return saved.FirstOrDefault(s=>s.Body && s.R.name==name);}
    Model Build(int character){
      var head=Source("CHR1_Head");if(head==null)throw new InvalidOperationException("Native head topology unavailable");
      var palette=new List<Material>{Mat("skin_"+character,character==0?new Color(.9f,.77f,.68f):new Color(.81f,.63f,.52f),false,.2f),Mat("dark_cloth",new Color(.019f,.025f,.044f),true,.09f),Mat("ivory_robe",new Color(.84f,.82f,.77f),true,.08f),Mat("hair_"+character,character==0?new Color(.65f,.68f,.76f):new Color(.67f,.38f,.35f),false,.16f),Mat("curse_ink",new Color(.009f,.007f,.012f),false,.07f),Mat("red_iris",new Color(.53f,.012f,.025f),false,.4f),Mat("metal",new Color(.23f,.24f,.29f),false,.6f),Mat("wood_sole",new Color(.22f,.12f,.06f),false,.1f)};
      var faceMaterial=Mat("face_"+character,Color.white,false,.12f);faceMaterial.mainTexture=FaceTexture(character);palette.Add(faceMaterial);
      var a=new Skin(palette.Count);var b=new Skin(palette.Count);
      AuthoredHead(a);int headCount=a.V.Count;
      var hands=Source("CHR_Male_Palm_hands") ?? Source("CHR_Male_Hands");
      if(hands!=null)CopyNative(a,hands,0,p=>Mathf.Min(Vector3.Distance(p,rig.P(HumanBodyBones.LeftHand)),Vector3.Distance(p,rig.P(HumanBodyBones.RightHand)))<.24f);
      Torso(a,character);Arms(a,character);HairAndFace(a,character);Legs(b,character);
      var bind=rig.Rest.Select(m=>m.inverse).ToArray();var model=new Model{Upper=a.Finish("JJK_"+(character==0?"Gojo":"Sukuna")+"_upper_skin",bind),Lower=b.Finish("JJK_"+(character==0?"Gojo":"Sukuna")+"_lower_skin",bind),Materials=palette.ToArray(),HeadVertices=headCount};owned.Add(model.Upper);owned.Add(model.Lower);return model;
    }
    void AuthoredHead(Skin skin){
      var h=rig.P(HumanBodyBones.Head);var center=h+V(0,.08f,.035f);int rings=64,sides=96;
      Func<float,float,Vector3> surface=(v,a)=>{
        float y=(v-.5f)*.24f,r=Mathf.Sqrt(Mathf.Max(0,1-(y/.12f)*(y/.12f)));float jaw=v<.42f?Mathf.Lerp(.72f,1,v/.42f):1;
        float x=Mathf.Sin(a)*.085f*r*jaw,z=Mathf.Cos(a)*.079f*r,worldY=.08f+y;float front=Mathf.Pow(Mathf.Max(0,Mathf.Cos(a)),8);
        float nose=.026f*Mathf.Exp(-x*x/.00016f-(worldY-.054f)*(worldY-.054f)/.00026f)+.011f*Mathf.Exp(-x*x/.00012f-(worldY-.09f)*(worldY-.09f)/.0012f);
        float eye=-.005f*(Mathf.Exp(-(x-.033f)*(x-.033f)/.0005f)+Mathf.Exp(-(x+.033f)*(x+.033f)/.0005f))*Mathf.Exp(-(worldY-.104f)*(worldY-.104f)/.0002f);
        float lip=.0045f*Mathf.Exp(-x*x/.0011f-(worldY-.01f)*(worldY-.01f)/.00007f);return center+V(x,y,z+front*(nose+eye+lip));
      };
      var frontMesh=new ShrineModel.Geometry();var backMesh=new ShrineModel.Geometry();
      for(int y=0;y<rings;y++)for(int j=0;j<sides;j++){float t=y/(float)rings,u=(y+1)/(float)rings,a=j*Mathf.PI*2/sides,b=(j+1)*Mathf.PI*2/sides;var p=surface(t,a);var q=surface(t,b);var r=surface(u,b);var s=surface(u,a);Func<Vector3,Vector2> uv=v=>new Vector2((v.x-h.x)/.176f+.5f,(v.y-h.y+.04f)/.24f);var mesh=Mathf.Cos((a+b)*.5f)>0?frontMesh:backMesh;mesh.QuadUV(p,q,r,s,uv(p),uv(q),uv(r),uv(s));}
      skin.Part(frontMesh,8,p=>rig.One(HumanBodyBones.Head));skin.Part(backMesh,0,p=>rig.One(HumanBodyBones.Head));
      var neck=new ShrineModel.Geometry();neck.Tube(Curve(t=>V(0,Mathf.Lerp(rig.P(HumanBodyBones.Neck).y-.015f,h.y-.02f,t),h.z+.025f),12),t=>.044f,24);skin.Part(neck,0,p=>rig.Blend(HumanBodyBones.Neck,HumanBodyBones.Head,Mathf.InverseLerp(rig.P(HumanBodyBones.Neck).y-.015f,h.y-.02f,p.y)));
      for(int side=-1;side<=1;side+=2){int s=side;var ear=new ShrineModel.Geometry();ear.Ellipsoid(h+V(s*.083f,.07f,.017f),V(.013f,.03f,.017f),24,20);ear.Tube(Curve(t=>h+V(s*(.09f+.003f*Mathf.Sin(t*6.28f)),.07f+.022f*Mathf.Sin(t*6.28f),.017f+.013f*Mathf.Cos(t*6.28f)),32),t=>.003f,8);skin.Part(ear,0,p=>rig.One(HumanBodyBones.Head));}
    }

    void CopyNative(Skin dest,Saved source,int material,Func<Vector3,bool> include){
      var mesh=source.Mesh;Mesh baked=null;if(!mesh.isReadable){baked=ReadGpuMesh(mesh,source.Bones.Length);mesh=baked;}bool readable=true;Vector3[] vertices=mesh.vertices,normals=mesh.normals;Vector2[] uv=mesh.uv;BoneWeight[] weights=mesh.boneWeights;
      var poses=mesh.bindposes;var output=new Vector3[vertices.Length];var outputN=new Vector3[vertices.Length];var outputW=new BoneWeight[vertices.Length];
      for(int i=0;i<vertices.Length;i++){
        var w=weights.Length==vertices.Length?weights[i]:rig.One(HumanBodyBones.Head);var indices=new[]{w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};var values=new[]{w.weight0,w.weight1,w.weight2,w.weight3};Vector3 p=Vector3.zero,n=Vector3.zero;var mapped=new int[4];
        for(int k=0;k<4;k++)if(values[k]>0){int original=indices[k];int target;if(!readable || original>=source.Bones.Length || !rig.Indices.TryGetValue(source.Bones[original],out target))target=rig.Human[HumanBodyBones.Head];mapped[k]=target;Matrix4x4 transform=readable?rig.Rest[target]*poses[original]:rig.Rest[target]*rig.Bones[target].worldToLocalMatrix*source.R.transform.localToWorldMatrix;p+=transform.MultiplyPoint3x4(vertices[i])*values[k];n+=transform.MultiplyVector(normals.Length==vertices.Length?normals[i]:Vector3.forward)*values[k];}
        output[i]=p;outputN[i]=n.normalized;outputW[i]=new BoneWeight{boneIndex0=mapped[0],boneIndex1=mapped[1],boneIndex2=mapped[2],boneIndex3=mapped[3],weight0=w.weight0,weight1=w.weight1,weight2=w.weight2,weight3=w.weight3};
      }
      int offset=dest.V.Count;dest.V.AddRange(output);dest.N.AddRange(outputN);dest.U.AddRange(uv.Length==vertices.Length?uv:Enumerable.Repeat(Vector2.zero,vertices.Length).ToArray());dest.W.AddRange(outputW);
      int sub=readable?mesh.subMeshCount:baked.subMeshCount;for(int s=0;s<sub;s++){var triangles=readable?mesh.GetTriangles(s):baked.GetTriangles(s);int m=material==8?Math.Min(8+s,dest.F.Length-1):material;for(int i=0;i<triangles.Length;i+=3)if(include==null || include(output[triangles[i]]) || include(output[triangles[i+1]]) || include(output[triangles[i+2]])){dest.F[m].Add(offset+triangles[i]);dest.F[m].Add(offset+triangles[i+1]);dest.F[m].Add(offset+triangles[i+2]);}}
      if(baked)Object.Destroy(baked);
    }
    float RobeFront(float hip,float neck,float y,float x){float t=Mathf.InverseLerp(hip-.17f,neck-.045f,y),w=.19f+.05f*Mathf.Sin(t*Mathf.PI*.8f);return (.125f+.023f*t)*Mathf.Sqrt(Mathf.Max(.1f,1-x*x/(w*w)));}
    void Torso(Skin skin,int c){
      float hip=rig.P(HumanBodyBones.Hips).y,neck=rig.P(HumanBodyBones.Neck).y;
      if(c==0){
        CopyNative(skin,Source("Shirt6") ?? Source("Suit"),1,null);
        var collar=new ShrineModel.Geometry();for(int j=0;j<64;j++){float a=j*Mathf.PI*2/64,b=(j+1)*Mathf.PI*2/64;collar.Quad(V(Mathf.Sin(a)*.085f,neck-.06f,Mathf.Cos(a)*.077f),V(Mathf.Sin(b)*.085f,neck-.06f,Mathf.Cos(b)*.077f),V(Mathf.Sin(b)*.082f,neck+.045f,Mathf.Cos(b)*.074f),V(Mathf.Sin(a)*.082f,neck+.045f,Mathf.Cos(a)*.074f));}skin.Part(collar,1,v=>rig.Blend(HumanBodyBones.Chest,HumanBodyBones.Neck,.6f));return;
      }
      var backing=new ShrineModel.Geometry();backing.Ellipsoid(V(0,neck-.16f,0),V(.172f,.19f,.108f),48,40);skin.Part(backing,0,rig.Torso);
      var g=new ShrineModel.Geometry();int rings=36,sides=64;
      Func<float,float,Vector3> at=(t,a)=>{float fold=.0035f*Mathf.Sin(a*11+t*18)*Mathf.Sin(t*Mathf.PI);return V(Mathf.Sin(a)*(.19f+.05f*Mathf.Sin(t*Mathf.PI*.8f)+fold),Mathf.Lerp(hip-.17f,neck-.045f,t),Mathf.Cos(a)*(.125f+.023f*t+fold));};
      for(int y=0;y<rings;y++)for(int j=0;j<sides;j++){float t=y/(float)rings,u=(y+1)/(float)rings,a=j*Mathf.PI*2/sides,b=(j+1)*Mathf.PI*2/sides;g.QuadUV(at(t,a),at(t,b),at(u,b),at(u,a),new Vector2(j/(float)sides,t),new Vector2((j+1)/(float)sides,t),new Vector2((j+1)/(float)sides,u),new Vector2(j/(float)sides,u));}skin.Part(g,2,rig.Torso);
      ShoulderYoke(skin,hip,neck);
      g=new ShrineModel.Geometry();for(int i=0;i<28;i++){float t=i/28f,u=(i+1)/28f;Func<float,float,Vector3> chest=(v,side)=>{float y=Mathf.Lerp(neck-.03f,hip+.12f,v),x=Mathf.Lerp(side*.068f,.042f,v);return V(x,y,RobeFront(hip,neck,y,x)+.006f);};g.Quad(chest(t,1),chest(t,-1),chest(u,-1),chest(u,1));}skin.Part(g,0,rig.Torso);
      for(int side=0;side<2;side++){float x0=side==0?-.07f:.07f,x1=side==0?.07f:.023f;
        for(int layer=0;layer<2;layer++){g=new ShrineModel.Geometry();float width=layer==0?.044f:.009f,edge=layer==0?0:(side==0?-.015f:.015f);
          Func<float,float,Vector3> strip=(t,v)=>{float y=Mathf.Lerp(neck-.02f,hip+.045f,t),x=Mathf.Lerp(x0,x1,t)+edge+v*width;return V(x,y,RobeFront(hip,neck,y,x)+.011f+layer*.002f);};
          for(int i=0;i<32;i++){float t=i/32f,u=(i+1)/32f;g.Quad(strip(t,.5f),strip(t,-.5f),strip(u,-.5f),strip(u,.5f));}skin.Part(g,layer==0?2:1,rig.Torso);
        }
      }
      g=new ShrineModel.Geometry();for(int j=0;j<80;j++){float a=j*Mathf.PI*2/80,b=(j+1)*Mathf.PI*2/80;g.Quad(V(Mathf.Sin(a)*.239f,hip-.04f,Mathf.Cos(a)*.17f),V(Mathf.Sin(b)*.239f,hip-.04f,Mathf.Cos(b)*.17f),V(Mathf.Sin(b)*.239f,hip+.065f,Mathf.Cos(b)*.17f),V(Mathf.Sin(a)*.239f,hip+.065f,Mathf.Cos(a)*.17f));}skin.Part(g,1,v=>rig.One(HumanBodyBones.Hips));
      g=new ShrineModel.Geometry();for(int y=0;y<20;y++)for(int j=0;j<64;j++){float t=y/20f,u=(y+1)/20f,a=j*Mathf.PI*2/64,b=(j+1)*Mathf.PI*2/64;Func<float,float,Vector3> scarf=(v,q)=>V(Mathf.Cos(q)*(.094f+.004f*Mathf.Sin(v*12+q*2)),neck-.03f+v*.095f,Mathf.Sin(q)*(.085f+.004f*Mathf.Sin(v*13+q*3)));g.Quad(scarf(t,a),scarf(u,a),scarf(u,b),scarf(t,b));}skin.Part(g,1,v=>rig.Blend(HumanBodyBones.Chest,HumanBodyBones.Neck,.65f));
    }
    void ShoulderYoke(Skin skin,float hip,float neck){
      var g=new ShrineModel.Geometry();Func<float,float,Vector3> at=(t,a)=>{float w=Mathf.Lerp(.2194f,.065f,t),d=Mathf.Lerp(.148f,.065f,t);return V(Mathf.Sin(a)*w,Mathf.Lerp(neck-.045f,neck+.008f,t)+.008f*Mathf.Sin(t*Mathf.PI),Mathf.Cos(a)*d);};
      for(int y=0;y<16;y++)for(int j=0;j<64;j++){float t=y/16f,u=(y+1)/16f,a=j*Mathf.PI*2/64,b=(j+1)*Mathf.PI*2/64;g.Quad(at(t,a),at(t,b),at(u,b),at(u,a));}skin.Part(g,2,rig.Torso);
    }
    void Arms(Skin skin,int c){
      if(c==0)return;
      for(int side=0;side<2;side++){
        var up=side==0?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm;var low=side==0?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm;var hand=side==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand;
        var a=rig.P(up);var b=rig.P(low);var d=rig.P(hand);int count=42,sides=32;float end=c==0?.95f:.64f;
        Func<float,Vector3> path=t=>t<.5f?Vector3.Lerp(a,b,t*2):Vector3.Lerp(b,d,(t-.5f)*2);
        Func<float,float,Vector3> at=(t,angle)=>{var p=path(t);var tangent=(path(Mathf.Min(end,t+.005f))-path(Mathf.Max(0,t-.005f))).normalized;var normal=Vector3.Cross(tangent,Vector3.forward).normalized;var binormal=Vector3.Cross(tangent,normal);float radius=c==0?Mathf.Lerp(.072f,.044f,t):Mathf.Lerp(.075f,.155f,t/end);float fold=1+.035f*Mathf.Sin(angle*9+t*40);return p+(normal*Mathf.Cos(angle)+binormal*Mathf.Sin(angle))*(radius*fold)+(c==1?Vector3.down*t*.07f:Vector3.zero);};
        var g=new ShrineModel.Geometry();g.Ellipsoid(a+Vector3.down*.008f,V(.095f,.083f,.1f),32,24);skin.Part(g,2,p=>rig.One(up));g=new ShrineModel.Geometry();for(int i=0;i<count;i++)for(int j=0;j<sides;j++){float t=i/(float)count*end,u=(i+1)/(float)count*end,p=j*Mathf.PI*2/sides,q=(j+1)*Mathf.PI*2/sides;g.Quad(at(t,p),at(t,q),at(u,q),at(u,p));}
        Func<Vector3,BoneWeight> weight=p=>{float t=Vector3.Dot(p-a,d-a)/(d-a).sqrMagnitude;return rig.Blend(up,low,Mathf.InverseLerp(.3f,.7f,t));};skin.Part(g,c==0?1:2,weight);
        if(c==1){g=new ShrineModel.Geometry();for(int band=0;band<2;band++){float t=.86f+band*.06f;var center=Vector3.Lerp(b,d,t);var axis=(d-b).normalized;var u=Vector3.Cross(axis,Vector3.forward).normalized;var v=Vector3.Cross(axis,u);g.Tube(Curve(x=>center+(u*Mathf.Cos(x*6.283185f)+v*Mathf.Sin(x*6.283185f))*.033f,48),x=>.005f,7);}skin.Part(g,4,p=>rig.Blend(low,hand,.55f));}
      }
    }
    void Legs(Skin skin,int c){
      var pants=Source("Pants5") ?? saved.First(s=>s.Body && s.R.name.StartsWith("Pants"));if(c==0)CopyNative(skin,pants,1,null);
      var feet=Source(c==0?"Boots5":"CHR_Male_Foot");if(feet!=null)CopyNative(skin,feet,c==0?1:0,null);
      if(c==0)return;
      float hip=rig.P(HumanBodyBones.Hips).y;int rings=42,sides=80;var g=new ShrineModel.Geometry();
      Func<float,float,Vector3> at=(t,a)=>{float fold=.006f*Mathf.Sin(a*20+.25f*Mathf.Sin(t*9))*t;return V(Mathf.Sin(a)*(.235f+t*.08f+fold),Mathf.Lerp(hip-.02f,.23f,t),Mathf.Cos(a)*(.166f+t*.045f+fold));};
      for(int i=0;i<rings;i++)for(int j=0;j<sides;j++){float t=i/(float)rings,u=(i+1)/(float)rings,p=j*Mathf.PI*2/sides,q=(j+1)*Mathf.PI*2/sides;g.QuadUV(at(t,p),at(u,p),at(u,q),at(t,q),new Vector2(j/(float)sides,t),new Vector2(j/(float)sides,u),new Vector2((j+1)/(float)sides,u),new Vector2((j+1)/(float)sides,t));}
      skin.Part(g,2,p=>{var thigh=p.x<0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg;var calf=p.x<0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg;float knee=rig.P(calf).y;return p.y>knee?rig.Blend(HumanBodyBones.Hips,thigh,Mathf.InverseLerp(hip,knee,p.y)*.85f):rig.Blend(thigh,calf,Mathf.InverseLerp(knee,.23f,p.y)*.7f);});
      for(int side=0;side<2;side++){var foot=side==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot;var p=rig.P(foot);g=new ShrineModel.Geometry();g.Ellipsoid(V(p.x,.025f,p.z+.08f),V(.065f,.02f,.13f),32,10);skin.Part(g,7,v=>rig.One(foot));g=new ShrineModel.Geometry();g.Tube(new[]{V(p.x-.05f,.045f,p.z+.14f),V(p.x,.077f,p.z+.08f),V(p.x+.05f,.045f,p.z+.14f)},t=>.008f,8);skin.Part(g,1,v=>rig.One(foot));}
    }
    void HairLock(ShrineModel.Geometry g,Vector3 start,Vector3 bend,Vector3 end,float width){
      Func<float,Vector3> curve=t=>(1-t)*(1-t)*start+2*(1-t)*t*bend+t*t*end;
      Func<float,float,Vector3> at=(t,a)=>{var tangent=(curve(Mathf.Min(1,t+.01f))-curve(Mathf.Max(0,t-.01f))).normalized;var u=Vector3.Cross(tangent,Mathf.Abs(tangent.y)>.92f?Vector3.forward:Vector3.up).normalized;var v=Vector3.Cross(tangent,u);float taper=Mathf.Pow(1-t,.7f);return curve(t)+(u*Mathf.Cos(a)*width+v*Mathf.Sin(a)*width*.38f)*Mathf.Max(.005f,taper);};
      for(int i=0;i<9;i++)for(int j=0;j<8;j++){float t=i/9f,u=(i+1)/9f,a=j*Mathf.PI*2/8,b=(j+1)*Mathf.PI*2/8;g.Quad(at(t,a),at(t,b),at(u,b),at(u,a));}
    }
    void HairAndFace(Skin skin,int c){
      var h=rig.P(HumanBodyBones.Head);var cap=h+V(0,.09f,.025f);var g=new ShrineModel.Geometry();
      Func<float,float> capAngle=a=>Mathf.Lerp(1.7f,.78f,Mathf.SmoothStep(0,1,Mathf.Clamp01((Mathf.Sin(a)+.05f)/.95f)));
      Func<float,float,Vector3> crown=(t,a)=>{float theta=t*capAngle(a);return cap+Vector3.Scale(V(Mathf.Sin(theta)*Mathf.Cos(a),Mathf.Cos(theta),Mathf.Sin(theta)*Mathf.Sin(a)),V(.086f,.124f,.081f));};
      for(int y=0;y<20;y++)for(int j=0;j<64;j++){float t=y/20f,u=(y+1)/20f,a=j*Mathf.PI*2/64,b=(j+1)*Mathf.PI*2/64;g.Quad(crown(t,a),crown(t,b),crown(u,b),crown(u,a));}skin.Part(g,3,v=>rig.One(HumanBodyBones.Head));
      for(int i=0;i<(c==0?54:46);i++){
        float a=i*2.399963f,theta=(.16f+.83f*H(i+2))*capAngle(a);var normal=V(Mathf.Sin(theta)*Mathf.Cos(a),Mathf.Cos(theta),Mathf.Sin(theta)*Mathf.Sin(a));var start=cap+Vector3.Scale(normal,V(.075f,.11f,.071f));var direction=(normal+Vector3.up*(c==0?.35f:.18f)+Vector3.back*.36f).normalized;float length=(c==0?.043f:.034f)+H(i+6)*(c==0?.05f:.039f);
        g=new ShrineModel.Geometry();HairLock(g,start,start+direction*length*.63f+Vector3.up*.011f,start+direction*length,.011f+H(i+9)*.008f);skin.Part(g,3,v=>rig.One(HumanBodyBones.Head));
      }
      if(c==0)for(int i=0;i<11;i++){float x=(i-5)*.011f;g=new ShrineModel.Geometry();HairLock(g,h+V(x,.17f,.079f),h+V(x*1.07f,.155f,.104f),h+V(x*1.12f,.13f+H(i+30)*.012f,.112f),.01f);skin.Part(g,3,v=>rig.One(HumanBodyBones.Head));}
    }

    Mesh Prepare(Model model,Saved target,bool top){
      string key=role+"/"+target.R.GetInstanceID()+"/"+top;Mesh m;if(prepared.TryGetValue(key,out m))return m;
      m=Object.Instantiate(top?model.Upper:model.Lower);m.name="JJK_bound_"+role+"_"+(top?"upper":"lower");var toMesh=target.R.transform.worldToLocalMatrix*rig.Root.localToWorldMatrix;var toRig=toMesh.inverse;m.vertices=m.vertices.Select(toMesh.MultiplyPoint3x4).ToArray();m.normals=m.normals.Select(v=>toMesh.MultiplyVector(v).normalized).ToArray();m.bindposes=rig.Rest.Select(p=>p.inverse*toRig).ToArray();m.RecalculateBounds();prepared[key]=m;owned.Add(m);return m;
    }
    public void Apply(int character){role=character;Refresh();}
    public bool RagdollBound {get{return corpse && corpseParts.Count==2;}}
    public void RefreshRagdoll(RagdollBody body){
      if(!Bound || !body || body.myPlayer!=player.Object || role<0)return;
      if(corpse==body && corpseParts.Count==2)return;
      foreach(var part in corpseParts)if(part)Object.Destroy(part);corpseParts.Clear();foreach(var state in corpseSaved)state.Restore();corpseSaved.Clear();
      var renderers=body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
      var map=renderers.SelectMany(s=>s.bones).Where(b=>b).Distinct().GroupBy(b=>b.name).ToDictionary(g=>g.Key,g=>g.First());
      if(rig.Bones.Any(b=>!map.ContainsKey(b.name)))throw new InvalidOperationException("Native ragdoll skeleton differs from player");
      var bones=rig.Bones.Select(b=>map[b.name]).ToArray();
      foreach(var renderer in renderers){corpseSaved.Add(new Saved(renderer,false,false));renderer.sharedMesh=null;}
      for(int i=0;i<2;i++){
        var go=new GameObject("JJK_corpse_"+role+"_"+i);go.layer=body.gameObject.layer;go.transform.SetParent(body.transform,false);var renderer=go.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=i==0?roles[role].Upper:roles[role].Lower;renderer.sharedMaterials=roles[role].Materials;renderer.bones=bones;renderer.rootBone=body.transform;renderer.localBounds=renderer.sharedMesh.bounds;renderer.updateWhenOffscreen=true;corpseParts.Add(go);
      }
      corpse=body;
    }
    public void Refresh(){
      if(!Bound || role<0)return;
      upper=saved.FirstOrDefault(s=>s.Body && s.R && s.R.gameObject.activeInHierarchy && (s.R.name.StartsWith("Shirt") || s.R.name=="Suit" || s.R.name.EndsWith("_Top"))) ?? saved.First(s=>s.Body && s.R.name=="Shirt6");
      lower=saved.FirstOrDefault(s=>s.Body && s.R && s.R.gameObject.activeInHierarchy && (s.R.name.StartsWith("Pants") || s.R.name=="SuitPants" || s.R.name=="CHR_Male_Bottom")) ?? saved.First(s=>s.Body && s.R.name=="Pants5");
      foreach(var s in saved)if(s.R){
        if(s.Body){if(s==upper || s==lower){var mesh=Prepare(roles[role],s,s==upper);if(s.R.sharedMesh!=mesh){s.R.sharedMesh=mesh;s.R.bones=rig.Bones;s.R.rootBone=rig.Root;s.R.sharedMaterials=roles[role].Materials;s.R.localBounds=mesh.bounds;s.R.updateWhenOffscreen=true;}}else if(s.R.sharedMesh)s.R.sharedMesh=null;}
        else if(s.Hand && s.R.gameObject.activeInHierarchy && s.R.enabled){bool full=s.Bones.Any(b=>b && b.name=="hand_l") && s.Bones.Any(b=>b && b.name=="hand_r") && s.Bones.Length>=38;if(!full){s.R.sharedMesh=null;continue;}string key=role+"/"+s.R.GetInstanceID();Mesh mesh;if(!handCopies.TryGetValue(key,out mesh)){mesh=BuildHand(s,role);handCopies[key]=mesh;owned.Add(mesh);}if(s.R.sharedMesh!=mesh){s.R.sharedMesh=mesh;s.R.sharedMaterials=roles[role].Materials.Take(8).ToArray();s.R.bones=s.Bones;s.R.rootBone=s.Root;s.R.updateWhenOffscreen=true;}}
      }
    }
    Mesh BuildHand(Saved source,int c){
      var m=source.Mesh.isReadable?Object.Instantiate(source.Mesh):ReadGpuMesh(source.Mesh,source.Bones.Length);if(!source.Mesh.isReadable)GpuHandCopies++;
      m.name="JJK_"+(c==0?"Gojo":"Sukuna")+"_native_hand_copy";var vertices=m.vertices;var weights=m.boneWeights;var faces=new List<int>[] {new List<int>(),new List<int>(),new List<int>(),new List<int>(),new List<int>(),new List<int>(),new List<int>(),new List<int>()};
      Func<int,bool> skin=i=>{var w=weights[i];string name=source.Bones[w.boneIndex0].name;return name.Contains("hand_") || name.Contains("thumb_") || name.Contains("index_") || name.Contains("middle_") || name.Contains("ring_") || name.Contains("pinky_");};
      var triangles=m.triangles;for(int i=0;i<triangles.Length;i+=3){int mat=skin(triangles[i]) || skin(triangles[i+1]) || skin(triangles[i+2])?0:c==0?1:2;faces[mat].Add(triangles[i]);faces[mat].Add(triangles[i+1]);faces[mat].Add(triangles[i+2]);}
      m.subMeshCount=8;for(int i=0;i<8;i++)m.SetTriangles(faces[i],i);m.RecalculateBounds();
      if(c==1){
        var data=new Skin(8);data.V.AddRange(m.vertices);data.N.AddRange(m.normals);data.U.AddRange(m.uv);data.W.AddRange(m.boneWeights);for(int i=0;i<8;i++)data.F[i].AddRange(m.GetTriangles(i));var poses=m.bindposes;
        for(int side=0;side<2;side++){
          string suffix=side==0?"_l":"_r";int hand=Array.FindIndex(source.Bones,b=>b && b.name=="hand"+suffix),arm=Array.FindIndex(source.Bones,b=>b && b.name=="lowerarm"+suffix);if(hand<0 || arm<0)continue;
          var wrist=poses[hand].inverse.MultiplyPoint3x4(Vector3.zero);var elbow=poses[arm].inverse.MultiplyPoint3x4(Vector3.zero);var axis=(wrist-elbow).normalized;var u=Vector3.Cross(axis,Mathf.Abs(axis.y)>.8f?Vector3.forward:Vector3.up).normalized;var v=Vector3.Cross(axis,u);
          for(int band=0;band<2;band++){
            var center=wrist-axis*(.025f+band*.023f);var g=new ShrineModel.Geometry();
            Func<float,float,Vector3> at=(angle,z)=>{var direction=u*Mathf.Cos(angle)+v*Mathf.Sin(angle);float radius=.027f;foreach(var point in vertices){var delta=point-center;float along=Vector3.Dot(delta,axis),radial=Vector3.Dot(delta,direction);if(Mathf.Abs(along-z)<.018f && radial>radius && radial<.055f && Vector3.Cross(delta-axis*along,direction).magnitude<.016f)radius=radial;}return center+axis*z+direction*(radius+.0007f);};
            for(int j=0;j<64;j++){float a=j*Mathf.PI*2/64,b=(j+1)*Mathf.PI*2/64;g.Quad(at(a,-.0045f),at(b,-.0045f),at(b,.0045f),at(a,.0045f));}
            data.Part(g,4,p=>new BoneWeight{boneIndex0=arm,boneIndex1=hand,weight0=.4f,weight1=.6f});
          }
        }
        var marked=data.Finish(m.name+"_curse_marks",poses);Object.Destroy(m);m=marked;
      }
      return m;
    }
    static int FormatBytes(UnityEngine.Rendering.VertexAttributeFormat f){switch(f){case UnityEngine.Rendering.VertexAttributeFormat.Float32:case UnityEngine.Rendering.VertexAttributeFormat.UInt32:case UnityEngine.Rendering.VertexAttributeFormat.SInt32:return 4;case UnityEngine.Rendering.VertexAttributeFormat.Float16:case UnityEngine.Rendering.VertexAttributeFormat.UNorm16:case UnityEngine.Rendering.VertexAttributeFormat.SNorm16:case UnityEngine.Rendering.VertexAttributeFormat.UInt16:case UnityEngine.Rendering.VertexAttributeFormat.SInt16:return 2;default:return 1;}}
    static float Scalar(byte[] data,int p,UnityEngine.Rendering.VertexAttributeFormat f){switch(f){case UnityEngine.Rendering.VertexAttributeFormat.Float32:return BitConverter.ToSingle(data,p);case UnityEngine.Rendering.VertexAttributeFormat.Float16:{int bits=BitConverter.ToUInt16(data,p),sign=(bits>>15)&1,exp=(bits>>10)&31,mant=bits&1023;return (sign==0?1:-1)*(exp==0?Mathf.Pow(2,-14)*(mant/1024f):exp==31?float.NaN:Mathf.Pow(2,exp-15)*(1+mant/1024f));}case UnityEngine.Rendering.VertexAttributeFormat.UInt32:return BitConverter.ToUInt32(data,p);case UnityEngine.Rendering.VertexAttributeFormat.SInt32:return BitConverter.ToInt32(data,p);case UnityEngine.Rendering.VertexAttributeFormat.UInt16:return BitConverter.ToUInt16(data,p);case UnityEngine.Rendering.VertexAttributeFormat.SInt16:return BitConverter.ToInt16(data,p);case UnityEngine.Rendering.VertexAttributeFormat.UNorm16:return BitConverter.ToUInt16(data,p)/65535f;case UnityEngine.Rendering.VertexAttributeFormat.SNorm16:return Mathf.Max(-1,BitConverter.ToInt16(data,p)/32767f);case UnityEngine.Rendering.VertexAttributeFormat.UNorm8:return data[p]/255f;case UnityEngine.Rendering.VertexAttributeFormat.SNorm8:return Mathf.Max(-1,(sbyte)data[p]/127f);case UnityEngine.Rendering.VertexAttributeFormat.SInt8:return (sbyte)data[p];default:return data[p];}}
    static byte[] ReadBuffer(GraphicsBuffer buffer){using(buffer){var bytes=new byte[buffer.count*buffer.stride];buffer.GetData(bytes);return bytes;}}
    static bool ValidWeights(BoneWeight w,int bones){float sum=w.weight0+w.weight1+w.weight2+w.weight3;return !float.IsNaN(sum) && Mathf.Abs(sum-1)<.01f && w.weight0>=0 && w.weight1>=0 && w.weight2>=0 && w.weight3>=0 && w.boneIndex0>=0 && w.boneIndex0<bones && w.boneIndex1>=0 && w.boneIndex1<bones && w.boneIndex2>=0 && w.boneIndex2<bones && w.boneIndex3>=0 && w.boneIndex3<bones;}
    static Mesh ReadGpuMesh(Mesh source,int bones){
      // ponytail: synchronous readback only once per newly activated hand style; use AsyncGPUReadback if a measured style-switch stall warrants it.
      int count=source.vertexCount;var streams=new Dictionary<int,byte[]>();
      for(int stream=0;stream<source.vertexBufferCount;stream++)streams[stream]=ReadBuffer(source.GetVertexBuffer(stream));
      Func<UnityEngine.Rendering.VertexAttribute,int,int,float> read=(attribute,vertex,component)=>{if(!source.HasVertexAttribute(attribute) || component>=source.GetVertexAttributeDimension(attribute))return 0;int stream=source.GetVertexAttributeStream(attribute),offset=vertex*source.GetVertexBufferStride(stream)+source.GetVertexAttributeOffset(attribute);var format=source.GetVertexAttributeFormat(attribute);return Scalar(streams[stream],offset+component*FormatBytes(format),format);};
      var v=new Vector3[count];var n=new Vector3[count];var uv=new Vector2[count];var weights=new BoneWeight[count];
      for(int i=0;i<count;i++){v[i]=V(read(UnityEngine.Rendering.VertexAttribute.Position,i,0),read(UnityEngine.Rendering.VertexAttribute.Position,i,1),read(UnityEngine.Rendering.VertexAttribute.Position,i,2));n[i]=V(read(UnityEngine.Rendering.VertexAttribute.Normal,i,0),read(UnityEngine.Rendering.VertexAttribute.Normal,i,1),read(UnityEngine.Rendering.VertexAttribute.Normal,i,2)).normalized;uv[i]=new Vector2(read(UnityEngine.Rendering.VertexAttribute.TexCoord0,i,0),read(UnityEngine.Rendering.VertexAttribute.TexCoord0,i,1));}
      if(source.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.BlendWeight) && source.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.BlendIndices))for(int i=0;i<count;i++)weights[i]=new BoneWeight{weight0=read(UnityEngine.Rendering.VertexAttribute.BlendWeight,i,0),weight1=read(UnityEngine.Rendering.VertexAttribute.BlendWeight,i,1),weight2=read(UnityEngine.Rendering.VertexAttribute.BlendWeight,i,2),weight3=read(UnityEngine.Rendering.VertexAttribute.BlendWeight,i,3),boneIndex0=(int)read(UnityEngine.Rendering.VertexAttribute.BlendIndices,i,0),boneIndex1=(int)read(UnityEngine.Rendering.VertexAttribute.BlendIndices,i,1),boneIndex2=(int)read(UnityEngine.Rendering.VertexAttribute.BlendIndices,i,2),boneIndex3=(int)read(UnityEngine.Rendering.VertexAttribute.BlendIndices,i,3)};
      else {var buffer=source.GetBoneWeightBuffer(SkinWeights.FourBones);int stride=buffer.stride;var data=ReadBuffer(buffer);if(stride<32)throw new InvalidDataException("Unexpected skin buffer stride "+stride);for(int i=0;i<count;i++){int p=i*stride;var w=new BoneWeight{weight0=BitConverter.ToSingle(data,p),weight1=BitConverter.ToSingle(data,p+4),weight2=BitConverter.ToSingle(data,p+8),weight3=BitConverter.ToSingle(data,p+12),boneIndex0=BitConverter.ToInt32(data,p+16),boneIndex1=BitConverter.ToInt32(data,p+20),boneIndex2=BitConverter.ToInt32(data,p+24),boneIndex3=BitConverter.ToInt32(data,p+28)};if(!ValidWeights(w,bones))w=new BoneWeight{boneIndex0=BitConverter.ToInt32(data,p),boneIndex1=BitConverter.ToInt32(data,p+4),boneIndex2=BitConverter.ToInt32(data,p+8),boneIndex3=BitConverter.ToInt32(data,p+12),weight0=BitConverter.ToSingle(data,p+16),weight1=BitConverter.ToSingle(data,p+20),weight2=BitConverter.ToSingle(data,p+24),weight3=BitConverter.ToSingle(data,p+28)};weights[i]=w;}}
      foreach(var w in weights)if(!ValidWeights(w,bones))throw new InvalidDataException("GPU skin data failed weight/index validation");
      var m=new Mesh();m.name="JJK_gpu_readable_hand";m.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;m.vertices=v;m.normals=n;m.uv=uv;m.boneWeights=weights;m.bindposes=source.bindposes;m.subMeshCount=source.subMeshCount;
      var index=ReadBuffer(source.GetIndexBuffer());int size=source.indexFormat==UnityEngine.Rendering.IndexFormat.UInt16?2:4;
      for(int sub=0;sub<source.subMeshCount;sub++){var desc=source.GetSubMesh(sub);var triangles=new int[desc.indexCount];for(int i=0;i<triangles.Length;i++){int p=(desc.indexStart+i)*size;triangles[i]=(size==2?BitConverter.ToUInt16(index,p):BitConverter.ToInt32(index,p))+desc.baseVertex;if(triangles[i]<0 || triangles[i]>=count)throw new InvalidDataException("GPU mesh index outside vertex range");}m.SetTriangles(triangles,sub);}m.RecalculateBounds();return m;
    }
    public int UpperTriangles(int c){return roles[c].Upper.triangles.Length/3;}public int LowerTriangles(int c){return roles[c].Lower.triangles.Length/3;}
    public void Export(string directory){
      Directory.CreateDirectory(directory);var culture=CultureInfo.InvariantCulture;
      for(int role=0;role<2;role++){
        string name=role==0?"gojo":"sukuna";var obj=new StringBuilder("mtllib "+name+".mtl\n");var mtl=new StringBuilder();int offset=1;
        foreach(var mesh in new[]{roles[role].Upper,roles[role].Lower}){obj.Append("o ").Append(mesh.name).Append('\n');foreach(var v in mesh.vertices)obj.AppendFormat(culture,"v {0} {1} {2}\n",v.x,v.y,v.z);foreach(var uv in mesh.uv)obj.AppendFormat(culture,"vt {0} {1}\n",uv.x,uv.y);foreach(var n in mesh.normals)obj.AppendFormat(culture,"vn {0} {1} {2}\n",n.x,n.y,n.z);for(int sub=0;sub<mesh.subMeshCount;sub++){obj.Append("usemtl Surface_").Append(sub).Append('\n');var indices=mesh.GetTriangles(sub);for(int j=0;j<indices.Length;j+=3){obj.Append('f');for(int k=0;k<3;k++){int index=indices[j+k]+offset;obj.Append(' ').Append(index).Append('/').Append(index).Append('/').Append(index);}obj.Append('\n');}}offset+=mesh.vertexCount;}
        for(int i=0;i<roles[role].Materials.Length;i++){var material=roles[role].Materials[i];var color=material.color;mtl.AppendFormat(culture,"newmtl Surface_{0}\nKd {1} {2} {3}\n",i,color.r,color.g,color.b);var texture=material.mainTexture as Texture2D;if(texture){string file=name+"-surface-"+i+".png";File.WriteAllBytes(Path.Combine(directory,file),ImageConversion.EncodeToPNG(texture));mtl.Append("map_Kd ").Append(file).Append('\n');}mtl.Append('\n');}
        File.WriteAllText(Path.Combine(directory,name+".obj"),obj.ToString());File.WriteAllText(Path.Combine(directory,name+".mtl"),mtl.ToString());
        using(var writer=new BinaryWriter(File.Open(Path.Combine(directory,name+"-skin.bin"),FileMode.Create))){writer.Write(0x4A4A4B53);writer.Write(1);writer.Write(rig.Bones.Length);for(int i=0;i<rig.Bones.Length;i++){writer.Write(rig.Bones[i].name);for(int j=0;j<16;j++)writer.Write(rig.Rest[i][j/4,j%4]);}foreach(var mesh in new[]{roles[role].Upper,roles[role].Lower}){writer.Write(mesh.vertexCount);foreach(var w in mesh.boneWeights){writer.Write(w.boneIndex0);writer.Write(w.boneIndex1);writer.Write(w.boneIndex2);writer.Write(w.boneIndex3);writer.Write(w.weight0);writer.Write(w.weight1);writer.Write(w.weight2);writer.Write(w.weight3);}}}
      }
    }
    public void ShowForReview(){if(upper!=null && upper.R)upper.R.enabled=true;if(lower!=null && lower.R)lower.R.enabled=true;}
    public bool OriginalRestored {get{return saved.Concat(corpseSaved).All(s=>!s.R || s.R.sharedMesh==s.Mesh && s.R.bones.SequenceEqual(s.Bones));}}
    public void Restore(){foreach(var s in saved.Concat(corpseSaved))s.Restore();foreach(var part in corpseParts)if(part)Object.Destroy(part);corpseParts.Clear();corpse=null;role=-1;}
    public void Dispose(){Restore();foreach(var m in owned)if(m)Object.Destroy(m);foreach(var m in materials)if(m)Object.Destroy(m);foreach(var t in textures)if(t)Object.Destroy(t);textures.Clear();if(fabric)Object.Destroy(fabric);owned.Clear();materials.Clear();prepared.Clear();handCopies.Clear();player=null;rig=null;roles=null;}
  }

  public sealed partial class JujutsuMod {
    CharacterModels characterModels;FPP_Player modelOwner;float modelPollAt;int modelErrors;
    void SyncCharacters(){
      bool ready=!arena && active.Value && IsLocalActor(player) && (player.spawnedAndReady || player.isDead);
      if(!ready){if(characterModels!=null)RestoreCharacters();return;}
      if(characterModels!=null && modelOwner!=player)RestoreCharacters();
      if(Time.unscaledTime<modelPollAt && (characterModels==null || characterModels.CurrentRole==rules.Character))return;
      modelPollAt=Time.unscaledTime+.1f;
      try {
        if(characterModels==null){modelOwner=player;characterModels=new CharacterModels(player);Logger.LogInfo("JJK_CHARACTER_BOUND bones="+characterModels.BodyBones+" Gojo="+characterModels.UpperTriangles(0)+"/"+characterModels.LowerTriangles(0)+" Sukuna="+characterModels.UpperTriangles(1)+"/"+characterModels.LowerTriangles(1));}
        if(characterModels.CurrentRole!=rules.Character)characterModels.Apply(rules.Character);else characterModels.Refresh();
        if(player.isDead){var body=Resources.FindObjectsOfTypeAll<RagdollBody>().FirstOrDefault(b=>b && b.gameObject.scene.isLoaded && b.myPlayer==player.Object);if(body)characterModels.RefreshRagdoll(body);}
      }catch(Exception ex){modelPollAt=Time.unscaledTime+5;if(modelErrors++<3)Logger.LogError("JJK_CHARACTER_ERROR "+ex);RestoreCharacters();}
    }
    void RestoreCharacters(){if(characterModels!=null){characterModels.Dispose();Logger.LogInfo("JJK_CHARACTER_RESTORED originals="+characterModels.OriginalRestored);characterModels=null;}modelOwner=null;}
  }
}
