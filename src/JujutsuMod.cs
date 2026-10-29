using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CrimeJJK {
  [BepInPlugin("local.crimesimulator.jujutsu", "Jujutsu Local", "1.5.5")]
  [BepInProcess("Crime Simulator.exe")]
  [DefaultExecutionOrder(-1000)]
  public sealed partial class JujutsuMod : BaseUnityPlugin {
    public static JujutsuMod Instance;
    readonly Rules rules = new Rules();
    readonly List<GameObject> effects = new List<GameObject>();
    readonly Dictionary<NPC, Frozen> frozen = new Dictionary<NPC,Frozen>();
    readonly List<Dummy> dummies = new List<Dummy>();
    readonly HashSet<NPC> slain = new HashSet<NPC>();
    ConfigEntry<bool> active;
    SkillAudio skillAudio;ConfigEntry<float> voiceVolume,effectVolume;
    ConfigEntry<KeyCode>[] keys;
    static readonly KeyCode[] SkillKeys={KeyCode.F,KeyCode.G,KeyCode.V,KeyCode.X};
    SkillHud skillHud;
    readonly SkillHud.State hudState=new SkillHud.State();
    readonly List<Rewired.ActionElementMap> reservedNative=new List<Rewired.ActionElementMap>();
    Rewired.Player nativeOwner;KeyCode[] nativeCodes=new KeyCode[0];float nativeScanAt;
    Harmony harmony;
    Material material, solidMaterial;
    SukunaFx sukunaFx;
    Vector3 domainCenter, domainForward;
    float nextDomainFx, nextDomainSound;
    int combatRevision, fireCharges, fireShots, fireImpacts, domainBursts, wakeBlocks;
    Font font;
    GUIStyle label;
    FPP_Player player;
    NPC[] npcs = new NPC[0];
    Camera view;
    bool infinity, hud = true, arena, selfTest;
    float scanAt, nextHeal, domainUntil, blueUntil, flashUntil, specialReady;
    Vector3 bluePoint;
    string notice = "F / G / V / X 施放 · 5 切换角色";
    float noticeUntil = 8;
    Color flashColor;
    int frameErrors;
    bool firstFrame;
    string runId="manual";
    float configPollAt;
    DateTime configStamp;
    string lastPlayerState;
    string hitFeedback="瞄准 NPC 后施放；命中会显示实际血量变化";
    float hitFeedbackUntil;
    string aimHint="";
    readonly Color blue = new Color(.12f,.65f,1), red = new Color(1,.12f,.2f), purple = new Color(.64f,.2f,1), fire = new Color(1,.45f,.05f);
    readonly string[,] names = {{"术式顺转 · 苍","术式反转 · 赫","虚式 · 茈","无量空处"},{"解","捌","灶 · 开","伏魔御厨子"}};
    Scene arenaScene;
    GameObject arenaRoot;
    Canvas[] hiddenCanvases;
    bool[] canvasStates;
    UnityEngine.EventSystems.EventSystem[] hiddenEventSystems;
    bool[] eventSystemStates;
    CursorLockMode oldLock;
    bool oldCursor;
    float oldTimeScale;
    Vector2 look;
    float verticalSpeed;
    CharacterController arenaController;
    sealed class Frozen { public bool Stopped; public float Speed, Until; }

    void Awake() {
      Instance=this;
      active=Config.Bind("General","Enabled",true,"Enable local skills. Hotkeys are configured in Keys.");
      voiceVolume=Config.Bind("Audio","VoiceVolume",.85f,"Short Japanese dialogue volume (0..1);0 mutes dialogue.");
      effectVolume=Config.Bind("Audio","EffectVolume",.65f,"Mod effect volume (0..1); speech ducks only the mod's own effects.");
      string[] labels={"Skill1","Skill2","Skill3","Domain","SwitchCharacter","Special","PracticeDummy","ToggleHUD","ToggleMod","PracticeArena"};
      keys=new ConfigEntry<KeyCode>[10];
      for(int i=0;i<10;i++) keys[i]=Config.Bind("Keys",labels[i],i<4?SkillKeys[i]:(KeyCode)((int)KeyCode.Keypad0+(i+1)%10),"Skill defaults: F/G/V/X. Numeric bindings accept both number row and numpad.");
      configStamp=System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);
      Shader shader=Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default") ?? Shader.Find("Hidden/Internal-Colored");
      if(shader==null) throw new Exception("No supported effect shader");
      material=new Material(shader);
      solidMaterial=new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard") ?? shader);
      solidMaterial.EnableKeyword("_EMISSION");
      solidMaterial.color=new Color(.08f,.09f,.13f);solidMaterial.SetColor("_EmissionColor",new Color(.08f,.09f,.13f));
      sukunaFx=new SukunaFx(material);
      gojoFx=new GojoFx(shader);
      skillAudio=new SkillAudio(transform);
      font=Font.CreateDynamicFontFromOSFont(new string[]{"Microsoft YaHei","SimHei","Arial"},20);
      skillHud=SkillHud.Create(transform,font);
      harmony=new Harmony("local.crimesimulator.jujutsu");
      harmony.PatchAll(typeof(JujutsuMod).Assembly);
      SceneManager.activeSceneChanged+=SceneChanged;
      selfTest=Environment.GetCommandLineArgs().Contains("-jjk-selftest");
      var args=Environment.GetCommandLineArgs();int runIndex=Array.IndexOf(args,"-jjk-run-id");
      if(runIndex>=0 && runIndex+1<args.Length)runId=args[runIndex+1];
      Logger.LogInfo("JJK_READY v1.5.5 | skills="+string.Join("/",keys.Take(4).Select(k=>k.Value.ToString()).ToArray())+" nativeHUD=True maxEnergy="+Rules.MaxEnergy+" autoShield=True sukunaPermanent=True damageMultiplier="+Rules.DamageMultiplier+" audioClips="+skillAudio.Loaded+" audioMissing="+skillAudio.Missing+" | "+Controls());
      Logger.LogInfo("JJK_SELFTEST_REQUESTED="+selfTest);
      Logger.LogInfo("JJK_COMPONENT active="+gameObject.activeInHierarchy+" enabled="+enabled+" object="+gameObject.name);
    }
    void OnEnable() { Logger.LogInfo("JJK_ON_ENABLE"); }
    void OnDisable() { Logger.LogInfo("JJK_ON_DISABLE");RestoreCharacters();RestoreNativeKeys();LeaveArena();ClearCombat(); }
    void OnApplicationQuit() { RestoreNativeKeys(); }
    void Start() { Logger.LogInfo("JJK_START");if(selfTest)StartCoroutine(SelfTest()); }

    void SceneChanged(Scene oldScene, Scene newScene) { RestoreCharacters();ResetEnvironmentCache();RestoreNativeKeys();ClearCombat();slain.RemoveWhere(n=>!n || !n.gameObject.scene.isLoaded);player=null;if(!arena)view=null;scanAt=0; }
    bool PlayerBound {get{return player && player.Object && player.Object.IsValid;}}
    bool SoloReady() {
      if(arena) return true;
      var wm=WorldManager.Instance;
      if(!wm || wm.localPlayer!=player || !player || !player.Object || !player.Object.IsValid || !player.Runner || !player.Runner.IsRunning) return false;
      return Rules.LocalPlayerAllowed(player.Runner.IsSinglePlayer,player.Runner.ActivePlayers.Take(2).Count(),player.HasStateAuthority) && !player.isDead && player.spawnedAndReady;
    }
    bool CombatReady() { return active.Value && SoloReady() && view && (arena || (!player.pauseActive && !player.chatActive)); }
    void Scan() {
      if(arena) return;
      var wm=WorldManager.Instance;
      player=wm ? wm.localPlayer : null;
      if(skillAudio!=null)skillAudio.Route(player);
      view=player ? player.myCamera : null;
      npcs=Object.FindObjectsOfType<NPC>();
      if(npcs.Length==0 && player)npcs=Resources.FindObjectsOfTypeAll<NPC>().Where(n=>n && n.gameObject.scene.IsValid() && n.gameObject.scene.isLoaded && n.gameObject.activeInHierarchy).ToArray();
      if(player && !SoloReady()) infinity=false;
      string state="scene="+SceneManager.GetActiveScene().name+" localPlayer="+(player!=null)+" camera="+(view!=null);
      if(player && player.Object && player.Object.IsValid && player.Runner && player.Runner.IsRunning)
        state+=" single="+player.Runner.IsSinglePlayer+" inputAuthority="+player.HasInputAuthority+" stateAuthority="+player.HasStateAuthority+" spawned="+player.spawnedAndReady+" ready="+SoloReady();
      if(state!=lastPlayerState){lastPlayerState=state;Logger.LogInfo("JJK_PLAYER_STATE "+state);}
      aimHint="";
      if(view && SoloReady())foreach(var hit in Physics.RaycastAll(view.transform.position,view.transform.forward,45,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(x=>x.distance)) {
        if(IgnoredCollider(hit.collider))continue;
        var n=NpcForCollider(hit.collider);
        if(n && n.Object && n.Object.IsValid && n.isReady)aimHint="目标 NPC · HP "+n.health.ToString("0.0")+" / "+n.maxHealth.ToString("0.0");
        break;
      }
    }
    void Update() {
      try { UpdateCore(); }
      catch(Exception ex) { if(frameErrors++ < 3) Logger.LogError(ex); ClearCombat(); }
    }
    void UpdateCore() {
      if(skillAudio!=null)skillAudio.Tick(voiceVolume.Value,effectVolume.Value);
      if(!firstFrame){firstFrame=true;Logger.LogInfo("JJK_FIRST_FRAME scene="+SceneManager.GetActiveScene().name);}
      if(selfTest)Application.runInBackground=true;
      if(Time.unscaledTime>=scanAt) { scanAt=Time.unscaledTime+.5f;Scan();slain.RemoveWhere(n=>!n); }
      if(Time.unscaledTime>=configPollAt) {
        configPollAt=Time.unscaledTime+1;
        var stamp=System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);
        if(stamp!=configStamp) { Config.Reload();configStamp=stamp;Logger.LogInfo("JJK_KEYS_RELOADED "+Controls()); }
      }
      SyncNativeKeys(!arena && CombatReady() && !TextEntryActive()?player.rewiredPlayer:null,keys.Take(4).Select(k=>k.Value).ToArray());
      if(!selfTest && !(PlayerBound && player.chatActive) && !TextEntryActive())for(int action=0;action<10;action++) {
        var key=keys[action].Value;var alias=NumericAlias(key);
        if(Input.GetKeyDown(key))HandleKey(key,"keyboard");
        else if(alias!=KeyCode.None && Input.GetKeyDown(alias))HandleKey(alias,"keyboard");
      }
      RestoreExpired();
      if(!CombatReady()) { if(!arena){bool keep=active.Value && PlayerBound && !player.isDead && rules.Character==0 && (player.pauseActive || player.chatActive);ClearCombat(keep);if(keep && infinity){rules.Tick(Time.deltaTime,true);if(rules.Energy<=0)infinity=false;}}return; }
      if(arena && !selfTest) MoveArena();
      rules.Tick(Time.deltaTime,infinity);
      if(rules.Energy<=0) infinity=false;
      if(Time.time<blueUntil) PullBlue();
      if(Time.time<domainUntil && rules.Character==1) {
        if(Time.time>=nextHeal){nextHeal=Time.time+.5f;Area(domainCenter,22,18,red,false,false);}
        if(Time.time>=nextDomainFx) {
          nextDomainFx=Time.time+.12f;domainBursts++;
          var right=Vector3.Cross(Vector3.up,domainForward).normalized;
          for(int i=0;i<8;i++) {
            Vector3 p=i<4?domainCenter+domainForward*UnityEngine.Random.Range(5f,18f)+right*UnityEngine.Random.Range(-10f,10f)+Vector3.up*UnityEngine.Random.Range(-.4f,5f):domainCenter+UnityEngine.Random.insideUnitSphere*20;
            p.y=Mathf.Clamp(p.y,domainCenter.y-1,domainCenter.y+6);Slash(p,red,UnityEngine.Random.Range(2.5f,5f));
          }
        }
        if(Time.time>=nextDomainSound){nextDomainSound=Time.time+.36f;PlaySound(sukunaFx.Cut,.32f,UnityEngine.Random.Range(.85f,1.15f));}
      }
      effects.RemoveAll(x=>!x);
      dummies.RemoveAll(x=>!x);
    }
    static KeyCode NumericAlias(KeyCode key) {
      if(key>=KeyCode.Keypad0 && key<=KeyCode.Keypad9)return (KeyCode)((int)KeyCode.Alpha0+(int)key-(int)KeyCode.Keypad0);
      if(key>=KeyCode.Alpha0 && key<=KeyCode.Alpha9)return (KeyCode)((int)KeyCode.Keypad0+(int)key-(int)KeyCode.Alpha0);
      return KeyCode.None;
    }
    static bool TextEntryActive() {
      var es=UnityEngine.EventSystems.EventSystem.current;
      var selected=es?es.currentSelectedGameObject:null;if(!selected)return false;
      var text=selected.GetComponent<TMPro.TMP_InputField>();if(text && text.isFocused)return true;
      var legacy=selected.GetComponent<UnityEngine.UI.InputField>();return legacy && legacy.isFocused;
    }
    int ActionForKey(KeyCode observed) {
      for(int action=0;action<keys.Length;action++)if(keys[action].Value==observed || NumericAlias(keys[action].Value)==observed)return action;
      return -1;
    }
    bool HandleKey(KeyCode observed,string source) { return Dispatch(ActionForKey(observed),source); }
    string KeyName(int action) {
      var key=keys[action].Value;
      if(key>=KeyCode.Keypad0 && key<=KeyCode.Keypad9)return ((int)key-(int)KeyCode.Keypad0).ToString();
      if(key>=KeyCode.Alpha0 && key<=KeyCode.Alpha9)return ((int)key-(int)KeyCode.Alpha0).ToString();
      return key.ToString();
    }
    string Controls() { return KeyName(4)+" 切换 | "+KeyName(5)+" 特殊 | "+KeyName(6)+" 靶 | "+KeyName(7)+" 面板 | "+KeyName(8)+" 开关 | "+KeyName(9)+" 训练/返回"; }
    bool Dispatch(int action,string source) {
      if(action<0 || action>=10)return false;
      Logger.LogInfo("JJK_INPUT action="+action+" source="+source+" binding="+keys[action].Value+" arena="+arena);
      if(action==8){active.Value=!active.Value;ClearCombat();Tell(active.Value?"术式已启用":"术式已停用");return true;}
      if(action==7){hud=!hud;return true;}
      if(action==9){if(arena)LeaveArena();else if(!player)EnterArena();else Tell("当前地图直接用 "+string.Join("/",Enumerable.Range(0,4).Select(KeyName).ToArray())+" 施放；主菜单进入训练场。");return true;}
      if(!active.Value){Tell("模组已停用；按 "+KeyName(8)+" 开启。");return false;}
      if(!arena && !player)EnterArena();
      if(!CombatReady()){Tell(PlayerBound && player.pauseActive?"请关闭暂停菜单再施放技能。":"等待本地单人玩家就绪；也可从主菜单进入训练场。");return false;}
      if(action<4)return Cast(action);
      if(action==4){rules.Character=1-rules.Character;ClearCombat();Tell(rules.Character==0?"五条悟 · 最强术师":"两面宿傩 · 诅咒之王");}
      else if(action==5)Special();
      else if(action==6)SpawnDummy(view.transform.position+view.transform.forward*7);
      return true;
    }
    void Special() {
      if(rules.Character==0) { infinity=!infinity && rules.Energy>5;if(infinity){GojoWardCue();if(skillAudio!=null)skillAudio.Say(0,4);}Tell(infinity?"无下限术式 · 开":"无下限术式 · 关");return; }
      if(Time.time<specialReady || rules.Energy<30) { Tell("反转术式冷却中或咒力不足");return; }
      if(!arena && player.damage<=0) { Tell("当前无需治疗");return; }
      rules.Energy-=30;specialReady=Time.time+12;
      if(player && !arena) player.damage=Mathf.Max(0,player.damage-40);
      Pulse(view.transform.position+view.transform.forward*2,Color.green,2,.6f);
      if(skillAudio!=null)skillAudio.Say(1,4);
      Tell("反转术式 · 恢复 40 受伤值");
    }
    bool Cast(int slot) {
      if(!CombatReady() || !rules.Spend(slot,Time.time)) { Tell("咒力不足 / 技能冷却中");return false; }
      Tell(names[rules.Character,slot]);
      Logger.LogInfo("JJK_CAST character="+rules.Character+" slot="+slot+" arena="+arena);
      Vector3 origin=view.transform.position, forward=view.transform.forward;
      if(rules.Character==0) {
        GojoCast(slot);
      } else {
        if(slot==0) { var p=Aim(40);Slash(p,red,4);Area(p,3.2f,38,red,false,false); }
        if(slot==1) { var p=origin+forward*2.8f;for(int i=0;i<4;i++) Slash(p+UnityEngine.Random.insideUnitSphere,red,2);Area(p,4,110,red,false,false); }
        if(slot==2) StartCoroutine(FireArrow());
        if(slot==3) {
          domainCenter=origin;domainForward=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;if(domainForward.sqrMagnitude<.1f)domainForward=Vector3.forward;
          domainUntil=Time.time+10;nextHeal=nextDomainFx=nextDomainSound=0;
          Ring(origin,22,red,10);Shrine(origin+domainForward*15);Flash(red,.7f);
          Tell("开放式领域 · 伏魔御厨子 · 持续斩击");Logger.LogInfo("JJK_OPEN_DOMAIN radius=22 duration=10 damageTick=0.5 visualBurst=8 visualInterval=0.12 barrier=False");
        }
      }
      Sound(slot);
      return true;
    }
    Vector3 Aim(float distance) {
      Vector3 o=view.transform.position, d=view.transform.forward;
      foreach(var h in Physics.RaycastAll(o,d,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(x=>x.distance)) {
        if(IgnoredCollider(h.collider)) continue;
        return h.point;
      }
      return o+d*distance;
    }
    static NPC NpcForCollider(Collider c) {
      if(!c)return null;
      var hitbox=c.GetComponent<NPC_Hitbox>() ?? c.GetComponentInParent<NPC_Hitbox>();
      return hitbox && hitbox.assignedNPC?hitbox.assignedNPC:c.GetComponentInParent<NPC>();
    }
    static bool IgnoredCollider(Collider c) {
      return !c || c.GetComponentInParent<FPP_Player>() ||
        (c.name.StartsWith("JJK_") && !c.name.Contains("floor") && !c.GetComponent<Dummy>());
    }
    struct Target {
      public NPC N;public Dummy D;public Collider C;public NPC_Hitbox H;
      public Vector3 Position { get { return C?C.bounds.center:N?N.transform.position+Vector3.up:D.transform.position; } }
      public bool Damage(JujutsuMod mod,float amount) {
        if(!N){if(!D)return false;float before=D.HP;D.Hit(amount);return D.HP<before;}
        float hp=N.health;bool down=N.knockedDown;
        bool lethal=mod.rules.Character==1;
        if(H)H.DealDamage(amount,false,lethal);else N.RPC_DealDamage(amount,false,lethal);
        float after=N.health;bool knocked=N.knockedDown;
        if(Rules.PermanentDefeat(mod.rules.Character,down,knocked)) {
          N.canGetUp=false;N.animatorWakeUp=false;mod.slain.Add(N);
          mod.Logger.LogInfo("JJK_SUKUNA_DEFEAT id="+N.GetInstanceID()+" knocked=True canGetUp="+N.canGetUp+" nativeKill=True");
        }
        bool changed=after<hp || (!down && knocked);
        mod.Logger.LogInfo("JJK_NPC_DAMAGE id="+N.GetInstanceID()+" before="+hp+" after="+after+" knocked="+knocked+" changed="+changed+" via="+(H?"NPC_Hitbox":"NPC_RPC"));
        if(changed){mod.hitFeedback=knocked?(lethal?"宿傩命中 · 已终结（本局不再苏醒）":"命中真实 NPC · 已击倒"):"命中真实 NPC · HP "+hp.ToString("0.0")+" → "+after.ToString("0.0");mod.hitFeedbackUntil=Time.unscaledTime+4;}
        return changed;
      }
      public void Move(Vector3 point) {
        if(N && N.AI) { point.y=N.transform.position.y;N.AI.Teleport(point,true); }
        else if(D) D.transform.position=point;
      }
      public void Stun(JujutsuMod mod,float seconds) {
        if(!N) { if(D) D.StunnedUntil=Time.time+seconds;return; }
        Frozen f;
        if(!mod.frozen.TryGetValue(N,out f)) {
          f=new Frozen{Stopped=N.AI && N.AI.isStopped,Speed=N.animator?N.animator.speed:1};mod.frozen[N]=f;
        }
        f.Until=Mathf.Max(f.Until,Time.time+seconds);
        if(N.AI)N.AI.isStopped=true;if(N.animator)N.animator.speed=0;
      }
    }
    List<Target> Targets(Vector3 center,float radius) {
      var result=new List<Target>();
      if(!SoloReady())return result;
      foreach(var d in dummies) if(d && d.HP>0 && Vector3.Distance(d.transform.position,center)<=radius) result.Add(new Target{D=d});
      if(!arena) {
        var found=new Dictionary<NPC,Target>();
        foreach(var n in npcs)if(n)found[n]=new Target{N=n};
        foreach(var c in Physics.OverlapSphere(center,radius,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide)) {
          var n=NpcForCollider(c);if(!n)continue;
          var h=c.GetComponent<NPC_Hitbox>() ?? c.GetComponentInParent<NPC_Hitbox>();
          Target existing;
          if(!found.TryGetValue(n,out existing) || !existing.C || (h && h.dmgMultiplier<=1))found[n]=new Target{N=n,C=c,H=h};
        }
        foreach(var t in found.Values) {
          var n=t.N;if(!n.Object || !n.Object.IsValid || !n.HasStateAuthority || !n.isReady || n.knockedDown)continue;
          if((t.C?t.C.bounds.SqrDistance(center):(t.Position-center).sqrMagnitude)<=radius*radius)result.Add(t);
        }
      }
      return result;
    }
    bool Visible(Target t,Vector3 from) {
      var d=t.Position-from;
      foreach(var h in Physics.RaycastAll(from,d.normalized,d.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) {
        if(IgnoredCollider(h.collider) || NpcForCollider(h.collider) || h.collider.GetComponent<Dummy>())continue;
        if(t.N)Logger.LogInfo("JJK_NPC_OCCLUDED id="+t.N.GetInstanceID()+" collider="+h.collider.name+" layer="+h.collider.gameObject.layer);
        return false;
      }
      return true;
    }
    int Area(Vector3 center,float radius,float damage,Color color,bool push,bool pulse=true) {
      damage=Rules.SkillDamage(damage);
      int environmentHits=DamageEnvironment(center,radius,damage);
      int hits=0,npcHits=0,dummyHits=0;var targets=Targets(center,radius);
      foreach(var t in targets) {
        if(!Visible(t,center))continue;
        if(!t.Damage(this,damage))continue;hits++;if(t.N)npcHits++;else dummyHits++;
        if(push) {
          var delta=t.Position-center;delta.y=0;var next=t.Position+delta.normalized*2;
          if(!Physics.Linecast(t.Position,next,~0,QueryTriggerInteraction.Ignore))t.Move(next);
        }
      }
      if(pulse)Pulse(center,color,Mathf.Min(radius,6),.45f);
      Logger.LogInfo("JJK_HIT count="+hits+" npc="+npcHits+" dummy="+dummyHits+" candidates="+targets.Count+" damage="+damage+" arena="+arena);
      if(!arena && npcHits==0 && environmentHits==0){hitFeedback="未伤及真实 NPC · 请瞄准并确认距离与遮挡";hitFeedbackUntil=Time.unscaledTime+2;LogNpcCoverage(center,radius);}
      return hits;
    }
    void LogNpcCoverage(Vector3 center,float radius) {
      int valid=0,authority=0,ready=0,awake=0;float nearest=float.PositiveInfinity;
      foreach(var n in npcs)if(n){nearest=Mathf.Min(nearest,Vector3.Distance(n.transform.position,center));if(n.Object && n.Object.IsValid){valid++;if(n.HasStateAuthority)authority++;if(n.isReady)ready++;if(n.isReady && !n.knockedDown)awake++;}}
      Logger.LogInfo("JJK_NPC_SCAN cached="+npcs.Length+" valid="+valid+" authority="+authority+" ready="+ready+" awake="+awake+" nearest="+nearest+" radius="+radius);
    }
    void PullBlue() {
      foreach(var t in Targets(bluePoint,10)) {
        t.Stun(this,.3f);
        var next=Vector3.MoveTowards(t.Position,bluePoint,8*Time.deltaTime);
        if(Visible(t,bluePoint))t.Move(next);
      }
    }
    IEnumerator FireArrow() {
      int revision=combatRevision;fireCharges++;
      var bow=Effect("fuga_charge_fire_bow",1.4f);sukunaFx.Bow(bow);PlaySound(sukunaFx.Charge,.8f,1);
      Tell("灶 · 开 —— 火焰弓蓄力");Logger.LogInfo("JJK_FUGA_CHARGE windup=1.2");
      float end=Time.time+1.2f;
      while(Time.time<end && bow && CombatReady() && revision==combatRevision) {
        bow.transform.position=view.transform.position+view.transform.forward*1.65f+view.transform.right*.5f-view.transform.up*.08f;
        bow.transform.rotation=view.transform.rotation*Quaternion.Euler(0,-10,-8);sukunaFx.FireModel.SetCharge(bow,1-(end-Time.time)/1.2f);yield return null;
      }
      if(bow)Destroy(bow);
      if(!CombatReady() || revision!=combatRevision){Logger.LogInfo("JJK_FUGA_CANCELLED");yield break;}
      fireShots++;Tell("灶 · 开");PlaySound(sukunaFx.Release,.85f,1);Logger.LogInfo("JJK_FUGA_RELEASE baseDamage=125 scaledDamage="+Rules.SkillDamage(125));
      yield return StartCoroutine(Projectile(view.transform.position+view.transform.forward*1.5f,view.transform.forward,fire,55,1.2f,125,9,true));
    }
    void FireImpact(Vector3 position) {
      fireImpacts++;var burst=Effect("fuga_explosion",1.8f);burst.transform.position=position;
      sukunaFx.FireModel.Impact(burst);sukunaFx.FireFlame(burst,Vector3.zero,1.6f,.7f,false,Vector3.up);
      for(int i=0;i<5;i++)sukunaFx.FireFlame(burst,UnityEngine.Random.insideUnitSphere*1.8f,.7f,.55f,false,Vector3.up);
      Ring(position,9,fire,.8f);PlaySound(sukunaFx.Release,.85f,.7f);Logger.LogInfo("JJK_FUGA_IMPACT flameEmitters=6");
    }
    IEnumerator Projectile(Vector3 position,Vector3 direction,Color color,float speed,float radius,float damage,float blast,bool flaming=false,bool gojo=false) {
      int revision=combatRevision;
      GameObject ball=flaming?Effect("fuga_flame_arrow",90/speed+1):gojo?GojoEffect(GojoFx.Form.Purple,position,Quaternion.LookRotation(direction),1.3f,90/speed+1):Orb(position,color,radius,90/speed+1);
      if(flaming){ball.transform.position=position;ball.transform.rotation=Quaternion.LookRotation(direction);sukunaFx.Arrow(ball);}
      float travelled=0;
      while(ball && travelled<90 && CombatReady() && revision==combatRevision) {
        float step=speed*Time.deltaTime;
        bool wall=false;
        foreach(var impact in Physics.SphereCastAll(position,radius*.25f,direction,step,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(x=>x.distance))if(!IgnoredCollider(impact.collider)){wall=true;position=impact.point;break;}
        var near=Targets(position,radius+1).Where(t=>Visible(t,position)).ToList();
        if(wall || near.Count>0) { Area(position,blast,damage,color,false,!flaming && !gojo);if(flaming)FireImpact(position);if(gojo)GojoEffect(GojoFx.Form.PurpleBurst,position,Quaternion.LookRotation(direction),2,.85f);Flash(color,.2f);Destroy(ball);yield break; }
        position+=direction*step;ball.transform.position=position;travelled+=step;yield return null;
      }
      if(ball) { if(CombatReady() && revision==combatRevision){Area(position,blast,damage,color,false,!flaming && !gojo);if(flaming)FireImpact(position);if(gojo)GojoEffect(GojoFx.Form.PurpleBurst,position,Quaternion.LookRotation(direction),2,.85f);}Destroy(ball); }
    }
    void RestoreExpired() {
      foreach(var n in frozen.Keys.ToArray()) {
        var f=frozen[n];if(n && Time.time<f.Until && CombatReady())continue;
        Restore(n,f);frozen.Remove(n);
      }
    }
    void Restore(NPC n,Frozen f) { if(!n)return;if(n.animator && n.animator.speed==0)n.animator.speed=f.Speed;if(n.AI && n.Object && n.Object.IsValid && !n.knockedDown)n.AI.isStopped=f.Stopped; }
    void ClearCombat(bool preserveInfinity=false) {
      if(skillAudio!=null)skillAudio.Stop();
      combatRevision++;if(!preserveInfinity)infinity=false;blueUntil=domainUntil=0;
      foreach(var p in frozen)Restore(p.Key,p.Value);frozen.Clear();
      foreach(var d in dummies)if(d)d.StunnedUntil=0;
      foreach(var e in effects)if(e)Destroy(e);effects.Clear();
    }
    public static bool BlockDamage(FPP_Player p) {
      var m=Instance;return m && m.WardContext(p) && m.InfinityReady;
    }
    [HarmonyPatch(typeof(FPP_Player),"DealDamageToPlayer")]
    static class InfinityPatch { static bool Prefix(FPP_Player __instance,ref float dmg,bool ___ressurecting,bool ___damageDelay) { return InterceptIncoming(__instance,ref dmg,___ressurecting,___damageDelay); } }
    public static bool InterceptIncoming(FPP_Player p,ref float damage,bool resurrecting,bool delay) {
      return WardBody(p,ref damage,resurrecting,delay);
    }
    [HarmonyPatch(typeof(NPC),"WakeUp")]
    static class PermanentDefeatPatch { static bool Prefix(NPC __instance) { if(!Instance || !Instance.slain.Contains(__instance))return true;Instance.wakeBlocks++;return false; } }

    GameObject Effect(string name,float lifetime) {
      if(effects.Count>=160) { if(effects[0])Destroy(effects[0]);effects.RemoveAt(0); }
      var go=new GameObject("JJK_"+name);go.layer=arena?31:0;effects.Add(go);Destroy(go,lifetime);return go;
    }
    GameObject Orb(Vector3 p,Color c,float size,float lifetime) {
      var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name="JJK_orb";go.layer=arena?31:0;Destroy(go.GetComponent<Collider>());
      go.transform.position=p;go.transform.localScale=Vector3.one*size;go.GetComponent<Renderer>().sharedMaterial=material;go.GetComponent<Renderer>().material.color=c;
      var fx=go.AddComponent<FadeFx>();fx.Lifetime=lifetime;fx.Grow=false;
      effects.Add(go);Destroy(go,lifetime);return go;
    }
    void Pulse(Vector3 p,Color c,float radius,float lifetime) {
      var ball=Orb(p,c,radius*.4f,lifetime);ball.GetComponent<FadeFx>().Grow=true;
      Ring(p,radius,c,lifetime);
    }
    void Beam(Vector3 a,Vector3 b,Color c,float width,float lifetime) {
      var go=Effect("beam",lifetime);var l=go.AddComponent<LineRenderer>();l.sharedMaterial=material;l.positionCount=2;l.SetPosition(0,a);l.SetPosition(1,b);l.startWidth=l.endWidth=width;l.startColor=l.endColor=c;
      go.AddComponent<FadeFx>().Lifetime=lifetime;
    }
    void Ring(Vector3 p,float radius,Color c,float lifetime) {
      var go=Effect("ring",lifetime);var l=go.AddComponent<LineRenderer>();l.sharedMaterial=material;l.positionCount=97;l.startWidth=l.endWidth=.09f;l.startColor=l.endColor=c;
      for(int i=0;i<=96;i++){float a=i*Mathf.PI*2/96;l.SetPosition(i,p+new Vector3(Mathf.Cos(a)*radius,-.6f,Mathf.Sin(a)*radius));}
      go.AddComponent<FadeFx>().Lifetime=lifetime;
    }
    void Slash(Vector3 p,Color color,float length) {
      sukunaFx.Slash(Effect("video_slash",.8f),p,view?view.transform.rotation:Quaternion.identity,length*2);
    }
    void Shrine(Vector3 p) {
      var root=Effect("open_shrine",10);Vector3 floor=p-Vector3.up*1.6f;
      foreach(var h in Physics.RaycastAll(p+Vector3.up*6,Vector3.down,30,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(x=>x.distance))if(!IgnoredCollider(h.collider) && !NpcForCollider(h.collider) && !h.collider.GetComponent<Dummy>()){floor=h.point;break;}
      root.transform.position=floor;root.transform.rotation=Quaternion.LookRotation(domainForward);sukunaFx.Shrine(root);
      Ring(p,4,red,10);
    }
    void Flash(Color c,float seconds) { flashColor=c;flashUntil=Time.unscaledTime+seconds; }
    void Sound(int skill) {
      if(skillAudio!=null){skillAudio.Cast(rules.Character,skill);return;}
      if(rules.Character==1){if(skill<2)PlaySound(sukunaFx.Cut,.75f,skill==1?.8f:1.1f);return;}
      if(arena && selfTest)return;
      const int count=11025;float[] data=new float[count];
      for(int i=0;i<count;i++){float t=i/(float)count;data[i]=Mathf.Sin(t*(160+skill*80)*(1-t*.7f)*Mathf.PI*2)*.16f*(1-t)*(1-t);}
      var clip=AudioClip.Create("JJK_cast",count,1,22050,false);clip.SetData(data,0);
      var go=Effect("audio",1);var src=go.AddComponent<AudioSource>();src.volume=.6f;src.clip=clip;src.Play();Destroy(clip,1);
    }
    void PlaySound(AudioClip clip,float volume,float pitch) {
      if(skillAudio!=null){skillAudio.PlayEffect(clip,volume,pitch);return;}
      var go=Effect("sukuna_audio",clip.length/Mathf.Max(.1f,pitch)+.1f);var src=go.AddComponent<AudioSource>();src.clip=clip;src.volume=volume;src.pitch=pitch;src.spatialBlend=0;src.Play();
    }
    void Tell(string text) { notice=text;noticeUntil=Time.unscaledTime+4; }
    void LateUpdate() {
      try { SyncCharacters();RefreshHud(); } catch(Exception ex){if(frameErrors++<3)Logger.LogError(ex);}
    }
    void RefreshHud() {
      if(!skillHud)return;
      hudState.Visible=hud;hudState.Character=rules.Character;hudState.Enabled=active.Value;hudState.Ready=CombatReady() && !TextEntryActive();hudState.Energy=rules.Energy;hudState.Accent=rules.Character==1?red:blue;
      bool spawned=PlayerBound;
      hudState.Status=!active.Value?"已停用":arena?"训练场":spawned && player.isDead?"已倒下":spawned && player.pauseActive?"已暂停":SoloReady()?"单人就绪":"等待玩家";
      if(Time.time<domainUntil)hudState.Status+=" · 领域中";if(infinity)hudState.Status+=" · 无下限";
      hudState.Notice=Time.unscaledTime<noticeUntil?notice:"";
      if(hudState.Notice.StartsWith("开放式领域"))hudState.Notice="领域展开 · 伏魔御厨子";
      if(hudState.Notice.StartsWith("训练结界"))hudState.Notice="训练场 · WASD移动 · Esc鼠标";
      hudState.Feedback=Time.unscaledTime<hitFeedbackUntil?hitFeedback:"";
      if(hudState.Feedback.StartsWith("未伤及"))hudState.Feedback=Time.time<domainUntil?"":"未命中 · 请确认距离与遮挡";
      hudState.Aim=aimHint;hudState.Crosshair=active.Value && SoloReady() && view;
      hudState.Footer=KeyName(4)+" 切换   ·   "+KeyName(5)+" 特殊   ·   "+KeyName(7)+" 收起";
      hudState.Flash=Time.unscaledTime<flashUntil?new Color(flashColor.r,flashColor.g,flashColor.b,.06f):Color.clear;
      for(int i=0;i<4;i++){hudState.Keys[i]=KeyName(i);hudState.Names[i]=names[rules.Character,i];hudState.Remaining[i]=Mathf.Max(0,rules.ReadyAt[rules.Character,i]-Time.time);hudState.Cooldown[i]=Rules.Cooldown[rules.Character,i];hudState.Cost[i]=Rules.Cost[rules.Character,i];}
      skillHud.Apply(hudState,arena?31:0);
    }
    void RestoreNativeKeys() {
      foreach(var entry in reservedNative)entry.enabled=true;reservedNative.Clear();nativeOwner=null;nativeScanAt=0;
    }
    int SyncNativeKeys(Rewired.Player owner,KeyCode[] codes,bool force=false) {
      if(owner!=nativeOwner || !nativeCodes.SequenceEqual(codes)){RestoreNativeKeys();nativeCodes=codes.ToArray();nativeOwner=owner;}
      if(owner==null)return 0;if(!force && Time.unscaledTime<nativeScanAt)return reservedNative.Count;nativeScanAt=Time.unscaledTime+1;
      foreach(var map in owner.controllers.maps.GetAllMaps(Rewired.ControllerType.Keyboard))if(map.enabled)foreach(var entry in map.ElementMaps)
        if(entry.enabled && codes.Contains(entry.keyCode)) {entry.enabled=false;reservedNative.Add(entry);Logger.LogInfo("JJK_NATIVE_KEY_RESERVED key="+entry.keyCode+" action="+entry.actionDescriptiveName);}
      return reservedNative.Count;
    }
    void OnGUI() {
      if(!hud){if(Cursor.visible && GUI.Button(new Rect(20,20,170,30),"显示术式面板 / "+KeyName(7)))hud=true;return;}
      if(selfTest || !((!arena && !player) || Cursor.visible))return;
      if(label==null)label=new GUIStyle(GUI.skin.button){font=font,fontSize=13};
      var previous=GUI.matrix;float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);float x=Screen.width/scale-270;
      GUI.color=new Color(.75f,.8f,.92f);
      if(GUI.Button(new Rect(x,20,250,32),arena?"返回主菜单 / "+KeyName(9):"进入训练场 / "+KeyName(9),label))Dispatch(9,"button");
      for(int i=0;i<4;i++)if(GUI.Button(new Rect(x,60+i*31,250,27),KeyName(i)+"    "+names[rules.Character,i],label))Dispatch(i,"button");
      if(GUI.Button(new Rect(x,190,120,28),"切换角色 / "+KeyName(4),label))Dispatch(4,"button");
      if(GUI.Button(new Rect(x+130,190,120,28),"特殊 / "+KeyName(5),label))Dispatch(5,"button");
      if(GUI.Button(new Rect(x,226,250,28),active.Value?"停用模组 / "+KeyName(8):"开启模组 / "+KeyName(8),label))Dispatch(8,"button");
      GUI.color=Color.white;GUI.matrix=previous;
    }

    void SpawnDummy(Vector3 p) {
      dummies.RemoveAll(x=>!x);if(dummies.Count>=12){Tell("训练靶上限 12");return;}
      RaycastHit hit;if(Physics.Raycast(p+Vector3.up*6,Vector3.down,out hit,20,~0,QueryTriggerInteraction.Ignore))p=hit.point+Vector3.up;
      var go=GameObject.CreatePrimitive(PrimitiveType.Capsule);go.name="JJK_training_dummy";go.layer=arena?31:0;go.transform.position=p;
      go.GetComponent<Renderer>().sharedMaterial=solidMaterial;go.GetComponent<Renderer>().material.color=red;
      var d=go.AddComponent<Dummy>();d.View=view;d.CaptionFont=font;dummies.Add(d);Tell("训练靶 HP 300 · 受击后自动重生");
    }
    void EnterArena() {
      if(arena)return;
      oldTimeScale=Time.timeScale;oldLock=Cursor.lockState;oldCursor=Cursor.visible;
      hiddenCanvases=Object.FindObjectsOfType<Canvas>().Where(c=>!skillHud || c!=skillHud.Canvas).ToArray();canvasStates=hiddenCanvases.Select(c=>c.enabled).ToArray();
      foreach(var c in hiddenCanvases)c.enabled=false;
      hiddenEventSystems=Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>();eventSystemStates=hiddenEventSystems.Select(e=>e.enabled).ToArray();
      foreach(var e in hiddenEventSystems)e.enabled=false;
      arenaScene=SceneManager.CreateScene("JJK_LocalPractice");arenaRoot=new GameObject("JJK_practice_root");SceneManager.MoveGameObjectToScene(arenaRoot,arenaScene);
      var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="JJK_arena_floor";floor.layer=31;floor.transform.SetParent(arenaRoot.transform);floor.transform.position=new Vector3(0,-1001,0);floor.transform.localScale=new Vector3(90,1,90);floor.GetComponent<Renderer>().sharedMaterial=solidMaterial;floor.GetComponent<Renderer>().sortingOrder=-100;
      var actor=new GameObject("JJK_practice_player");actor.transform.SetParent(arenaRoot.transform);actor.transform.position=new Vector3(0,-999,0);
      arenaController=actor.AddComponent<CharacterController>();arenaController.height=1.8f;arenaController.radius=.35f;arenaController.center=new Vector3(0,.9f,0);
      var camObj=new GameObject("JJK_practice_camera");camObj.transform.SetParent(actor.transform);camObj.transform.localPosition=new Vector3(0,1.6f,0);view=camObj.AddComponent<Camera>();view.depth=100;view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=new Color(.018f,.022f,.04f);view.cullingMask=1<<31;view.farClipPlane=130;
      Time.timeScale=1;arena=true;look=Vector2.zero;rules.Energy=Rules.MaxEnergy;Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;
      for(int i=0;i<6;i++)SpawnDummy(new Vector3((i%3-1)*5,-999.4f,12+i/3*8));
      Tell("训练结界："+KeyName(9)+" 返回；Esc 显示鼠标，可点击技能按钮。");Logger.LogInfo("JJK_ARENA_ENTER");
    }
    void MoveArena() {
      if(Input.GetKeyDown(KeyCode.Escape)){bool locked=Cursor.lockState==CursorLockMode.Locked;Cursor.lockState=locked?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=locked;}
      if(Cursor.lockState!=CursorLockMode.Locked)return;
      look.x+=Input.GetAxisRaw("Mouse X")*2;look.y=Mathf.Clamp(look.y-Input.GetAxisRaw("Mouse Y")*2,-85,85);
      arenaController.transform.rotation=Quaternion.Euler(0,look.x,0);view.transform.localRotation=Quaternion.Euler(look.y,0,0);
      var v=arenaController.transform.right*(Input.GetKey(KeyCode.D)?1:Input.GetKey(KeyCode.A)?-1:0)+arenaController.transform.forward*(Input.GetKey(KeyCode.W)?1:Input.GetKey(KeyCode.S)?-1:0);
      if(arenaController.isGrounded){verticalSpeed=-2;if(Input.GetKeyDown(KeyCode.Space))verticalSpeed=6;}else verticalSpeed-=18*Time.deltaTime;
      arenaController.Move((Vector3.ClampMagnitude(v,1)*8+Vector3.up*verticalSpeed)*Time.deltaTime);
      if(arenaController.transform.position.y < -1010){arenaController.enabled=false;arenaController.transform.position=new Vector3(0,-999,0);arenaController.enabled=true;verticalSpeed=0;}
    }
    void LeaveArena() {
      if(!arena)return;ClearCombat();
      foreach(var d in dummies)if(d)Destroy(d.gameObject);dummies.Clear();
      Destroy(arenaRoot);SceneManager.UnloadSceneAsync(arenaScene);
      for(int i=0;i<hiddenCanvases.Length;i++)if(hiddenCanvases[i])hiddenCanvases[i].enabled=canvasStates[i];
      for(int i=0;i<hiddenEventSystems.Length;i++)if(hiddenEventSystems[i])hiddenEventSystems[i].enabled=eventSystemStates[i];
      Time.timeScale=oldTimeScale;Cursor.lockState=oldLock;Cursor.visible=oldCursor;arena=false;view=null;scanAt=0;Logger.LogInfo("JJK_ARENA_EXIT");
    }
    IEnumerator SelfTest() {
      Logger.LogInfo("JJK_SELFTEST_BEGIN");
      bool background=Application.runInBackground;Application.runInBackground=true;
      int oldWidth=Screen.width,oldHeight=Screen.height;bool oldFullscreen=Screen.fullScreen;Screen.SetResolution(1280,720,false);
      yield return new WaitForSecondsRealtime(12);
      bool inputOk=true;
      for(int digit=0;digit<10;digit++) {
        int action=digit==0?9:digit-1;
        int expected=digit>=1 && digit<=4?-1:action;
        inputOk &= ActionForKey((KeyCode)((int)KeyCode.Keypad0+digit))==expected && ActionForKey((KeyCode)((int)KeyCode.Alpha0+digit))==expected;
      }
      for(int slot=0;slot<4;slot++)inputOk &= ActionForKey(SkillKeys[slot])==slot;
      bool nativeKeysOk=true;int reservedCount=0;
      if(Rewired.ReInput.isReady) {
        var inputOwner=Rewired.ReInput.players.GetPlayer(0);
        var before=inputOwner.controllers.maps.GetAllMaps(Rewired.ControllerType.Keyboard).Where(m=>m.enabled).SelectMany(m=>m.ElementMaps).Distinct().ToDictionary(e=>e,e=>e.enabled);
        reservedCount=SyncNativeKeys(inputOwner,SkillKeys,true);
        nativeKeysOk=before.All(p=>p.Key.enabled==(SkillKeys.Contains(p.Key.keyCode)?false:p.Value));
        RestoreNativeKeys();nativeKeysOk &= before.All(p=>p.Key.enabled==p.Value);
      }
      inputOk &= nativeKeysOk;Logger.LogInfo("JJK_BINDING_TEST F="+ActionForKey(KeyCode.F)+" G="+ActionForKey(KeyCode.G)+" V="+ActionForKey(KeyCode.V)+" X="+ActionForKey(KeyCode.X)+" oldDigitsRejected="+(ActionForKey(KeyCode.Keypad1)==-1)+" nativeReserved="+reservedCount+" nativeRestored="+nativeKeysOk);
      inputOk &= HandleKey(KeyCode.Alpha0,"selftest") && arena;
      inputOk &= HandleKey(KeyCode.Alpha5,"selftest") && rules.Character==1;
      inputOk &= HandleKey(KeyCode.Keypad5,"selftest") && rules.Character==0;
      // Test the real physics query and hitbox owner contract, not dummy damage.
      var probe=GameObject.CreatePrimitive(PrimitiveType.Cube);probe.name="CollisionHelperProbe";probe.layer=2;probe.transform.position=(view.transform.position+dummies[0].transform.position)*.5f;probe.transform.localScale=Vector3.one*2;
      Physics.SyncTransforms();var ray=dummies[0].transform.position-view.transform.position;
      bool oldSeesHelper=Physics.RaycastAll(view.transform.position,ray.normalized,ray.magnitude,~0,QueryTriggerInteraction.Ignore).Any(x=>x.collider==probe.GetComponent<Collider>());
      bool newIgnoresHelper=Visible(new Target{D=dummies[0]},view.transform.position);
      var owner=new GameObject("HitboxOwnerProbe");owner.SetActive(false);var npc=owner.AddComponent<NPC>();
      var bone=new GameObject("DetachedHitboxProbe");bone.SetActive(false);var body=bone.AddComponent<NPC_Hitbox>();body.assignedNPC=npc;var bodyCollider=bone.AddComponent<BoxCollider>();
      bool ownerOk=NpcForCollider(bodyCollider)==npc;
      int blocks=wakeBlocks;slain.Add(npc);AccessTools.Method(typeof(NPC),"WakeUp").Invoke(npc,null);
      bool wakeGuardOk=wakeBlocks==blocks+1;slain.Remove(npc);
      var shieldProbe=new Rules();bool shieldOk=shieldProbe.Energy==5000 && shieldProbe.AbsorbDamage(120)==0 && shieldProbe.Energy==4880;
      shieldProbe.Energy=25;shieldOk &= shieldProbe.AbsorbDamage(40)==15 && shieldProbe.Energy==0 && shieldProbe.AbsorbDamage(10)==10;
      Logger.LogInfo("JJK_NEW_COMBAT_TEST energy5000=True shieldFullPartialEmpty="+shieldOk+" registeredWakeGuard="+wakeGuardOk);
      bool damageRoutingOk=oldSeesHelper && newIgnoresHelper && ownerOk;
      Logger.LogInfo("JJK_DAMAGE_REGRESSION oldSeesIgnoreLayer="+oldSeesHelper+" newIgnoresHelper="+newIgnoresHelper+" detachedHitboxOwner="+ownerOk);
      Destroy(probe);Destroy(bone);Destroy(owner);yield return null;
      Vector3 originalPosition=arenaController.transform.position;
      arenaController.Move(Vector3.right*2);bool movementOk=arenaController.transform.position.x-originalPosition.x>1.5f;
      arenaController.Move(Vector3.left*2);
      bool vfxOk=sukunaFx.SlashFrames.Length==23 && sukunaFx.SlashFrames.All(s=>s) && sukunaFx.Cut.samples>10000;
      int casts=0,skillsPassed=0;bool audioOk=skillAudio!=null && skillAudio.Loaded==17 && skillAudio.Missing==0;
      for(int character=0;character<2;character++)for(int slot=0;slot<4;slot++) {
        ClearCombat();
        Vector3[] positions={new Vector3(0,-999.4f,12),new Vector3(-5,-999.4f,14),new Vector3(5,-999.4f,14),new Vector3(-8,-999.4f,22),new Vector3(0,-999.4f,24),new Vector3(8,-999.4f,22)};
        for(int i=0;i<dummies.Count;i++)if(dummies[i]){dummies[i].ResetDummy();dummies[i].transform.position=positions[i];}
        rules.Character=character;rules.Energy=100;rules.ReadyAt[character,slot]=0;
        dummies[0].transform.position=new Vector3(0,-999.4f,character==1 && slot==1?3:12);
        view.transform.rotation=Quaternion.LookRotation(dummies[0].transform.position-view.transform.position);
        Physics.SyncTransforms();
        int oldSlashes=sukunaFx.Slashes,oldShots=fireShots,oldImpacts=fireImpacts,oldBursts=domainBursts;
        int voiceBefore=skillAudio.VoiceStarts[character,slot];
        if(HandleKey(keys[slot].Value,"selftest"))casts++;
        yield return null;bool heardSource=skillAudio.VoiceStarts[character,slot]==voiceBefore+1 && skillAudio.VoicePlaying;
        audioOk &= heardSource;Logger.LogInfo("JJK_AUDIO_CAST character="+character+" slot="+slot+" voiceSourcePlaying="+heardSource+" clip="+skillAudio.VoiceId);
        if(character==1 && slot==0) {
          yield return new WaitForSecondsRealtime(.08f);yield return StartCoroutine(CaptureVfx("slash"));
          yield return new WaitForSecondsRealtime(1.42f);vfxOk &= sukunaFx.Slashes>oldSlashes;
        } else if(character==1 && slot==2) {
          yield return new WaitForSecondsRealtime(.55f);
          bool charge=fireShots==oldShots && Object.FindObjectsOfType<ParticleSystem>().Any(p=>p.name.StartsWith("JJK_") && p.particleCount>0);
          yield return StartCoroutine(CaptureVfx("fuga-charge"));yield return new WaitForSecondsRealtime(.72f);
          yield return StartCoroutine(CaptureVfx("fuga-flight"));yield return new WaitForSecondsRealtime(.33f);
          yield return StartCoroutine(CaptureVfx("fuga-impact"));yield return new WaitForSecondsRealtime(.7f);
          bool arrow=fireShots==oldShots+1 && fireImpacts==oldImpacts+1;vfxOk &= charge && arrow;
          Logger.LogInfo("JJK_FUGA_TEST windupParticles="+charge+" releaseAndImpact="+arrow);
        } else if(character==1 && slot==3) {
          yield return new WaitForSecondsRealtime(.65f);yield return StartCoroutine(CaptureVfx("open-domain"));yield return new WaitForSecondsRealtime(.85f);
          bool density=domainBursts-oldBursts>=8 && sukunaFx.Slashes-oldSlashes>=64;
          bool open=!effects.Where(e=>e && e.name=="JJK_open_shrine").SelectMany(e=>e.GetComponentsInChildren<Collider>()).Any();
          vfxOk &= density && open;Logger.LogInfo("JJK_DOMAIN_TEST bursts="+(domainBursts-oldBursts)+" slashes="+(sukunaFx.Slashes-oldSlashes)+" dense="+density+" open="+open);
        } else if(character==0) {
          yield return new WaitForSecondsRealtime(slot==2?.45f:slot==1?.25f:.4f);yield return StartCoroutine(CaptureVfx("gojo-cast-"+slot+"-early"));
          yield return new WaitForSecondsRealtime(slot==2?.8f:.6f);yield return StartCoroutine(CaptureVfx("gojo-cast-"+slot+"-late"));
          yield return new WaitForSecondsRealtime(.5f);
          vfxOk &= gojoFx.Spawned[slot==0?0:slot==1?2:slot==2?4:6]>0;
        } else yield return new WaitForSecondsRealtime(1.5f);
        bool passed=character==0 && slot==3 ? dummies[0].StunnedUntil>Time.time : dummies[0].HP<300;
        if(passed)skillsPassed++;
        Logger.LogInfo("JJK_SKILL_TEST character="+character+" slot="+slot+" hp="+dummies[0].HP+" passed="+passed);
      }
      ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Paths.GameRootPath,"JJK_PREVIEW.png"));yield return new WaitForEndOfFrame();
      ClearCombat();rules.Character=0;rules.Energy=100;Special();bool infinityOk=infinity;Special();bool offOk=!infinity;
      rules.Character=1;rules.Energy=100;Special();bool healOk=rules.Energy==70;
      audioOk &= skillAudio.VoiceStarts[0,4]>0 && skillAudio.VoiceStarts[1,4]>0;
      skillAudio.Tick(0,0);skillAudio.Say(0,4);bool muteOk=skillAudio.VoiceLevel==0;skillAudio.Stop();bool silenceOk=skillAudio.AllStopped;skillAudio.Tick(voiceVolume.Value,effectVolume.Value);
      skillAudio.Say(0,0);skillAudio.Say(1,0);bool exclusive=skillAudio.VoiceId=="JJK_v10";ClearCombat();audioOk &= muteOk && silenceOk && exclusive && skillAudio.AllStopped;
      Logger.LogInfo("JJK_AUDIO_TEST loaded="+skillAudio.Loaded+" missing="+skillAudio.Missing+" active-casts=8 specials=2 mute="+muteOk+" exclusive="+exclusive+" stop="+silenceOk+" mixer="+skillAudio.MixerName+" passed="+audioOk);vfxOk &= audioOk;
      foreach(var d in dummies)if(d){d.ResetDummy();}
      dummies[0].transform.position=new Vector3(0,-999.4f,12);dummies[0].HP=1000;Physics.SyncTransforms();
      int hits=Area(new Vector3(0,-999.4f,12),5,40,red,false);
      bool damageOk=dummies[0].HP==600 && Rules.SkillDamage(40)==400;
      Logger.LogInfo("JJK_EXACT_DAMAGE_TEST before=1000 base=40 after="+dummies[0].HP+" passed="+damageOk);
      rules.Energy=100;rules.ReadyAt[1,2]=0;int shotsBeforeCancel=fireShots;Cast(2);yield return new WaitForSecondsRealtime(.2f);ClearCombat();yield return new WaitForSecondsRealtime(1.25f);
      bool cancelOk=fireShots==shotsBeforeCancel;vfxOk &= cancelOk && shieldOk && wakeGuardOk;
      rules.Character=0;
      for(int slot=1;slot<=2;slot++) {
        rules.Energy=5000;rules.ReadyAt[0,slot]=0;int before=gojoReleases;Cast(slot);yield return new WaitForSecondsRealtime(.15f);ClearCombat();yield return new WaitForSecondsRealtime(1.1f);
        bool stopped=gojoReleases==before;vfxOk &= stopped;Logger.LogInfo("JJK_GOJO_CANCEL_TEST slot="+slot+" noRelease="+stopped);
      }
      bool gojoOk=gojoFx.TextureWidth>=512 && gojoFx.Spawned.All(n=>n>0);vfxOk &= gojoOk;
      Logger.LogInfo("JJK_GOJO_TEST forms="+string.Join(",",gojoFx.Spawned.Select(n=>n.ToString()).ToArray())+" generatedTexture="+gojoFx.TextureWidth+" passed="+gojoOk);
      Logger.LogInfo("JJK_VFX_TEST frames="+sukunaFx.SlashFrames.Length+" slashes="+sukunaFx.Slashes+" flames="+sukunaFx.Flames+" cancelled="+cancelOk+" passed="+vfxOk);
      yield return StartCoroutine(ReviewGojo());yield return StartCoroutine(ReviewShrine());
      bool modelOk=sukunaFx.Model.Triangles>20000 && sukunaFx.Model.Triangles<160000 && sukunaFx.Model.Parts==8;
      vfxOk &= modelOk;Logger.LogInfo("JJK_SHRINE_TEST triangles="+sukunaFx.Model.Triangles+" parts="+sukunaFx.Model.Parts+" width="+sukunaFx.Model.Bounds.size.x+" height="+sukunaFx.Model.Bounds.size.y+" model="+modelOk);
      yield return StartCoroutine(ReviewFuga());
      bool fireModelOk=sukunaFx.FireModel.BowTriangles>3000 && sukunaFx.FireModel.ArrowTriangles>1000 && sukunaFx.FireModel.BowBounds.size.z>.15f;
      vfxOk &= fireModelOk;Logger.LogInfo("JJK_FUGA_MODEL_TEST bowTriangles="+sukunaFx.FireModel.BowTriangles+" arrowTriangles="+sukunaFx.FireModel.ArrowTriangles+" burstTriangles="+sukunaFx.FireModel.BurstTriangles+" volumetric="+fireModelOk);
      yield return StartCoroutine(ReviewHud());
      bool hudOk=skillHud.LabelsMatch(new[]{"F","G","V","X"});vfxOk &= hudOk;
      Logger.LogInfo("JJK_HUD_TEST nativeCanvas=True labels="+hudOk+" panel=272x238 topBanner=False resolutions=1280x720,1920x1080,1024x768");
      int errors=frameErrors;var oldRoot=arenaRoot;var oldEffects=effects.ToArray();var oldDummies=dummies.ToArray();var oldScene=arenaScene;LeaveArena();
      float cleanupDeadline=Time.realtimeSinceStartup+5;
      do { yield return null; } while(oldScene.isLoaded && Time.realtimeSinceStartup<cleanupDeadline);
      bool cleanupOk=!arena && !oldRoot && !oldScene.isLoaded && oldEffects.All(x=>!x) && oldDummies.All(x=>!x) && frozen.Count==0 && effects.Count==0 && hiddenCanvases.Select((c,i)=>!c || c.enabled==canvasStates[i]).All(x=>x) && hiddenEventSystems.Select((e,i)=>!e || e.enabled==eventSystemStates[i]).All(x=>x) && Cursor.lockState==oldLock && Cursor.visible==oldCursor;
      string result="JJK_SELFTEST "+(casts==8 && skillsPassed==8 && infinityOk && offOk && healOk && hits>0 && damageOk && movementOk && cleanupOk && inputOk && damageRoutingOk && vfxOk && errors==0?"PASS":"FAIL")+" casts="+casts+" skills="+skillsPassed+" input="+inputOk+" damageRouting="+damageRoutingOk+" infinity="+infinityOk+" off="+offOk+" heal="+healOk+" hits="+hits+" damage10x="+damageOk+" vfx="+vfxOk+" audio="+audioOk+" movement="+movementOk+" cleanup="+cleanupOk+" errors="+errors+" run="+runId;
      Logger.LogInfo(result);System.IO.File.WriteAllText(System.IO.Path.Combine(Paths.GameRootPath,"JJK_SELFTEST.txt"),result);
      Screen.SetResolution(oldWidth,oldHeight,oldFullscreen);Application.runInBackground=background;selfTest=false;rules.Character=0;rules.Energy=Rules.MaxEnergy;specialReady=0;Array.Clear(rules.ReadyAt,0,rules.ReadyAt.Length);
    }
    IEnumerator CaptureVfx(string name) {
      yield return new WaitForEndOfFrame();
      var target=RenderTexture.GetTemporary(1280,720,24);var oldTarget=view.targetTexture;var oldActive=RenderTexture.active;Texture2D capture=null;
      try {
        view.targetTexture=target;view.Render();RenderTexture.active=target;capture=new Texture2D(1280,720,TextureFormat.RGB24,false);capture.ReadPixels(new Rect(0,0,1280,720),0,0);capture.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(Paths.GameRootPath,"JJK_"+name+".png"),ImageConversion.EncodeToPNG(capture));
      } finally {view.targetTexture=oldTarget;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(target);if(capture)Destroy(capture);}
    }
    IEnumerator ReviewShrine() {
      ClearCombat();foreach(var d in dummies)if(d)d.gameObject.SetActive(false);
      var root=Effect("shrine_review",45);root.transform.position=new Vector3(0,-1000,14);sukunaFx.Shrine(root);
      float oldFov=view.fieldOfView;view.fieldOfView=42;
      var offsets=new[]{new Vector3(-12.5f,6.3f,-15),new Vector3(0,4.5f,-17),new Vector3(17,5,0),new Vector3(0,5,17)};
      string[] names={"shrine-three-quarter","shrine-front","shrine-side","shrine-back"};
      for(int i=0;i<4;i++) {
        view.transform.position=root.transform.position+offsets[i];view.transform.rotation=Quaternion.LookRotation(root.transform.position+Vector3.up*3.6f-view.transform.position);
        yield return null;yield return StartCoroutine(CaptureVfx(names[i]));
      }
      sukunaFx.Model.Export(System.IO.Path.Combine(Paths.GameRootPath,"ShrineModel"));
      view.fieldOfView=oldFov;
      ClearCombat();foreach(var d in dummies)if(d)d.gameObject.SetActive(true);
    }
    IEnumerator ReviewFuga() {
      ClearCombat();foreach(var d in dummies)if(d)d.gameObject.SetActive(false);
      var basePoint=new Vector3(0,-998.6f,12);var root=Effect("fuga_model_review",30);root.transform.position=basePoint;sukunaFx.Bow(root);sukunaFx.FireModel.SetCharge(root,1);
      float oldFov=view.fieldOfView;view.fieldOfView=44;
      var offsets=new[]{new Vector3(2.8f,.5f,-3.3f),new Vector3(3.8f,.2f,.45f),new Vector3(0,.05f,-3.3f)};
      string[] names={"fuga-model-three-quarter","fuga-model-side","fuga-model-front"};
      for(int i=0;i<3;i++) {
        view.transform.position=basePoint+offsets[i];view.transform.rotation=Quaternion.LookRotation(basePoint+new Vector3(0,0,.4f)-view.transform.position);
        yield return new WaitForSecondsRealtime(.15f);yield return StartCoroutine(CaptureVfx(names[i]));
      }
      root.SetActive(false);var arrow=Effect("fuga_arrow_review",10);arrow.transform.position=basePoint;sukunaFx.Arrow(arrow);
      view.transform.position=basePoint+new Vector3(-3.2f,.3f,.5f);view.transform.rotation=Quaternion.LookRotation(basePoint+Vector3.forward*.5f-view.transform.position);
      yield return new WaitForSecondsRealtime(.15f);yield return StartCoroutine(CaptureVfx("fuga-model-arrow"));
      sukunaFx.FireModel.Export(System.IO.Path.Combine(Paths.GameRootPath,"FugaModel"));view.fieldOfView=oldFov;ClearCombat();foreach(var d in dummies)if(d)d.gameObject.SetActive(true);
    }
    IEnumerator ReviewHud() {
      ClearCombat();view.transform.position=new Vector3(0,-997.4f,0);view.transform.rotation=Quaternion.LookRotation(new Vector3(0,-999.4f,12)-view.transform.position);view.fieldOfView=60;
      int oldCharacter=rules.Character;float oldEnergy=rules.Energy;Color oldSky=view.backgroundColor,oldFloor=solidMaterial.color;
      rules.Character=1;rules.Energy=4943;for(int i=0;i<4;i++)rules.ReadyAt[1,i]=0;rules.ReadyAt[1,3]=Time.time+33;
      noticeUntil=hitFeedbackUntil=flashUntil=0;hud=true;
      yield return null;RefreshHud();yield return StartCoroutine(CaptureHud("hud-sukuna-dark",1280,720));
      view.backgroundColor=new Color(.57f,.68f,.82f);solidMaterial.color=new Color(.66f,.68f,.7f);
      yield return null;RefreshHud();yield return StartCoroutine(CaptureHud("hud-sukuna-day",1920,1080));
      rules.Energy=4943;rules.ReadyAt[1,3]=0;HandleKey(KeyCode.X,"selftest");
      yield return new WaitForSecondsRealtime(.45f);RefreshHud();yield return StartCoroutine(CaptureHud("hud-sukuna-domain",1280,720));ClearCombat();noticeUntil=hitFeedbackUntil=0;
      rules.Character=0;rules.Energy=4260;rules.ReadyAt[0,2]=Time.time+12.4f;
      yield return null;RefreshHud();yield return StartCoroutine(CaptureHud("hud-gojo-4x3",1024,768));
      Dispatch(7,"selftest");RefreshHud();bool hidden=!skillHud.gameObject.activeSelf;Dispatch(7,"selftest");RefreshHud();
      Logger.LogInfo("JJK_HUD_VISIBILITY hidden="+hidden+" restored="+skillHud.gameObject.activeSelf);
      view.backgroundColor=oldSky;solidMaterial.color=oldFloor;rules.Character=oldCharacter;rules.Energy=oldEnergy;RefreshHud();
    }
    IEnumerator CaptureHud(string name,int width,int height) {
      yield return new WaitForEndOfFrame();var canvas=skillHud.Canvas;var scaler=canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
      var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;float oldDistance=canvas.planeDistance,oldScale=canvas.scaleFactor;bool oldScaler=scaler.enabled;
      var oldTarget=view.targetTexture;var oldActive=RenderTexture.active;var target=RenderTexture.GetTemporary(width,height,24);Texture2D pixels=null;
      try {
        view.targetTexture=target;scaler.enabled=false;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=view;canvas.planeDistance=Mathf.Max(1,view.nearClipPlane+.1f);canvas.scaleFactor=Mathf.Min(width/1280f,height/720f);
        Canvas.ForceUpdateCanvases();view.Render();RenderTexture.active=target;pixels=new Texture2D(width,height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(Paths.GameRootPath,"JJK_"+name+".png"),ImageConversion.EncodeToPNG(pixels));
        bool textFits=skillHud.NameLabels.Concat(skillHud.StatusLabels).All(t=>t.preferredWidth<=((RectTransform)t.transform).rect.width+1);
        Logger.LogInfo("JJK_HUD_LAYOUT size="+width+"x"+height+" textFits="+textFits);
      } finally {canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.planeDistance=oldDistance;canvas.scaleFactor=oldScale;scaler.enabled=oldScaler;view.targetTexture=oldTarget;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(target);if(pixels)Destroy(pixels);Canvas.ForceUpdateCanvases();}
    }
    void OnDestroy() { Logger.LogInfo("JJK_ON_DESTROY");SceneManager.activeSceneChanged-=SceneChanged;RestoreCharacters();LeaveArena();ClearCombat();RestoreNativeKeys();if(skillHud)Destroy(skillHud.gameObject);if(harmony!=null)harmony.UnpatchSelf();if(skillAudio!=null)skillAudio.Dispose();if(gojoFx!=null)gojoFx.Dispose();if(sukunaFx!=null)sukunaFx.Dispose();if(material)Destroy(material);if(solidMaterial)Destroy(solidMaterial);if(font)Destroy(font);Instance=null; }
  }
  public sealed class FadeFx : MonoBehaviour {
    public float Lifetime=1;public bool Grow;
    float born;Vector3 scale;Renderer render;LineRenderer line;Color color;
    void Start(){born=Time.time;scale=transform.localScale;line=GetComponent<LineRenderer>();render=GetComponent<Renderer>();color=line?line.startColor:render?render.material.color:Color.white;}
    void Update(){float t=Mathf.Clamp01((Time.time-born)/Lifetime);Color c=color;c.a=(1-t)*.65f;if(line)line.startColor=line.endColor=c;else if(render)render.material.color=c;if(Grow)transform.localScale=scale*(1+t*3);}
    void OnDestroy(){if(render && !line)Destroy(render.material);}
  }
  public sealed class Dummy : MonoBehaviour {
    public float HP=300,StunnedUntil;public Camera View;public Font CaptionFont;float resetAt;TextMesh text;Material ownMaterial;
    void Start(){ownMaterial=GetComponent<Renderer>().material;var go=new GameObject("JJK_dummy_label");go.layer=gameObject.layer;go.transform.SetParent(transform);go.transform.localPosition=Vector3.up*1.4f;text=go.AddComponent<TextMesh>();text.font=CaptionFont;go.GetComponent<Renderer>().sharedMaterial=CaptionFont.material;text.fontSize=48;text.characterSize=.055f;text.anchor=TextAnchor.MiddleCenter;text.color=Color.white;}
    public void Hit(float n){HP=Mathf.Max(0,HP-n);if(HP<=0)resetAt=Time.time+3;}
    public void ResetDummy(){HP=300;resetAt=0;StunnedUntil=0;}
    void Update(){if(HP<=0 && Time.time>=resetAt)ResetDummy();if(text){text.text="CURSE  "+HP.ToString("0")+(Time.time<StunnedUntil?"  FROZEN":"");if(View)text.transform.rotation=Quaternion.LookRotation(text.transform.position-View.transform.position);}if(ownMaterial){Color c=HP<=0?Color.gray:Time.time<StunnedUntil?Color.cyan:new Color(.85f,.1f,.17f);ownMaterial.color=c;ownMaterial.SetColor("_EmissionColor",c*.8f);}}
    void OnDestroy(){if(ownMaterial)Destroy(ownMaterial);}
  }
}
