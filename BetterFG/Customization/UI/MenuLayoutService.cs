using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using UnityEngine;

namespace BetterFG.Customization.UI
{
    public static class MenuLayoutService
    {
        public enum Axis { Both, Horizontal, Vertical }

        public sealed class Slot
        {
            public readonly string Path;
            public readonly Axis Axis;
            public string[] HitPaths = new string[0];
            public bool Snap = true;
            public string HostPath;
            public string FramePath;
            public string FrameHostPath;
            public bool HomeOnly;
            public string Link;
            public bool Deep;
            public int View;
            public bool OneChild;
            public string LiveRoot;
            public bool FixedAnchor;
            public bool Whole;
            public bool Image;
            public Slot Owner;
            public string Rest;

            public Slot(string path, Axis axis = Axis.Both)
            {
                Path = path;
                Axis = axis;
            }

            public string Name
            {
                get
                {
                    int i = Path.LastIndexOf('/');
                    return i < 0 ? Path : Path.Substring(i + 1);
                }
            }
        }

        public sealed class Kind
        {
            public readonly string Id;
            public readonly string LabelId;
            public readonly string RootPath;
            public Slot[] Slots = new Slot[0];
            public string Source;
            public string[] Extras = new string[0];
            public string[] Views = new string[0];
            public string ImageHost = "";

            public Kind(string id, string labelId, string rootPath)
            {
                Id = id;
                LabelId = labelId;
                RootPath = rootPath;
            }

            public Slot SlotFor(string path)
            {
                for (int i = 0; i < Slots.Length; i++)
                    if (Slots[i].Path == path) return Slots[i];
                return null;
            }

            public string Prefix => "layout4." + Id + ".";

            private Texture2D _preview;
            public Texture2D Preview => _preview != null ? _preview
                : _preview = Utilities.EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.layout.preview_" + Id + ".png");

            public Transform Root
            {
                get
                {
                    var go = GameObject.Find(RootPath);
                    return go == null ? null : go.transform;
                }
            }
        }

        private const string MENU_SAFE =
            "MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_MainMenu_Canvas(Clone)/SafeArea/";
        private const string MENU_SAFE_TOP =
            "MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_MainMenu_Canvas(Clone)/SafeAreaTop/";
        private const string TOPBAR_CURRENCY =
            "Topbar_Prime(Clone)/SafeArea/TopRight_Group/CurrencyHorizontalLayout/";
        private const string TOPBAR_HOST = "Topbar_Prime(Clone)/SafeArea";
        private const string CURRENCY_FRAME =
            "Topbar_Prime(Clone)/SafeArea/TopRight_Group/CurrencyHorizontalLayout";
        private const string MENU_HOST =
            "MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_MainMenu_Canvas(Clone)/SafeArea";

        public static readonly Kind MainMenu =
            new Kind("mainmenu", "layout.kind.main_menu", "UICanvas_Client_V2(Clone)/Default")
            {
                ImageHost = MENU_HOST,
                Slots = new[]
                {
                    new Slot("Topbar_Prime(Clone)/SafeArea/TabsHorizontalLayout") { HostPath = TOPBAR_HOST },
                    new Slot("Topbar_Prime(Clone)/SafeArea/TopRight_Group") { HostPath = TOPBAR_HOST },
                    new Slot(TOPBAR_CURRENCY + "Gems_CurrencyCounter") { FramePath = CURRENCY_FRAME, FrameHostPath = TOPBAR_HOST, HomeOnly = true },
                    new Slot(TOPBAR_CURRENCY + "Kudos_CurrencyCounter") { FramePath = CURRENCY_FRAME, FrameHostPath = TOPBAR_HOST, HomeOnly = true },
                    new Slot(TOPBAR_CURRENCY + "Settings") { FramePath = CURRENCY_FRAME, FrameHostPath = TOPBAR_HOST, HomeOnly = true },
                    new Slot(MENU_SAFE + "BottomLeft_Group/AnimContainer") { HostPath = MENU_HOST },
                    new Slot(MENU_SAFE + "BottomLeft_Group/OpenPartyMenuPrompt") { HostPath = MENU_HOST },
                    new Slot(MENU_SAFE + "BottomRight_Group") { HostPath = MENU_HOST },
                    new Slot(MENU_SAFE + "BottomRight_Group/Generic_UI_PlayButton2_Prefab") { HostPath = MENU_HOST },
                    new Slot(MENU_SAFE + "BottomRight_Group/Generic_UI_FriendsButton_Variant") { HostPath = MENU_HOST },
                    new Slot(MENU_SAFE + "TopRight_Group") { HostPath = MENU_HOST },
                    new Slot(MENU_SAFE + "Toast_Group") { HostPath = MENU_HOST },
                    new Slot(MENU_SAFE + "PlinthTracking") { HostPath = MENU_HOST },
                    new Slot(MENU_SAFE_TOP + "LayoutGroup/TopLeft_Group") { HostPath = MENU_HOST },
                },
            };

        private const string LOAD_SAFE = "SafeArea/";
        private const string LOAD_INFO = "SafeArea/InfoGroup/";
        private const string LOAD_HOST = "SafeArea";
        private const string LOAD_SHOW = "SafeArea/SelectedShow/";
        private const string LOAD_CREATOR = "Generic_UI_LE_CreatorInfo_Prefab";
        private const string LOAD_CREATOR_BITS = LOAD_SHOW + "ShowMask/" + LOAD_CREATOR + "/Container_Content/";

        public static readonly Kind Loading =
            new Kind("loadscreen", "layout.kind.loading", "UICanvas_Client_V2(Clone)/LoadingScreen")
            {
                Source = "Prime_UI_RoundSelected_UGC_Prefab_Canvas",
                ImageHost = LOAD_HOST,
                Slots = new[]
                {
                    new Slot(LOAD_SHOW + "ShowMask") { Link = "ShowOutline" },
                    new Slot(LOAD_SHOW + "ToolTip"),
                    new Slot(LOAD_SHOW + "ShowMask/" + LOAD_CREATOR) { HostPath = LOAD_SAFE + "SelectedShow" },
                    new Slot(LOAD_CREATOR_BITS + "Text_ShareCode") { Deep = true },
                    new Slot(LOAD_CREATOR_BITS + "Icon_CreatorMade") { Deep = true },
                    new Slot(LOAD_CREATOR_BITS + "PB_UI_NameTag") { Deep = true },
                    new Slot(LOAD_SAFE + "TopLeft_Group/RoundName_Panel") { HostPath = LOAD_HOST },
                    new Slot(LOAD_SAFE + "BettrFG_PBLoadPanel") { HitPaths = new[] { "BettrFG_PBLoadContent/BettrFG_PBLoadLabel" } },
                    new Slot(LOAD_INFO + "TagsLayout") { HostPath = LOAD_HOST },
                    new Slot(LOAD_INFO + "HowToPlay_Text") { HostPath = LOAD_HOST },
                    new Slot(LOAD_INFO + "Description_Text") { HostPath = LOAD_HOST },
                    new Slot(LOAD_INFO + "PromptInfo") { HostPath = LOAD_HOST },
                    new Slot(LOAD_INFO + "Medals") { HostPath = LOAD_HOST },
                    new Slot(LOAD_SAFE + "BottomBanner"),
                    new Slot(LOAD_SAFE + "UI_LoadingAnim_Prefab"),
                    new Slot(LOAD_SAFE + "Generic_UI_LoadingSpinner_Prefab"),
                },
            };

        private const string HUD_PLAY = "GameStates/PlayingState/";
        private const string HUD_SPEC = "GameStates/SpectatorState/";
        private const string HUD_UNDER = "PersistentUnderlayUI/";
        private const string HUD_OVER = "PersistentOverlayUI/";

