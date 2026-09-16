// Cigar — grouped mod source. Existing type identities are preserved.

// PipePatches
namespace TobaccoPotAndCigar.Patches
{
    using HarmonyLib;
    using TobaccoPotAndCigar.Prefabs;
    using TobaccoPotAndCigar.Runtime;
    using TobaccoPotAndCigar.Smoking;
[HarmonyPatch(typeof(ShipItemPipe), "OnItemClick")]
    internal static class CigarRefillBlockPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ShipItemPipe __instance, ref bool __result)
        {
            if (__instance.GetComponent<CigarRuntimeState>() == null)
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ShipItemPipe), "ExtraLateUpdate")]
    internal static class CigarPipeContextPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            ShipItemPipe __instance,
            out CigarRuntimeState __state)
        {
            __state = __instance.GetComponent<CigarRuntimeState>();
            CigarSmokeContext.Current = __state;
        }

        [HarmonyPostfix]
        private static void Postfix(
            CigarRuntimeState __state,
            float ___currentHeat,
            bool ___inhaling)
        {
            if (__state != null)
            {
                if (!__state.TryDestroyIfConsumed())
                {
                    __state.SyncVisuals(
                        false,
                        ___currentHeat / 100f,
                        ___inhaling);
                }
            }
            if (CigarSmokeContext.Current == __state)
                CigarSmokeContext.Current = null;
        }
    }

    [HarmonyPatch(typeof(ShipItemPipe), "OnLoad")]
    internal static class CigarPipeLoadPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ShipItemPipe __instance)
        {
            VanillaPipeSmokeProfile.ApplyIfVanilla(__instance);
            RestingPipeState.Ensure(__instance);
            CigarRuntimeState cigar = __instance.GetComponent<CigarRuntimeState>();
            if (cigar != null)
            {
                if (cigar.TryDestroyIfConsumed())
                    return;
                cigar.RefreshValue();
                cigar.SyncVisuals(true, 0f, false);
            }
        }
    }
}


// TobaccoEffectPatches
namespace TobaccoPotAndCigar.Patches
{
    using HarmonyLib;
    using TobaccoPotAndCigar.Runtime;
    using TobaccoPotAndCigar.Smoking;
    using UnityEngine;
[HarmonyPatch(typeof(PlayerTobacco), "Smoke")]
    internal static class CigarSmokeRecipePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(int tobaccoType)
        {
            if (tobaccoType >= 0)
                return true;

            CigarRuntimeState cigar = CigarSmokeContext.Current;
            TobaccoBlend blend;
            if (cigar == null ||
                !TobaccoBlendCodec.TryDecode(-tobaccoType, out blend))
            {
                RuntimeDiagnostics.Error(
                    "Rejected a cigar smoke call with no valid cigar recipe context.");
                return false;
            }

            CigarEffectService.ApplyInhale(cigar, blend, Time.deltaTime);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerTobacco), "Update")]
    internal static class CigarEffectUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerTobacco __instance)
        {
            CigarEffectService.TickAndCompose(__instance, Time.deltaTime);
        }
    }
}


// VanillaPipeSmokeProfile
namespace TobaccoPotAndCigar.Prefabs
{
    using System;
    using TobaccoPotAndCigar.Runtime;
    using UnityEngine;
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


// CigarEffectService
namespace TobaccoPotAndCigar.Smoking
{
    using System.Collections.Generic;
    using TobaccoPotAndCigar.Compatibility;
    using TobaccoPotAndCigar.Runtime;
    using UnityEngine;
internal static class CigarEffectService
    {
        private sealed class DoseChannel
        {
            internal CigarTobaccoType Type;
            internal float Charge;
        }

        private sealed class DoseGroup
        {
            internal readonly List<DoseChannel> Channels =
                new List<DoseChannel>(3);
        }

        private static readonly Dictionary<int, DoseGroup> Doses =
            new Dictionary<int, DoseGroup>();
        private static readonly List<int> RemovalBuffer = new List<int>();

        internal static void ApplyInhale(
            CigarRuntimeState source,
            TobaccoBlend blend,
            float deltaTime)
        {
            if (source == null || deltaTime <= 0f)
                return;

            DoseGroup dose = GetOrCreateDose(source, blend);
            BlueEffectProfile blue =
                RadRefinementsCompatibility.GetBlueEffectProfile();
            CigarEffectTotals totals = CigarEffectMath.Calculate(
                blend,
                blue.ImmediateSleepChange,
                blue.GreenVisualChargeRate);
            PlayerNeeds.sleep += deltaTime * totals.ImmediateSleepPerSecond;

            for (int i = 0; i < dose.Channels.Count; i++)
            {
                DoseChannel channel = dose.Channels[i];
                float chargeRate = channel.Type == CigarTobaccoType.Blue
                    ? blue.GreenVisualChargeRate
                    : 2f;
                channel.Charge += deltaTime * chargeRate;
            }
        }

