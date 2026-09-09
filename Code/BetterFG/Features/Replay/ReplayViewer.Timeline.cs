using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using BetterFG.Nametag;
using BetterFG.Network;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.UI.Tabs;
using BetterFG.Utilities;
using Cinemachine;
using FallGuysLib.Camera;
using FallGuysLib.NPC;
using FG.Common;
using FG.Common.Fraggle;
using FGClient;
using MPG.Utility;
using UnityEngine;
using Wushu.Integration;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    public partial class ReplayViewer
    {
#if MACHINIMA
        public bool Machinima => _rec != null && _rec.isMachinima;

        float PlayerLanesH =>
            Machinima && _solo == null ? VisiblePlayerRows * (LANE_H + LANE_GAP) : 0f;
#else
        public bool Machinima => false;

        float PlayerLanesH => 0f;
#endif

        ReplayPlayer _solo;

        float PlayerLanesTop => LANES_Y + LANE_COUNT * (LANE_H + LANE_GAP);

        float PlayerLanesBottom => LANES_BOTTOM + PlayerLanesH;

        float ZoomBarY => ZOOMBAR_Y + PlayerLanesH;

        float TimelineH => TIMELINE_H + PlayerLanesH;

        string LaneName(int lane)
        {
#if MACHINIMA
            if (_solo != null)
                return lane == LANE_COUNT - 1 ? "machinima.lane_action"
                    : lane == LANE_COUNT - 2 ? "machinima.lane_movement" : "";
#endif
            return lane == LANE_COUNT - 1 ? "ui.camera"
                : lane == LANE_COUNT - 2 ? "ui.lane_postfx" : "ui.lane_visibility";
        }

        void BuildWindow()
        {
            var win = UGUIShip.CreatePanel(_canvas.transform, new Rect(MARGIN, MARGIN, WIN_W, WIN_H), Color.clear, "ReplayWindow");
            _windowRt = win;
            ReplayWindowKit.MainBackdrop(win);
            ReplayWindowKit.Title(win, WIN_W, "REPLAY", TITLE_POS);
            var titleLabel = win.GetComponentInChildren<Text>();
            if (titleLabel != null) titleLabel.transform.localScale *= 1.15f;
            UGUIShip.CreateButton(win, new Rect(WIN_W - 30f, 3f, 26f, 20f), "<<", BTN_DARK, Color.white, UIScale.FS_SM, new Action(Minimize));

            _restoreBtn = UGUIShip.CreateButton(_canvas.transform, new Rect(2f, 2f, 34f, 20f), ">>",
                BTN_DARK, Color.white, UIScale.FS_SM, new Action(Restore));
            _restoreBtn.gameObject.SetActive(false);

            _doneBtn = UGUIShip.CreateButton(_canvas.transform, new Rect(2f, 2f, 62f, 20f), "ui.done",
                BTN_GREEN, Color.white, UIScale.FS_SM, new Action(EndCameraEdit));
            _doneBtn.gameObject.SetActive(false);

            float y = 26f + PAD;
            _info = UGUIShip.CreateLabel(win, new Rect(PAD, y, WIN_W - PAD * 2f, ROW * 4f), "", UIScale.FS_SM, HINT, TextAnchor.UpperLeft);
            y += ROW * 4f + 2f;

            float labelW = ReplayWindowKit.LABEL_W;
            UGUIShip.CreateLabel(win, new Rect(PAD, y, labelW, ROW), "ui.name", UIScale.FS_SM, HINT);
            var nameField = UGUIShip.CreateInputField(win, new Rect(PAD + labelW, y, WIN_W - PAD * 2f - labelW, ROW),
                "ui.replay_name", new Color(0f, 0f, 0f, 0.55f), Color.white, UIScale.FS_SM);
            nameField.text = ReplayName();
            nameField.onEndEdit.AddListener(new Action<string>(OnNameChanged));
            y += ROW + PAD;

            WindowRow(win, y, 0, "ui.save", new Action(SaveAs)); y += ROW;
            WindowRow(win, y, 1, "ui.load", new Action(LoadFrom)); y += ROW;
            WindowRow(win, y, 2, "ui.export", new Action(OpenExportWindow)); y += ROW;
            WindowRow(win, y, 3, "ui.controls", new Action(OpenControlsWindow)); y += ROW;
            WindowRow(win, y, 4, "ui.exit", new Action(Exit)); y += ROW;

            win.sizeDelta = new Vector2(WIN_W, y + PAD);
        }

        static void WindowRow(RectTransform win, float y, int row, string label, Action onClick)
        {
            ReplayWindowKit.Stripe(win, y, WIN_W, row);
            UGUIShip.CreateButton(win, new Rect(PAD, y, WIN_W - PAD * 2f, ROW), label,
                ReplayWindowKit.PICKABLE, Color.white, UIScale.FS_SM, onClick);
        }

        void BuildTimeline()
        {
            float w = UIScaleService.CurrentRef.x - MARGIN * 2f;
            var bar = UGUIShip.CreatePanel(_canvas.transform, new Rect(0f, 0f, w, TimelineH), Color.clear, "ReplayTimeline");
            _timelineRt = bar;
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(0f, 0f);
            bar.pivot = new Vector2(0f, 0f);
            bar.anchoredPosition = new Vector2(MARGIN, TIMELINE_Y);
            bar.sizeDelta = new Vector2(w, TimelineH);
            BuildTimelineBackdrop(bar, w);

            _playLabel = UGUIShip.CreateLabel(bar, new Rect(PAD, PAD, 200f, HEADER_H), "", UIScale.FS_SM, HINT);
            _clock = UGUIShip.CreateLabel(bar, new Rect(w - 190f - PAD, PAD, 190f, HEADER_H), "", UIScale.FS, Color.white, TextAnchor.MiddleRight);
            BuildPlayPauseButton(bar, w);
            BuildSnapButton(bar, w);

            _trackLeft = PAD + LANE_NAME_W;
            float trackW = w - _trackLeft - PAD;
            _trackWidth = trackW;
            _viewStart = 0f;
            _viewSpan = _rec.duration;
            NormaliseTrim();

            var scrub = UGUIShip.CreatePanel(bar, new Rect(_trackLeft, SCRUB_Y, trackW, SCRUB_H), new Color(1f, 1f, 1f, 0.08f), "ScrubBand");
            if (_stripeSprite == null) _stripeSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.stripe.png", 400f);
            var scrubImg = scrub.GetComponent<Image>();
            scrubImg.sprite = _stripeSprite;
            scrubImg.type = Image.Type.Tiled;
            scrubImg.raycastTarget = false;

            var rulerGo = new GameObject("RulerTicks");
            _rulerRoot = rulerGo.AddComponent<RectTransform>();
            _rulerRoot.SetParent(bar, false);
            UGUIShip.SetPixelRect(_rulerRoot, new Rect(0f, 0f, w, TimelineH));
            _rulerLabels.Clear();
            _rulerTicks.Clear();
            _minorTicks.Clear();
            RefreshRuler();

            for (int i = 0; i < LANE_COUNT; i++)
            {
                float y = LANES_Y + i * (LANE_H + LANE_GAP);
                string name = LaneName(i);
                if (string.IsNullOrEmpty(name)) continue;

                UGUIShip.CreatePanel(bar, new Rect(_trackLeft, y, trackW, LANE_H), LANE_BG, "Lane").GetComponent<Image>().raycastTarget = false;
                UGUIShip.CreateLabel(bar, new Rect(PAD, y, LANE_NAME_W - 6f, LANE_H),
                    name, UIScale.FS_SM - 1, HINT, TextAnchor.MiddleRight);
            }

#if MACHINIMA
            BuildMachinimaLanes(bar, w, trackW);
#endif

            _dimLeft = UGUIShip.CreatePanel(bar, new Rect(_trackLeft, LANES_Y, 0f, LANES_H), OUTSIDE_DIM, "OutsideLeft");
            _dimLeft.GetComponent<Image>().raycastTarget = false;
            _dimRight = UGUIShip.CreatePanel(bar, new Rect(_trackLeft, LANES_Y, 0f, LANES_H), OUTSIDE_DIM, "OutsideRight");
            _dimRight.GetComponent<Image>().raycastTarget = false;

            var cutOverlayGo = new GameObject("CutOverlays");
            _cutOverlayRoot = cutOverlayGo.AddComponent<RectTransform>();
            _cutOverlayRoot.SetParent(bar, false);
            UGUIShip.SetPixelRect(_cutOverlayRoot, new Rect(0f, 0f, w, LANES_BOTTOM));
            _cutOverlays.Clear();

            var ticksGo = new GameObject("KeyframeTicks");
            _keyTicks = ticksGo.AddComponent<RectTransform>();
            _keyTicks.SetParent(bar, false);
            UGUIShip.SetPixelRect(_keyTicks, new Rect(0f, 0f, w, LANES_BOTTOM));

            var visTicksGo = new GameObject("VisibilityKeyframeTicks");
            _visTicks = visTicksGo.AddComponent<RectTransform>();
            _visTicks.SetParent(bar, false);
            UGUIShip.SetPixelRect(_visTicks, new Rect(0f, 0f, w, LANES_BOTTOM));

            var fxTicksGo = new GameObject("PostFxKeyframeTicks");
            _fxTicks = fxTicksGo.AddComponent<RectTransform>();
            _fxTicks.SetParent(bar, false);
            UGUIShip.SetPixelRect(_fxTicks, new Rect(0f, 0f, w, LANES_BOTTOM));

            var zoomTrackBg = UGUIShip.CreatePanel(bar, new Rect(_trackLeft, ZoomBarY, trackW, ZOOMBAR_H),
                new Color(0f, 0f, 0f, 0.2f), "ZoomTrack");
            zoomTrackBg.GetComponent<Image>().raycastTarget = false;

            var zoomFill = UGUIShip.CreateButton(bar, new Rect(_trackLeft, ZoomBarY, trackW, ZOOMBAR_H),
                "", new Color(EDGE_BLUE.r, EDGE_BLUE.g, EDGE_BLUE.b, 0.85f), Color.clear, 1, null, skipHoverSound: true, customSprite: true);
            _zoomFillRt = zoomFill.GetComponent<RectTransform>();
            zoomFill.GetComponent<Image>().raycastTarget = false;
            RefreshZoomBar();

            if (_playheadFill == null)
            {
                _playheadFill = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.playhead_fill.png");
                _playheadOutline = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.playhead_outline.png");
                _edgeFill = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.edgemarker_fill.png");
                _edgeOutline = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.edgemarker_outline.png");
            }

            _inMarker = BuildMarker(bar, "TrimIn", EDGE_W, EDGE_H, _edgeOutline, _edgeFill, out _inFill);
            _outMarker = BuildMarker(bar, "TrimOut", EDGE_W, EDGE_H, _edgeOutline, _edgeFill, out _outFill);
            _outMarker.localScale = new Vector3(-1f, 1f, 1f);

            _marker = BuildMarker(bar, "Playhead", PH_W, PH_H, _playheadOutline, _playheadFill, out _playheadImg);
        }

        void BuildTimelineBackdrop(RectTransform bar, float w)
        {
            if (_timelineBg == null)
            {
                var tex = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.replay.timeline.png");
                if (tex == null) return;

                _timelineBg = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), Vector2.zero,
                    100f / BG_SCALE, 0, SpriteMeshType.FullRect,
                    new Vector4(BG_BORDER_L, BG_BORDER_B, BG_BORDER_R, BG_BORDER_T));
                _timelineBg.hideFlags = HideFlags.HideAndDontSave;
            }

            var rt = UGUIShip.CreatePanel(bar, new Rect(0f, -BG_OVERHANG, w, TimelineH + BG_OVERHANG), Color.white, "Backdrop");
            var img = rt.GetComponent<Image>();
            img.sprite = _timelineBg;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = BG_PPU_MULT;
            img.raycastTarget = false;
            rt.SetAsFirstSibling();
        }

        void BuildPlayPauseButton(RectTransform bar, float w)
        {
            if (_playFillSprite == null)
            {
                _playFillSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.play_fill.png");
                _playOutlineSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.play_outline.png");
                _pauseFillSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.pause_fill.png");
                _pauseOutlineSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.pause_outline.png");
            }

            float x = w * 0.5f - PLAYPAUSE_SIZE * 0.5f;
            float y = PAD + (HEADER_H - PLAYPAUSE_SIZE) * 0.5f;
            var root = UGUIShip.CreatePanel(bar, new Rect(x, y, PLAYPAUSE_SIZE, PLAYPAUSE_SIZE), Color.clear, "PlayPause");

            var outlineRt = UGUIShip.CreatePanel(root, new Rect(0f, 0f, PLAYPAUSE_SIZE, PLAYPAUSE_SIZE), KEY_OUTLINE, "Outline");
            _playPauseOutline = outlineRt.GetComponent<Image>();
            _playPauseOutline.raycastTarget = false;

            var fillRt = UGUIShip.CreatePanel(root, new Rect(0f, 0f, PLAYPAUSE_SIZE, PLAYPAUSE_SIZE), KEY_IDLE, "Fill");
            _playPauseFill = fillRt.GetComponent<Image>();
            _playPauseFill.raycastTarget = false;

            var rootImg = root.GetComponent<Image>();
            rootImg.raycastTarget = true;
            var btn = root.gameObject.AddComponent<Button>();
            btn.targetGraphic = rootImg;
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            btn.onClick.AddListener(new Action(TogglePause));

            UGUIShip.WireButtonAudio(root.gameObject);
            var trigger = root.GetComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(new Action<BaseEventData>(_ => _playPauseFill.color = Color.white));
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(new Action<BaseEventData>(_ => _playPauseFill.color = KEY_IDLE));
            trigger.triggers.Add(exit);

            RefreshPlayPauseIcon();
        }

        void RefreshPlayPauseIcon()
        {
            if (_playPauseFill == null) return;
            _playPauseFill.sprite = _paused ? _playFillSprite : _pauseFillSprite;
            _playPauseOutline.sprite = _paused ? _playOutlineSprite : _pauseOutlineSprite;
        }

        void BuildSnapButton(RectTransform bar, float w)
        {
            if (_snapFillSprite == null)
            {
                _snapFillSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.snap_fill.png");
                _snapOutlineSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.snap_outline.png");
                _unsnapFillSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.unsnap_fill.png");
                _unsnapOutlineSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.unsnap_outline.png");
            }

            float x = w * 0.5f + PLAYPAUSE_SIZE * 0.5f + SNAP_BTN_GAP;
            float y = PAD + (HEADER_H - PLAYPAUSE_SIZE) * 0.5f;
            var root = UGUIShip.CreatePanel(bar, new Rect(x, y, PLAYPAUSE_SIZE, PLAYPAUSE_SIZE), Color.clear, "SnapToggle");

            var outlineRt = UGUIShip.CreatePanel(root, new Rect(0f, 0f, PLAYPAUSE_SIZE, PLAYPAUSE_SIZE), KEY_OUTLINE, "Outline");
            _snapOutline = outlineRt.GetComponent<Image>();
            _snapOutline.raycastTarget = false;

            var fillRt = UGUIShip.CreatePanel(root, new Rect(0f, 0f, PLAYPAUSE_SIZE, PLAYPAUSE_SIZE), KEY_IDLE, "Fill");
            _snapFill = fillRt.GetComponent<Image>();
            _snapFill.raycastTarget = false;

            var rootImg = root.GetComponent<Image>();
            rootImg.raycastTarget = true;
            var btn = root.gameObject.AddComponent<Button>();
            btn.targetGraphic = rootImg;
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            btn.onClick.AddListener(new Action(ToggleSnap));

            UGUIShip.WireButtonAudio(root.gameObject);
            var trigger = root.GetComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(new Action<BaseEventData>(_ => _snapFill.color = Color.white));
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(new Action<BaseEventData>(_ => _snapFill.color = KEY_IDLE));
            trigger.triggers.Add(exit);

            RefreshSnapIcon();
        }

        void RefreshSnapIcon()
        {
            if (_snapFill == null) return;
            _snapFill.sprite = _snapToKeyframes ? _snapFillSprite : _unsnapFillSprite;
            _snapOutline.sprite = _snapToKeyframes ? _snapOutlineSprite : _unsnapOutlineSprite;
        }

        void ToggleSnap()
        {
            _snapToKeyframes = !_snapToKeyframes;
            RefreshSnapIcon();
            Plugin.Log.LogInfo(_snapToKeyframes ? "keyframe snap on" : "keyframe snap off");
        }

        float RulerStep()
        {
            float rawStep = ViewSpan / RULER_TARGET_TICKS;
            foreach (var s in RULER_STEPS)
                if (s >= rawStep) return s;
            return RULER_STEPS[RULER_STEPS.Length - 1];
        }

        void RefreshRuler()
        {
            float step = RulerStep();
            float viewEnd = _viewStart + ViewSpan;
            float first = Mathf.Ceil(_viewStart / step) * step;

            int needed = 0;
            for (float t = first; t <= viewEnd + 0.0001f && needed < 64; t += step) needed++;

            while (_rulerTicks.Count < needed)
            {
                var root = BuildRulerTick(out var label);
                _rulerTicks.Add(root);
                _rulerLabels.Add(label);
            }
            for (int i = 0; i < _rulerTicks.Count; i++)
                _rulerTicks[i].gameObject.SetActive(i < needed);

            int idx = 0;
            for (float t = first; idx < needed; t += step, idx++)
            {
                _rulerTicks[idx].anchoredPosition = new Vector2(TrackX(t), 0f);
                _rulerLabels[idx].text = Stamp(t);
            }

            float minorStep = step / MINOR_DIVISIONS;
            float minorFirst = Mathf.Ceil(_viewStart / minorStep) * minorStep;

            int neededMinor = 0;
            for (float t = minorFirst; t <= viewEnd + 0.0001f && neededMinor < 320; t += minorStep)
            {
                if (OnStep(t, step)) continue;
                neededMinor++;
            }

            while (_minorTicks.Count < neededMinor) _minorTicks.Add(BuildMinorTick());
            for (int i = 0; i < _minorTicks.Count; i++)
                _minorTicks[i].gameObject.SetActive(i < neededMinor);

            int midx = 0;
            int guard = 0;
            for (float t = minorFirst; t <= viewEnd + 0.0001f && midx < neededMinor && guard < 2000; t += minorStep, guard++)
            {
                if (OnStep(t, step)) continue;
                _minorTicks[midx].anchoredPosition = new Vector2(TrackX(t), -SCRUB_Y);
                midx++;
            }

            RefreshZoomBar();
        }

        void RefreshZoomBar()
        {
            if (_zoomFillRt == null) return;

            float duration = Mathf.Max(_rec.duration, 0.001f);
            float leftF = Mathf.Clamp01(_viewStart / duration);
            float rightF = Mathf.Clamp01((_viewStart + ViewSpan) / duration);
            float left = _trackLeft + _trackWidth * leftF;
            float width = Mathf.Max(_trackWidth * (rightF - leftF), 4f);
            UGUIShip.SetPixelRect(_zoomFillRt, new Rect(left, ZoomBarY, width, ZOOMBAR_H));
        }

        void ZoomTimeline(float notches, float pivot)
        {
            float duration = Mathf.Max(_rec.duration, MIN_VIEW_SPAN);
            float anchor = Mathf.Clamp01(TimeToFraction(pivot));
            _viewSpan = Mathf.Clamp(ViewSpan * Mathf.Pow(0.82f, notches), MIN_VIEW_SPAN, duration);
            _viewStart = Mathf.Clamp(pivot - anchor * _viewSpan, 0f, duration - _viewSpan);
            RefreshRuler();
        }

        void PanTimeline(float notches)
        {
            float duration = Mathf.Max(_rec.duration, MIN_VIEW_SPAN);
            if (ViewSpan >= duration) return;
            _viewStart = Mathf.Clamp(_viewStart - notches * ViewSpan * 0.15f, 0f, duration - ViewSpan);
            RefreshRuler();
        }

        void FollowPlayhead()
        {
            if (_draggingZoomLeft || _draggingZoomRight || _draggingZoomPan) return;
            if (ViewSpan >= _rec.duration) return;
            if (_time >= _viewStart && _time <= _viewStart + ViewSpan) return;
            _viewStart = Mathf.Clamp(_time - ViewSpan * 0.5f, 0f, _rec.duration - ViewSpan);
            RefreshRuler();
        }

        void RefreshKeyframeTicks()
        {
            if (_keyTicks.childCount != _rec.keyframes.Count)
            {
                for (int i = _keyTicks.childCount - 1; i >= 0; i--)
                    DestroyImmediate(_keyTicks.GetChild(i).gameObject);

                for (int i = 0; i < _rec.keyframes.Count; i++)
                    BuildKeyframeTick();
            }

            var editing = ReplayKeyframeWindow.Instance?.Keyframe;
            var hovered = HoveredKeyframe();
            for (int i = 0; i < _rec.keyframes.Count && i < _keyTicks.childCount; i++)
            {
                var k = _rec.keyframes[i];
                float f = TimeToFraction(k.time);

                var rt = _keyTicks.GetChild(i).GetComponent<RectTransform>();
                bool onScreen = f >= -0.01f && f <= 1.01f;
                if (rt.gameObject.activeSelf != onScreen) rt.gameObject.SetActive(onScreen);
                if (!onScreen) continue;

                rt.anchoredPosition = new Vector2(_trackLeft + _trackWidth * f - KEY_W * 0.5f, -KEYROW_Y);

                var fill = rt.GetChild(0).GetComponent<Image>();
                fill.color = k == hovered ? Color.white
                    : k == editing ? KEY_EDITING
                    : _selected.Contains(k) ? KEY_SELECTED
                    : KEY_IDLE;

                var outline = rt.GetChild(1).GetComponent<Image>();
                outline.color = k.cutToNext ? KEY_CUT_OUTLINE : KEY_OUTLINE;
            }
        }

        void RefreshVisibilityTicks()
        {
            if (_visTicks.childCount != _rec.visibilityKeyframes.Count)
            {
                for (int i = _visTicks.childCount - 1; i >= 0; i--)
                    DestroyImmediate(_visTicks.GetChild(i).gameObject);

                for (int i = 0; i < _rec.visibilityKeyframes.Count; i++)
                    BuildKeyframeTick(_visTicks);
            }

            var editing = ReplayVisibilityKeyframeWindow.Instance?.Keyframe;
            var hovered = HoveredVisKeyframe();
            for (int i = 0; i < _rec.visibilityKeyframes.Count && i < _visTicks.childCount; i++)
            {
                var k = _rec.visibilityKeyframes[i];
                float f = TimeToFraction(k.time);

                var rt = _visTicks.GetChild(i).GetComponent<RectTransform>();
                bool onScreen = f >= -0.01f && f <= 1.01f;
                if (rt.gameObject.activeSelf != onScreen) rt.gameObject.SetActive(onScreen);
                if (!onScreen) continue;

                rt.anchoredPosition = new Vector2(_trackLeft + _trackWidth * f - KEY_W * 0.5f, -VIS_KEYROW_Y);

                var fill = rt.GetChild(0).GetComponent<Image>();
                fill.color = k == hovered ? Color.white
                    : k == editing ? KEY_EDITING
                    : _selectedVis.Contains(k) ? KEY_SELECTED
                    : KEY_IDLE;
            }
        }

        void RefreshPostFxTicks()
        {
            if (_fxTicks.childCount != _rec.postFxKeyframes.Count)
            {
                for (int i = _fxTicks.childCount - 1; i >= 0; i--)
                    DestroyImmediate(_fxTicks.GetChild(i).gameObject);

                for (int i = 0; i < _rec.postFxKeyframes.Count; i++)
                    BuildKeyframeTick(_fxTicks);
            }

            var editing = ReplayPostFxKeyframeWindow.Instance?.Keyframe;
            var hovered = HoveredFxKeyframe();
            for (int i = 0; i < _rec.postFxKeyframes.Count && i < _fxTicks.childCount; i++)
            {
                var k = _rec.postFxKeyframes[i];
                float f = TimeToFraction(k.time);

                var rt = _fxTicks.GetChild(i).GetComponent<RectTransform>();
                bool onScreen = f >= -0.01f && f <= 1.01f;
                if (rt.gameObject.activeSelf != onScreen) rt.gameObject.SetActive(onScreen);
                if (!onScreen) continue;

                rt.anchoredPosition = new Vector2(_trackLeft + _trackWidth * f - KEY_W * 0.5f, -FX_KEYROW_Y);

                var fill = rt.GetChild(0).GetComponent<Image>();
                fill.color = k == hovered ? Color.white
                    : k == editing ? KEY_EDITING
                    : _selectedFx.Contains(k) ? KEY_SELECTED
                    : KEY_IDLE;
            }
        }

        void RefreshCutOverlays()
        {
            int needed = 0;
            for (int i = 0; i < _rec.keyframes.Count; i++)
                if (_rec.keyframes[i].cutToNext) needed++;

            while (_cutOverlays.Count < needed) _cutOverlays.Add(BuildCutOverlay());
            for (int i = 0; i < _cutOverlays.Count; i++)
                _cutOverlays[i].gameObject.SetActive(i < needed);

            int idx = 0;
            for (int i = 0; i < _rec.keyframes.Count && idx < needed; i++)
            {
                var k = _rec.keyframes[i];
                if (!k.cutToNext) continue;

                float endTime = i + 1 < _rec.keyframes.Count ? _rec.keyframes[i + 1].time : _rec.duration;
                float x0 = TrackX(k.time);
                float x1 = TrackX(endTime);
                UGUIShip.SetPixelRect(_cutOverlays[idx], new Rect(Mathf.Min(x0, x1), LANES_Y, Mathf.Abs(x1 - x0), LANES_H));
                idx++;
            }
        }

        void BuildKeyframeTick() => BuildKeyframeTick(_keyTicks);

        void BuildKeyframeTick(RectTransform parent)
        {
            if (_fillSprite == null)
            {
                _fillSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.marker_fill.png");
                _outlineSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.marker_outline.png");
            }

            var tick = UGUIShip.CreatePanel(parent, new Rect(0f, KEYROW_Y, KEY_W, KEY_H), Color.clear, "Keyframe");
            tick.GetComponent<Image>().raycastTarget = false;

            var fill = UGUIShip.CreatePanel(tick, new Rect(0f, 0f, KEY_W, KEY_H), KEY_IDLE, "Fill");
            var fillImg = fill.GetComponent<Image>();
            fillImg.sprite = _fillSprite;
            fillImg.raycastTarget = false;

            var outline = UGUIShip.CreatePanel(tick, new Rect(0f, 0f, KEY_W, KEY_H), KEY_OUTLINE, "Outline");
            var outlineImg = outline.GetComponent<Image>();
            outlineImg.sprite = _outlineSprite;
            outlineImg.raycastTarget = false;
        }

        void NormaliseTrim()
        {
            if (_rec.trimEnd <= 0f || _rec.trimEnd > _rec.duration) _rec.trimEnd = _rec.duration;
            _rec.trimStart = Mathf.Clamp(_rec.trimStart, 0f, Mathf.Max(0f, _rec.trimEnd - MIN_TRIM));
        }

        void PositionMarker()
        {
            _marker.anchoredPosition = new Vector2(TrackX(_time) - PH_W * PH_STEM, -SCRUB_Y);
            _playheadImg.color = _scrubbing || _snapGuideActive || NearMarker(_time, false) ? Color.white : PLAYHEAD_YELLOW;

            float inX = TrackX(_rec.trimStart);
            float outX = TrackX(_rec.trimEnd);
            _inMarker.anchoredPosition = new Vector2(inX - EDGE_W * EDGE_STEM, -EDGE_Y);
            _outMarker.anchoredPosition = new Vector2(outX + EDGE_W * EDGE_STEM, -EDGE_Y);
            _inFill.color = _draggingIn || NearMarker(_rec.trimStart, true) ? Color.white : EDGE_BLUE;
            _outFill.color = _draggingOut || NearMarker(_rec.trimEnd, true) ? Color.white : EDGE_BLUE;

            UGUIShip.SetPixelRect(_dimLeft, new Rect(_trackLeft, LANES_Y, inX - _trackLeft, LANES_H));
            UGUIShip.SetPixelRect(_dimRight, new Rect(outX, LANES_Y, _trackLeft + _trackWidth - outX, LANES_H));
        }

        bool NearMarker(float time, bool edgeBand)
        {
            if (_canvas == null) return false;
            var cursor = TimelineCursor();
            if (edgeBand ? !OverEdgeBand(cursor) : !OverScrub(cursor)) return false;
            return Mathf.Abs(cursor.x - TrackX(time)) <= GRAB_PX;
        }

        bool HandleTimelineMouse()
        {
            _hoverZoomEdge = _draggingZoomLeft || _draggingZoomRight;
            _snapGuideActive = false;

            if (_dragKeyframe == null && _dragVisKeyframe == null && _dragFxKeyframe == null && _contextMenu != null
                && RectTransformUtility.RectangleContainsScreenPoint(_contextMenu, Input.mousePosition, null))
                return true;

            var cursor = TimelineCursor();
            if (Input.GetMouseButtonDown(0)) _timelineFocused = OverTimeline(cursor);
            float time = TimeAt(cursor.x);

#if MACHINIMA
            if (Machinima && HandleMachinimaDrag()) return true;
#endif

            if (_dragFxKeyframe != null)
            {
                if (Input.GetMouseButton(0))
                {
                    if (!_dragMovedFx && Mathf.Abs(Input.mousePosition.x - _dragGrabMouseXFx) < DRAG_DEADZONE) return true;
                    _dragMovedFx = true;
                    MoveSelectionFx(time - _dragGrabTimeFx);
                }
                else
                {
                    if (_dragMovedFx)
                        Plugin.Log.LogInfo(_selectedFx.Count > 1
                            ? $"shifted {_selectedFx.Count} post fx keyframes, the one you grabbed sits at {Stamp(_dragFxKeyframe.time)} now"
                            : $"moved a post fx keyframe to {Stamp(_dragFxKeyframe.time)}");
                    _dragFxKeyframe = null;
                    _dragMovedFx = false;
                }
                return true;
            }

            if (_dragVisKeyframe != null)
            {
                if (Input.GetMouseButton(0))
                {
                    if (!_dragMovedVis && Mathf.Abs(Input.mousePosition.x - _dragGrabMouseXVis) < DRAG_DEADZONE) return true;
                    _dragMovedVis = true;
                    MoveSelectionVis(time - _dragGrabTimeVis);
                }
                else
                {
                    if (_dragMovedVis)
                        Plugin.Log.LogInfo(_selectedVis.Count > 1
                            ? $"shifted {_selectedVis.Count} visibility keyframes, the one you grabbed sits at {Stamp(_dragVisKeyframe.time)} now"
                            : $"moved a visibility keyframe to {Stamp(_dragVisKeyframe.time)}");
                    _dragVisKeyframe = null;
                    _dragMovedVis = false;
                }
                return true;
            }

            if (_dragKeyframe != null)
            {
                if (Input.GetMouseButton(0))
                {
                    if (!_dragMoved && Mathf.Abs(Input.mousePosition.x - _dragGrabMouseX) < DRAG_DEADZONE) return true;
                    _dragMoved = true;
                    _clickedKeyframe = null;
                    MoveSelection(time - _dragGrabTime);
                }
                else
                {
                    if (_dragMoved)
                        Plugin.Log.LogInfo(_selected.Count > 1
                            ? $"shifted {_selected.Count} keyframes, the one you grabbed sits at {Stamp(_dragKeyframe.time)} now"
                            : $"moved a keyframe to {Stamp(_dragKeyframe.time)}");
                    _dragKeyframe = null;
                    _dragMoved = false;
                }
                return true;
            }

            if (_draggingIn || _draggingOut)
            {
                if (Input.GetMouseButton(0)) DragTrim(time);
                else
                {
                    Plugin.Log.LogInfo($"composition is {Stamp(_rec.trimStart)} to {Stamp(_rec.trimEnd)} now");
                    _draggingIn = _draggingOut = false;
                }
                return true;
            }

            if (_draggingZoomLeft || _draggingZoomRight)
            {
                if (Input.GetMouseButton(0)) DragZoomEdge(cursor.x);
                else
                {
                    Plugin.Log.LogInfo($"view zoomed to {Stamp(_viewStart)}-{Stamp(_viewStart + ViewSpan)}");
                    _draggingZoomLeft = _draggingZoomRight = false;
                }
                return true;
            }

            if (_draggingZoomPan)
            {
                if (Input.GetMouseButton(0)) DragZoomPan(cursor.x);
                else
                {
                    Plugin.Log.LogInfo($"view panned to {Stamp(_viewStart)}-{Stamp(_viewStart + ViewSpan)}");
                    _draggingZoomPan = false;
                }
                return true;
            }

            if (_scrubbing)
            {
                if (Input.GetMouseButton(0)) SeekTo(time);
                else _scrubbing = false;
                return true;
            }

            if (_marqueeing)
            {
                if (Input.GetMouseButton(0)) DragMarquee(cursor);
                else EndMarquee();
                return true;
            }

            if (_marqueeingVis)
            {
                if (Input.GetMouseButton(0)) DragMarqueeVis(cursor);
                else EndMarqueeVis();
                return true;
            }

            if (_marqueeingFx)
            {
                if (Input.GetMouseButton(0)) DragMarqueeFx(cursor);
                else EndMarqueeFx();
                return true;
            }

            if (!OverTimeline(cursor)) return false;

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.01f && ShiftHeld()) scroll = Input.mouseScrollDelta.x;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                if (CtrlHeld()) ZoomTimeline(scroll, time);
                else PanTimeline(scroll);
                SyncUi();
                return true;
            }

            if (OverZoomBar(cursor))
            {
                float edgeL = ZoomEdgeLeftX();
                float edgeR = ZoomEdgeRightX();
                bool nearLeft = Mathf.Abs(cursor.x - edgeL) <= GRAB_PX;
                bool nearRight = !nearLeft && Mathf.Abs(cursor.x - edgeR) <= GRAB_PX;
                bool overFill = !nearLeft && !nearRight && cursor.x >= edgeL && cursor.x <= edgeR;
                _hoverZoomEdge = nearLeft || nearRight;

                if (Input.GetMouseButtonDown(0))
                {
                    if (nearLeft) _draggingZoomLeft = true;
                    else if (nearRight) _draggingZoomRight = true;
                    else if (overFill)
                    {
                        _draggingZoomPan = true;
                        _dragZoomPanGrabX = cursor.x;
                        _dragZoomPanStartViewStart = _viewStart;
                    }
                }
                return true;
            }

            if (OverEdgeBand(cursor))
            {
                if (!Input.GetMouseButtonDown(0)) return true;

                CloseContextMenu();
                Deselect();

                float inGap = Mathf.Abs(cursor.x - TrackX(_rec.trimStart));
                float outGap = Mathf.Abs(cursor.x - TrackX(_rec.trimEnd));
                if (inGap <= GRAB_PX && inGap <= outGap) _draggingIn = true;
                else if (outGap <= GRAB_PX) _draggingOut = true;
                return true;
            }

            if (OverScrub(cursor))
            {
                if (Input.GetMouseButtonDown(0))
                {
                    CloseContextMenu();
                    Deselect();
                    _scrubbing = true;
                    SeekTo(time);
                    return true;
                }
                if (Input.GetMouseButtonDown(1))
                {
                    CloseContextMenu();
                    Deselect();
                    SeekTo(time);
                    ShowContextMenu(cursor);
                    return true;
                }
                return false;
            }

