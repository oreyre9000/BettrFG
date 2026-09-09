using System;
using System.Collections.Generic;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.UI
{
    // per-screen gradient + circles-pattern customisation. each "screen" (the main menu / title
    // screen, the various loading screens) has its own SeasonS11Background with a Backdrop Image
    // and a Circles Image. we bake a gradient texture onto the Backdrop and swap the Circles
    // pattern texture. every screen stores its own colours + pattern under screen.<id>.* keys.
    //
    // no quad, no shader material — just textures slapped onto the existing UI Images, so it works
    // the same on the 3D menu background and the flat UI loading-screen backgrounds.
    public static class ScreenBackgroundService
    {
        public enum Screen
        {
            FallForce,    // main menu + title screen
            LoadingLevel, // loading a normal (non-final) round
            FinalRound,   // loading the final round
            Explore,      // UP loading screen (Explore)
            ShowSelector, // the S10 show-selector screen backdrop
        }

        public static string Id(Screen s)
        {
            switch (s)
            {
                case Screen.LoadingLevel: return "loading";
                case Screen.FinalRound: return "finalround";
                case Screen.Explore: return "explore";
                case Screen.ShowSelector: return "showselector";
                default: return "fallforce";
            }
        }

        public static string Label(Screen s)
        {
            switch (s)
            {
                case Screen.LoadingLevel: return "Loading Level";
                case Screen.FinalRound: return "Loading Final Round";
                case Screen.Explore: return "Explore";
                case Screen.ShowSelector: return "Show Selector";
                default: return "Fall Force";
            }
        }

        // settings keys for one screen
        public static string KeyEnabled(Screen s) => $"screen.{Id(s)}.enabled";
        public static string KeyTopR(Screen s) => $"screen.{Id(s)}.top.r";
        public static string KeyTopG(Screen s) => $"screen.{Id(s)}.top.g";
        public static string KeyTopB(Screen s) => $"screen.{Id(s)}.top.b";
        public static string KeyBotR(Screen s) => $"screen.{Id(s)}.bot.r";
        public static string KeyBotG(Screen s) => $"screen.{Id(s)}.bot.g";
        public static string KeyBotB(Screen s) => $"screen.{Id(s)}.bot.b";
        public static string KeyBias(Screen s) => $"screen.{Id(s)}.bias";
        public static string KeySmooth(Screen s) => $"screen.{Id(s)}.smooth";
        public static string KeyPattern(Screen s) => $"screen.{Id(s)}.pattern.path";
        public static string KeyPatternR(Screen s) => $"screen.{Id(s)}.pattern.r";
        public static string KeyPatternG(Screen s) => $"screen.{Id(s)}.pattern.g";
        public static string KeyPatternB(Screen s) => $"screen.{Id(s)}.pattern.b";
        public static string KeyPatternA(Screen s) => $"screen.{Id(s)}.pattern.a";
        public static Color PatternColor(Screen s) =>
            new Color(Get(KeyPatternR(s), 1f), Get(KeyPatternG(s), 1f), Get(KeyPatternB(s), 1f), Get(KeyPatternA(s), 0.0588f));

        // ── pattern library ─────────────────────────────────────────────────
        // KeyPattern's value is one of: "" (Default — screen's own pattern, untouched), PatternNone
        // (an explicit blank/transparent override), "builtin:<id>" (one of the bundled seasonal
        // patterns, loaded from the embedded resource), or a raw file path (Browse/user-added).
        public const string PatternNone = "none";
        private const string BuiltinPrefix = "builtin:";
        private const string BuiltinResourcePrefix = "BetterFG.assets.ui.uiscreen.patterns.";

        public static readonly (string id, string label)[] BuiltinPatterns =
        {
            ("s1", "Season 1"), ("s2", "Season 2"), ("s3", "Season 3"),
            ("s4", "Season 4"), ("s5", "Season 5"), ("s6", "Season 6"), ("s7", "Season 7"),
            ("s8", "Season 8"), ("s9", "Season 9"), ("s10", "Season 10"),
        };

        public static string BuiltinKey(string id) => BuiltinPrefix + id;
        public static Texture2D LoadBuiltinTexture(string id) =>
            EmbeddedResourceandUnity.LoadTexture(BuiltinResourcePrefix + id + ".png");

        // display name for whatever's stored under KeyPattern(s)
        public static string PatternDisplayName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Default";
            if (value == PatternNone) return "None";
            if (value.StartsWith(BuiltinPrefix))
            {
                string id = value.Substring(BuiltinPrefix.Length);
                foreach (var (bid, label) in BuiltinPatterns) if (bid == id) return label;
                return id;
            }
            return System.IO.Path.GetFileName(value);
        }

        // resolves a KeyPattern value into a texture to push onto the Circles material. null means
        // "leave whatever's there alone" — Default, or a builtin/file that failed to load.
        public static Texture2D LoadPatternTexture(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (value == PatternNone)
            {
                var blank = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                blank.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                blank.Apply();
                return blank;
            }
            if (value.StartsWith(BuiltinPrefix))
            {
                // LoadBuiltinTexture goes through the shared embedded-resource loader, which sets
                // Clamp on purpose for its normal callers (icons/thumbnails that must not wrap) — a
                // pattern needs to actually tile, so force it back to Repeat here, not in the shared
                // loader (UIPatternPickerTab's thumbnail use of the same call still wants Clamp).
                var built = LoadBuiltinTexture(value.Substring(BuiltinPrefix.Length));
                if (built != null) built.wrapMode = TextureWrapMode.Repeat;
                return built;
            }
            if (!System.IO.File.Exists(value)) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(System.IO.File.ReadAllBytes(value));
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.Apply();
                return tex;
            }
            catch (Exception ex) { Plugin.Log.LogError("ScreenBg: pattern load failed: " + ex.Message); return null; }
        }

        // the Circles shader tiles _Pattern at whatever repeat rate it was authored for the game's own
        // (small, fixed-size) default pattern texture — that rate doesn't follow the swapped-in texture,
        // so a differently-sized custom/builtin pattern shows as one stretched copy instead of repeating.
        // resample it to the default's pixel size so it tiles the same way the vanilla art did.
        public static Texture2D MatchPatternSize(Texture2D src, Texture reference)
        {
            if (src == null || reference == null) return src;
            if (src.width == reference.width && src.height == reference.height) return src;

            var rt = RenderTexture.GetTemporary(reference.width, reference.height);
            var prevActive = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var resized = new Texture2D(reference.width, reference.height, TextureFormat.RGBA32, false);
            resized.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            resized.wrapMode = TextureWrapMode.Repeat;
            resized.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);

            UnityEngine.Object.Destroy(src);
            return resized;
        }

        // user-added custom patterns — a flat, shared library (not per-screen), so a pattern you add
        // while looking at one screen shows up as a pickable tile for every other screen too.
        private const string KEY_CUSTOM_PATTERN_COUNT = "screen.pattern.custom.count";
        private static string CustomPatternKey(int i) => $"screen.pattern.custom.{i}.path";

        public static List<string> LoadCustomPatterns()
        {
            var list = new List<string>();
            int count = int.TryParse(SettingsService.Get(KEY_CUSTOM_PATTERN_COUNT, "0"), out int c) ? c : 0;
            for (int i = 0; i < count; i++)
            {
                var p = SettingsService.Get(CustomPatternKey(i), "");
                if (!string.IsNullOrEmpty(p)) list.Add(p);
            }
            return list;
        }

        public static void SaveCustomPatterns(List<string> paths)
        {
            SettingsService.Set(KEY_CUSTOM_PATTERN_COUNT, paths.Count.ToString());
            for (int i = 0; i < paths.Count; i++) SettingsService.Set(CustomPatternKey(i), paths[i]);
        }

        public static void AddCustomPattern(string path)
        {
            var list = LoadCustomPatterns();
            if (!list.Contains(path)) list.Add(path);
            SaveCustomPatterns(list);
        }

        public static void RemoveCustomPattern(string path)
        {
            var list = LoadCustomPatterns();
            list.Remove(path);
            SaveCustomPatterns(list);
        }

        // the pattern image is "Circles" on every screen's Mask except FinalRoundBackground's Area,
        // which names the same slot "Pattern" instead — confirmed live via FGRuntimeConsole.
        public static Transform FindCirclesChild(Transform container) =>
            container.Find("Circles") ?? container.Find("Pattern");

        private static List<Transform> PatternChildren(Transform container)
        {
            var list = new List<Transform>();
            var circles = container.Find("Circles");
            if (circles != null) list.Add(circles);
            var pattern = container.Find("Pattern");
            if (pattern != null) list.Add(pattern);
            return list;
        }

        private static float Get(string key, float def)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return float.TryParse(SettingsService.Get(key, def.ToString(ci)),
                System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;
        }

        public static Color TopColor(Screen s) => new Color(Get(KeyTopR(s), 0f), Get(KeyTopG(s), 0f), Get(KeyTopB(s), 0f));
        public static Color BotColor(Screen s) => new Color(Get(KeyBotR(s), 1f), Get(KeyBotG(s), 1f), Get(KeyBotB(s), 1f));
        public static float Bias(Screen s) => Get(KeyBias(s), 0f);
        public static float Smooth(Screen s) => Get(KeySmooth(s), 1f);
        public static bool Enabled(Screen s) => SettingsService.Get(KeyEnabled(s), "false") == "true";

        // build a 1xH vertical gradient texture matching the old shader feel (bias offset, pow smooth)
        public static Texture2D BuildGradientTex(Color top, Color bot, float bias, float smooth)
        {
            const int h = 256;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);
                float k = Mathf.Clamp01(t + bias * 0.5f);
                k = Mathf.Pow(k, Mathf.Max(0.1f, smooth));
                tex.SetPixel(0, y, Color.Lerp(bot, top, k));
            }
            tex.Apply();
            return tex;
        }

        // cached originals so toggling a screen off restores the game's own backdrop sprite + pattern.
        // keyed by the Image's instance id. backdrop sprite + colour, and the circles _Pattern texture.
        private static readonly Dictionary<int, Sprite> _origBackdropSprite = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Color> _origBackdropColor = new Dictionary<int, Color>();
        private static readonly Dictionary<int, Texture> _origPattern = new Dictionary<int, Texture>();
        private static readonly Dictionary<int, Sprite> _origPatternSprite = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Image.Type> _origPatternType = new Dictionary<int, Image.Type>();
        private static readonly Dictionary<int, Color> _origCirclesColor = new Dictionary<int, Color>();

        // FinalRoundBackground's sprite-driven "Pattern" node has no built-in tiling (it's a single
        // "crown-tile" sprite stretched over the whole Area) — our season patterns are small repeating
        // motifs, so a custom pattern needs Image.Tiled + a much lower PPU multiplier to read as a
        // pattern instead of one giant stretched image. lower multiplier = bigger/fewer tiles.
        public const float FinalRoundPatternTileScale = 0.5f;
        public const float FallForcePatternTileScale = 0.08333333f;
        // Image.material does NOT auto-instance like Renderer.material — writing straight to it mutates
        // the shared Material asset every Circles instance (every screen) reads from. instance it once
        // per Circles Image and reuse that instance for apply/revert.
        private static readonly Dictionary<int, Material> _circlesMatInstance = new Dictionary<int, Material>();

        private static Material EnsureCirclesMaterialInstance(Image cimg)
        {
            if (cimg == null || cimg.material == null) return null;
            int id = cimg.GetInstanceID();
            if (!_circlesMatInstance.TryGetValue(id, out var mat) || mat == null)
            {
                mat = new Material(cimg.material);
                cimg.material = mat;
                _circlesMatInstance[id] = mat;
            }
            return mat;
        }

        // the SAME originals, but keyed by Screen instead of by (transient, per-instance) id — most of
        // these screens' containers only exist for the few seconds their loading screen is up, so the
        // per-instance dictionaries above are useless once that instance is destroyed. this survives:
        // captured once, the first time we ever see each screen's container, whether or not custom
        // colours are enabled — so the Background tab can preview a screen's real default look without
        // needing that screen to be live right now.
        private static readonly Dictionary<Screen, Sprite> _screenDefaultSprite = new Dictionary<Screen, Sprite>();
        private static readonly Dictionary<Screen, Color> _screenDefaultColor = new Dictionary<Screen, Color>();
        private static readonly Dictionary<Screen, Texture> _screenDefaultPattern = new Dictionary<Screen, Texture>();
        private static readonly Dictionary<Screen, Color> _screenDefaultCirclesColor = new Dictionary<Screen, Color>();

        public static void CacheScreenDefault(Screen s, Transform container)
        {
            if (container == null || _screenDefaultSprite.ContainsKey(s)) return;

            var backdrop = container.Find("Backdrop");
            var img = backdrop != null ? backdrop.GetComponent<Image>() : null;
            if (img == null) return;
            int id = img.GetInstanceID();
            _screenDefaultSprite[s] = _origBackdropSprite.TryGetValue(id, out var os) ? os : img.sprite;
            _screenDefaultColor[s] = _origBackdropColor.TryGetValue(id, out var oc) ? oc : img.color;

            var circles = FindCirclesChild(container);
            var cimg = circles != null ? circles.GetComponent<Image>() : null;
            if (cimg != null && cimg.material != null)
            {
                int cid = cimg.GetInstanceID();
                if (cimg.material.HasProperty("_Pattern"))
                    _screenDefaultPattern[s] = _origPattern.TryGetValue(cid, out var op) ? op : cimg.material.GetTexture("_Pattern");
                _screenDefaultCirclesColor[s] = _origCirclesColor.TryGetValue(cid, out var occ) ? occ : cimg.color;
            }
        }

        public static bool TryGetScreenDefault(Screen s, out Sprite sprite, out Color color, out Texture pattern)
        {
            sprite = _screenDefaultSprite.TryGetValue(s, out var sp) ? sp : null;
            color = _screenDefaultColor.TryGetValue(s, out var c) ? c : Color.white;
            pattern = _screenDefaultPattern.TryGetValue(s, out var p) ? p : null;
            return _screenDefaultSprite.ContainsKey(s);
        }

        public static bool TryGetScreenDefaultCirclesColor(Screen s, out Color color) =>
            _screenDefaultCirclesColor.TryGetValue(s, out color);

        // these loading-screen canvases are NOT transient — they're pre-instantiated and inactive off
        // the same MainMenu scene as everything else, same story as ShowSelectorBg. Resources.FindObjectsOfTypeAll
        // sees inactive objects (unlike GameObject.Find), so there's no need to have actually lived
        // through that loading screen this session — the Background tab's preview can clone straight
        // off these any time.
        //
        // each of these canvases holds BOTH a "normal round" backdrop AND a "final round" backdrop as
        // sibling children (Generic_UI_CurrentSeasonBackground_Container / Generic_UI_FinalRoundBackground_Prefab)
        // — the game picks one at runtime depending on which round is actually loading. FinalRound isn't
        // a separate ViewModel at all, it's the other sibling under the SAME plain canvas as LoadingLevel.
        // confirmed live via FGRuntimeConsole: `go Prime_UI_RoundSelected_Prefab_Canvas` lists both as
        // direct children.
        private const string ChildNormalRound = "Generic_UI_CurrentSeasonBackground_Container";
        private const string ChildFinalRound = "Generic_UI_FinalRoundBackground_Prefab";

        // canvas names confirmed live via FGRuntimeConsole (`scan RoundSelected`) — searching by
        // ViewModel TYPE instead (LoadingGameScreenViewModel etc.) looked cleaner but was unreliable:
        // Resources.FindObjectsOfTypeAll<T>() matches T's subclasses, and IL2CPP interop appears to
        // always wrap the results AS the requested T regardless of the object's true concrete type, so
        // a GetType() == typeof(T) filter never actually excluded the UP/UGC variants — LoadingLevel and
        // FinalRound (both searching the base type) silently landed on whichever of the three canvases
        // happened to enumerate first. Explore only looked reliable because its leaf-type search had
        // nothing else to collide with. Name-based lookup sidesteps the whole issue.
        private const string CanvasPlain = "Prime_UI_RoundSelected_Prefab_Canvas";
        private const string CanvasUP = "Prime_UI_RoundSelected_UP_Prefab_Canvas";

        public static Transform FindPreviewSource(Screen s)
        {
            string canvasName;
            string childName;
            switch (s)
            {
                case Screen.LoadingLevel:
                    canvasName = CanvasPlain;
                    childName = ChildNormalRound;
                    break;
                case Screen.FinalRound:
                    canvasName = CanvasPlain;
                    childName = ChildFinalRound;
                    break;
                case Screen.Explore:
                    canvasName = CanvasUP;
                    childName = ChildNormalRound;
                    break;
                default:
                    return null;
            }

            GameObject vmGo = null;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == canvasName) { vmGo = go; break; }
            if (vmGo == null) return null;

            var root = vmGo.transform.Find(childName);
            if (root == null) return null;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && t.Find("Backdrop")?.GetComponent<Image>() != null) return t;
            return null;
        }

        // apply this screen's gradient + pattern onto a single background container — any transform
        // that holds a "Backdrop" child (and usually a "Circles" pattern child). this covers both the
        // SeasonS11Background Mask and the FinalRoundBackground "Area".
        public static void ApplyToContainer(Screen s, Transform container)
        {
            if (container == null) return;

            var backdrop = container.Find("Backdrop");
            if (backdrop != null)
            {
                var img = backdrop.GetComponent<Image>();
                if (img != null)
                {
                    int id = img.GetInstanceID();
                    if (!_origBackdropSprite.ContainsKey(id)) _origBackdropSprite[id] = img.sprite;
                    if (!_origBackdropColor.ContainsKey(id)) _origBackdropColor[id] = img.color;

                    var tex = BuildGradientTex(TopColor(s), BotColor(s), Bias(s), Smooth(s));
                    img.sprite = Sprite.Create(tex, new Rect(0, 0, 1, tex.height), new Vector2(0.5f, 0.5f));
                    img.color = Color.white;
                }
            }

            var painted = 0;
            foreach (var circles in PatternChildren(container))
            {
                var cimg = circles.GetComponent<Image>();
                if (cimg == null || cimg.material == null) continue;
                painted++;
                int id = cimg.GetInstanceID();
                if (!_origCirclesColor.ContainsKey(id)) _origCirclesColor[id] = cimg.color;
                var ptex = LoadPatternTexture(SettingsService.Get(KeyPattern(s), ""));

                // FinalRoundBackground's "Pattern" node uses UI/ScrollingSprite2.0 (no _Pattern property
                // on its material at all) and paints the pattern via Image.sprite instead — confirmed live
                // via FGRuntimeConsole (shader.GetPropertyCount dump). every other screen's "Circles" node
                // is shader-driven through a _Pattern texture. branch on whichever this one actually has.
                if (cimg.material.HasProperty("_Pattern"))
                {
                    var mat = EnsureCirclesMaterialInstance(cimg);
                    if (!_origPattern.ContainsKey(id)) _origPattern[id] = mat.GetTexture("_Pattern");
                    if (ptex != null) mat.SetTexture("_Pattern", MatchPatternSize(ptex, _origPattern[id]));
                }
                else
                {
                    if (!_origPatternSprite.ContainsKey(id)) _origPatternSprite[id] = cimg.sprite;
                    if (!_origPatternType.ContainsKey(id)) _origPatternType[id] = cimg.type;
                    if (ptex != null)
                    {
                        cimg.sprite = Sprite.Create(ptex, new Rect(0, 0, ptex.width, ptex.height), new Vector2(0.5f, 0.5f));
                        cimg.type = Image.Type.Tiled;
                        cimg.pixelsPerUnitMultiplier = s == Screen.FallForce ? FallForcePatternTileScale : FinalRoundPatternTileScale;
                    }
                    else
                    {
                        cimg.type = _origPatternType[id];
                    }
                }
                cimg.color = PatternColor(s);
            }
            if (painted == 0) Plugin.Log.LogWarning($"no pattern image under {container.name}, only the backdrop got painted");
        }

        // restore a container's backdrop sprite/colour + circles pattern to the game's originals.
        public static void RevertContainer(Transform container)
        {
            if (container == null) return;

            var backdrop = container.Find("Backdrop");
            var img = backdrop != null ? backdrop.GetComponent<Image>() : null;
            if (img != null)
            {
                int id = img.GetInstanceID();
                if (_origBackdropSprite.TryGetValue(id, out var spr)) img.sprite = spr;
                if (_origBackdropColor.TryGetValue(id, out var col)) img.color = col;
            }

            foreach (var circles in PatternChildren(container))
            {
                var cimg = circles.GetComponent<Image>();
                if (cimg == null || cimg.material == null) continue;
                int id = cimg.GetInstanceID();
                if (cimg.material.HasProperty("_Pattern"))
                {
                    if (_origPattern.TryGetValue(id, out var tex)) EnsureCirclesMaterialInstance(cimg)?.SetTexture("_Pattern", tex);
                }
                else
                {
                    if (_origPatternSprite.TryGetValue(id, out var spr)) cimg.sprite = spr;
                    if (_origPatternType.TryGetValue(id, out var otype)) cimg.type = otype;
                }
                if (_origCirclesColor.TryGetValue(id, out var col)) cimg.color = col;
            }
        }

        // apply to a known single mask (used by the menu/title FallForce path)
        public static void Apply(Screen s, Transform mask)
        {
            if (mask == null) { Plugin.Log.LogInfo($"ScreenBg: Apply {Id(s)}: mask null"); return; }
            CacheScreenDefault(s, mask);
            if (!Enabled(s)) { Plugin.Log.LogInfo($"ScreenBg: Apply {Id(s)}: not enabled, skipping"); return; }
            Plugin.Log.LogInfo($"ScreenBg: Apply {Id(s)} onto {mask.name}");
            ApplyToContainer(s, mask);
        }

        // find every background container under a root and apply to ALL of them. a "container" is any
        // transform with a Backdrop child — covers SeasonS11Background/Mask AND FinalRoundBackground/Area.
        // searches the whole loading-screen canvas, not just the VM subtree (the bg lives elsewhere).
        public static void ApplyUnder(Screen s, Transform root)
        {
            if (root == null) return;
            var containers = FindContainers(root);
            foreach (var c in containers) CacheScreenDefault(s, c);

            if (!Enabled(s)) { Plugin.Log.LogInfo($"ScreenBg: ApplyUnder {Id(s)}: not enabled, reverting"); RevertUnder(root); return; }

            int n = 0;
            foreach (var c in containers) { ApplyToContainer(s, c); n++; }
            Plugin.Log.LogInfo($"ScreenBg: ApplyUnder {Id(s)}: applied to {n} container(s)");
        }

        // revert every background container under the loading-screen canvas to the game's originals.
        public static void RevertUnder(Transform root)
        {
            if (root == null) return;
            int n = 0;
            foreach (var c in FindContainers(root)) { RevertContainer(c); n++; }
            Plugin.Log.LogInfo($"ScreenBg: RevertUnder: reverted {n} container(s)");
        }

        // every transform with a Backdrop Image child — covers SeasonS11Background/Mask AND
        // FinalRoundBackground/Area. searches the whole loading-screen canvas, not just the VM subtree.
        private static List<Transform> FindContainers(Transform root)
        {
            Transform searchRoot = root;
            var canvas = GameObject.Find("UICanvas_Client_V2(Clone)/LoadingScreen");
            if (canvas != null) searchRoot = canvas.transform;

            var result = new List<Transform>();
            var all = searchRoot.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t == null) continue;
                var backdrop = t.Find("Backdrop");
                if (backdrop != null && backdrop.GetComponent<Image>() != null) result.Add(t);
            }
            return result;
        }
    }
}
