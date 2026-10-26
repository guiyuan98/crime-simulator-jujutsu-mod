using System;
namespace CrimeJJK {
  public sealed class Rules {
    public const float DamageMultiplier = 10f;
    public static float SkillDamage(float baseDamage) { return baseDamage * DamageMultiplier; }
    public static bool LocalPlayerAllowed(bool singlePlayer, int playerCount, bool stateAuthority) {
      return singlePlayer || (playerCount == 1 && stateAuthority);
    }
    public const float MaxEnergy = 5000f;
    public float Energy = MaxEnergy;
    public float AbsorbDamage(float damage) {
      if(damage<=0 || float.IsNaN(damage) || float.IsInfinity(damage))return damage;
      float absorbed=Math.Min(damage,Math.Max(0,Energy));Energy-=absorbed;return damage-absorbed;
    }
    public static bool PermanentDefeat(int character,bool previouslyDown,bool nowDown) { return character==1 && !previouslyDown && nowDown; }
    public int Character;
    public readonly float[,] ReadyAt = new float[2,4];
    public static readonly float[,] Cost = {{18,25,55,75},{12,24,45,75}};
    public static readonly float[,] Cooldown = {{5,7,16,35},{2,5,13,35}};
    public bool Spend(int slot, float now) {
      if (slot < 0 || slot > 3 || float.IsNaN(now) || float.IsInfinity(now)) return false;
      if (Energy < Cost[Character,slot] || now < ReadyAt[Character,slot]) return false;
      Energy -= Cost[Character,slot]; ReadyAt[Character,slot] = now + Cooldown[Character,slot]; return true;
    }
    public void Tick(float dt, bool infinity) { Energy = Math.Max(0, Math.Min(MaxEnergy, Energy + (infinity ? -8 : 9) * Math.Max(0,dt))); }
  }
}
