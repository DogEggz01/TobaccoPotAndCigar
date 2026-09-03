using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    public enum CigarEmberCoolingPhase
    {
        Dark,
        YellowToOrange,
        OrangeToRed,
        RedToDark
    }

    public static class CigarEmberMath
    {
        private const float BoundaryEpsilon = 0.0001f;
        public const float YellowToOrangeSeconds = 2f;
        public const float OrangeToRedSeconds = 12f;
        public const float RedToDarkSeconds = 36f;
        public const float FullHeatCoolingSeconds =
            YellowToOrangeSeconds + OrangeToRedSeconds + RedToDarkSeconds;

        public static CigarEmberCoolingPhase GetCoolingPhase(
            float currentHeat01,
            float coolingStartHeat01)
        {
            if (currentHeat01 <= 0f || coolingStartHeat01 <= 0f)
                return CigarEmberCoolingPhase.Dark;

            float profileSeconds = GetProfileSeconds(
                currentHeat01,
                coolingStartHeat01);
            if (profileSeconds + BoundaryEpsilon < YellowToOrangeSeconds)
                return CigarEmberCoolingPhase.YellowToOrange;
            if (profileSeconds + BoundaryEpsilon <
                YellowToOrangeSeconds + OrangeToRedSeconds)
                return CigarEmberCoolingPhase.OrangeToRed;
            return CigarEmberCoolingPhase.RedToDark;
        }

        public static float GetPhaseProgress(
            CigarEmberCoolingPhase phase,
            float currentHeat01,
            float coolingStartHeat01)
        {
            if (currentHeat01 <= 0f || coolingStartHeat01 <= 0f)
                return phase == CigarEmberCoolingPhase.RedToDark ? 1f : 0f;

            float profileSeconds = GetProfileSeconds(
                currentHeat01,
                coolingStartHeat01);
            if (phase == CigarEmberCoolingPhase.YellowToOrange)
            {
                return Mathf.Clamp01(
                    profileSeconds / YellowToOrangeSeconds);
            }

            if (phase == CigarEmberCoolingPhase.OrangeToRed)
            {
                return Mathf.Clamp01(
                    (profileSeconds - YellowToOrangeSeconds) /
                    OrangeToRedSeconds);
            }

            if (phase == CigarEmberCoolingPhase.RedToDark)
            {
                return Mathf.Clamp01(
                    (profileSeconds - YellowToOrangeSeconds -
                     OrangeToRedSeconds) / RedToDarkSeconds);
            }

            return 0f;
        }

        public static float GetProfileSeconds(
            float currentHeat01,
            float coolingStartHeat01)
        {
            if (coolingStartHeat01 <= 0f)
                return FullHeatCoolingSeconds;
            float remainingFraction = Mathf.Clamp01(
                currentHeat01 / coolingStartHeat01);
            return (1f - remainingFraction) * FullHeatCoolingSeconds;
        }
    }
}
