using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    public static class GrowthMath
    {
        public const float Capacity = 15f;
        public const float GameHoursPerDay = 24f;
        public const int RequiredDays = 15;
        public const float RequiredGameHours = RequiredDays * GameHoursPerDay;

        public static int Advance(
            ref float storedWater,
            ref float growthGameHours,
            float availableGameHours)
        {
            storedWater = Mathf.Clamp(storedWater, 0f, Capacity);
            growthGameHours = Mathf.Clamp(growthGameHours, 0f, RequiredGameHours);
            availableGameHours = Mathf.Max(0f, availableGameHours);
            int waterUnitsConsumed = 0;

            while (availableGameHours > 0.00001f &&
                   storedWater >= 1f &&
                   growthGameHours < RequiredGameHours)
            {
                int completedDays = Mathf.FloorToInt(
                    growthGameHours / GameHoursPerDay);
                float nextBoundary = Mathf.Min(
                    (completedDays + 1) * GameHoursPerDay,
                    RequiredGameHours);
                float step = Mathf.Min(
                    availableGameHours,
                    nextBoundary - growthGameHours);

                if (step <= 0f)
                    break;

                growthGameHours += step;
                availableGameHours -= step;

                if (growthGameHours >= nextBoundary - 0.0001f)
                {
                    growthGameHours = nextBoundary;
                    storedWater = Mathf.Max(0f, storedWater - 1f);
                    waterUnitsConsumed++;
                }
            }

            return waterUnitsConsumed;
        }
    }
}
