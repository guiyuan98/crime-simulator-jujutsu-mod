using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CrimeJJK {
 public sealed partial class JujutsuMod {
  readonly List<NetworkBehaviour> environmentTargets=new List<NetworkBehaviour>();
  readonly HashSet<int> breachedEnvironment=new HashSet<int>();float environmentScanAt;
  void ResetEnvironmentCache(){environmentTargets.Clear();breachedEnvironment.Clear();environmentScanAt=0;}
  static bool LiveProp(NetworkBehaviour b){return b && b.gameObject.scene.IsValid() && b.gameObject.scene.isLoaded && b.gameObject.activeInHierarchy && b.Object && b.Object.IsValid && b.HasStateAuthority;}
  static GameObject PropRoot(Component c) {
   var safe=c as Safe;if(safe && safe.mainObj)return safe.mainObj;
   var electronic=c as ElectronicSafe;if(electronic && electronic.mainObj)return electronic.mainObj;
   var d=c as Destroyable;if(d && d.deactivate)return d.deactivate;
   return c.gameObject;
  }
  static Bounds PropBounds(Component c) {
   var root=PropRoot(c);var colliders=root.GetComponentsInChildren<Collider>().Where(x=>x && x.enabled && !x.isTrigger && !ContainedLoot(x,root.transform)).ToArray();
   if(colliders.Length>0){var b=colliders[0].bounds;foreach(var x in colliders)b.Encapsulate(x.bounds);return b;}
   var render=root.GetComponentsInChildren<Renderer>().FirstOrDefault(r=>r);return render?render.bounds:new Bounds(root.transform.position,Vector3.one*.2f);
  }
  static bool ContainedLoot(Component component,Transform barrier) {
   var loot=component.GetComponentInParent<Pickupable>();
   return loot && loot.transform!=barrier && loot.transform.IsChildOf(barrier);
  }
  static bool ContainedActor(Component component,Transform barrier) {
   Component actor=component.GetComponentInParent<NPC>();if(!actor)actor=component.GetComponentInParent<FPP_Player>();
   return actor && actor.transform!=barrier && actor.transform.IsChildOf(barrier);
  }
  static void ClearBarrierGeometry(Component component) {
   if(!component)return;var barrier=component.transform;
   foreach(var animator in component.GetComponentsInChildren<Animator>(true))if(animator && !ContainedLoot(animator,barrier) && !ContainedActor(animator,barrier))animator.enabled=false;
   foreach(var animation in component.GetComponentsInChildren<Animation>(true))if(animation && !ContainedLoot(animation,barrier) && !ContainedActor(animation,barrier)){animation.Stop();animation.enabled=false;}
   foreach(var lod in component.GetComponentsInChildren<LODGroup>(true))if(lod && !ContainedLoot(lod,barrier) && !ContainedActor(lod,barrier))lod.enabled=false;
   foreach(var collider in component.GetComponentsInChildren<Collider>(true))if(collider && !ContainedLoot(collider,barrier) && !ContainedActor(collider,barrier))collider.enabled=false;
   foreach(var render in component.GetComponentsInChildren<Renderer>(true))if(render && !ContainedLoot(render,barrier) && !ContainedActor(render,barrier))render.enabled=false;
   var door=component as Openable;if(door){door.bodyBlocksDoor=false;door.enabled=false;}
  }
  bool PropVisible(Component c,Vector3 origin) {
   var root=PropRoot(c);var p=PropBounds(c).ClosestPoint(origin);var delta=p-origin;if(delta.sqrMagnitude<.04f)return true;
   foreach(var h in Physics.RaycastAll(origin,delta.normalized,delta.magnitude+.03f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) {
    if(IgnoredCollider(h.collider) || NpcForCollider(h.collider) || h.collider.transform.IsChildOf(root.transform) || h.collider.gameObject==root)continue;
    return false;
   }
   return true;
  }
  public int DamageEnvironment(Vector3 point,float radius,float damage) {
   var wm=WorldManager.Instance;var local=wm?wm.localPlayer:null;
   if(!active.Value || arena || !IsLocalActor(local) || local.isDead || damage<=0)return 0;
   if(Time.unscaledTime>=environmentScanAt){
    environmentScanAt=Time.unscaledTime+1;environmentTargets.Clear();
    foreach(var type in new[]{typeof(Safe),typeof(ElectronicSafe),typeof(Padlock),typeof(BlowtorchObject),typeof(Destroyable),typeof(Openable),typeof(Keypad),typeof(TrapWire),typeof(MotionSensor),typeof(SecurityCamera),typeof(WindowShutter),typeof(GarageDoor),typeof(SmokeDetector)})
     environmentTargets.AddRange(Resources.FindObjectsOfTypeAll(type).Cast<NetworkBehaviour>().Where(LiveProp));
   }
   int hits=0;
   foreach(var target in environmentTargets.Where(LiveProp).OrderBy(x=>PropBounds(x).SqrDistance(point)).ToArray()) {
    if(PropBounds(target).SqrDistance(point)>radius*radius || !PropVisible(target,point))continue;
    if(BreachComponent(target,damage))hits++;
   }
   if(hits>0){hitFeedback="术式破坏 · "+hits+" 个门锁 / 物件";hitFeedbackUntil=Time.unscaledTime+3;}
   return hits;
  }
  public bool BreachComponent(Component component,float damage) {
   var target=component as NetworkBehaviour;var wm=WorldManager.Instance;var local=wm?wm.localPlayer:null;
   if(!active.Value || !LiveProp(target) || !IsLocalActor(local) || local.isDead || damage<=0)return false;
   var root=PropRoot(component);int id=root.GetInstanceID();if(breachedEnvironment.Contains(id))return false;
   var safe=component as Safe;var electronic=component as ElectronicSafe;
   var padlock=component as Padlock;var torch=component as BlowtorchObject;var destroyable=component as Destroyable;
   var door=component as Openable;
   if(!door)door=root.GetComponent<Openable>() ?? root.GetComponentInParent<Openable>();
   // Prefer actual destruction components: they own loot, alarm, job and challenge callbacks.
   if(component is Openable || component is SecurityCamera || component is WindowShutter) {
    var alternate=component.GetComponent<Destroyable>();if(alternate && alternate.health>0)return BreachComponent(alternate,damage);
    var s=component.GetComponent<Safe>();if(s)return BreachComponent(s,damage);
    var e=component.GetComponent<ElectronicSafe>();if(e)return BreachComponent(e,damage);
    var t=component.GetComponent<BlowtorchObject>();if(t && !t.destroyed)return BreachComponent(t,damage);
   }
   var keypad=component as Keypad;var wire=component as TrapWire;var motion=component as MotionSensor;var camera=component as SecurityCamera;var shutter=component as WindowShutter;var garage=component as GarageDoor;var smoke=component as SmokeDetector;
   bool security=keypad || wire || motion || camera || shutter || garage || smoke;
   var bounds=PropBounds(component);string kind=component.GetType().Name,rootName=root.name;bool success=false;
   try {
    if(keypad) {
     // A severed controller reaches the native full-unlock endpoint, not the fuse/backup-power minigame.
     if(!keypad.isUnlocked)keypad.RPC_Unlocked(true);success=keypad.isUnlocked;
    } else if(wire) {
     wire.RPC_Disarm();success=!wire.gameObject.activeInHierarchy;
    } else if(motion) {
     if(!motion.isDisabled)motion.RPC_SetDisabled();success=motion.isDisabled;
    } else if(camera) {
     if(!camera.isDisabled)camera.RPC_SetDisabled();success=camera.isDisabled;
    } else if(shutter) {
     if(shutter.isActive){shutter.RPC_AlarmOnDestroy();shutter.RPC_SetDisabled();}success=!shutter.isActive;
    } else if(garage) {
     if(!garage.forcedOpen)garage.RPC_ForceOpen();success=garage.isOpen && garage.forcedOpen;
    } else if(smoke) {
     smoke.RPC_SetDisabled();success=true;
    } else if(destroyable) {
     float old=destroyable.health;if(old<=0)return false;
     var brokenShutter=component.GetComponentInParent<WindowShutter>();
     destroyable.RPC_DealDamage(Mathf.Max(damage,old),local.Object.Id);
     success=!destroyable || destroyable.health<old;
     if(!destroyable || destroyable.health<=0){
      // The native destroyable owns the alarm/job event; its surviving shutter controller must not close again.
      if(brokenShutter){brokenShutter.RPC_SetDisabled();ClearBarrierGeometry(brokenShutter);}
      ClearBarrierGeometry(door);breachedEnvironment.Add(id);if(component)breachedEnvironment.Add(component.gameObject.GetInstanceID());
     }
    } else if(padlock) {
     float old=padlock.health;if(old<=0)return false;padlock.RPC_DealDamage(Mathf.Max(damage,old),local.Object.Id);success=!padlock || padlock.health<old;
     if(!padlock || padlock.health<=0)breachedEnvironment.Add(id);
    } else {
     if(safe){safe.RPC_SetUnlocked();success=true;}
     if(electronic){electronic.RPC_UnlockAndAlarm();success=true;}
     if(torch){torch.RPC_DestroyLock();success=torch.destroyed;}
     if(door) {
      if(door.isLocked)door.RPC_Unlocked(true,false,true);
      if(!door.isOpen)door.RPC_OpenClose(false,false,false);
      success=!door.isLocked && door.isOpen;
      if(success) {
       ClearBarrierGeometry(door);
      }
     }
     if(success){breachedEnvironment.Add(id);BreachDebris(bounds);}
    }
    if(security && success){breachedEnvironment.Add(id);ClearBarrierGeometry(component);BreachDebris(bounds);}
    if(success)Logger.LogInfo("JJK_BREACH kind="+kind+" root="+rootName+" nativeCallback=True unlocked="+(door?!door.isLocked:true)+" open="+(door?door.isOpen:true)+" damage="+damage);
   } catch(Exception ex){Logger.LogWarning("JJK_BREACH_ERROR kind="+kind+" type="+ex.GetType().Name+" message="+ex.Message);return false;}
   return success;
  }
  void BreachDebris(Bounds b) {
   // Local cosmetic debris has no mission/loot component and is excluded from skill raycasts.
   for(int i=0;i<9 && effects.Count<145;i++) {
    var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="JJK_breach_shard";go.layer=2;
    go.transform.position=b.center+Vector3.Scale(UnityEngine.Random.insideUnitSphere,b.extents*.6f);go.transform.localScale=new Vector3(UnityEngine.Random.Range(.04f,.12f),UnityEngine.Random.Range(.08f,.26f),.025f);
    go.transform.rotation=UnityEngine.Random.rotation;go.GetComponent<Renderer>().sharedMaterial=solidMaterial;
    var collider=go.GetComponent<Collider>();collider.enabled=false;var body=go.AddComponent<Rigidbody>();body.mass=.1f;body.velocity=UnityEngine.Random.insideUnitSphere*3+Vector3.up*2;
    effects.Add(go);Destroy(go,2.5f);
   }
  }
 }
}
