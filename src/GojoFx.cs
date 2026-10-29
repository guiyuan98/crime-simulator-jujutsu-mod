using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CrimeJJK {
  // Shared meshes/textures; a cast allocates transforms and one bounded particle buffer, not new materials.
  public sealed class GojoFx : IDisposable {
    public enum Form { Blue, RedCharge, RedBurst, Merge, Purple, PurpleBurst, Void, Ward }
    internal readonly Material Plasma, Glow, Dark, Rock;
    internal readonly Material VoidPlasma, VoidGlow, VoidDark;
    internal readonly Mesh Arc, Ray, Halo, Sphere, Shard;
    readonly Texture2D wisp, spark;
    public readonly int[] Spawned=new int[8];
    public int TextureWidth {get{return wisp.width;}}
    public GojoFx(Shader shader) {
      wisp=new Texture2D(2,2,TextureFormat.RGBA32,true);
      using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("jjk.gojo.wisp.png")) {
        if(stream==null)throw new InvalidDataException("Gojo plasma texture missing");
        using(var bytes=new MemoryStream()){stream.CopyTo(bytes);if(!ImageConversion.LoadImage(wisp,bytes.ToArray(),false))throw new InvalidDataException("Gojo plasma decode failed");}
      }
      wisp.wrapMode=TextureWrapMode.Clamp;wisp.filterMode=FilterMode.Trilinear;
      spark=new Texture2D(64,64,TextureFormat.RGBA32,false);
      for(int y=0;y<64;y++)for(int x=0;x<64;x++) {
        float dx=(x-31.5f)/31.5f,dy=(y-31.5f)/31.5f,r=dx*dx+dy*dy;
        float a=Mathf.Max(0,Mathf.Exp(-r*7)-.001f);
        spark.SetPixel(x,y,new Color(1,1,1,a));
      }
      spark.Apply();spark.wrapMode=TextureWrapMode.Clamp;
      Plasma=new Material(shader);Plasma.name="JJK_gojo_generated_plasma";Plasma.mainTexture=wisp;Plasma.color=Color.white;Plasma.renderQueue=3003;
      Glow=new Material(shader);Glow.name="JJK_gojo_soft_emission";Glow.mainTexture=spark;Glow.color=Color.white;Glow.renderQueue=3004;
      Dark=new Material(shader);Dark.name="JJK_gojo_lens_and_void";Dark.color=Color.white;Dark.renderQueue=2992;
      // The domain transforms the perceived environment even indoors; it must not disappear behind a room wall.
      Shader overlay=Shader.Find("UI/Default") ?? shader;
      VoidPlasma=new Material(overlay);VoidPlasma.name="JJK_gojo_void_plasma";VoidPlasma.mainTexture=wisp;VoidPlasma.SetInt("unity_GUIZTestMode",8);VoidPlasma.renderQueue=3103;
      VoidGlow=new Material(overlay);VoidGlow.name="JJK_gojo_void_stars";VoidGlow.mainTexture=spark;VoidGlow.SetInt("unity_GUIZTestMode",8);VoidGlow.renderQueue=3104;
      VoidDark=new Material(overlay);VoidDark.name="JJK_gojo_void_environment";VoidDark.SetInt("unity_GUIZTestMode",8);VoidDark.renderQueue=3100;
      Rock=new Material(Shader.Find("Standard") ?? shader);Rock.color=new Color(.08f,.11f,.16f);Rock.SetFloat("_Glossiness",.18f);
      var g=new ShrineModel.Geometry();
      for(int i=0;i<96;i++) {
        float t=i/96f,u=(i+1)/96f;
        Ribbon(g,t,u,Spiral,.19f,false);
      }
      Arc=Finish(g,"spiral_plasma");g=new ShrineModel.Geometry();
      for(int i=0;i<160;i++)Ribbon(g,i/160f,(i+1)/160f,t=>new Vector3(Mathf.Cos(t*Mathf.PI*2),Mathf.Sin(t*Mathf.PI*2),0),.022f,true);
      Halo=Finish(g,"lens_halo");g=new ShrineModel.Geometry();g.Ellipsoid(Vector3.zero,Vector3.one,32,20);Sphere=Finish(g,"lens_sphere");
      g=new ShrineModel.Geometry();g.Ellipsoid(Vector3.zero,new Vector3(.55f,.42f,.6f),6,4);Shard=Finish(g,"floating_shard");
      g=new ShrineModel.Geometry();for(int i=0;i<48;i++)Ribbon(g,i/48f,(i+1)/48f,t=>new Vector3(.13f+t*1.65f,Mathf.Sin(t*9)*t*.12f,Mathf.Sin(t*7)*.12f),.22f,false);Ray=Finish(g,"repulsion_filament");
    }
    static Mesh Finish(ShrineModel.Geometry g,string name){var m=g.Finish("JJK_Gojo_"+name);foreach(var v in m.vertices)if(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.sqrMagnitude))throw new InvalidDataException("Non-finite Gojo mesh "+name);m.colors=Enumerable.Repeat(Color.white,m.vertexCount).ToArray();return m;}
    static Vector3 Spiral(float t){float a=t*Mathf.PI*3.3f,r=.23f+t*1.2f;return new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,Mathf.Sin(t*8)*.2f);}
    static void Ribbon(ShrineModel.Geometry g,float t,float u,Func<float,Vector3> path,float width,bool halo) {
      var p=path(t);var q=path(u);var tangent=path(Mathf.Min(1,t+.001f))-path(Mathf.Max(0,t-.001f));var nextTangent=path(Mathf.Min(1,u+.001f))-path(Mathf.Max(0,u-.001f));var n=new Vector3(-tangent.y,tangent.x,0).normalized;var m=new Vector3(-nextTangent.y,nextTangent.x,0).normalized;
      float a=width*(halo?1:Mathf.Sqrt(Mathf.Max(0,Mathf.Sin(t*Mathf.PI)))),b=width*(halo?1:Mathf.Sqrt(Mathf.Max(0,Mathf.Sin(u*Mathf.PI))));
      g.QuadUV(p-n*a,p+n*a,q+m*b,q-m*b,new Vector2(t,0),new Vector2(t,1),new Vector2(u,1),new Vector2(u,0));
    }
    public GojoMotion Attach(GameObject root,Form form,float size,float seconds) {
      Spawned[(int)form]++;var m=root.AddComponent<GojoMotion>();m.Configure(this,form,size,seconds);return m;
    }
    public void Dispose(){foreach(var m in new[]{Arc,Ray,Halo,Sphere,Shard})Object.Destroy(m);foreach(var m in new[]{Plasma,Glow,Dark,Rock,VoidPlasma,VoidGlow,VoidDark})Object.Destroy(m);Object.Destroy(wisp);Object.Destroy(spark);}
  }

  public sealed class GojoMotion : MonoBehaviour {
    sealed class Part {public Transform T;public Renderer R;public Vector3 Center,Scale;public Quaternion Tilt;public Color Color;public float Phase,Speed;public int Mode,Node;}
    readonly List<Part> parts=new List<Part>();
    readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
    GojoFx fx;GojoFx.Form form;ParticleSystem particles,plumes;ParticleSystem.Particle[] buffer,wisps;
    float born,seconds,size;Light lamp;
    static readonly Color cyan=new Color(.28f,.85f,1,.5f),scarlet=new Color(1,.22f,.28f,.5f),violet=new Color(.76f,.44f,1,.5f);
    public int VisibleParticles {get{return particles?particles.particleCount:0;}}
    Part PartMesh(Mesh mesh,Material mat,Vector3 center,Vector3 scale,Quaternion tilt,Color color,int mode,float phase,float speed,int node) {
      var go=new GameObject("JJK_gojo_"+form+"_part");go.layer=gameObject.layer;go.transform.SetParent(transform,false);
      var mf=go.AddComponent<MeshFilter>();mf.sharedMesh=mesh;var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=mat;mr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;mr.receiveShadows=false;
      var p=new Part{T=go.transform,R=mr,Center=center,Scale=scale,Tilt=tilt,Color=color,Mode=mode,Phase=phase,Speed=speed,Node=node};parts.Add(p);return p;
    }
    void Swirl(Vector3 center,float radius,Color color,int count,int node) {
      for(int i=0;i<count;i++) {
        float f=i/(float)count;
        Color strand=i%3==0?Color.Lerp(color,Color.white,.72f):color;strand.a=color.a*(i%3==0?1.15f:1);
        PartMesh(fx.Arc,fx.Plasma,center,Vector3.one*radius*(.8f+f*.38f),Quaternion.Euler((i%3-1)*19,(i%4-1.5f)*13,i*137.5f),strand,0,i*2.17f,(i%2==0?1:-1)*(32+i*7),node);
      }
      for(int i=0;i<2;i++)PartMesh(fx.Halo,fx.Glow,center,Vector3.one*radius*(1.4f+i*.37f),Quaternion.Euler(i*32+12,i*21,i*43),new Color(color.r,color.g,color.b,.24f-i*.07f),0,i,22+i*9,node);
    }
    public void Configure(GojoFx owner,GojoFx.Form kind,float scale,float lifetime) {
      fx=owner;form=kind;born=Time.time;seconds=lifetime;size=scale;
      bool blue=form==GojoFx.Form.Blue,merge=form==GojoFx.Form.Merge,red=form==GojoFx.Form.RedBurst,voidField=form==GojoFx.Form.Void,ward=form==GojoFx.Form.Ward;
      Color c=blue || voidField || ward?cyan:form==GojoFx.Form.RedCharge || red?scarlet:violet;
      if(merge) {
        Swirl(Vector3.left*.8f,.48f,cyan,5,-1);Swirl(Vector3.right*.8f,.48f,scarlet,5,1);
      } else if(voidField) {
        PartMesh(fx.Sphere,fx.Dark,Vector3.zero,Vector3.one*28,Quaternion.identity,new Color(.002f,.008f,.024f,.65f),3,0,0,0);
        var center=new Vector3(0,1.7f,17);PartMesh(fx.Sphere,fx.Dark,center,Vector3.one*2.1f,Quaternion.identity,new Color(.001f,.002f,.008f,1),0,0,0,0);
        Swirl(center,3.4f,new Color(.55f,.8f,1,.29f),9,0);
        foreach(var p in parts)if(p.R.sharedMaterial==fx.Plasma || p.R.sharedMaterial==fx.Glow){p.Tilt=Quaternion.Euler(64,0,0);p.Speed=12+p.Phase*2;}
        for(int i=0;i<4;i++)PartMesh(fx.Halo,fx.Glow,center,new Vector3(6+i*1.8f,1.35f+i*.22f,1),Quaternion.Euler(15+i*2,4*i,-8),new Color(.65f,.82f,1,.18f),0,i,i*4+3,0);
        for(int i=0;i<12;i++)PartMesh(fx.Ray,fx.Plasma,new Vector3((Hash(i+20)-.5f)*22,3+Hash(i+2)*7,6+Hash(i+80)*15),new Vector3(3+Hash(i+3)*4,.18f,1),Quaternion.Euler(0,0,-88),new Color(.65f,.8f,1,.12f),5,i,0,0);
      } else if(ward) {
        for(int i=0;i<4;i++)PartMesh(fx.Halo,fx.Glow,Vector3.zero,Vector3.one*(.3f+i*.13f),Quaternion.Euler(10*i,12*i,i*33),new Color(.65f,.88f,1,.5f),1,i,10,0);
        Swirl(Vector3.zero,.46f,new Color(.5f,.85f,1,.2f),3,0);
      } else {
        if(red || form==GojoFx.Form.PurpleBurst)for(int i=0;i<15;i++) {
          var rayColor=Color.Lerp(c,Color.white,i%3==0?.7f:.15f);rayColor.a=.32f;
          PartMesh(fx.Ray,fx.Plasma,Vector3.zero,new Vector3(.7f+Hash(i+4)*.6f,.65f,1),Quaternion.Euler((i%3-1)*20,(i%4-2)*16,i*137.5f),rayColor,0,i,(i%2==0?1:-1)*12,0);
        } else Swirl(Vector3.zero,1,c,blue?8:5,0);
        if(blue)PartMesh(fx.Sphere,fx.Dark,Vector3.zero,Vector3.one*.28f,Quaternion.identity,new Color(.002f,.006f,.018f,1),0,0,0,0);
        if(red || form==GojoFx.Form.PurpleBurst)for(int i=0;i<3;i++)PartMesh(fx.Halo,fx.Glow,Vector3.zero,Vector3.one*(.75f+i*.37f),Quaternion.Euler(i*28,i*45,i*12),new Color(c.r,c.g,c.b,.28f),1,i,18+i*3,0);
        if(blue)for(int i=0;i<32;i++)PartMesh(fx.Shard,fx.Rock,Vector3.zero,Vector3.one*(.06f+Hash(i+9)*.15f),Quaternion.Euler(i*18,i*22,i*27),Color.white,4,i,1,0);
        if(form==GojoFx.Form.Purple) {
          var trail=gameObject.AddComponent<TrailRenderer>();trail.sharedMaterial=fx.Glow;trail.time=.38f;trail.minVertexDistance=.08f;trail.startWidth=.8f*size;trail.endWidth=0;
          var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(new Color(.7f,.55f,2),0),new GradientColorKey(new Color(.2f,.04f,.8f),1)},new[]{new GradientAlphaKey(.8f,0),new GradientAlphaKey(0,1)});trail.colorGradient=g;
          for(int i=0;i<2;i++)PartMesh(fx.Arc,fx.Plasma,new Vector3(0,0,-.6f-i*.65f),new Vector3(1.2f,1.2f,2.3f),Quaternion.Euler(0,65,i*180),i==0?cyan:scarlet,0,i,180,0);
        }
      }
      if(voidField)foreach(var p in parts)p.R.sharedMaterial=p.R.sharedMaterial==fx.Plasma?fx.VoidPlasma:p.R.sharedMaterial==fx.Glow?fx.VoidGlow:fx.VoidDark;
      var pg=new GameObject("JJK_gojo_"+form+"_particles");pg.layer=gameObject.layer;pg.transform.SetParent(transform,false);particles=pg.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
      buffer=new ParticleSystem.Particle[voidField?1800:ward?100:blue?500:merge?260:form==GojoFx.Form.RedCharge?180:360];
      var main=particles.main;main.loop=false;main.playOnAwake=false;main.startSpeed=0;main.startLifetime=999;main.maxParticles=buffer.Length;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
      var emission=particles.emission;emission.enabled=false;var shape=particles.shape;shape.enabled=false;
      var render=pg.GetComponent<ParticleSystemRenderer>();render.sharedMaterial=voidField?fx.VoidGlow:fx.Glow;render.renderMode=ParticleSystemRenderMode.Billboard;render.maxParticleSize=.5f;
      particles.Play();
      var wg=new GameObject("JJK_gojo_"+form+"_soft_wisps");wg.layer=gameObject.layer;wg.transform.SetParent(transform,false);plumes=wg.AddComponent<ParticleSystem>();plumes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
      wisps=new ParticleSystem.Particle[voidField?70:ward?10:36];main=plumes.main;main.loop=false;main.playOnAwake=false;main.startSpeed=0;main.startLifetime=999;main.maxParticles=wisps.Length;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
      emission=plumes.emission;emission.enabled=false;shape=plumes.shape;shape.enabled=false;render=wg.GetComponent<ParticleSystemRenderer>();render.sharedMaterial=voidField?fx.VoidPlasma:fx.Plasma;render.renderMode=ParticleSystemRenderMode.Billboard;render.maxParticleSize=.4f;plumes.Play();
      lamp=gameObject.AddComponent<Light>();lamp.type=LightType.Point;float peak=Mathf.Max(c.r,Mathf.Max(c.g,c.b));lamp.color=new Color(c.r/peak,c.g/peak,c.b/peak);lamp.range=voidField?15:8*size;lamp.intensity=blue?1.3f:2;lamp.shadows=LightShadows.None;
      Update();
    }
    static float Hash(int n){return Mathf.Repeat(Mathf.Sin(n*127.1f+311.7f)*43758.5453f,1);}
    static Color Fade(Color c,float alpha){c.a*=alpha;return c;}
    void Update() {
      if(fx==null)return;
      float age=Time.time-born,t=Mathf.Clamp01(age/seconds),fade=Mathf.Clamp01(age/.12f)*Mathf.Clamp01((seconds-age)/.32f);
      bool blue=form==GojoFx.Form.Blue,red=form==GojoFx.Form.RedBurst,merge=form==GojoFx.Form.Merge,voidField=form==GojoFx.Form.Void,ward=form==GojoFx.Form.Ward,burst=red || form==GojoFx.Form.PurpleBurst;
      float expand=burst?.3f+t*3.8f:ward?.6f+t*1.6f:1;
      transform.localScale=Vector3.one*size*(voidField?Mathf.SmoothStep(.15f,1,Mathf.Clamp01(age/.55f)):expand);
      foreach(var p in parts) {
        var center=p.Center;float a=age*p.Speed;
        if(p.Node!=0){center.x=p.Node*.8f*Mathf.Pow(1-t,1.4f);center.y=Mathf.Sin(t*Mathf.PI)*p.Node*.12f;}
        if(p.Mode==5)center.y+=Mathf.Sin(age*.6f+p.Phase)*1.4f;
        if(p.Mode==4) {
          float u=Mathf.Repeat(Hash((int)p.Phase+13)+age*.19f,1),r=Mathf.Lerp(2.15f,.42f,u),angle=age*(1.6f+Hash((int)p.Phase+2)) +p.Phase*2.4f;
          center=new Vector3(Mathf.Cos(angle)*r,Mathf.Sin(angle)*r,(Hash((int)p.Phase+5)-.5f)*1.2f);p.T.localRotation=p.Tilt*Quaternion.Euler(a*30,a*55,a*20);
        } else p.T.localRotation=p.Tilt*Quaternion.Euler(0,0,a);
        p.T.localPosition=center;float scale=p.Mode==1?.65f+t*1.5f:p.Mode==2?.1f+Mathf.Pow(t,4)*9:1;
        p.T.localScale=p.Scale*scale;
        block.SetColor("_Color",Fade(p.Color,fade*(p.Mode==3?1:p.Mode==2?Mathf.Clamp01((t-.45f)*3):.82f+.18f*Mathf.Sin(age*8+p.Phase))));p.R.SetPropertyBlock(block);
      }
      for(int i=0;i<buffer.Length;i++) {
        float h=Hash(i+1),h2=Hash(i+707),h3=Hash(i+1401),u=Mathf.Repeat(h+age*(voidField?.1f:blue?.46f:1.15f),1),angle=h2*Mathf.PI*2+age*(blue?2.7f:voidField?.08f:2),r;
        Vector3 pos;Color c;float sz;
        if(voidField) {
          float phi=h3*Mathf.PI*2; r=5+21*h;
          pos=new Vector3(Mathf.Cos(phi)*r,Mathf.Sin(phi)*r*.6f,Mathf.Sin(h2*6.28f)*r);
          if(i<700){r=2.2f+u*18;angle=h2*6.28f+u*5+age*.12f;pos=new Vector3(Mathf.Cos(angle)*r,3+Mathf.Sin(angle)*r*.35f,17-Mathf.Sin(u*3.14f)*12-h3*5);}
          c=new Color(.3f+h2*.7f,.55f+h2*.45f,1,.35f+.65f*h3);sz=i%19==0?.14f:.025f+h2*.065f;
        } else {
          r=blue?Mathf.Lerp(2.2f,.35f,u):burst?(.2f+h*2.2f):ward?.35f+u*.5f:.2f+h*1.05f;
          float z=(h3-.5f)*(blue?.9f:1.5f);pos=new Vector3(Mathf.Cos(angle)*r,Mathf.Sin(angle)*r,z);
          if(form==GojoFx.Form.Purple){pos.z=-h*4;pos.x*=1-h*.65f;pos.y*=1-h*.65f;}
          if(merge)pos.x+=(i%2==0?-1:1)*.8f*Mathf.Pow(1-t,1.4f);
          c=blue || ward?new Color(.15f,.65f,1,1):red || form==GojoFx.Form.RedCharge?new Color(1,.12f,.18f,1):merge && i%2==0?new Color(.1f,.65f,1,1):merge?new Color(1,.1f,.15f,1):new Color(.65f,.35f,1,1);
          if(i%8==0)c=Color.Lerp(c,Color.white,.85f);
          c.a=Mathf.Sin(u*Mathf.PI)*fade;sz=(i%21==0?.11f:.016f+.04f*h2)*(burst?.7f:1);
          if(!blue && !ward && i<3){pos=Vector3.zero;c=Color.white;c.a=(i==0?1:.28f)*fade;sz=i==0?.23f:i==1?.62f:.95f;if(merge){c.a*=Mathf.Clamp01((t-.35f)*2);sz*=.5f+t;}}
        }
        c.a*=fade;buffer[i].position=pos;buffer[i].startColor=c;buffer[i].startSize=sz;buffer[i].startLifetime=999;buffer[i].remainingLifetime=999;buffer[i].rotation=age*30+i*19;
      }
      for(int i=0;i<wisps.Length;i++) {
        int index=(i*7+31)%buffer.Length;var p=buffer[index];p.startColor=Color.Lerp(p.startColor,Color.white,.5f);var c=(Color)p.startColor;c.a=(voidField?.08f:.18f)*Mathf.Sin(Mathf.Repeat(Hash(i+22)+age*.4f,1)*Mathf.PI)*fade;p.startColor=c;p.startSize=voidField?2:blue?.45f+Hash(i+7)*.8f:.35f+Hash(i+7)*.5f;p.rotation=age*55+i*137.5f;wisps[i]=p;
      }
      plumes.SetParticles(wisps,wisps.Length);
      particles.SetParticles(buffer,buffer.Length);if(lamp)lamp.intensity=(voidField?.65f:1.5f)*fade;
    }
  }

  public sealed partial class JujutsuMod {
    GojoFx gojoFx;int gojoReleases;float nextWardCue;
    GameObject GojoEffect(GojoFx.Form form,Vector3 position,Quaternion rotation,float size,float seconds) {
      var root=Effect("gojo_"+form,seconds);root.transform.position=position;root.transform.rotation=rotation;gojoFx.Attach(root,form,size,seconds);return root;
    }
    void GojoCast(int slot) {
      var origin=view.transform.position;var facing=view.transform.rotation;
      if(slot==0) {
        bluePoint=Aim(24);blueUntil=Time.time+3;GojoEffect(GojoFx.Form.Blue,bluePoint,facing,2.35f,3);
        Area(bluePoint,9,15,blue,false,false);
        Logger.LogInfo("JJK_GOJO_BLUE inwardVortex=True debris=True duration=3 scaledDamage="+Rules.SkillDamage(15));
      } else if(slot==1 || slot==2)StartCoroutine(GojoWindup(slot));
      else {
        domainUntil=Time.time+8;GojoEffect(GojoFx.Form.Void,origin,facing,1,8);
        foreach(var target in Targets(origin,24))target.Stun(this,8);
        Flash(new Color(.16f,.35f,.7f),.25f);Logger.LogInfo("JJK_GOJO_VOID stars=1800 radius=24 duration=8 control=True");
      }
    }
    System.Collections.IEnumerator GojoWindup(int slot) {
      int revision=combatRevision;float duration=slot==1?.55f:1.05f;
      var charge=GojoEffect(slot==1?GojoFx.Form.RedCharge:GojoFx.Form.Merge,Vector3.zero,Quaternion.identity,slot==1?.5f:.82f,duration+.1f);
      float end=Time.time+duration;
      Logger.LogInfo("JJK_GOJO_WINDUP slot="+slot+" seconds="+duration);
      while(charge && Time.time<end && CombatReady() && revision==combatRevision) {
        charge.transform.position=view.transform.position+view.transform.forward*1.8f+view.transform.right*.24f-view.transform.up*.12f;
        charge.transform.rotation=view.transform.rotation;yield return null;
      }
      if(charge)Destroy(charge);
      if(!CombatReady() || revision!=combatRevision){Logger.LogInfo("JJK_GOJO_CANCELLED slot="+slot);yield break;}
      gojoReleases++;
      if(skillAudio!=null)skillAudio.Release(0,slot);
      if(slot==1) {
        var hit=Aim(28);GojoEffect(GojoFx.Form.RedBurst,hit,view.transform.rotation,2.4f,.9f);
        Logger.LogInfo("JJK_GOJO_RELEASE_AIM slot=1 origin="+view.transform.position+" forward="+view.transform.forward+" point="+hit);
        Area(hit,9,65,red,true,false);Flash(red,.12f);
        Logger.LogInfo("JJK_GOJO_RED release=True repulsion=True scaledDamage="+Rules.SkillDamage(65));
      } else {
        Logger.LogInfo("JJK_GOJO_PURPLE release=True merge=True scaledDamage="+Rules.SkillDamage(180));
        yield return StartCoroutine(Projectile(view.transform.position+view.transform.forward*1.7f,view.transform.forward,purple,55,2.7f,180,6,false,true));
      }
    }
    void GojoWardCue() {
      if(rules.Character!=0 || !view || Time.time<nextWardCue)return;nextWardCue=Time.time+.14f;
      GojoEffect(GojoFx.Form.Ward,view.transform.position+view.transform.forward*.95f,view.transform.rotation,.85f,.48f);
    }
    System.Collections.IEnumerator ReviewGojo() {
      ClearCombat();foreach(var d in dummies)if(d)d.gameObject.SetActive(false);
      var oldPos=view.transform.position;var oldRot=view.transform.rotation;float oldFov=view.fieldOfView;view.fieldOfView=48;
      var point=new Vector3(0,-997.4f,12);
      var forms=new[]{GojoFx.Form.Blue,GojoFx.Form.RedBurst,GojoFx.Form.Merge,GojoFx.Form.Purple,GojoFx.Form.Void,GojoFx.Form.Ward};
      string[] labels={"gojo-blue-front","gojo-red-burst","gojo-purple-merge","gojo-purple-bolt","gojo-unlimited-void","gojo-infinity-contact"};
      for(int i=0;i<forms.Length;i++) {
        ClearCombat();bool field=forms[i]==GojoFx.Form.Void;
        view.transform.position=field?new Vector3(0,-997.4f,0):point+new Vector3(0,1,-8);
        view.transform.rotation=field?Quaternion.identity:Quaternion.LookRotation(point-view.transform.position);
        var root=GojoEffect(forms[i],field?view.transform.position:point,Quaternion.identity,field?1:forms[i]==GojoFx.Form.Ward?2:2.2f,forms[i]==GojoFx.Form.RedBurst?.9f:4);
        yield return new WaitForSecondsRealtime(forms[i]==GojoFx.Form.RedBurst?.16f:.6f);
        yield return StartCoroutine(CaptureVfx(labels[i]));
        if(forms[i]==GojoFx.Form.Blue) {
          view.transform.position=point+new Vector3(6,.5f,-5);view.transform.rotation=Quaternion.LookRotation(point-view.transform.position);
          yield return new WaitForSecondsRealtime(.15f);yield return StartCoroutine(CaptureVfx("gojo-blue-three-quarter"));
        }
        Logger.LogInfo("JJK_GOJO_RENDER form="+forms[i]+" particles="+root.GetComponent<GojoMotion>().VisibleParticles+" colliders="+root.GetComponentsInChildren<Collider>().Length);
      }
      ClearCombat();view.transform.position=oldPos;view.transform.rotation=oldRot;view.fieldOfView=oldFov;foreach(var d in dummies)if(d)d.gameObject.SetActive(true);
    }
  }
}
