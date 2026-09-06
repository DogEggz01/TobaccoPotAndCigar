using System;
using System.Collections.Generic;
using System.IO;
using TobaccoPotAndCigar.Runtime;
using UnityEngine;

namespace TobaccoPotAndCigar.Prefabs
{
    internal static class CigarAssetBundle
    {
        internal const string BundleName = "dogeggz.cigar.assets";

        private static readonly Dictionary<int, GameObject> Prefabs =
            new Dictionary<int, GameObject>();
        private static AssetBundle bundle;

        internal static bool IsLoaded
        {
            get { return bundle != null && Prefabs.Count == 4; }
        }

        internal static void Load(string pluginDirectory)
        {
            if (IsLoaded)
                return;

            string path = Path.Combine(pluginDirectory, "assets", BundleName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Cigar AssetBundle is missing.", path);

            bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
                throw new InvalidOperationException("Unity could not load " + path);

            Prefabs.Clear();
            GameObject[] loaded = bundle.LoadAllAssets<GameObject>();
            for (int i = 0; i < loaded.Length; i++)
            {
                SaveablePrefab saveable = loaded[i].GetComponent<SaveablePrefab>();
                if (saveable == null)
                    continue;
                int index = saveable.prefabIndex;
                if (index < RuntimeConstants.TobaccoPotPrefabIndex ||
                    index > RuntimeConstants.DriedTobaccoLeafPrefabIndex)
                    continue;
                if (Prefabs.ContainsKey(index))
                    throw new InvalidOperationException(
                        "AssetBundle contains duplicate prefab index " + index + ".");
                Prefabs.Add(index, loaded[i]);
            }

            for (int index = RuntimeConstants.TobaccoPotPrefabIndex;
                 index <= RuntimeConstants.DriedTobaccoLeafPrefabIndex;
                 index++)
            {
                if (!Prefabs.ContainsKey(index))
                    throw new InvalidOperationException(
                        "AssetBundle is missing prefab index " + index + ".");
            }
        }

        internal static GameObject GetPrefab(int index)
        {
            GameObject prefab;
            return Prefabs.TryGetValue(index, out prefab) ? prefab : null;
        }

        internal static void Unload()
        {
            Prefabs.Clear();
            if (bundle != null)
                bundle.Unload(false);
            bundle = null;
        }
    }
}
