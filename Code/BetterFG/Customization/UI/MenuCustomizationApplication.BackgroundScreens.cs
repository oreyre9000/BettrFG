using System;
using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Customization.Player;
using System.Numerics;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;
using FGClient;

namespace BetterFG.Customization.UI
{
    public partial class MenuCustomizationApplication
    {
        private const string FGUI_CUSTOM_BACKDROP_PARENT_PATH = "3D Environment/Generic_UI_CurrentSeasonBackground_Container_MainMenu_Variant/Generic_UI_SeasonS11Background_Canvas_Variant/Mask";
        private const string FGUI_ORIGINAL_BACKDROP_PATH = "3D Environment/Generic_UI_CurrentSeasonBackground_Container_MainMenu_Variant/Generic_UI_SeasonS11Background_Canvas_Variant/Mask/Backdrop";
        private const string FGUI_ORIGINAL_CIRCLES_PATH = "3D Environment/Generic_UI_CurrentSeasonBackground_Container_MainMenu_Variant/Generic_UI_SeasonS11Background_Canvas_Variant/Mask/Circles";

        private Vector3 FG_UI_ORIGINAL_CIRCLES_PREFERREDLOCALPOS = new Vector3(0f, 0f, -95.4f);
        private bool _bgTweakApplied;

        public const string KEY_BG_ENABLED = "screen.fallforce.bg.enabled";

        // title screen background — same SeasonS11Background Mask as the menu one but under the
        // TitleScreen prefab. handled by ScreenBackgroundService (FallForce screen).
        private const string FGUI_TITLE_BACKDROP_PARENT_PATH = "UICanvas_Client_V2(Clone)/Default/Prefab_UI_TitleScreen(Clone)/Generic_UI_CurrentSeasonBackground_Container_Prefab/Generic_UI_SeasonS11Background_Canvas_Variant/Mask";

        // circles pattern — FallForce screen's pattern key (menu + title share it)
        public const string KEY_PATTERN_PATH = "screen.fallforce.pattern.path";
        // cached on first apply so remove can restore it. ONCE set, never overwritten by ApplyPatternFromSettings
        // (the reapply coroutine would otherwise re-read the live texture, which is our own custom one, and
        // RestorePattern would then restore the custom texture instead of the real original).
        private Texture _originalPatternTex;
        private bool _originalPatternCaptured;
        private Texture2D _appliedPatternTex; // the custom tex we last set, so we can destroy it on swap/remove
        private Material _circlesMatInstance;
        private int _circlesMatOwnerId;

        // menu background images — a library of saved images. every enabled entry gets its own quad
        // and renders simultaneously (not exclusive), keyed by entry name.
        private class BgImageRuntime
        {
            public GameObject go;
            public Material mat;
            public Texture2D tex;
            public string loadedPath;
        }
        private readonly Dictionary<string, BgImageRuntime> _bgImageRuntimes = new Dictionary<string, BgImageRuntime>();

        // bg image slider ranges + defaults — single source of truth (UI reads these too)
        public const float BG_IMG_POS_MIN = -10f;
        public const float BG_IMG_POS_MAX = 10f;
        public const float BG_IMG_POS_DEFAULT = 0f;
        public const float BG_IMG_ROT_DEFAULT = 0f;
        public const float BG_IMG_SCALE_MIN = 0f;
        public const float BG_IMG_SCALE_MAX = 15f;
        public const float BG_IMG_SCALE_DEFAULT = 5f;
        public const float BG_IMG_SCALE_AXIS_MIN = 0.1f;
        public const float BG_IMG_SCALE_AXIS_MAX = 3f;
        public const float BG_IMG_SCALE_AXIS_DEFAULT = 1f;

        public class BgImageEntry
        {
            public string entryName = "";
            public string path = "";
            public bool enabled = true;
            public float posX, posY, posZ;
            public float rotX, rotY, rotZ;
            public float scale = BG_IMG_SCALE_DEFAULT;
            public float scaleX = BG_IMG_SCALE_AXIS_DEFAULT;
            public float scaleY = BG_IMG_SCALE_AXIS_DEFAULT;
        }

        private const string KEY_BG_IMG_ENTRY_COUNT = "menu.bg.img.entryCount";
        private static string BgImgEK(int i, string f) => $"menu.bg.img.entry.{i}.{f}";

        public static List<BgImageEntry> LoadBgImageEntries()
        {
            var entries = new List<BgImageEntry>();
            int count = int.TryParse(SettingsService.Get(KEY_BG_IMG_ENTRY_COUNT, "0"), out int c) ? c : 0;
            for (int i = 0; i < count; i++)
            {
                entries.Add(new BgImageEntry
                {
                    entryName = SettingsService.Get(BgImgEK(i, "name"), "background " + i),
                    path = SettingsService.Get(BgImgEK(i, "path"), ""),
                    enabled = SettingsService.Get(BgImgEK(i, "enabled"), "1") == "1",
                    posX = ParseF(BgImgEK(i, "posX"), BG_IMG_POS_DEFAULT),
                    posY = ParseF(BgImgEK(i, "posY"), BG_IMG_POS_DEFAULT),
                    posZ = ParseF(BgImgEK(i, "posZ"), BG_IMG_POS_DEFAULT),
                    rotX = ParseF(BgImgEK(i, "rotX"), BG_IMG_ROT_DEFAULT),
                    rotY = ParseF(BgImgEK(i, "rotY"), BG_IMG_ROT_DEFAULT),
                    rotZ = ParseF(BgImgEK(i, "rotZ"), BG_IMG_ROT_DEFAULT),
                    scale = ParseF(BgImgEK(i, "scale"), BG_IMG_SCALE_DEFAULT),
                    scaleX = ParseF(BgImgEK(i, "scaleX"), BG_IMG_SCALE_AXIS_DEFAULT),
                    scaleY = ParseF(BgImgEK(i, "scaleY"), BG_IMG_SCALE_AXIS_DEFAULT),
                });
            }
            return entries;
        }

