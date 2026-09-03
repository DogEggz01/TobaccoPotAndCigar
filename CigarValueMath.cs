using System;

namespace TobaccoPotAndCigar.Runtime
{
    public static class CigarValueMath
    {
        public const int WrapperValue = 200;

        public static int Calculate(
            TobaccoBlend blend,
            int whiteValue,
            int greenValue,
            int blackValue,
            int brownValue,
            int blueValue)
        {
            return WrapperValue +
                   blend.White * whiteValue +
                   blend.Green * greenValue +
                   blend.Black * blackValue +
                   blend.Brown * brownValue +
                   blend.Blue * blueValue;
        }

        public static int ScaleByRemainingHealth(
            int fullValue,
            float currentHealth,
            float initialHealth)
        {
            if (fullValue <= 0)
                return 0;
            if (initialHealth <= 0f || float.IsNaN(initialHealth))
                return fullValue;
            if (currentHealth <= 0f || float.IsNaN(currentHealth))
                return 0;
            if (currentHealth >= initialHealth)
                return fullValue;

            double scaled = fullValue * (double)currentHealth / initialHealth;
            return Math.Max(
                0,
                (int)Math.Round(scaled, MidpointRounding.AwayFromZero));
        }
    }
}
