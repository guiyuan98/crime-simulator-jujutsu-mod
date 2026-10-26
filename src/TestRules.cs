using System;
using CrimeJJK;
class TestRules {
  static void Check(bool b, string name) { if (!b) throw new Exception(name); }
  static int Main() {
    var r = new Rules();
    Check(r.Energy==5000 && r.Spend(0,0) && r.Energy == 4982,"5000 initial; cast consumes energy");
    Check(!r.Spend(0,1),"cooldown blocks repeat");
    r.Character=1; Check(r.Spend(0,1),"independent character cooldown");
    r.Character=0; Check(!r.Spend(0,2),"switch cannot reset cooldown");
    r.Energy=0; Check(!r.Spend(3,100),"insufficient energy");
    r.Tick(10000,false); Check(r.Energy==5000,"regen clamped to5000");
    r.Tick(10000,true); Check(r.Energy==0,"infinity drain clamped");
    Check(!r.Spend(-1,100) && !r.Spend(4,100) && !r.Spend(0,float.NaN),"invalid input");
    r.Energy=100;Check(r.Spend(0,5),"cooldown exact boundary");
    Check(Rules.LocalPlayerAllowed(true,0,true),"offline local owner without input authority");
    Check(Rules.LocalPlayerAllowed(true,1,false),"offline mode does not depend on input authority");
    Check(Rules.LocalPlayerAllowed(false,1,true),"solo host");
    Check(!Rules.LocalPlayerAllowed(false,2,true),"multiplayer rejected");
    Check(!Rules.LocalPlayerAllowed(false,1,false),"non-owner rejected");
    r.Energy=5000;Check(r.AbsorbDamage(125)==0 && r.Energy==4875,"full shield1energy per1damage");
    r.Energy=25;Check(r.AbsorbDamage(40)==15 && r.Energy==0,"partial shield residual damage");
    Check(r.AbsorbDamage(40)==40 && r.Energy==0,"empty shield passthrough");
    Check(r.AbsorbDamage(0)==0 && r.AbsorbDamage(-1)==-1 && r.Energy==0,"nonpositive no drain");
    Check(float.IsNaN(r.AbsorbDamage(float.NaN)) && float.IsInfinity(r.AbsorbDamage(float.PositiveInfinity)) && r.Energy==0,"invalid shield inputs");
    Check(Rules.PermanentDefeat(1,false,true) && !Rules.PermanentDefeat(0,false,true) && !Rules.PermanentDefeat(1,true,true) && !Rules.PermanentDefeat(1,false,false),"only Sukuna newly defeated targets");
    Console.WriteLine("PASS rules: cost,cooldown,character-switch,regen,infinity,invalid-input,boundary");
    Console.WriteLine("PASS local-player: offline-zero-player-count,offline-no-input-authority,solo-owner,multiplayer-rejected,non-owner-rejected");
    Console.WriteLine("PASS combat: energy=5000 shield=full,partial,empty,invalid-input permanent-defeat=Sukuna-only");return 0;
  }
}
