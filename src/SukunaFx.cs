using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CrimeJJK {
  // Build-time video extraction; the shipped mod needs no video decoder or network.
  public sealed class SukunaFx : IDisposable {
    public readonly Sprite[] SlashFrames = new Sprite[23];
    public readonly AudioClip Cut, Charge, Release;
    public int Slashes, Flames;
    public readonly ShrineModel Model;
    public readonly FugaModel FireModel;
    readonly Texture2D atlas, flameTexture;
    readonly Material flameMaterial, lineMaterial, darkMaterial, boneMaterial, detailMaterial;
    public SukunaFx(Material lines) {
      lineMaterial=lines;
      atlas=new Texture2D(2,2,TextureFormat.RGBA32,false);
      using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("jjk.slash.png")) {
        if(stream==null)throw new InvalidDataException("Embedded slash atlas missing");
        using(var bytes=new MemoryStream()){stream.CopyTo(bytes);if(!ImageConversion.LoadImage(atlas,bytes.ToArray(),false))throw new InvalidDataException("Slash atlas decode failed");}
      }
      atlas.wrapMode=TextureWrapMode.Clamp;
      for(int i=0;i<23;i++)SlashFrames[i]=Sprite.Create(atlas,new Rect((i%6)*320,atlas.height-(i/6+1)*180,320,180),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);
      flameTexture=new Texture2D(32,64,TextureFormat.RGBA32,false);
      for(int y=0;y<64;y++)for(int x=0;x<32;x++) {
        float v=y/63f,dx=Mathf.Abs((x-15.5f)/15.5f + Mathf.Sin(v*13)*v*.16f);
        float a=Mathf.Clamp01((1-dx/Mathf.Max(.04f,Mathf.Sin(Mathf.PI*v)*(.8f-.5f*v)))*4)*Mathf.Sin(Mathf.PI*v);
        flameTexture.SetPixel(x,y,new Color(1,1,1,a));
      }
      flameTexture.Apply();flameTexture.wrapMode=TextureWrapMode.Clamp;
      flameMaterial=new Material(Shader.Find("Legacy Shaders/Particles/Additive") ?? lines.shader);flameMaterial.mainTexture=flameTexture;
      darkMaterial=new Material(Shader.Find("Unlit/Color") ?? lines.shader);darkMaterial.color=new Color(.055f,.018f,.025f);
      boneMaterial=new Material(darkMaterial);boneMaterial.color=new Color(.75f,.62f,.47f);
      Cut=MakeSound("JJK_cut_swish_swish_swish",.66f,0);
      Charge=MakeSound("JJK_fuga_charge",1.25f,1);
      Release=MakeSound("JJK_fuga_roar",1.1f,2);
      Model=new ShrineModel();
      FireModel=new FugaModel(lines.shader);
      detailMaterial=new Material(lines.shader);detailMaterial.mainTexture=FireModel.FlameTexture;detailMaterial.color=Color.white;
    }
    static AudioClip MakeSound(string name,float seconds,int type) {
      const int rate=22050;var data=new float[(int)(seconds*rate)];var rng=new System.Random(1437+type);float previous=0,low=0,phase=0;
      for(int i=0;i<data.Length;i++) {
        float t=i/(float)rate,noise=(float)rng.NextDouble()*2-1;low=Mathf.Lerp(low,noise,type==0?.38f:.06f);
        if(type==0) {
          float value=0;
          for(int j=0;j<3;j++){float p=(t-j*.155f)/.29f;if(p<0 || p>1)continue;float e=Mathf.Sin(p*Mathf.PI);value+=(noise-previous)*e*e*.24f+low*e*.22f;}
          data[i]=Mathf.Clamp(value,-.8f,.8f);
        } else {
          float p=t/seconds,e=type==1?Mathf.Sin(p*Mathf.PI*.85f)*p:Mathf.Pow(1-p,1.6f);
          phase+=(type==1?70+p*180:100-p*65)*Mathf.PI*2/rate;
          data[i]=(low*.8f+noise*.1f+Mathf.Sin(phase)*.13f)*e*.7f;
        }
        previous=noise;
      }
      var clip=AudioClip.Create(name,data.Length,1,rate,false);clip.SetData(data,0);return clip;
    }
    public void Slash(GameObject root,Vector3 position,Quaternion facing,float width) {
      Slashes++;root.transform.position=position;root.transform.rotation=facing*Quaternion.Euler(0,0,UnityEngine.Random.Range(-55f,55f));
      root.transform.localScale=Vector3.one*(width/3.2f);
      var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=SlashFrames[0];renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
      root.AddComponent<SlashAnimation>().Frames=SlashFrames;
    }
    LineRenderer Line(GameObject parent,string name,Vector3[] points,Color color,float width) {
      var go=new GameObject("JJK_"+name);go.layer=parent.layer;go.transform.SetParent(parent.transform,false);
      var r=go.AddComponent<LineRenderer>();r.sharedMaterial=lineMaterial;r.useWorldSpace=false;r.positionCount=points.Length;r.SetPositions(points);r.startWidth=r.endWidth=width;r.startColor=r.endColor=color;return r;
    }
    public ParticleSystem Flame(GameObject parent,Vector3 local,float size,float duration,bool loop,Vector3 direction) {
      Flames++;
      var go=new GameObject("JJK_flame_tongues");go.layer=parent.layer;go.transform.SetParent(parent.transform,false);go.transform.localPosition=local;go.transform.localRotation=Quaternion.FromToRotation(Vector3.forward,direction);
      var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
      var main=ps.main;main.duration=duration;main.loop=loop;main.startLifetime=new ParticleSystem.MinMaxCurve(.25f,.6f);main.startSpeed=new ParticleSystem.MinMaxCurve(size*1.5f,size*4);main.startSize=new ParticleSystem.MinMaxCurve(size*.5f,size*1.5f);main.startRotation=new ParticleSystem.MinMaxCurve(-.25f,.25f);main.maxParticles=128;main.simulationSpace=ParticleSystemSimulationSpace.World;main.gravityModifier=-.15f;main.playOnAwake=false;
      var emission=ps.emission;emission.rateOverTime=65;
      var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=15;shape.radius=size*.15f;
      var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(1,1,.65f),0),new GradientColorKey(new Color(1,.55f,.03f),.3f),new GradientColorKey(new Color(.9f,.04f,.005f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.95f,.08f),new GradientAlphaKey(.65f,.55f),new GradientAlphaKey(0,1)});
      var color=ps.colorOverLifetime;color.enabled=true;color.color=gradient;
      var sizing=ps.sizeOverLifetime;sizing.enabled=true;sizing.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.25f),new Keyframe(.25f,1),new Keyframe(1,0)));
      var render=go.GetComponent<ParticleSystemRenderer>();render.sharedMaterial=flameMaterial;render.renderMode=ParticleSystemRenderMode.Billboard;
      ps.Play();return ps;
    }
    public void Arrow(GameObject root) {
      FireModel.Arrow(root,true);DetailFlame(root,new Vector3(0,0,-.15f),.15f,Vector3.back,45);
    }
    public void Bow(GameObject root) {
      FireModel.Bow(root);
      for(int i=-2;i<=2;i++)DetailFlame(root,new Vector3(0,i*.28f,.14f),.065f,Vector3.up,22);
    }
    void DetailFlame(GameObject root,Vector3 p,float size,Vector3 direction,float rate) {
      var ps=FireFlame(root,p,size,1,true,direction);var main=ps.main;main.startLifetime=new ParticleSystem.MinMaxCurve(.18f,.42f);main.startSize=new ParticleSystem.MinMaxCurve(size*.7f,size*1.7f);main.startSpeed=new ParticleSystem.MinMaxCurve(.3f,.8f);main.maxParticles=48;var emission=ps.emission;emission.rateOverTime=rate;
    }
    public ParticleSystem FireFlame(GameObject root,Vector3 p,float size,float duration,bool loop,Vector3 direction) {
      var ps=Flame(root,p,size,duration,loop,direction);ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=detailMaterial;var main=ps.main;main.startColor=Color.white;
      var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.9f,.12f),new GradientAlphaKey(0,1)});var color=ps.colorOverLifetime;color.color=gradient;return ps;
    }
    public void Shrine(GameObject root) {
      Model.Instantiate(root);
    }
    public void Dispose() {
      FireModel.Dispose();
      Object.Destroy(detailMaterial);
      Model.Dispose();
      foreach(var sprite in SlashFrames)if(sprite)Object.Destroy(sprite);
      Object.Destroy(atlas);Object.Destroy(flameTexture);Object.Destroy(flameMaterial);Object.Destroy(darkMaterial);Object.Destroy(boneMaterial);Object.Destroy(Cut);Object.Destroy(Charge);Object.Destroy(Release);
    }
  }
  public sealed class SlashAnimation : MonoBehaviour {
    public Sprite[] Frames;float born;SpriteRenderer render;
    void Awake(){born=Time.time;render=GetComponent<SpriteRenderer>();}
    void Update(){int i=(int)((Time.time-born)*30);if(Frames==null || i>=Frames.Length){Destroy(gameObject);return;}render.sprite=Frames[i];}
  }
}