        internal static void TickAndCompose(PlayerTobacco vanilla, float deltaTime)
        {
            if (vanilla == null || Doses.Count == 0 || deltaTime <= 0f)
                return;

            float white = vanilla.white;
            float green = vanilla.green;
            float black = vanilla.black;
            RemovalBuffer.Clear();

            foreach (KeyValuePair<int, DoseGroup> pair in Doses)
            {
                bool groupActive = false;
                List<DoseChannel> channels = pair.Value.Channels;
                for (int i = 0; i < channels.Count; i++)
                {
                    DoseChannel channel = channels[i];
                    if (channel.Charge <= 0f)
                        continue;

                    groupActive = true;
                    channel.Charge = Mathf.Max(
                        0f,
                        channel.Charge - deltaTime * 0.5f);
                    switch (channel.Type)
                    {
                        case CigarTobaccoType.White:
                            white += channel.Charge;
                            PlayerNeeds.sleep -= deltaTime * 0.165f;
                            break;
                        case CigarTobaccoType.Green:
                            green += channel.Charge;
                            PlayerNeeds.sleep -= deltaTime * 0.055f;
                            break;
                        case CigarTobaccoType.Blue:
                            green += channel.Charge;
                            PlayerNeeds.sleep -= deltaTime * 0.055f;
                            break;
                        case CigarTobaccoType.Black:
                            black += channel.Charge;
                            PlayerNeeds.sleep -= deltaTime * 0.30f;
                            break;
                        case CigarTobaccoType.Brown:
                            PlayerNeeds.sleep -= deltaTime * 0.20f;
                            break;
                    }
                }

                if (!groupActive)
                    RemovalBuffer.Add(pair.Key);
            }

            for (int i = 0; i < RemovalBuffer.Count; i++)
                Doses.Remove(RemovalBuffer[i]);

            PlayerTobacco.saturationOverride = 1f + (green - white) / 100f;
            if (vanilla.postProcessing != null)
            {
                var bloom = vanilla.postProcessing.bloom.settings;
                bloom.bloom.intensity = Mathf.Lerp(
                    0.66f,
                    0f,
                    Mathf.Clamp01(black / 100f));
                vanilla.postProcessing.bloom.settings = bloom;
            }
        }

        internal static void Reset()
        {
            Doses.Clear();
            RemovalBuffer.Clear();
            CigarSmokeContext.Current = null;
        }

        private static DoseGroup GetOrCreateDose(
            CigarRuntimeState source,
            TobaccoBlend blend)
        {
            int key = source.GetInstanceID();
            DoseGroup group;
            if (Doses.TryGetValue(key, out group))
                return group;

            group = new DoseGroup();
            AddChannels(group, CigarTobaccoType.White, blend.White);
            AddChannels(group, CigarTobaccoType.Green, blend.Green);
            AddChannels(group, CigarTobaccoType.Black, blend.Black);
            AddChannels(group, CigarTobaccoType.Brown, blend.Brown);
            AddChannels(group, CigarTobaccoType.Blue, blend.Blue);
            Doses.Add(key, group);
            return group;
        }

        private static void AddChannels(
            DoseGroup group,
            CigarTobaccoType type,
            int count)
        {
            for (int i = 0; i < count; i++)
                group.Channels.Add(new DoseChannel { Type = type });
        }
    }
}


// CigarSmokeContext
namespace TobaccoPotAndCigar.Smoking
{
    using TobaccoPotAndCigar.Runtime;
internal static class CigarSmokeContext
    {
        internal static CigarRuntimeState Current;
    }
}


// CigarEffectMath
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


// CigarEmberMath
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
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


// CigarHintText
namespace TobaccoPotAndCigar.Runtime
{

public static class CigarHintText
    {
        public const string GreenHex = "3FAF45";
        public const string BrownHex = "8B5A2B";
        public const string BlueHex = "3F7FFF";
        public const string BlackHex = "202020";

        public static string Build(TobaccoRecipe recipe)
        {
            if (recipe.Count <= 0)
                return "cigar";

            if (recipe.IsFullSingleType)
                return "Full " + GetName(recipe.GetAt(0)) + " Cigar";

            string code = "";
            for (int i = 0; i < recipe.Count; i++)
                code += GetColoredLetter(recipe.GetAt(i));
            return code + " Cigar";
        }

        private static string GetName(CigarTobaccoType tobaccoType)
        {
            switch (tobaccoType)
            {
                case CigarTobaccoType.White:
                    return "White";
                case CigarTobaccoType.Green:
                    return "Green";
                case CigarTobaccoType.Black:
                    return "Black";
                case CigarTobaccoType.Brown:
                    return "Brown";
                case CigarTobaccoType.Blue:
                    return "Blue";
                default:
                    return "Unknown";
            }
        }

        private static string GetColoredLetter(CigarTobaccoType tobaccoType)
        {
            switch (tobaccoType)
            {
                case CigarTobaccoType.White:
                    return "W";
                case CigarTobaccoType.Green:
                    return Color("G", GreenHex);
                case CigarTobaccoType.Brown:
                    return Color("B", BrownHex);
                case CigarTobaccoType.Blue:
                    return Color("B", BlueHex);
                case CigarTobaccoType.Black:
                    return Color("B", BlackHex);
                default:
                    return "?";
            }
        }

        private static string Color(string letter, string hex)
        {
            return "<color=#" + hex + ">" + letter + "</color>";
        }
    }
}


// CigarValueMath
namespace TobaccoPotAndCigar.Runtime
{
    using System;
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


// TobaccoBlend
namespace TobaccoPotAndCigar.Runtime
{
    using System;
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


// TobaccoRecipe
namespace TobaccoPotAndCigar.Runtime
{
    using System;
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


// CigarRuntimeState
namespace TobaccoPotAndCigar.Runtime
{
    using System.Collections;
    using UnityEngine;
public sealed class CigarRuntimeState : MonoBehaviour
    {
        [SerializeField] private CigarVisualController cigarVisual;

        private ShipItemPipe pipe;
        private int fullRecipeValue;
        private bool valueInitialized;
        private bool destructionQueued;
        private float ashClearedAt01 = 1f;
        private AshtrayState restingTray;
        private int restSlot;
        private int savedTrayId;
        private Transform restSocket;
        private bool started;
        public bool IsResting { get { return restingTray != null; } }
        public float AccumulatedAsh01
        {
            get
            {
                float initial = GetInitialHealth();
                return initial > 0 ? Mathf.Max(0, ashClearedAt01 - Mathf.Clamp01(pipe.health / initial)) : 0;
            }
        }

