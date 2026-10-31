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
        // foreground keys — shared with MenuTab
        public const string KEY_FG_CYAN_ON = "menu.fg.cyan.on";
        public const string KEY_FG_CYAN_R = "menu.fg.cyan.r";
        public const string KEY_FG_CYAN_G = "menu.fg.cyan.g";
        public const string KEY_FG_CYAN_B = "menu.fg.cyan.b";
        public const string KEY_FG_BLACK_ON = "menu.fg.black.on";
        public const string KEY_FG_BLACK_R = "menu.fg.black.r";
        public const string KEY_FG_BLACK_G = "menu.fg.black.g";
        public const string KEY_FG_BLACK_B = "menu.fg.black.b";
        public const string KEY_FG_YELLOW_ON = "menu.fg.yellow.on";
        public const string KEY_FG_YELLOW_R = "menu.fg.yellow.r";
        public const string KEY_FG_YELLOW_G = "menu.fg.yellow.g";
        public const string KEY_FG_YELLOW_B = "menu.fg.yellow.b";
        public const string KEY_FG_BLUE_ON = "menu.fg.blue.on";
        public const string KEY_FG_BLUE_R = "menu.fg.blue.r";
        public const string KEY_FG_BLUE_G = "menu.fg.blue.g";
        public const string KEY_FG_BLUE_B = "menu.fg.blue.b";
        public const string KEY_FG_PINK_ON = "menu.fg.pink.on";
        public const string KEY_FG_PINK_R = "menu.fg.pink.r";
        public const string KEY_FG_PINK_G = "menu.fg.pink.g";
        public const string KEY_FG_PINK_B = "menu.fg.pink.b";
        public const string KEY_FG_ORANGE_ON = "menu.fg.orange.on";
        public const string KEY_FG_ORANGE_R = "menu.fg.orange.r";
        public const string KEY_FG_ORANGE_G = "menu.fg.orange.g";
        public const string KEY_FG_ORANGE_B = "menu.fg.orange.b";

        // cache: instanceID → original color, so we can revert
        private readonly Dictionary<int, Color> _fgOriginals = new Dictionary<int, Color>();
        // parallel ref cache so RevertForeground doesn't have to GetComponentsInChildren the
        // entire UICanvas just to find the ~dozens of images we actually touched. id -> Image.
        private readonly Dictionary<int, UnityEngine.UI.Image> _fgTouchedImages = new Dictionary<int, UnityEngine.UI.Image>();
        private readonly Dictionary<int, UnityEngine.Sprite> _showSelectOriginalSprites = new Dictionary<int, UnityEngine.Sprite>();

        public static IEnumerator ReapplyForegroundFromSettingsCoroutine(Transform scopeRoot = null, bool anyImage = false)
        {
            yield return null;
            yield return new WaitForSeconds(0.01f);
            Instance.ReapplyForegroundFromSettings(scopeRoot, anyImage: anyImage);
        }

        private Transform _uiCanvasRoot;
        private Transform _navOverlay;

        private Transform UICanvasRoot()
        {
            if (_uiCanvasRoot != null) return _uiCanvasRoot;
            var go = GameObject.Find("UICanvas_Client_V2(Clone)");
            _uiCanvasRoot = go == null ? null : go.transform;
            return _uiCanvasRoot;
        }

        private readonly Dictionary<int, Transform> _pendingFgScopes = new Dictionary<int, Transform>();
        private readonly HashSet<int> _pendingFgAnyImage = new HashSet<int>();
        private bool _fgFlushQueued;

        public void QueueForegroundScope(Transform scope, bool anyImage = false)
        {
            if (scope == null) return;
            int id = scope.GetInstanceID();
            _pendingFgScopes[id] = scope;
            if (anyImage) _pendingFgAnyImage.Add(id);

            if (_fgFlushQueued) return;
            _fgFlushQueued = true;
            StartCoroutine(FlushForegroundScopes().WrapToIl2Cpp());
        }

        private IEnumerator FlushForegroundScopes()
        {
            yield return null;
            yield return new WaitForSeconds(0.01f);
            _fgFlushQueued = false;

            var pal = FgPalette.FromSettings();
            if (pal.AnyOn)
                foreach (var kv in _pendingFgScopes)
                    if (kv.Value != null)
                        ApplyForeground(pal, kv.Value, null, _pendingFgAnyImage.Contains(kv.Key));

            _pendingFgScopes.Clear();
            _pendingFgAnyImage.Clear();
        }

        public void SeedForegroundCloneOriginals(GameObject source, GameObject clone)
        {
            if (source == null || clone == null) return;
            var srcImages = source.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            var cloneImages = clone.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            int n = Mathf.Min(srcImages.Length, cloneImages.Length);
            for (int i = 0; i < n; i++)
            {
                var src = srcImages[i];
                var dst = cloneImages[i];
                if (src == null || dst == null) continue;
                if (!_fgOriginals.TryGetValue(src.GetInstanceID(), out var orig)) continue;
                int id = dst.GetInstanceID();
                _fgOriginals[id] = orig;
                _fgTouchedImages[id] = dst;
            }
        }

        public void ReapplyForegroundFromSettings(Transform scopeRoot = null, string excludeSubtreeName = null, bool anyImage = false, bool refreshOriginals = false)
        {
            bool fullCanvas = scopeRoot == null;

            var pal = FgPalette.FromSettings();
            if (pal.AnyOn || fullCanvas)
                ApplyForeground(pal, scopeRoot, excludeSubtreeName, anyImage, refreshOriginals);

            if (fullCanvas)
                ApplyKnownSpecialForegroundScreens();
        }

        private static string FullPath(Transform t)
        {
            if (t == null) return "<null>";
            var sb = new System.Text.StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        // ── Foreground ────────────────────────────────────────────────────────

        private struct FgPalette
        {
            public bool CyanOn, BlueOn, BlackOn, YellowOn, PinkOn, OrangeOn;
            public Color Cyan, Blue, Black, Yellow, Pink, Orange;

            public bool AnyOn => CyanOn || BlueOn || BlackOn || YellowOn || PinkOn || OrangeOn;

            public static FgPalette FromSettings() => new FgPalette
            {
                CyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true", Cyan = CyanReplacement(),
                BlueOn = SettingsService.Get(KEY_FG_BLUE_ON, "false") == "true", Blue = BlueReplacement(),
                BlackOn = SettingsService.Get(KEY_FG_BLACK_ON, "false") == "true", Black = BlackReplacement(),
                YellowOn = SettingsService.Get(KEY_FG_YELLOW_ON, "false") == "true", Yellow = YellowReplacement(),
                PinkOn = SettingsService.Get(KEY_FG_PINK_ON, "false") == "true", Pink = PinkReplacement(),
                OrangeOn = SettingsService.Get(KEY_FG_ORANGE_ON, "false") == "true", Orange = OrangeReplacement(),
            };

            // orange sits between pink and yellow, so band order decides who eats it
            public bool TryRetint(Color c, out Color target)
            {
                Color.RGBToHSV(c, out float h, out float s, out float v);
                if (BlueOn && s > 0.3f && h > 0.58f && h <= 0.70f) target = Blue;
                else if (CyanOn && s > 0.3f && h >= 0.47f && h <= 0.58f) target = Cyan;
                else if (PinkOn && s > 0.3f && (h >= 0.88f || h <= 0.05f) && v > 0.3f) target = Pink;
                else if (OrangeOn && s > 0.3f && h > 0.05f && h < 0.1f && v > 0.3f) target = Orange;
                else if (YellowOn && s > 0.3f && h >= 0.1f && h <= 0.2f) target = Yellow;
                else if (BlackOn && v < 0.15f && s < 0.15f) target = Black;
                else { target = c; return false; }
                return true;
            }
        }

        private void ApplyForeground(FgPalette pal, Transform scopeRoot = null, string excludeSubtreeName = null, bool anyImage = false, bool refreshOriginals = false)
        {
            bool fullCanvas = scopeRoot == null;
            if (fullCanvas)
            {
                RevertForeground();
                scopeRoot = UICanvasRoot();
                if (scopeRoot == null) return;
            }

            var images = scopeRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            // excludeSubtreeName may be several names joined by '|' (e.g. the creative bg canvas plus
            // the options screen's Description block); anything under one of them is left alone.
            string[] excludes = excludeSubtreeName == null ? null : excludeSubtreeName.Split('|');

            for (int i = 0; i < images.Length; i++)
            {
                var img = images[i];
                if (img == null) continue;

                string n = img.name;
                bool isFill = n.Contains("Fill") || n.Contains("Background") || n.Contains("Outline") || n.Contains("Inline") || n.Contains("BG");
                bool isCrowns = n.Equals("crowns");
                bool isBacking = n.Contains("Backing");
                // level-editor screens (anyImage) recolour every hue-matching image, not just the
                // menu's Fill/Backing/crowns-named ones — their controls are named Slider/Overlay/etc.
                if (!anyImage && !isFill && !isBacking && !isCrowns) continue;

                int id = img.GetInstanceID();
                // refreshOriginals: the game just repainted this image to a new state colour (radial
                // select/deselect/disable), so the live colour is the real source hue, not the stale cache.
                Color c = (!refreshOriginals && _fgOriginals.TryGetValue(id, out var cachedOrig)) ? cachedOrig : img.color;

                var hues = pal;
                if (isBacking && !isFill) hues.BlackOn = hues.YellowOn = false;
                if (isCrowns && pal.BlueOn) hues.Cyan = pal.Blue; // crowns follow cyan, but want the blue colour
                if (!hues.TryRetint(c, out Color target)) continue;

                bool excluded = false;
                bool inTileFolder = false;
                bool inRadialItem = false;
                for (var p = img.transform; p != null; p = p.parent)
                {
                    string pn = p.name;
                    if (pn == "TeamContainer" || pn == "TabContentSocialPhraseOverlay" || pn.StartsWith("StarPopup_")) { excluded = true; break; }
                    if (pn == "Background" && p.parent != null && p.parent.name == "Menu_Screen_PersonalBests") { excluded = true; break; }
                    if (pn == "Folder Items" || pn == "Carousel_Items") inTileFolder = true;
                    else if (pn.StartsWith("CustomiserSubMenu")) inRadialItem = true;
                    if (excludes != null)
                    {
                        foreach (var ex in excludes) if (pn == ex) { excluded = true; break; }
                        if (excluded) break;
                    }
                }
                if (excluded) continue;
                // radial fills belong to RetintRadialItems; sweeping them fought the toggle state and
                // re-cached our own output as the original, double-tinting on the next pass
                if (inRadialItem && n == "Background_Fill") continue;
                // carousel/folder tiles are recoloured by name in ApplyFolderTileColours (Fill->blue,
                // Selected->cyan); the hue sweep mis-buckets Background_Fill as cyan, so leave both to it.
                // scoped to the tile subtrees ApplyFolderTileColours actually runs on — skipping the name
                // globally also killed the private lobby player list's Background_Fill, which nothing else paints.
                if (!anyImage && inTileFolder && (n == "Background_Fill" || n == "Background_Selected")) continue;

                if (refreshOriginals || !_fgOriginals.ContainsKey(id))
                {
                    _fgOriginals[id] = c;
                    _fgTouchedImages[id] = img;
                }

                img.color = new Color(target.r, target.g, target.b, img.color.a);
            }

            ApplyMainPlayButtonUnselectedFill(scopeRoot, pal.YellowOn, pal.Yellow, fullCanvas);
        }

        // level-editor variation folder: recolour by name only. Background_Fill -> blue, Background_Selected
        // -> cyan. covers the disabled tile states too (GetComponentsInChildren(true)); originals tracked so
        // RevertForeground puts them back.
        public void ApplyFolderTileColours(Transform folderRoot)
        {
            bool blueOn = SettingsService.Get(KEY_FG_BLUE_ON, "false") == "true";
            bool cyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true";
            if (!blueOn && !cyanOn)
            {
                Plugin.Log.LogInfo($"folder tile colours: blue+cyan both off, skipping {FullPath(folderRoot)}");
                return;
            }

            Color blue = BlueReplacement();
            Color cyan = CyanReplacement();

            int painted = 0;
            foreach (var img in folderRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null) continue;
                string n = img.name;
                bool fill = blueOn && n == "Background_Fill";
                bool sel = cyanOn && n == "Background_Selected";
                if (!fill && !sel) continue;

                int id = img.GetInstanceID();
                if (!_fgOriginals.ContainsKey(id)) { _fgOriginals[id] = img.color; _fgTouchedImages[id] = img; }
                Color target = fill ? blue : cyan;
                img.color = new Color(target.r, target.g, target.b, img.color.a);
                painted++;
            }
            Plugin.Log.LogInfo($"folder tiles under {folderRoot.name}: {painted} recoloured");
        }

        // OnApply's full-canvas RevertForeground clears the same _fgOriginals cache the carousel/folder
        // tiles are tracked in, but nothing re-sweeps them by name afterward — so a manual apply while
        // browsing left every visible tile blank. re-hit whatever's actually open right now: the
        // in-editor object carousel AND the level browser's own tile grid (two different screens,
        // two different game types, same Background_Fill/Background_Selected naming).
        public void ReapplyLevelBrowserTilesLive()
        {
            var carrousels = FindObjectsOfType<LevelEditorCarrouselViewModel>();
            Plugin.Log.LogInfo($"reapply level browser tiles: {carrousels.Length} editor carrousel(s) live");
            foreach (var carrousel in carrousels)
            {
                if (carrousel == null) continue;
                var carouselItems = carrousel.transform.Find("Carousel_Items");
                if (carouselItems != null) ApplyFolderTileColours(carouselItems);
                var folderItems = carrousel.transform.Find("Folder Items");
                if (folderItems != null) ApplyFolderTileColours(folderItems);
            }

            // browser tiles use a totally different prefab to the editor's object carousel, so they
            // go through the generic hue sweep instead of the Background_Fill/Selected name match.
            var browserTiles = FindObjectsOfType<Wushu.LevelEditor.Runtime.UI.LevelBrowser.LevelBrowserTileViewModel>();
            Plugin.Log.LogInfo($"reapply level browser tiles: {browserTiles.Length} browser tile(s) live");
            foreach (var tile in browserTiles)
            {
                if (tile == null) continue;
                ReapplyForegroundFromSettings(tile.transform, null, true);
            }
        }

        private struct RadialItemColours
        {
            public LevelEditor_RadialMenuItemViewModel Item;
            public Color Selected, Deselected;
        }

        // a radial item repaints its own fill from _default*Color on hover/press/submit, so the tint
        // has to live in those and be matched off the cached true originals, never off our own output
        private readonly Dictionary<int, RadialItemColours> _radialOriginals = new Dictionary<int, RadialItemColours>();

        public void RetintRadialItems(Transform scopeRoot, bool remapLive = false)
        {
            var pal = FgPalette.FromSettings();
            if (!pal.AnyOn) return;

            foreach (var item in scopeRoot.GetComponentsInChildren<LevelEditor_RadialMenuItemViewModel>(true))
            {
                if (item._toggle == null) continue;

                int id = item.GetInstanceID();
                if (!_radialOriginals.TryGetValue(id, out var orig))
                {
                    orig = new RadialItemColours { Item = item, Selected = item._defaultSelectedColor, Deselected = item._defaultDeselectedColor };
                    _radialOriginals[id] = orig;
                }

                Color selected = pal.TryRetint(orig.Selected, out var s) ? new Color(s.r, s.g, s.b, orig.Selected.a) : orig.Selected;
                Color deselected = pal.TryRetint(orig.Deselected, out var d) ? new Color(d.r, d.g, d.b, orig.Deselected.a) : orig.Deselected;
                PaintRadialItem(item, selected, deselected);

                if (!remapLive) continue;
                foreach (var img in item.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    if (img.gameObject.name != "Background_Fill") continue;
                    Color to;
                    if (SameRgb(img.color, orig.Selected)) to = selected;
                    else if (SameRgb(img.color, orig.Deselected)) to = deselected;
                    else continue;

                    int imgId = img.GetInstanceID();
                    if (!_fgOriginals.ContainsKey(imgId)) { _fgOriginals[imgId] = img.color; _fgTouchedImages[imgId] = img; }
                    img.color = new Color(to.r, to.g, to.b, img.color.a);
                }
            }
        }

        private static bool SameRgb(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

        private static void PaintRadialItem(LevelEditor_RadialMenuItemViewModel item, Color selected, Color deselected)
        {
            item._defaultSelectedColor = selected;
            item._defaultDeselectedColor = deselected;
        }

        public void RevertForeground()
        {
            foreach (var kv in _radialOriginals)
                if (kv.Value.Item != null && kv.Value.Item._toggle != null)
                    PaintRadialItem(kv.Value.Item, kv.Value.Selected, kv.Value.Deselected);
            _radialOriginals.Clear();

            if (_fgOriginals.Count == 0) return;

            // restore via the tracked Image refs — avoids a full UICanvas GetComponentsInChildren
            // walk every state-change reapply, which was visibly freezing transitions.
            foreach (var kv in _fgTouchedImages)
            {
                var img = kv.Value;
                if (img == null) continue;
                if (_fgOriginals.TryGetValue(kv.Key, out var orig))
                    img.color = new Color(orig.r, orig.g, orig.b, img.color.a);
            }

            _fgOriginals.Clear();
            _fgTouchedImages.Clear();
        }

        // the season progress banner is driven by UITween so it can't be recoloured once-and-done —
        // SeasonProgressHoverPatch calls this every tween frame with the live Image and whether the
        // banner is hovered. idle = blue, hover = pink. only stomp the tween's colour if that colour's
        // foreground toggle is actually on; otherwise leave the game's colour alone.
        // driven from UITween.ColorUpdate, so this runs per tween write per frame. re-reading the two
        // toggles and re-parsing six floats out of settings on every one of those was pure overhead —
        // resolve them once a frame instead.
        private static int _spFrame = -1;
        private static bool _spPinkOn, _spBlueOn;
        private static Color _spPink, _spBlue;

        public void RecolourSeasonProgressImage(UnityEngine.UI.Image img, bool hovered)
        {
            if (img == null) return;

            if (_spFrame != Time.frameCount)
            {
                _spFrame = Time.frameCount;
                _spPinkOn = SettingsService.Get(KEY_FG_PINK_ON, "false") == "true";
                _spBlueOn = SettingsService.Get(KEY_FG_BLUE_ON, "false") == "true";
                if (_spPinkOn) _spPink = PinkReplacement();
                if (_spBlueOn) _spBlue = BlueReplacement();
            }

            if (hovered)
            {
                if (!_spPinkOn) return;
                img.color = new Color(_spPink.r, _spPink.g, _spPink.b, img.color.a);
            }
            else
            {
                if (!_spBlueOn) return;
                img.color = new Color(_spBlue.r, _spBlue.g, _spBlue.b, img.color.a);
            }
        }

        public enum SpecialScreen
        {
            PrivateLobbyShowSelect,
            PrivateLobbyPlayerList
        }

        public IEnumerator ReapplySpecialForegroundNextFrame(SpecialScreen screen)
        {
            ReapplySpecialForeground(screen);
            yield return null;
            ReapplySpecialForeground(screen);
        }

        public void ReapplySpecialForeground(SpecialScreen screen)
        {
            switch (screen)
            {
                case SpecialScreen.PrivateLobbyShowSelect:
                {
                    var root = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Prefab_UI_PrivateLobbyShowSelect(Clone)");
                    if (root == null) return;
                    ReapplyForegroundFromSettings(root.transform);
                    ApplyPrivateLobbyShowSelect(root.transform);
                    break;
                }
                case SpecialScreen.PrivateLobbyPlayerList:
                {
                    var root = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Prime_UI_PrivateLobbyPlayerList(Clone)");
                    if (root == null) return;
                    ReapplyForegroundFromSettings(root.transform);
                    SetImageColor(root.transform, "SafeArea/PlayerListArea/PlayerListBackground/Background_Fill/OuterBorder", BlueReplacement(), true);
                    BetterFG.Features.MorePlatformIcon.FeatureMorePlatformIcon.ApplyPrivateLobbyNamesFromScene(root.transform);
                    break;
                }
            }
        }

        public void ApplyKnownSpecialForegroundScreens()
        {
            ReapplySpecialForeground(SpecialScreen.PrivateLobbyShowSelect);
            ReapplySpecialForeground(SpecialScreen.PrivateLobbyPlayerList);

            // navigation overlay sits outside UICanvas_Client_V2, so the main sweep misses it.
            if (_navOverlay == null)
            {
                var nav = GameObject.Find("Prefab_UI_NavigationOverlay(Clone)");
                _navOverlay = nav == null ? null : nav.transform;
            }
            if (_navOverlay != null && _navOverlay.gameObject.activeInHierarchy)
                ReapplyForegroundFromSettings(_navOverlay);

            // RevertForeground put the radial's toggles back, so redo them if it's open right now
            var radial = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Prime_UI_LE_RadialMenu_Canvas(Clone)");
            if (radial != null && radial.activeInHierarchy)
            {
                ReapplyForegroundFromSettings(radial.transform, "SettingsBackingSprite", true);
                ApplyEditorShader(radial.transform);
                RetintRadialItems(radial.transform, true);
            }
        }

        private UnityEngine.UI.Image _playButtonUnselectedFill;

        private void ApplyMainPlayButtonUnselectedFill(Transform scopeRoot, bool yellowOn, Color yellowTarget, bool fullCanvas)
        {
            if (scopeRoot == null || !yellowOn) return;

            var cached = _playButtonUnselectedFill;
            if (cached != null)
            {
                TintPlayButtonFill(cached, yellowTarget);
                return;
            }
            if (!fullCanvas) return;

            string[] roots =
            {
                "Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_MainMenu_Canvas(Clone)/SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab/",
                "MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_MainMenu_Canvas(Clone)/SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab/",
                "Prime_UI_MainMenu_Canvas(Clone)/SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab/",
                "SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab/"
            };

            foreach (var root in roots)
            {
                var unselected = scopeRoot.Find(root + "Panel_Unselected/Panel_Fill");
                var img = unselected == null ? null : unselected.GetComponent<UnityEngine.UI.Image>();
                if (img == null) continue;

                _playButtonUnselectedFill = img;
                TintPlayButtonFill(img, yellowTarget);
                return;
            }
        }

        private void TintPlayButtonFill(UnityEngine.UI.Image img, Color yellowTarget)
        {
            int id = img.GetInstanceID();
            if (!_fgOriginals.ContainsKey(id))
            {
                _fgOriginals[id] = img.color;
                _fgTouchedImages[id] = img;
            }

            img.color = new Color(yellowTarget.r, yellowTarget.g, yellowTarget.b, img.color.a);
        }

        public void ReapplyPrivateLobbyShowEntryForeground(Transform entry)
        {
            if (entry == null) return;

            ReapplyForegroundFromSettings(entry);
            var outline = entry.Find("ButtonImage/Outline");
            var img = outline == null ? null : outline.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = Color.white;
        }

        private readonly Dictionary<int, UnityEngine.Sprite> _showSelectBaked = new Dictionary<int, UnityEngine.Sprite>();
        private Color _showSelectBakedCyan;
        private bool _showSelectBakedCyanOn;

        private void ApplyPrivateLobbyShowSelect(Transform root)
        {
            if (root == null) return;

            var container = root.Find("Container");
            var image = container == null ? null : container.GetComponent<UnityEngine.UI.Image>();
            if (image != null && image.sprite != null)
            {
                int id = image.GetInstanceID();
                if (!_showSelectOriginalSprites.TryGetValue(id, out var original) || original == null)
                {
                    original = image.sprite;
                    _showSelectOriginalSprites[id] = original;
                }

                bool cyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true";
                Color cyan = CyanReplacement();
                if (cyanOn != _showSelectBakedCyanOn || cyan != _showSelectBakedCyan)
                {
                    foreach (var stale in _showSelectBaked.Values)
                    {
                        if (stale == null || !stale.name.EndsWith("_betterfg")) continue;
                        Destroy(stale.texture);
                        Destroy(stale);
                    }
                    _showSelectBaked.Clear();
                    _showSelectBakedCyanOn = cyanOn;
                    _showSelectBakedCyan = cyan;
                }

                if (!cyanOn) image.sprite = original;
                else
                {
                    int oid = original.GetInstanceID();
                    if (!_showSelectBaked.TryGetValue(oid, out var baked) || baked == null)
                    {
                        baked = ProcessShowSelectSprite(original);
                        _showSelectBaked[oid] = baked;
                    }
                    image.sprite = baked;
                }
            }

            SetImageColor(root, "Container/ShowsScrollList/TabScrollbar", BlueReplacement(), true);

            var content = root.Find("Container/ShowsScrollList/ShowsListViewport/ShowsListContent");
            if (content == null) return;

            foreach (var img in content.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img != null && img.name == "Outline")
                    img.color = Color.white;
            }
        }

        private void SetImageColor(Transform root, string path, Color color, bool requireBlueOn = false)
        {
            if (root == null || requireBlueOn && SettingsService.Get(KEY_FG_BLUE_ON, "false") != "true") return;

            var t = root.Find(path);
            var img = t == null ? null : t.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = new Color(color.r, color.g, color.b, img.color.a);
        }

        public static readonly Color FgDefaultCyan = new Color(0.196f, 0.788f, 0.922f);
        public static readonly Color FgDefaultBlack = new Color(0.133f, 0.137f, 0.149f);
        public static readonly Color FgDefaultYellow = new Color(1f, 0.808f, 0f);
        public static readonly Color FgDefaultBlue = new Color(0.353f, 0.388f, 0.678f);
        public static readonly Color FgDefaultPink = new Color(1f, 0f, 0.647f);
        public static readonly Color FgDefaultOrange = new Color(1f, 0.525f, 0.039f);

        private static Color FgColour(string prefix, Color d) =>
            new Color(ParseF(prefix + ".r", d.r), ParseF(prefix + ".g", d.g), ParseF(prefix + ".b", d.b), 1f);

        private static Color BlueReplacement() => FgColour("menu.fg.blue", FgDefaultBlue);
        private static Color CyanReplacement() => FgColour("menu.fg.cyan", FgDefaultCyan);
        private static Color PinkReplacement() => FgColour("menu.fg.pink", FgDefaultPink);
        private static Color BlackReplacement() => FgColour("menu.fg.black", FgDefaultBlack);
        public static Color YellowReplacement() => FgColour("menu.fg.yellow", FgDefaultYellow);
        public static Color OrangeReplacement() => FgColour("menu.fg.orange", FgDefaultOrange);

        // these gameplay images have hot-pink + dark-grey baked into their TEXTURE, not the image
        // colour, so colour tinting can't touch them. we recolour the actual pixels at runtime:
        // hot pink -> pink replacement, dark grey -> black replacement. cached per-image so we only
        // build the swapped texture once.
        //
        // paths are RELATIVE to the GameStates root so we can resolve them with transform.Find,
        // which (unlike GameObject.Find) also finds INACTIVE children — needed because only one
        // GameState is active at a time (CountdownState vs PlayingState).
        private const string GameStatesRootPath =
            "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates";


        // shader-material recolour. the hot-pink/dark-grey HUD bits are baked into the TEXTURE, not the
        // image colour, so a tint can't touch them. instead of reading pixels back and rebuilding textures
        // on the CPU (the old bake — a per-round GPU stall) we assign a custom UI material whose shader
        // remaps those hue/sat bands on the GPU at draw time. zero readback, zero per-round cost.
        private UnityEngine.Material _pinkGreyMat;

        // every Image we've put our material on, so we can clear it when the feature turns off.
        private readonly HashSet<UnityEngine.UI.Image> _shadedImages = new HashSet<UnityEngine.UI.Image>();

        private UnityEngine.Material GetPinkGreyMaterial()
        {
            if (_pinkGreyMat != null) return _pinkGreyMat;
            var sh = BetterFG.Core.AssetManager.GetShader("bettrfg_ui_shader");
            if (sh == null) { Plugin.Log.LogWarning("bettrfg_ui_shader missing, menu recolour is off"); return null; }
            _pinkGreyMat = new UnityEngine.Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_pinkGreyMat);
            return _pinkGreyMat;
        }

        public void ReapplyBakedPinkGreyTextures()
        {
            bool pinkOn = SettingsService.Get(KEY_FG_PINK_ON, "false") == "true";
            bool blackOn = SettingsService.Get(KEY_FG_BLACK_ON, "false") == "true";

            // feature off and nothing ever shaded -> nothing to do.
            if (!pinkOn && !blackOn && _shadedImages.Count == 0) return;

            // turned off: strip our material from everything we touched, then bail.
            if (!pinkOn && !blackOn)
            {
                foreach (var i in _shadedImages) if (i != null) i.material = null;
                _shadedImages.Clear();
                return;
            }

            var mat = GetPinkGreyMaterial();
            if (mat == null) return;

            Color pink = PinkReplacement();
            Color black = BlackReplacement();
            mat.SetColor("_PinkColor", pink);
            mat.SetColor("_BlackColor", black);
            mat.SetFloat("_PinkOn", pinkOn ? 1f : 0f);
            mat.SetFloat("_BlackOn", blackOn ? 1f : 0f);

            var gameStates = GameObject.Find(GameStatesRootPath);
            if (gameStates == null) return;
            var root = gameStates.transform;

            // only full-white images under one of these four gameplay view-models get the shader.
            foreach (var img in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null) continue;

                var c = img.color;
                // white = full rgb, alpha ignored (some are faded in/out)
                if (c.r < 0.99f || c.g < 0.99f || c.b < 0.99f) continue;

                bool inScope = false;
                bool inCreatorInfoSlim = false;
                bool inTeamContainer = false;
                for (var p = img.transform; p != null; p = p.parent)
                {
                    var n = p.name;
                    if (n == "Generic_UI_LE_CreatorInfoSlim") { inCreatorInfoSlim = true; break; }
                    if (n == "TeamContainer") { inTeamContainer = true; break; }
                    if (n == "GameplayTimerViewModel" || n == "GameplayScoringViewModel"
                        || n == "GameplayInstructionsViewModel" || n == "GameplayTimeAttackViewModel")
                    {
                        inScope = true;
                    }
                }
                if (inCreatorInfoSlim || inTeamContainer || !inScope) continue;
                if (img.gameObject.name.Contains("Glyph")) continue;

                if (img.material != mat) img.material = mat;
                _shadedImages.Add(img);
            }
        }

        // editor screens (radial, settings backing, etc) bake their colours into the texture behind a
        // white image tint, so a flat colour swap can't reach them — the same reason the ingame HUD
        // needs a shader. white-tinted images get the pink/grey UI shader (with a cyan band added) so
        // the baked cyan/pink/grey remap on the GPU while white edges stay white. its own material keeps
        // this config off the ingame _pinkGreyMat.
        private UnityEngine.Material _editorShaderMat;

        public void ApplyEditorShader(Transform scopeRoot)
        {
            bool cyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true";
            bool pinkOn = SettingsService.Get(KEY_FG_PINK_ON, "false") == "true";
            bool blackOn = SettingsService.Get(KEY_FG_BLACK_ON, "false") == "true";
            bool anyOn = cyanOn || pinkOn || blackOn;

            if (anyOn)
            {
                if (_editorShaderMat == null)
                {
                    var sh = BetterFG.Core.AssetManager.GetShader("bettrfg_ui_shader");
                    if (sh == null) { Plugin.Log.LogWarning("bettrfg_ui_shader missing, editor recolour is off"); return; }
                    _editorShaderMat = new UnityEngine.Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.Object.DontDestroyOnLoad(_editorShaderMat);
                }
                _editorShaderMat.SetColor("_CyanColor", CyanReplacement());
                _editorShaderMat.SetColor("_PinkColor", PinkReplacement());
                _editorShaderMat.SetColor("_BlackColor", BlackReplacement());
                _editorShaderMat.SetFloat("_CyanOn", cyanOn ? 1f : 0f);
                _editorShaderMat.SetFloat("_PinkOn", pinkOn ? 1f : 0f);
                _editorShaderMat.SetFloat("_BlackOn", blackOn ? 1f : 0f);
            }

            foreach (var img in scopeRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null) continue;
                if (img.gameObject.name.IndexOf("glyph", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                // row SelectedStateOverlay highlights are light cyan/blue; the flat sweep turns them
                // into the cyan replacement, but they should read as faint white instead.
                if (img.gameObject.name == "SelectedStateOverlay")
                {
                    if (cyanOn) img.color = new Color(1f, 1f, 1f, 0.2f);
                    continue;
                }

                // white-tinted images carry their colour in the texture — hand them the shader.
                var c = img.color;
                if (c.r >= 0.99f && c.g >= 0.99f && c.b >= 0.99f)
                    img.material = anyOn ? _editorShaderMat : null;
            }
        }

        private static UnityEngine.Sprite ProcessShowSelectSprite(UnityEngine.Sprite sprite)
        {
            bool cyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true";
            if (!cyanOn) return sprite;

            var cyan = CyanReplacement();
            var src = sprite.texture;
            var rect = sprite.textureRect;
            int w = Mathf.RoundToInt(rect.width);
            int h = Mathf.RoundToInt(rect.height);
            if (src == null || w <= 0 || h <= 0) return sprite;

            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            var old = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.name = src.name + "_betterfg_private_lobby";
            tex.filterMode = src.filterMode;
            tex.wrapMode = src.wrapMode;
            tex.anisoLevel = src.anisoLevel;
            tex.mipMapBias = src.mipMapBias;
            tex.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
            tex.Apply(false, false);

            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);

            var pixels = tex.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (c.a <= 0.01f) continue;

                Color.RGBToHSV(c, out float hue, out float sat, out float val);
                if (val > 0.82f && sat < 0.2f)
                    pixels[i] = new Color(1f, 1f, 1f, c.a);
                else if (sat > 0.25f && hue >= 0.45f && hue <= 0.6f)
                    pixels[i] = new Color(cyan.r, cyan.g, cyan.b, c.a);
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);

            var made = UnityEngine.Sprite.Create(
                tex,
                new Rect(0, 0, w, h),
                new UnityEngine.Vector2(sprite.pivot.x / w, sprite.pivot.y / h),
                sprite.pixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                sprite.border
            );
            made.name = sprite.name + "_betterfg";
            return made;
        }
    }
}