        public static void SaveBgImageEntries(List<BgImageEntry> entries)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(KEY_BG_IMG_ENTRY_COUNT, entries.Count.ToString());
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                SettingsService.Set(BgImgEK(i, "name"), e.entryName);
                SettingsService.Set(BgImgEK(i, "path"), e.path);
                SettingsService.Set(BgImgEK(i, "enabled"), e.enabled ? "1" : "0");
                SettingsService.Set(BgImgEK(i, "posX"), e.posX.ToString(ci));
                SettingsService.Set(BgImgEK(i, "posY"), e.posY.ToString(ci));
                SettingsService.Set(BgImgEK(i, "posZ"), e.posZ.ToString(ci));
                SettingsService.Set(BgImgEK(i, "rotX"), e.rotX.ToString(ci));
                SettingsService.Set(BgImgEK(i, "rotY"), e.rotY.ToString(ci));
                SettingsService.Set(BgImgEK(i, "rotZ"), e.rotZ.ToString(ci));
                SettingsService.Set(BgImgEK(i, "scale"), e.scale.ToString(ci));
                SettingsService.Set(BgImgEK(i, "scaleX"), e.scaleX.ToString(ci));
                SettingsService.Set(BgImgEK(i, "scaleY"), e.scaleY.ToString(ci));
            }
        }

        // one-time carry-over from the old single flat-key background image into entry 0 — otherwise
        // everyone who already had a menu background image loses it silently on update.
        private static void MigrateOldBgImageEntry()
        {
            if (SettingsService.Get("menu.bg.img.migrated", "false") == "true") return;
            SettingsService.Set("menu.bg.img.migrated", "true");

            string oldPath = SettingsService.Get("menu.bg.img.path", "");
            if (string.IsNullOrEmpty(oldPath)) return;
            if (int.TryParse(SettingsService.Get(KEY_BG_IMG_ENTRY_COUNT, "0"), out int c) && c > 0) return;

            var entry = new BgImageEntry
            {
                entryName = "background",
                path = oldPath,
                enabled = SettingsService.Get("menu.bg.img.enabled", "false") == "true",
                posX = ParseF("menu.bg.img.pos.x", BG_IMG_POS_DEFAULT),
                posY = ParseF("menu.bg.img.pos.y", BG_IMG_POS_DEFAULT),
                posZ = ParseF("menu.bg.img.pos.z", BG_IMG_POS_DEFAULT),
                scale = ParseF("menu.bg.img.scale", BG_IMG_SCALE_DEFAULT),
                scaleX = ParseF("menu.bg.img.scale.x", BG_IMG_SCALE_AXIS_DEFAULT),
                scaleY = ParseF("menu.bg.img.scale.y", BG_IMG_SCALE_AXIS_DEFAULT),
            };
            SaveBgImageEntries(new List<BgImageEntry> { entry });
        }

        // flips one entry's enabled flag and re-applies immediately so the menu picks it up live.
        public void SetBgImageEnabled(int index, bool enabled)
        {
            var entries = LoadBgImageEntries();
            if (index < 0 || index >= entries.Count) return;
            entries[index].enabled = enabled;
            SaveBgImageEntries(entries);
            ApplyImageBgFromSettings();
        }

        // gradient settings keys — these are the FallForce screen's keys (menu + title share them).
        // the Screen tab edits the same screen.fallforce.* keys via ScreenBackgroundService.
        public const string KEY_BG_TOP_R = "screen.fallforce.top.r";
        public const string KEY_BG_TOP_G = "screen.fallforce.top.g";
        public const string KEY_BG_TOP_B = "screen.fallforce.top.b";
        public const string KEY_BG_TOP_A = "screen.fallforce.top.a";
        public const string KEY_BG_BOT_R = "screen.fallforce.bot.r";
        public const string KEY_BG_BOT_G = "screen.fallforce.bot.g";
        public const string KEY_BG_BOT_B = "screen.fallforce.bot.b";
        public const string KEY_BG_BOT_A = "screen.fallforce.bot.a";
        public const string KEY_BG_BIAS = "screen.fallforce.bias";
        public const string KEY_BG_SMOOTH = "screen.fallforce.smooth";

        // one-shot: previously screen.fallforce.enabled drove BOTH the GO active state AND the
        // custom-colour apply. it's now split — bg.enabled drives the GO. for users who already
        // had the screen enabled, carry that state into the new bg.enabled so the bg stays on.
        private static void MigrateBgSplit()
        {
            if (SettingsService.Get("screen.bgsplit.migrated", "false") == "true") return;
            if (SettingsService.Get("screen.fallforce.enabled", "false") == "true")
                SettingsService.Set(KEY_BG_ENABLED, "true");
            SettingsService.Set("screen.bgsplit.migrated", "true");
        }

        // one-time copy of the old menu.bg.* gradient/pattern values into the new screen.fallforce.*
        // keys so existing setups keep their look after the migration to per-screen settings.
        private static void MigrateOldBgKeys()
        {
            if (SettingsService.Get("screen.migrated", "false") == "true") return;
            (string oldK, string newK)[] map =
            {
                ("menu.bg.top.r", KEY_BG_TOP_R), ("menu.bg.top.g", KEY_BG_TOP_G), ("menu.bg.top.b", KEY_BG_TOP_B),
                ("menu.bg.bot.r", KEY_BG_BOT_R), ("menu.bg.bot.g", KEY_BG_BOT_G), ("menu.bg.bot.b", KEY_BG_BOT_B),
                ("menu.bg.bias", KEY_BG_BIAS), ("menu.bg.smooth", KEY_BG_SMOOTH),
                ("menu.bg.enabled", "screen.fallforce.enabled"),
                ("menu.bg.pattern.path", "screen.fallforce.pattern.path"),
            };
            foreach (var (oldK, newK) in map)
            {
                var v = SettingsService.Get(oldK, "");
                if (!string.IsNullOrEmpty(v)) SettingsService.Set(newK, v);
            }
            SettingsService.Set("screen.migrated", "true");
        }

        // ── Menu background ───────────────────────────────────────────────────

        public void SpawnMenuBg()
        {
            if (!_bgTweakApplied) { TweakFallGuysBgForBetterfg(); _bgTweakApplied = true; }

            BetterFG.UI.Tabs.UIScalingTab.ApplyCanvasScalingFromSettings();
            ApplyImageBgFromSettings();

            // sun GO is fresh each menu enter — recapture its original rotation before applying.
            // ambient/sun must run after the game finishes its own scene lighting setup, else it
            // overwrites RenderSettings/the sun transform — so defer a frame.
            _sunSaved = false;
            _ambientSaved = false;
            _plinthColSaved = false;
            ForgetBg3dEnv();
            StartCoroutine(ApplyAmbientAndSunNextFrame().WrapToIl2Cpp());

        }

        // title screen uses the FallForce screen's gradient + pattern (same look as the menu).
        // the bg respawns over the first few hundred ms, so re-assert a few times to win the race.
        public void ReapplyTitleScreenBg()
        {
            StartCoroutine(ReapplyTitleScreenBgLoop().WrapToIl2Cpp());
        }

        private IEnumerator ReapplyTitleScreenBgLoop()
        {
            yield return null; // let the title screen finish building its background first
            for (int i = 0; i < 6; i++)
            {
                var maskGo = GameObject.Find(FGUI_TITLE_BACKDROP_PARENT_PATH);
                if (maskGo != null)
                    ScreenBackgroundService.Apply(ScreenBackgroundService.Screen.FallForce, maskGo.transform);
                yield return new WaitForSeconds(0.1f);
            }
        }

        public void ReapplyPatternNextFrame()
        {
            StartCoroutine(ReapplyPatternNextFrameLoop().WrapToIl2Cpp());
        }

        private IEnumerator ReapplyPatternNextFrameLoop()
        {
            for (int i = 0; i < 6; i++)
            {
                yield return null;
                ApplyPatternFromSettings();
            }
        }

        private IEnumerator ApplyAmbientAndSunNextFrame()
        {
            yield return null;
            ApplyAmbientFromSettings();
            ApplySunFromSettings();
            ApplyPlinthColorFromSettings();
            ApplyMenuBackground3DFromSettings();
            ApplyPatternFromSettings();
            ApplyGradientFromSettings();
            StartBg3dPoll();
        }

        // ── Menu background image ─────────────────────────────────────────────

        // fallback base only — the real base is PB_UI_Character's world position, cached once in the
        // OnMainMenuEntered postfix (content updates move it, same story as the cam). never re-cached.
        private static readonly Vector3 BG_IMG_BASE_WORLD_POS = new Vector3(0f, 3.5f, 4.3f);
        private Vector3 _bgImgBasePos = BG_IMG_BASE_WORLD_POS;
        private bool _bgImgBaseCached;

        public void CacheBgImageBase()
        {
            if (_bgImgBaseCached) return;
            var t = GameObject.Find("3D Environment/MainMenu_Environment/PlinthRig/CharacterAndPlinthHolder_Main/ENV_Plinth_MO/CharacterHolder/PB_UI_Character");
            if (t == null) return;
            _bgImgBasePos = t.transform.position;
            _bgImgBaseCached = true;
        }

        // Sprites/Default is picked first on purpose: it alpha-blends (real PNG transparency, unlike
        // an opaque Unlit shader) and culls neither face (visible regardless of the entry's rotation).
        private BgImageRuntime EnsureImageBgRuntime(string key)
        {
            // ?. against a destroyed Unity object returns true for == null, so this also recreates after scene unload
            if (_bgImageRuntimes.TryGetValue(key, out var existing) && existing.go != null) return existing;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "BetterFG_MenuBgImage_" + key;
            var col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // unparented + scene-bound: gets cleaned up when the menu scene unloads,
            // so it never leaks into rounds.
            quad.transform.position = _bgImgBasePos;
            quad.transform.rotation = Quaternion.identity;
            quad.transform.localScale = Vector3.one;
            quad.layer = LayerMask.NameToLayer("PlayerUI");

            var rt = new BgImageRuntime { go = quad };

            Shader shader = null;
            foreach (var name in new[] { "Sprites/Default", "Universal Render Pipeline/Unlit", "Unlit/Transparent", "Unlit/Texture", "UI/Default" })
            {
                shader = Shader.Find(name);
                if (shader != null) break;
            }
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = Color.white;
                var rend = quad.GetComponent<Renderer>();
                rend.material = mat;
                // re-read the renderer's actual instance — assigning .material may instantiate a copy
                rt.mat = rend.material;
                rt.mat.renderQueue = 3000; // transparent queue
            }

            _bgImageRuntimes[key] = rt;
            return rt;
        }

        private void DestroyBgImageRuntime(string key)
        {
            if (!_bgImageRuntimes.TryGetValue(key, out var rt)) return;
            if (rt.tex != null) Destroy(rt.tex);
            if (rt.go != null) Destroy(rt.go);
            _bgImageRuntimes.Remove(key);
        }

        // image bg is hidden whenever the customiser prefab canvas or store screen is active
        private const string CUSTOMISER_CANVAS_PATH = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Customiser/Prime_UI_Customizer_Prefab_Canvas(Clone)";
        private const string STORE_SCREEN_PATH = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Store";

        // true when the customiser prefab canvas is active in the hierarchy
        private static bool IsCustomiserOpen()
        {
            var go = GameObject.Find(CUSTOMISER_CANVAS_PATH);
            return go != null && go.activeInHierarchy;
        }

        private static bool IsStoreOpen()
        {
            var go = GameObject.Find(STORE_SCREEN_PATH);
            return go != null && go.activeInHierarchy;
        }

        private static void RefreshRuntimeVisibility(BgImageRuntime rt)
        {
            if (rt.go == null) return;
            // the PB tab deactivates the customiser canvas, so IsCustomiserOpen reads false there —
            // hide the custom menu image explicitly while our tab owns the screen.
            bool pbOpen = BetterFG.Features.QualificationTime.PBTabView.IsOpen;
            rt.go.SetActive(!pbOpen && !IsCustomiserOpen() && !IsStoreOpen());
        }

        public void RefreshImageBgVisibility()
        {
            foreach (var rt in _bgImageRuntimes.Values) RefreshRuntimeVisibility(rt);
        }

        public void HideImageBg()
        {
            foreach (var rt in _bgImageRuntimes.Values)
                if (rt.go != null) rt.go.SetActive(false);
        }

        private static void ApplyTextureToRuntime(BgImageRuntime rt, string path)
        {
            if (rt.mat == null) return;

            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                rt.mat.mainTexture = null;
                rt.loadedPath = null;
                return;
            }

            // scrubbing a transform slider re-calls this every frame with the SAME path — re-decoding
            // a high-res PNG off disk that often is what made dragging position/rotation/scale feel
            // laggy. only the transform changed, so skip the reload when the texture's already current.
            if (rt.tex != null && path == rt.loadedPath) return;

            try
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!tex.LoadImage(bytes)) { Plugin.Log.LogWarning($"not a decodable image: {System.IO.Path.GetFileName(path)}"); return; }
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Trilinear;
                tex.Apply(true, false);
                if (rt.tex != null) Destroy(rt.tex);
                rt.tex = tex;
                rt.loadedPath = path;

                foreach (var prop in new[] { "_MainTex", "_BaseMap", "_BaseColorMap", "_UnlitColorMap", "_Texture", "_Tex" })
                    if (rt.mat.HasProperty(prop))
                        rt.mat.SetTexture(prop, tex);
                rt.mat.mainTexture = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError($"menu bg image '{System.IO.Path.GetFileName(path)}' failed to load: {ex.Message}"); }
        }

        private void ApplyTransformToRuntime(BgImageRuntime rt, BgImageEntry e)
        {
            if (rt.go == null) return;
            rt.go.transform.position = _bgImgBasePos + new Vector3(e.posX, e.posY, e.posZ);
            rt.go.transform.rotation = Quaternion.Euler(e.rotX, e.rotY, e.rotZ);
            rt.go.transform.localScale = new Vector3(e.scale * e.scaleX, e.scale * e.scaleY, 1f);
        }

        // rebuilds the full set of visible quads from settings — one per enabled entry, keyed by name.
        // entries that are disabled or gone get their quad torn down.
        public void ApplyImageBgFromSettings()
        {
            var entries = LoadBgImageEntries();
            var keep = new HashSet<string>();

            foreach (var e in entries)
            {
                if (!e.enabled) continue;
                keep.Add(e.entryName);
                var rt = EnsureImageBgRuntime(e.entryName);
                ApplyTextureToRuntime(rt, e.path);
                ApplyTransformToRuntime(rt, e);
                RefreshRuntimeVisibility(rt);
            }

            var stale = new List<string>();
            foreach (var kv in _bgImageRuntimes)
                if (!keep.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var name in stale) DestroyBgImageRuntime(name);
        }

        // live scrub preview for the background image wizard. keyed by the entry's OWN name when
        // editing so the scrub updates that entry's real, already-visible quad in place instead of
        // spawning a second one alongside it; the wizard passes a scratch key instead when adding a
        // new entry, since no real quad exists under any name yet. ApplyImageBgFromSettings's
        // stale-runtime sweep (called whenever the wizard leaves, see MakeListTarget) tears the scratch
        // key down on its own — and for an edit, reapplies the on-disk (possibly still-old, if the user
        // backed out without saving) transform over whatever was scrubbed.
        public void PreviewBgImage(string key, string path, float posX, float posY, float posZ,
            float rotX, float rotY, float rotZ, float scale, float scaleX, float scaleY)
        {
            var rt = EnsureImageBgRuntime(key);
            ApplyTextureToRuntime(rt, path);
            ApplyTransformToRuntime(rt, new BgImageEntry
            {
                posX = posX, posY = posY, posZ = posZ,
                rotX = rotX, rotY = rotY, rotZ = rotZ,
                scale = scale, scaleX = scaleX, scaleY = scaleY
            });
            if (rt.go != null) rt.go.SetActive(true);
        }

        public void SetMenuBgEnabled(bool enabled)
        {
            SettingsService.Set(KEY_BG_ENABLED, enabled ? "true" : "false");
            ApplyGradientFromSettings();
        }

        public void TweakFallGuysBgForBetterfg()
        {
            GameObject.Find(FGUI_ORIGINAL_CIRCLES_PATH).transform.localPosition = FG_UI_ORIGINAL_CIRCLES_PREFERREDLOCALPOS;
        }

        // the "Mask" container that holds the live Backdrop + Circles images — same node
        // ScreenBackgroundService.ApplyToContainer targets. only present while the main menu/title
        // screen is up; the Background tab clones it as a live preview source.
        public static GameObject FindBackgroundContainer() => GameObject.Find(FGUI_CUSTOM_BACKDROP_PARENT_PATH);

        private Material EnsureCirclesMaterialInstance(UnityEngine.UI.Image img)
        {
            if (img == null || img.material == null) return null;
            int id = img.GetInstanceID();
            if (_circlesMatInstance == null || _circlesMatOwnerId != id)
            {
                _circlesMatInstance = new Material(img.material);
                img.material = _circlesMatInstance;
                _circlesMatOwnerId = id;
            }
            return _circlesMatInstance;
        }

        // cache the REAL original pattern texture exactly once per session. only safe to read off the
        // live material when we haven't applied a custom one yet — once we have, GetTexture("_Pattern")
        // would just return our own custom tex back to us.
        private Color _originalCirclesColor = Color.white;

        private void CaptureOriginalPattern(UnityEngine.UI.Image img)
        {
            if (_originalPatternCaptured || _appliedPatternTex != null || img == null || img.material == null) return;
            var mat = EnsureCirclesMaterialInstance(img);
            if (mat == null) return;
            _originalPatternTex = mat.GetTexture("_Pattern");
            _originalCirclesColor = img.color;
            _originalPatternCaptured = true;
        }

        // the real, uncorrupted default circles pattern — for the Background tab's preview clone,
        // which can't just read the live Circles material directly (if a custom pattern is already
        // active this session, that live read would return OUR OWN texture, not the game's default).
        public Texture EnsureOriginalCirclesPatternCaptured()
        {
            CaptureOriginalPattern(GameObject.Find(FGUI_ORIGINAL_CIRCLES_PATH)?.GetComponent<UnityEngine.UI.Image>());
            return _originalPatternTex;
        }

        public Color EnsureOriginalCirclesColorCaptured()
        {
            CaptureOriginalPattern(GameObject.Find(FGUI_ORIGINAL_CIRCLES_PATH)?.GetComponent<UnityEngine.UI.Image>());
            return _originalCirclesColor;
        }

        // MainMenuBackgroundViewModel fades the Backdrop image's alpha to swap between the 3D
        // environment and the flat 2D background, per screen. we only ever own rgb + the sprite;
        // Backdrop is the only image the game fades, so Circles has to follow it or the pattern
        // stays up over the 3D background after the 2D one is gone.
        private static UnityEngine.UI.Image _patternImg;
        private static UnityEngine.UI.Image _patternBackdropImg;
        private static Color _patternBaseColour = Color.white;

        internal static void SyncPatternAlpha()
        {
            float a = _patternBaseColour.a * _patternBackdropImg.color.a;
            if (Mathf.Approximately(_patternImg.color.a, a)) return;
            _patternImg.color = new Color(_patternBaseColour.r, _patternBaseColour.g, _patternBaseColour.b, a);
        }

        public void ApplyPatternFromSettings()
        {
            var circlesGo = GameObject.Find(FGUI_ORIGINAL_CIRCLES_PATH);
            if (circlesGo == null) return;

            var img = circlesGo.GetComponent<UnityEngine.UI.Image>();
            if (img == null || img.material == null) return;

            CaptureOriginalPattern(img);
            var mat = EnsureCirclesMaterialInstance(img);
            if (mat == null) return;

            string value = SettingsService.Get(KEY_PATTERN_PATH, "");
            var tex = ScreenBackgroundService.LoadPatternTexture(value);
            if (tex != null)
            {
                tex = ScreenBackgroundService.MatchPatternSize(tex, _originalPatternTex);
                if (_appliedPatternTex != null) Destroy(_appliedPatternTex);
                _appliedPatternTex = tex;
                mat.SetTexture("_Pattern", tex);
            }

            _patternImg = img;
            _patternBackdropImg = circlesGo.transform.parent.Find("Backdrop").GetComponent<UnityEngine.UI.Image>();
            _patternBaseColour = ScreenBackgroundService.PatternColor(ScreenBackgroundService.Screen.FallForce);
            if (circlesGo.GetComponent<BetterFG.Patches.MenuPatternAlphaMirror>() == null)
                circlesGo.AddComponent<BetterFG.Patches.MenuPatternAlphaMirror>();
            SyncPatternAlpha();
        }

        public void RestorePattern()
        {
            // nothing to do if we never applied a custom in the first place
            if (!_originalPatternCaptured && _appliedPatternTex == null) return;

            var circlesGo = GameObject.Find(FGUI_ORIGINAL_CIRCLES_PATH);
            if (circlesGo == null) return;
            var img = circlesGo.GetComponent<UnityEngine.UI.Image>();
            if (img == null || img.material == null) return;

            var mat = EnsureCirclesMaterialInstance(img);
            if (mat == null) return;
            var mirror = circlesGo.GetComponent<BetterFG.Patches.MenuPatternAlphaMirror>();
            if (mirror != null) Destroy(mirror);

            mat.SetTexture("_Pattern", _originalPatternTex);
            img.color = _originalCirclesColor;

            if (_appliedPatternTex != null)
            {
                Destroy(_appliedPatternTex);
                _appliedPatternTex = null;
            }
        }

        private Sprite _originalBackdropSprite;
        private bool _originalBackdropCaptured;
        private Texture2D _appliedBackdropTex;

        private void CaptureOriginalBackdrop(UnityEngine.UI.Image img)
        {
            if (_originalBackdropCaptured || _appliedBackdropTex != null || img == null) return;
            _originalBackdropSprite = img.sprite;
            _originalBackdropCaptured = true;
        }

        public Sprite EnsureOriginalBackdropSpriteCaptured()
        {
            CaptureOriginalBackdrop(GameObject.Find(FGUI_ORIGINAL_BACKDROP_PATH)?.GetComponent<UnityEngine.UI.Image>());
            return _originalBackdropSprite;
        }

        public void ApplyGradient(Color top, Color bot, float bias, float smoothness)
        {
            var backdropGo = GameObject.Find(FGUI_ORIGINAL_BACKDROP_PATH);
            if (backdropGo == null) return;
            var img = backdropGo.GetComponent<UnityEngine.UI.Image>();
            if (img == null) return;

            CaptureOriginalBackdrop(img);

            var tex = ScreenBackgroundService.BuildGradientTex(top, bot, bias, smoothness);
            if (_appliedBackdropTex != null) Destroy(_appliedBackdropTex);
            _appliedBackdropTex = tex;
            img.sprite = Sprite.Create(tex, new Rect(0, 0, 1, tex.height), new UnityEngine.Vector2(0.5f, 0.5f));
            img.color = new Color(1f, 1f, 1f, img.color.a);
        }

        public void RestoreBackdrop()
        {
            if (!_originalBackdropCaptured && _appliedBackdropTex == null) return;

            var backdropGo = GameObject.Find(FGUI_ORIGINAL_BACKDROP_PATH);
            if (backdropGo == null) return;
            var img = backdropGo.GetComponent<UnityEngine.UI.Image>();
            if (img == null) return;

            img.sprite = _originalBackdropSprite;
            img.color = new Color(1f, 1f, 1f, img.color.a);

            if (_appliedBackdropTex != null)
            {
                Destroy(_appliedBackdropTex);
                _appliedBackdropTex = null;
            }
        }

        public void ApplyGradientFromSettings()
        {
            if (SettingsService.Get(KEY_BG_ENABLED, "false") != "true")
            {
                RestoreBackdrop();
                return;
            }

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float Parse(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            var top = new Color(Parse(KEY_BG_TOP_R, 0f), Parse(KEY_BG_TOP_G, 0f), Parse(KEY_BG_TOP_B, 0f), Parse(KEY_BG_TOP_A, 1f));
            var bot = new Color(Parse(KEY_BG_BOT_R, 1f), Parse(KEY_BG_BOT_G, 1f), Parse(KEY_BG_BOT_B, 1f), Parse(KEY_BG_BOT_A, 1f));
            float bias = Parse(KEY_BG_BIAS, 0f);
            float smooth = Parse(KEY_BG_SMOOTH, 1f);
            ApplyGradient(top, bot, bias, smooth);
        }

        // ── Lobby background ─────────────────────────────────────────────────

        // lobby bg texture cache: instanceID → original sprite (cached once, never destroyed)
        private readonly Dictionary<int, UnityEngine.Sprite> _lobbyTexOriginals = new Dictionary<int, UnityEngine.Sprite>();

        // lobby bg color cache: instanceID → original color (cached once, never overwritten)
        private readonly Dictionary<int, Color> _lobbyColorOriginals = new Dictionary<int, Color>();

        // falling screen (lobby bg) — only ever driven by the falling-screen custom slot colours now.
        // the Foreground cyan/colour toggles no longer touch the lobby bg at all.
        public void ApplyLobbyBGForeground()
        {
            if (SettingsService.Get(KEY_LOBBYBG_ENABLED, "false").Equals("true"))
                ApplyLobbyBgCustomColors(
                    new Color(ParseF(KEY_LOBBYBG_SLOT0_R, 0f), ParseF(KEY_LOBBYBG_SLOT0_G, 0f), ParseF(KEY_LOBBYBG_SLOT0_B, 1f)),
                    new Color(ParseF(KEY_LOBBYBG_SLOT1_R, 0f), ParseF(KEY_LOBBYBG_SLOT1_G, 0.5f), ParseF(KEY_LOBBYBG_SLOT1_B, 1f)),
                    new Color(ParseF(KEY_LOBBYBG_SLOT2_R, 0.8f), ParseF(KEY_LOBBYBG_SLOT2_G, 0.8f), ParseF(KEY_LOBBYBG_SLOT2_B, 1f)));
            else
                RevertLobbyBGForeground();
        }

        public void RevertLobbyBGForeground()
        {
            if (_lobbyTexOriginals.Count == 0) return;

            var lobbyRoot = GameObject.Find("Menu_Screen_Lobby(Clone)/BackgroundCanvas/Prefab_UI_Lobby");
            if (lobbyRoot == null) return;

            var images = lobbyRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            foreach (var img in images)
            {
                if (img == null) continue;
                int id = img.GetInstanceID();
                if (_lobbyTexOriginals.TryGetValue(id, out var origSprite))
                    img.sprite = origSprite;
                if (_lobbyColorOriginals.TryGetValue(id, out var origColor))
                    img.color = origColor;
            }

            foreach (var id in _lobbyTexOriginals.Keys) { _fgOriginals.Remove(id); _fgTouchedImages.Remove(id); }
            _lobbyTexOriginals.Clear();
            _lobbyColorOriginals.Clear();
        }

        public static IEnumerator ApplyLobbyBGForegroundNextFrame()
        {
            yield return null;
            Instance?.ApplyLobbyBGForeground();
        }

        // keys for lobby bg custom slot colours
        // enabled gate for the falling-screen (lobby bg) custom colours — independent of the
        // Foreground cyan toggle now that it has its own UI in the UI tab's Background section.
        public const string KEY_LOBBYBG_ENABLED = "menu.lobbybg.enabled";
        public const string KEY_LOBBYBG_SLOT0_R = "menu.lobbybg.slot0.r";
        public const string KEY_LOBBYBG_SLOT0_G = "menu.lobbybg.slot0.g";
        public const string KEY_LOBBYBG_SLOT0_B = "menu.lobbybg.slot0.b";
        public const string KEY_LOBBYBG_SLOT1_R = "menu.lobbybg.slot1.r";
        public const string KEY_LOBBYBG_SLOT1_G = "menu.lobbybg.slot1.g";
        public const string KEY_LOBBYBG_SLOT1_B = "menu.lobbybg.slot1.b";
        public const string KEY_LOBBYBG_SLOT2_R = "menu.lobbybg.slot2.r";
        public const string KEY_LOBBYBG_SLOT2_G = "menu.lobbybg.slot2.g";
        public const string KEY_LOBBYBG_SLOT2_B = "menu.lobbybg.slot2.b";

        // scans Prefab_UI_Lobby images and returns up to 3 dominant clustered colours from originals
        // slot indices: 0=DarkBlue, 1=MedBlue, 2=LightBlue
        private static readonly string[] LobbyBgSlotNames = { "DarkBlue", "MedBlue", "LightBlue" };

        public static Transform FindLobbyBgPreviewSource()
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == "Menu_Screen_Lobby(Clone)")
                {
                    var bg = go.transform.Find("BackgroundCanvas");
                    if (bg != null) return bg;
                }
            return null;
        }

        public Color TrueLobbyBgColor(UnityEngine.UI.Image img) =>
            img != null && _lobbyColorOriginals.TryGetValue(img.GetInstanceID(), out var c) ? c : (img != null ? img.color : Color.white);

        public UnityEngine.Sprite TrueLobbyBgSprite(UnityEngine.UI.Image img) =>
            img != null && _lobbyTexOriginals.TryGetValue(img.GetInstanceID(), out var s) ? s : (img != null ? img.sprite : null);

        public static int LobbyBgSlotIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < LobbyBgSlotNames.Length; i++)
                if (name.Contains(LobbyBgSlotNames[i])) return i;
            return -1;
        }

        public Color[] ScanLobbyBgColors()
        {
            var lobbyRoot = GameObject.Find("Menu_Screen_Lobby(Clone)/BackgroundCanvas/Prefab_UI_Lobby");
            var result = new Color[] { new Color(0.05f, 0.1f, 0.3f), new Color(0.1f, 0.3f, 0.7f), new Color(0.3f, 0.6f, 1f) };
            if (lobbyRoot == null) return result;

            var images = lobbyRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);

            for (int slot = 0; slot < LobbyBgSlotNames.Length; slot++)
            {
                float r = 0f, g = 0f, b = 0f;
                int count = 0;
                foreach (var img in images)
                {
                    if (img == null || !img.gameObject.name.Contains(LobbyBgSlotNames[slot])) continue;
                    int id = img.GetInstanceID();
                    Color c = _lobbyColorOriginals.TryGetValue(id, out var orig) ? orig : img.color;
                    if (c.a < 0.05f) continue;
                    r += c.r; g += c.g; b += c.b;
                    count++;
                }
                if (count > 0)
                    result[slot] = new Color(r / count, g / count, b / count);
            }

            return result;
        }

        // applies 3 custom slot colours to lobby bg images by name-based group match (DarkBlue/MedBlue/LightBlue)
        public void ApplyLobbyBgCustomColors(Color slot0, Color slot1, Color slot2)
        {
            var lobbyRoot = GameObject.Find("Menu_Screen_Lobby(Clone)/BackgroundCanvas/Prefab_UI_Lobby");
            if (lobbyRoot == null) return;

            var slotColors = new Color[] { slot0, slot1, slot2 };
            var images = lobbyRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);

            foreach (var img in images)
            {
                if (img == null) continue;

                int slotIdx = -1;
                for (int k = 0; k < LobbyBgSlotNames.Length; k++)
                    if (img.gameObject.name.Contains(LobbyBgSlotNames[k])) { slotIdx = k; break; }
                if (slotIdx < 0) continue;

                int id = img.GetInstanceID();
                if (!_lobbyColorOriginals.ContainsKey(id))
                    _lobbyColorOriginals[id] = img.color;
                if (!_lobbyTexOriginals.ContainsKey(id))
                    _lobbyTexOriginals[id] = img.sprite;

                Color original = _lobbyColorOriginals[id];
                if (original.a < 0.05f) continue;

                img.sprite = null;
                var target = slotColors[slotIdx];
                img.color = new Color(target.r, target.g, target.b, original.a);
            }

        }

        // ── Creative (level browser) background ────────────────────────────────
        // Generic_UI_CreativeBackground_Prefab_Canvas is a flat paper-craft UI with named image
        // groups, not a gradient. one colour per slot; Drawings also covers the Grid image.
        // originals cached per Image id so remove restores the game's own colours.
        public enum CreativeSlot { Backdrop, Glows, Drawings, Vignette }
        public const string KEY_CREATIVE_ENABLED = "screen.creative.enabled";

        public static Color CreativeSlotDefault(CreativeSlot slot) =>
              slot == CreativeSlot.Backdrop ? new Color(0.98f, 0.93f, 0.82f)
            : slot == CreativeSlot.Glows ? new Color(1f, 0.98f, 0.9f)
            : slot == CreativeSlot.Drawings ? new Color(0.2f, 0.3f, 0.45f)
            : Color.black;

        public static Color CreativeSlotColor(CreativeSlot slot)
        {
            string k = $"screen.creative.{slot}";
            Color d = CreativeSlotDefault(slot);
            return new Color(ParseF(k + ".r", d.r), ParseF(k + ".g", d.g), ParseF(k + ".b", d.b));
        }

        private readonly Dictionary<int, Color> _creativeOriginals = new Dictionary<int, Color>();

        // the prefab shows up in two spots, both full-path pinned (the level editor's other variants
        // share the name): the level browser popup, and the level-editor menu backdrop on the
        // world-space CameraRig (view index 3 of the MainMenuBuilder switcher).
        private static Transform CreativeBrowserCanvas()
        {
            var go = GameObject.Find("UICanvas_Client_V2(Clone)/Popup/Prime_UI_LE_LevelBrowser(Clone)/Generic_UI_CreativeBackground_Prefab_Canvas");
            return go != null ? go.transform : null;
        }

        public static Transform CreativeEditorCanvas()
        {
            var go = GameObject.Find("CameraRig/VirtualCameras/MainMenu_LevelEditor/Generic_UI_CreativeBackground_Prefab_Canvas");
            return go != null ? go.transform : null;
        }

        // GameObject.Find only ever returns active objects, so it comes up empty while the level
        // editor view isn't the one showing. the CameraRig itself is always up, so walk the rest of
        // the (possibly inactive) path off it — same trick ShowSelectorBg uses. preview-only: the real
        // apply path doesn't need this because the editor canvas' own OnEnable applier repaints it.
        public static Transform FindCreativePreviewSource()
        {
            var rig = GameObject.Find("CameraRig");
            return rig == null ? null : rig.transform.Find("VirtualCameras/MainMenu_LevelEditor/Generic_UI_CreativeBackground_Prefab_Canvas");
        }

        // every named colour-slot graphic on a creative background canvas — shared by the real apply
        // path and the Background tab's preview clone, which paints the same slots with its own
        // (unsaved) colours instead of the settings-backed CreativeSlotColor.
        public static void ForEachCreativeSlotGraphic(Transform canvas, Action<UnityEngine.UI.Graphic, CreativeSlot> visit)
        {
            if (canvas == null) return;
            void One(Transform t, CreativeSlot slot)
            {
                var g = t != null ? t.GetComponent<UnityEngine.UI.Graphic>() : null;
                if (g != null) visit(g, slot);
            }
            void Many(Transform parent, CreativeSlot slot)
            {
                if (parent == null) return;
                for (int i = 0; i < parent.childCount; i++) One(parent.GetChild(i), slot);
            }
            One(canvas.Find("Backdrop"), CreativeSlot.Backdrop);
            Many(canvas.Find("Glows"), CreativeSlot.Glows);
            One(canvas.Find("Grid"), CreativeSlot.Drawings);
            Many(canvas.Find("Drawings"), CreativeSlot.Drawings);
            One(canvas.Find("Vignette"), CreativeSlot.Vignette);
        }

        // the real original colour for a live creative graphic — reads the captured cache if this
        // graphic has already been recoloured this session (so the preview clone doesn't inherit our
        // own custom colour as if it were the default), falls back to its current colour otherwise.
        public Color TrueCreativeColor(UnityEngine.UI.Graphic g) =>
            g != null && _creativeOriginals.TryGetValue(g.GetInstanceID(), out var c) ? c : (g != null ? g.color : Color.white);

        public void ApplyCreativeBg(Transform canvas) =>
            ForEachCreativeSlotGraphic(canvas, (g, slot) =>
            {
                int id = g.GetInstanceID();
                if (!_creativeOriginals.ContainsKey(id)) _creativeOriginals[id] = g.color;
                var c = CreativeSlotColor(slot);
                g.color = new Color(c.r, c.g, c.b, g.color.a);
            });

        public void RevertCreativeBg(Transform canvas)
        {
            if (canvas == null) return;
            foreach (var g in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                if (g != null && _creativeOriginals.TryGetValue(g.GetInstanceID(), out var c)) g.color = c;
        }

        // apply if enabled, else revert — for one creative canvas. the editor canvas' OnEnable applier
        // and the Screen tab both go through here.
        public void RefreshCreativeCanvas(Transform canvas)
        {
            if (canvas == null) return;
            if (SettingsService.Get(KEY_CREATIVE_ENABLED, "false") == "true") ApplyCreativeBg(canvas);
            else RevertCreativeBg(canvas);
        }

        // Screen tab Apply/Remove/toggle — hit whichever creative canvas is up right now
        public void ReapplyCreativeBgLive()
        {
            RefreshCreativeCanvas(CreativeBrowserCanvas());
            RefreshCreativeCanvas(CreativeEditorCanvas());
        }

    }
}