#if MACHINIMA
            if (Machinima)
            {
                if (_solo != null) return HandleSoloRows(cursor, time);
                if (cursor.y >= PlayerLanesTop && cursor.y < PlayerLanesBottom) return HandlePlayerLaneRow(cursor, time);
            }
#endif

            if (OverVisRow(cursor)) return HandleVisibilityKeyRow(cursor, time);
            if (OverFxRow(cursor)) return HandleFxKeyRow(cursor, time);
            if (!OverCameraRow(cursor)) return false;

            if (Input.GetMouseButtonDown(0))
            {
                CloseContextMenu();
                var k = KeyframeNear(time);
                if (k == null)
                {
                    BeginMarquee(cursor);
                    return true;
                }

                if (ShiftHeld())
                {
                    if (_selected.Contains(k)) { _selected.Remove(k); return true; }
                    UGUIShip.PlaySelectSound();
                    _selected.Add(k);
                }
                else
                {
                    if (k == _clickedKeyframe && Time.realtimeSinceStartup - _clickedAt <= DOUBLE_CLICK)
                    {
                        _clickedKeyframe = null;
                        SelectOnly(k);
                        SeekTo(k.time);
                        OpenKeyframeWindow(k);
                        BeginCameraEdit(k);
                        return true;
                    }

                    _clickedKeyframe = k;
                    _clickedAt = Time.realtimeSinceStartup;

                    if (_selected.Count <= 1)
                    {
                        SelectOnly(k);
                        SeekTo(k.time);
                        OpenKeyframeWindow(k);
                    }
                }

                _dragKeyframe = k;
                _dragGrabTime = time;
                _dragGrabMouseX = Input.mousePosition.x;
                _dragMoved = false;
                _dragStartTimes.Clear();
                foreach (var sel in _selected) _dragStartTimes.Add(sel.time);
                return true;
            }

            if (Input.GetMouseButtonDown(1))
            {
                CloseContextMenu();
                var k = KeyframeNear(time);
                if (k != null)
                {
                    if (!_selected.Contains(k)) SelectOnly(k);
                    ShowKeyframeMenu(k, cursor);
                }
                else
                {
                    SeekTo(time);
                    ShowAddCameraMenu(cursor);
                }
                return true;
            }

            return false;
        }

        bool HandleVisibilityKeyRow(Vector2 cursor, float time)
        {
            if (Input.GetMouseButtonDown(0))
            {
                CloseContextMenu();
                var k = VisKeyframeNear(time);
                if (k == null)
                {
                    BeginMarqueeVis(cursor);
                    return true;
                }

                if (ShiftHeld())
                {
                    if (_selectedVis.Contains(k)) { _selectedVis.Remove(k); return true; }
                    _selectedVis.Add(k);
                }
                else
                {
                    if (_selectedVis.Count <= 1)
                    {
                        SelectOnlyVis(k);
                        SeekTo(k.time);
                        OpenVisibilityKeyframeWindow(k);
                    }
                }

                _dragVisKeyframe = k;
                _dragGrabTimeVis = time;
                _dragGrabMouseXVis = Input.mousePosition.x;
                _dragMovedVis = false;
                _dragStartTimesVis.Clear();
                foreach (var sel in _selectedVis) _dragStartTimesVis.Add(sel.time);
                return true;
            }

            if (Input.GetMouseButtonDown(1))
            {
                CloseContextMenu();
                var k = VisKeyframeNear(time);
                if (k != null)
                {
                    if (!_selectedVis.Contains(k)) SelectOnlyVis(k);
                    ShowVisKeyframeMenu(k, cursor);
                }
                else
                {
                    SeekTo(time);
                    ShowAddVisMenu(cursor);
                }
                return true;
            }

            return false;
        }

        bool HandleFxKeyRow(Vector2 cursor, float time)
        {
            if (Input.GetMouseButtonDown(0))
            {
                CloseContextMenu();
                var k = FxKeyframeNear(time);
                if (k == null)
                {
                    BeginMarqueeFx(cursor);
                    return true;
                }

                if (ShiftHeld())
                {
                    if (_selectedFx.Contains(k)) { _selectedFx.Remove(k); return true; }
                    _selectedFx.Add(k);
                }
                else
                {
                    if (_selectedFx.Count <= 1)
                    {
                        SelectOnlyFx(k);
                        SeekTo(k.time);
                        OpenPostFxKeyframeWindow(k);
                    }
                }

                _dragFxKeyframe = k;
                _dragGrabTimeFx = time;
                _dragGrabMouseXFx = Input.mousePosition.x;
                _dragMovedFx = false;
                _dragStartTimesFx.Clear();
                foreach (var sel in _selectedFx) _dragStartTimesFx.Add(sel.time);
                return true;
            }

            if (Input.GetMouseButtonDown(1))
            {
                CloseContextMenu();
                var k = FxKeyframeNear(time);
                if (k != null)
                {
                    if (!_selectedFx.Contains(k)) SelectOnlyFx(k);
                    ShowFxKeyframeMenu(k, cursor);
                }
                else
                {
                    SeekTo(time);
                    ShowAddFxMenu(cursor);
                }
                return true;
            }

            return false;
        }

        void Deselect()
        {
            _selected.Clear();
            _selectedVis.Clear();
            _selectedFx.Clear();
#if MACHINIMA
            _selKeys.Clear();
#endif
            ReplayKeyframeWindow.Instance?.Close();
            ReplayVisibilityKeyframeWindow.Instance?.Close();
            ReplayPostFxKeyframeWindow.Instance?.Close();
#if MACHINIMA
            MachinimaKeyWindow.Instance?.Close();
            MachinimaPlayerWindow.Instance?.Close();
#endif
        }

        void BeginMarqueeVis(Vector2 cursor)
        {
            _marqueeingVis = true;
            _marqueeAnchorVis = cursor;
            _marqueeBaseVis.Clear();
            if (ShiftHeld()) _marqueeBaseVis.AddRange(_selectedVis);
            else Deselect();
            _selectedVis.Clear();
            _selectedVis.AddRange(_marqueeBaseVis);

            _marqueeRtVis = UGUIShip.CreatePanel(_timelineRt, new Rect(cursor.x, cursor.y, 0f, 0f),
                new Color(1f, 0.83f, 0.25f, 0.16f), "MarqueeVis");
            _marqueeRtVis.GetComponent<Image>().raycastTarget = false;
        }

        void DragMarqueeVis(Vector2 cursor)
        {
            float left = Mathf.Min(_marqueeAnchorVis.x, cursor.x);
            float right = Mathf.Max(_marqueeAnchorVis.x, cursor.x);
            float top = Mathf.Min(_marqueeAnchorVis.y, cursor.y);
            float bottom = Mathf.Max(_marqueeAnchorVis.y, cursor.y);
            UGUIShip.SetPixelRect(_marqueeRtVis, new Rect(left, top, right - left, bottom - top));

            float from = TimeAt(left);
            float to = TimeAt(right);

            _selectedVis.Clear();
            _selectedVis.AddRange(_marqueeBaseVis);
            foreach (var k in _rec.visibilityKeyframes)
                if (k.time >= from && k.time <= to && !_selectedVis.Contains(k)) _selectedVis.Add(k);
        }

        void EndMarqueeVis()
        {
            _marqueeingVis = false;
            if (_marqueeRtVis != null) Destroy(_marqueeRtVis.gameObject);
            _marqueeRtVis = null;
            if (_selectedVis.Count > 0) Plugin.Log.LogInfo($"{_selectedVis.Count} visibility keyframes in the box");
        }

        void SelectOnlyVis(ReplayVisibilityKeyframe k)
        {
            _selectedVis.Clear();
            _selectedVis.Add(k);
        }

        void MoveSelectionVis(float delta)
        {
            float earliest = float.MaxValue;
            float latest = float.MinValue;
            foreach (float start in _dragStartTimesVis)
            {
                if (start < earliest) earliest = start;
                if (start > latest) latest = start;
            }
            delta = Mathf.Clamp(delta, -earliest, _rec.duration - latest);

            if (_snapToKeyframes)
            {
                int gi = _selectedVis.IndexOf(_dragVisKeyframe);
                float grabStart = gi >= 0 ? _dragStartTimesVis[gi] : _dragVisKeyframe.time - delta;
                float snapped = MagnetSnap(SnapFrame(grabStart + delta), null, _selectedVis, null, out bool didSnap);
                if (didSnap) _snapGuideActive = true;
                delta = snapped - grabStart;
            }
            else delta = SnapFrame(delta);

            for (int i = 0; i < _selectedVis.Count && i < _dragStartTimesVis.Count; i++)
                _selectedVis[i].time = _dragStartTimesVis[i] + delta;
            _rec.SortVisibilityKeyframes();

            if (_selectedVis.Count > 1) return;
            _time = _dragVisKeyframe.time;
            _freeLook = false;
        }

        void BeginMarqueeFx(Vector2 cursor)
        {
            _marqueeingFx = true;
            _marqueeAnchorFx = cursor;
            _marqueeBaseFx.Clear();
            if (ShiftHeld()) _marqueeBaseFx.AddRange(_selectedFx);
            else Deselect();
            _selectedFx.Clear();
            _selectedFx.AddRange(_marqueeBaseFx);

            _marqueeRtFx = UGUIShip.CreatePanel(_timelineRt, new Rect(cursor.x, cursor.y, 0f, 0f),
                new Color(1f, 0.83f, 0.25f, 0.16f), "MarqueeFx");
            _marqueeRtFx.GetComponent<Image>().raycastTarget = false;
        }

        void DragMarqueeFx(Vector2 cursor)
        {
            float left = Mathf.Min(_marqueeAnchorFx.x, cursor.x);
            float right = Mathf.Max(_marqueeAnchorFx.x, cursor.x);
            float top = Mathf.Min(_marqueeAnchorFx.y, cursor.y);
            float bottom = Mathf.Max(_marqueeAnchorFx.y, cursor.y);
            UGUIShip.SetPixelRect(_marqueeRtFx, new Rect(left, top, right - left, bottom - top));

            float from = TimeAt(left);
            float to = TimeAt(right);

            _selectedFx.Clear();
            _selectedFx.AddRange(_marqueeBaseFx);
            foreach (var k in _rec.postFxKeyframes)
                if (k.time >= from && k.time <= to && !_selectedFx.Contains(k)) _selectedFx.Add(k);
        }

        void EndMarqueeFx()
        {
            _marqueeingFx = false;
            if (_marqueeRtFx != null) Destroy(_marqueeRtFx.gameObject);
            _marqueeRtFx = null;
            if (_selectedFx.Count > 0) Plugin.Log.LogInfo($"{_selectedFx.Count} post fx keyframes in the box");
        }

        void SelectOnlyFx(ReplayPostFxKeyframe k)
        {
            _selectedFx.Clear();
            _selectedFx.Add(k);
        }

        void MoveSelectionFx(float delta)
        {
            float earliest = float.MaxValue;
            float latest = float.MinValue;
            foreach (float start in _dragStartTimesFx)
            {
                if (start < earliest) earliest = start;
                if (start > latest) latest = start;
            }
            delta = Mathf.Clamp(delta, -earliest, _rec.duration - latest);

            if (_snapToKeyframes)
            {
                int gi = _selectedFx.IndexOf(_dragFxKeyframe);
                float grabStart = gi >= 0 ? _dragStartTimesFx[gi] : _dragFxKeyframe.time - delta;
                float snapped = MagnetSnap(SnapFrame(grabStart + delta), null, null, _selectedFx, out bool didSnap);
                if (didSnap) _snapGuideActive = true;
                delta = snapped - grabStart;
            }
            else delta = SnapFrame(delta);

            for (int i = 0; i < _selectedFx.Count && i < _dragStartTimesFx.Count; i++)
                _selectedFx[i].time = _dragStartTimesFx[i] + delta;
            _rec.SortPostFxKeyframes();

            if (_selectedFx.Count > 1) return;
            _time = _dragFxKeyframe.time;
            _freeLook = false;
        }

        void BeginMarquee(Vector2 cursor)
        {
            _marqueeing = true;
            _marqueeAnchor = cursor;
            _marqueeBase.Clear();
            if (ShiftHeld()) _marqueeBase.AddRange(_selected);
            else Deselect();
            _selected.Clear();
            _selected.AddRange(_marqueeBase);

            _marqueeRt = UGUIShip.CreatePanel(_timelineRt, new Rect(cursor.x, cursor.y, 0f, 0f),
                new Color(1f, 0.83f, 0.25f, 0.16f), "Marquee");
            _marqueeRt.GetComponent<Image>().raycastTarget = false;
        }

        void DragMarquee(Vector2 cursor)
        {
            float left = Mathf.Min(_marqueeAnchor.x, cursor.x);
            float right = Mathf.Max(_marqueeAnchor.x, cursor.x);
            float top = Mathf.Min(_marqueeAnchor.y, cursor.y);
            float bottom = Mathf.Max(_marqueeAnchor.y, cursor.y);
            UGUIShip.SetPixelRect(_marqueeRt, new Rect(left, top, right - left, bottom - top));

            float from = TimeAt(left);
            float to = TimeAt(right);

            _selected.Clear();
            _selected.AddRange(_marqueeBase);
            foreach (var k in _rec.keyframes)
                if (k.time >= from && k.time <= to && !_selected.Contains(k)) _selected.Add(k);
        }

        void EndMarquee()
        {
            _marqueeing = false;
            if (_marqueeRt != null) Destroy(_marqueeRt.gameObject);
            _marqueeRt = null;
            if (_selected.Count > 0) Plugin.Log.LogInfo($"{_selected.Count} keyframes in the box");
        }

        void DragTrim(float time)
        {
            if (_draggingIn) _rec.trimStart = SnapEdge(Mathf.Clamp(time, 0f, _rec.trimEnd - MIN_TRIM));
            else _rec.trimEnd = SnapEdge(Mathf.Clamp(time, _rec.trimStart + MIN_TRIM, _rec.duration));
            _time = Mathf.Clamp(_time, _rec.trimStart, _rec.trimEnd);
        }

        void DragZoomEdge(float cursorX)
        {
            float duration = Mathf.Max(_rec.duration, MIN_VIEW_SPAN);
            float f = Mathf.Clamp01(_trackWidth > 0f ? (cursorX - _trackLeft) / _trackWidth : 0f);
            float t = duration * f;

            if (_draggingZoomLeft)
            {
                float right = _viewStart + ViewSpan;
                _viewStart = Mathf.Clamp(t, 0f, right - MIN_VIEW_SPAN);
                _viewSpan = right - _viewStart;
            }
            else
            {
                float end = Mathf.Clamp(t, _viewStart + MIN_VIEW_SPAN, duration);
                _viewSpan = end - _viewStart;
            }
            RefreshRuler();
        }

        void DragZoomPan(float cursorX)
        {
            float duration = Mathf.Max(_rec.duration, MIN_VIEW_SPAN);
            float dt = _trackWidth > 0f ? (cursorX - _dragZoomPanGrabX) / _trackWidth * duration : 0f;
            _viewStart = Mathf.Clamp(_dragZoomPanStartViewStart + dt, 0f, duration - ViewSpan);
            RefreshRuler();
        }

        void SelectOnly(ReplayKeyframe k)
        {
            UGUIShip.PlaySelectSound();
            _selected.Clear();
            _selected.Add(k);
        }

        void MoveSelection(float delta)
        {
            float earliest = float.MaxValue;
            float latest = float.MinValue;
            foreach (float start in _dragStartTimes)
            {
                if (start < earliest) earliest = start;
                if (start > latest) latest = start;
            }
            delta = Mathf.Clamp(delta, -earliest, _rec.duration - latest);

            if (_snapToKeyframes)
            {
                int gi = _selected.IndexOf(_dragKeyframe);
                float grabStart = gi >= 0 ? _dragStartTimes[gi] : _dragKeyframe.time - delta;
                float snapped = MagnetSnap(SnapFrame(grabStart + delta), _selected, null, null, out bool didSnap);
                if (didSnap) _snapGuideActive = true;
                delta = snapped - grabStart;
            }
            else delta = SnapFrame(delta);

            for (int i = 0; i < _selected.Count && i < _dragStartTimes.Count; i++)
                _selected[i].time = _dragStartTimes[i] + delta;
            _rec.SortKeyframes();

            if (_selected.Count > 1) return;
            _time = _dragKeyframe.time;
            _freeLook = false;
        }

    }
}
