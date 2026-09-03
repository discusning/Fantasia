namespace Fantasia.Combat
{
    public enum DurabilityTier
    {
        Weak,
        Normal,
        Strong,
    }

    // Plain data for now (see GDD 6.2). Swap for a ScriptableObject once the
    // item/data system is scaffolded — combat logic only needs these fields.
    public class WeaponDefinition
    {
        public int SlotCount;
        public float BaseDamagePerSlot;
        public DurabilityTier Durability;

        public float SlotSuccessChance => 1f / SlotCount;

        public float DurabilityMultiplier => Durability switch
        {
            DurabilityTier.Weak => 0.33f,
            DurabilityTier.Normal => 0.66f,
            DurabilityTier.Strong => 1f,
            _ => 1f,
        };
    }
}
