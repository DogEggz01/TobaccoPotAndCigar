using System;

namespace TobaccoPotAndCigar.Runtime
{
    public struct TobaccoRecipe
    {
        private CigarTobaccoType first;
        private CigarTobaccoType second;
        private CigarTobaccoType third;
        private int count;

        public int Count
        {
            get { return count; }
        }

        public bool IsFullSingleType
        {
            get
            {
                return count == 3 && first == second && second == third;
            }
        }

        public CigarTobaccoType GetAt(int index)
        {
            switch (index)
            {
                case 0:
                    if (count > 0)
                        return first;
                    break;
                case 1:
                    if (count > 1)
                        return second;
                    break;
                case 2:
                    if (count > 2)
                        return third;
                    break;
            }

            throw new ArgumentOutOfRangeException("index");
        }

        public bool TryAdd(CigarTobaccoType tobaccoType)
        {
            if (count >= 3 || !TobaccoTypeResolver.IsSupported(tobaccoType))
                return false;

            if (count == 0)
                first = tobaccoType;
            else if (count == 1)
                second = tobaccoType;
            else
                third = tobaccoType;
            count++;
            return true;
        }

        public bool TryAdd(ShipItemTobacco tobacco)
        {
            CigarTobaccoType tobaccoType;
            return TobaccoTypeResolver.TryResolve(tobacco, out tobaccoType) &&
                   TryAdd(tobaccoType);
        }

        public TobaccoBlend ToBlend()
        {
            TobaccoBlend blend = default(TobaccoBlend);
            for (int i = 0; i < count; i++)
                blend.TryAdd(GetAt(i));
            return blend;
        }

        internal static TobaccoRecipe FromLegacyBlend(TobaccoBlend blend)
        {
            TobaccoRecipe recipe = default(TobaccoRecipe);
            AddCopies(ref recipe, CigarTobaccoType.Green, blend.Green);
            AddCopies(ref recipe, CigarTobaccoType.White, blend.White);
            AddCopies(ref recipe, CigarTobaccoType.Brown, blend.Brown);
            AddCopies(ref recipe, CigarTobaccoType.Blue, blend.Blue);
            AddCopies(ref recipe, CigarTobaccoType.Black, blend.Black);
            return recipe;
        }

        private static void AddCopies(
            ref TobaccoRecipe recipe,
            CigarTobaccoType tobaccoType,
            int copies)
        {
            for (int i = 0; i < copies; i++)
                recipe.TryAdd(tobaccoType);
        }
    }

    public static class TobaccoRecipeCodec
    {
        public const int OrderedRecipeMarker = 4096;
        private const int Radix = 6;
        private const int MaximumPayload = Radix * Radix * Radix;

        public static int Encode(TobaccoRecipe recipe)
        {
            if (recipe.Count < 1 || recipe.Count > 3)
                throw new ArgumentOutOfRangeException("recipe");

            int payload = 0;
            int place = 1;
            for (int i = 0; i < recipe.Count; i++)
            {
                payload += (int)recipe.GetAt(i) * place;
                place *= Radix;
            }
            return OrderedRecipeMarker + payload;
        }

        public static bool TryDecode(
            int code,
            out TobaccoRecipe recipe,
            out bool wasLegacy)
        {
            wasLegacy = false;
            if (TryDecodeOrdered(code, out recipe))
                return true;

            TobaccoBlend legacyBlend;
            if (!TobaccoBlendCodec.TryDecodeLegacy(code, out legacyBlend))
            {
                recipe = default(TobaccoRecipe);
                return false;
            }

            recipe = TobaccoRecipe.FromLegacyBlend(legacyBlend);
            wasLegacy = true;
            return recipe.Count == legacyBlend.Count;
        }

        public static bool TryDecodeCigar(
            float itemAmount,
            out TobaccoRecipe recipe,
            out bool wasLegacy)
        {
            recipe = default(TobaccoRecipe);
            wasLegacy = false;
            int signedCode = UnityEngine.Mathf.RoundToInt(itemAmount);
            return signedCode < 0 && TryDecode(-signedCode, out recipe, out wasLegacy);
        }

        internal static bool TryDecodeOrdered(
            int code,
            out TobaccoRecipe recipe)
        {
            recipe = default(TobaccoRecipe);
            int payload = code - OrderedRecipeMarker;
            if (payload <= 0 || payload >= MaximumPayload)
                return false;

            int first = payload % Radix;
            int second = payload / Radix % Radix;
            int third = payload / (Radix * Radix) % Radix;
            if (!IsValidDigit(first) ||
                (second == 0 && third != 0) ||
                (second != 0 && !IsValidDigit(second)) ||
                (third != 0 && !IsValidDigit(third)))
            {
                return false;
            }

            recipe.TryAdd((CigarTobaccoType)first);
            if (second != 0)
                recipe.TryAdd((CigarTobaccoType)second);
            if (third != 0)
                recipe.TryAdd((CigarTobaccoType)third);
            return true;
        }

        private static bool IsValidDigit(int value)
        {
            return value >= (int)CigarTobaccoType.White &&
                   value <= (int)CigarTobaccoType.Blue;
        }
    }

    public static class TobaccoTypeResolver
    {
        public static bool TryResolve(
            ShipItemTobacco tobacco,
            out CigarTobaccoType tobaccoType)
        {
            tobaccoType = default(CigarTobaccoType);
            if (tobacco == null)
                return false;

            SaveablePrefab saveable = tobacco.GetComponent<SaveablePrefab>();
            if ((saveable != null &&
                 saveable.prefabIndex == RuntimeConstants.BlueTobaccoPrefabIndex) ||
                tobacco.tobaccoType == (int)CigarTobaccoType.Blue)
            {
                tobaccoType = CigarTobaccoType.Blue;
                return true;
            }

            tobaccoType = (CigarTobaccoType)tobacco.tobaccoType;
            return IsSupported(tobaccoType);
        }

        public static bool IsSupported(CigarTobaccoType tobaccoType)
        {
            return tobaccoType >= CigarTobaccoType.White &&
                   tobaccoType <= CigarTobaccoType.Blue;
        }
    }
}
