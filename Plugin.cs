using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TobaccoPotAndCigar.Prefabs;
using TobaccoPotAndCigar.Runtime;
using TobaccoPotAndCigar.Shops;
using TobaccoPotAndCigar.Smoking;

namespace TobaccoPotAndCigar
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(
        Compatibility.RadRefinementsCompatibility.PluginGuid,
        BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "DogEggz.Cigar";
        public const string PluginName = "Tobacco pot and cigar";
        public const string PluginVersion = "1.0.1";

        internal static ManualLogSource LogSource { get; private set; }
        internal static string PluginDirectory { get; private set; }

        private Harmony harmony;

        private void Awake()
        {
            LogSource = Logger;
            PluginDirectory = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            RuntimeDiagnostics.InfoSink = message => Logger.LogInfo(message);
            RuntimeDiagnostics.WarningSink = message => Logger.LogWarning(message);
            RuntimeDiagnostics.ErrorSink = message => Logger.LogError(message);

            CigarAssetBundle.Load(PluginDirectory);
            harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            ShopPlacement.ResetDiagnostics();

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            CigarEffectService.Reset();
            Compatibility.RadRefinementsCompatibility.Reset();
            CigarPrefabRegistrar.Reset();
            CigarAssetBundle.Unload();
            ShopPlacement.ResetDiagnostics();
            RuntimeDiagnostics.Reset();
            PluginDirectory = null;
            LogSource = null;
        }
    }
}