        public ShipItemPipe Pipe
        {
            get { return pipe; }
        }

        private void Awake()
        {
            pipe = GetComponent<ShipItemPipe>();
            if (cigarVisual == null)
                cigarVisual = GetComponentInChildren<CigarVisualController>(true);
            if (cigarVisual != null)
                cigarVisual.BindRoot(pipe);
        }

        private void Start()
        {
            started = true;
            RefreshValue();
            SyncVisuals(true, 0f, false);
            if (savedTrayId != 0) StartCoroutine(RestoreRest());
        }

        public bool TryGetBlend(out TobaccoBlend blend)
        {
            blend = default(TobaccoBlend);
            TobaccoRecipe recipe;
            if (!TryGetRecipe(out recipe))
                return false;
            blend = recipe.ToBlend();
            return true;
        }

        public bool TryGetRecipe(out TobaccoRecipe recipe)
        {
            recipe = default(TobaccoRecipe);
            bool wasLegacy;
            return pipe != null && TobaccoRecipeCodec.TryDecodeCigar(
                pipe.amount,
                out recipe,
                out wasLegacy);
        }

        public float GetInitialHealth()
        {
            TobaccoRecipe recipe;
            return TryGetRecipe(out recipe) ? recipe.Count * 100f : 0f;
        }

        public void SyncVisuals(bool snap, float heat01, bool inhaling)
        {
            if (pipe == null)
                pipe = GetComponent<ShipItemPipe>();
            if (pipe == null)
                return;

            float initial = GetInitialHealth();
            SyncValueFromHealth(initial);
            if (cigarVisual == null)
                return;

            float remaining = initial > 0f
                ? Mathf.Clamp01(pipe.health / initial)
                : 0f;
            cigarVisual.SetAshBaseline(ashClearedAt01);
            cigarVisual.SetRemaining01(
                remaining,
                pipe.health > 0f && initial > 0f,
                Mathf.Clamp01(heat01),
                inhaling,
                snap);
        }

        public void RefreshValue()
        {
            if (pipe == null)
                pipe = GetComponent<ShipItemPipe>();
            if (pipe == null)
                return;

            TobaccoRecipe recipe;
            if (!TryGetRecipe(out recipe))
            {
                fullRecipeValue = CigarValueMath.WrapperValue;
                valueInitialized = true;
                pipe.description = string.Empty;
                SyncValueFromHealth(0f);
                return;
            }

            TobaccoBlend blend = recipe.ToBlend();

            fullRecipeValue = CigarValueMath.Calculate(
                blend,
                GetPrefabValue(RuntimeConstants.WhiteTobaccoPrefabIndex),
                GetPrefabValue(RuntimeConstants.GreenTobaccoPrefabIndex),
                GetPrefabValue(RuntimeConstants.BlackTobaccoPrefabIndex),
                GetPrefabValue(RuntimeConstants.BrownTobaccoPrefabIndex),
                GetPrefabValue(RuntimeConstants.BlueTobaccoPrefabIndex));
            valueInitialized = true;
            pipe.description = CigarHintText.Build(recipe);
            SyncValueFromHealth(blend.Count * 100f);
        }

        public bool TryDestroyIfConsumed()
        {
            if (destructionQueued)
                return true;
            if (pipe == null)
                pipe = GetComponent<ShipItemPipe>();
            if (pipe == null || pipe.health > 0f)
                return false;

            destructionQueued = true;
            pipe.DestroyItem();
            return true;
        }

        private void SyncValueFromHealth(float initialHealth)
        {
            if (!valueInitialized || pipe == null)
                return;

            int remainingValue = CigarValueMath.ScaleByRemainingHealth(
                fullRecipeValue,
                pipe.health,
                initialHealth);
            if (pipe.value != remainingValue)
                pipe.value = remainingValue;
        }

        private static int GetPrefabValue(int prefabIndex)
        {
            PrefabsDirectory directory = PrefabsDirectory.instance;
            if (directory == null || directory.directory == null ||
                prefabIndex < 0 || prefabIndex >= directory.directory.Length ||
                directory.directory[prefabIndex] == null)
            {
                return 0;
            }

            ShipItem item =
                directory.directory[prefabIndex].GetComponent<ShipItem>();
            return item != null ? item.value : 0;
        }

        public void ClearAsh()
        {
            float initial = GetInitialHealth();
            ashClearedAt01 = initial > 0 ? Mathf.Clamp01(pipe.health / initial) : 0;
            if (cigarVisual != null) cigarVisual.SetAshBaseline(ashClearedAt01);
        }

        public void RestOn(AshtrayState tray, Transform socket, int slot)
        {
            LeaveRest();
            restingTray = tray;
            restSocket = socket;
            restSlot = slot;
            Vector3 offset = RestOffset();
            transform.SetPositionAndRotation(tray.transform.TransformPoint(offset), socket.rotation);
            pipe.ResetRigidbody();
            pipe.itemRigidbodyC.EnterBox(tray.transform, offset, socket.localRotation);
            SyncRestParent();
        }

        public void LeaveRest()
        {
            savedTrayId = 0;
            if (restingTray == null) return;
            if (pipe != null && pipe.itemRigidbodyC != null &&
                pipe.itemRigidbodyC.GetCurrentBox() == restingTray.transform)
                pipe.itemRigidbodyC.ExitBox();
            restingTray.Release(this);
            restingTray = null;
            restSocket = null;
        }