        public static readonly Kind Hud =
            new Kind("ingamehud", "layout.kind.hud", "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)")
            {
                Source = "InGameUiManager",
                Views = new[] { "layout.view.race", "layout.view.scores", "layout.view.messages", "layout.view.spectator" },
                Slots = new[]
                {
                    new Slot(HUD_PLAY + "GameplayTimerViewModel/TopRightContainer"),
                    new Slot(HUD_PLAY + "GameplayTimerViewModel/TopMiddleLayerGroup"),
                    new Slot(HUD_PLAY + "GameplayInstructionsViewModel/TopLeftContainer"),
                    new Slot(HUD_PLAY + "GameplayScoringViewModel/TopLeftContainer") { View = 1 },
                    new Slot(HUD_PLAY + "GameplayScoringViewModel/TopRightContainer") { View = 1 },
                    new Slot(HUD_PLAY + "GameplayScoringViewModel/TopMiddleContainer") { View = 1, OneChild = true },
                    new Slot(HUD_PLAY + "GameplayTimeAttackViewModel/TopMiddleContainer") { View = 1 },
                    new Slot(HUD_PLAY + "UI_SpectatorCount_Prefab/Container"),
                    new Slot(HUD_PLAY + Features.QualificationTime.FeatureQualificationTime.LIVE_TIMER),
                    new Slot(HUD_PLAY + "Generic_UI_PowerupsInventory"),
                    new Slot(HUD_UNDER + "PB_UI_ConnectionQuality"),
                    new Slot(HUD_UNDER + "PB_InfoHUD/ScreenSpace_PB_UI_PlayerLives_Local"),
                    new Slot(HUD_OVER + "PB_UI_FallFeed/SafeArea/FallFeedNotificationsContainer") { View = 2 },
                    new Slot(HUD_OVER + "MessagingUI/Generic_UI_InstructionsPopup_Prefab") { View = 2 },
                    new Slot(HUD_OVER + "MessagingUI/InfoMessage") { View = 2 },
                    new Slot(HUD_OVER + "MessagingUI/AlertMessage") { View = 2 },
                    new Slot(HUD_OVER + "Countdown/Countdown") { View = 2 },
                    new Slot(HUD_SPEC + "GameplaySpectatorViewModel/SafeArea/BumperActions") { View = 3 },
                    new Slot(HUD_SPEC + "Generic_UI_QualificationStatusPrompt_Prefab") { View = 3 },
                    new Slot(HUD_SPEC + "Generic_UI_LikesPanel") { View = 3 },
                },
            };

        public static readonly Kind Lobby =
            new Kind("lobby", "layout.kind.lobby", "UICanvas_Client_V2(Clone)/Default/Prime_UI_PrivateLobby_Canvas(Clone)")
            {
                Source = "Prime_UI_PrivateLobby_Canvas",
                ImageHost = "SafeArea",
                Slots = new[]
                {
                    new Slot("SafeArea/PlayersButton"),
                    new Slot("SafeArea/LobbyCodeGroup"),
                    new Slot("SafeArea/BottomRightGroup"),
                    new Slot("SafeArea/BottomRightGroup/ShowSelectorGroup") { HostPath = "SafeArea" },
                    new Slot("SafeArea/BottomRightGroup/StartGroup") { HostPath = "SafeArea" },
                    new Slot("SafeArea/BFG_CustomiseGroup"),
                    new Slot(LOBBY_MM + "PlayersFound") { LiveRoot = LOBBY_SCREEN },
                    new Slot(LOBBY_MM + "Loading/UI_LoadingAnim_Prefab") { LiveRoot = LOBBY_SCREEN },
                    new Slot(LOBBY_MM + "Loading/LoadingText") { LiveRoot = LOBBY_SCREEN },
                },
            };

        private const string PAUSE_OPTS = "Generic_UI_InGameMenu_Options/Container_Options";
        private const string PAUSE_BTNS = PAUSE_OPTS + "/ButtonsCarrousel/";
        private const string PARTY_NAME = "Prefab_UI_PartyMenu_Overlay";
        private const string PARTY_LIVE = "UICanvas_Client_V2(Clone)/PartyMenu";
        private const string PARTY = PARTY_NAME + "(Clone)/SafeArea/";
        private const string PARTY_HOST = PARTY + "Overlay_Container/Party_Panel_Container";
        private const string PARTY_PANEL = PARTY_HOST + "/";
        private const string PARTY_BTNS = PARTY_PANEL + "Container_Party_Buttons/Prefab_UI_Party_Menu_Button_";

        public static readonly Kind Pause =
            new Kind("pausemenu", "layout.kind.pause", "PB_UI_InGameMenu(Clone)")
            {
                Source = "PB_UI_InGameMenu",
                Extras = new[] { PARTY_NAME, "Generic_UI_LikesPanelWithInput/Generic_UI_LikesPanel" },
                Slots = new[]
                {
                    new Slot(PARTY + "Overlay_Container") { LiveRoot = PARTY_LIVE },
                    new Slot(PARTY_PANEL + "Container_Panel_Title") { LiveRoot = PARTY_LIVE },
                    new Slot(PARTY_PANEL + "Container_Party_Buttons") { LiveRoot = PARTY_LIVE },
                    new Slot(PARTY_PANEL + "VoiceModerationStatusContainer") { LiveRoot = PARTY_LIVE },
                    new Slot(PARTY_PANEL + "Players/Prefab_UI_Party_Member_Item1") { LiveRoot = PARTY_LIVE, HostPath = PARTY_HOST, Whole = true },
                    new Slot(PARTY_PANEL + "Players/Prefab_UI_Party_Member_Item2") { LiveRoot = PARTY_LIVE, HostPath = PARTY_HOST, Whole = true },
                    new Slot(PARTY_PANEL + "Players/Prefab_UI_Party_Member_Item3") { LiveRoot = PARTY_LIVE, HostPath = PARTY_HOST, Whole = true },
                    new Slot(PARTY_PANEL + "Players/Prefab_UI_Party_Member_Item4") { LiveRoot = PARTY_LIVE, HostPath = PARTY_HOST, Whole = true },
                    new Slot(PARTY_BTNS + "Leave_Party") { LiveRoot = PARTY_LIVE, HostPath = PARTY_HOST, Whole = true },
                    new Slot(PARTY_BTNS + "Report_Player") { LiveRoot = PARTY_LIVE, HostPath = PARTY_HOST, Whole = true },
                    new Slot(PARTY_BTNS + "Voice_Report") { LiveRoot = PARTY_LIVE, HostPath = PARTY_HOST, Whole = true },
                    new Slot(PARTY_BTNS + "Leave_Voice") { LiveRoot = PARTY_LIVE, HostPath = PARTY_HOST, Whole = true },
                    new Slot("Generic_UI_LikesPanel") { FixedAnchor = true },
                    new Slot(PAUSE_OPTS),
                    new Slot(PAUSE_OPTS + "/Container_Panel"),
                    new Slot(PAUSE_OPTS + "/Container_Panel/Party_Menu_Text (1)"),
                    new Slot(PAUSE_OPTS + "/ButtonsCarrousel"),
                    new Slot(PAUSE_BTNS + "Container_Button_Resume") { HostPath = PAUSE_OPTS },
                    new Slot(PAUSE_BTNS + "Container_Button_Report_Player") { HostPath = PAUSE_OPTS },
                    new Slot(PAUSE_BTNS + "Container_Button_Settings") { HostPath = PAUSE_OPTS },
                    new Slot(PAUSE_BTNS + "Container_Button_Checkpoint") { HostPath = PAUSE_OPTS },
                    new Slot(PAUSE_BTNS + "Container_Button_Leave_Show") { HostPath = PAUSE_OPTS },
                },
            };

