using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

namespace BettrFG.uGUI
{
    // �� GradientImage ���������������������������������������������������������
    public class GradientImage : BaseMeshEffect
    {
        public GradientImage(IntPtr ptr) : base(ptr) { }

        public bool Vertical = false;

        public Color Left = new Color(0f, 1f, 0.1f, 0f);
        public Color LeftMid = new Color(0f, 1f, 0.1f, 0.18f);
        public Color RightMid = new Color(0f, 1f, 0.1f, 0.18f);
        public Color Right = new Color(0f, 1f, 0.1f, 0f);

        public float LeftMidT = 0.33f;
        public float RightMidT = 0.66f;

        public Color TopColor = Color.white;
        public Color BottomColor = Color.black;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            int count = vh.currentVertCount;
            if (count == 0) return;

            var vert = new UIVertex();

            if (Vertical)
            {
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    if (vert.position.y < minY) minY = vert.position.y;
                    if (vert.position.y > maxY) maxY = vert.position.y;
                }
                float h = maxY - minY;
                if (h < 0.001f) return;
                for (int i = 0; i < count; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    float t = 1f - (vert.position.y - minY) / h;
                    vert.color = Color.Lerp(TopColor, BottomColor, t);
                    vh.SetUIVertex(vert, i);
                }
            }
            else if (count == 4)
            {
                var bl = new UIVertex(); var tl = new UIVertex(); var tr = new UIVertex(); var br = new UIVertex();
                vh.PopulateUIVertex(ref bl, 0);
                vh.PopulateUIVertex(ref tl, 1);
                vh.PopulateUIVertex(ref tr, 2);
                vh.PopulateUIVertex(ref br, 3);
                vh.Clear();
                float[] stops = { 0f, LeftMidT, RightMidT, 1f };
                for (int s = 0; s < 4; s++)
                {
                    float t = stops[s];
                    var bot = bl; var top = tl;
                    bot.position = Vector3.Lerp(bl.position, br.position, t);
                    top.position = Vector3.Lerp(tl.position, tr.position, t);
                    bot.uv0 = Vector4.Lerp(bl.uv0, br.uv0, t);
                    top.uv0 = Vector4.Lerp(tl.uv0, tr.uv0, t);
                    bot.color = top.color = SampleHorizontal(t);
                    vh.AddVert(bot);
                    vh.AddVert(top);
                }
                for (int s = 0; s < 3; s++)
                {
                    int b0 = s * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                    vh.AddTriangle(b0, t0, t1);
                    vh.AddTriangle(t1, b1, b0);
                }
            }
            else
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    if (vert.position.x < minX) minX = vert.position.x;
                    if (vert.position.x > maxX) maxX = vert.position.x;
                }
                float w = maxX - minX;
                if (w < 0.001f) return;
                for (int i = 0; i < count; i++)
                {
                    vh.PopulateUIVertex(ref vert, i);
                    float t = (vert.position.x - minX) / w;
                    vert.color = SampleHorizontal(t);
                    vh.SetUIVertex(vert, i);
                }
            }
        }

        private Color32 SampleHorizontal(float t)
        {
            if (t <= 0f) return Left;
            if (t >= 1f) return Right;
            if (t < LeftMidT) return Color.Lerp(Left, LeftMid, t / LeftMidT);
            if (t < RightMidT) return Color.Lerp(LeftMid, RightMid, (t - LeftMidT) / (RightMidT - LeftMidT));
            return Color.Lerp(RightMid, Right, (t - RightMidT) / (1f - RightMidT));
        }
    }
}