        private void LateUpdate() { if (IsResting) SyncRestParent(); }
        private void OnDestroy() { LeaveRest(); }

        public void SyncRestParent()
        {
            if (!IsResting) return;
            // Native EnterBox owns pose/physics. Carry its boat save context too.
            ShipItem tray = restingTray.Item;
            if (tray.GetComponent<SaveablePrefab>().currentCrateId != 0 ||
                (tray.itemRigidbodyC != null && tray.itemRigidbodyC.GetCurrentInventorySlot() != null))
            {
                LeaveRest();
                return;
            }
            pipe.currentActualBoat = tray.currentActualBoat;
            pipe.currentWalkCol = tray.currentWalkCol;
            GetComponent<SaveablePrefab>().SetParentObject(tray.GetComponent<SaveablePrefab>().GetParentObject());
            if (restSocket != null && pipe.itemRigidbodyC != null)
                pipe.itemRigidbodyC.EnterBox(tray.transform, RestOffset(), restSocket.localRotation);
        }

        private Vector3 RestOffset()
        {
            float initial = GetInitialHealth();
            float remaining = initial > 0 ? Mathf.Clamp01(pipe.health / initial) : 0;
            // Keep the middle of the remaining wrapper supported by the groove.
            return restSocket.localPosition + restSocket.localRotation * Vector3.up * (.116f * (1 - remaining));
        }

        public void WriteSave(SavePrefabData data)
        {
            if (IsResting) SyncRestParent();
            data.extraValue0 = ashClearedAt01;
            int id = IsResting ? restingTray.GetComponent<SaveablePrefab>().instanceId : savedTrayId;
            // Split the 31-bit native ID: a single float cannot preserve every ID.
            data.extraValue1 = id & 65535;
            data.extraValue2 = (id >> 16) & 32767;
            data.extraValue3 = restSlot;
            data.extraValue4 = 110;
        }

        public void ReadSave(SavePrefabData data)
        {
            ashClearedAt01 = data.extraValue4 == 110 && !float.IsNaN(data.extraValue0) &&
                !float.IsInfinity(data.extraValue0) ? Mathf.Clamp01(data.extraValue0) : 1f;
            savedTrayId = 0;
            if (data.extraValue4 == 110 && data.inventorySlot < 0 && data.crateId == 0 &&
                data.extraValue1 >= 0 && data.extraValue1 <= 65535 && data.extraValue1 % 1 == 0 &&
                data.extraValue2 >= 0 && data.extraValue2 <= 32767 && data.extraValue2 % 1 == 0 &&
                (data.extraValue3 == 0 || data.extraValue3 == 1))
            {
                savedTrayId = (int)data.extraValue1 | ((int)data.extraValue2 << 16);
                restSlot = (int)data.extraValue3;
            }
            SyncVisuals(true, 0, false);
            if (started && savedTrayId != 0) StartCoroutine(RestoreRest());
        }

        private IEnumerator RestoreRest()
        {
            while (GameState.currentlyLoading) yield return null;
            // Native item rigidbodies and all saved tray IDs may initialize later.
            for (int frame = 0; frame < 120 && savedTrayId != 0; frame++)
            {
                if (SaveLoadManager.instance != null)
                    foreach (SaveablePrefab saved in SaveLoadManager.instance.GetCurrentPrefabs())
                        if (saved != null && saved.instanceId == savedTrayId)
                        {
                            AshtrayState tray = saved.GetComponent<AshtrayState>();
                            if (tray != null && tray.TrySeat(this, restSlot)) { savedTrayId = 0; yield break; }
                        }
                yield return null;
            }
            // Missing/occupied tray leaves the independently saved cigar recoverable.
            savedTrayId = 0;
        }
    }
}


// CigarVisualController
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
public sealed class CigarVisualController : MonoBehaviour
    {
        private const float BurnVisualLerpSpeed = 12f;
        private const float SpentFrontClearance = 0.008f;
        private const float MinimumPhysicalColliderRatio = 0.05f;
        private const float MinimumPickupLength = 0.04f;
        private const float MaximumEmberLightIntensity = 0.15f;
        private const int IgnoreRaycastLayer = 2;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorProperty =
            Shader.PropertyToID("_EmissionColor");
        private static readonly Color YellowColor =
            new Color(1f, 0.42f, 0.025f, 1f);
        private static readonly Color OrangeColor =
            new Color(0.92f, 0.12f, 0.008f, 1f);
        private static readonly Color YellowEmission =
            new Color(3.2f, 0.85f, 0.04f, 1f);
        private static readonly Color OrangeEmission =
            new Color(2.6f, 0.22f, 0.012f, 1f);
        private static readonly Color YellowLightColor =
            new Color(1f, 0.38f, 0.04f, 1f);
        private static readonly Color OrangeLightColor =
            new Color(1f, 0.12f, 0.015f, 1f);
        private static readonly Color RedLightColor =
            new Color(1f, 0.025f, 0.008f, 1f);

        [SerializeField] private Transform mouthAnchor;
        [SerializeField] private Transform burnableBody;
        [SerializeField] private Transform burnFront;
        [SerializeField] private Transform unlitTip;
        [SerializeField] private Transform ash;
        [SerializeField] private GameObject band;
        [SerializeField] private Renderer emberRenderer;
        [SerializeField] private Light emberLight;
        [SerializeField] private BoxCollider rootCollider;
        [SerializeField] private BoxCollider pickupCollider;
        [SerializeField] private Collider mouthSmokingTrigger;

