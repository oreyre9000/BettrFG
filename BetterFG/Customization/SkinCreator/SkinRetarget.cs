using System.Text;
using BetterFG.Services;
using UnityEngine;

namespace BetterFG.Customization.SkinCreator
{
    internal static class SkinRetarget
    {
        public static string Friendly(string boneName)
        {
            var parts = boneName.Replace("_jnt", "").Split('_');
            var sb = new StringBuilder();
            string side = null;
            foreach (var part in parts)
            {
                if (part == "L") side = "skin_creator.side_left";
                else if (part == "R") side = "skin_creator.side_right";
                else if (part == "C") continue;
                else
                {
                    if (sb.Length > 0) sb.Append(' ');
                    for (int i = 0; i < part.Length; i++)
                    {
                        if (i > 0 && char.IsUpper(part[i]) && !char.IsUpper(part[i - 1])) sb.Append(' ');
                        sb.Append(part[i]);
                    }
                }
            }
            return side == null ? sb.ToString() : LocalizationService.Get(side) + " " + sb;
        }

        public static void SetParts(Mesh mesh, ImportedModel m)
        {
            mesh.subMeshCount = m.Parts.Length;
            for (int i = 0; i < m.Parts.Length; i++) mesh.SetTriangles(m.Parts[i].Tris, i);
        }

        static Shader _shader;

        static Shader GameShader()
        {
            if (_shader != null) return _shader;
            foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
                if (s != null && s.name == "Mediatonic/ENV_Standard_Uber") return _shader = s;
            return _shader = Shader.Find("Standard");
        }

        static Texture2D _matte;

        public static Material[] Materials(ImportedModel m)
        {
            var shader = GameShader();
            if (_matte == null)
            {
                _matte = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _matte.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                _matte.Apply();
                Object.DontDestroyOnLoad(_matte);
            }
            var mats = new Material[m.Parts.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = new Material(shader) { color = m.Parts[i].Col, mainTexture = m.Parts[i].Tex };
                if (!mats[i].HasProperty("_MetallicSmoothness")) continue;
                mats[i].SetTexture("_MetallicSmoothness", _matte);
                mats[i].SetColor("_RimColor", Color.black);
            }
            return mats;
        }
    }
}
