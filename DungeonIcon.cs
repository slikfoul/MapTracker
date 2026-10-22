using System;
using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    internal sealed class DungeonIcon
    {
        private DungeonGraphic graphic;
        internal Graphic Graphic => graphic;
        internal static bool HasSymbol(CatalogEntry entry)
            => entry != null && (entry.dungeon || entry.key == "location:AshlandRuins" && entry.prefab == "AshlandRuins");
        internal void Update(Minimap.PinData pin, string prefab)
        {
            if (graphic != null && graphic.transform.parent != pin.m_uiElement) { UnityEngine.Object.Destroy(graphic.gameObject); graphic = null; }
            if (graphic == null)
            {
                var root = new GameObject("DungeonEntranceIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(DungeonGraphic));
                root.layer = pin.m_uiElement.gameObject.layer; root.transform.SetParent(pin.m_uiElement, false);
                root.transform.SetAsFirstSibling();
                var rect = (RectTransform)root.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                graphic = root.GetComponent<DungeonGraphic>(); graphic.raycastTarget = false;
                graphic.Kind = Kind(prefab);
            }
            if (pin.m_iconElement != null)
            {
                pin.m_iconElement.enabled = false;
                graphic.color = pin.m_iconElement.color;
            }
        }
        internal static int Kind(string prefab)
        {
            string name = (prefab ?? "").ToLowerInvariant();
            if (name == "ashlandruins") return 4;
            if (name.Contains("boss") || name.Contains("fortress") || name.Contains("tower")) return 3;
            if (name.Contains("mistlands") || name.Contains("mine")) return 2;
            if (name.Contains("cave") || name.Contains("hole") || name.Contains("morgen") || name == "troll") return 1;
            return 0;
        }
    }
    // Code-native icon silhouettes: outlined arch, cave, crossed picks, tower and ruined arch.
    public sealed class DungeonGraphic : MaskableGraphic
    {
        internal int Kind;
        private Vector2 center;
        private float scale;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = GetPixelAdjustedRect(); center = rect.center;
            scale = Mathf.Min(rect.width, rect.height) * 0.47f;
            Color dark = new Color(0.08f, 0.07f, 0.06f, color.a);
            if (Kind == 1)
            {
                Shape(mesh, dark, -1,-.9f, -.85f,.25f, -.25f,1, .25f,.8f, .95f,.3f, 1,-.9f);
                Shape(mesh, color, -.84f,-.74f, -.69f,.17f, -.22f,.81f, .18f,.64f, .79f,.21f, .84f,-.74f);
                Shape(mesh, dark, -.46f,-.75f, -.35f,.08f, 0,.39f, .35f,.08f, .46f,-.75f);
            }
            else if (Kind == 2)
            {
                Bar(mesh, dark, -.8f,-.8f, .72f,.71f, .28f); Bar(mesh, dark, .8f,-.8f, -.72f,.71f, .28f);
                Bar(mesh, color, -.75f,-.75f, .65f,.65f, .13f); Bar(mesh, color, .75f,-.75f, -.65f,.65f, .13f);
                Bar(mesh, dark, .15f,.96f, 1,.08f, .32f); Bar(mesh, dark, -.15f,.96f, -1,.08f, .32f);
                Bar(mesh, color, .17f,.91f, .93f,.11f, .16f); Bar(mesh, color, -.17f,.91f, -.93f,.11f, .16f);
            }
            else if (Kind == 3)
            {
                Shape(mesh, dark, -.88f,-1, -.88f,.62f, .88f,.62f, .88f,-1);
                Shape(mesh, dark, -.88f,.6f, -.88f,.95f, -.36f,.95f, -.36f,.6f);
                Shape(mesh, dark, .36f,.6f, .36f,.95f, .88f,.95f, .88f,.6f);
                Shape(mesh, color, -.7f,-.83f, -.7f,.45f, .7f,.45f, .7f,-.83f);
                Shape(mesh, color, -.7f,.4f, -.7f,.77f, -.53f,.77f, -.53f,.4f);
                Shape(mesh, color, .53f,.4f, .53f,.77f, .7f,.77f, .7f,.4f);
                Shape(mesh, dark, -.27f,-.85f, -.27f,-.22f, 0,-.03f, .27f,-.22f, .27f,-.85f);
            }
            else if (Kind == 4)
            {
                // Uneven stone supports and a broken gable distinguish ruins from dungeon entrances.
                Shape(mesh, dark, -.87f,-.96f, -.87f,.33f, -.39f,.33f, -.39f,-.96f);
                Shape(mesh, dark, .39f,-.96f, .39f,.2f, .87f,.2f, .87f,-.96f);
                Shape(mesh, dark, -.98f,.27f, -.12f,1f, .04f,.7f, -.68f,.07f);
                Shape(mesh, dark, .14f,.69f, .39f,.78f, .98f,.21f, .69f,.01f);
                Shape(mesh, color, -.73f,-.82f, -.73f,.25f, -.53f,.25f, -.53f,-.82f);
                Shape(mesh, color, .53f,-.82f, .53f,.12f, .73f,.12f, .73f,-.82f);
                Shape(mesh, color, -.78f,.25f, -.16f,.79f, -.13f,.68f, -.67f,.22f);
                Shape(mesh, color, .32f,.61f, .38f,.61f, .77f,.23f, .69f,.19f);
                Shape(mesh, dark, -.24f,-.98f, -.19f,-.67f, .03f,-.73f, .08f,-.98f);
                Shape(mesh, color, -.12f,-.86f, -.1f,-.81f, -.04f,-.83f, -.02f,-.86f);
            }
            else
            {
                Shape(mesh, dark, -.92f,-1, -.92f,.44f, -.52f,.88f, 0,1, .52f,.88f, .92f,.44f, .92f,-1);
                Shape(mesh, color, -.73f,-.82f, -.73f,.36f, -.43f,.71f, 0,.83f, .43f,.71f, .73f,.36f, .73f,-.82f);
                Shape(mesh, dark, -.39f,-.85f, -.39f,.17f, -.2f,.37f, 0,.43f, .2f,.37f, .39f,.17f, .39f,-.85f);
                Bar(mesh, color, 0,-.64f, 0,.16f, .08f);
                Bar(mesh, color, -.25f,-.22f, .25f,-.22f, .08f);
            }
        }
        private void Shape(VertexHelper mesh, Color shade, params float[] coordinates)
        {
            int start = mesh.currentVertCount;
            for (int i = 0; i < coordinates.Length; i += 2) mesh.AddVert(center + new Vector2(coordinates[i], coordinates[i+1]) * scale, shade, Vector2.zero);
            for (int i = 1; i < coordinates.Length / 2 - 1; i++) mesh.AddTriangle(start, start+i, start+i+1);
        }
        private void Bar(VertexHelper mesh, Color shade, float x1, float y1, float x2, float y2, float width)
        {
            Vector2 a = new Vector2(x1, y1), b = new Vector2(x2, y2);
            Vector2 normal = new Vector2(-(b-a).y, (b-a).x).normalized * width * .5f;
            Shape(mesh, shade, (a+normal).x,(a+normal).y, (b+normal).x,(b+normal).y, (b-normal).x,(b-normal).y, (a-normal).x,(a-normal).y);
        }
    }
}
