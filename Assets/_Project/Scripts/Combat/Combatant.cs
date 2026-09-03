namespace Fantasia.Combat
{
    // Minimal fighter data for the turn/attack prototype — HP, Speed (GDD 6.1
    // stat table) and a Focus pool. Swap for real character data once that
    // system exists; combat logic only needs these fields.
    public class Combatant
    {
        public string Name;
        public bool IsPlayerSide;
        public int MaxHP;
        public int CurrentHP;
        public int Speed;
        public int Focus;
        public int MaxFocus;
        public WeaponDefinition Weapon;

        public bool IsAlive => CurrentHP > 0;

        public void TakeDamage(int amount)
        {
            CurrentHP = System.Math.Max(0, CurrentHP - amount);
        }
    }
}