        public const string LOBBY_MM_NAME = "UI_Matchmaking_Prime";
        private const string LOBBY_MM = LOBBY_MM_NAME + "/SafeArea/";
        private const string LOBBY_SCREEN_NAME = "Menu_Screen_Lobby(Clone)";
        private const string LOBBY_SCREEN = LOBBY_SCREEN_NAME + "/ForegroundCanvas/Prefab_UI_Lobby";

        private static Transform LiveRootFor(Slot slot, Transform root)
        {
            if (slot.LiveRoot == null) return root;
            var go = GameObject.Find(slot.LiveRoot);
            return go == null ? null : go.transform;
        }

        private static void RestoreKind(Kind kind, Transform root)
        {
            RestoreUnder(root, true);
            for (int i = 0; i < kind.Slots.Length; i++)
            {
                if (kind.Slots[i].LiveRoot == null) continue;
                var live = LiveRootFor(kind.Slots[i], root);
                var top = live == null ? null : live.Find(kind.Slots[i].Path.Split('/')[0]);
                if (top != null) RestoreUnder(top, true);
            }
        }

        public static readonly Kind[] Kinds = { MainMenu, Loading, Hud, Lobby, Pause };

        public static Slot SlotAt(Kind kind, string path)
        {
            if (path.StartsWith(IMAGE, StringComparison.Ordinal))
                return new Slot(path)
                {
                    Image = true,
                    Rest = (kind.ImageHost.Length == 0 ? "" : kind.ImageHost + "/") + IMAGES + "/" + path.Substring(IMAGE.Length),
                };
            if (path.StartsWith(PICKED, StringComparison.Ordinal)) path = path.Substring(PICKED.Length);
            var own = kind.SlotFor(path);
            if (own != null) return own;

            Slot owner = null;
            for (int i = 0; i < kind.Slots.Length; i++)
            {
                var s = kind.Slots[i];
                if (path.StartsWith(s.Path + "/", StringComparison.Ordinal) && (owner == null || s.Path.Length > owner.Path.Length)) owner = s;
            }
            return new Slot(PICKED + path)
            {
                Snap = false,
                FixedAnchor = true,
                Owner = owner,
                Rest = owner == null ? path : path.Substring(owner.Path.Length + 1),
                View = owner == null ? 0 : owner.View,
                LiveRoot = owner == null ? null : owner.LiveRoot,
            };
        }

        public const string PICKED = "+";
        public const string IMAGE = "#";
        public const string IMAGES = "BFG_Images";

        public static List<Slot> SlotsOf(Kind kind, IEnumerable<string> paths)
        {
            var list = new List<Slot>(kind.Slots);
            foreach (var p in paths)
                if ((p.StartsWith(PICKED, StringComparison.Ordinal) || p.StartsWith(IMAGE, StringComparison.Ordinal))
                    && !list.Exists(s => s.Path == p)) list.Add(SlotAt(kind, p));
            return list;
        }

        public struct Entry
        {
            public Vector2 AMin, AMax, Pivot, Pos, Size;
            public float Scale, Rot;
            public bool Hidden, Hosted;
            public int Layer;
            public string Image;

            public static Entry From(RectTransform rt) => new Entry
            {
                AMin = rt.anchorMin,
                AMax = rt.anchorMax,
                Pivot = rt.pivot,
                Pos = rt.anchoredPosition,
                Scale = 1f,
            };
        }

        public static Dictionary<string, Entry> Load(Kind kind)
        {
            var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
            if (Styles.UIStyles.Styled) return result;
            var ci = CultureInfo.InvariantCulture;

            foreach (var kv in SettingsService.Snapshot(new[] { kind.Prefix }))
            {
                var p = kv.Value.Split('|');
                if (p.Length < 11) continue;

                var e = new Entry();
                e.AMin = new Vector2(F(p[0], ci), F(p[1], ci));
                e.AMax = new Vector2(F(p[2], ci), F(p[3], ci));
                e.Pivot = new Vector2(F(p[4], ci), F(p[5], ci));
                e.Pos = new Vector2(F(p[6], ci), F(p[7], ci));
                e.Scale = F(p[8], ci);
                if (e.Scale <= 0f) e.Scale = 1f;
                e.Hidden = p[9] == "1";
                e.Hosted = p[10] == "1";
                if (p.Length > 11) e.Rot = F(p[11], ci);
                if (p.Length > 13) e.Size = new Vector2(F(p[12], ci), F(p[13], ci));
                if (p.Length > 15)
                {
                    int.TryParse(p[14], NumberStyles.Integer, ci, out e.Layer);
                    e.Image = p[15].Length == 0 ? null : p[15];
                }
                result[kv.Key.Substring(kind.Prefix.Length)] = e;
            }
            return result;
        }

        private static float F(string s, CultureInfo ci)
        {
            float.TryParse(s, NumberStyles.Float, ci, out float v);
            return v;
        }

        public static void Save(Kind kind, Dictionary<string, Entry> entries)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var ci = CultureInfo.InvariantCulture;

            foreach (var kv in entries)
            {
                var e = kv.Value;
                values[kind.Prefix + kv.Key] = string.Join("|", new[]
                {
                    e.AMin.x.ToString("0.#####", ci), e.AMin.y.ToString("0.#####", ci),
                    e.AMax.x.ToString("0.#####", ci), e.AMax.y.ToString("0.#####", ci),
                    e.Pivot.x.ToString("0.#####", ci), e.Pivot.y.ToString("0.#####", ci),
                    e.Pos.x.ToString("0.##", ci), e.Pos.y.ToString("0.##", ci),
                    e.Scale.ToString("0.###", ci),
                    e.Hidden ? "1" : "0",
                    e.Hosted ? "1" : "0",
                    e.Rot.ToString("0.##", ci),
                    e.Size.x.ToString("0.##", ci), e.Size.y.ToString("0.##", ci),
                    e.Layer.ToString(ci), e.Image ?? "",
                });
            }
            SettingsService.ReplacePrefixed(new[] { kind.Prefix }, values);
        }

        private static readonly Dictionary<string, Texture2D> _imageTex = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        public static Texture2D ImageTexture(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (_imageTex.TryGetValue(file, out var tex) && tex != null) return tex;

            var path = System.IO.Path.Combine(SettingsService.LayoutImagesDir, file);
            if (!System.IO.File.Exists(path))
            {
                Plugin.Log.LogWarning($"layout image {file} isn't in {SettingsService.LayoutImagesDir} anymore, drawing it blank");
                return null;
            }
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(System.IO.File.ReadAllBytes(path));
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _imageTex[file] = tex;
        }

        public static string ImportImage(string source)
        {
            System.IO.Directory.CreateDirectory(SettingsService.LayoutImagesDir);
            string file = Guid.NewGuid().ToString("N").Substring(0, 12) + System.IO.Path.GetExtension(source).ToLowerInvariant();
            System.IO.File.Copy(source, System.IO.Path.Combine(SettingsService.LayoutImagesDir, file));
            Plugin.Log.LogInfo($"copied {source} in as layout image {file}");
            return file;
        }

        public static RectTransform ImageBox(Kind kind, Transform root)
        {
            var host = kind.ImageHost.Length == 0 ? root : root.Find(kind.ImageHost);
            var box = host == null ? null : host.Find(IMAGES);
            return box == null ? null : box.GetComponent<RectTransform>();
        }

        public static RectTransform ImageHostOf(Kind kind, Transform root)
        {
            var host = kind.ImageHost.Length == 0 ? root : root.Find(kind.ImageHost);
            return host == null ? null : host.GetComponent<RectTransform>();
        }

