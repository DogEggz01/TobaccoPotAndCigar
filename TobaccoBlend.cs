using System;

namespace TobaccoPotAndCigar.Runtime
{
    public enum CigarTobaccoType
    {
        White = 1,
        Green = 2,
        Black = 3,
        Brown = 4,
        Blue = 5
    }

    public struct TobaccoBlend
    {
        public int White;
        public int Green;
        public int Black;
        public int Brown;
        public int Blue;

        public int Count
        {
            get { return White + Green + Black + Brown + Blue; }
        }

        public bool TryAdd(ShipItemTobacco tobacco)
        {
            CigarTobaccoType tobaccoType;
            return TobaccoTypeResolver.TryResolve(tobacco, out tobaccoType) &&
                   TryAdd(tobaccoType);
        }

        public bool TryAdd(CigarTobaccoType tobaccoType)
        {
            if (Count >= 3)
                return false;

            switch (tobaccoType)
            {
                case CigarTobaccoType.White:
                    White++;
                    return true;
                case CigarTobaccoType.Green:
                    Green++;
                    return true;
                case CigarTobaccoType.Black:
                    Black++;
                    return true;
                case CigarTobaccoType.Brown:
                    Brown++;
                    return true;
                case CigarTobaccoType.Blue:
                    Blue++;
                    return true;
                default:
                    return false;
            }
        }
    }

    public static class TobaccoBlendCodec
    {
        public static int Encode(TobaccoBlend blend)
        {
            if (blend.Count < 1 || blend.Count > 3 ||
                blend.White < 0 || blend.White > 3 ||
                blend.Green < 0 || blend.Green > 3 ||
                blend.Black < 0 || blend.Black > 3 ||
                blend.Brown < 0 || blend.Brown > 3 ||
                blend.Blue < 0 || blend.Blue > 3)
            {
                throw new ArgumentOutOfRangeException("blend");
            }

            return 1 + blend.White +
                   (blend.Green << 2) +
                   (blend.Black << 4) +
                   (blend.Brown << 6) +
                   (blend.Blue << 8);
        }

        public static bool TryDecode(int code, out TobaccoBlend blend)
        {
            TobaccoRecipe recipe;
            if (TobaccoRecipeCodec.TryDecodeOrdered(code, out recipe))
            {
                blend = recipe.ToBlend();
                return true;
            }

            return TryDecodeLegacy(code, out blend);
        }

        internal static bool TryDecodeLegacy(int code, out TobaccoBlend blend)
        {
            blend = default(TobaccoBlend);
            if (code <= 0 || code >= TobaccoRecipeCodec.OrderedRecipeMarker)
                return false;

            int raw = code - 1;
            blend.White = raw & 3;
            blend.Green = (raw >> 2) & 3;
            blend.Black = (raw >> 4) & 3;
            blend.Brown = (raw >> 6) & 3;
            blend.Blue = (raw >> 8) & 3;

            int knownBits = (3 << 0) | (3 << 2) | (3 << 4) | (3 << 6) | (3 << 8);
            return (raw & ~knownBits) == 0 && blend.Count >= 1 && blend.Count <= 3;
        }

        public static bool TryDecodeCigar(float itemAmount, out TobaccoBlend blend)
        {
            blend = default(TobaccoBlend);
            int signedCode = UnityEngine.Mathf.RoundToInt(itemAmount);
            return signedCode < 0 && TryDecode(-signedCode, out blend);
        }
    }
}
