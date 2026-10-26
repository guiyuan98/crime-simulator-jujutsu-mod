using System;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace CrimeJJK {
  // Passive native Unity UI: no input focus, no raycasts, and the same widgets in runtime/capture.
  public sealed class SkillHud : MonoBehaviour {
    public sealed class State {
      public bool Visible,Ready,Enabled,Crosshair;
      public int Character;
      public float Energy;
      public Color Accent,Flash;
      public string Status,Notice,Feedback,Aim,Footer;
      public readonly string[] Keys=new string[4],Names=new string[4];
      public readonly float[] Remaining=new float[4],Cooldown=new float[4],Cost=new float[4];
    }
    public Canvas Canvas {get;private set;}
    public RectTransform Card {get;private set;}
    public readonly Text[] KeyLabels=new Text[4],NameLabels=new Text[4],StatusLabels=new Text[4];
    readonly Image[] keyBack=new Image[4],rowBack=new Image[4],cooldownFill=new Image[4];
    Text character,avatar,status,energy,footer,notice,feedback,aim;
    Image energyFill,avatarBack,statusDot,flash;
    RectTransform toast,crosshair;
    Image crossV,crossH;
    Texture2D roundTexture;
    Sprite roundSprite;
    Font font;
    static readonly Color Ink=new Color(.91f,.93f,.97f),Muted=new Color(.54f,.59f,.68f);
    public static SkillHud Create(Transform parent,Font font) {
      var root=new GameObject("JJK_HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));root.transform.SetParent(parent,false);
      var hud=root.AddComponent<SkillHud>();hud.Build(font);return hud;
    }
    static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h) {
      var go=new GameObject("JJK_"+name,typeof(RectTransform));var r=(RectTransform)go.transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;
    }
    Image Panel(Transform parent,string name,float x,float y,float w,float h,Color color,bool round=true) {
      var r=Rect(parent,name,x,y,w,h);var image=r.gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;if(round){image.sprite=roundSprite;image.type=Image.Type.Sliced;}return image;
    }
    Text Label(Transform parent,string name,float x,float y,float w,float h,int size,Color color,TextAnchor align=TextAnchor.MiddleLeft,bool bold=false) {
      var r=Rect(parent,name,x,y,w,h);var text=r.gameObject.AddComponent<Text>();text.font=font;text.fontSize=size;text.color=color;text.alignment=align;text.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;return text;
    }
    void Build(Font typeface) {
      font=typeface;Canvas=GetComponent<Canvas>();Canvas.renderMode=RenderMode.ScreenSpaceOverlay;Canvas.sortingOrder=32000;
      var scaler=GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
      roundTexture=new Texture2D(40,40,TextureFormat.RGBA32,false);roundTexture.wrapMode=TextureWrapMode.Clamp;
      for(int y=0;y<40;y++)for(int x=0;x<40;x++){float dx=Mathf.Max(Mathf.Abs(x-19.5f)-9.5f,0),dy=Mathf.Max(Mathf.Abs(y-19.5f)-9.5f,0);roundTexture.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(10.5f-Mathf.Sqrt(dx*dx+dy*dy))));}
      roundTexture.Apply();roundSprite=Sprite.Create(roundTexture,new Rect(0,0,40,40),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(10,10,10,10));
      flash=Panel(transform,"flash",0,0,1,1,Color.clear,false);var fr=(RectTransform)flash.transform;fr.anchorMin=Vector2.zero;fr.anchorMax=Vector2.one;fr.offsetMin=fr.offsetMax=Vector2.zero;
      var shadow=Panel(transform,"hud_shadow",0,0,272,238,new Color(0,0,0,.24f));var sr=(RectTransform)shadow.transform;sr.anchorMin=sr.anchorMax=Vector2.zero;sr.pivot=Vector2.zero;sr.anchoredPosition=new Vector2(22,17);
      var body=Panel(transform,"skill_card",0,0,272,238,new Color(.035f,.043f,.061f,.96f));Card=(RectTransform)body.transform;Card.anchorMin=Card.anchorMax=Vector2.zero;Card.pivot=Vector2.zero;Card.anchoredPosition=new Vector2(20,20);
      avatarBack=Panel(Card,"avatar",12,12,30,30,new Color(.35f,.07f,.12f,.75f));avatar=Label(avatarBack.transform,"seal",0,0,30,30,17,Ink,TextAnchor.MiddleCenter,true);
      character=Label(Card,"character",50,9,156,24,18,Ink,TextAnchor.MiddleLeft,true);
      statusDot=Panel(Card,"status_dot",51,40,4,4,new Color(.42f,.8f,.62f),false);status=Label(Card,"status",60,34,196,15,10,Muted);
      Label(Card,"energy_caption",12,54,70,18,11,Muted).text="咒力";
      energy=Label(Card,"energy_value",86,53,174,19,12,Ink,TextAnchor.MiddleRight);
      Panel(Card,"energy_track",12,76,248,4,new Color(.16f,.18f,.23f));energyFill=Panel(Card,"energy_fill",12,76,248,4,Color.red);
      for(int i=0;i<4;i++) {
        float y=90+i*29;
        rowBack[i]=Panel(Card,"skill_row_"+i,10,y,252,26,new Color(.075f,.085f,.11f,.92f));
        keyBack[i]=Panel(Card,"keycap_"+i,14,y+3,24,20,new Color(.3f,.07f,.1f));KeyLabels[i]=Label(Card,"key_"+i,14,y+3,24,20,13,Ink,TextAnchor.MiddleCenter,true);
        NameLabels[i]=Label(Card,"name_"+i,48,y+1,150,24,14,Ink);
        StatusLabels[i]=Label(Card,"cooldown_"+i,198,y+1,54,24,11,Muted,TextAnchor.MiddleRight);
        cooldownFill[i]=Panel(Card,"cooldown_line_"+i,48,y+24,204,2,new Color(.5f,.12f,.18f),false);
      }
      footer=Label(Card,"aux_keys",12,214,248,17,10,Muted);
      toast=(RectTransform)Panel(transform,"notification",0,0,272,58,new Color(.035f,.043f,.061f,.94f)).transform;toast.anchorMin=toast.anchorMax=Vector2.zero;toast.pivot=Vector2.zero;toast.anchoredPosition=new Vector2(20,266);
      notice=Label(toast,"notice",12,7,248,23,12,Ink);feedback=Label(toast,"feedback",12,30,248,19,10,Muted);
      crosshair=Rect(transform,"crosshair",0,0,18,18);crosshair.anchorMin=crosshair.anchorMax=new Vector2(.5f,.5f);crosshair.pivot=new Vector2(.5f,.5f);crosshair.anchoredPosition=Vector2.zero;
      crossV=Panel(crosshair,"cross_v",8,2,1.5f,14,Color.white,false);crossH=Panel(crosshair,"cross_h",2,8,14,1.5f,Color.white,false);
      aim=Label(transform,"target",16,18,350,24,11,Ink);var ar=(RectTransform)aim.transform;ar.anchorMin=ar.anchorMax=new Vector2(.5f,.5f);ar.anchoredPosition=new Vector2(16,-18);
    }
    public void Apply(State s,int layer) {
      if(gameObject.activeSelf!=s.Visible)gameObject.SetActive(s.Visible);if(!s.Visible)return;
      if(gameObject.layer!=layer){foreach(var t in GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;}
      avatar.text=s.Character==1?"宿":"悟";character.text=s.Character==1?"两面宿傩":"五条悟";
      avatarBack.color=new Color(s.Accent.r*.42f,s.Accent.g*.42f,s.Accent.b*.42f,.9f);status.text=s.Status+"  ·  "+(s.Enabled?"自动护体":"术式停用");statusDot.color=s.Ready?new Color(.38f,.82f,.62f):Muted;
      energy.text=s.Energy.ToString("0")+" <color=#697486>/ 5000</color>";energy.supportRichText=true;energyFill.color=s.Accent;((RectTransform)energyFill.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,248*Mathf.Clamp01(s.Energy/5000));
      for(int i=0;i<4;i++) {
        float remain=s.Remaining[i];bool ready=s.Ready && remain<=0 && s.Energy>=s.Cost[i];KeyLabels[i].text=s.Keys[i];NameLabels[i].text=s.Names[i];
        NameLabels[i].color=ready?Ink:new Color(.59f,.63f,.71f);KeyLabels[i].color=ready?new Color(1,.95f,.95f):Muted;
        keyBack[i].color=ready?new Color(s.Accent.r*.25f+.07f,s.Accent.g*.25f+.025f,s.Accent.b*.25f+.04f,.95f):new Color(.12f,.13f,.17f);
        StatusLabels[i].text=!s.Enabled?"停用":remain>0?remain.ToString("0.0")+"s":!s.Ready?"等待":s.Energy<s.Cost[i]?"咒力不足":"就绪";
        StatusLabels[i].color=ready?new Color(.47f,.8f,.66f):Muted;
        cooldownFill[i].gameObject.SetActive(remain>0);cooldownFill[i].color=new Color(s.Accent.r,s.Accent.g,s.Accent.b,.55f);
        ((RectTransform)cooldownFill[i].transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,204*Mathf.Clamp01(1-remain/Mathf.Max(.1f,s.Cooldown[i])));
      }
      footer.text=s.Footer;notice.text=s.Notice??"";feedback.text=s.Feedback??"";
      bool hasNotice=!string.IsNullOrEmpty(s.Notice),hasFeedback=!string.IsNullOrEmpty(s.Feedback);toast.gameObject.SetActive(hasNotice||hasFeedback);toast.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,hasNotice && hasFeedback?58:36);
      notice.gameObject.SetActive(hasNotice);((RectTransform)feedback.transform).anchoredPosition=new Vector2(12,hasNotice?-30:-7);
      crosshair.gameObject.SetActive(s.Crosshair);crossV.color=crossH.color=new Color(s.Accent.r,s.Accent.g,s.Accent.b,.8f);aim.text=s.Crosshair?s.Aim:"";
      flash.gameObject.SetActive(s.Flash.a>0);flash.color=s.Flash;
    }
    public bool LabelsMatch(string[] labels){for(int i=0;i<4;i++)if(KeyLabels[i].text!=labels[i])return false;return true;}
    void OnDestroy(){if(roundSprite)Object.Destroy(roundSprite);if(roundTexture)Object.Destroy(roundTexture);}
  }
}
