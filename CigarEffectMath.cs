namespace TobaccoPotAndCigar.Runtime
{
    public struct CigarEffectTotals
    {
        public float ImmediateSleepPerSecond;
        public float LingeringSleepPerSecond;
        public float WhiteVisualInputPerSecond;
        public float GreenVisualInputPerSecond;
        public float BlackVisualInputPerSecond;
    }

    public static class CigarEffectMath
    {
        public static CigarEffectTotals Calculate(
            TobaccoBlend blend,
            float blueImmediateSleepPerSecond,
            float blueGreenVisualInputPerSecond)
        {
            return new CigarEffectTotals
            {
                ImmediateSleepPerSecond =
                    blend.White * 0.66f -
                    blend.Green * 0.11f +
                    blend.Blue * blueImmediateSleepPerSecond +
                    blend.Black * 1.20f +
                    blend.Brown * 0.80f,
                LingeringSleepPerSecond =
                    blend.White * -0.165f +
                    blend.Green * -0.055f +
                    blend.Blue * -0.055f +
                    blend.Black * -0.30f +
                    blend.Brown * -0.20f,
                WhiteVisualInputPerSecond = blend.White * 2f,
                GreenVisualInputPerSecond =
                    blend.Green * 2f +
                    blend.Blue * blueGreenVisualInputPerSecond,
                BlackVisualInputPerSecond = blend.Black * 2f
            };
        }
    }
}
