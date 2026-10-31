using BetterFG.Services;
using BetterFG.Utilities;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI
{
    public class SlotDropdown
    {
        private const string BG_PREFIX = "BetterFG.assets.ui.";

        private static string RowTextureFor(string bgResource)
        {
            if (string.IsNullOrEmpty(bgResource)) return null;
            if (!bgResource.StartsWith(BG_PREFIX) || !bgResource.EndsWith(".png")) return null;
            string key = bgResource.Substring(BG_PREFIX.Length, bgResource.Length - BG_PREFIX.Length - 4);
            return BG_PREFIX + "tab.rows." + key.Replace('.', '_') + ".png";
        }

        private static readonly Dictionary<string, Texture2D> _bgTexCache = new Dictionary<string, Texture2D>();
        private static Texture2D LoadBg(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return null;
            if (_bgTexCache.TryGetValue(resource, out var t) && t != null) return t;
            t = EmbeddedResourceandUnity.LoadTexture(resource);
            _bgTexCache[resource] = t;
            return t;
        }

        public const float ITEM_H = 42f * UIScale.S;
        public const float ANIM_DUR = 0.22f;

        private static readonly AnimationCurve ScaleCurve = new AnimationCurve(new Keyframe[]
        {
            new Keyframe(0.0000f, 0.0000f, 0.0162f, 0.0162f),
            new Keyframe(0.2214f, 0.4801f, 1.8824f, 1.8824f),
            new Keyframe(1.0000f, 1.0000f, 0.0000f, 0.0000f),
        });

        private static readonly Vector3 TITLE_POS = new Vector3(21.6729f, 225f, 0f);
        private const float TITLE_REACH = -4f;
        private static readonly Vector3 TITLE_LABEL_POS = new Vector3(7.8f, -3.96f, 0f);

        private static readonly Dictionary<string, string> TAB_DESC = new Dictionary<string, string>
        {
            { "Customization", "switch_tab.desc.customization" },
            { "UGC Customization", "switch_tab.desc.ugc_customization" },
            { "Main Menu", "switch_tab.desc.main_menu" },
            { "User Interface", "switch_tab.desc.user_interface" },
            { "Nametag", "switch_tab.desc.nametag" },
            { "Social", "switch_tab.desc.social" },
            { "Features", "switch_tab.desc.features" },
            { "Skin Texture", "switch_tab.desc.skin_texture" },
            { "All Cosmetics", "switch_tab.desc.all_cosmetics" },
            { "Creative", "switch_tab.desc.creative" },
            { "Personal Bests", "switch_tab.desc.personal_bests" },
            { "Replays", "switch_tab.desc.replays" },
            { "Plinths", "switch_tab.desc.plinths" },
            { "Pets", "switch_tab.desc.pets" },
        };

        private GameObject _headerGo;
        private GameObject _go;
        private float _animElapsed = -1f;
        public bool IsOpen => _go != null;

        private readonly List<GameObject> _hidden = new List<GameObject>();
        private RectTransform _tabRoot;

        public void Open(RectTransform tabRoot, int ownerIdx, string ownerName, string[] tabNames, string[] tabTitles, string[] tabBgResources, string[] occupiedNames)
        {
            Close();
            if (tabRoot == null) return;

            AudioService.PlaySlotDwopdmdmom();

            _tabRoot = tabRoot;
            _hidden.Clear();
            var contentT = tabRoot.Find("Content");
            if (contentT != null && contentT.gameObject.activeSelf)
            {
                contentT.gameObject.SetActive(false);
                _hidden.Add(contentT.gameObject);
            }

            _headerGo = new GameObject("SwitchTabHeader");
            _headerGo.hideFlags = HideFlags.HideAndDontSave;
            _headerGo.transform.SetParent(tabRoot, false);
            _headerGo.transform.SetAsLastSibling();
            var headerRt = _headerGo.AddComponent<RectTransform>();
            headerRt.anchorMin = Vector2.zero;
            headerRt.anchorMax = Vector2.one;
            headerRt.offsetMin = headerRt.offsetMax = Vector2.zero;

            var titleGo = new GameObject("TitleBar");
            titleGo.transform.SetParent(_headerGo.transform, false);
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.offsetMin = new Vector2(0f, -UIScale.TITLE_H - TITLE_REACH);
            titleRt.offsetMax = Vector2.zero;
            float contentH = tabRoot.rect.height - UIScale.TITLE_H;
            titleRt.localPosition = TITLE_POS + new Vector3(0f, (contentH - UIScale.TAB_CONTENT_H) * 0.5f, 0f);
            titleRt.localRotation = Quaternion.Euler(22f, 345f, 0f);
            titleRt.localScale = new Vector3(1.2f, 1.3f, 1.3f);

            var titleTxt = UGUIShip.CreateLabel(titleGo.transform, default, "ui.switch_tab", UIScale.FS_TITLE,
                new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
            titleTxt.fontStyle = FontStyle.Bold;
            UGUIShip.Unstylize(titleTxt);
            var titleLblRt = titleTxt.rectTransform;
            titleLblRt.anchorMin = new Vector2(0f, 1f);
            titleLblRt.anchorMax = new Vector2(1f, 1f);
            titleLblRt.pivot = new Vector2(0.5f, 1f);
            titleLblRt.sizeDelta = new Vector2(0f, UIScale.TITLE_H);
            titleLblRt.anchoredPosition = Vector2.zero;
            titleLblRt.offsetMin = new Vector2(UIScale.PAD * 3f, titleLblRt.offsetMin.y);
            titleLblRt.localPosition = TITLE_LABEL_POS;

            int tabCount = 0;
            if (tabNames != null)
                foreach (var n in tabNames) if (!string.IsNullOrEmpty(n)) tabCount++;
            float rowsH = tabCount * (ITEM_H + 2f) + 8f;
            float topReserve = UIScale.TITLE_H + 10f;
            float maxH = contentH - topReserve - 6f;
            float scrollH = rowsH > maxH ? maxH : rowsH;
            float panelH = scrollH + topReserve;

            _go = new GameObject("SlotDropdown");
            _go.hideFlags = HideFlags.HideAndDontSave;
            _go.transform.SetParent(tabRoot, false);
            _go.transform.SetAsLastSibling();

            var rt = _go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -panelH);
            rt.offsetMax = Vector2.zero;
            rt.localScale = new Vector3(1f, 0f, 1f);

            var cg = _go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = true;

            var (sr, contentRt) = UGUIShip.CreateScrollView(_go.transform,
                new Rect(4f, topReserve, UIScale.TAB_W - 8f, scrollH));
            var contentGo = contentRt.gameObject;
            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;
            vlg.padding = new RectOffset(2, 2, 2, 4);
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var occupied = new HashSet<string>();
            if (occupiedNames != null)
            {
                foreach (var name in occupiedNames)
                    if (!string.IsNullOrEmpty(name)) occupied.Add(name);
            }

            if (tabNames == null) tabNames = new string[0];
            int rowIdx = 0;
            for (int i = 0; i < tabNames.Length; i++)
            {
                string tabName = tabNames[i];
                if (string.IsNullOrEmpty(tabName)) continue;
                string tabTitle = (tabTitles != null && i < tabTitles.Length && !string.IsNullOrEmpty(tabTitles[i]))
                    ? tabTitles[i] : tabName;

                bool isCurrentSlot = tabName == ownerName;
                bool blocked = occupied.Contains(tabName) && !isCurrentSlot;
                string capturedName = tabName;
                int capturedIdx = ownerIdx;

                bool disabled = blocked || isCurrentSlot;
                int myRowIdx = rowIdx++;

                Color titleColor = isCurrentSlot
                    ? new Color(0.6f, 1f, 0.6f, 0.95f)
                    : disabled
                        ? new Color(1f, 1f, 1f, 0.15f)
                        : new Color(1f, 1f, 1f, 0.95f);
                Color descColor = new Color(titleColor.r, titleColor.g, titleColor.b, titleColor.a * 0.55f);

                Action clickAction = isCurrentSlot
                    ? new Action(() =>
                    {
                        var ui = BetterFGUIMan.Instance;
                        ui?.KeepDropdownTabOpen();
                        Close();
                    })
                    : blocked
                        ? new Action(() =>
                        {
                            Close();
                            var ui = BetterFGUIMan.Instance;
                            if (ui != null) ui.SwapPlacesFromDropdown(capturedIdx, capturedName);
                        })
                        : new Action(() =>
                        {
                            Close();
                            var ui = BetterFGUIMan.Instance;
                            if (ui != null) ui.SwapSlotFromDropdown(capturedIdx, capturedName);
                        });

                int titleFont = UIScale.FS_TITLE + 3;
                int descFont = UIScale.FS_SM;
                var btn = UGUIShip.CreateButton(contentGo.transform, tabTitle,
                    Color.clear, titleColor, titleFont, clickAction, skipHoverSound: false, customSprite: false, shine: false,
                    passThroughDrag: true);
                btn.gameObject.AddComponent<LayoutElement>().preferredHeight = ITEM_H;

                UGUIShip.PaintListRow(btn, myRowIdx, isCurrentSlot);

                string bgRes = (tabBgResources != null && i < tabBgResources.Length) ? tabBgResources[i] : null;
                var bgTex = LoadBg(RowTextureFor(bgRes));
                if (bgTex != null)
                {
                    var previewGo = new GameObject("BgPreview");
                    previewGo.transform.SetParent(btn.transform, false);
                    previewGo.transform.SetSiblingIndex(1);
                    var pRt = previewGo.AddComponent<RectTransform>();
                    pRt.anchorMin = Vector2.zero;
                    pRt.anchorMax = Vector2.one;
                    pRt.offsetMin = pRt.offsetMax = Vector2.zero;
                    var raw = previewGo.AddComponent<RawImage>();
                    raw.texture = bgTex;
                    raw.color = new Color(1f, 1f, 1f, 0.55f);
                    raw.raycastTarget = false;
                }

                var rowShine = btn.transform.Find("RowShine");
                if (rowShine != null) rowShine.SetAsLastSibling();

                var lbl = btn.transform.Find("Label")?.GetComponent<Text>();
                if (lbl != null)
                {
                    lbl.alignment = TextAnchor.LowerLeft;
                    var lblRt = lbl.GetComponent<RectTransform>();
                    if (lblRt != null)
                    {
                        lblRt.anchorMin = new Vector2(0f, 0.5f);
                        lblRt.anchorMax = new Vector2(1f, 1f);
                        lblRt.offsetMin = new Vector2(12f, 0f);
                        lblRt.offsetMax = new Vector2(-8f, -2f);
                    }
                    lbl.transform.SetAsLastSibling();
                }

                string descText;
                if (TAB_DESC.TryGetValue(tabName, out descText) && !string.IsNullOrEmpty(descText))
                {
                    var descLbl = UGUIShip.CreateLabel(btn.transform, default, descText, descFont,
                        descColor, TextAnchor.UpperLeft);
                    var dRt = descLbl.rectTransform;
                    dRt.anchorMin = new Vector2(0f, 0f);
                    dRt.anchorMax = new Vector2(1f, 0.5f);
                    dRt.offsetMin = new Vector2(12f, 2f);
                    dRt.offsetMax = new Vector2(-8f, 0f);
                    descLbl.raycastTarget = false;
                }

            }

            _animElapsed = 0f;
        }

        public void Close()
        {
            _animElapsed = -1f;
            if (_go != null) { UnityEngine.Object.Destroy(_go); _go = null; }
            if (_headerGo != null) { UnityEngine.Object.Destroy(_headerGo); _headerGo = null; }

            foreach (var go in _hidden)
                if (go != null) go.SetActive(true);
            _hidden.Clear();
            _tabRoot = null;
        }

        public void Tick()
        {
            if (_animElapsed < 0f || _go == null) return;
            _animElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_animElapsed / ANIM_DUR);
            _go.GetComponent<RectTransform>().localScale = new Vector3(1f, ScaleCurve.Evaluate(t), 1f);
            if (t >= 1f) _animElapsed = -1f;
        }

        public bool HitTest(Vector2 screenPos)
        {
            if (_go == null) return false;
            var rt = _go.GetComponent<RectTransform>();
            return rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null);
        }
    }
}
