using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CrimeJJK {
  public sealed partial class JujutsuMod {
    int wardNativeScope;
    FPP_Player healthActor;
    float healthAnchor;
    bool IsLocalActor(FPP_Player p) {
      var wm=WorldManager.Instance;
      return p && wm && wm.localPlayer==p && p.Object && p.Object.IsValid && p.Runner && p.Runner.IsRunning &&
        Rules.LocalPlayerAllowed(p.Runner.IsSinglePlayer,p.Runner.ActivePlayers.Take(2).Count(),p.HasStateAuthority);
    }
    bool WardContext(FPP_Player p) {
      if(!active.Value || !IsLocalActor(p) || p.isDead || !p.WM || p.WM.summaryActive)return false;
      if(player!=p){player=p;view=p.myCamera;Logger.LogInfo("JJK_WARD_OWNER_REFRESH id="+p.GetInstanceID());}
      if(healthActor!=p){healthActor=p;healthAnchor=p.damage;}
      return true;
    }
    bool InfinityReady {get{return infinity && rules.Character==0 && rules.Energy>0;}}
    bool WardAvailable(FPP_Player p){return wardNativeScope==0 && WardContext(p) && (InfinityReady || rules.Energy>0);}
    void TrackHealth(FPP_Player p){if(IsLocalActor(p)){healthActor=p;healthAnchor=p.damage;}}
    float WardAmount(FPP_Player p,float amount,string route) {
      float before=rules.Energy;float remaining=InfinityReady?0:rules.AbsorbDamage(amount);
      Logger.LogInfo("JJK_WARD route="+route+" input="+amount+" absorbed="+(amount-remaining)+" remaining="+remaining+" energy="+before+"->"+rules.Energy+" infinity="+InfinityReady);
      if(amount>remaining){GojoWardCue();Tell(InfinityReady?"无下限 · 已阻隔命中":"咒力护体 · 抵挡 "+(amount-remaining).ToString("0.0"));Flash(rules.Character==0?blue:red,.12f);}
      return remaining;
    }
    static float BodyFactor(FPP_Player p) {
      if(!p.WM || !p.WM.difficultyManager)return 0;
      return p.WM.difficultyManager.GetCurrentPreset().dmgToPlayerMultiplier*(p.isDodging?.75f:1)*(p.hasArmor?.75f:1);
    }
    static bool WardBody(FPP_Player p,ref float damage,bool resurrecting,bool delay) {
      var m=Instance;
      if(!m || m.wardNativeScope>0 || damage<=0 || float.IsNaN(damage) || float.IsInfinity(damage) || !m.WardContext(p) || resurrecting || delay)return true;
      if(m.InfinityReady){m.WardAmount(p,damage,"body");return false;}
      float factor=BodyFactor(p);if(factor<=0 || float.IsNaN(factor) || float.IsInfinity(factor))return true;
      float amount=damage*factor;if(float.IsInfinity(amount))return true;
      float remaining=m.WardAmount(p,amount,"body");damage=remaining/factor;return remaining>0;
    }
    void ApplyGuardedTase(FPP_Player p) {
      // The native lethal-taser path compares damage+120 before the normal damage method.
      // Resolve that impact exactly once, then use native routines only for the uncovered part.
      bool armor=p.hasArmor;float amount=armor?45*UnityEngine.Random.Range(.75f,1.25f):120*BodyFactor(p);
      if(amount<=0 || float.IsNaN(amount) || float.IsInfinity(amount))amount=120;
      float remaining=WardAmount(p,amount,armor?"tase-armor":"tase-body");
      if(remaining<=0){TrackHealth(p);Logger.LogInfo("JJK_TASE_BLOCKED alive="+!p.isDead+" damage="+p.damage);return;}
      wardNativeScope++;
      try {
        if(armor)p.RPC_DealDamageToArmor(remaining);
        else {
          float factor=BodyFactor(p);if(factor<=0)factor=1;
          p.DealDamageToPlayer(remaining/factor);
          if(p.damage>=p.maxDamage)p.PlayerDeath(true);
          else {
            p.DrainStamina(p.maxStamina,true);
            var inv=p.inventory;
            if(inv && inv.currentItemIndex>=0 && inv.currentItemIndex<inv.itemSlots.Count && inv.itemSlots[inv.currentItemIndex].assignedItem && p.CheckIfCanInteract())inv.DropItem(3,false);
          }
        }
      } finally {wardNativeScope--;TrackHealth(p);}
    }
    [HarmonyPatch(typeof(FPP_Player),"RPC_Tazed")]
    static class TaseWardPatch {
      static bool Prefix(FPP_Player __instance,ref bool ___InvokeRpc) {
        var m=Instance;if(!m || !m.WardAvailable(__instance))return true;
        ___InvokeRpc=false;m.ApplyGuardedTase(__instance);return false;
      }
    }
    [HarmonyPatch(typeof(FPP_Player),"PlayerDeath")]
    static class TaseDeathWardPatch {
      static bool Prefix(FPP_Player __instance,bool tazed) {
        var m=Instance;if(!m || !tazed || !m.WardAvailable(__instance))return true;
        m.ApplyGuardedTase(__instance);return false;
      }
    }
    [HarmonyPatch(typeof(FPP_Player),"RPC_DealDamageToArmor")]
    static class ArmorWardPatch {
      static bool Prefix(FPP_Player __instance,ref float dmg,ref bool ___InvokeRpc) {
        var m=Instance;if(!m || dmg<=0 || float.IsNaN(dmg) || float.IsInfinity(dmg) || !m.WardAvailable(__instance))return true;
        dmg=m.WardAmount(__instance,dmg,"armor");if(dmg>0)return true;
        ___InvokeRpc=false;return false;
      }
    }
    [HarmonyPatch(typeof(FPP_Player),"DealDamageToPlayer")]
    static class HealthAnchorPatch {static void Postfix(FPP_Player __instance){if(Instance)Instance.TrackHealth(__instance);}}
    [HarmonyPatch(typeof(FPP_Player),"PlayerHealth")]
    static class HealthFieldWardPatch {
      static void Prefix(FPP_Player __instance) {
        var m=Instance;if(!m || !m.WardAvailable(__instance))return;
        float delta=__instance.damage-m.healthAnchor;
        if(delta>0 && !float.IsNaN(delta) && !float.IsInfinity(delta)){
          __instance.damage=m.healthAnchor+m.WardAmount(__instance,delta,"health-field");m.TrackHealth(__instance);
        }
      }
      static void Postfix(FPP_Player __instance){if(Instance)Instance.TrackHealth(__instance);}
    }
  }
}
