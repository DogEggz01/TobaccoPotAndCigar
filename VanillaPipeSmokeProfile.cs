using System;
using TobaccoPotAndCigar.Runtime;
using UnityEngine;

namespace TobaccoPotAndCigar.Prefabs
{
    internal static class VanillaPipeSmokeProfile
    {
        internal const float EmissionRate = 120f;
        internal const float MaximumLifetime = 12f;
        internal const int MaximumParticles = 5000;

        internal static void ApplyRequired(GameObject prefab, int prefabIndex)
        {
            if (prefab == null)
                throw new InvalidOperationException(
                    "Vanilla pipe prefab " + prefabIndex + " is missing.");

            SaveablePrefab saveable = prefab.GetComponent<SaveablePrefab>();
            ShipItemPipe pipe = prefab.GetComponent<ShipItemPipe>();
            if (saveable == null || saveable.prefabIndex != prefabIndex ||
                pipe == null || !Apply(pipe))
            {
                throw new InvalidOperationException(
                    "Vanilla pipe prefab " + prefabIndex +
                    " does not expose its expected smoke profile.");
            }
        }

        internal static bool ApplyIfVanilla(ShipItemPipe pipe)
        {
            if (pipe == null)
                return false;
            SaveablePrefab saveable = pipe.GetComponent<SaveablePrefab>();
            if (saveable == null ||
                saveable.prefabIndex < RuntimeConstants.FirstVanillaPipePrefabIndex ||
                saveable.prefabIndex > RuntimeConstants.LastVanillaPipePrefabIndex)
            {
                return false;
            }

            return Apply(pipe);
        }

        private static bool Apply(ShipItemPipe pipe)
        {
            ParticleSystem smokeParticles =
                pipe.GetComponentInChildren<ParticleSystem>(true);
            if (smokeParticles == null)
                return false;

            pipe.maxLifetime = MaximumLifetime;
            pipe.maxEmission = EmissionRate;
            ParticleSystem.MainModule main = smokeParticles.main;
            main.maxParticles = MaximumParticles;
            ParticleSystem.EmissionModule emission = smokeParticles.emission;
            emission.rateOverTime = EmissionRate;
            return true;
        }
    }
}