        private ShipItemPipe pipe;
        private BoxCollider physicalCollider;
        private Vector3 fullBodyScale;
        private Vector3 fullBurnFrontPosition;
        private Vector3 spentBurnFrontPosition;
        private Vector3 fullRootColliderCenter;
        private Vector3 fullRootColliderSize;
        private Vector3 fullPickupColliderCenter;
        private Vector3 fullPickupColliderSize;
        private float rootColliderMouthEndY;
        private float pickupColliderMouthEndY;
        private float burnDirectionSign;
        private float bandThresholdY;
        private float targetRemaining01;
        private float visibleRemaining01;
        private float heat01;
        private float coolingStartHeat01;
        private bool inhaling;
        private bool wasInhaling;
        private bool hasCoolingStartHeat;
        private bool available;
        private bool initialized;
        private MaterialPropertyBlock emberProperties;
        private Color emberBaseColor = Color.white;
        private Color emberBaseEmission = Color.black;
        private float ashBaseline01 = 1f;
        private Vector3 ashOriginalScale;
        private Vector3 ashOriginalPosition;
        private float ashMeshLength;
        private float ashMeshBase;

        private void Awake()
        {
            if (ash != null)
            {
                ashOriginalScale = ash.localScale;
                ashOriginalPosition = ash.localPosition;
                Bounds bounds = ash.GetComponent<MeshFilter>().sharedMesh.bounds;
                ashMeshLength = bounds.size.y * ashOriginalScale.y;
                ashMeshBase = bounds.min.y * ashOriginalScale.y;
            }
            fullBodyScale = burnableBody != null
                ? burnableBody.localScale
                : Vector3.one;
            fullBurnFrontPosition = burnFront != null
                ? burnFront.localPosition
                : Vector3.zero;
            burnDirectionSign = mouthAnchor != null && burnFront != null
                ? Mathf.Sign(burnFront.localPosition.y - mouthAnchor.localPosition.y)
                : 1f;
            if (Mathf.Approximately(burnDirectionSign, 0f))
                burnDirectionSign = 1f;

            spentBurnFrontPosition = fullBurnFrontPosition;
            if (mouthAnchor != null)
            {
                spentBurnFrontPosition.y = mouthAnchor.localPosition.y +
                                           burnDirectionSign * SpentFrontClearance;
            }
            bandThresholdY = band != null
                ? band.transform.localPosition.y
                : spentBurnFrontPosition.y;

            CacheRootColliderGeometry();
            CachePickupColliderGeometry();

            if (emberRenderer != null && emberRenderer.sharedMaterial != null)
            {
                Material material = emberRenderer.sharedMaterial;
                if (material.HasProperty(ColorProperty))
                    emberBaseColor = material.GetColor(ColorProperty);
                if (material.HasProperty(EmissionColorProperty))
                    emberBaseEmission = material.GetColor(EmissionColorProperty);
                emberProperties = new MaterialPropertyBlock();
            }
        }

        public void BindRoot(ShipItemPipe newPipe)
        {
            pipe = newPipe;
            SyncInteractionLayers();
            if (rootCollider == null && pipe != null)
            {
                rootCollider = pipe.GetComponent<BoxCollider>();
                CacheRootColliderGeometry();
            }
            CachePickupColliderGeometry();
        }

        public void SetAshBaseline(float remainingWhenCleared)
        {
            ashBaseline01 = Mathf.Clamp01(remainingWhenCleared);
            if (initialized) ApplyAsh();
        }

        private void ApplyAsh()
        {
            if (ash == null || ashMeshLength <= 0) return;
            float length = Mathf.Max(0, ashBaseline01 - visibleRemaining01) *
                Mathf.Abs(fullBurnFrontPosition.y - spentBurnFrontPosition.y);
            ash.gameObject.SetActive(available && length > .00001f);
            float scale = length / ashMeshLength;
            ash.localScale = new Vector3(ashOriginalScale.x, ashOriginalScale.y * scale, ashOriginalScale.z);
            // Keep the ash's rear edge touching the moving ember while its front grows.
            ash.localPosition = ashOriginalPosition + Vector3.up * ashMeshBase * (1 - scale);
        }

        public void SetRemaining01(
            float remaining,
            bool isAvailable,
            float newHeat01,
            bool isInhaling,
            bool snap)
        {
            targetRemaining01 = Mathf.Clamp01(remaining);
            available = isAvailable;
            heat01 = Mathf.Clamp01(newHeat01);
            UpdateInhaleState(isInhaling, snap);
            if (!available)
                ResetEmberTimeline();

            if (!initialized || snap)
            {
                visibleRemaining01 = targetRemaining01;
                initialized = true;
                ApplyVisualState();
            }
        }

        private void LateUpdate()
        {
            SyncInteractionLayers();
            if (!initialized)
                return;
            TryBindPhysicalCollider();

            float lerp = 1f - Mathf.Exp(-BurnVisualLerpSpeed * Time.deltaTime);
            visibleRemaining01 = Mathf.Lerp(
                visibleRemaining01,
                targetRemaining01,
                lerp);
            if (Mathf.Abs(visibleRemaining01 - targetRemaining01) < 0.0001f)
                visibleRemaining01 = targetRemaining01;
            ApplyVisualState();
        }

        private void UpdateInhaleState(bool newInhaling, bool snap)
        {
            inhaling = newInhaling;
            if (snap)
            {
                wasInhaling = newInhaling;
                hasCoolingStartHeat = false;
                coolingStartHeat01 = 0f;
                return;
            }

            if (newInhaling)
            {
                wasInhaling = true;
                hasCoolingStartHeat = false;
                coolingStartHeat01 = 0f;
                return;
            }

            if (wasInhaling)
            {
                wasInhaling = false;
                coolingStartHeat01 = heat01;
                hasCoolingStartHeat = coolingStartHeat01 > 0f;
            }
        }

