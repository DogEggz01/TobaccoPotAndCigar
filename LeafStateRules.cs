using System;

namespace TobaccoPotAndCigar.Runtime
{
    public static class LeafStateRules
    {
        public const float DryingHours = 144f;
        public const int HarvestCount = 3;

        public static bool IsDry(float hours)
        {
            return !float.IsNaN(hours) && !float.IsInfinity(hours) && hours >= DryingHours;
        }

        public static bool TryReadWrapper(float count, float code, out TobaccoRecipe recipe)
        {
            recipe = default(TobaccoRecipe);
            if (float.IsNaN(count) || float.IsInfinity(count) || count < 0 || count > 3 ||
                count != Math.Floor(count) || float.IsNaN(code) || float.IsInfinity(code) ||
                code < 0 || code > int.MaxValue || code != Math.Floor(code))
                return false;
            if (count == 0) return code == 0;
            bool legacy;
            return TobaccoRecipeCodec.TryDecode((int)code, out recipe, out legacy) && recipe.Count == (int)count;
        }

        public static bool CanCombine(float targetHours, float heldHours, bool distinct,
            bool targetOwned, bool heldOwned)
        {
            return distinct && targetOwned && heldOwned && IsDry(targetHours) && IsDry(heldHours);
        }
    }
}