        public static void EnsureImages(Kind kind, Transform root, Dictionary<string, Entry> entries)
        {
            var host = ImageHostOf(kind, root);
            if (host == null) return;

            var order = new List<(int Layer, string Id, string File)>();
            foreach (var kv in entries)
                if (kv.Key.StartsWith(IMAGE, StringComparison.Ordinal)) order.Add((kv.Value.Layer, kv.Key.Substring(IMAGE.Length), kv.Value.Image));

            var box = host.Find(IMAGES);
            if (box == null)
            {
                if (order.Count == 0) return;
                var brt = new GameObject(IMAGES).AddComponent<RectTransform>();
                brt.SetParent(host, false);
                brt.anchorMin = Vector2.zero;
                brt.anchorMax = Vector2.one;
                brt.offsetMin = brt.offsetMax = Vector2.zero;
                box = brt;
            }

            for (int i = box.childCount - 1; i >= 0; i--)
            {
                var c = box.GetChild(i);
                if (!entries.ContainsKey(IMAGE + c.name)) UnityEngine.Object.DestroyImmediate(c.gameObject);
            }

            order.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : string.CompareOrdinal(a.Id, b.Id));
            for (int i = 0; i < order.Count; i++)
            {
                var t = box.Find(order[i].Id);
                if (t == null)
                {
                    var rt = new GameObject(order[i].Id).AddComponent<RectTransform>();
                    rt.SetParent(box, false);
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.gameObject.AddComponent<UnityEngine.UI.RawImage>().raycastTarget = false;
                    t = rt;
                }
                t.GetComponent<UnityEngine.UI.RawImage>().texture = ImageTexture(order[i].File);
                t.SetSiblingIndex(i);
            }
            box.SetAsLastSibling();
        }

        public static int Count(Kind kind) => SettingsService.Snapshot(new[] { kind.Prefix }).Count;

        public static void ClearAll(Kind kind)
        {
            var root = kind.Root;
            if (root != null && kind.Source != null) RestoreKind(kind, root);
            else if (root != null) RestoreUnder(root);
            SettingsService.ReplacePrefixed(new[] { kind.Prefix }, null);
        }

        private struct Stock
        {
            public Vector2 AMin, AMax, Pivot, Pos, Size;
            public Vector3 Scale;
            public Quaternion Rot;
            public bool Active;
            public Transform Parent;
            public int SiblingIndex;
            public bool LayoutGroupOn, FitterOn;
        }

        private static readonly Dictionary<int, Stock> _stock = new Dictionary<int, Stock>();
        private static readonly Dictionary<int, RectTransform> _touched = new Dictionary<int, RectTransform>();

        public static void ForgetStock()
        {
            _stock.Clear();
            _touched.Clear();
        }

        public static void PruneDead()
        {
            var dead = new List<int>();
            foreach (var kv in _touched)
                if (kv.Value == null) dead.Add(kv.Key);
            foreach (int id in dead)
            {
                _touched.Remove(id);
                _stock.Remove(id);
            }
        }

        private static Stock StockOf(RectTransform rt)
        {
            int id = rt.GetInstanceID();
            if (!_stock.TryGetValue(id, out var s))
            {
                s = new Stock
                {
                    AMin = rt.anchorMin,
                    AMax = rt.anchorMax,
                    Pivot = rt.pivot,
                    Pos = rt.anchoredPosition,
                    Size = rt.sizeDelta,
                    Scale = rt.localScale,
                    Rot = rt.localRotation,
                    Active = rt.gameObject.activeSelf,
                    Parent = rt.parent,
                    SiblingIndex = rt.GetSiblingIndex(),
                    LayoutGroupOn = LayoutGroupEnabled(rt),
                    FitterOn = FitterEnabled(rt),
                };
                var tween = rt.GetComponent<FGClient.Tweening.AppearTween>();
                if (tween != null && tween._rectTransform != null)
                {
                    if (tween._useRectMovementTween) s.Pos = tween._startAnchorPos;
                    if (tween._useScaleTween) s.Scale = tween._startLocalScale;
                }
                var pauseTween = rt.GetComponentInParent<TweenPauseMenu>();
                var popped = pauseTween == null ? null : pauseTween._rects;
                for (int i = 0; popped != null && i < popped.Length; i++)
                    if (popped[i] == rt) { s.Scale = Vector3.one; break; }
                _stock[id] = s;
                _touched[id] = rt;
            }
            return s;
        }

        public static void CopyStockPose(RectTransform from, RectTransform to)
        {
            if (!_stock.TryGetValue(from.GetInstanceID(), out var s)) return;
            to.anchorMin = s.AMin;
            to.anchorMax = s.AMax;
            to.pivot = s.Pivot;
            to.anchoredPosition = s.Pos;
            to.sizeDelta = s.Size;
            to.localRotation = s.Rot;
            to.localScale = s.Scale;
        }

        public static void RestoreLive(Kind kind)
        {
            var root = kind.Root;
            if (root == null) return;
            foreach (var slot in SlotsOf(kind, Load(kind).Keys))
            {
                var live = Resolve(root, slot);
                if (live == null) continue;
                if (slot.FramePath != null) Restore(live.parent.GetComponent<RectTransform>());
                Restore(live);
                var link = LinkOf(slot, live);
                if (link != null) Restore(link);
            }
        }

        public static void RestoreStaged(Kind kind, Slot slot, Transform stage)
        {
            var staged = Resolve(stage, slot);
            if (staged == null) return;

            var root = slot.LiveRoot != null ? LiveRootFor(slot, null) : kind.Root;
            var live = root == null ? null : Resolve(root, slot);
            if (live == null || !_stock.ContainsKey(live.GetInstanceID()))
            {
                StockOf(staged);
                Restore(staged, true);
                var ownLink = LinkOf(slot, staged);
                if (ownLink != null)
                {
                    StockOf(ownLink);
                    Restore(ownLink, true);
                }
                Relayout(staged);
                return;
            }

            if (slot.FramePath != null)
                MirrorStock(live.parent.GetComponent<RectTransform>(), staged.parent.GetComponent<RectTransform>(), root, stage);
            MirrorStock(live, staged, root, stage);
            var liveLink = LinkOf(slot, live);
            var stagedLink = LinkOf(slot, staged);
            if (liveLink != null && stagedLink != null) MirrorStock(liveLink, stagedLink, root, stage);
            Relayout(staged);
        }

        private static void Relayout(RectTransform rt)
        {
            var parent = rt.parent == null ? null : rt.parent.GetComponent<RectTransform>();
            var lg = parent == null ? null : parent.GetComponent<UnityEngine.UI.LayoutGroup>();
            if (lg != null && lg.enabled) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
        }

        public static void MirrorTouched(Transform liveRoot, Transform stagedRoot)
        {
            var pairs = new List<(RectTransform Live, Transform Staged)>();
            foreach (var rt in _touched.Values)
            {
                if (rt == null || rt.transform == liveRoot || !rt.IsChildOf(liveRoot)) continue;
                var path = PathOf(liveRoot, rt);
                var staged = path == null ? null : stagedRoot.Find(path);
                if (staged != null) pairs.Add((rt, staged));
            }
            foreach (var (live, staged) in pairs) MirrorStock(live, staged.GetComponent<RectTransform>(), liveRoot, stagedRoot);
            if (pairs.Count > 0) Plugin.Log.LogInfo($"layout stage: put {pairs.Count} live-edited bits of {liveRoot.name} back to stock on the copy");
        }

        private static void MirrorStock(RectTransform live, RectTransform staged, Transform root, Transform stage)
        {
            if (!_stock.TryGetValue(live.GetInstanceID(), out var s)) return;

            string path = "";
            for (var p = s.Parent; p != null && p != root; p = p.parent) path = path.Length == 0 ? p.name : p.name + "/" + path;
            var parent = path.Length == 0 ? stage : stage.Find(path);
            if (parent != null && staged.parent != parent)
            {
                staged.SetParent(parent, false);
                staged.SetSiblingIndex(Mathf.Min(s.SiblingIndex, parent.childCount - 1));
            }

            var lg = staged.GetComponent<UnityEngine.UI.LayoutGroup>();
            if (lg != null) lg.enabled = s.LayoutGroupOn;
            var fit = staged.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fit != null) fit.enabled = s.FitterOn;
            staged.anchorMin = s.AMin;
            staged.anchorMax = s.AMax;
            staged.pivot = s.Pivot;
            staged.anchoredPosition = s.Pos;
            staged.sizeDelta = s.Size;
            staged.localScale = s.Scale;
            staged.localRotation = s.Rot;
        }

        public static RectTransform Resolve(Transform root, Slot slot)
        {
            if (slot.Rest != null)
            {
                var owner = slot.Owner == null ? root : Resolve(root, slot.Owner);
                var child = owner == null ? null : owner.Find(slot.Rest);
                return child == null ? null : child.GetComponent<RectTransform>();
            }

            var t = root.Find(slot.Path);
            if (t != null) return t.GetComponent<RectTransform>();

            if (slot.FramePath != null)
            {
                var frameHost = root.Find(slot.FrameHostPath);
                if (frameHost == null) return null;
                int cut = slot.FramePath.LastIndexOf('/');
                var frame = frameHost.Find(cut < 0 ? slot.FramePath : slot.FramePath.Substring(cut + 1));
                if (frame == null) return null;
                var child = frame.Find(slot.Name);
                return child == null ? null : child.GetComponent<RectTransform>();
            }

            if (slot.Deep)
            {
                foreach (var r in root.GetComponentsInChildren<RectTransform>(true))
                    if (r.name == slot.Name) return r;
                return null;
            }

            if (slot.HostPath == null) return null;
            var host = HostFor(root, slot);
            if (host == null) return null;
            t = host.Find(slot.Name);
            return t == null ? null : t.GetComponent<RectTransform>();
        }

        public static RectTransform LinkOf(Slot slot, RectTransform rt)
        {
            if (slot == null || slot.Link == null || rt.parent == null) return null;
            var t = rt.parent.Find(slot.Link);
            return t == null ? null : t.GetComponent<RectTransform>();
        }

        public static void ApplySlot(Slot slot, RectTransform rt, Entry e, bool soft = false)
        {
            ApplyTo(rt, e, soft);
            var link = LinkOf(slot, rt);
            if (link == null) return;
            var le = e;
            le.Pos += StockOf(link).Pos - StockOf(rt).Pos;
            ApplyTo(link, le, soft);
        }

        public const string LAYOUT_HOST = "BFG_LayoutHost";

        public static RectTransform HostFor(Transform root, Slot slot)
        {
            if (slot.HostPath == null) return null;
            var host = root.Find(slot.HostPath);
            return host == null ? null : host.GetComponent<RectTransform>();
        }

        public static void DropStrayHost(Transform root)
        {
            var stray = root.Find(LAYOUT_HOST);
            if (stray == null) return;
            Plugin.Log.LogInfo("binning a leftover BFG_LayoutHost, that thing had no business outliving the menu");
            UnityEngine.Object.DestroyImmediate(stray.gameObject);
        }

        private static GameObject _ticker;

        private static void EnsureTicker()
        {
            if (_ticker != null) return;
            _ticker = new GameObject("BettrFG_LayoutSync");
            _ticker.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_ticker);
            _ticker.AddComponent<LayoutSync>();
        }

        public static bool MoveToHost(Transform root, Slot slot, RectTransform rt)
        {
            if (slot.FramePath != null) return PromoteFrame(root, slot);

            var host = HostFor(root, slot);
            if (host == null || rt == null || rt.parent == host) return false;

            StockOf(rt);
            var lg = rt.parent == null ? null : rt.parent.GetComponent<UnityEngine.UI.LayoutGroup>();
            if (lg != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt.parent.GetComponent<RectTransform>());
            var size = rt.rect.size;
            if (rt.anchorMin != rt.anchorMax)
            {
                var world = rt.position;
                rt.anchorMin = rt.anchorMax = rt.pivot;
                rt.sizeDelta = size;
                rt.position = world;
            }
            rt.SetParent(host, true);
            rt.sizeDelta = size;
            return true;
        }

        public static bool PromoteFrame(Transform root, Slot slot)
        {
            var frameT = root.Find(slot.FramePath);
            if (frameT == null)
            {
                var host = root.Find(slot.FrameHostPath);
                if (host == null) return false;
                int cut = slot.FramePath.LastIndexOf('/');
                frameT = host.Find(cut < 0 ? slot.FramePath : slot.FramePath.Substring(cut + 1));
                if (frameT == null) return false;
            }

            var frame = frameT.GetComponent<RectTransform>();
            StockOf(frame);

            var frameHost = root.Find(slot.FrameHostPath);
            if (frameHost == null) return false;
            if (frame.parent != frameHost) frame.SetParent(frameHost, false);

            var lg = frame.GetComponent<UnityEngine.UI.LayoutGroup>();
            if (lg != null) lg.enabled = false;
            var fit = frame.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fit != null) fit.enabled = false;

            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = frame.offsetMax = Vector2.zero;
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.localScale = Vector3.one;

            for (int i = 0; i < _watch.Count; i++)
                if (_watch[i].Rt == frame) return true;

            _stock.TryGetValue(frame.GetInstanceID(), out var st);
            _watch.Add(new Watch { Rt = frame, Shadow = st.Parent, IsFrame = true, HomeOnly = slot.HomeOnly });
            return true;
        }

        public static void ApplyTo(RectTransform rt, Entry e, bool soft = false)
        {
            var s = StockOf(rt);
            rt.anchorMin = e.AMin;
            rt.anchorMax = e.AMax;
            rt.pivot = e.Pivot;
            rt.anchoredPosition = e.Pos;
            if (e.Size.x > 0f) rt.sizeDelta = e.Size;
            rt.localScale = soft && e.Hidden ? Vector3.zero : new Vector3(s.Scale.x * e.Scale, s.Scale.y * e.Scale, s.Scale.z);
            rt.localRotation = s.Rot * Quaternion.Euler(0f, 0f, e.Rot);
            if (!soft) rt.gameObject.SetActive(s.Active && !e.Hidden);
            RebaseTween(rt);
        }

        public static void ApplyLoading(Transform inside)
        {
            var root = inside;
            while (root.parent != null && root.parent.name != "LoadingScreen") root = root.parent;
            ApplySoft(Loading, root);
        }

        public static void ApplyLobby()
        {
            var root = Lobby.Root;
            if (root != null) ApplySoft(Lobby, root);
        }

        private static readonly Dictionary<int, GameObject[]> _optionStock = new Dictionary<int, GameObject[]>();
        private static readonly Dictionary<int, (MenuInputHandler.MoveLeft L, MenuInputHandler.MoveRight R)> _sideStock =
            new Dictionary<int, (MenuInputHandler.MoveLeft, MenuInputHandler.MoveRight)>();

        public static void ApplyPause(FGClient.InGameMenuViewModel vm) => ApplySoft(Pause, vm.transform);

        public static void ApplyParty()
        {
            var party = GameObject.Find(PARTY_LIVE + "/" + PARTY_NAME + "(Clone)").transform;
            PruneDead();
            RestoreUnder(party, true);
            var entries = Load(Pause);
            foreach (var slot in SlotsOf(Pause, entries.Keys))
            {
                if (slot.LiveRoot == null || !entries.TryGetValue(slot.Path, out var e)) continue;
                var rt = Resolve(party.parent, slot);
                if (rt == null) continue;
                if (e.Hosted) MoveToHost(party.parent, slot, rt);
                ApplySlot(slot, rt, e, true);
            }
            RelinkNav(party);
        }

        public static IEnumerator SettlePause(FGClient.InGameMenuViewModel vm)
        {
            yield return null;
            if (vm == null) yield break;

            var like = vm._levelLikeViewModel;
            ApplySoft(Pause, vm.transform);
            if (like != null)
            {
                ApplySoft(Pause, like.transform, false);
                var wrap = like.GetComponent<RectTransform>();
                StockOf(wrap);
                wrap.pivot = wrap.anchorMin;
                wrap.anchorMin = Vector2.zero;
                wrap.anchorMax = Vector2.one;
                wrap.offsetMin = wrap.offsetMax = Vector2.zero;
            }

            var carousel = vm._menuInputHandler;
            var top = vm.transform.Find(PAUSE_OPTS);
            if (!_optionStock.TryGetValue(carousel.GetInstanceID(), out var stock))
                _optionStock[carousel.GetInstanceID()] = stock = carousel._menuOptions.ToArray();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(carousel.GetComponent<RectTransform>());
            carousel._menuOptions = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject>(stock
                .Where(g => g.transform.localScale.x != 0f)
                .OrderByDescending(g => LocalY(g.transform, top))
                .ToArray());
            carousel.ResetNavigation();

            var entries = Load(Pause);
            EnsureTicker();
            _watch.RemoveAll(w => w.Pause);
            foreach (var slot in SlotsOf(Pause, entries.Keys))
            {
                if (slot.LiveRoot != null || !entries.TryGetValue(slot.Path, out var e)) continue;
                var rt = Resolve(vm.transform, slot);
                if (rt == null && like != null) rt = Resolve(like.transform, slot);
                if (rt == null) continue;
                _watch.Add(new Watch { Rt = rt, E = e, Pause = true, HoldScale = e.Hidden || e.Scale != 1f, Scale = rt.localScale });
            }
            var party = GameObject.Find(PARTY_LIVE + "/" + PARTY_NAME + "(Clone)");
            bool partyRight = party != null && entries.TryGetValue(PARTY + "Overlay_Container", out var pe)
                && pe.AMin.x + pe.Pos.x / party.GetComponent<RectTransform>().rect.width > 0.5f;
            SetSides(carousel, partyRight);
            if (party != null)
            {
                SetSides(party.GetComponent<MenuInputHandler>(), partyRight);
                RelinkNav(party.transform);
            }

            var lh = like == null ? null : like._inputHandler;
            if (lh == null) yield break;
            float w = like.GetComponentInParent<Canvas>().rootCanvas.GetComponent<RectTransform>().rect.width;
            bool likeLeft = entries.TryGetValue("Generic_UI_LikesPanel", out var le) && le.Pos.x < -w * 0.5f;
            if (!_sideStock.TryGetValue(lh.GetInstanceID(), out var ls)) _sideStock[lh.GetInstanceID()] = ls = (lh.onMoveLeft, lh.onMoveRight);
            lh.onMoveRight = likeLeft
                ? Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<MenuInputHandler.MoveRight>(new Action(() => lh.onMoveLeft?.Invoke()))
                : ls.R;
        }

        private static float LocalY(Transform t, Transform top)
        {
            var p = Vector3.zero;
            for (; t != top; t = t.parent) p = t.localRotation * Vector3.Scale(p, t.localScale) + t.localPosition;
            return p.y;
        }

        private static void SetSides(MenuInputHandler h, bool swap)
        {
            if (!_sideStock.TryGetValue(h.GetInstanceID(), out var s)) _sideStock[h.GetInstanceID()] = s = (h.onMoveLeft, h.onMoveRight);
            if (!swap || (s.L == null && s.R == null))
            {
                h.onMoveLeft = s.L;
                h.onMoveRight = s.R;
                return;
            }
            h.onMoveLeft = s.R == null ? null : Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<MenuInputHandler.MoveLeft>(new Action(s.R.Invoke));
            h.onMoveRight = s.L == null ? null : Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<MenuInputHandler.MoveRight>(new Action(s.L.Invoke));
        }

        public static void RestoreLobbyScreen()
        {
            foreach (var rt in _touched.Values)
            {
                var p = rt == null ? null : rt.parent;
                while (p != null && p.name != LOBBY_SCREEN_NAME) p = p.parent;
                if (p == null) continue;

                Restore(rt, true);
                foreach (var data in rt.GetComponentsInChildren<FGClient.Tweening.UITweenData>(true))
                {
                    var anims = data.Animations;
                    for (int a = 0; anims != null && a < anims.Count; a++)
                        if (anims[a].Preset != null && _presetOrigins.TryGetValue(anims[a].Preset.GetInstanceID(), out var orig))
                            anims[a].Preset = orig;
                }
                var tween = rt.GetComponentInParent<FGClient.Tweening.UITweenData>();
                if (tween != null) tween.ClearSavedPrefabValues();
            }
        }

        private static void ApplySoft(Kind kind, Transform root, bool images = true)
        {
            PruneDead();
            RestoreKind(kind, root);

            var entries = Load(kind);
            if (images) EnsureImages(kind, root, entries);
            if (entries.Count == 0) return;
            foreach (var slot in SlotsOf(kind, entries.Keys))
            {
                if (!entries.TryGetValue(slot.Path, out var e)) continue;

                var live = LiveRootFor(slot, root);
                var rt = live == null ? null : Resolve(live, slot);
                if (rt == null) continue;
                if (e.Hosted) MoveToHost(live, slot, rt);
                ApplySlot(slot, rt, e, true);
            }
            if (kind == Lobby) RelinkNav(root);
            Plugin.Log.LogInfo($"{kind.Id} layout on {root.name}, {entries.Count} saved");
        }

        private static readonly Vector2[] NavDirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

        private static void RelinkNav(Transform root)
        {
            foreach (var lg in root.GetComponentsInChildren<UnityEngine.UI.LayoutGroup>(false))
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(lg.GetComponent<RectTransform>());

            var sels = new List<UnityEngine.UI.Selectable>();
            foreach (var s in root.GetComponentsInChildren<UnityEngine.UI.Selectable>(false))
                if (s.navigation.mode == UnityEngine.UI.Navigation.Mode.Explicit && s.transform.lossyScale.x > 0.0001f) sels.Add(s);

            var centres = new Vector2[sels.Count];
            for (int i = 0; i < sels.Count; i++)
            {
                var rt = sels[i].GetComponent<RectTransform>();
                centres[i] = root.InverseTransformPoint(rt.TransformPoint(rt.rect.center));
            }

            var handler = root.GetComponent<FGClient.UI.NavigableMenuInputHandler>();
            if (handler != null)
            {
                handler.onMoveUp = null;
                handler.onMoveDown = null;
                handler.onMoveLeft = null;
                handler.onMoveRight = null;
            }

            var pick = new UnityEngine.UI.Selectable[4];
            for (int i = 0; i < sels.Count; i++)
            {
                for (int d = 0; d < 4; d++)
                {
                    pick[d] = null;
                    float best = float.MaxValue;
                    for (int j = 0; j < sels.Count; j++)
                    {
                        var v = centres[j] - centres[i];
                        float along = Vector2.Dot(v, NavDirs[d]);
                        float perp = Mathf.Abs(v.x * NavDirs[d].y - v.y * NavDirs[d].x);
                        if (j == i || along <= 1f || perp >= along) continue;
                        float score = along + perp * 2f;
                        if (score < best) { best = score; pick[d] = sels[j]; }
                    }
                }

                var nav = sels[i].navigation;
                nav.selectOnUp = pick[0];
                nav.selectOnDown = pick[1];
                nav.selectOnLeft = pick[2];
                nav.selectOnRight = pick[3];
                sels[i].navigation = nav;

                var modes = new UnityEngine.UI.Navigation.Mode[4];
                for (int d = 0; d < 4; d++)
                    modes[d] = pick[d] == null ? UnityEngine.UI.Navigation.Mode.None : UnityEngine.UI.Navigation.Mode.Explicit;

                var tg = sels[i].TryCast<UICustomToggle>();
                if (tg != null && (handler == null || handler._inFocus))
                {
                    tg._navigationModeUp = modes[0];
                    tg._navigationModeDown = modes[1];
                    tg._navigationModeLeft = modes[2];
                    tg._navigationModeRight = modes[3];
                }
                if (handler != null && handler._cachedNavModes.ContainsKey(sels[i]))
                {
                    var cached = handler._cachedNavModes[sels[i]];
                    for (int d = 0; d < 4; d++) cached[d + 1] = modes[d];
                }
            }
        }

        private static void RebaseTween(RectTransform rt)
        {
            RedirectTweens(rt);
            var tween = rt.GetComponentInParent<FGClient.Tweening.UITweenData>();
            if (tween != null) tween.ClearSavedPrefabValues();
        }

        private static readonly Dictionary<int, FGClient.Tweening.UITweenPreset> _presetOrigins = new Dictionary<int, FGClient.Tweening.UITweenPreset>();

        private static void RedirectTweens(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRt = canvas.rootCanvas.GetComponent<RectTransform>();
            Vector2 res = canvasRt.rect.size;
            if (res.x < 1f || res.y < 1f) return;

            Vector2 centre = canvasRt.InverseTransformPoint(rt.TransformPoint(rt.rect.center));
            Vector3 span = canvasRt.InverseTransformVector(rt.TransformVector(new Vector3(rt.rect.width, rt.rect.height, 0f)));
            Vector2 half = res * 0.5f;
            float toRight = half.x - centre.x, toLeft = half.x + centre.x;
            float toTop = half.y - centre.y, toBottom = half.y + centre.y;

            Vector2 outward;
            float reach;
            if (Mathf.Min(toLeft, toRight) <= Mathf.Min(toTop, toBottom))
            {
                outward = toRight <= toLeft ? new Vector2(1f, 0f) : new Vector2(-1f, 0f);
                reach = Mathf.Min(toLeft, toRight) + Mathf.Abs(span.x) * 0.5f + 60f;
            }
            else
            {
                outward = toTop <= toBottom ? new Vector2(0f, 1f) : new Vector2(0f, -1f);
                reach = Mathf.Min(toTop, toBottom) + Mathf.Abs(span.y) * 0.5f + 60f;
            }

            foreach (var data in rt.GetComponentsInChildren<FGClient.Tweening.UITweenData>(true))
            {
                var anims = data.Animations;
                if (anims == null) continue;

                for (int a = 0; a < anims.Count; a++)
                {
                    var anim = anims[a];
                    var preset = anim.Preset;
                    if (preset == null || preset.Layers == null) continue;

                    if (!_presetOrigins.ContainsKey(preset.GetInstanceID()))
                    {
                        var orig = preset;
                        preset = UnityEngine.Object.Instantiate(preset).Cast<FGClient.Tweening.UITweenPreset>();
                        _presetOrigins[preset.GetInstanceID()] = orig;
                        anim.Preset = preset;
                    }

                    var layers = preset.Layers;
                    var proxies = anim.LayerProxies;
                    for (int i = 0; i < layers.Count; i++)
                    {
                        var layer = layers[i];
                        if (layer.Type != FGClient.Tweening.UITweenType.Move) continue;

                        var target = data.GetComponent<RectTransform>();
                        if (proxies != null && i < proxies.Count && proxies[i] != null && proxies[i].Target != null)
                        {
                            var go = proxies[i].Target.TryCast<GameObject>();
                            var own = go == null ? null : go.GetComponent<RectTransform>();
                            if (own != null) target = own;
                        }
                        if (target == null || target.parent == null) continue;

                        Vector2 rest = target.anchoredPosition;
                        var saved = data._defaultPrefabValues;
                        for (int k = 0; saved != null && k < saved.Count && !_stock.ContainsKey(target.GetInstanceID()); k++)
                            if (saved[k].Type == FGClient.Tweening.UITweenType.Move && saved[k].Comp == target) rest = saved[k].Value;
                        Vector2 fromOff, toOff;
                        if (layer.ScreenSpace)
                        {
                            fromOff = new Vector2(layer.InitialState.x * res.x / 100f, layer.InitialState.y * res.y / 100f);
                            if (!layer.SampleX) fromOff.x -= rest.x;
                            if (!layer.SampleY) fromOff.y -= rest.y;
                            toOff = new Vector2(layer.FinalState.x * res.x / 100f, layer.FinalState.y * res.y / 100f);
                            if (!layer.FromSampleX) toOff.x -= rest.x;
                            if (!layer.FromSampleY) toOff.y -= rest.y;
                        }
                        else
                        {
                            fromOff = new Vector2(layer.SampleX ? 0f : layer.InitialState.x - rest.x,
                                                  layer.SampleY ? 0f : layer.InitialState.y - rest.y);
                            toOff = new Vector2(layer.FromSampleX ? layer.FinalState.x : layer.FinalState.x - rest.x,
                                                layer.FromSampleY ? layer.FinalState.y : layer.FinalState.y - rest.y);
                        }

                        Vector2 big = fromOff.sqrMagnitude >= toOff.sqrMagnitude ? fromOff : toOff;
                        float mag = Mathf.Max(Mathf.Abs(big.x), Mathf.Abs(big.y));
                        if (mag < 60f) continue;

                        Vector2 oldOut = Mathf.Abs(big.x) >= Mathf.Abs(big.y)
                            ? new Vector2(Mathf.Sign(big.x), 0f)
                            : new Vector2(0f, Mathf.Sign(big.y));
                        float cos = Vector2.Dot(oldOut, outward);
                        float sin = oldOut.x * outward.y - oldOut.y * outward.x;
                        float local = canvasRt.lossyScale.x / Mathf.Max(0.0001f, target.parent.lossyScale.x);
                        float grow = reach * local / mag;

                        fromOff = new Vector2((fromOff.x * cos - fromOff.y * sin) * grow, (fromOff.x * sin + fromOff.y * cos) * grow);
                        toOff = new Vector2((toOff.x * cos - toOff.y * sin) * grow, (toOff.x * sin + toOff.y * cos) * grow);

                        layer.ScreenSpace = true;
                        layer.SampleX = layer.SampleY = true;
                        layer.FromSampleX = layer.FromSampleY = true;
                        layer.InitialState = new Vector3(fromOff.x * 100f / res.x, fromOff.y * 100f / res.y, layer.InitialState.z);
                        layer.FinalState = new Vector3(toOff.x * 100f / res.x, toOff.y * 100f / res.y, layer.FinalState.z);
                    }
                }
            }
        }

        private static bool LayoutGroupEnabled(RectTransform rt)
        {
            var lg = rt.GetComponent<UnityEngine.UI.LayoutGroup>();
            return lg != null && lg.enabled;
        }

        private static bool FitterEnabled(RectTransform rt)
        {
            var fit = rt.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            return fit != null && fit.enabled;
        }

        public static void Restore(RectTransform rt, bool soft = false)
        {
            if (!_stock.TryGetValue(rt.GetInstanceID(), out var s)) return;

            var lg = rt.GetComponent<UnityEngine.UI.LayoutGroup>();
            if (lg != null) lg.enabled = s.LayoutGroupOn;
            var fit = rt.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fit != null) fit.enabled = s.FitterOn;
            if (s.Parent != null && rt.parent != s.Parent)
            {
                rt.SetParent(s.Parent, false);
                rt.SetSiblingIndex(s.SiblingIndex);
            }
            rt.anchorMin = s.AMin;
            rt.anchorMax = s.AMax;
            rt.pivot = s.Pivot;
            rt.anchoredPosition = s.Pos;
            rt.sizeDelta = s.Size;
            rt.localScale = s.Scale;
            rt.localRotation = s.Rot;
            if (!soft) rt.gameObject.SetActive(s.Active);
        }

        public static void RestoreUnder(Transform root, bool soft = false)
        {
            foreach (var kv in _touched)
                if (kv.Value != null && kv.Value.IsChildOf(root)) Restore(kv.Value, soft);
        }

        private struct Watch
        {
            public RectTransform Rt;
            public Transform Shadow;
            public Entry E;
            public bool IsFrame;
            public bool HomeOnly;
            public bool Pause;
            public bool HoldScale;
            public Vector3 Scale;
        }

        private static readonly List<Watch> _watch = new List<Watch>();

        public static void Apply(Kind kind)
        {
            if (kind == Hud) { ApplyHud(); return; }
            if (kind == Lobby) { ApplyLobby(); return; }
            var root = kind.Root;
            if (root == null || kind.Source != null) return;

            DropStrayHost(root);
            EnsureTicker();

            _watch.Clear();
            var entries = Load(kind);
            EnsureImages(kind, root, entries);
            foreach (var slot in SlotsOf(kind, entries.Keys))
            {
                if (!entries.TryGetValue(slot.Path, out var e)) continue;

                var rt = Resolve(root, slot);
                if (rt == null) continue;
                if (e.Hosted) MoveToHost(root, slot, rt);
                ApplyTo(rt, e);

                _stock.TryGetValue(rt.GetInstanceID(), out var st);
                _watch.Add(new Watch { Rt = rt, Shadow = st.Parent, E = e, HomeOnly = slot.HomeOnly });
            }
            _homeApplied = true;
            _lastView = -1;
        }

        public static void ApplyHud()
        {
            var kind = Hud;
            var root = kind.Root;
            if (root == null) return;

            EnsureTicker();
            PruneDead();
            RestoreUnder(root, true);
            _watch.Clear();

            var entries = Load(kind);
            EnsureImages(kind, root, entries);
            var states = root.Find("GameStates");
            foreach (var slot in SlotsOf(kind, entries.Keys))
            {
                if (!entries.TryGetValue(slot.Path, out var e)) continue;

                var real = slot.Rest == null ? slot.Path : slot.Owner == null ? slot.Rest : slot.Owner.Path + "/" + slot.Rest;
                if (!real.StartsWith(HUD_PLAY, StringComparison.Ordinal))
                {
                    PinHud(root.Find(real), e);
                    continue;
                }

                var rest = real.Substring(HUD_PLAY.Length);
                for (int s = 0; s < states.childCount; s++)
                    PinHud(states.GetChild(s).Find(rest), e);
            }
            if (entries.Count > 0) Plugin.Log.LogInfo($"hud layout pinned, {entries.Count} saved, {_watch.Count} rects held");
        }

        public static void Unpin(RectTransform rt)
        {
            _watch.RemoveAll(w => w.Rt == rt);
            Restore(rt, true);
        }

        public static void PlaceSpectatorCount(Transform playingState)
        {
            var spec = playingState.Find("UI_SpectatorCount_Prefab");
            if (spec == null)
            {
                Plugin.Log.LogInfo("spectator count prefab not up yet, left it where it was");
                return;
            }

            var rt = spec.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.one;
            var size = rt.parent.GetComponent<RectTransform>().rect.size;
            rt.anchoredPosition = new Vector2(-size.x * 0.18f, -size.y * 0.18f);
            spec.Find("Container").localPosition = Vector3.zero;
        }

        private static void PinHud(Transform t, Entry e)
        {
            if (t == null) return;
            var rt = t.GetComponent<RectTransform>();
            ApplyTo(rt, e, true);
            _watch.Add(new Watch { Rt = rt, E = e });
        }

        private static bool _homeApplied = true;
        private static int _lastView = -1;

        private static int CurrentView()
        {
            var sv = MenuCustomizationApplication.MainMenuSwitchableView;
            return sv == null ? -1 : sv.CurrentViewIndex;
        }

        private static void SetHomeOnlyApplied(bool on)
        {
            _homeApplied = on;
            var kind = MainMenu;
            var root = kind.Root;
            if (root == null) return;

            var entries = Load(kind);
            for (int i = 0; i < kind.Slots.Length; i++)
            {
                var slot = kind.Slots[i];
                if (!slot.HomeOnly) continue;
                if (!entries.TryGetValue(slot.Path, out var e)) continue;

                var rt = Resolve(root, slot);
                if (rt == null) continue;

                if (on)
                {
                    if (e.Hosted) MoveToHost(root, slot, rt);
                    ApplyTo(rt, e);
                    continue;
                }

                Restore(rt);
                if (slot.FramePath == null) continue;

                var frameHost = root.Find(slot.FrameHostPath);
                int cut = slot.FramePath.LastIndexOf('/');
                var frameT = frameHost == null ? null : frameHost.Find(cut < 0 ? slot.FramePath : slot.FramePath.Substring(cut + 1));
                if (frameT != null) Restore(frameT.GetComponent<RectTransform>());
            }
        }

        public static void Tick()
        {
            int view = CurrentView();
            if (view != _lastView)
            {
                _lastView = view;
                bool wantHome = view <= 0;
                if (wantHome != _homeApplied) SetHomeOnlyApplied(wantHome);
            }

            for (int i = 0; i < _watch.Count; i++)
            {
                var w = _watch[i];
                if (w.Rt == null) continue;
                if (w.HomeOnly && !_homeApplied) continue;

                if (w.Shadow != null)
                {
                    bool want = w.Shadow.gameObject.activeInHierarchy && !w.E.Hidden;
                    if (w.Rt.gameObject.activeSelf != want) w.Rt.gameObject.SetActive(want);
                }

                if (w.HoldScale && w.Rt.localScale != w.Scale) w.Rt.localScale = w.Scale;

                if (w.IsFrame)
                {
                    if (w.Rt.anchorMin != Vector2.zero || w.Rt.anchorMax != Vector2.one)
                    {
                        w.Rt.anchorMin = Vector2.zero;
                        w.Rt.anchorMax = Vector2.one;
                        w.Rt.offsetMin = w.Rt.offsetMax = Vector2.zero;
                    }
                    continue;
                }

                if (w.Rt.anchorMin != w.E.AMin || w.Rt.anchorMax != w.E.AMax || w.Rt.pivot != w.E.Pivot ||
                    (w.Rt.anchoredPosition - w.E.Pos).sqrMagnitude > 0.01f)
                {
                    w.Rt.anchorMin = w.E.AMin;
                    w.Rt.anchorMax = w.E.AMax;
                    w.Rt.pivot = w.E.Pivot;
                    w.Rt.anchoredPosition = w.E.Pos;
                }
            }
        }

        public static string PathOf(Transform root, Transform t)
        {
            if (t == null || root == null) return null;
            string path = t.name;
            var cur = t.parent;
            while (cur != null && cur != root)
            {
                path = cur.name + "/" + path;
                cur = cur.parent;
            }
            return cur == root ? path : null;
        }

        public static void OnMainMenuEntered(MonoBehaviour host)
        {
            PruneDead();
            Apply(MainMenu);
            if (host != null) host.StartCoroutine(ReapplyRoutine().WrapToIl2Cpp());
        }

        private static IEnumerator ReapplyRoutine()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            Apply(MainMenu);
        }
    }
}