        private void ResetEmberTimeline()
        {
            inhaling = false;
            wasInhaling = false;
            coolingStartHeat01 = 0f;
            hasCoolingStartHeat = false;
        }

        private void SyncInteractionLayers()
        {
            // The mouth trigger must participate in mouth collisions but never
            // intercept GoPointer's single ray.
            if (mouthSmokingTrigger != null &&
                mouthSmokingTrigger.gameObject.layer != IgnoreRaycastLayer)
            {
                mouthSmokingTrigger.gameObject.layer = IgnoreRaycastLayer;
            }

            // The pickup trigger now lives on the item root, so it always shares
            // the root's pointer and inventory layer without synchronization.
        }

        private void TryBindPhysicalCollider()
        {
            if (physicalCollider != null || pipe == null || pipe.itemRigidbodyC == null)
                return;
            physicalCollider = pipe.itemRigidbodyC.GetComponent<BoxCollider>();
        }

        private void CacheRootColliderGeometry()
        {
            if (rootCollider == null)
                return;
            fullRootColliderCenter = rootCollider.center;
            fullRootColliderSize = rootCollider.size;
            rootColliderMouthEndY = fullRootColliderCenter.y -
                                     burnDirectionSign *
                                     fullRootColliderSize.y * 0.5f;
        }

        private void CachePickupColliderGeometry()
        {
            if (pickupCollider == null)
                return;
            fullPickupColliderCenter = pickupCollider.center;
            fullPickupColliderSize = pickupCollider.size;
            pickupColliderMouthEndY = fullPickupColliderCenter.y -
                                       burnDirectionSign *
                                       fullPickupColliderSize.y * 0.5f;
        }

        private void ApplyVisualState()
        {
            if (burnableBody != null)
            {
                Vector3 scale = fullBodyScale;
                scale.y = fullBodyScale.y * visibleRemaining01;
                burnableBody.localScale = scale;
            }

            if (burnFront != null)
            {
                burnFront.localPosition = Vector3.Lerp(
                    spentBurnFrontPosition,
                    fullBurnFrontPosition,
                    visibleRemaining01);
                bool burned = targetRemaining01 < .99999f || heat01 > 0f || inhaling;
                burnFront.gameObject.SetActive(available && (unlitTip == null || burned));
                if (unlitTip != null)
                {
                    unlitTip.localPosition = burnFront.localPosition;
                    unlitTip.gameObject.SetActive(available && !burned);
                }
            }

            if (band != null && burnFront != null)
            {
                bool frontBeforeBand = burnDirectionSign > 0f
                    ? burnFront.localPosition.y > bandThresholdY
                    : burnFront.localPosition.y < bandThresholdY;
                band.SetActive(available && frontBeforeBand);
            }

            ApplyPhysicalCollider(rootCollider);
            ApplyPhysicalCollider(physicalCollider);
            ApplyPickupCollider();
            ApplyAsh();
            ApplyEmber();
        }

        private void ApplyPhysicalCollider(BoxCollider collider)
        {
            if (collider == null || fullRootColliderSize.y <= 0f)
                return;
            float colliderRatio = Mathf.Max(
                MinimumPhysicalColliderRatio,
                visibleRemaining01);
            Vector3 size = fullRootColliderSize;
            size.y = fullRootColliderSize.y * colliderRatio;
            Vector3 center = fullRootColliderCenter;
            center.y = rootColliderMouthEndY +
                       burnDirectionSign * size.y * 0.5f;
            collider.size = size;
            collider.center = center;
        }

        private void ApplyPickupCollider()
        {
            if (pickupCollider == null || fullPickupColliderSize.y <= 0f)
                return;
            Vector3 size = fullPickupColliderSize;
            size.y = Mathf.Max(
                MinimumPickupLength,
                fullPickupColliderSize.y * visibleRemaining01);
            Vector3 center = fullPickupColliderCenter;
            center.y = pickupColliderMouthEndY +
                       burnDirectionSign * size.y * 0.5f;
            pickupCollider.size = size;
            pickupCollider.center = center;
        }

