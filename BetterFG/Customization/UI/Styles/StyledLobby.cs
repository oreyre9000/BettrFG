using System;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles
{
    internal static class StyledLobby
    {
        const string CANVAS = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_MainMenu_Canvas(Clone)";
        const string HOST = "BFG_StyledLobby";
        const string STOCK_PLAY = "SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab";
        const string STOCK_FRIENDS = "SafeArea/BottomRight_Group/Generic_UI_FriendsButton_Variant";
        const string STOCK_SHOWS = "SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Cards";
        const string STOCK_PASS = "SafeArea/BottomLeft_Group/AnimContainer/SeasonPassBanner";
        const string STOCK_TOPLEFT = "SafeAreaTop/LayoutGroup/TopLeft_Group";
        static readonly string[] StockVisuals = { "SafeArea/BottomRight_Group", "SafeArea/TopRight_Group", "SafeArea/BottomLeft_Group", STOCK_TOPLEFT };

        static GameObject _root;
        static LobbySpec _spec;
        static TMP_Text _lvl, _count, _srcLvl, _srcCount, _srcName, _srcNick;
        static Slider _fill;
        static bool _textHooked;

        public static void Build(UIStyle style)
        {
            _spec = style.Lobby;
            var canvas = GameObject.Find(CANVAS)?.transform;
            if (canvas == null || _spec == null) return;
            Teardown();
            var built = StyleLayout.Build(style.Assets, _spec.Layout, canvas);
            var root = built.transform.Find(_spec.Root);
            root.SetParent(canvas, false);
            root.name = HOST;
            UnityEngine.Object.DestroyImmediate(built);
            _root = root.gameObject;
            foreach (var p in _spec.Disable) root.Find(p)?.gameObject.SetActive(false);
            var cg = root.GetComponent<CanvasGroup>();
            if (cg != null) { cg.alpha = 1f; cg.blocksRaycasts = true; cg.interactable = true; }
            foreach (var p in StockVisuals) StyledUI.Hide(canvas.Find(p), true);

            Forward(root.Find(_spec.Play), canvas.Find(STOCK_PLAY));
            Forward(root.Find(_spec.Invite), canvas.Find(STOCK_FRIENDS));
            if (_spec.InvitePosition is Vector2 at)
            {
                var invite = root.Find(_spec.Invite).GetComponent<RectTransform>();
                invite.anchorMin = invite.anchorMax = _spec.InviteAnchor;
                invite.pivot = new Vector2(_spec.InviteAnchor.x, 0.5f);
                invite.anchoredPosition = at;
                invite.GetComponent<HorizontalLayoutGroup>().spacing = _spec.InviteSpacing;
            }
            if (_spec.ShowSelect != null)
            {
                var show = StyleLayout.Build(style.Assets, _spec.ShowSelect, root.Find(_spec.ShowSelectParent)).transform;
                show.gameObject.SetActive(true);
                show.Find("ButtonTextPlay").GetComponent<TMP_Text>().text = FG.Common.CMS.CMSLoader.Instance._localisedStrings.GetString("show_selector_title");
                Forward(show, canvas.Find(STOCK_SHOWS));
            }

            _lvl = root.Find(_spec.SeasonLevel)?.GetComponent<TMP_Text>();
            _count = root.Find(_spec.SeasonCounter)?.GetComponent<TMP_Text>();
            _fill = root.Find(_spec.SeasonFill)?.GetComponent<Slider>();
            if (_spec.SeasonDone != null) root.Find(_spec.SeasonDone)?.gameObject.SetActive(false);
            _srcLvl = _srcCount = null;
            var pass = canvas.Find(STOCK_PASS);
            if (pass != null)
                foreach (var t in pass.GetComponentsInChildren<TMP_Text>(true))
                {
                    var s = t.text.Trim();
                    if (_srcLvl == null && t.name == "Text_SeasonNumber") _srcLvl = t;
                    if (_srcCount == null && s.Contains("/")) _srcCount = t;
                }
            if (!_textHooked)
            {
                _textHooked = true;
                TMPro_EventManager.TEXT_CHANGED_EVENT.Add(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UnityEngine.Object>>(new Action<UnityEngine.Object>(OnTextChanged)));
            }

            var follower = root.gameObject.AddComponent<StyleFollower>();
            follower.Kind = StyleFollower.LOBBY;
            root.gameObject.SetActive(true);
            Refresh();
            Plugin.Log.LogInfo($"{style.Id} lobby up");
        }

        public static void Teardown()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            var canvas = GameObject.Find(CANVAS)?.transform;
            if (canvas == null) return;
            foreach (var p in StockVisuals) StyledUI.Hide(canvas.Find(p), false);
        }

        static void Forward(Transform styled, Transform stock)
        {
            if (styled == null || stock == null) return;
            var real = stock.GetComponent<Button>();
            styled.GetComponent<Button>()?.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => real.onClick.Invoke())));
            Image glyph = null;
            foreach (var i in styled.GetComponentsInChildren<Image>(true))
                if (i.transform != styled && i.GetComponentInChildren<TMP_Text>(true) != null && i.GetComponentInChildren<TMP_Text>(true).transform.parent == i.transform) { glyph = i; break; }
            StyleGlyphs.Bind(glyph, StyleGlyphs.Live(stock));
        }

        static void OnTextChanged(UnityEngine.Object o)
        {
            if (o == null || _root == null) return;
            if ((_srcLvl != null && o.Pointer == _srcLvl.Pointer) || (_srcCount != null && o.Pointer == _srcCount.Pointer)) Season();
            else if ((_srcName != null && o.Pointer == _srcName.Pointer) || (_srcNick != null && o.Pointer == _srcNick.Pointer)) Nameplate();
        }

        public static void Refresh()
        {
            if (_root == null) return;
            Season();
            Nameplate();
        }

        static void Season()
        {
            if (_lvl != null && _srcLvl != null) _lvl.text = _srcLvl.text;
            if (_count == null || _srcCount == null) return;
            _count.text = _srcCount.text;
            var parts = _srcCount.text.Replace(",", "").Replace(".", "").Split('/');
            if (_fill != null && parts.Length == 2 && float.TryParse(parts[0], out var a) && float.TryParse(parts[1], out var b) && b > 0f)
            {
                _fill.minValue = 0f;
                _fill.maxValue = 1f;
                _fill.value = a / b;
            }
        }

        static void Mirror(TMP_Text dst, TMP_Text src)
        {
            if (dst == null || src == null) return;
            dst.richText = true;
            dst.font = src.font;
            dst.fontSharedMaterial = src.fontSharedMaterial;
            dst.text = src.text;
        }

        static void Nameplate()
        {
            var canvas = GameObject.Find(CANVAS)?.transform;
            var top = canvas?.Find(STOCK_TOPLEFT);
            if (top == null) return;
            Transform tag = null;
            foreach (var t in top.GetComponentsInChildren<Transform>(true)) if (t.name == "PB_UI_NameTag") { tag = t; break; }
            if (tag == null) return;
            var back = _root.transform.Find(_spec.NameplateBacking)?.GetComponent<Image>();
            var stockBack = tag.Find("NameTagBacking")?.GetComponent<Image>();
            if (back != null && stockBack != null) back.sprite = stockBack.sprite;
            TMP_Text srcName = null, srcNick = null;
            foreach (var t in tag.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.name == "NameTagUserNameText") srcName = t;
                if (t.name == "NameTagUserNicknameText") srcNick = t;
            }
            _srcName = srcName;
            _srcNick = srcNick;
            var name = _root.transform.Find(_spec.Name)?.GetComponent<TMP_Text>();
            var nick = _root.transform.Find(_spec.Nickname)?.GetComponent<TMP_Text>();
            Mirror(name, srcName);
            Mirror(nick, srcNick);
            bool hasNick = nick != null && srcNick != null && srcNick.text.Length > 0;
            if (nick != null)
            {
                nick.text = hasNick ? srcNick.text : "";
                nick.gameObject.SetActive(hasNick);
            }
            if (name != null && hasNick)
            {
                var nr = name.rectTransform;
                nr.anchoredPosition = new Vector2(nr.anchoredPosition.x, _spec.NameY);
                var kr = nick.rectTransform;
                kr.anchoredPosition = new Vector2(kr.anchoredPosition.x, _spec.NicknameY);
            }
        }
    }
}
