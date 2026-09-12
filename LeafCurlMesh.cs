using System;
using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    /// <summary>Imported vertex correspondence is baked by the authoring builder.</summary>
    [Serializable]
    public sealed class LeafCurlMesh
    {
        [SerializeField] private MeshFilter filter;
        [SerializeField] private Vector3[] driedVertices;
        [NonSerialized] private Mesh source;
        [NonSerialized] private Mesh instance;
        [NonSerialized] private Vector3[] freshVertices;
        [NonSerialized] private Vector3[] workingVertices;

        public LeafCurlMesh(MeshFilter newFilter, Vector3[] target)
        {
            filter = newFilter;
            driedVertices = target;
        }

        public void Apply(float progress)
        {
            if (filter == null || filter.sharedMesh == null)
                return;
            if (instance == null)
            {
                source = filter.sharedMesh;
                if (driedVertices == null || driedVertices.Length != source.vertexCount)
                    throw new InvalidOperationException("Leaf curl target does not match its source mesh.");
                freshVertices = source.vertices;
                workingVertices = new Vector3[freshVertices.Length];
                instance = UnityEngine.Object.Instantiate(source);
                instance.name = source.name + " (leaf instance)";
                instance.MarkDynamic();
                filter.sharedMesh = instance;
            }
            // A completed legacy conversion can replace the root mesh before cleanup.
            if (filter.sharedMesh != instance)
                return;
            for (int i = 0; i < workingVertices.Length; i++)
                workingVertices[i] = Vector3.LerpUnclamped(freshVertices[i], driedVertices[i], progress);
            instance.vertices = workingVertices;
            instance.RecalculateNormals();
            instance.RecalculateTangents();
            instance.RecalculateBounds();
        }

        public void Release()
        {
            if (instance == null)
                return;
            if (filter != null && filter.sharedMesh == instance)
                filter.sharedMesh = source;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(instance);
            else
                UnityEngine.Object.DestroyImmediate(instance);
            instance = null;
            freshVertices = null;
            workingVertices = null;
        }
    }
}
