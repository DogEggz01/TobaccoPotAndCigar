using System.Collections.Generic;
using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    /// <summary>Deposited ash follows the shared D-shaped basin, including its sloping floor.</summary>
    public sealed class AshPileVisual : MonoBehaviour
    {
        private Mesh ownedMesh;
        private float lastFill = -1;
        // Optional sampled basin floor for shapes such as the thicker glass tray.
        [SerializeField] private float[] floorHeights;
        public const int FloorGridSize = 65;

        public void ConfigureFloor(float[] heights)
        {
            floorHeights = heights;
            lastFill = -1;
        }

        public void SetFill(float fill)
        {
            fill = Mathf.Clamp01(fill);
            gameObject.SetActive(fill > .000001f);
            if (fill <= .000001f || fill == lastFill) return;
            if (ownedMesh == null) ownedMesh = new Mesh { name = "Deposited Ash (instance)" };
            WriteMesh(ownedMesh, fill, floorHeights);
            GetComponent<MeshFilter>().sharedMesh = ownedMesh;
            lastFill = fill;
        }

        private void OnDestroy()
        {
            if (ownedMesh == null) return;
            if (Application.isPlaying) Destroy(ownedMesh); else DestroyImmediate(ownedMesh);
        }

        public static Mesh CreateMesh(float fill, float[] floorHeights = null)
        {
            var mesh = new Mesh { name = "Deposited Ash" };
            WriteMesh(mesh, Mathf.Clamp01(fill), floorHeights);
            return mesh;
        }

        private static void WriteMesh(Mesh mesh, float fill, float[] floorHeights)
        {
            const int segments = 64, rings = 5;
            const float centre = .05f, outerRadius = .108f, divider = .002f;
            int half = 1 + segments * rings;
            var vertices = new Vector3[half * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new List<int>();
            float spread = Mathf.Lerp(.16f, 1, Mathf.Sqrt(fill));
            float peak = .015f * Mathf.Pow(fill, .6f);
            SetVertex(vertices, uv, 0, half, centre, 0, peak, floorHeights);
            for (int ring = 0; ring < rings; ring++)
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                float dx = Mathf.Cos(angle), dz = Mathf.Sin(angle);
                float reach = -centre * dx + Mathf.Sqrt(outerRadius * outerRadius - centre * centre * dz * dz);
                if (dx < 0) reach = Mathf.Min(reach, (divider - centre) / dx);
                // Broad irregularity stays inside both the divider and curved wall.
                reach *= .982f + .012f * Mathf.Sin(i * 1.7f);
                float t = (ring + 1f) / rings;
                float radius = reach * spread * t;
                float mound = peak * Mathf.Pow(1 - t * t, 1.25f) * (1 + .08f * Mathf.Sin(i * 2.3f + ring));
                int current = 1 + ring * segments + i;
                int next = 1 + ring * segments + (i + 1) % segments;
                SetVertex(vertices, uv, current, half, centre + dx * radius, dz * radius, mound, floorHeights);
                if (ring == 0) AddFace(triangles, 0, current, next, half);
                else
                {
                    AddFace(triangles, current - segments, current, next, half);
                    AddFace(triangles, current - segments, next, next - segments, half);
                }
                if (ring == rings - 1)
                {
                    triangles.AddRange(new[] { current, current + half, next, next, current + half, next + half });
                }
            }
            mesh.Clear(); mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
        }

        private static void AddFace(List<int> triangles, int a, int b, int c, int half)
        {
            triangles.AddRange(new[] { a, b, c, c + half, b + half, a + half });
        }

        private static void SetVertex(Vector3[] vertices, Vector2[] uv, int i, int half, float x, float z, float mound, float[] floorHeights)
        {
            float radius = Mathf.Sqrt(x * x + z * z);
            // Same lathed floor profile as the Blender basin cutter (all ten styles).
            float floor = radius <= .075f ? .013f : radius <= .098f
                ? Mathf.Lerp(.013f, .016f, (radius - .075f) / .023f)
                : Mathf.Lerp(.016f, .023f, (radius - .098f) / .012f);
            if (floorHeights != null && floorHeights.Length == FloorGridSize * FloorGridSize)
            {
                float gx = Mathf.Clamp01(x / .11f) * (FloorGridSize - 1);
                float gz = Mathf.Clamp01((z + .11f) / .22f) * (FloorGridSize - 1);
                int ix = Mathf.Min((int)gx, FloorGridSize - 2), iz = Mathf.Min((int)gz, FloorGridSize - 2);
                float a = Mathf.Lerp(floorHeights[iz * FloorGridSize + ix], floorHeights[iz * FloorGridSize + ix + 1], gx - ix);
                float b = Mathf.Lerp(floorHeights[(iz + 1) * FloorGridSize + ix], floorHeights[(iz + 1) * FloorGridSize + ix + 1], gx - ix);
                floor = Mathf.Lerp(a, b, gz - iz);
            }
            const float cos = .984807753f, sin = .173648178f;
            Vector3 position = new Vector3(cos * x - sin * z, floor, -sin * x - cos * z);
            vertices[i] = position + Vector3.up * (.00065f + mound);
            vertices[i + half] = position + Vector3.up * .00035f;
            // Keep every sample within the cigar-ash atlas island, away from black padding.
            uv[i] = uv[i + half] = new Vector2(.225f + (x - .05f) * 1.3f, .21f + z * 1.3f);
        }
    }
}
