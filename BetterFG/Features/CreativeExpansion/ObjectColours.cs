using System;
using System.Collections.Generic;
using BetterFG.Customization.TexturePacks;
using BetterFG.Services;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using Il2CppInterop.Runtime;
using LevelEditor;
using UnityEngine;
using NodeEntry = LevelEditorParameterMenuViewModel.ParameterMenuNodeEntry;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class ObjectColours
    {
        internal static bool Enabled => FeatureCreativeContent.On("colouring");

        private static readonly MarkerKind Kind = MarkerKinds.ObjectColour;
        internal static string MarkerName => Kind.Prefab;

        private const float MatchRadius = 0.3f;
        private const int MaxSlots = 10;
        private const int BakeSize = 512;
        private const float MergeApart = 0.16f;
        private const float BlendFit = 0.08f;
        private const float MinShare = 0.01f;
        private const float FitTight = 0.08f;
        private const float FitLoose = 0.16f;
        private const float ScalePenalty = 0.15f;
        private const int EdgeReach = 4;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int RimId = Shader.PropertyToID("_RimColor");
        private static readonly int Color2Id = Shader.PropertyToID("_Color2");
        private static readonly int DetailId = Shader.PropertyToID("_UseDetailAlbedo");

        private sealed class Host
        {
            public LevelEditorPlaceableObject Lepo;
            public Color[] Keys;
            public readonly Color[] Slots = new Color[MaxSlots];
            public readonly LevelEditorPlaceableObject[] Markers = new LevelEditorPlaceableObject[MaxSlots];
        }

        private sealed class Split
        {
            public int W, H;
            public byte[] Rgba, I, J;
            public float[] A, B;
        }

        private static readonly Dictionary<int, Host> _hosts = new Dictionary<int, Host>();
        private static readonly Dictionary<int, List<KeyValuePair<Color, float>>> _palettes = new Dictionary<int, List<KeyValuePair<Color, float>>>();
        private static readonly Dictionary<long, Split> _splits = new Dictionary<long, Split>();
        private static readonly Dictionary<long, Texture2D> _baked = new Dictionary<long, Texture2D>();
        private static MaterialPropertyBlock _block;
        private static bool _repaint;
        private static int _tick;

        internal static bool IsMarker(LevelEditorPlaceableObject lepo) => Kind.Matches(lepo);

        internal static LevelEditorPlaceableObject OwnerOf(LevelEditorPlaceableObject marker)
        {
            foreach (var kv in _hosts)
                for (int s = 0; s < MaxSlots; s++)
                    if (kv.Value.Markers[s] != null && kv.Value.Markers[s].Pointer == marker.Pointer) return kv.Value.Lepo;
            return null;
        }

        internal static bool CanColour(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return false;
            if (MarkerKinds.Of(lepo) != null || NeonMaterial.IsMarkerObject(lepo)) return false;
            if (BatchTargets.GetColourParam(lepo) != null) return false;
            if (RuleColours.IsHost(lepo) || SlimeColours.IsHost(lepo) || Thrusters.IsThrusterController(lepo)) return false;
            if (ObjectThemes.IsThemable(lepo) && ObjectThemes.ThemeOf(lepo) != 0) return false;
            return KeysOf(lepo).Length > 0;
        }

        internal static int SlotCount(LevelEditorPlaceableObject lepo) => KeysOf(lepo).Length;

        private static bool IsRecoloured(LevelEditorPlaceableObject lepo)
        {
            if (!_hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var h)) return false;
            for (int s = 0; s < MaxSlots; s++) if (h.Slots[s].a > 0f) return true;
            return false;
        }

        private static Host HostFor(LevelEditorPlaceableObject lepo)
        {
            int id = lepo.gameObject.GetInstanceID();
            if (_hosts.TryGetValue(id, out var host)) { host.Lepo = lepo; return host; }
            host = new Host { Lepo = lepo, Keys = KeysOf(lepo) };
            _hosts[id] = host;
            return host;
        }

        private static Color[] KeysOf(LevelEditorPlaceableObject lepo)
        {
            if (_hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var known) && known.Keys != null) return known.Keys;

            var found = new List<KeyValuePair<Color, float>>();
            foreach (var r in lepo.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.TryCast<ParticleSystemRenderer>() != null) continue;
                var mats = r.sharedMaterials;
                if (mats.Length == 0) continue;
                float share = Vector3.Scale(r.localBounds.size, r.transform.lossyScale).sqrMagnitude / mats.Length;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    var tex = AlbedoOf(mat);
                    var tint = TintOf(mat);
                    if (tex != null)
                        foreach (var kv in PaletteOf(tex)) found.Add(new KeyValuePair<Color, float>(kv.Key * tint, kv.Value * share));
                    else if (mat.HasProperty(ColorId))
                        found.Add(new KeyValuePair<Color, float>(tint, share));
                    if (HasDetail(mat))
                    {
                        var c2 = mat.GetColor(Color2Id);
                        c2.a = 1f;
                        found.Add(new KeyValuePair<Color, float>(c2, share * 0.5f));
                    }
                }
            }
            found.Sort((a, b) => b.Value.CompareTo(a.Value));

            var clusters = new List<Color>();
            var weights = new List<float>();
            float total = 0f;
            foreach (var kv in found)
            {
                total += kv.Value;
                int k = clusters.FindIndex(c => Near(c, kv.Key) < MergeApart);
                if (k >= 0) { weights[k] += kv.Value; continue; }
                clusters.Add(kv.Key);
                weights.Add(kv.Value);
            }

            var order = new List<int>();
            for (int i = 0; i < clusters.Count; i++) order.Add(i);
            order.Sort((a, b) => weights[b] != weights[a] ? weights[b].CompareTo(weights[a]) : a.CompareTo(b));

            var keys = new List<Color>();
            foreach (int i in order)
            {
                if (keys.Count >= MaxSlots || weights[i] < total * MinShare) break;
                var c = clusters[i];
                int bi = 0, bj = -1;
                float ba = 0f, bb = 0f;
                if (keys.Count >= 2 && Pairs(keys.ToArray(), -1, c.r, c.g, c.b, BlendFit, 1f, ref bi, ref bj, ref ba, ref bb) < BlendFit) continue;
                keys.Add(c);
            }
            return keys.ToArray();
        }

        private static Color TintOf(Material mat)
        {
            var c = mat.HasProperty(ColorId) ? mat.GetColor(ColorId) : Color.white;
            c.a = 1f;
            return c;
        }

        private static bool HasDetail(Material mat)
            => mat.HasProperty(DetailId) && mat.GetFloat(DetailId) > 0.5f && mat.HasProperty(Color2Id);

        private static Texture AlbedoOf(Material mat)
        {
            if (mat == null || !mat.HasProperty(MainTexId)) return null;
            return mat.GetTexture(MainTexId);
        }

        private static float Near(Color a, Color b)
        {
            float va = Mathf.Max(a.r, Mathf.Max(a.g, a.b)), vb = Mathf.Max(b.r, Mathf.Max(b.g, b.b));
            if (va < 0.02f || vb < 0.02f) return Mathf.Abs(va - vb) * 4f;
            float dr = a.r / va - b.r / vb, dg = a.g / va - b.g / vb, db = a.b / va - b.b / vb;
            float trust = Mathf.Clamp01(Mathf.Min(va, vb) * 4f);
            return Mathf.Sqrt(dr * dr + dg * dg + db * db) * trust + 0.6f * Mathf.Abs(Mathf.Log(va / vb));
        }

        private static List<KeyValuePair<Color, float>> PaletteOf(Texture src)
        {
            int id = src.GetInstanceID();
            if (_palettes.TryGetValue(id, out var cached)) return cached;

            var small = TexturePackProject.Blit(src, 64, 64, false, true);
            var px = small.GetPixels32();
            UnityEngine.Object.Destroy(small);

            var counts = new Dictionary<int, int>();
            var sums = new Dictionary<int, Vector3>();
            int total = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.a < 128) continue;
                int bucket = ((c.r >> 5) << 10) | ((c.g >> 5) << 5) | (c.b >> 5);
                counts.TryGetValue(bucket, out int n);
                counts[bucket] = n + 1;
                sums.TryGetValue(bucket, out var s);
                sums[bucket] = s + new Vector3(c.r / 255f, c.g / 255f, c.b / 255f);
                total++;
            }

            var result = new List<KeyValuePair<Color, float>>();
            foreach (var kv in counts)
            {
                if (kv.Value < 4) continue;
                var mean = sums[kv.Key] / kv.Value;
                result.Add(new KeyValuePair<Color, float>(new Color(mean.x, mean.y, mean.z, 1f), kv.Value / (float)total));
            }
            _palettes[id] = result;
            Plugin.Log.LogInfo($"{src.name}: {result.Count} colour buckets out of {total} sampled pixels");
            return result;
        }

        private static float Single(Color[] keys, float pr, float pg, float pb, out int bi, out float ba)
        {
            bi = 0;
            ba = 0f;
            float best = float.MaxValue;
            for (int k = 0; k < keys.Length; k++)
            {
                var K = keys[k];
                float kk = Mathf.Max(K.r * K.r + K.g * K.g + K.b * K.b, 1e-4f);
                float a = Mathf.Max(0f, (pr * K.r + pg * K.g + pb * K.b) / kk);
                float er = pr - a * K.r, eg = pg - a * K.g, eb = pb - a * K.b;
                float c = Mathf.Sqrt(er * er + eg * eg + eb * eb) + ScalePenalty * Mathf.Abs(Mathf.Log(Mathf.Max(a, 1e-3f)));
                if (c < best) { best = c; bi = k; ba = a; }
            }
            return best;
        }

        private static float Pairs(Color[] keys, int mask, float pr, float pg, float pb, float best, float penalty,
            ref int bi, ref int bj, ref float ba, ref float bb)
        {
            for (int x = 0; x < keys.Length; x++)
            {
                if ((mask & (1 << x)) == 0) continue;
                var P = keys[x];
                float pp = P.r * P.r + P.g * P.g + P.b * P.b, xp = pr * P.r + pg * P.g + pb * P.b;
                for (int y = x + 1; y < keys.Length; y++)
                {
                    if ((mask & (1 << y)) == 0) continue;
                    var Q = keys[y];
                    float qq = Q.r * Q.r + Q.g * Q.g + Q.b * Q.b, pq = P.r * Q.r + P.g * Q.g + P.b * Q.b;
                    float det = pp * qq - pq * pq;
                    if (det < 1e-5f) continue;
                    float xq = pr * Q.r + pg * Q.g + pb * Q.b;
                    float a = (xp * qq - xq * pq) / det, b = (xq * pp - xp * pq) / det;
                    if (a <= 0f || b <= 0f) continue;
                    float er = pr - a * P.r - b * Q.r, eg = pg - a * P.g - b * Q.g, eb = pb - a * P.b - b * Q.b;
                    float c = Mathf.Sqrt(er * er + eg * eg + eb * eb) + penalty * Mathf.Abs(Mathf.Log(a + b));
                    if (c >= best) continue;
                    best = c;
                    bi = x; bj = y; ba = a; bb = b;
                }
            }
            return best;
        }

        private static float Weight(float cost)
        {
            float t = Mathf.Clamp01((cost - FitTight) / (FitLoose - FitTight));
            return 1f - t * t * (3f - 2f * t);
        }

        private static Color Remap(Color c, Color[] keys, Color[] slots)
        {
            int j = -1;
            float b = 0f;
            float cost = Single(keys, c.r, c.g, c.b, out int i, out float a);
            cost = Pairs(keys, -1, c.r, c.g, c.b, cost, ScalePenalty, ref i, ref j, ref a, ref b);
            float w = Weight(cost);
            if (w <= 0f) return c;
            var d = Delta(keys, slots, i) * (a * w);
            if (j >= 0) d += Delta(keys, slots, j) * (b * w);
            return new Color(Mathf.Clamp01(c.r + d.r), Mathf.Clamp01(c.g + d.g), Mathf.Clamp01(c.b + d.b), c.a);
        }

        private static Color Delta(Color[] keys, Color[] slots, int k)
            => slots[k].a > 0f ? new Color(slots[k].r - keys[k].r, slots[k].g - keys[k].g, slots[k].b - keys[k].b, 0f) : Color.clear;

        private static long KeyHash(Color c) => (Mathf.RoundToInt(c.r * 255f) << 16) | (Mathf.RoundToInt(c.g * 255f) << 8) | Mathf.RoundToInt(c.b * 255f);

        private static Split SplitOf(Texture src, Color tint, Color[] keys, long id)
        {
            if (_splits.TryGetValue(id, out var cached)) return cached;

            int w = Mathf.Min(src.width, BakeSize), h = Mathf.Min(src.height, BakeSize), n = w * h;
            var tex = TexturePackProject.Blit(src, w, h, false, true);
            var s = new Split { W = w, H = h, Rgba = TexturePackProject.Raw(tex), I = new byte[n], J = new byte[n], A = new float[n], B = new float[n] };
            UnityEngine.Object.Destroy(tex);
            for (int p = 0; p < n; p++)
            {
                int o = p * 4;
                s.Rgba[o] = (byte)(s.Rgba[o] * tint.r);
                s.Rgba[o + 1] = (byte)(s.Rgba[o + 1] * tint.g);
                s.Rgba[o + 2] = (byte)(s.Rgba[o + 2] * tint.b);
            }

            var lab = new byte[n];
            var sc = new float[n];
            var sa = new float[n];
            for (int p = 0; p < n; p++)
            {
                int o = p * 4;
                sc[p] = Single(keys, s.Rgba[o] / 255f, s.Rgba[o + 1] / 255f, s.Rgba[o + 2] / 255f, out int k, out sa[p]);
                lab[p] = (byte)k;
            }

            int hit = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x, o = p * 4, i = lab[p], j = -1;
                    float cost = sc[p], a = sa[p], b = 0f;
                    if (cost > FitTight * 0.5f)
                    {
                        int mask = 1 << i;
                        for (int yy = Math.Max(0, y - EdgeReach); yy <= Math.Min(h - 1, y + EdgeReach); yy++)
                            for (int xx = Math.Max(0, x - EdgeReach); xx <= Math.Min(w - 1, x + EdgeReach); xx++)
                            {
                                int q = yy * w + xx;
                                if (sc[q] <= FitTight) mask |= 1 << lab[q];
                            }
                        cost = Pairs(keys, mask, s.Rgba[o] / 255f, s.Rgba[o + 1] / 255f, s.Rgba[o + 2] / 255f, cost, ScalePenalty, ref i, ref j, ref a, ref b);
                    }

                    float wt = Weight(cost);
                    if (wt <= 0f) { s.I[p] = 255; continue; }
                    s.I[p] = (byte)i;
                    s.J[p] = (byte)(j < 0 ? 255 : j);
                    s.A[p] = a * wt;
                    s.B[p] = b * wt;
                    hit++;
                }
            _splits[id] = s;
            Plugin.Log.LogInfo($"split {src.name} {w}x{h} against {keys.Length} keys, {hit * 100 / n}% of it is paint");
            return s;
        }

        private static Texture2D Bake(Texture src, Color tint, Color[] keys, Color[] slots)
        {
            long id = src.GetInstanceID() * 31L + KeyHash(tint);
            for (int k = 0; k < keys.Length; k++) id = id * 31 + KeyHash(keys[k]);
            long hash = id;
            for (int k = 0; k < keys.Length; k++) hash = hash * 397 + (slots[k].a <= 0f ? 0 : KeyHash(slots[k]));

            if (_baked.TryGetValue(hash, out var cached) && cached != null) return cached;
            if (_baked.Count > 48 && IdentifierObjects.InEditor())
            {
                foreach (var kv in _baked) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
                _baked.Clear();
                _splits.Clear();
                Plugin.Log.LogInfo("recolour cache was getting fat, dropped the lot and repainted");
                _repaint = true;
            }

            var s = SplitOf(src, tint, keys, id);
            var delta = new Color[keys.Length];
            for (int k = 0; k < keys.Length; k++) delta[k] = Delta(keys, slots, k) * 255f;

            var rgba = (byte[])s.Rgba.Clone();
            for (int p = 0; p < s.I.Length; p++)
            {
                if (s.I[p] == 255) continue;
                var d = delta[s.I[p]] * s.A[p];
                if (s.J[p] != 255) d += delta[s.J[p]] * s.B[p];
                int o = p * 4;
                rgba[o] = (byte)Mathf.Clamp(rgba[o] + d.r, 0f, 255f);
                rgba[o + 1] = (byte)Mathf.Clamp(rgba[o + 1] + d.g, 0f, 255f);
                rgba[o + 2] = (byte)Mathf.Clamp(rgba[o + 2] + d.b, 0f, 255f);
            }

            var tex = new Texture2D(s.W, s.H, TextureFormat.RGBA32, false);
            TexturePackProject.Upload(tex, rgba);
            tex.name = "bfg_recolour_" + src.name;
            tex.wrapMode = src.wrapMode;
            tex.filterMode = src.filterMode;
            tex.hideFlags = HideFlags.HideAndDontSave;
            _baked[hash] = tex;
            return tex;
        }

        private static void Apply(Host host)
        {
            if (host.Lepo == null || host.Keys == null || host.Keys.Length == 0) return;
            if (_block == null) _block = new MaterialPropertyBlock();

            if (ObjectThemes.ThemeOf(host.Lepo) != 0)
            {
                _block.Clear();
                foreach (var r in host.Lepo.GetComponentsInChildren<Renderer>(true))
                    if (r != null)
                        for (int m = 0; m < r.sharedMaterials.Length; m++) r.SetPropertyBlock(_block, m);
                return;
            }

            bool any = false;
            for (int k = 0; k < host.Keys.Length; k++) if (host.Slots[k].a > 0f) any = true;

            foreach (var r in host.Lepo.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.TryCast<ParticleSystemRenderer>() != null) continue;
                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    var src = AlbedoOf(mat);
                    if (src == null && !mat.HasProperty(ColorId)) continue;
                    var tint = TintOf(mat);

                    r.GetPropertyBlock(_block, m);
                    if (src != null) _block.SetTexture(MainTexId, src);
                    if (mat.HasProperty(ColorId)) _block.SetColor(ColorId, mat.GetColor(ColorId));

                    if (any)
                    {
                        var palette = src != null ? PaletteOf(src) : null;
                        if ((palette == null || palette.Count == 1) && mat.HasProperty(ColorId))
                        {
                            var mean = palette == null ? Color.white : palette[0].Key;
                            var c = Remap(mean * tint, host.Keys, host.Slots);
                            c = new Color(c.r / Mathf.Max(mean.r, 0.05f), c.g / Mathf.Max(mean.g, 0.05f), c.b / Mathf.Max(mean.b, 0.05f), mat.GetColor(ColorId).a);
                            _block.SetColor(ColorId, c);
                        }
                        else
                        {
                            _block.SetTexture(MainTexId, Bake(src, tint, host.Keys, host.Slots));
                            if (mat.HasProperty(ColorId)) _block.SetColor(ColorId, new Color(1f, 1f, 1f, mat.GetColor(ColorId).a));
                        }
                    }

                    if (HasDetail(mat))
                    {
                        var c2 = mat.GetColor(Color2Id);
                        _block.SetColor(Color2Id, any ? Remap(c2, host.Keys, host.Slots) : c2);
                    }
                    RemapGlow(mat, EmissionId, any, host);
                    RemapGlow(mat, RimId, any, host);
                    r.SetPropertyBlock(_block, m);
                }
            }
        }

        private static void RemapGlow(Material mat, int id, bool any, Host host)
        {
            if (!mat.HasProperty(id)) return;
            var e = mat.GetColor(id);
            float power = Mathf.Max(e.r, Mathf.Max(e.g, e.b));
            if (power <= 0.001f) return;
            _block.SetColor(id, any ? Remap(e * (1f / power), host.Keys, host.Slots) * power : e);
        }

        internal static void OnThemeChanged(LevelEditorPlaceableObject lepo)
        {
            if (lepo != null && _hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var host)) Apply(host);
        }

        internal static void OpenPicker(LevelEditorPlaceableObject lepo, int slot)
        {
            var host = HostFor(lepo);
            if (slot >= host.Keys.Length) return;

            if (host.Slots[slot].a <= 0f) host.Slots[slot] = host.Keys[slot];
            Reconcile(host);

            var marker = host.Markers[slot];
            if (marker == null) return;

            var param = BatchTargets.GetColourParam(marker);
            if (param == null)
            {
                Plugin.Log.LogWarning($"object colours: {lepo.name} slot {slot} has a marker but no colour parameter on it, no picker to open");
                return;
            }
            IdentifierObjects.OpenPicker(param);
        }

        internal static void ResetColours(LevelEditorPlaceableObject lepo)
        {
            if (!_hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var host)) return;
            for (int s = 0; s < MaxSlots; s++)
            {
                host.Slots[s] = default;
                if (host.Markers[s] == null) continue;
                IdentifierObjects.Remove(host.Markers[s]);
                host.Markers[s] = null;
            }
            Apply(host);
            ColourSwatches.Refresh();
            Plugin.Log.LogInfo($"{lepo.name} back to its stock colours");
        }

        internal static void OnMarkerColourChanged(LevelEditorPlaceableObject marker)
        {
            foreach (var kv in _hosts)
            {
                var host = kv.Value;
                for (int s = 0; s < MaxSlots; s++)
                {
                    if (host.Markers[s] == null || host.Markers[s].Pointer != marker.Pointer) continue;
                    var colour = BatchTargets.GetColourParam(marker);
                    if (colour == null) return;
                    var c = colour.CurrentColour;
                    c.a = 1f;
                    host.Slots[s] = c;
                    Apply(host);
                    ColourSwatches.Refresh();
                    return;
                }
            }
        }

        internal static void OnCloned(LevelEditorPlaceableObject clone, LevelEditorPlaceableObject source)
        {
            if ((IdentifierObjects.InEditor() && !Enabled) || clone == null || source == null) return;
            if (!_hosts.TryGetValue(source.gameObject.GetInstanceID(), out var from)) return;

            var host = new Host { Lepo = clone, Keys = from.Keys };
            for (int s = 0; s < MaxSlots; s++) host.Slots[s] = from.Slots[s];
            _hosts[clone.gameObject.GetInstanceID()] = host;
            Apply(host);
        }

        internal static void OnPlaced(LevelEditorPlaceableObject lepo)
        {
            if ((IdentifierObjects.InEditor() && !Enabled) || lepo == null) return;
            if (!_hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var host)) return;
            Reconcile(host);
        }

        internal static void OnDestroyed(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return;
            int id = lepo.gameObject.GetInstanceID();
            if (!_hosts.TryGetValue(id, out var host)) return;
            _hosts.Remove(id);
            for (int s = 0; s < MaxSlots; s++)
                if (host.Markers[s] != null) IdentifierObjects.Remove(host.Markers[s]);
        }

        internal static void ResetEditorState()
        {
            _hosts.Clear();
            _palettes.Clear();
            _splits.Clear();
            _repaint = false;
            _tick = 0;
        }

        internal static void Tick()
        {
            ColourSwatches.Tick();
            if (!Enabled || _hosts.Count == 0 || ++_tick % 15 != 0) return;

            if (_repaint)
            {
                _repaint = false;
                foreach (var kv in _hosts) Apply(kv.Value);
            }

            foreach (var kv in _hosts)
            {
                var host = kv.Value;
                bool alive = host.Lepo != null && host.Lepo.gameObject.activeInHierarchy;
                bool marked = false, wanted = false;
                for (int s = 0; s < MaxSlots; s++)
                {
                    if (host.Markers[s] != null) marked = true;
                    if (host.Slots[s].a > 0f) wanted = true;
                }

                if (!alive && marked)
                {
                    for (int s = 0; s < MaxSlots; s++)
                    {
                        if (host.Markers[s] == null) continue;
                        IdentifierObjects.Remove(host.Markers[s]);
                        host.Markers[s] = null;
                    }
                    Plugin.Log.LogInfo("a recoloured object went away, took its colour markers with it");
                }
                else if (alive && wanted && !marked)
                {
                    Reconcile(host);
                    Plugin.Log.LogInfo($"{host.Lepo.name} is back, put its colour markers back too");
                }
            }
        }

        private static void Reconcile(Host host)
        {
            if (host.Lepo == null) return;
            var pos = host.Lepo.transform.position;

            for (int s = 0; s < MaxSlots; s++)
            {
                if (host.Slots[s].a <= 0f)
                {
                    if (host.Markers[s] == null) continue;
                    IdentifierObjects.Remove(host.Markers[s]);
                    host.Markers[s] = null;
                    continue;
                }

                if (host.Markers[s] == null)
                {
                    host.Markers[s] = Kind.Spawn(pos, "#" + ColorUtility.ToHtmlStringRGB(host.Slots[s]), Kind.Scale * (s + 1));
                    continue;
                }

                host.Markers[s].transform.position = pos;
            }
        }

        internal static void Sync()
        {
            bool inEditor = IdentifierObjects.InEditor();
            if (inEditor && !Enabled) return;

            var col = LevelEditorPlaceableObject.Collection;
            if (col == null) return;

            int painted = 0, missed = 0;

            var touched = new Dictionary<int, Host>();

            if (inEditor)
            {
                for (int i = 0; i < col.Count; i++)
                {
                    var marker = col[i];
                    if (!IsMarker(marker)) continue;

                    var target = Nearest(col, marker.transform.position);
                    if (target == null) { missed++; continue; }

                    var colour = BatchTargets.GetColourParam(marker);
                    if (colour == null) continue;

                    var host = HostFor(target);
                    int slot = Mathf.Clamp(Mathf.RoundToInt(MarkerScale(marker).y / Kind.Scale) - 1, 0, MaxSlots - 1);
                    if (slot >= host.Keys.Length) continue;

                    host.Markers[slot] = marker;
                    var c = colour.CurrentColour;
                    c.a = 1f;
                    host.Slots[slot] = c;
                    touched[target.gameObject.GetInstanceID()] = host;
                    painted++;
                }
            }
            else
            {
                foreach (var m in IdentifierObjects.ReadRound(MarkerName))
                {
                    if (!Kind.MatchesRotation(m.Rotation)) continue;
                    if (m.Scale.x > Kind.Scale * 4f) continue;

                    Color c;
                    if (string.IsNullOrEmpty(m.Hex) || !ColorUtility.TryParseHtmlString(m.Hex, out c)) continue;
                    c.a = 1f;

                    var target = Nearest(col, m.Position);
                    if (target == null) { missed++; continue; }

                    var host = HostFor(target);
                    int slot = Mathf.Clamp(Mathf.RoundToInt(m.Scale.y / Kind.Scale) - 1, 0, MaxSlots - 1);
                    if (slot >= host.Keys.Length) continue;

                    host.Slots[slot] = c;
                    touched[target.gameObject.GetInstanceID()] = host;
                    painted++;
                }
            }

            foreach (var kv in touched) Apply(kv.Value);

            if (painted > 0 || missed > 0)
                Plugin.Log.LogInfo($"object colours: {painted} slot(s) applied"
                    + (missed > 0 ? $", {missed} marker(s) with nothing near them" : ""));
        }

        private static Vector3 MarkerScale(LevelEditorPlaceableObject lepo)
        {
            var sp = lepo._levelEditorScaleParameter;
            return sp != null ? sp.CurrentScale : lepo.transform.localScale;
        }

        private static LevelEditorPlaceableObject Nearest(
            Il2CppSystem.Collections.Generic.List<LevelEditorPlaceableObject> col, Vector3 pos)
        {
            LevelEditorPlaceableObject best = null;
            float bestSq = MatchRadius * MatchRadius;
            for (int i = 0; i < col.Count; i++)
            {
                var lepo = col[i];
                if (lepo == null || MarkerKinds.Of(lepo) != null || NeonMaterial.IsMarkerObject(lepo)) continue;
                float d = (lepo.transform.position - pos).sqrMagnitude;
                if (d > bestSq) continue;
                bestSq = d;
                best = lepo;
            }
            return best;
        }

        private static readonly HashSet<IntPtr> _ourRows = new HashSet<IntPtr>();
        private static readonly ParameterChangedIndex[] _slotCb = new ParameterChangedIndex[MaxSlots];
        private static ParameterChangedIndex _resetCb;
        private static LevelEditorPlaceableObject _rowTarget;

        internal static void OnParamMenu(LevelEditorPlaceableObject lepo)
        {
            if (_rowTarget != null) CleanupRows(_rowTarget);
            _rowTarget = null;

            if (!Enabled || !CanColour(lepo)) return;

            if (_slotCb[0] == null)
            {
                for (int s = 0; s < MaxSlots; s++)
                {
                    int slot = s;
                    _slotCb[s] = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(_ => Pick(slot)));
                }
                _resetCb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(OnReset));
            }

            _rowTarget = lepo;
            int slots = SlotCount(lepo);

            var keys = KeysOf(lepo);
            int id = lepo.gameObject.GetInstanceID();
            for (int s = 0; s < slots; s++)
            {
                string title = LocalizationService.Get($"objectcolours.slot{s + 1}");
                AddRow(lepo, ParameterUtils.CreateButtonEntry(title,
                    ParameterWrapMode.NoWrap, _slotCb[s], null, null, false, 1f, null, false, false, null));
                int slot = s;
                ColourSwatches.Show(title, () => keys[slot],
                    () => _hosts.TryGetValue(id, out var h) && h.Slots[slot].a > 0f ? h.Slots[slot] : keys[slot]);
            }

            if (IsRecoloured(lepo))
                AddRow(lepo, ParameterUtils.CreateButtonEntry(LocalizationService.Get("objectcolours.reset"),
                    ParameterWrapMode.NoWrap, _resetCb, null, null, false, 1f, null, false, false, null));
        }

        private static void AddRow(LevelEditorPlaceableObject lepo, NodeEntry row)
        {
            if (row == null) return;
            row.Tag(ParamCategory.Appearance);
            lepo.AddParameter(row, -1);
            _ourRows.Add(row.Pointer);
        }

        private static void CleanupRows(LevelEditorPlaceableObject lepo)
        {
            var existing = lepo.CustomParameters;
            if (existing == null || _ourRows.Count == 0) { _ourRows.Clear(); return; }

            for (int i = existing.Count - 1; i >= 0; i--)
            {
                var e = existing[i].ParameterEntry;
                if (e == null || !_ourRows.Remove(e.Pointer)) continue;
                existing.RemoveAt(i);
            }
            _ourRows.Clear();
        }

        private static void OnReset(int index) { if (_rowTarget != null) ResetColours(_rowTarget); }

        private static void Pick(int slot)
        {
            if (_rowTarget == null) return;
            OpenPicker(_rowTarget, slot);
        }
    }
}