        private void ApplyEmber()
        {
            float activeHeat = available ? heat01 : 0f;
            Color surfaceColor;
            Color emissionColor;
            Color lightColor;

            if (activeHeat > 0f && inhaling)
            {
                surfaceColor = YellowColor;
                emissionColor = YellowEmission;
                lightColor = YellowLightColor;
            }
            else
            {
                if (activeHeat > 0f && !hasCoolingStartHeat)
                {
                    coolingStartHeat01 = activeHeat;
                    hasCoolingStartHeat = true;
                }

                CigarEmberCoolingPhase phase = CigarEmberMath.GetCoolingPhase(
                    activeHeat,
                    coolingStartHeat01);
                float progress = CigarEmberMath.GetPhaseProgress(
                    phase,
                    activeHeat,
                    coolingStartHeat01);
                Color darkColor = Color.Lerp(Color.black, emberBaseColor, 0.08f);
                if (phase == CigarEmberCoolingPhase.YellowToOrange)
                {
                    surfaceColor = Color.Lerp(
                        YellowColor,
                        OrangeColor,
                        progress);
                    emissionColor = Color.Lerp(
                        YellowEmission,
                        OrangeEmission,
                        progress);
                    lightColor = Color.Lerp(
                        YellowLightColor,
                        OrangeLightColor,
                        progress);
                }
                else if (phase == CigarEmberCoolingPhase.OrangeToRed)
                {
                    surfaceColor = Color.Lerp(
                        OrangeColor,
                        emberBaseColor,
                        progress);
                    emissionColor = Color.Lerp(
                        OrangeEmission,
                        emberBaseEmission,
                        progress);
                    lightColor = Color.Lerp(
                        OrangeLightColor,
                        RedLightColor,
                        progress);
                }
                else if (phase == CigarEmberCoolingPhase.RedToDark)
                {
                    surfaceColor = Color.Lerp(
                        emberBaseColor,
                        darkColor,
                        progress);
                    emissionColor = Color.Lerp(
                        emberBaseEmission,
                        Color.black,
                        progress);
                    lightColor = Color.Lerp(
                        RedLightColor,
                        Color.black,
                        progress);
                }
                else
                {
                    surfaceColor = darkColor;
                    emissionColor = Color.black;
                    lightColor = RedLightColor;
                }
            }

            if (emberRenderer != null && emberProperties != null)
            {
                emberRenderer.GetPropertyBlock(emberProperties);
                emberProperties.SetColor(ColorProperty, surfaceColor);
                emberProperties.SetColor(
                    EmissionColorProperty,
                    emissionColor);
                emberRenderer.SetPropertyBlock(emberProperties);
            }

            if (emberLight != null)
            {
                emberLight.color = lightColor;
                emberLight.intensity =
                    MaximumEmberLightIntensity * activeHeat;
                emberLight.enabled = activeHeat > 0.001f;
            }
        }
    }
}


// CigarWrapperState
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
public sealed class CigarWrapperState : MonoBehaviour
    {
        [SerializeField] private CigarWrapperVisual wrapperVisual;
        private ShipItem item;
        private bool rolling;
        private bool invalidLogged;
        public int FillCount { get { TobaccoRecipe recipe; return TryGetRecipe(out recipe) ? recipe.Count : -1; } }

        private void Awake()
        {
            item = GetComponent<ShipItem>();
            if (wrapperVisual == null) wrapperVisual = GetComponent<CigarWrapperVisual>();
        }
        private void Start() { SyncFromSavedState(); }
        public void BindVisual(CigarWrapperVisual visual) { wrapperVisual = visual; }

        public bool TryGetRecipe(out TobaccoRecipe recipe)
        {
            if (item == null) item = GetComponent<ShipItem>();
            recipe = default(TobaccoRecipe);
            return item != null && LeafStateRules.TryReadWrapper(item.health, item.amount, out recipe);
        }

        public bool CanInsert(ShipItemTobacco tobacco)
        {
            TobaccoRecipe recipe;
            if(item==null) item=GetComponent<ShipItem>();
            return !rolling && gameObject.activeInHierarchy && item != null && item.sold &&
                tobacco != null && tobacco.sold && tobacco.gameObject.activeInHierarchy &&
                PrefabReplacement.CanTransform(item) && PrefabReplacement.CanTransform(tobacco) &&
                TryGetRecipe(out recipe) && recipe.Count < 3 && recipe.TryAdd(tobacco);
        }

        public bool TryInsert(ShipItemTobacco tobacco)
        {
            if (!CanInsert(tobacco)) return false;
            TobaccoRecipe recipe;
            if (!TryGetRecipe(out recipe) || !recipe.TryAdd(tobacco)) return false;
            if(wrapperVisual==null || wrapperVisual.gameObject!=gameObject) wrapperVisual=GetComponent<CigarWrapperVisual>();
            // Presentation must be usable before committing the recipe or consuming tobacco.
            if(wrapperVisual==null)
            {
                if(!invalidLogged) RuntimeDiagnostics.Error("Cigar Wrapper "+GetInstanceID()+" has no visual component; tobacco was not consumed.");
                invalidLogged=true;return false;
            }
            if(!wrapperVisual.TrySetRecipe(recipe)) return false;
            item.health = recipe.Count;
            item.amount = TobaccoRecipeCodec.Encode(recipe);
            PrefabReplacement.ConsumeOwned(tobacco);
            SyncFromSavedState();
            if (UISoundPlayer.instance != null) UISoundPlayer.instance.PlayUISound(UISounds.itemInventoryIn, .5f, .5f);
            return true;
        }

        public bool TryRoll()
        {
            TobaccoRecipe recipe;
            if (rolling || !gameObject.activeInHierarchy || !TryGetRecipe(out recipe) || recipe.Count == 0) return false;
            rolling = true;
            bool replaced = PrefabReplacement.ReplaceOwnedItem(item, RuntimeConstants.CigarPrefabIndex,
                recipe.Count * 100f, -TobaccoRecipeCodec.Encode(recipe), Vector3.up * .10f,
                item.transform.rotation * Quaternion.Euler(0, 0, -90));
            if (!replaced) rolling = false;
            return replaced;
        }

        public void SyncFromSavedState()
        {
            if (item == null) item = GetComponent<ShipItem>();
            if (item == null) return;
            item.name = "Cigar Wrapper";
            TobaccoRecipe recipe;
            if (!TryGetRecipe(out recipe))
            {
                item.description = string.Empty;
                if (!invalidLogged) RuntimeDiagnostics.Error("Cigar Wrapper instance " + GetInstanceID() +
                    " has invalid count/recipe data; original values preserved and crafting disabled.");
                invalidLogged = true;
                return;
            }
            invalidLogged = false;
            if (wrapperVisual == null || wrapperVisual.gameObject != gameObject) wrapperVisual = GetComponent<CigarWrapperVisual>();
            if (wrapperVisual != null) wrapperVisual.SetRecipe(recipe);
            item.description = "Tobacco: " + recipe.Count + " / 3";
        }
    }
}


// CigarWrapperVisual
namespace TobaccoPotAndCigar.Runtime
{
    using System;
    using UnityEngine;
/// <summary>Wrapper filling presentation driven by the persisted ordered recipe.</summary>
    public sealed class CigarWrapperVisual : MonoBehaviour
    {
        [Serializable]
        public sealed class Filling
        {
            public Mesh mesh;
            public Material[] materials;
        }
        [SerializeField] private Filling[] fillings;
        [SerializeField] private Color[] tobaccoColors;
        private MaterialPropertyBlock properties;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private bool invalidLogged;
        private static CigarWrapperVisualData bundledData;
        private static Filling[] bundledFillings;
        private static readonly int ColorProperty=Shader.PropertyToID("_Color");

