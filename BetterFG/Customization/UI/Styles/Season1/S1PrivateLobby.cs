using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using FG.Common.CMS;
using FGClient.UI.PrivateLobby;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1PrivateLobby
    {
        const string HOST = "BFG_S1PrivateLobby";
        const string MATCHMAKING = "Menu_Screen_Lobby(Clone)/ForegroundCanvas/Prefab_UI_Lobby/UI_Matchmaking_Prime";
        const string MINE = "Prefab_UI_Lobby/UI_Private_Lobby_Container/SafeArea/";
        const string V2 = MINE + "PrivateLobbiesV2/";
        const string LIST = "ShowsScrollList/ShowsListViewport/ShowsListContent";
        const int START = 15, TOGGLE_CODE = 84, PLAYERS = 95, SHOWS = 96;

        sealed class Rows
        {
            public readonly List<Transform> Mine = new List<Transform>();
            public List<Transform> Src = new List<Transform>();
            public int Cur = -1;
            public string Sig;
        }

        static UIStyle _style;
        static PrivateLobbyScreenViewModel _vm;
        static Transform _root, _plist, _ssel, _mm;
        static RawImage _cardArt, _infoArt;
        static readonly Rows _players = new Rows(), _shows = new Rows(), _tabs = new Rows();
        static readonly List<Transform> _hidden = new List<Transform>();
        static IntPtr _arrowsFor;

        static TMP_Text T(Transform t, string path) => t.Find(path).GetComponent<TMP_Text>();
        static string Loc(string key) => CMSLoader.Instance._localisedStrings.GetString(key);

        public static void Screen(UIStyle style, Component screen)
        {
            var vm = screen.TryCast<PrivateLobbyScreenViewModel>();
            if (vm == null || (_root != null && _vm != null && _vm.Pointer == vm.Pointer)) return;
            Teardown();
            _style = style;
            _vm = vm;
            var root = StyleLayout.Build(style.Assets, "screen_S45Lobby_ForegroundCanvas", vm.transform).transform;
            root.name = HOST;
            S1Kit.Stretch(root);
            root.localScale = Vector3.one;
            _root = root;
            root.Find("Prefab_UI_Lobby/SafeArea/Prefab_UI_MobileBackButton").gameObject.SetActive(false);
            root.Find("Prefab_UI_Lobby/SafeArea/SubMenuNavigation").gameObject.SetActive(false);
            root.Find(MINE + "HostName").gameObject.SetActive(false);
            root.Find(V2 + "Prefab_UI_LobbyHostOptions").gameObject.SetActive(false);

            var stock = vm.transform.Find("SafeArea");
            _cardArt = Art(root.Find(V2 + "ShowSelection/ShowCard/ShowSelectorImageMask/ShowSelectorImage"));
            Glyph(root.Find(MINE + "ButtonStart/ButtonInputStart"), stock.Find("BottomRightGroup/StartGroup"), START);
            Glyph(root.Find(MINE + "LobbyCode/PrivateLobbyToggleCode/SubMenuNavigationToggleCodeIcon"), stock.Find("LobbyCodeGroup"), TOGGLE_CODE);
            Glyph(root.Find(V2 + "ShowSelection/ButtonShowSelector/ButtonInput"), stock.Find("BottomRightGroup/ShowSelectorGroup/ShowSelectorButton"), SHOWS);
            Glyph(root.Find(V2 + "ButtonPlayersList/ButtonInput"), stock.Find("PlayersButton"), PLAYERS);
            On(root.Find(MINE + "ButtonStart"), () => { if (_vm.CanStart) _vm.OnStartButtonPressed(); });
            S1Kit.Listen(root.Find(MINE + "LobbyCode/PrivateLobbyToggleCode").gameObject, EventTriggerType.PointerClick, () => _vm.OnShowCodePressed());
            On(root.Find(V2 + "ShowSelection/ButtonShowSelector"), () => _vm.OpenShowSelector());
            S1Kit.Listen(root.Find(V2 + "ShowSelection/ShowCard").gameObject, EventTriggerType.PointerClick, () => _vm.OpenShowSelector());
            On(root.Find(V2 + "ButtonPlayersList"), () => _vm.OpenPlayerList());

            _plist = root.Find(V2 + "Prefab_UI_PrivateLobbyPlayerList");
            _plist.Find("Background/SubMenuNavigation").gameObject.SetActive(false);
            _plist.gameObject.SetActive(false);

            _ssel = root.Find(V2 + "Prefab_UI_PrivateLobbyShowSelect");
            _ssel.Find("SubMenuNavigation").gameObject.SetActive(false);
            _infoArt = Art(_ssel.Find("Container/ShowInfo/ShowImage/ShowSelectorImageMask/ShowSelectorImage"));
            _arrowsFor = IntPtr.Zero;
            _ssel.gameObject.SetActive(false);

            Hide(stock);
            root.gameObject.AddComponent<S1PrivateLobbyDriver>();
            root.gameObject.SetActive(true);
            Sync();
            Plugin.Log.LogInfo("season 4.5 private lobby over the modern one");
        }

        public static void Teardown()
        {
            foreach (var t in _hidden) StyledUI.Hide(t, false);
            _hidden.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = _plist = _ssel = null;
            _vm = null;
            _players.Sig = _shows.Sig = _tabs.Sig = null;
        }

        public static void Suspend()
        {
            if (_mm != null) StyledUI.Hide(_mm.Find("SafeArea"), false);
        }

        static Transform Matchmaking()
        {
            if (_mm != null) return _mm;
            var go = GameObject.Find(MATCHMAKING);
            if (go != null) _mm = go.transform;
            return _mm;
        }

        static void Hide(Transform t)
        {
            StyledUI.Hide(t, true);
            if (!_hidden.Contains(t)) _hidden.Add(t);
        }

        static RawImage Art(Transform img)
        {
            img.GetComponent<Image>().enabled = false;
            var go = new GameObject("Art");
            go.transform.SetParent(img, false);
            go.AddComponent<RectTransform>();
            S1Kit.Stretch(go.transform);
            var raw = go.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var fit = go.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = 16f / 9f;
            return raw;
        }

        static void Glyph(Transform mine, Transform stock, int action)
        {
            StyleGlyphs.Bind(mine.GetComponent<Image>(), StyleGlyphs.Live(stock));
            mine.gameObject.SetActive(false);
            mine.GetComponent<ActiveControllerUI>().RewiredAction = action;
            mine.gameObject.SetActive(true);
        }

        static void On(Transform t, Action a) =>
            t.GetComponent<Button>().onClick.AddListener(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(a));

        static TweenOnToggleSubMenu Row(Transform row)
        {
            var tw = row.GetComponent<TweenOnToggleSubMenu>();
            tw._activeToggleCanvasGroup = row.Find("HighlightButtonImage").GetComponent<CanvasGroup>();
            tw._inactiveToggleCanvasGroup = row.Find("ButtonImage").GetComponent<CanvasGroup>();
            tw._activeToggleCanvasGroup.alpha = 0f;
            tw._inactiveToggleCanvasGroup.alpha = 1f;
            tw._playSFXOnSelect = false;
            if (tw._currentTweeners == null) tw._currentTweeners = new Il2CppSystem.Collections.Generic.List<DG.Tweening.Tweener>();
            tw._activeToggleCanvasGroup.transform.localScale = tw._downScale;
            return tw;
        }

        static void Highlight(Rows rows, int at)
        {
            if (at == rows.Cur) return;
            if (rows.Cur >= 0 && rows.Cur < rows.Mine.Count) rows.Mine[rows.Cur].GetComponent<TweenOnToggleSubMenu>().StartTweenOff();
            if (at >= 0 && at < rows.Mine.Count) rows.Mine[at].GetComponent<TweenOnToggleSubMenu>().StartTweenOn();
            rows.Cur = at;
        }

        static void Clear(Transform list, Rows rows)
        {
            for (int i = list.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(list.GetChild(i).gameObject);
            rows.Mine.Clear();
            rows.Cur = -1;
        }

        public static void Sync()
        {
            if (_root == null || _vm == null) return;
            var stock = _vm.transform.Find("SafeArea");
            var mine = _root.Find(MINE.TrimEnd('/'));
            var top = _root.Find("Prefab_UI_Lobby/SafeArea");
            bool host = stock.Find("BottomRightGroup").gameObject.activeSelf;

            var code = mine.Find("LobbyCode");
            code.gameObject.SetActive(stock.Find("LobbyCodeGroup").gameObject.activeSelf);
            T(code, "ShownCode").text = _vm.Code;
            code.Find("ShownCode").gameObject.SetActive(_vm.ShowCode);
            code.Find("HiddenCode").gameObject.SetActive(!_vm.ShowCode);

            var stockShow = stock.Find("BottomRightGroup/ShowSelectorGroup/Generic_UI_PrivateLobbyShow_Prefab");
            bool hasShow = stockShow.gameObject.activeSelf;
            var sel = mine.Find("PrivateLobbiesV2/ShowSelection");
            sel.gameObject.SetActive(host && stock.Find("BottomRightGroup/ShowSelectorGroup").gameObject.activeSelf);
            sel.Find("ShowCard").gameObject.SetActive(hasShow);
            var title = T(stockShow, "ShowTitle").text;
            T(sel, "ShowCard/ShowSelectorHeader/ShowSelectorNameText").text = title.ToUpperInvariant();
            top.Find("ShowNameText").gameObject.SetActive(hasShow);
            T(top, "ShowNameText").text = title;
            var shot = stockShow.Find("ShowScreenshot_Mask/ShowScreenshot").GetComponent<RawImage>();
            _cardArt.texture = shot.texture;
            _cardArt.uvRect = shot.uvRect;

            var start = mine.Find("ButtonStart");
            start.gameObject.SetActive(stock.Find("BottomRightGroup/StartGroup").gameObject.activeSelf);
            T(start, "ButtonTextStart").text = _vm.CanStart ? Loc("private_lobbies_start_show") : _vm.CannotStartButtonMessage;
            start.GetComponent<CanvasGroup>().alpha = _vm.CanStart ? 1f : 0.5f;
            start.Find("ButtonInputStart").gameObject.SetActive(_vm.CanStart);
            mine.Find("PrivateLobbiesV2/ButtonPlayersList").gameObject.SetActive(stock.Find("PlayersButton").gameObject.activeSelf);

            var hostName = HostName();
            mine.Find("HostName").gameObject.SetActive(!host && hostName != null);
            if (!host && hostName != null) T(mine, "HostName").text = string.Format(Loc("private_lobbies_host_name"), hostName);

            var mm = Matchmaking();
            if (mm != null)
            {
                StyledUI.Hide(mm.Find("SafeArea"), true);
                var found = mm.Find("SafeArea/PlayersFound");
                Mirror(top, "TMPro_CurrentPlayersDynText", found, "PlayerNumText");
                Mirror(top, "TMPro_LobbyPhaseText", found, "PlayersFoundText");
                Mirror(top, "TMPro_LobbyStatusMessageText", found, "TimeToStartText");
                var load = mm.Find("SafeArea/Loading");
                top.Find("TMPro_WaitingForPlayersText").gameObject.SetActive(load.gameObject.activeInHierarchy);
                T(top, "TMPro_WaitingForPlayersText").text = T(load, "LoadingText").text;
            }

            Players();
            Shows();

            if (!_vm._isInFocus) return;
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.transform.IsChildOf(_vm.transform)) es.SetSelectedGameObject(null);
            var input = _vm.RewiredPlayer;
            if (input.GetButtonDown(PLAYERS)) _vm.OpenPlayerList();
            else if (host && input.GetButtonDown(SHOWS)) _vm.OpenShowSelector();
            else if (host && _vm.CanStart && input.GetButtonDown(START) && !(Tweaks.LobbyCustomiserTweak.Instance?.S1Picked ?? false)) _vm.OnStartButtonPressed();
        }

        static void Mirror(Transform top, string mine, Transform found, string theirs)
        {
            var src = found.Find(theirs);
            top.Find(mine).gameObject.SetActive(found.gameObject.activeInHierarchy && src.gameObject.activeSelf);
            T(top, mine).text = src.GetComponent<TMP_Text>().text;
        }

        static string HostName()
        {
            var list = _vm._privateLobbyPlayerListViewModel;
            if (list == null) return null;
            var content = list._playersParentTransform;
            for (int i = 0; i < content.childCount; i++)
            {
                var e = content.GetChild(i).GetComponent<PrivateLobbyPlayerListEntryViewModel>();
                if (e != null && content.GetChild(i).gameObject.activeSelf && e.IsHost) return Plain(e.PlayerName);
            }
            return null;
        }

        static string Plain(string s) => Regex.Replace(s, "<[^>]+>", "").Trim();

        static void Players()
        {
            var list = _vm._privateLobbyPlayerListViewModel;
            bool open = list != null && list.gameObject.activeInHierarchy;
            if (_plist.gameObject.activeSelf != open) _plist.gameObject.SetActive(open);
            if (!open) { _players.Sig = null; return; }
            Hide(list.transform.Find("SafeArea"));
            Hide(list.transform.Find("Background"));
            var data = list.PlayerList;
            var cached = list._cachedPlayersGameObjectList;
            var src = new List<Transform>();
            var keys = new List<string>();
            var sig = new StringBuilder();
            for (int i = 0; data != null && cached != null && i < data.Count && i < cached.Count; i++)
            {
                var c = cached[i].transform;
                if (!c.gameObject.activeSelf) continue;
                var e = c.GetComponent<PrivateLobbyPlayerListEntryViewModel>();
                src.Add(c);
                keys.Add(data[i].PlayerKey);
                sig.Append(e.PlayerName).Append(e.IsHost).Append(T(c, "Panel/Content/PlayerName").text).Append('|');
            }
            if (sig.ToString() != _players.Sig)
            {
                _players.Sig = sig.ToString();
                var dst = _plist.Find("Container/PlayerScrollList/PlayerListViewport/PlayerListContent");
                Clear(dst, _players);
                _players.Src = src;
                for (int i = 0; i < src.Count; i++)
                {
                    var e = src[i].GetComponent<PrivateLobbyPlayerListEntryViewModel>();
                    var row = StyleLayout.Build(_style.Assets, "PrivateLobbyPlayerListEntry", dst).transform;
                    row.gameObject.SetActive(true);
                    var name = row.Find("PlayerNameContainer/PlayerNameText").GetComponent<TextMeshProUGUI>();
                    name.text = Plain(e.PlayerName);
                    Features.MorePlatformIcon.FeatureMorePlatformIcon.ApplyPrivateLobbyPlatform(name, keys[i]);
                    Features.MorePlatformIcon.FeatureMorePlatformIcon.ApplyPrivateLobbyCustomName(name, keys[i]);
                    row.Find("PlayerNameContainer/Icon").gameObject.SetActive(e.IsHost);
                    Row(row);
                    int idx = i;
                    On(row.Find("Button"), () => { if (_players.Cur == idx) e.OnClicked(); else list._inputHandler.SelectByIndex(idx); });
                    _players.Mine.Add(row);
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(dst.GetComponent<RectTransform>());
            }
            Highlight(_players, list._inputHandler.CurrentIndex);
        }

        static void Shows()
        {
            var list = _vm._privateLobbyShowListViewModel;
            bool open = list != null && list.gameObject.activeInHierarchy;
            if (_ssel.gameObject.activeSelf != open) _ssel.gameObject.SetActive(open);
            if (!open) { _shows.Sig = _tabs.Sig = null; return; }
            var m = list.transform;
            if (_arrowsFor != list.Pointer)
            {
                _arrowsFor = list.Pointer;
                foreach (var side in new[] { "TabCarrouselNavigationLeft", "TabCarrouselNavigationRight" })
                {
                    var mine = _ssel.Find("TabCarrouselShows/" + side);
                    var theirs = m.Find("Container/TabCarrouselShows/" + side);
                    StyleGlyphs.Bind(mine.GetComponent<Image>(), StyleGlyphs.Live(theirs));
                    S1Kit.Listen(mine.gameObject, EventTriggerType.PointerClick, () => theirs.GetComponent<Button>().onClick.Invoke());
                }
            }
            Hide(m.Find("Generic_UI_Scrim_Prefab"));
            foreach (var n in new[] { "ShowsScrollList", "Title", "ShowInfo", "MissingInfo_Text", "TabCarrouselShows" }) Hide(m.Find("Container/" + n));
            var panel = m.Find("Container").GetComponent<Image>();
            if (panel != null) panel.enabled = false;

            bool code = list.IsShowingEnterCodePanel;
            T(_ssel, "Container/Title/Text").text = code ? T(m, "Container/Title/EnterCodeText").text : T(m, "Container/Title/ShowInfoText").text;
            var info = _ssel.Find("Container/ShowInfo");
            info.gameObject.SetActive(!code && list.IsShowingShowInfoPanel);
            _ssel.Find("Container/MissingInfo_Text").gameObject.SetActive(!code && list.IsShowingMissingFavouritesPanel);
            if (info.gameObject.activeSelf) Info(m, info);
            Tabs(list);
            Entries(list);
            _ssel.Find("Container/ShowsScrollList").GetComponent<ScrollRect>().verticalNormalizedPosition = m.Find("Container/ShowsScrollList").GetComponent<ScrollRect>().verticalNormalizedPosition;
        }

        static void Info(Transform m, Transform info)
        {
            var details = m.Find("Container/ShowInfo/ShowDetailsLayout");
            T(info, "ShowImage/ShowSelectorHeader/ShowSelectorNameText").text = T(details, "Title_Text").text;
            T(info, "Description").text = T(details, "Description").text;
            var src = m.Find("Container/ShowInfo/ShowImage/ShowSelectorImageMask/ShowSelectorImage").GetComponent<RawImage>();
            _infoArt.texture = src.texture;
            _infoArt.uvRect = src.uvRect;
            string req = null;
            var tags = details.Find("ShowTagsFirstRow");
            for (int i = 0; i < tags.childCount && req == null; i++)
            {
                var tag = tags.GetChild(i);
                if (tag.gameObject.activeSelf && tag.Find("Tag_Icon_Content/Tag_PlayerRequirement").gameObject.activeInHierarchy) req = T(tag, "Tag_Text").text;
            }
            info.Find("PlayersRequirement").gameObject.SetActive(req != null);
            if (req != null) T(info, "PlayersRequirement/Icon/PlayerRequirementText").text = req;
        }

        static void Tabs(PrivateLobbyShowListViewModel list)
        {
            var theirs = list._tabsParentTransform;
            var sig = new StringBuilder();
            for (int i = 0; i < theirs.childCount; i++) sig.Append(T(theirs.GetChild(i), "PrivateLobbyShowTab/Content/SubMenuTabText").text).Append('|');
            var dst = _ssel.Find("TabCarrouselShows/TabsContent");
            if (sig.ToString() != _tabs.Sig)
            {
                _tabs.Sig = sig.ToString();
                Clear(dst, _tabs);
                var group = dst.GetComponent<ToggleGroup>();
                for (int i = 0; i < theirs.childCount; i++)
                {
                    var src = theirs.GetChild(i).Find("PrivateLobbyShowTab");
                    var tab = StyleLayout.Build(_style.Assets, "PrivateLobbyShowTab", dst).transform;
                    tab.gameObject.SetActive(true);
                    tab.Find("PrivateLobbyShowTabTop").gameObject.SetActive(false);
                    var bottom = tab.Find("PrivateLobbyShowTabBottom");
                    T(bottom, "SubMenuTabText").text = T(src, "Content/SubMenuTabText").text;
                    bottom.Find("SubMenuFavouriteIcon").gameObject.SetActive(src.Find("Content/SubMenuFavouriteIcon").gameObject.activeSelf);
                    bottom.GetComponent<Toggle>().group = group;
                    S1Kit.Listen(bottom.gameObject, EventTriggerType.PointerClick, () => S1Kit.Click(src.gameObject));
                    _tabs.Mine.Add(tab);
                }
            }
            for (int i = 0; i < _tabs.Mine.Count && i < theirs.childCount; i++)
            {
                bool on = theirs.GetChild(i).Find("PrivateLobbyShowTab").GetComponent<Toggle>().isOn;
                var tg = _tabs.Mine[i].Find("PrivateLobbyShowTabBottom").GetComponent<Toggle>();
                if (tg.isOn != on) tg.isOn = on;
            }
        }

        static void Entries(PrivateLobbyShowListViewModel list)
        {
            var theirs = list.transform.Find("Container/" + LIST);
            var src = new List<Transform>();
            var sig = new StringBuilder();
            for (int i = 0; i < theirs.childCount; i++)
            {
                var c = theirs.GetChild(i);
                if (!c.gameObject.activeSelf || c.name == "Tooltip") continue;
                src.Add(c);
                sig.Append(c.name == "EnterCodeButton" ? T(c, "Content/Text").text : T(c, "ShowNameText").text).Append('|');
            }
            var dst = _ssel.Find("Container/" + LIST);
            if (sig.ToString() != _shows.Sig)
            {
                _shows.Sig = sig.ToString();
                Clear(dst, _shows);
                _shows.Src = src;
                foreach (var c in src)
                {
                    bool enter = c.name == "EnterCodeButton";
                    var row = StyleLayout.Build(_style.Assets, "PrivateLobbyShowEntry", dst).transform;
                    row.gameObject.SetActive(true);
                    T(row, "ShowNameText").text = enter ? T(c, "Content/Text").text : T(c, "ShowNameText").text;
                    Row(row);
                    var e = c.GetComponent<PrivateLobbyShowListEntryViewModel>();
                    if (enter) On(row.Find("Button"), () => S1Kit.Click(c.gameObject));
                    else On(row.Find("Button"), () => e.OnClicked());
                    _shows.Mine.Add(row);
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(dst.GetComponent<RectTransform>());
            }
            int at = -1;
            for (int i = 0; i < _shows.Src.Count; i++)
            {
                var c = _shows.Src[i];
                var e = c.GetComponent<PrivateLobbyShowListEntryViewModel>();
                bool fav = e != null && e.IsFavourite;
                var icon = _shows.Mine[i].Find("SubMenuFavouriteIcon").gameObject;
                if (icon.activeSelf != fav) icon.SetActive(fav);
                if (at < 0 && (e != null ? e.Selected : c.Find("Panel_Selected").GetComponent<CanvasGroup>().alpha > 0.5f)) at = i;
            }
            Highlight(_shows, at);
        }
    }

    public class S1PrivateLobbyDriver : MonoBehaviour
    {
        public S1PrivateLobbyDriver(IntPtr ptr) : base(ptr) { }

        void Update() => S1PrivateLobby.Sync();

        void OnDisable() => S1PrivateLobby.Suspend();
    }
}
