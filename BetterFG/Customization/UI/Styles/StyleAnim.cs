using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BetterFG.Utilities;

namespace BetterFG.Customization.UI.Styles
{
    internal static class StyleAnim
    {
        static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>
        {
            { "RectTransform", typeof(RectTransform) },
            { "Transform", typeof(Transform) },
            { "GameObject", typeof(GameObject) },
            { "UnityEngine.CanvasGroup", typeof(CanvasGroup) },
            { "UnityEngine.UI.Image", typeof(Image) },
            { "UnityEngine.UI.RawImage", typeof(RawImage) },
            { "TMPro.TextMeshProUGUI", typeof(TextMeshProUGUI) },
        };

        public static Animation Load(StyleAssets assets, GameObject root, string file)
        {
            var anim = root.GetComponent<Animation>();
            if (anim == null) anim = root.AddComponent<Animation>();
            anim.playAutomatically = false;
            var json = assets.Text("anims/" + file + ".json");
            if (json == null) { Plugin.Log.LogWarning($"no anim {file} in the style"); return anim; }
            foreach (var c in JsonUtil.Parse(json).GetProperty("clips").EnumerateArray())
            {
                var clip = new AnimationClip { legacy = true, name = c.GetProperty("name").GetString() };
                clip.wrapMode = c.GetProperty("loop").GetBoolean() ? WrapMode.Loop : WrapMode.Once;
                foreach (var t in c.GetProperty("tracks").EnumerateArray())
                {
                    if (!Types.TryGetValue(t.GetProperty("type").GetString(), out var ty)) continue;
                    var prop = t.GetProperty("prop").GetString();
                    if (ty == typeof(TextMeshProUGUI) && prop.StartsWith("m_Color")) prop = prop.Replace("m_Color", "m_fontColor");
                    var curve = new AnimationCurve();
                    foreach (var k in t.GetProperty("k").EnumerateArray()) curve.AddKey(k[0].GetSingle(), k[1].GetSingle());
                    if (prop == "m_IsActive" || prop == "m_Enabled")
                        for (int i = 0; i < curve.length; i++)
                        {
                            var key = curve[i];
                            key.inTangent = key.outTangent = float.PositiveInfinity;
                            curve.MoveKey(i, key);
                        }
                    clip.SetCurve(t.GetProperty("path").GetString(), Il2CppType.From(ty), prop, curve);
                }
                anim.AddClip(clip, clip.name);
                if (anim.clip == null) anim.clip = clip;
            }
            return anim;
        }
    }
}
