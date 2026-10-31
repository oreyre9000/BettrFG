using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Features.Replay;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using FG.Common;
using FGClient.UI;
using FGClient.UI.PrivateLobby;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static FGClient.UI.UIModalMessage;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class PublishThumbnailPicker
    {
        const int Cols = 3;
        const float CellW = 430f, CellH = 302f, Gap = 26f, PadV = 22f, PadH = 16f, Gutter = 38f;
        const float ViewH = CellH * 2f + Gap + PadV * 2f + 30f;
        const float ViewW = Cols * CellW + (Cols - 1) * Gap + PadH * 2f + Gutter * 2f;
        const float ContentPad = 150f;
        static readonly Color ChosenOutline = new Color(1f, 0.776f, 0f, 1f);
        const float FirstRepeat = 0.32f, NextRepeat = 0.09f;

        static readonly string[] Right = { "D-Pad Right", "DPad Right", "Hat Right" };
        static readonly string[] Left = { "D-Pad Left", "DPad Left", "Hat Left" };
        static readonly string[] Up = { "D-Pad Up", "DPad Up", "Hat Up" };
        static readonly string[] Down = { "D-Pad Down", "DPad Down", "Hat Down" };
        static readonly string[] StickX = { "Left Stick X", "Left Stick Horizontal" };
        static readonly string[] StickY = { "Left Stick Y", "Left Stick Vertical" };

        static List<string> _files;
        static Toggle[] _toggles;
        static Image[][] _panels;
        static Transform _blue, _green;
        static RectTransform _grid;
        static int _sel;
        static bool _built;

        internal static bool Open(LevelEditorPublishPopupViewModel publish, Action<bool> onClosed)
        {
            if (!PublishThumbnail.Enabled) return false;

            PublishThumbnail.Armed = false;
            PublishThumbnail.Clear();
            _files = PublishThumbnail.PicturesFor(CreativeRoundMemory.GetCurrentShareCode());
            foreach (string path in ReplayImages.ListFiles())
                if (!_files.Contains(path)) _files.Add(path);

            var lobby = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<PrivateLobbyShowListViewModel>());
            if (lobby.Length == 0)
            {
                Plugin.Log.LogWarning("lobby show list isn't loaded, no tile to borrow. publishing with the editor shot");
                return false;
            }
            var list = lobby[0].Cast<PrivateLobbyShowListViewModel>();

            NavPromptCore.SetCmsString("bfg_publishthumb_title", LocalizationService.Get("ui.level_thumbnail"));
            _built = false;
            _sel = 0;

            var data = new ModalMessageData
            {
                ModalType = ModalType.MT_OK_CANCEL,
                OkButtonType = OKButtonType.Positive,
                ShowExternalLinkIcon = false,
                LocaliseMessage = LocaliseOption.NotLocalised,
                OnFocusGained = new Action(() =>
                {
                    if (_built) return;
                    _built = true;
                    PopupManager.s_Instance.StartCoroutine(Run(list).WrapToIl2Cpp());
                }),
                OnCloseButtonPressed = new Action<bool>(ok =>
                {
                    if (ok) Apply(publish);
                    onClosed(ok);
                })
            };
            data.Title = "bfg_publishthumb_title";
            data.Message = " ";

            PopupManager.s_Instance.StartCoroutine(ShowWhenFree(data).WrapToIl2Cpp());
            return true;
        }

        static IEnumerator ShowWhenFree(ModalMessageData data)
        {
            while (FallGuysLib.UI.PopUp.HasActive) yield return null;
            bool shown = PopupManager.s_Instance.Show(PopupInteractionType.Query, data, null);
            Plugin.Log.LogInfo($"thumbnail picker, editor shot + {_files.Count} replay picture(s), shown={shown}");
        }

        static void Apply(LevelEditorPublishPopupViewModel publish)
        {
            if (_sel == 0) { PublishThumbnail.Clear(); return; }

            PublishThumbnail.Choose(_files[_sel - 1]);
            publish._shouldAskThumbnailUpload = false;
            publish._shouldUploadThumbnailDirectly = true;
        }

        static IEnumerator Run(PrivateLobbyShowListViewModel list)
        {
            yield return null;

            var pm = PopupManager.s_Instance;
            var vm = pm.ActivePopup;
            var root = vm.transform;
            var content = root.Find("Content");
            var body = content.Find("Content_Text").gameObject;
            var rootRt = root.GetComponent<RectTransform>();
            var contentRt = content.GetComponent<RectTransform>();
            float oldW = rootRt.rect.width, oldContentW = contentRt.rect.width;

            body.SetActive(false);
            rootRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ViewW + ContentPad + (oldW - oldContentW));
            contentRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ViewW + ContentPad);

            var view = Build(root, content, list);

            int loaded = 0, repX = 0, repY = 0;
            float repT = 0f;
            var mouse = ReInput.controllers.Mouse;

            while (view != null && vm.gameObject.activeInHierarchy && pm.ActivePopup == vm)
            {
                if (loaded < _files.Count) { _raws[loaded + 1].texture = ReplayImages.Thumb(_files[loaded]); loaded++; }

                if (mouse != null && mouse.axisCount > 2)
                {
                    float wheel = mouse.GetAxis(2);
                    if (Mathf.Abs(wheel) > 0.0001f) SetScroll(_grid.anchoredPosition.y - wheel * (CellH + Gap) * 0.6f);
                }

                ReadDir(out int dx, out int dy);
                if (dx == 0 && dy == 0) { repX = repY = 0; repT = 0f; }
                else if (dx != repX || dy != repY || (repT -= Time.unscaledDeltaTime) <= 0f)
                {
                    repT = dx != repX || dy != repY ? FirstRepeat : NextRepeat;
                    repX = dx; repY = dy;
                    Move(dx, dy);
                }
                yield return null;
            }

            if (view != null) UnityEngine.Object.Destroy(view);
            body.SetActive(true);
            rootRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, oldW);
            contentRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, oldContentW);
            _toggles = null;
            _panels = null;
            _grid = null;
            _raws = null;
        }

        static RawImage[] _raws;

        static GameObject Build(Transform root, Transform content, PrivateLobbyShowListViewModel list)
        {
            var label = root.Find("ButtonContainer/LeftButton/Content/Text").gameObject;
            _blue = root.Find("ButtonContainer/LeftButton/Panel_Unselected");
            _green = root.Find("ButtonContainer/RightButton/Panel_Unselected");
            var round = _blue.Find("Panel_Fill").GetComponent<Image>().sprite;
            var srcTile = list._showsParentTransform.Find("EnterCodeButton").gameObject;
            var srcBar = list.GetComponentInChildren<ScrollRect>(true).verticalScrollbar.gameObject;

            var view = new GameObject("BettrFG_ThumbnailPicker");
            view.transform.SetParent(content, false);
            view.AddComponent<RectTransform>();
            var le = view.AddComponent<LayoutElement>();
            le.preferredWidth = ViewW;
            le.preferredHeight = ViewH;
            var sr = view.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.inertia = false;
            sr.movementType = ScrollRect.MovementType.Clamped;

            var vp = new GameObject("Viewport");
            vp.transform.SetParent(view.transform, false);
            var vpRt = vp.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(Gutter, 0f);
            vpRt.offsetMax = new Vector2(-Gutter, 0f);
            vp.AddComponent<RectMask2D>();

            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(vp.transform, false);
            _grid = gridGo.AddComponent<RectTransform>();
            _grid.anchorMin = new Vector2(0f, 1f);
            _grid.anchorMax = Vector2.one;
            _grid.pivot = new Vector2(0.5f, 1f);
            _grid.sizeDelta = Vector2.zero;
            var g = gridGo.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(CellW, CellH);
            g.spacing = new Vector2(Gap, Gap);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = Cols;
            g.childAlignment = TextAnchor.UpperCenter;
            g.padding = new RectOffset((int)PadH, (int)PadH, (int)PadV, (int)PadV);
            gridGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var group = gridGo.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;

            var bar = UnityEngine.Object.Instantiate(srcBar, view.transform, false).GetComponent<Scrollbar>();
            var barRt = bar.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(1f, 0f);
            barRt.anchorMax = Vector2.one;
            barRt.pivot = new Vector2(1f, 0.5f);
            barRt.offsetMin = new Vector2(-Gutter * 0.5f - 12f, 16f);
            barRt.offsetMax = new Vector2(-Gutter * 0.5f + 12f, -16f);
            sr.content = _grid;
            sr.viewport = vpRt;
            sr.verticalScrollbar = bar;
            view.AddComponent<BettrFG.uGUI.SmoothScroll>();
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            int count = _files.Count + 1;
            _toggles = new Toggle[count];
            _panels = new Image[count][];
            _raws = new RawImage[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var tile = UnityEngine.Object.Instantiate(srcTile, gridGo.transform, false);
                tile.name = "Picture_" + i;
                tile.SetActive(true);
                UnityEngine.Object.DestroyImmediate(tile.GetComponent<EventTrigger>());
                UnityEngine.Object.DestroyImmediate(tile.GetComponent("ActiveBinding"));
                UnityEngine.Object.DestroyImmediate(tile.transform.Find("Content").gameObject);
                UnityEngine.Object.DestroyImmediate(tile.transform.Find("Container_LoadingSpinner").gameObject);

                _panels[i] = tile.transform.Find("Panel_Unselected").GetComponentsInChildren<Image>(true);

                var toggle = tile.GetComponent<Toggle>();
                toggle.group = group;
                toggle.isOn = false;
                var nav = toggle.navigation;
                nav.mode = Navigation.Mode.None;
                toggle.navigation = nav;
                _toggles[i] = toggle;

                var clip = new GameObject("Shot");
                clip.transform.SetParent(tile.transform, false);
                var clipRt = clip.AddComponent<RectTransform>();
                clipRt.anchorMin = Vector2.zero;
                clipRt.anchorMax = Vector2.one;
                clipRt.offsetMin = new Vector2(10f, 56f);
                clipRt.offsetMax = new Vector2(-10f, -10f);
                var clipImg = clip.AddComponent<Image>();
                clipImg.sprite = round;
                clipImg.type = Image.Type.Sliced;
                clipImg.raycastTarget = false;
                clip.AddComponent<Mask>().showMaskGraphic = false;

                var shot = new GameObject("Img");
                shot.transform.SetParent(clip.transform, false);
                var shotRt = shot.AddComponent<RectTransform>();
                shotRt.anchorMin = Vector2.zero;
                shotRt.anchorMax = Vector2.one;
                shotRt.offsetMin = shotRt.offsetMax = Vector2.zero;
                var raw = shot.AddComponent<RawImage>();
                raw.raycastTarget = false;
                if (i == 0) raw.texture = LevelEditorManager.Instance.IO.LevelThumbnail;
                _raws[i] = raw;

                var txt = UnityEngine.Object.Instantiate(label, tile.transform, false);
                var txtRt = txt.GetComponent<RectTransform>();
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = new Vector2(1f, 0f);
                txtRt.pivot = new Vector2(0.5f, 0f);
                txtRt.anchoredPosition = new Vector2(0f, 18f);
                txtRt.sizeDelta = new Vector2(-24f, 40f);
                UnityEngine.Object.Destroy(txt.GetComponent("TextBinding"));
                var tmp = txt.GetComponent<TMPro.TextMeshProUGUI>();
                tmp.text = i == 0
                    ? LocalizationService.Get("publishthumbnail.editor_shot")
                    : File.GetLastWriteTime(_files[i - 1]).ToString("g", System.Globalization.CultureInfo.CurrentCulture);
                tmp.alignment = TMPro.TextAlignmentOptions.Center;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 14f;
                tmp.fontSizeMax = 30f;
                tmp.raycastTarget = false;

                var trig = tile.AddComponent<EventTrigger>();
                var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                click.callback.AddListener(new Action<BaseEventData>(_ => Choose(index)));
                trig.triggers.Add(click);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(root.GetComponent<RectTransform>());
            Select(0);
            return view;
        }

        static void Move(int dx, int dy)
        {
            int count = _toggles.Length;
            int col = Mathf.Clamp(_sel % Cols + dx, 0, Cols - 1);
            int row = _sel / Cols + dy;
            int next = row * Cols + col;
            if (next >= 0 && next < count) Select(next);
            else if (dy > 0 && row * Cols < count) Select(count - 1);
        }

        static void Choose(int index)
        {
            _sel = index;
            for (int i = 0; i < _panels.Length; i++)
                foreach (var im in _panels[i])
                {
                    var src = (i == index ? _green : _blue).Find(im.name).GetComponent<Image>();
                    im.color = i == index && im.name == "Panel_Outline" ? ChosenOutline : src.color;
                    im.sprite = src.sprite;
                    im.pixelsPerUnitMultiplier = src.pixelsPerUnitMultiplier;
                }
        }

        static void Select(int index)
        {
            Choose(index);
            EventSystem.current.SetSelectedGameObject(_toggles[index].gameObject);

            float top = PadV + index / Cols * (CellH + Gap);
            float y = _grid.anchoredPosition.y;
            if (top - PadV < y) y = top - PadV;
            else if (top + CellH + PadV - y > ViewH) y = top + CellH + PadV - ViewH;
            SetScroll(y);
        }

        static void SetScroll(float y) =>
            _grid.anchoredPosition = new Vector2(_grid.anchoredPosition.x, Mathf.Clamp(y, 0f, Mathf.Max(0f, _grid.rect.height - ViewH)));

        static void ReadDir(out int dx, out int dy)
        {
            dx = (NavPromptCore.ElementHeldByName(Right) ? 1 : 0) - (NavPromptCore.ElementHeldByName(Left) ? 1 : 0);
            dy = (NavPromptCore.ElementHeldByName(Down) ? 1 : 0) - (NavPromptCore.ElementHeldByName(Up) ? 1 : 0);
            if (dx == 0 && dy == 0)
            {
                float sx = NavPromptCore.AxisByName(StickX), sy = NavPromptCore.AxisByName(StickY);
                if (Mathf.Abs(sx) > 0.6f) dx = sx > 0f ? 1 : -1;
                if (Mathf.Abs(sy) > 0.6f) dy = sy > 0f ? -1 : 1;
            }
            if (dx == 0) dx = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            if (dy == 0) dy = (Input.GetKey(KeyCode.DownArrow) ? 1 : 0) - (Input.GetKey(KeyCode.UpArrow) ? 1 : 0);
        }
    }
}