        public void Configure(Filling[] states,Color[] colors)
        {
            fillings=states; tobaccoColors=colors;
        }

        public static void BindBundleData(CigarWrapperVisualData data)
        {
            string error=null;
            if(data==null || !data.Validate(out error))
                throw new InvalidOperationException("Invalid wrapper visual bundle data: "+(data==null?"missing asset":error));
            bundledData=data;
            bundledFillings=data.GetFillings();
        }

        public static void ClearBundleData() { bundledData=null; bundledFillings=null; }

        private void Awake() { BindConfiguration(); }

        private void BindConfiguration()
        {
            if(bundledData!=null) Configure(bundledFillings,bundledData.tobaccoColors);
            // Never retain a renderer/filter from a different wrapper instance.
            meshFilter=GetComponent<MeshFilter>();
            meshRenderer=GetComponent<MeshRenderer>();
        }

        public static bool ValidateConfiguration(Filling[] states,Color[] colors,out string error)
        {
            if(states==null || states.Length!=4) { error="expected four filling states";return false; }
            if(colors==null || colors.Length!=5) { error="expected five tobacco colours";return false; }
            for(int count=0;count<4;count++)
            {
                Filling state=states[count];
                if(state==null || state.mesh==null || state.mesh.subMeshCount!=count+2 ||
                    state.materials==null || state.materials.Length!=count+2)
                { error="invalid mesh/material slots at filling state "+count;return false; }
                for(int i=0;i<state.materials.Length;i++)
                    if(state.materials[i]==null || state.materials[i].shader==null || state.mesh.GetIndexCount(i)==0)
                    { error="missing geometry/material at filling state "+count+", slot "+i;return false; }
            }
            error=null;return true;
        }

        public bool IsConfigured(out string error)
        {
            BindConfiguration();
            if(meshFilter==null || meshRenderer==null) {error="missing root mesh filter/renderer";return false;}
            return ValidateConfiguration(fillings,tobaccoColors,out error);
        }

        public void SetRecipe(TobaccoRecipe recipe)
        {
            TrySetRecipe(recipe);
        }

        public bool TrySetRecipe(TobaccoRecipe recipe)
        {
            string error;
            if(!IsConfigured(out error))
            {
                if(!invalidLogged) RuntimeDiagnostics.Error("Cigar Wrapper "+GetInstanceID()+" cannot display tobacco: "+error+". Insertion is disabled until its visual assets are valid.");
                invalidLogged=true;return false;
            }
            invalidLogged=false;
            if(properties==null) properties=new MaterialPropertyBlock();
            Filling filling=fillings[recipe.Count];
            meshFilter.sharedMesh=filling.mesh;
            meshRenderer.sharedMaterials=filling.materials;
            for(int index=0;index<filling.materials.Length;index++)
            {
                properties.Clear();
                if(index>=2)
                {
                    Color color=tobaccoColors[(int)recipe.GetAt(index-2)-1];
                    properties.SetColor(ColorProperty,color);
                }
                meshRenderer.SetPropertyBlock(properties,index);
            }
            LODGroup lod=GetComponent<LODGroup>();
            if(lod!=null) lod.RecalculateBounds();
            return true;
        }
    }
}


// CigarWrapperVisualData
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
/// <summary>Explicit bundle asset used to bind wrapper presentation before registration.</summary>
    public sealed class CigarWrapperVisualData : ScriptableObject
    {
        // Native Unity references survive loading this assembly through BepInEx.
        // Do not serialize the nested managed Filling[] here: the game can load
        // the ScriptableObject without restoring that array.
        public Mesh[] meshes;
        public Material[] emptyMaterials;
        public Material fillerMaterial;
        public Color[] tobaccoColors;

        public CigarWrapperVisual.Filling[] GetFillings()
        {
            if (meshes == null || meshes.Length != 4 || emptyMaterials == null ||
                emptyMaterials.Length != 2 || fillerMaterial == null) return null;
            var states = new CigarWrapperVisual.Filling[4];
            for (int count = 0; count < states.Length; count++)
            {
                var materials = new Material[count + 2];
                materials[0] = emptyMaterials[0];
                materials[1] = emptyMaterials[1];
                for (int i = 2; i < materials.Length; i++) materials[i] = fillerMaterial;
                states[count] = new CigarWrapperVisual.Filling { mesh = meshes[count], materials = materials };
            }
            return states;
        }

        public bool Validate(out string error)
        {
            var states = GetFillings();
            if (states == null)
            {
                error = "native wrapper references: meshes=" + (meshes == null ? -1 : meshes.Length) +
                    ", base materials=" + (emptyMaterials == null ? -1 : emptyMaterials.Length) +
                    ", filler=" + (fillerMaterial != null) +
                    ", colours=" + (tobaccoColors == null ? -1 : tobaccoColors.Length);
                return false;
            }
            return CigarWrapperVisual.ValidateConfiguration(states, tobaccoColors, out error);
        }
    }
}
