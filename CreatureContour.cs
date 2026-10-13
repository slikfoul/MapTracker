using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    // Offset copies retain the sprite UVs, so transparent pixels remain transparent.
    // No silhouette readback, extra textures or per-frame image processing is needed.
    public sealed class CreatureContour : BaseMeshEffect
    {
        private readonly List<UIVertex> original = new List<UIVertex>();
        private int thickness = 1;
        internal int Thickness
        {
            get => thickness;
            set { int next = Mathf.Clamp(value, 1, 4); if (next == thickness) return; thickness = next; if (graphic != null) graphic.SetVerticesDirty(); }
        }
        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive()) return;
            original.Clear(); mesh.GetUIVertexStream(original);
            if (original.Count == 0) return;
            mesh.Clear();
            for (int radius = 1; radius <= thickness; radius++) Layer(mesh, radius, new Color32(0, 0, 0, 255));
            for (int i = 0; i < original.Count; i++) mesh.AddVert(original[i]);
            int first = mesh.currentVertCount - original.Count;
            for (int i = 0; i < original.Count; i += 3) mesh.AddTriangle(first + i, first + i + 1, first + i + 2);
        }
        private void Layer(VertexHelper mesh, float radius, Color32 tint)
        {
            for (int direction = 0; direction < 8; direction++)
            {
                float angle = direction * Mathf.PI / 4;
                var offset = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
                int first = mesh.currentVertCount;
                for (int i = 0; i < original.Count; i++)
                {
                    var vertex = original[i]; vertex.position += offset;
                    vertex.color = new Color32(tint.r, tint.g, tint.b, (byte)(tint.a * vertex.color.a / 255));
                    mesh.AddVert(vertex);
                }
                for (int i = 0; i < original.Count; i += 3) mesh.AddTriangle(first + i, first + i + 1, first + i + 2);
            }
        }
    }
}
