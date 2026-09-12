using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
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
