using System;
using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
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
