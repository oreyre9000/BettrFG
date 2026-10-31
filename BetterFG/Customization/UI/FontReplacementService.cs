using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using BetterFG.Services;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace BetterFG.Customization.UI
{
    // one font override: replace a named game TMP_FontAsset with a custom ttf/otf.
    public class FontOverride
    {
        public string entryName = "";
        public string fontPath = "";       // the user's ttf/otf
        public string targetFontName = ""; // the game TMP_FontAsset name this replaces
        public bool enabled = true;

        public TMP_FontAsset builtAsset;   // the donor asset built from fontPath (not persisted)
    }

    // ── Font replacement by ATLAS PIXEL SWAP ──────────────────────────────────────────────────────
    //
    // Every earlier approach mutated things the game keeps re-deriving — `tmp.font`, or `_MainTex` on
    // every font material (plus the per-bean instance clones TMP spawns in a round) — and then had to
    // reverse all of it perfectly on toggle-off. Each fix traded one break for another.
    //
    // This approach touches NO material and NO `tmp.font`. It builds a dynamic SDF atlas from the user's
    // ttf at the GAME atlas's exact size + format + padding, then blits those pixels straight INTO the
    // game font asset's existing atlas Texture2D (same object every material already samples) and swaps
    // in the matching glyph / character tables + face metrics. Materials are never read or written, so
    // there is nothing about them to get wrong or to undo — shadow / gold / outline / every instance
    // clone just render the new pixels through their unchanged `_GradientScale` (valid because we kept
    // the game's padding). Non-Latin still routes through the game's untouched fallback font table.
    //
    // Toggle-off blits the stashed original pixels back and restores the table references. One cold
    // Harmony hook on ReadFontAssetDefinition re-applies if the game re-initialises a hijacked asset.
    public static class FontReplacementService
    {
        public const string KEY_MASTER_ON = "ui.font.master";
        public const string KEY_COUNT = "ui.font.count";
        private static string EK(int i, string f) => $"ui.font.entry.{i}.{f}";

        private const string OUR_PREFIX = "BFG_";

        // ASCII printable + Latin-1 Supplement printable — covers essentially all Latin text the game
        // shows; small enough to fit any real game atlas. Anything outside it falls through to the
        // game's own fallback font chain, untouched, exactly as before.
        private static readonly string WarmupChars = BuildWarmup();
        private static string BuildWarmup()
        {
            var sb = new System.Text.StringBuilder();
            for (int c = 0x20; c <= 0x7E; c++) sb.Append((char)c);
            for (int c = 0xA1; c <= 0xFF; c++) sb.Append((char)c);
            return sb.ToString();
        }

        private static readonly Dictionary<string, FontOverride> _active =
            new Dictionary<string, FontOverride>(StringComparer.OrdinalIgnoreCase);
        private static bool _masterOn;

        public static bool MasterOn => SettingsService.Get(KEY_MASTER_ON, "false") == "true";
        public static bool MasterOnFast => _masterOn;

        // ── enumerate the game's font assets (target picker) ──────────────────
        public static List<string> GetAllFontAssetNames()
        {
            var names = new List<string>();
            try
            {
                foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (fa == null || string.IsNullOrEmpty(fa.name)) continue;
                    if (fa.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) continue;
                    if (!names.Contains(fa.name)) names.Add(fa.name);
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("BetterFG: enumerate fonts: " + ex.Message); }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static TMP_FontAsset GetFontAssetByName(string name)
        {
            var all = GetFontAssetsByName(name);
            return all.Count > 0 ? all[0] : null;
        }

        private static List<TMP_FontAsset> GetFontAssetsByName(string name)
        {
            var outp = new List<TMP_FontAsset>();
            if (string.IsNullOrEmpty(name)) return outp;
            try
            {
                foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (fa == null || string.IsNullOrEmpty(fa.name)) continue;
                    if (fa.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) continue;
                    if (fa.name == name) outp.Add(fa);
                }
            }
            catch { }
            return outp;
        }

        // ── build the donor asset: a dynamic SDF font at a given atlas size/padding, warmed with the
        //    Latin set. cached by (path|WxH|pad) since different game fonts have different atlases. ──
        private static readonly Dictionary<string, TMP_FontAsset> _builtByKey =
            new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);

        private static TMP_FontAsset BuildDonor(FontOverride ov, int w, int h, int pad)
        {
            if (string.IsNullOrEmpty(ov.fontPath) || !File.Exists(ov.fontPath))
            {
                Plugin.Log.LogError("BetterFG: font file missing: " + ov.fontPath);
                return null;
            }
            string key = $"{ov.fontPath}|{w}x{h}|{pad}";
            if (_builtByKey.TryGetValue(key, out var cached) && cached != null) return cached;

            try
            {
                // pick a sampling size that lets the whole warmup set fit this atlas on ONE page. cell
                // side ~= sqrt(area / cells); sampling = cell minus the padding on both sides.
                int cells = WarmupChars.Length + 48;
                int cell = Mathf.Max(8, (int)Mathf.Sqrt((float)w * h / cells));
                int sampling = Mathf.Clamp(cell - 2 * pad - 2, 12, 80);

                var asset = TMP_FontAsset.CreateFontAsset(ov.fontPath, 0, sampling, pad,
                    GlyphRenderMode.SDFAA, w, h, AtlasPopulationMode.Dynamic, false);
                if (asset == null) { Plugin.Log.LogError("BetterFG: CreateFontAsset null for " + ov.fontPath); return null; }
                asset.hideFlags = HideFlags.HideAndDontSave;
                asset.name = OUR_PREFIX + Path.GetFileNameWithoutExtension(ov.fontPath) + $"_{w}_{pad}";

                // TMP defers the atlas texture (null until glyphs render, queued to end of frame). give it
                // a real Alpha8 texture set up like TMP's own — zero-filled (raw Texture2D memory is
                // garbage => edge seams), Clamp (Repeat wraps a border sample across the atlas => a line),
                // Bilinear — then bake the charset and flush the queue synchronously.
                var atlas = new Texture2D(w, h, TextureFormat.Alpha8, false, true)
                {
                    name = asset.name + " Atlas",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                try { atlas.LoadRawTextureData(new Il2CppStructArray<byte>(w * h)); atlas.Apply(false, false); } catch { }

                var arr = new Il2CppReferenceArray<Texture2D>(1); arr[0] = atlas;
                asset.m_AtlasTextures = arr;
                asset.m_AtlasTexture = atlas;
                asset.m_AtlasTextureIndex = 0;
                if (asset.material != null) asset.material.SetTexture("_MainTex", atlas);

                try { asset.TryAddCharacters(WarmupChars, false); } catch (Exception ex) { Plugin.Log.LogWarning("warmup: " + ex.Message); }
                try { TMP_FontAsset.UpdateFontAssetsInUpdateQueue(); } catch { }
                try { atlas.Apply(false, false); } catch { }

                // drop any glyph that spilled to a second atlas page — with a single-page atlas its
                // atlasIndex >= 1 would send TMP_MaterialManager.GetFallbackMaterial out of bounds.
                PruneOverflowGlyphs(asset);

                Plugin.Log.LogInfo($"donor {asset.name}: {w}x{h} Alpha8 sampling={sampling} pad={pad} chars={asset.characterTable?.Count}");
                _builtByKey[key] = asset;
                return asset;
            }
            catch (Exception ex) { Plugin.Log.LogError("BetterFG: BuildDonor: " + ex); return null; }
        }

        private static void PruneOverflowGlyphs(TMP_FontAsset a)
        {
            try
            {
                var ct = a.characterTable;
                if (ct == null) return;
                int removed = 0;
                for (int i = ct.Count - 1; i >= 0; i--)
                {
                    var ch = ct[i];
                    if (ch != null && ch.glyph != null && ch.glyph.atlasIndex != 0) { ct.RemoveAt(i); removed++; }
                }
                if (removed > 0)
                {
                    a.InitializeDictionaryLookupTables();
                    Plugin.Log.LogWarning($"donor {a.name}: pruned {removed} overflow glyphs (atlas too small for full warmup)");
                }
            }
            catch { }
        }

        public static TMP_FontAsset BuildPreview(FontOverride ov) => BuildDonor(ov, 512, 512, 4);

        // ── hijack state ─────────────────────────────────────────────────────
        private class HijackSnapshot
        {
            public TMP_FontAsset game;
            public Texture2D gameAtlas;        // the game's own atlas texture (object never changes)
            public Texture2D stash;            // GPU copy of its original pixels
            public UnityEngine.TextCore.FaceInfo faceInfo;
            public AtlasPopulationMode populationMode;
            public Il2CppSystem.Collections.Generic.List<UnityEngine.TextCore.Glyph> glyphTable;
            public Il2CppSystem.Collections.Generic.List<TMP_Character> characterTable;
            public Il2CppSystem.Collections.Generic.List<UnityEngine.TextCore.GlyphRect> usedGlyphRects, freeGlyphRects;
            public TMP_FontFeatureTable fontFeatureTable;
            public TMP_FontAsset donor;
        }

        private static readonly Dictionary<int, HijackSnapshot> _hijacked = new Dictionary<int, HijackSnapshot>();

        private static bool _restoring;
        internal static bool IsRestoring => _restoring;

        // ── entry point ─────────────────────────────────────────────────────
        public static void RebuildAndApply()
        {
            _masterOn = SettingsService.Get(KEY_MASTER_ON, "false") == "true";
            Utilities.PatchGate.SetActive(KEY_MASTER_ON, _masterOn);

            RestoreAllHijacks();
            _active.Clear();

            if (_masterOn)
            {
                foreach (var ov in LoadAll())
                {
                    if (!ov.enabled || string.IsNullOrEmpty(ov.targetFontName)) continue;
                    _active[ov.targetFontName] = ov;
                    var games = GetFontAssetsByName(ov.targetFontName);
                    if (games.Count == 0) { Plugin.Log.LogWarning($"font target not loaded yet: {ov.targetFontName}"); continue; }
                    foreach (var game in games) HijackFont(game, ov);
                }
            }

            RefreshAllText();
        }

        public static void ReapplyFromSettings() => RebuildAndApply();

        public static void HealAndReapply()
        {
            if (!_masterOn) return;
            foreach (var ov in _active.Values)
                foreach (var game in GetFontAssetsByName(ov.targetFontName))
                    if (!_hijacked.ContainsKey(game.GetInstanceID())) HijackFont(game, ov);
            RefreshAllText();
        }

        private static void HijackFont(TMP_FontAsset game, FontOverride ov)
        {
            int id = game.GetInstanceID();
            if (_hijacked.ContainsKey(id)) return;

            var gAtlas = game.m_AtlasTexture;
            if (gAtlas == null) { Plugin.Log.LogWarning($"hijack {game.name}: no atlas texture, skipping"); return; }
            if (gAtlas.format != TextureFormat.Alpha8)
            {
                Plugin.Log.LogWarning($"hijack {game.name}: atlas is {gAtlas.format}, only Alpha8 supported — skipping");
                return;
            }
            int w = gAtlas.width, h = gAtlas.height, pad = Mathf.Max(1, game.atlasPadding);

            var donor = BuildDonor(ov, w, h, pad);
            if (donor == null || donor.m_AtlasTexture == null) return;
            ov.builtAsset = donor;

            var snap = new HijackSnapshot
            {
                game = game,
                gameAtlas = gAtlas,
                faceInfo = game.faceInfo,
                populationMode = game.atlasPopulationMode,
                glyphTable = game.glyphTable,
                characterTable = game.characterTable,
                usedGlyphRects = game.usedGlyphRects,
                freeGlyphRects = game.freeGlyphRects,
                fontFeatureTable = game.fontFeatureTable,
                donor = donor,
            };

            // stash the original pixels (GPU copy — works even though the game atlas isn't CPU-readable)
            try
            {
                var stash = new Texture2D(w, h, TextureFormat.Alpha8, false, true) { hideFlags = HideFlags.HideAndDontSave, name = game.name + " orig" };
                Graphics.CopyTexture(gAtlas, stash);
                snap.stash = stash;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("stash: " + ex.Message); }

            // OUR glyphs into the game's own atlas texture object — no material is touched.
            try { Graphics.CopyTexture(donor.m_AtlasTexture, gAtlas); }
            catch (Exception ex) { Plugin.Log.LogError("hijack blit failed: " + ex.Message); return; }

            // matching tables + metrics so UVs line up with the pixels we just wrote. Static: the pixels
            // are baked, no dynamic growth into this texture.
            game.faceInfo = donor.faceInfo;
            game.glyphTable = donor.glyphTable;
            game.characterTable = donor.characterTable;
            game.usedGlyphRects = donor.usedGlyphRects;
            game.freeGlyphRects = donor.freeGlyphRects;
            game.fontFeatureTable = donor.fontFeatureTable;
            game.atlasPopulationMode = AtlasPopulationMode.Static;
            try { game.InitializeDictionaryLookupTables(); } catch { }

            _hijacked[id] = snap;
            Plugin.Log.LogInfo($"hijack {game.name}: {w}x{h} atlas repainted from {Path.GetFileName(ov.fontPath)}");
        }

        private static void RestoreAllHijacks()
        {
            if (_hijacked.Count == 0) return;
            _restoring = true;
            foreach (var snap in _hijacked.Values)
            {
                try
                {
                    var g = snap.game;
                    var gAtlas = snap.gameAtlas;

                    if (gAtlas != null && snap.stash != null)
                    {
                        try { Graphics.CopyTexture(snap.stash, gAtlas); }
                        catch (Exception ex) { Plugin.Log.LogWarning("restore blit: " + ex.Message); }
                    }

                    if (g != null)
                    {
                        g.faceInfo = snap.faceInfo;
                        g.glyphTable = snap.glyphTable;
                        g.characterTable = snap.characterTable;
                        g.usedGlyphRects = snap.usedGlyphRects;
                        g.freeGlyphRects = snap.freeGlyphRects;
                        g.fontFeatureTable = snap.fontFeatureTable;
                        g.atlasPopulationMode = snap.populationMode;
                        try { g.ReadFontAssetDefinition(); }
                        catch { try { g.InitializeDictionaryLookupTables(); } catch { } }
                        try { TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, g); } catch { }
                    }

                    if (snap.stash != null) { try { UnityEngine.Object.Destroy(snap.stash); } catch { } }
                    Plugin.Log.LogInfo($"restore {(g != null ? g.name : "<reloaded>")}: atlas repainted to original");
                }
                catch (Exception ex) { Plugin.Log.LogWarning("BFGFont: restore: " + ex.Message); }
            }
            _hijacked.Clear();
            _restoring = false;
        }

        // re-apply after the game re-initialised a hijacked asset (ReadFontAssetDefinition hook).
        internal static void ReassertHijack(TMP_FontAsset game)
        {
            if (game == null) return;
            if (!_hijacked.TryGetValue(game.GetInstanceID(), out var snap) || snap.donor == null) return;
            try
            {
                var gAtlas = game.m_AtlasTexture;
                if (gAtlas != null && snap.donor.m_AtlasTexture != null && gAtlas.format == TextureFormat.Alpha8
                    && gAtlas.width == snap.donor.m_AtlasTexture.width && gAtlas.height == snap.donor.m_AtlasTexture.height)
                {
                    snap.gameAtlas = gAtlas;
                    Graphics.CopyTexture(snap.donor.m_AtlasTexture, gAtlas);
                }
                game.faceInfo = snap.donor.faceInfo;
                game.glyphTable = snap.donor.glyphTable;
                game.characterTable = snap.donor.characterTable;
                game.usedGlyphRects = snap.donor.usedGlyphRects;
                game.freeGlyphRects = snap.donor.freeGlyphRects;
                game.fontFeatureTable = snap.donor.fontFeatureTable;
                game.atlasPopulationMode = AtlasPopulationMode.Static;
                game.InitializeDictionaryLookupTables();
            }
            catch (Exception ex) { Plugin.Log.LogWarning("BFGFont: reassert: " + ex.Message); }
        }

        internal static bool IsHijacked(int id) => _hijacked.ContainsKey(id);

        // ── force every live text to rebuild against the changed atlas ───────
        private static void RefreshAllText()
        {
            try
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
                {
                    if (t == null) continue;
                    try
                    {
                        var ui = t.TryCast<TextMeshProUGUI>();
                        if (ui != null) ui.UpdateFontAsset();
                        else { var wr = t.TryCast<TextMeshPro>(); if (wr != null) wr.UpdateFontAsset(); }
                    }
                    catch { }
                    try { t.ForceMeshUpdate(true, true); } catch { }
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("BetterFG: text refresh: " + ex.Message); }
        }

        // ── persistence ──────────────────────────────────────────────────────
        public static List<FontOverride> LoadAll()
        {
            var list = new List<FontOverride>();
            if (!int.TryParse(SettingsService.Get(KEY_COUNT, "0"), out int count)) return list;
            for (int i = 0; i < count; i++)
                list.Add(new FontOverride
                {
                    entryName = SettingsService.Get(EK(i, "name"), "entry " + i),
                    fontPath = SettingsService.Get(EK(i, "path"), ""),
                    targetFontName = SettingsService.Get(EK(i, "target"), ""),
                    enabled = SettingsService.Get(EK(i, "enabled"), "1") == "1",
                });
            return list;
        }

        public static void SaveAll(List<FontOverride> list)
        {
            SettingsService.Set(KEY_COUNT, list.Count.ToString());
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                SettingsService.Set(EK(i, "name"), e.entryName);
                SettingsService.Set(EK(i, "path"), e.fontPath);
                SettingsService.Set(EK(i, "target"), e.targetFontName);
                SettingsService.Set(EK(i, "enabled"), e.enabled ? "1" : "0");
            }
        }

        public static void SetMaster(bool on)
        {
            _masterOn = on;
            SettingsService.Set(KEY_MASTER_ON, on ? "true" : "false");
            Utilities.PatchGate.SetActive(KEY_MASTER_ON, on);
        }

        // ── legacy no-op shims (old per-text swap API) ──
        public static void ApplyToNametag(TMP_Text t) { }
        public static void ProtectText(TMP_Text t) { }
        public static void Protect(TMP_Text t) { }
        public static void RevertIfTouched(TMP_Text t) { }
        public static void RestoreUncovered() { }
        public static void ApplyToScope(UnityEngine.Transform scope) { }
        public static void ApplyToAllLive() => RefreshAllText();
    }

    // the game re-runs ReadFontAssetDefinition when it (re)loads/rebuilds a font asset — that repaints
    // the atlas from the original source font. cold method, safe to hook: re-apply on a hijacked asset.
    [BetterFG.Utilities.BfgPatchGate(FontReplacementService.KEY_MASTER_ON)]
    [HarmonyLib.HarmonyPatch(typeof(TMP_FontAsset), "ReadFontAssetDefinition")]
    internal static class TMPFontAssetReadDefinitionPatch
    {
        [HarmonyLib.HarmonyPostfix]
        public static void Postfix(TMP_FontAsset __instance)
        {
            if (__instance == null || !FontReplacementService.MasterOnFast) return;
            if (FontReplacementService.IsRestoring) return;
            if (FontReplacementService.IsHijacked(__instance.GetInstanceID()))
                FontReplacementService.ReassertHijack(__instance);
        }
    }
}
