using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BetterFG.Services;
using BetterFG.Tweaks;
using FGClient;
using FGClient.UI;
using FGClient.UI.PrivateLobby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.TextChat
{
    internal static class LobbySuggestions
    {
        private const float RowHeight = 52f;
        private const float RowGap = 6f;
        private const float Width = 400f;
        private const float CoverWidth = 76f;
        private const float CoverHeight = 43f;
        private const float SendGap = 3f;
        private const int MaxRows = 6;
        private static readonly Regex ShareCode = new(@"^\d{4}-\d{4}-\d{4}$");
        private static readonly Regex SuggestId = new(@"^[A-Za-z0-9_-]{1,64}$");
        private static readonly Color32[] Palette =
        {
            new(255, 79, 176, 255), new(255, 194, 61, 255), new(91, 227, 125, 255),
            new(169, 139, 255, 255), new(255, 138, 76, 255), new(79, 210, 255, 255),
        };

        private class Row
        {
            public string Code;
            public string By;
            public int Color;
            public float Born;
            public float Died = -1f;
            public float NextFetch;
            public int Tries;
            public bool Titled;
            public RectTransform Rt;
            public Button Btn;
            public CanvasGroup Group;
            public RawImage Cover;
            public FG.Common.UI.DlcImageViewModel ShowImage;
            public TextMeshProUGUI Title;
        }

        private static readonly List<Row> _rows = new();
        private static readonly Dictionary<string, string> _knownTitles = new();
        private static readonly Dictionary<string, (string name, int color)> _members = new();
        private static readonly HashSet<string> _present = new();
        private static readonly List<(string room, string from, string code, float until)> _unresolved = new();
        private static bool _hostListening;
        private static PrivateLobbyScreenViewModel _vm;
        private static PrivateLobbyShowListViewModel _list;
        private static RectTransform _feed;
        private static RectTransform _header;
        private static GameObject _suggest;
        private static Transform _showButton;
        private static Sprite _rounded;
        private static TextMeshProUGUI _fontSrc;
        private static Material _outlined;
        private static string _lobbyId;
        private static string _membersRoom;
        private static string _pendingView;
        private static string _pendingShow;
        private static int _pendingTab;
        private static float _pendingUntil;
        private static float _nextSend;
        private static float _nextLobbyCheck;
        private static bool _shown;
        private static bool _dirty;
        private static NavigableMenuInputHandler _input;
        private static UICustomToggle _players;
        private static Selectable _playersUp;

        internal static void OnLobbyFocus(PrivateLobbyScreenViewModel vm)
        {
            _vm = vm;
            _dirty = true;
        }

        internal static void OnShowListAwake(PrivateLobbyShowListViewModel vm) => _list = vm;

        internal static void ResetPresence() => _present.Clear();

        internal static void OnHere(string from, bool on)
        {
            if (from == null || from.Length != 16) return;
            if (on) _present.Add(from);
            else _present.Remove(from);
        }

        internal static void Tick()
        {
            if (_vm == null) return;
            float now = Time.unscaledTime;
            if (now >= _nextLobbyCheck)
            {
                _nextLobbyCheck = now + 0.5f;
                _dirty = true;
                string id =PrivateLobbyUtils.IsInPrivateLobby() ? PrivateLobbyUtils.GetPrivateLobbyId() : null;
                if (id != _lobbyId)
                {
                    _lobbyId = id;
                    Clear();
                }
                if (_unresolved.Count > 0)
                {
                    var retry = _unresolved.ToArray();
                    _unresolved.Clear();
                    foreach (var u in retry)
                        if (now < u.until && u.room == FeatureTextChat.LobbyRoom) OnRemote(u.room, u.from, u.code);
                }
                string room = FeatureTextChat.LobbyRoom;
                string host = id != null ? PrivateLobbyUtils.GetPrivateLobbyHostId() : null;
                _hostListening = room != null && !string.IsNullOrEmpty(host) && _present.Contains(FeatureTextChat.ChatId(room, host));
            }

            ApplyPendingView(now);
            bool want = _lobbyId != null && FeatureTextChat.Enabled && FeatureTextChat.LobbyRequests && _vm.isActiveAndEnabled && (_vm.IsHost || _hostListening);
            if (want && _feed == null) Build();
            if (want != _shown && _feed != null)
            {
                _shown = want;
                _feed.gameObject.SetActive(want);
                if (want) Place();
                else if (_players != null) Link(_players, 1, _playersUp);
                _dirty = true;
            }
            if (_suggest != null)
            {
                bool joiner = want && !_vm.IsHost;
                if (_suggest.activeSelf != joiner)
                {
                    _suggest.SetActive(joiner);
                    _dirty = true;
                }
            }
            if (!want) return;
            Animate(now);
            if (_dirty) Relink();
        }

        private static void Relink()
        {
            _dirty = false;
            var alive = new List<Row>();
            foreach (var r in _rows)
                if (r.Died < 0f && r.Btn != null) alive.Add(r);

            var safe = _vm.transform.Find("SafeArea");
            var interfaceButton = safe.Find("BFG_CustomiseGroup/BFG_Customise_2")?.GetComponent<UICustomToggle>();
            Selectable right = _vm.IsHost ? _showButton.GetComponent<Selectable>() : _suggest.GetComponent<Selectable>();

            for (int i = 0; i < alive.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnUp = i + 1 < alive.Count ? alive[i + 1].Btn : null;
                nav.selectOnDown = i == 0 ? _players : alive[i - 1].Btn;
                nav.selectOnRight = right;
                alive[i].Btn.navigation = nav;
            }

            var current = _players.navigation.selectOnUp;
            if (current == null || current.transform.parent != _feed) _playersUp = current;
            Link(_players, 1, alive.Count > 0 ? alive[0].Btn : _playersUp);
            if (_vm.IsHost) return;
            var suggest = _suggest.GetComponent<UICustomToggle>();
            Link(suggest, 1, interfaceButton);
            Link(suggest, 2, null);
            Link(suggest, 3, alive.Count > 0 ? alive[0].Btn : _players);
            Link(suggest, 4, null);
            if (interfaceButton != null) Link(interfaceButton, 2, suggest);
        }

        private static void Link(UICustomToggle t, int dir, Selectable target)
        {
            var nav = t.navigation;
            nav.mode = Navigation.Mode.Explicit;
            if (dir == 1) nav.selectOnUp = target;
            else if (dir == 2) nav.selectOnDown = target;
            else if (dir == 3) nav.selectOnLeft = target;
            else nav.selectOnRight = target;
            t.navigation = nav;

            var mode = target == null ? Navigation.Mode.None : Navigation.Mode.Explicit;
            bool cached = _input != null && _input._cachedNavModes.ContainsKey(t);
            if (cached) _input._cachedNavModes[t][dir] = mode;
            if (cached && !_input._inFocus) return;
            if (dir == 1) t._navigationModeUp = mode;
            else if (dir == 2) t._navigationModeDown = mode;
            else if (dir == 3) t._navigationModeLeft = mode;
            else t._navigationModeRight = mode;
        }

        internal static bool TrySuggest(FragglePlobbiesManager mgr)
        {
            if (_feed == null || _vm == null || _vm.IsHost || _lobbyId == null || !FeatureTextChat.Enabled || !FeatureTextChat.LobbyRequests || !_hostListening) return false;

            var show = mgr.CurrentHighlightedShow;
            string code = show?.ShowId;
            if (code != null && SuggestId.IsMatch(code) && Time.unscaledTime >= _nextSend)
            {
                _nextSend = Time.unscaledTime + SendGap;
                if (!string.IsNullOrEmpty(show.ShowName)) _knownTitles[code] = show.ShowName;
                if (FeatureTextChat.SendSuggestion(code)) Plugin.Log.LogInfo($"suggested {code} to the lobby");
                else
                {
                    var me = Own();
                    Add(code, me.name, me.color);
                    Plugin.Log.LogInfo($"suggested {code}, relay not joined so it stays local");
                }
            }
            if (_list != null) _list.OnBackPressed();
            return true;
        }

        internal static void OnRemote(string room, string from, string code)
        {
            if (code == null || from == null || !SuggestId.IsMatch(code) || from.Length != 16) return;
            if (!ShareCode.IsMatch(code))
            {
                var show = FindShow(code, out _);
                if (show == null)
                {
                    if (_unresolved.Count < MaxRows) _unresolved.Add((room, from, code, Time.unscaledTime + 30f));
                    return;
                }
                _knownTitles[code] = show.ShowName;
            }
            if (room != _membersRoom)
            {
                _membersRoom = room;
                _members.Clear();
            }
            if (!_members.TryGetValue(from, out var who))
            {
                IndexMembers(room);
                if (!_members.TryGetValue(from, out who)) return;
            }
            foreach (var r in _rows)
                if (r.Died < 0f && r.Code == code && r.By == who.name) return;
            Add(code, who.name, who.color);
        }

        private static PlayerLobbiesData FindShow(string id, out int tabIndex)
        {
            tabIndex = -1;
            var mgr = MPG.Utility.SingletonBehaviour<FragglePlobbiesManager>.Instance;
            var tabs = mgr?._tabsData;
            if (tabs == null) return null;
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i].IsFraggle || !mgr._showDictionary.TryGetValue(tabs[i].Id, out var list)) continue;
                int guard = 0;
                for (var node = list.First; node != null && guard++ < 500; node = node.Next)
                {
                    if (node.Value?.ShowId != id || string.IsNullOrEmpty(node.Value.ShowName)) continue;
                    tabIndex = i;
                    return node.Value;
                }
            }
            return null;
        }

        private static void IndexMembers(string room)
        {
            var members = PrivateLobbyUtils.GetCurrentPrivateLobby()?.Members;
            if (members == null) return;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                _members[FeatureTextChat.ChatId(room, m.Id)] = (NameOf(m), ColorOf(m.Id));
            }
        }

        private static (string name, int color) Own()
        {
            string id = FeatureTextChat.ProfileId;
            var members = PrivateLobbyUtils.GetCurrentPrivateLobby()?.Members;
            if (members != null)
                for (int i = 0; i < members.Count; i++)
                    if (members[i].Id == id) return (NameOf(members[i]), ColorOf(id));
            return ("", ColorOf(id ?? ""));
        }

        private static string NameOf(FallGuys.Lobby.Protocol.Client.Lobbies.LobbyMemberDto m)
        {
            string name = m.ExternalAccountName;
            if (string.IsNullOrEmpty(name)) name = m.InternalAccountName?.PlayerName ?? "";
            if (m.Id == FeatureTextChat.ProfileId) return string.IsNullOrEmpty(Core.LocalPlayerInfo.CustomName) ? name : Core.LocalPlayerInfo.CustomName;
#if PROFILES
            string custom = Customization.Profiles.ProfileService.GetRemoteProfileForName(FallGuysLib.Players.PlayerUtils.CleanPlayerName(name))?.nametag?.customName;
            if (!string.IsNullOrEmpty(custom)) return custom;
#endif
            return name;
        }

        private static int ColorOf(string id)
        {
            uint h = 2166136261;
            foreach (char c in id) h = (h ^ c) * 16777619;
            return (int)(h % (uint)Palette.Length);
        }

        private static void Add(string code, string by, int color)
        {
            var row = new Row { Code = code, By = by, Color = color, Born = Time.unscaledTime };
            _rows.Insert(0, row);
            int alive = 0;
            foreach (var r in _rows)
                if (r.Died < 0f && ++alive > MaxRows) Retire(r);
            if (_feed != null) MakeRow(row);
        }

        private static void Retire(Row r)
        {
            r.Died = Time.unscaledTime;
            _dirty = true;
            if (r.Btn == null) return;
            r.Btn.interactable = false;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null || es.currentSelectedGameObject != r.Rt.gameObject) return;
            Row next = null;
            foreach (var o in _rows)
                if (o.Died < 0f && o.Btn != null) next = o;
            es.SetSelectedGameObject(next != null ? next.Rt.gameObject : _players.gameObject);
        }

        private static void Clear()
        {
            foreach (var r in _rows)
                if (r.Rt != null) UnityEngine.Object.Destroy(r.Rt.gameObject);
            _rows.Clear();
            _members.Clear();
            _membersRoom = null;
            _unresolved.Clear();
            _pendingView = _pendingShow = null;
        }

        private static void View(string code)
        {
            if (!ShareCode.IsMatch(code))
            {
                var show = FindShow(code, out int tab);
                if (show == null) return;
                _pendingShow = show.ShowName;
                _pendingTab = tab;
                _pendingUntil = Time.unscaledTime + 5f;
                if (_list == null || !_list.isActiveAndEnabled) _vm.OpenShowSelector();
                return;
            }
            if (_list != null && _list.isActiveAndEnabled)
            {
                _list.OnCodeInputButtonPressed(code);
                return;
            }
            _pendingView = code;
            _pendingUntil = Time.unscaledTime + 5f;
            _vm.OpenShowSelector();
        }

        private static void ApplyPendingView(float now)
        {
            if (_pendingView == null && _pendingShow == null) return;
            if (now > _pendingUntil)
            {
                _pendingView = _pendingShow = null;
                return;
            }
            if (_list == null || !_list.isActiveAndEnabled || !_list._isInFocus) return;
            if (_pendingView != null)
            {
                _list.OnCodeInputButtonPressed(_pendingView);
                _pendingView = null;
                return;
            }

            var mgr = _list._dataManager;
            string tabId = mgr._tabsData[_pendingTab].Id;
            if (mgr.CurrentTab?.Id != tabId)
            {
                mgr.SelectTab(_pendingTab, false);
                return;
            }
            var content = _list._showsParentTransform;
            for (int i = 0; i < content.childCount; i++)
            {
                var entry = content.GetChild(i).GetComponent<PrivateLobbyShowListEntryViewModel>();
                if (entry == null || !entry.gameObject.activeInHierarchy || entry.ShowName != _pendingShow) continue;
                var toggle = entry.GetComponent<Toggle>();
                if (toggle != null) toggle.isOn = true;
                else entry.OnClicked();
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(entry.gameObject);
                _pendingShow = null;
                return;
            }
        }

        private static void Build()
        {
            var safe = _vm.transform.Find("SafeArea");
            var players = safe.Find("PlayersButton");
            _rounded = players.Find("Panel_Unselected/Panel_Fill").GetComponent<Image>().sprite;
            _fontSrc = players.Find("Content/Text").GetComponent<TextMeshProUGUI>();
            _players = players.GetComponent<UICustomToggle>();
            _input = _vm.GetComponent<NavigableMenuInputHandler>();
            _playersUp = null;

            _feed = NewRect("BFG_SuggestFeed", safe);
            _feed.anchorMin = _feed.anchorMax = Vector2.zero;
            _feed.pivot = Vector2.zero;
            _feed.sizeDelta = new Vector2(Width, MaxRows * (RowHeight + RowGap));

            var header = Label(_feed, LocalizationService.Get("text_chat.suggest.title"), 26f, TextAlignmentOptions.BottomLeft);
            header.fontSharedMaterial = _fontSrc.fontSharedMaterial;
            _header = header.rectTransform;
            _header.anchorMin = _header.anchorMax = Vector2.zero;
            _header.pivot = Vector2.zero;
            _header.sizeDelta = new Vector2(Width, 34f);
            _header.anchoredPosition = new Vector2(2f, 2f);

            _showButton = safe.Find("BottomRightGroup/ShowSelectorGroup/ShowSelectorButton") ?? safe.Find("ShowSelectorGroup/ShowSelectorButton");
            _suggest = UnityEngine.Object.Instantiate(_showButton.gameObject, safe, true);
            _suggest.name = "BFG_SuggestButton";
            var text = _suggest.transform.Find("Content/Text");
            text.GetComponent("LocalisedStaticLabel").TryCast<Behaviour>().enabled = false;
            BettrFG.uGUI.UGUIShip.RelabelText(text.GetComponent<TextMeshProUGUI>(), "text_chat.suggest.button");
            _suggest.SetActive(false);

            foreach (var r in _rows)
                if (r.Died < 0f) MakeRow(r);
            _shown = false;
        }

        private static void Place()
        {
            var p = _vm.transform.Find("SafeArea/PlayersButton").GetComponent<RectTransform>();
            _feed.anchoredPosition = new Vector2(p.anchoredPosition.x - p.rect.width * p.pivot.x, p.anchoredPosition.y + p.rect.height * (1f - p.pivot.y) + 17f);
            _suggest.transform.position = _showButton.position;
        }

        private static void MakeRow(Row r)
        {
            var rt = NewRect("BFG_Suggestion", _feed);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(Width, RowHeight);
            rt.anchoredPosition = new Vector2(-24f, 0f);
            var bg = Box(rt, Color.white, 2.4f);
            r.Group = rt.gameObject.AddComponent<CanvasGroup>();
            r.Group.alpha = 0f;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            var colors = btn.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.18f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.32f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0.42f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.45f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            string code = r.Code;
            btn.onClick.AddListener((Action)(() => View(code)));

            var mask = NewRect("Cover", rt);
            var maskImg = Box(mask, new Color(0f, 0f, 0f, 0.25f), 3.5f);
            maskImg.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            mask.anchorMin = mask.anchorMax = new Vector2(0f, 0.5f);
            mask.pivot = new Vector2(0f, 0.5f);
            mask.sizeDelta = new Vector2(CoverWidth, CoverHeight);
            mask.anchoredPosition = new Vector2(5f, 0f);
            var cover = NewRect("Image", mask).gameObject.AddComponent<RawImage>();
            cover.rectTransform.anchorMin = Vector2.zero;
            cover.rectTransform.anchorMax = Vector2.one;
            cover.rectTransform.offsetMin = cover.rectTransform.offsetMax = Vector2.zero;
            cover.color = new Color(1f, 1f, 1f, 0f);
            cover.raycastTarget = false;
            r.Cover = cover;
            if (!ShareCode.IsMatch(code))
            {
                var show = FindShow(code, out _);
                string imageId = show != null ? PlayerLobbiesDataExtension.ShowImageCMS(show) : null;
                if (!string.IsNullOrEmpty(imageId))
                {
                    r.ShowImage = cover.gameObject.AddComponent<FG.Common.UI.DlcImageViewModel>();
                    r.ShowImage.SetImage(imageId);
                }
            }

            float left = 5f + CoverWidth + 10f;
            r.Titled = _knownTitles.TryGetValue(code, out var known) && !string.IsNullOrEmpty(known);
            r.Title = Label(rt, r.Titled ? known : code, 19f, TextAlignmentOptions.BottomLeft);
            r.Title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            r.Title.rectTransform.anchorMax = Vector2.one;
            r.Title.rectTransform.offsetMin = new Vector2(left, 1f);
            r.Title.rectTransform.offsetMax = new Vector2(-10f, 0f);

            var dot = NewRect("Dot", rt);
            Box(dot, Palette[r.Color], 8f).raycastTarget = false;
            dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
            dot.pivot = new Vector2(0f, 0.5f);
            dot.sizeDelta = new Vector2(10f, 10f);
            dot.anchoredPosition = new Vector2(left + 1f, -12f);

            var by = Label(rt, r.By, 15f, TextAlignmentOptions.TopLeft);
            by.rectTransform.anchorMin = Vector2.zero;
            by.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            by.rectTransform.offsetMin = new Vector2(left + 17f, 2f);
            by.rectTransform.offsetMax = new Vector2(-10f, -3f);

            r.Rt = rt;
            r.Btn = btn;
            _dirty = true;
        }

        private static void Animate(float now)
        {
            float dt = Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-14f * dt);
            int slot = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                if (r.Died >= 0f)
                {
                    if (r.Rt == null)
                    {
                        _rows.RemoveAt(i--);
                        continue;
                    }
                    float t = (now - r.Died) / 0.25f;
                    r.Group.alpha = Mathf.Clamp01(1f - t);
                    r.Rt.anchoredPosition += new Vector2(0f, 40f * dt);
                    if (t >= 1f)
                    {
                        UnityEngine.Object.Destroy(r.Rt.gameObject);
                        _rows.RemoveAt(i--);
                    }
                    continue;
                }
                if (r.Rt == null) continue;

                Fetch(r, now);
                float target = slot++ * (RowHeight + RowGap);
                var pos = r.Rt.anchoredPosition;
                r.Rt.anchoredPosition = new Vector2(Mathf.Lerp(pos.x, 0f, k), Mathf.Lerp(pos.y, target, k));
                r.Group.alpha = Mathf.Clamp01((now - r.Born) / 0.2f);
                if (r.Cover.texture != null && r.Cover.color.a < 1f)
                    r.Cover.color = new Color(1f, 1f, 1f, Mathf.MoveTowards(r.Cover.color.a, 1f, dt * 5f));
            }
            if (_header.gameObject.activeSelf != slot > 0) _header.gameObject.SetActive(slot > 0);
            var hp = _header.anchoredPosition;
            _header.anchoredPosition = new Vector2(hp.x, Mathf.Lerp(hp.y, slot * (RowHeight + RowGap) + 2f, k));
        }

        private static void Fetch(Row r, float now)
        {
            if (r.ShowImage != null)
            {
                if (r.Cover.texture == null && r.ShowImage.Image != null) SetCover(r, r.ShowImage.Image);
                return;
            }
            if ((r.Cover.texture != null && r.Titled) || now < r.NextFetch || r.Tries >= 20 || !ShareCode.IsMatch(r.Code)) return;
            r.Tries++;
            r.NextFetch = now + 0.5f * r.Tries;

            if (r.Cover.texture == null)
            {
                var tex = ShowTileDislikesTweak.CoverFor(r.Code);
                if (tex != null) SetCover(r, tex);
            }
            if (!r.Titled)
            {
                string title = ShowTileDislikesTweak.TitleFor(r.Code);
                if (title != null)
                {
                    r.Title.text = title;
                    r.Titled = true;
                }
            }
        }

        private static void SetCover(Row r, Texture tex)
        {
            r.Cover.texture = tex;
            float aspect = (float)tex.width / tex.height;
            float box = CoverWidth / CoverHeight;
            r.Cover.uvRect = aspect > box
                ? new Rect((1f - box / aspect) / 2f, 0f, box / aspect, 1f)
                : new Rect(0f, (1f - aspect / box) / 2f, 1f, aspect / box);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rt = new GameObject(name).AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image Box(RectTransform rt, Color color, float ppu)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = _rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = ppu;
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI Label(Transform parent, string text, float size, TextAlignmentOptions align)
        {
            var t = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = _fontSrc.font;
            t.fontSharedMaterial = Outlined();
            t.fontStyle = _fontSrc.fontStyle;
            t.fontSize = size;
            t.color = Color.white;
            t.richText = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.alignment = align;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        private static Material Outlined()
        {
            if (_outlined != null) return _outlined;
            _outlined = new Material(_fontSrc.fontSharedMaterial);
            _outlined.EnableKeyword(ShaderUtilities.Keyword_Outline);
            _outlined.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.24f);
            _outlined.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            _outlined.SetFloat(ShaderUtilities.ID_FaceDilate, 0.24f);
            ShaderUtilities.UpdateShaderRatios(_outlined);
            return _outlined;
        }
    }
}
