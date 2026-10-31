using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BetterFG.Customization.UI.Styles
{
    public static class StyleLayout
    {
        static readonly Dictionary<string, Type> _types = new Dictionary<string, Type>();
        static readonly Dictionary<string, string> Renamed = new Dictionary<string, string>
        {
            { "AnimRotateObject", "Levels.Animations.AnimRotateObject" },
        };

        struct Pending { public Component C; public Type T; public JsonElement P; }

        public static GameObject Build(StyleAssets assets, string layout, Transform parent)
        {
            var j = assets.Layout(layout);
            var pend = new List<Pending>();
            var root = Node(j, parent, pend, true);
            foreach (var p in pend) Apply(assets, p, root.transform);
            return root;
        }

        static Type T(string fullName)
        {
            if (_types.TryGetValue(fullName, out var t)) return t;
            if (Renamed.TryGetValue(fullName, out var rn)) return _types[fullName] = T(rn);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var x = asm.GetType(fullName, false) ?? asm.GetType("Il2Cpp" + fullName, false);
                if (x != null) return _types[fullName] = x;
            }
            Plugin.Log.LogWarning($"style layout wants {fullName}, the game doesn't have it anymore");
            return _types[fullName] = null;
        }

        static float F(JsonElement e) => e.GetSingle();

        static GameObject Node(JsonElement j, Transform parent, List<Pending> pend, bool isRoot)
        {
            var go = new GameObject(j.GetProperty("n").GetString());
            if (isRoot) go.SetActive(false);
            go.layer = parent != null ? parent.gameObject.layer : 5;
            if (j.TryGetProperty("r", out var r))
            {
                var rt = go.AddComponent<RectTransform>();
                rt.SetParent(parent, false);
                rt.anchorMin = new Vector2(F(r[0]), F(r[1]));
                rt.anchorMax = new Vector2(F(r[2]), F(r[3]));
                rt.pivot = new Vector2(F(r[4]), F(r[5]));
                rt.anchoredPosition = new Vector2(F(r[6]), F(r[7]));
                rt.sizeDelta = new Vector2(F(r[8]), F(r[9]));
            }
            else
            {
                go.transform.SetParent(parent, false);
                if (j.TryGetProperty("lp", out var lp)) go.transform.localPosition = new Vector3(F(lp[0]), F(lp[1]), F(lp[2]));
            }
            if (j.TryGetProperty("q", out var q)) go.transform.localRotation = new Quaternion(F(q[0]), F(q[1]), F(q[2]), F(q[3]));
            if (j.TryGetProperty("s", out var s)) go.transform.localScale = new Vector3(F(s[0]), F(s[1]), F(s[2]));
            if (!isRoot && j.TryGetProperty("a", out var a) && a.GetInt32() == 0) go.SetActive(false);
            if (j.TryGetProperty("c", out var cs))
            {
                foreach (var c in cs.EnumerateArray())
                {
                    if (c.TryGetProperty("x", out _)) continue;
                    var t = T(c.GetProperty("t").GetString());
                    if (t == null) continue;
                    var comp = go.AddComponent(Il2CppType.From(t));
                    if (comp == null) continue;
                    var typed = (Component)Activator.CreateInstance(t, comp.Pointer);
                    if (c.TryGetProperty("en", out var en) && en.GetInt32() == 0)
                    {
                        var b = comp.TryCast<Behaviour>();
                        if (b != null) b.enabled = false;
                    }
                    if (c.TryGetProperty("p", out var p)) pend.Add(new Pending { C = typed, T = t, P = p });
                }
            }
            if (j.TryGetProperty("k", out var ks)) foreach (var k in ks.EnumerateArray()) Node(k, go.transform, pend, false);
            return go;
        }

        static void Apply(StyleAssets assets, Pending p, Transform root)
        {
            foreach (var kv in p.P.EnumerateObject())
            {
                var prop = p.T.GetProperty(kv.Name, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanWrite) continue;
                object v;
                try { v = Conv(assets, kv.Value, prop.PropertyType, root); }
                catch (Exception e) { Plugin.Log.LogWarning($"style {p.T.Name}.{kv.Name} didn't convert: {e.Message}"); continue; }
                if (v == null && kv.Value.ValueKind != JsonValueKind.Null) continue;
                try { prop.SetValue(p.C, v); }
                catch (Exception e) { Plugin.Log.LogWarning($"style {p.T.Name}.{kv.Name} refused the value: {(e.InnerException ?? e).Message}"); }
            }
        }

        public static Transform FindPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            var p = path;
            while (true)
            {
                if (p == root.name) return root;
                if (p.StartsWith(root.name + "/", StringComparison.Ordinal))
                {
                    var hit = root.Find(p.Substring(root.name.Length + 1));
                    if (hit != null) return hit;
                }
                var t = root.Find(p);
                if (t != null) return t;
                int i = p.IndexOf('/');
                if (i < 0) return null;
                p = p.Substring(i + 1);
            }
        }

        static object Conv(StyleAssets assets, JsonElement e, Type t, Transform root)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Null: return null;
                case JsonValueKind.String: return t == typeof(string) ? e.GetString() : null;
                case JsonValueKind.True:
                case JsonValueKind.False: return t == typeof(bool) ? e.GetBoolean() : null;
                case JsonValueKind.Number:
                    if (t.IsEnum) return Enum.ToObject(t, e.GetInt64());
                    if (t == typeof(float)) return e.GetSingle();
                    if (t == typeof(int)) return e.GetInt32();
                    if (t == typeof(uint)) return e.GetUInt32();
                    if (t == typeof(byte)) return (byte)e.GetInt32();
                    if (t == typeof(double)) return e.GetDouble();
                    return Convert.ChangeType(e.GetDouble(), t, CultureInfo.InvariantCulture);
                case JsonValueKind.Array:
                    if (t == typeof(Vector2)) return new Vector2(F(e[0]), F(e[1]));
                    if (t == typeof(Vector3)) return new Vector3(F(e[0]), F(e[1]), e.GetArrayLength() > 2 ? F(e[2]) : 0f);
                    if (t == typeof(Vector4)) return new Vector4(F(e[0]), F(e[1]), F(e[2]), F(e[3]));
                    if (t == typeof(Color)) return new Color(F(e[0]), F(e[1]), F(e[2]), F(e[3]));
                    if (t == typeof(Color32)) return (Color32)new Color(F(e[0]), F(e[1]), F(e[2]), F(e[3]));
                    if (t == typeof(Quaternion)) return new Quaternion(F(e[0]), F(e[1]), F(e[2]), F(e[3]));
                    if (t == typeof(Rect)) return new Rect(F(e[0]), F(e[1]), F(e[2]), F(e[3]));
                    return null;
            }
            if (e.TryGetProperty("$sprite", out var sn)) return assets.Sprite(sn.GetString());
            if (e.TryGetProperty("$tex", out var tn)) return assets.Texture(tn.GetString());
            if (e.TryGetProperty("$mat", out var mn)) return assets.Material(mn.GetString());
            if (e.TryGetProperty("$font", out var fn)) return assets.Font(fn.GetString());
            if (e.TryGetProperty("$go", out var gp))
            {
                var tr = FindPath(root, gp.GetString());
                return tr == null ? null : tr.gameObject;
            }
            if (e.TryGetProperty("$comp", out var cp))
            {
                var tr = FindPath(root, cp.GetString());
                var ct = T(e.GetProperty("t").GetString());
                if (tr == null || ct == null) return null;
                var c = tr.GetComponent(Il2CppType.From(ct));
                return c == null ? null : Activator.CreateInstance(t.IsAssignableFrom(ct) ? ct : t, c.Pointer);
            }
            if (e.TryGetProperty("$asset", out _) && t.Name == "LocalisedStrings") return FG.Common.CMS.CMSLoader.Instance._localisedStrings;
            if (e.TryGetProperty("$ext", out _) || e.TryGetProperty("$asset", out _)) return null;
            if (e.TryGetProperty("$curve", out var keys))
            {
                var ac = new AnimationCurve();
                foreach (var k in keys.EnumerateArray())
                    ac.AddKey(new Keyframe(F(k[0]), F(k[1]), F(k[2]), F(k[3]), F(k[4]), F(k[5])) { weightedMode = (WeightedMode)k[6].GetInt32() });
                ac.preWrapMode = (WrapMode)e.GetProperty("pre").GetInt32();
                ac.postWrapMode = (WrapMode)e.GetProperty("post").GetInt32();
                return ac;
            }
            var o = t.IsValueType ? Activator.CreateInstance(t) : t.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(t) : null;
            if (o == null) return null;
            foreach (var kv in e.EnumerateObject())
            {
                var pr = t.GetProperty(kv.Name, BindingFlags.Public | BindingFlags.Instance);
                if (pr == null || !pr.CanWrite) continue;
                var sub = Conv(assets, kv.Value, pr.PropertyType, root);
                if (sub != null) pr.SetValue(o, sub);
            }
            return o;
        }
    }
}
