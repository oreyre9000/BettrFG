using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FG.Common.CMS;
using FGClient;
using FGClient.ShowSelector;
using FGClient.UI;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Shows
    {
        public const int VIEW = 16;
        const string HOST = "BFG_S1Shows";
        const string MAIN = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main";
        const string PAGE = "Content/SafeArea/ShowSelectorPage";
        const string STOCK_TABS = "SafeArea/TabsHorizontalLayout";

        sealed class Entry
        {
            public Transform Tile;
            public Action Pick, Confirm;
            public string Desc;
            public int Section, Column;
        }

        sealed class Likes
        {
            public TMP_Text Text;
            public Transform Pill;
            public string Base, Code;
        }

        static Transform _canvas, _root, _content, _rows;
        static bool _stacked, _dislikesHooked;
        static ShowSelectorShowTileViewModel _probe;
        static Sprite _like;
        static readonly List<Likes> _likes = new List<Likes>();
        static TMP_Text _desc;
        static MenuInputHandler _handler;
        static readonly List<Entry> _entries = new List<Entry>();
        static int _cur = -1;

        public static void ArmIn()
        {
            var main = GameObject.Find(MAIN);
            if (main == null) return;
            var vm = main.GetComponentInChildren<ShowSelectorViewModel>(true);
            if (vm == null) return;
            foreach (var view in vm._switchableView.Views)
            {
                var f = S1Kit.Follow(view, VIEW, 2);
                if (view.activeInHierarchy) f.Arm();
            }
        }

        public static void Teardown()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            if (_content != null)
            {
                StyledUI.Hide(_content, false);
                _content.Find(STOCK_TABS).localScale = Vector3.one;
            }
            _root = _canvas = _content = null;
            _handler = null;
            _probe = null;
            _entries.Clear();
            _likes.Clear();
        }

        public static void Apply(UIStyle style, Transform view)
        {
            var canvas = view.GetComponentInParent<ShowSelectorViewModel>().transform;
            if (_canvas != canvas || _root == null) Build(style, canvas);
            Fill(style, view);
        }

        static void Build(UIStyle style, Transform canvas)
        {
            Teardown();
            _canvas = canvas;
            _content = canvas.Find("ShowSelectorContent");
            _root = StyleLayout.Build(style.Assets, "Prefab_UI_Show_Selector", canvas).transform;
            _root.name = HOST;
            S1Kit.Stretch(_root);
            _root.Find("SafeArea/Background").gameObject.SetActive(false);
            _root.Find("SafeArea/SubMenuNavigation").gameObject.SetActive(false);
            _root.Find("SafeArea/SubMenuTitle/BackingTitle/SubMenuTitleText").GetComponent<TMP_Text>().text =
                CMSLoader.Instance._localisedStrings.GetString("show_selector_title");
            var page = _root.Find(PAGE);
            _desc = page.Find("Description/Text").GetComponent<TMP_Text>();
            var sr = page.gameObject.AddComponent<ScrollRect>();
            sr.content = page.Find("TabMasterViewport").GetComponent<RectTransform>();
            sr.viewport = page.GetComponent<RectTransform>();
            sr.horizontal = true;
            sr.vertical = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = false;
            var rows = new GameObject("Rows");
            rows.transform.SetParent(page, false);
            rows.AddComponent<RectTransform>();
            S1Kit.Stretch(rows.transform);
            rows.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -150f);
            rows.AddComponent<RectMask2D>();
            var list = new GameObject("Content");
            list.transform.SetParent(rows.transform, false);
            var lrt = list.AddComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = Vector2.one;
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = lrt.sizeDelta = Vector2.zero;
            var vlg = list.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = vlg.childControlHeight = vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;
            list.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _rows = rows.transform;
            StyledUI.Hide(_content, true);
            Tabs(page.Find("TabCarrouselShowSelector"));
            _root.gameObject.SetActive(true);
            Plugin.Log.LogInfo("season 2 show selector built over the modern one");
        }

        static void Tabs(Transform bar)
        {
            var stock = _content.Find(STOCK_TABS);
            var left = bar.Find("TabCarrouselNavigationLeft");
            var right = bar.Find("TabCarrouselNavigationRight");
            StyleGlyphs.Bind(left.GetComponent<Image>(), StyleGlyphs.Live(stock.Find("NavigationLeftGlyph")));
            StyleGlyphs.Bind(right.GetComponent<Image>(), StyleGlyphs.Live(stock.Find("NavigationRightGlyph")));
            bar.GetComponent<HorizontalLayoutGroup>().spacing = 30f;
            var templates = new[] { bar.Find("SubMenutab"), bar.Find("SubMenutab (1)") };
            var group = bar.GetComponent<ToggleGroup>();
            left.SetAsFirstSibling();
            int n = 0;
            for (int i = 0; i < stock.childCount; i++)
            {
                var real = stock.GetChild(i).GetComponent<Toggle>();
                if (real == null || !real.gameObject.activeSelf) continue;
                var tab = n < templates.Length ? templates[n] : UnityEngine.Object.Instantiate(templates[1].gameObject, bar, false).transform;
                n++;
                tab.gameObject.SetActive(true);
                tab.SetSiblingIndex(n);
                var label = tab.GetComponentInChildren<TMP_Text>(true);
                label.text = real.GetComponentInChildren<TMP_Text>(true).text;
                label.enableWordWrapping = false;
                var mine = tab.GetComponent<Toggle>();
                mine.group = group;
                mine.isOn = real.isOn;
                real.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>(new Action<bool>(on => { if (mine != null && on) mine.isOn = true; })));
                var target = real.gameObject;
                S1Kit.Listen(tab.gameObject, EventTriggerType.PointerClick, () => S1Kit.Click(target));
            }
            right.SetAsLastSibling();
            stock.localScale = Vector3.zero;
            bar.gameObject.SetActive(true);
        }

        static Transform Tile(UIStyle style, Transform row, string name, string party, bool fits, string timeLeft)
        {
            var tile = StyleLayout.Build(style.Assets, "ShowSelectorElement", row).transform;
            tile.name = name;
            tile.gameObject.SetActive(true);
            var c = tile.Find("ShowSelectorContainer");
            c.Find("ShowSelectorHeader/ShowSelectorNameText").GetComponent<TMP_Text>().text = name.ToUpperInvariant();
            c.Find("ShowSelectorPartySize").gameObject.SetActive(party != null && fits);
            c.Find("ShowSelectorPartySizeTooLarge").gameObject.SetActive(party != null && !fits);
            c.Find("ShowSelectorPartySize/ShowSelectorPartySizeText").GetComponent<TMP_Text>().text = party;
            c.Find("ShowSelectorPartySizeTooLarge/ShowSelectorPartySizeText").GetComponent<TMP_Text>().text = party;
            c.Find("ShowSelectorTimeRemaining").gameObject.SetActive(timeLeft != null);
            if (timeLeft != null) c.Find("ShowSelectorTimeRemaining/ShowSelectorTimeRemainingText").GetComponent<TMP_Text>().text = timeLeft;
            c.Find("ShowSelectorHighlight").gameObject.SetActive(false);
            var img = c.Find("ShowSelectorImageMask/ShowSelectorImage");
            img.GetComponent<Image>().enabled = false;
            var art = new GameObject("Art");
            art.transform.SetParent(img, false);
            art.AddComponent<RectTransform>();
            S1Kit.Stretch(art.transform);
            art.AddComponent<RawImage>().raycastTarget = false;
            var fit = art.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = 16f / 9f;

            var tw = tile.GetComponent<TweenOnToggleSubMenu>();
            tw._activeToggleCanvasGroup = c.Find("ShowSelectorHeader/ShowSelectorHighlightImage").GetComponent<CanvasGroup>();
            tw._inactiveToggleCanvasGroup = c.Find("ShowSelectorHeader/ShowSelectorImage").GetComponent<CanvasGroup>();
            tw._activeToggleCanvasGroup.alpha = 0f;
            tw._inactiveToggleCanvasGroup.alpha = 1f;
            tw._scrollRect = _stacked ? null : _root.Find(PAGE).GetComponent<ScrollRect>();
            tw._shouldCenterOnItem = !_stacked;
            tw._playSFXOnSelect = false;
            if (tw._currentTweeners == null) tw._currentTweeners = new Il2CppSystem.Collections.Generic.List<DG.Tweening.Tweener>();
            tile.localScale = tw._downScale;
            tile.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            return tile;
        }

        static string TimeLeft(ShowSelectorShow s)
        {
            if (s == null) return null;
            TimeSpan left;
            try { left = TimeSpan.FromTicks((Marshal.ReadInt64(s.ExpiryDate.Pointer + 16) & 0x3FFFFFFFFFFFFFFF) - DateTime.UtcNow.Ticks); }
            catch (NullReferenceException) { return null; }
            if (left <= TimeSpan.Zero || left.TotalDays > ShowsManager.Instance.DisplayShowExpiryTimerLimit) return null;
            var key = left.TotalDays >= 1 ? "remaining_time_days" : left.TotalHours >= 1 ? "remaining_time_hours" : "remaining_time_minutes";
            return string.Format(CMSLoader.Instance._localisedStrings.GetString(key), left);
        }

        static void Fill(UIStyle style, Transform view)
        {
            var row = _root.Find(PAGE + "/TabMasterViewport");
            var list = _rows.Find("Content");
            for (int i = row.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(row.GetChild(i).gameObject);
            for (int i = list.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(list.GetChild(i).gameObject);
            list.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            _entries.Clear();
            _likes.Clear();
            _cur = -1;
            int start = 0;
            _stacked = view.GetComponent<ShowSelectorDiscoveryShowsTabViewModel>() != null;
            row.gameObject.SetActive(!_stacked);
            _rows.gameObject.SetActive(_stacked);
            var shows = view.GetComponent<ShowSelectorShowsTabViewModel>();
            if (shows != null)
            {
                var sm = ShowsManager.Instance;
                var h = view.GetComponent<MultiRowMenuInputHandler>();
                _handler = h;
                var at = h.CurrentOptionIndex;
                for (int si = 0; si < shows.TabSections.Count; si++)
                {
                    var sec = shows.TabSections[si];
                    var parent = row;
                    TMP_Text header = null;
                    if (_stacked)
                    {
                        header = Header(list, sec.SectionTitleId);
                        parent = Strip(list);
                    }
                    for (int j = 0; j < sec.Shows.Count; j++)
                    {
                        var e = sec.Shows[j].ShowSelectorEntryPoint;
                        var s = sec.Shows[j].ShowSelectorShow;
                        string party = s == null ? null : s.MinPartySize == s.MaxPartySize ? s.MinPartySize.ToString() : s.MinPartySize + "-" + s.MaxPartySize;
                        var tile = Tile(style, parent, e.ShowName, party, s == null || sm.IsPartySizeCorrect(s), TimeLeft(s));
                        if (header != null && j == 0)
                        {
                            var name = tile.Find("ShowSelectorContainer/ShowSelectorHeader/ShowSelectorNameText").GetComponent<TMP_Text>();
                            header.font = name.font;
                            header.fontSharedMaterial = name.fontSharedMaterial;
                        }
                        var art = tile.Find("ShowSelectorContainer/ShowSelectorImageMask/ShowSelectorImage/Art");
                        var raw = art.GetComponent<RawImage>();
                        var remote = art.gameObject.AddComponent<RemoteImage>();
                        remote.OnImageStateChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<RemoteImage.ImageState>>(new Action<RemoteImage.ImageState>(_ => Fit(raw)));
                        if (s != null && !string.IsNullOrEmpty(s.ShowImageUrl)) remote.SetUrl(s.ShowImageUrl);
                        else remote.SetUrlFromDlcImage(e.ShowImageCMS);
                        if (_stacked && s != null) Pills(view, tile, sec.Shows[j]);
                        var opt = new MultiRowMenuInputHandler.OptionIndex(si, j);
                        if (si == at.Row && j == at.ElementInRow) start = _entries.Count;
                        _entries.Add(new Entry { Tile = tile, Section = si, Column = j, Desc = _stacked ? null : e.ShowDescription, Pick = () => h.SelectOption(opt, true, true), Confirm = () => h.ClickCurrentOption() });
                    }
                    if (_stacked) LayoutRebuilder.ForceRebuildLayoutImmediate(parent.GetComponent<RectTransform>());
                }
                if (_stacked) LayoutRebuilder.ForceRebuildLayoutImmediate(list.GetComponent<RectTransform>());
            }
            else
            {
                var stock = view.Find("CustomShowsTiles");
                var h = stock.GetComponent<SimpleCarrouselInputHandler>();
                _handler = h;
                for (int i = 0; i < stock.childCount; i++)
                {
                    var src = stock.GetChild(i);
                    if (!src.gameObject.activeSelf) continue;
                    var squad = src.Find("SquadSize");
                    var time = src.Find("TimeLeft_Mask");
                    var tile = Tile(style, row, src.Find("ShowTitle").GetComponent<TMP_Text>().text,
                        squad.gameObject.activeSelf ? squad.Find("SquadSize_Text").GetComponent<TMP_Text>().text : null,
                        !src.Find("ShowDisabled_Overlay").gameObject.activeSelf,
                        time.gameObject.activeSelf ? time.GetComponentInChildren<TMP_Text>(true).text : null);
                    var shot = src.Find("ShowScreenshot_Mask/ShowScreenshot").GetComponent<RawImage>();
                    var art = tile.Find("ShowSelectorContainer/ShowSelectorImageMask/ShowSelectorImage/Art").GetComponent<RawImage>();
                    art.texture = shot.texture;
                    art.uvRect = shot.uvRect;
                    Fit(art);
                    int idx = i;
                    var target = src.gameObject;
                    if (idx == h.CurrentIndex) start = _entries.Count;
                    _entries.Add(new Entry { Tile = tile, Pick = () => h.SelectByIndex(idx), Confirm = () => S1Kit.Click(target) });
                }
            }
            for (int k = 0; k < _entries.Count; k++)
            {
                int idx = k;
                S1Kit.Listen(_entries[k].Tile.gameObject, EventTriggerType.PointerEnter, () => Select(idx, false));
                S1Kit.Listen(_entries[k].Tile.gameObject, EventTriggerType.PointerClick, () => { if (_cur == idx) _entries[idx].Confirm(); else Select(idx, false); });
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(row.GetComponent<RectTransform>());
            _handler.onMoveLeft = DelegateSupport.ConvertDelegate<MenuInputHandler.MoveLeft>(new Action(() => Step(-1)));
            _handler.onMoveRight = DelegateSupport.ConvertDelegate<MenuInputHandler.MoveRight>(new Action(() => Step(1)));
            _handler.onMoveUp = DelegateSupport.ConvertDelegate<MenuInputHandler.MoveUp>(new Action(() => Jump(-1)));
            _handler.onMoveDown = DelegateSupport.ConvertDelegate<MenuInputHandler.MoveDown>(new Action(() => Jump(1)));
            if (_entries.Count > 0) Select(start, true);
            Plugin.Log.LogInfo($"{view.name}: {_entries.Count} tiles");
        }

        static Transform Pill(Transform tile, Transform parent, string text, Sprite icon)
        {
            var pill = UnityEngine.Object.Instantiate(tile.Find("ShowSelectorContainer/ShowSelectorPartySize").gameObject, parent, false).transform;
            pill.name = "Pill";
            pill.gameObject.SetActive(true);
            var label = pill.Find("ShowSelectorPartySizeText");
            UnityEngine.Object.DestroyImmediate(label.GetComponent<UnityEngine.UI.LayoutElement>());
            label.GetComponent<TMP_Text>().text = text;
            var img = pill.Find("ShowSelectorPartyIcon");
            img.gameObject.SetActive(icon != null);
            if (icon != null)
            {
                var i = img.GetComponent<Image>();
                i.sprite = icon;
                i.preserveAspect = true;
            }
            return pill;
        }

        static Transform Group(Transform parent, string name, Vector2 anchor, Vector2 pos, bool vertical)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            HorizontalOrVerticalLayoutGroup lg = vertical ? go.AddComponent<VerticalLayoutGroup>() : go.AddComponent<HorizontalLayoutGroup>();
            lg.spacing = 6f;
            lg.childAlignment = anchor.y > 0.5f ? TextAnchor.UpperLeft : TextAnchor.LowerLeft;
            lg.childControlWidth = lg.childControlHeight = lg.childForceExpandWidth = lg.childForceExpandHeight = false;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go.transform;
        }

        static void Pills(Transform view, Transform tile, ShowData data)
        {
            if (_probe == null)
            {
                var real = view.GetComponentInChildren<ShowSelectorShowTileViewModel>(true);
                _like = real.transform.Find("ShowTileHolder/DefaultTags2/LikeCount/LikeIcon").GetComponent<Image>().sprite;
                var probe = UnityEngine.Object.Instantiate(real.gameObject, _root, false);
                probe.name = "Probe";
                var cg = probe.GetComponent<CanvasGroup>() ?? probe.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                cg.blocksRaycasts = cg.interactable = false;
                probe.transform.localScale = Vector3.zero;
                probe.SetActive(true);
                _probe = probe.GetComponent<ShowSelectorShowTileViewModel>();
            }
            if (!_dislikesHooked)
            {
                _dislikesHooked = true;
                Tweaks.ShowTileDislikesTweak.Fetched += OnDislikes;
            }
            var show = data.ShowSelectorShow;
            _probe.SetData(data);
            var area = tile.Find("ShowSelectorContainer/ShowSelectorImageMask");
            var top = Group(area, "Top", new Vector2(0f, 1f), new Vector2(12f, -58f), true);
            var bottom = Group(area, "Bottom", Vector2.zero, new Vector2(12f, 12f), false);
            if (!string.IsNullOrEmpty(_probe.GameMode))
                Pill(tile, top, _probe.GameMode.ToUpperInvariant() + Tweaks.ShowTilePlaysTweak.SuffixFor(show), null);
            if (show.Stats != null)
            {
                var pill = Pill(tile, top, "", _like);
                var l = new Likes
                {
                    Pill = pill,
                    Text = pill.Find("ShowSelectorPartySizeText").GetComponent<TMP_Text>(),
                    Base = FraggleLocalizationUtils.GetLevelLikeCountString(CMSLoader.Instance._localisedStrings, show.Stats.LikesCount),
                    Code = show.ShareCode,
                };
                _likes.Add(l);
                Dislikes(l);
            }
            var tags = _probe.ShowTags;
            for (int i = 0; tags != null && i < tags.Length; i++)
                if (tags[i] != null && !string.IsNullOrEmpty(tags[i].TagText)) Pill(tile, bottom, tags[i].TagText, null);
            LayoutRebuilder.ForceRebuildLayoutImmediate(top.GetComponent<RectTransform>());
            LayoutRebuilder.ForceRebuildLayoutImmediate(bottom.GetComponent<RectTransform>());
        }

        static void Dislikes(Likes l)
        {
            bool known = Tweaks.ShowTileDislikesTweak.TryDislikes(l.Code, out int n);
            l.Text.text = known ? l.Base + " / " + n.ToString("N0") : l.Base;
            var icon = l.Pill.Find("Dislike");
            if (icon == null && known)
            {
                icon = UnityEngine.Object.Instantiate(l.Pill.Find("ShowSelectorPartyIcon").gameObject, l.Pill, false).transform;
                icon.name = "Dislike";
                icon.localScale = new Vector3(1f, -1f, 1f);
            }
            if (icon != null) icon.gameObject.SetActive(known);
            LayoutRebuilder.ForceRebuildLayoutImmediate(l.Pill.parent.GetComponent<RectTransform>());
        }

        static void OnDislikes()
        {
            foreach (var l in _likes) if (l.Text != null) Dislikes(l);
        }

        static void Fit(RawImage raw)
        {
            if (raw == null || raw.texture == null) return;
            var uv = raw.uvRect;
            raw.GetComponent<AspectRatioFitter>().aspectRatio = raw.texture.width * uv.width / Mathf.Max(1f, raw.texture.height * uv.height);
        }

        static TMP_Text Header(Transform list, string titleId)
        {
            var go = new GameObject("Header");
            go.transform.SetParent(list, false);
            go.AddComponent<RectTransform>().sizeDelta = new Vector2(1800f, 50f);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = CMSLoader.Instance._localisedStrings.GetString(titleId).ToUpperInvariant();
            t.fontSize = 40f;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.BottomLeft;
            t.enableWordWrapping = false;
            t.margin = new Vector4(150f, 0f, 0f, 0f);
            t.raycastTarget = false;
            return t;
        }

        static Transform Strip(Transform list)
        {
            var row = new GameObject("Row");
            row.transform.SetParent(list, false);
            row.AddComponent<RectTransform>().sizeDelta = new Vector2(1920f, 380f);
            var strip = new GameObject("Strip");
            strip.transform.SetParent(row.transform, false);
            var rt = strip.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 560f);
            rt.localScale = new Vector3(0.75f, 0.75f, 1f);
            var hlg = strip.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 20f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.padding = new RectOffset(200, 200, 0, 0);
            hlg.childControlWidth = hlg.childControlHeight = hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
            strip.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            return strip.transform;
        }

        static void Step(int dir)
        {
            int next = _cur + dir;
            if (next < 0 || next >= _entries.Count || (_stacked && _entries[next].Section != _entries[_cur].Section)) return;
            Select(next, false);
        }

        static void Jump(int dir)
        {
            int sec = _entries[_cur].Section + dir, col = _entries[_cur].Column, best = -1;
            for (int k = 0; k < _entries.Count; k++)
                if (_entries[k].Section == sec && (best < 0 || (_stacked && _entries[k].Column <= col))) best = k;
            if (best >= 0) Select(best, false);
        }

        static void Reveal(Transform tile)
        {
            var strip = tile.parent;
            var view = _rows.GetComponent<RectTransform>();
            var c = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            var v = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            view.GetWorldCorners(v);
            tile.GetComponent<RectTransform>().GetWorldCorners(c);
            float scale = strip.parent.lossyScale.x;
            float pad = 150f * scale;
            float shift = c[0].x < v[0].x + pad ? v[0].x + pad - c[0].x : c[2].x > v[2].x - pad ? v[2].x - pad - c[2].x : 0f;
            if (shift != 0f)
            {
                var srt = strip.GetComponent<RectTransform>();
                float x = Mathf.Clamp(srt.anchoredPosition.x + shift / scale, Mathf.Min(0f, view.rect.width - srt.rect.width * strip.localScale.x), 0f);
                DG.Tweening.ShortcutExtensions.DOKill(strip, true);
                DG.Tweening.TweenSettingsExtensions.SetEase(DG.Tweening.ShortcutExtensions.DOLocalMoveX(strip, strip.localPosition.x + x - srt.anchoredPosition.x, 0.25f), DG.Tweening.Ease.OutQuad);
            }
            strip.parent.GetComponent<RectTransform>().GetWorldCorners(c);
            float top = c[1].y + 60f * scale;
            float dy = top > v[1].y ? v[1].y - top : c[0].y < v[0].y ? v[0].y - c[0].y : 0f;
            if (dy == 0f) return;
            var list = _rows.Find("Content");
            var lrt = list.GetComponent<RectTransform>();
            float y = Mathf.Clamp(lrt.anchoredPosition.y + dy / scale, 0f, Mathf.Max(0f, lrt.rect.height - view.rect.height));
            DG.Tweening.ShortcutExtensions.DOKill(list, true);
            DG.Tweening.TweenSettingsExtensions.SetEase(DG.Tweening.ShortcutExtensions.DOLocalMoveY(list, list.localPosition.y + y - lrt.anchoredPosition.y, 0.25f), DG.Tweening.Ease.OutQuad);
        }

        static void Select(int i, bool fromGame)
        {
            if (!fromGame) _entries[i].Pick();
            if (_cur == i) return;
            for (int k = 0; k < _entries.Count; k++)
            {
                var t = _entries[k].Tile;
                t.Find("ShowSelectorContainer/ShowSelectorHighlight").gameObject.SetActive(k == i);
                var tw = t.GetComponent<TweenOnToggleSubMenu>();
                if (k == i) tw.StartTweenOn();
                else if (k == _cur) tw.StartTweenOff();
            }
            _cur = i;
            _desc.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(_entries[i].Desc));
            _desc.text = _entries[i].Desc;
            if (_stacked) Reveal(_entries[i].Tile);
            else _entries[i].Tile.GetComponent<TweenOnToggleSubMenu>().TryCenterOnItem();
        }
    }
}
