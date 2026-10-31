using System;
using System.Collections.Generic;
using System.Globalization;
using BetterFG.Services;
using BetterFG.UI;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    public partial class ReplayViewer
    {
        enum LayerRow { Visibility, PostFx, Camera, Transform, PositionX, PositionY, PositionZ, RotationX, RotationY, RotationZ, Lens, OffsetX, OffsetY, Fov }

        const int ROW_COUNT = 14;
        const float PROP_KEY_SCALE = 0.8f;
        const float KEY_CONTROLS_W = 70f;
        const float VALUE_W = 66f;
        const float GRAPH_SAMPLE_PX = 4f;
        const float GRAPH_DOT = 8f;
        const float GRAPH_GRAB_PX = 8f;

        static readonly int[] RowParent = { -1, -1, -1, 2, 3, 3, 3, 3, 3, 3, 2, 10, 10, 10 };
        static readonly int[] RowDepth = { 0, 0, 0, 1, 2, 2, 2, 2, 2, 2, 1, 2, 2, 2 };
        static readonly string[] RowLabel =
        {
            null, null, null, "replay.layer_transform",
            "replay.layer_position_x", "replay.layer_position_y", "replay.layer_position_z",
            "replay.layer_rotation_x", "replay.layer_rotation_y", "replay.layer_rotation_z",
            "replay.layer_lens", "replay.layer_offset_x", "replay.layer_offset_y", "replay.layer_fov",
        };
        static readonly float[] ChannelScrubStep = { 0.05f, 0.05f, 0.05f, 0.5f, 0.5f, 0.5f, 0.002f, 0.002f, 0.2f };
        static readonly string[] ChannelFormat = { "0.00", "0.00", "0.00", "0.0", "0.0", "0.0", "0.000", "0.000", "0.0" };
        static readonly Color[] AxisColours =
        {
            new Color(0.96f, 0.4f, 0.42f, 1f), new Color(0.56f, 0.86f, 0.34f, 1f), new Color(0.36f, 0.62f, 1f, 1f), new Color(1f, 0.8f, 0.3f, 1f),
        };
        static readonly string[] EaseIds =
        {
            "replay.ease_linear", "replay.ease_constant", "replay.ease_sine", "replay.ease_quadratic", "replay.ease_cubic",
            "replay.ease_quartic", "replay.ease_quintic", "replay.ease_exponential", "replay.ease_circular", "replay.ease_back",
            "replay.ease_elastic", "replay.ease_bounce", "replay.ease_custom",
        };
        static readonly string[] EaseDirIds = { "replay.ease_in", "replay.ease_out", "replay.ease_in_out", "replay.ease_out_in" };
        static readonly Color KEY_CHANGED = new Color(1f, 0.55f, 0.15f, 1f);
        static readonly Color GRAPH_BG = new Color(0.07f, 0.08f, 0.1f, 1f);
        static readonly Color GRAPH_GRID = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color GRAPH_ZERO = new Color(1f, 1f, 1f, 0.2f);
        static readonly Color HANDLE_LINE = new Color(1f, 1f, 1f, 0.55f);

        readonly RectTransform[] _rowRt = new RectTransform[ROW_COUNT];
        readonly Image[] _rowZebra = new Image[ROW_COUNT];
        readonly Text[] _rowArrow = new Text[ROW_COUNT];
        readonly InputField[] _rowValue = new InputField[ROW_COUNT];
        readonly RectTransform[] _rowTicks = new RectTransform[ROW_COUNT];
        readonly Image[] _rowDiamond = new Image[ROW_COUNT];
        readonly float[] _rowY = new float[ROW_COUNT];
        readonly bool[] _rowOpen = new bool[ROW_COUNT];
        readonly bool[] _graphRow = new bool[ROW_COUNT];
        RectTransform _layerContent;
        Scrollbar _layerBar;
        bool _syncingLayerBar;
        float _layerScroll;
        float _layerContentH;
        bool _lensShifted;
        Vector2 _poseLens;

        readonly List<(ReplayKeyframe key, ReplayChannel channel)> _selectedCh = new List<(ReplayKeyframe, ReplayChannel)>();
        readonly List<float> _dragChValues = new List<float>();
        readonly List<ReplayKeyframe> _dragKeys = new List<ReplayKeyframe>();
        readonly List<float> _dragKeyStarts = new List<float>();
        ReplayKeyframe _dragChKey;
        float _dragChGrabTime;
        float _dragChGrabValue;
        Vector3 _dragChMouse;
        bool _dragChMoved;
        bool _dragChGraph;
        bool _dragChSplit;
        int _scrubRow = -1;
        float _scrubLastX;
        float _scrubStartX;
        bool _scrubbingValue;

        ReplayKeyframe _curveKey;
        ReplayChannel _curveChannel;
        int _dragHandle;

        bool _graphMode;
        RectTransform _graphPanel;
        Text _graphToggleLabel;
        float _graphCenter;
        float _graphScale = 20f;
        float _graphSig = float.NaN;
        bool _graphPanning;
        Vector3 _panMouse;
        float _panCenter;
        float _panViewStart;
        readonly List<RectTransform> _graphLines = new List<RectTransform>();
        readonly List<RectTransform> _graphDots = new List<RectTransform>();
        readonly List<RectTransform> _gridLines = new List<RectTransform>();
        readonly List<Text> _gridLabels = new List<Text>();
        RectTransform _handleLineOut;
        RectTransform _handleLineIn;
        RectTransform _handleDotOut;
        RectTransform _handleDotIn;

        static bool IsChannelRow(int row) => RowDepth[row] == 2;

        static ReplayChannel RowChannel(int row) =>
            (ReplayChannel)(row < (int)LayerRow.Lens ? row - (int)LayerRow.PositionX : row - (int)LayerRow.OffsetX + (int)ReplayChannel.OffsetX);

        static int ChannelRow(ReplayChannel c) =>
            c < ReplayChannel.OffsetX ? (int)LayerRow.PositionX + (int)c : (int)LayerRow.OffsetX + (int)c - (int)ReplayChannel.OffsetX;

        static float RowHeight(int row) => RowDepth[row] == 0 ? TOP_ROW_H : RowDepth[row] == 1 ? GROUP_ROW_H : PROP_ROW_H;

        static Color ChannelColour(ReplayChannel c) =>
            AxisColours[c <= ReplayChannel.RotationZ ? (int)c % 3 : c == ReplayChannel.OffsetX ? 0 : c == ReplayChannel.OffsetY ? 1 : 3];

        bool Expandable(int row) =>
            row == (int)LayerRow.Transform || row == (int)LayerRow.Lens || (row == (int)LayerRow.Camera && _solo == null);

        float GraphX(float time) => _trackWidth * TimeToFraction(time);

        float GraphY(float value) => LAYERS_H * 0.5f - (value - _graphCenter) * _graphScale;

        float GraphValue(float y) => _graphCenter + (LAYERS_H * 0.5f - y) / _graphScale;

        bool OverGraph(Vector2 cursor) =>
            _graphMode && _solo == null && cursor.x >= _trackLeft && cursor.x <= _trackLeft + _trackWidth
            && cursor.y >= LANES_Y && cursor.y < LANES_Y + LAYERS_H;

        void BuildLayers(RectTransform bar, float trackW)
        {
            float width = _trackLeft + trackW;
            var viewport = UGUIShip.CreatePanel(bar, new Rect(0f, LANES_Y, width, LAYERS_H), Color.clear, "LayerViewport");
            viewport.GetComponent<Image>().raycastTarget = false;
            viewport.gameObject.AddComponent<RectMask2D>();

            _layerContent = new GameObject("LayerContent").AddComponent<RectTransform>();
            _layerContent.SetParent(viewport, false);
            UGUIShip.SetPixelRect(_layerContent, new Rect(0f, 0f, width, 0f));

            for (int row = 0; row < ROW_COUNT; row++) BuildLayerRow(row, width, trackW);
            _visTicks = _rowTicks[(int)LayerRow.Visibility];
            _fxTicks = _rowTicks[(int)LayerRow.PostFx];
            _keyTicks = _rowTicks[(int)LayerRow.Camera];

            var track = ReplayWindowKit.ColorFill(UGUIShip.CreatePanel(bar, new Rect(width + 4f, LANES_Y, LAYER_SCROLL_W, LAYERS_H),
                new Color(0f, 0f, 0f, 0.35f), "LayerScrollbar"));
            track.GetComponent<Image>().raycastTarget = true;
            var area = new GameObject("Sliding").AddComponent<RectTransform>();
            area.SetParent(track, false);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = area.offsetMax = Vector2.zero;
            var handle = ReplayWindowKit.ColorFill(UGUIShip.CreatePanel(area, new Rect(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 0.45f), "Handle"));
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            var handleImg = handle.GetComponent<Image>();
            handleImg.raycastTarget = true;
            _layerBar = track.gameObject.AddComponent<Scrollbar>();
            _layerBar.handleRect = handle;
            _layerBar.targetGraphic = handleImg;
            _layerBar.direction = Scrollbar.Direction.TopToBottom;
            _layerBar.onValueChanged.AddListener(new Action<float>(OnLayerBarMoved));

            _graphPanel = ReplayWindowKit.ColorFill(UGUIShip.CreatePanel(bar, new Rect(_trackLeft, LANES_Y, trackW, LAYERS_H), GRAPH_BG, "GraphEditor"));
            _graphPanel.gameObject.AddComponent<RectMask2D>();
            _graphLines.Clear();
            _graphDots.Clear();
            _gridLines.Clear();
            _gridLabels.Clear();
            _handleLineOut = BuildLine(_graphPanel, HANDLE_LINE);
            _handleLineIn = BuildLine(_graphPanel, HANDLE_LINE);
            _handleDotOut = BuildDiamond(_graphPanel, GRAPH_DOT, PLAYHEAD_YELLOW);
            _handleDotIn = BuildDiamond(_graphPanel, GRAPH_DOT, PLAYHEAD_YELLOW);
            _graphPanel.gameObject.SetActive(_graphMode && _solo == null);
            _graphSig = float.NaN;

            var toggle = UGUIShip.CreateButton(bar, new Rect(PAD, ZoomBarY, _trackLeft - PAD - 8f, ZOOMBAR_H),
                _graphMode ? "replay.graphs_hide" : "replay.graphs_show", BTN_DARK, Color.white, UIScale.FS_SM, new Action(ToggleGraphs));
            _graphToggleLabel = toggle.GetComponentInChildren<Text>();
            toggle.gameObject.SetActive(_solo == null);

            RelayoutLayers();
        }

        void BuildLayerRow(int row, float width, float trackW)
        {
            int depth = RowDepth[row];
            float h = RowHeight(row);
            var rt = new GameObject("Layer").AddComponent<RectTransform>();
            rt.SetParent(_layerContent, false);
            UGUIShip.SetPixelRect(rt, new Rect(0f, 0f, width, h));
            _rowRt[row] = rt;
            _rowArrow[row] = null;
            _rowValue[row] = null;
            _rowDiamond[row] = null;
            _rowTicks[row] = null;

            _rowZebra[row] = ReplayWindowKit.ColorFill(UGUIShip.CreatePanel(rt, new Rect(PAD, 0f, width - PAD, h), Color.white, "Zebra"))
                .GetComponent<Image>();
            var laneColor = depth == 0 ? LANE_BG : new Color(LANE_BG.r, LANE_BG.g, LANE_BG.b, LANE_BG.a * (depth == 1 ? 0.4f : 0.8f));
            ReplayWindowKit.ColorFill(UGUIShip.CreatePanel(rt, new Rect(_trackLeft, 0f, trackW, h), laneColor, "Lane"));

            float x = LabelX(row);
            float labelRight = IsChannelRow(row) ? ValueLeft() - 4f : _trackLeft - 6f;
            var labelColour = IsChannelRow(row) ? ChannelColour(RowChannel(row)) * new Color(1f, 1f, 1f, 0.85f) : HINT;
            UGUIShip.CreateLabel(rt, new Rect(x, 0f, labelRight - x, h), (row < LANE_COUNT ? LaneName(row) : RowLabel[row]) ?? "",
                depth == 2 ? UIScale.FS_SM - 2 : UIScale.FS_SM - 1, labelColour, TextAnchor.MiddleLeft);

            if (Expandable(row))
            {
                int r = row;
                _rowArrow[row] = UGUIShip.CreateButton(rt, new Rect(x - 16f, 0f, 16f, h), ">", Color.clear, Color.white, UIScale.FS_SM,
                    new Action(() => ToggleLayer(r)), customSprite: false).GetComponentInChildren<Text>();
            }

            if (row <= (int)LayerRow.Camera || IsChannelRow(row))
            {
                _rowTicks[row] = new GameObject("Ticks").AddComponent<RectTransform>();
                _rowTicks[row].SetParent(rt, false);
                UGUIShip.SetPixelRect(_rowTicks[row], new Rect(0f, 0f, width, h));
            }

            if (!IsChannelRow(row)) return;

            int ch = row;
            var field = UGUIShip.CreateInputField(rt, new Rect(ValueLeft(), 2f, VALUE_W, h - 4f), "", new Color(0f, 0f, 0f, 0.4f), Color.white, UIScale.FS_SM - 2);
            field.contentType = InputField.ContentType.DecimalNumber;
            field.textComponent.alignment = TextAnchor.MiddleRight;
            field.onEndEdit.AddListener(new Action<string>(s => CommitTypedValue(ch, s)));
            _rowValue[row] = field;

            float kx = _trackLeft - KEY_CONTROLS_W;
            UGUIShip.CreateButton(rt, new Rect(kx, 0f, 20f, h), "<", Color.clear, HINT, UIScale.FS_SM,
                new Action(() => JumpChannelKey(ch, -1)), customSprite: false);
            var key = UGUIShip.CreateButton(rt, new Rect(kx + 22f, 0f, 20f, h), "", Color.clear, Color.white, UIScale.FS_SM,
                new Action(() => KeyChannelHere(ch)), customSprite: false);
            var diamond = BuildDiamond(key.transform, 9f, Color.white);
            diamond.anchorMin = diamond.anchorMax = new Vector2(0.5f, 0.5f);
            diamond.anchoredPosition = Vector2.zero;
            diamond.gameObject.SetActive(true);
            _rowDiamond[row] = diamond.GetComponent<Image>();
            UGUIShip.CreateButton(rt, new Rect(kx + 44f, 0f, 20f, h), ">", Color.clear, HINT, UIScale.FS_SM,
                new Action(() => JumpChannelKey(ch, 1)), customSprite: false);
        }

        static float LabelX(int row) => PAD + 14f + RowDepth[row] * 16f;

        float ValueLeft() => _trackLeft - KEY_CONTROLS_W - VALUE_W - 4f;

        static RectTransform BuildDiamond(Transform parent, float size, Color colour)
        {
            var dot = ReplayWindowKit.ColorFill(UGUIShip.CreatePanel(parent, new Rect(0f, 0f, size, size), colour, "Diamond"));
            dot.pivot = new Vector2(0.5f, 0.5f);
            dot.localEulerAngles = new Vector3(0f, 0f, 45f);
            dot.gameObject.SetActive(false);
            return dot;
        }

        bool RowShown(int row)
        {
            if (row < LANE_COUNT) return !string.IsNullOrEmpty(LaneName(row));
            if (_solo != null) return false;
            for (int p = RowParent[row]; p >= 0; p = RowParent[p])
                if (!_rowOpen[p]) return false;
            return true;
        }

        void ToggleLayer(int row)
        {
            _rowOpen[row] = !_rowOpen[row];
            RelayoutLayers();
            SyncUi();
        }

        void RelayoutLayers()
        {
            float y = 0f;
            int shown = 0;
            for (int row = 0; row < ROW_COUNT; row++)
            {
                bool visible = RowShown(row);
                if (_rowRt[row].gameObject.activeSelf != visible) _rowRt[row].gameObject.SetActive(visible);
                if (!visible) continue;

                _rowRt[row].anchoredPosition = new Vector2(0f, -y);
                _rowY[row] = y;
                _rowZebra[row].color = _graphRow[row]
                    ? ChannelColour(RowChannel(row)) * new Color(1f, 1f, 1f, 0.16f)
                    : new Color(1f, 1f, 1f, shown % 2 == 0 ? 0.035f : 0.012f);
                if (_rowArrow[row] != null) _rowArrow[row].text = _rowOpen[row] ? "v" : ">";
                y += RowHeight(row);
                shown++;
            }
            _layerContentH = y;
            _layerContent.sizeDelta = new Vector2(_layerContent.sizeDelta.x, y);
            ScrollLayersTo(_layerScroll);
        }

        void OnLayerBarMoved(float v)
        {
            if (_syncingLayerBar) return;
            ScrollLayersTo(v * Mathf.Max(0f, _layerContentH - LAYERS_H));
        }

        void ScrollLayersTo(float px)
        {
            float max = Mathf.Max(0f, _layerContentH - LAYERS_H);
            _layerScroll = Mathf.Clamp(px, 0f, max);
            _layerContent.anchoredPosition = new Vector2(0f, _layerScroll);
            _syncingLayerBar = true;
            _layerBar.size = max > 0f ? LAYERS_H / _layerContentH : 1f;
            _layerBar.value = max > 0f ? _layerScroll / max : 0f;
            _syncingLayerBar = false;
        }

        int RowAt(Vector2 cursor)
        {
            if (cursor.y < LANES_Y || cursor.y >= LANES_Y + LAYERS_H) return -1;
            float y = cursor.y - LANES_Y + _layerScroll;
            for (int row = 0; row < ROW_COUNT; row++)
                if (_rowRt[row] != null && _rowRt[row].gameObject.activeSelf && y >= _rowY[row] && y < _rowY[row] + RowHeight(row))
                    return row;
            return -1;
        }

        void ToggleGraphs()
        {
            _graphMode = !_graphMode;
            if (_graphMode)
            {
                bool any = false;
                for (int row = 0; row < ROW_COUNT; row++) any |= _graphRow[row];
                if (!any)
                {
                    int first = _curveKey != null ? ChannelRow(_curveChannel) : (int)LayerRow.PositionX;
                    if (_curveKey != null) _graphRow[first] = true;
                    else for (int row = (int)LayerRow.PositionX; row <= (int)LayerRow.PositionZ; row++) _graphRow[row] = true;
                }
                for (int row = 0; row < ROW_COUNT; row++)
                    if (_graphRow[row])
                        for (int p = RowParent[row]; p >= 0; p = RowParent[p]) _rowOpen[p] = true;
                _graphCenter = 0f;
                FitGraphScale(false);
            }
            _graphPanel.gameObject.SetActive(_graphMode && _solo == null);
            UGUIShip.RelabelText(_graphToggleLabel, _graphMode ? "replay.graphs_hide" : "replay.graphs_show");
            _graphSig = float.NaN;
            RelayoutLayers();
            SyncUi();
            Plugin.Log.LogInfo(_graphMode ? "graph editor open" : "graph editor shut");
        }

        void FitGraphScale(bool recentre)
        {
            float lo = float.MaxValue;
            float hi = float.MinValue;
            for (int row = 0; row < ROW_COUNT; row++)
            {
                if (!_graphRow[row]) continue;
                var c = RowChannel(row);
                foreach (var k in _rec.keyframes)
                {
                    if (!k.channels[(int)c].keyed) continue;
                    float v = k.Raw(c);
                    lo = Mathf.Min(lo, v);
                    hi = Mathf.Max(hi, v);
                }
            }
            if (lo > hi) { lo = -1f; hi = 1f; }

            float half = LAYERS_H * 0.5f - 14f;
            if (recentre)
            {
                _graphCenter = (lo + hi) * 0.5f;
                _graphScale = half / Mathf.Max((hi - lo) * 0.5f, 0.01f);
            }
            else _graphScale = half / Mathf.Max(Mathf.Abs(lo), Mathf.Abs(hi), 0.01f);
            _graphSig = float.NaN;
        }

        void RefreshLayers()
        {
            RefreshChannelRows();
            if (_graphMode) RefreshGraph();
        }

        bool ChannelHasKeys(ReplayChannel c)
        {
            foreach (var k in _rec.keyframes)
                if (k.channels[(int)c].keyed) return true;
            return false;
        }

        ReplayKeyframe KeyframeAtPlayhead()
        {
            foreach (var k in _rec.keyframes)
                if (Mathf.Abs(k.time - _time) < FRAME_TIME * 0.5f) return k;
            return null;
        }

        void RefreshChannelRows()
        {
            var hovered = HoveredChannelKey(out var hoveredChannel);
            CurrentPose(out var pos, out var rot, out float fov, out var lens);
            var here = KeyframeAtPlayhead();

            for (int row = (int)LayerRow.PositionX; row < ROW_COUNT; row++)
            {
                if (!IsChannelRow(row) || !_rowRt[row].gameObject.activeSelf) continue;

                var channel = RowChannel(row);
                int ci = (int)channel;
                var parent = _rowTicks[row];
                int count = 0;
                foreach (var k in _rec.keyframes)
                {
                    if (!k.channels[ci].keyed) continue;
                    if (count >= parent.childCount)
                    {
                        BuildKeyframeTick(parent);
                        parent.GetChild(count).localScale = new Vector3(PROP_KEY_SCALE, PROP_KEY_SCALE, 1f);
                    }
                    var rt = parent.GetChild(count).GetComponent<RectTransform>();
                    count++;

                    float f = TimeToFraction(k.time);
                    bool onScreen = f >= -0.01f && f <= 1.01f;
                    if (rt.gameObject.activeSelf != onScreen) rt.gameObject.SetActive(onScreen);
                    if (!onScreen) continue;

                    rt.anchoredPosition = new Vector2(_trackLeft + _trackWidth * f - KEY_W * PROP_KEY_SCALE * 0.5f,
                        -(PROP_ROW_H - KEY_H * PROP_KEY_SCALE) * 0.5f);
                    rt.GetChild(0).GetComponent<Image>().color = k == hovered && channel == hoveredChannel ? Color.white
                        : k == _curveKey && channel == _curveChannel ? KEY_EDITING
                        : _selectedCh.Contains((k, channel)) ? KEY_SELECTED
                        : KEY_IDLE;
                }
                for (int i = parent.childCount - 1; i >= count; i--)
                    DestroyImmediate(parent.GetChild(i).gameObject);

                bool keyed = here != null && here.channels[ci].keyed;
                bool changed = keyed && Mathf.Abs(PoseRaw(here, channel, pos, rot, fov, lens) - here.Raw(channel)) > 0.001f;
                _rowDiamond[row].color = !keyed ? new Color(1f, 1f, 1f, 0.28f) : changed ? KEY_CHANGED : Color.white;

                var field = _rowValue[row];
                if (field.isFocused) continue;
                float shown = !_freeLook && count > 0 ? EvalChannel(channel, _time, true, 0f, pos) : PoseWorld(channel, pos, rot, fov, lens);
                string text = shown.ToString(ChannelFormat[ci], CultureInfo.InvariantCulture);
                if (field.text != text) field.text = text;
                field.textComponent.color = new Color(1f, 1f, 1f, keyed ? 1f : count > 0 ? 0.75f : 0.45f);
            }
        }

        static float PoseWorld(ReplayChannel c, Vector3 pos, Quaternion rot, float fov, Vector2 lens)
        {
            if (c <= ReplayChannel.PositionZ) return pos[(int)c];
            if (IsRotationChannel(c)) return Mathf.DeltaAngle(0f, rot.eulerAngles[(int)c - (int)ReplayChannel.RotationX]);
            if (c == ReplayChannel.OffsetX) return lens.x;
            if (c == ReplayChannel.OffsetY) return lens.y;
            return fov;
        }

        ReplayKeyframe HoveredChannelKey(out ReplayChannel channel)
        {
            channel = _curveChannel;
            var cursor = TimelineCursor();
            int row = RowAt(cursor);
            if (row < 0 || !IsChannelRow(row) || cursor.x < _trackLeft || !OverTimeline(cursor) || OverGraph(cursor)) return null;
            channel = RowChannel(row);
            return ChannelKeyNear(channel, TimeAt(cursor.x));
        }

        ReplayKeyframe ChannelKeyNear(ReplayChannel channel, float time)
        {
            if (_trackWidth <= 0f) return null;
            float tolerance = (KEY_W * 0.75f / _trackWidth) * ViewSpan;

            ReplayKeyframe best = null;
            float bestDelta = float.MaxValue;
            foreach (var k in _rec.keyframes)
            {
                if (!k.channels[(int)channel].keyed) continue;
                float delta = Mathf.Abs(k.time - time);
                if (delta <= tolerance && delta < bestDelta) { bestDelta = delta; best = k; }
            }
            return best;
        }

        ReplayKeyframe NeighbourKey(ReplayKeyframe key, ReplayChannel c, int dir)
        {
            int i = _rec.keyframes.IndexOf(key);
            if (i < 0) return null;
            for (int j = i + dir; j >= 0 && j < _rec.keyframes.Count; j += dir)
                if (_rec.keyframes[j].channels[(int)c].keyed) return _rec.keyframes[j];
            return null;
        }

        bool HandlePoints(ReplayKeyframe k, ReplayChannel c, out Vector2 outTv, out Vector2 inTv, out ReplayKeyframe prev, out ReplayKeyframe next)
        {
            int ci = (int)c;
            outTv = inTv = Vector2.zero;
            next = NeighbourKey(k, c, 1);
            prev = NeighbourKey(k, c, -1);
            if (next != null && next.cut) next = null;
            if (prev != null && k.cut) prev = null;
            float vk = k.Raw(c);

            if (next != null)
            {
                var e = k.channels[ci];
                float dt = next.time - k.time;
                float vn = next.Raw(c);
                if (e.curve == ReplayEasingCurve.Custom) outTv = new Vector2(k.time + e.outHandle.x * dt, vk + e.outHandle.y);
                else
                {
                    var p = ReplayEase.PresetBezier(e.curve, e.direction);
                    outTv = new Vector2(k.time + p.x * dt, vk + p.y * (vn - vk));
                }
            }
            if (prev != null)
            {
                var e = prev.channels[ci];
                float dt = k.time - prev.time;
                float vp = prev.Raw(c);
                if (e.curve == ReplayEasingCurve.Custom) inTv = new Vector2(k.time - k.channels[ci].inHandle.x * dt, vk + k.channels[ci].inHandle.y);
                else
                {
                    var p = ReplayEase.PresetBezier(e.curve, e.direction);
                    inTv = new Vector2(prev.time + p.z * dt, vp + p.w * (vk - vp));
                }
            }
            return next != null || prev != null;
        }

        void MakeCustom(ReplayKeyframe a, ReplayKeyframe b, ReplayChannel c)
        {
            int ci = (int)c;
            var e = a.channels[ci];
            if (e.curve == ReplayEasingCurve.Custom) return;
            var p = ReplayEase.PresetBezier(e.curve, e.direction);
            float va = a.Raw(c);
            float vb = b.Raw(c);
            e.outHandle = new Vector2(p.x, p.y * (vb - va));
            b.channels[ci].inHandle = new Vector2(1f - p.z, va + p.w * (vb - va) - vb);
            e.curve = ReplayEasingCurve.Custom;
        }

        void RefreshGraph()
        {
            float sig = _viewStart * 7.31f + ViewSpan * 3.17f + _trackWidth * 0.13f + _graphCenter * 1.7f + _graphScale * 2.9f
                + _rec.keyframes.Count * 11f + _selectedCh.Count * 5.3f + (int)_curveChannel * 0.37f + (_curveKey != null ? _curveKey.time * 2.2f : 0f);
            for (int row = 0; row < ROW_COUNT; row++) if (_graphRow[row]) sig += row * 3.9f + 1f;
            foreach (var (key, channel) in _selectedCh) sig += key.time * 0.917f + (int)channel * 0.29f;
            foreach (var k in _rec.keyframes)
            {
                sig += k.time * 1.618f + (k.cut ? 5.1f : 0f);
                for (int i = 0; i < ReplayKeyframe.ChannelCount; i++)
                {
                    var e = k.channels[i];
                    if (!e.keyed) continue;
                    sig += k.Raw((ReplayChannel)i) * (0.731f + i * 0.13f) + (int)e.curve * 2.3f + (int)e.direction * 0.7f
                        + e.outHandle.x * 3.1f + e.outHandle.y * 4.7f + e.inHandle.x * 5.3f + e.inHandle.y * 6.1f + i;
                }
            }
            if (sig == _graphSig) return;
            _graphSig = sig;

            float w = _trackWidth;
            int lines = 0;
            int grid = 0;
            int dots = 0;

            float step = NiceStep(28f / _graphScale);
            float top = GraphValue(0f);
            float bottom = GraphValue(LAYERS_H);
            string fmt = step >= 1f ? "0" : step >= 0.1f ? "0.0" : step >= 0.01f ? "0.00" : "0.000";
            for (float v = Mathf.Floor(bottom / step) * step; v <= top && grid < 60; v += step)
            {
                float y = GraphY(v);
                bool zero = Mathf.Abs(v) < step * 0.01f;
                var line = GridLine(grid);
                UGUIShip.SetPixelRect(line, new Rect(0f, y, w, zero ? 2f : 1f));
                line.GetComponent<Image>().color = zero ? GRAPH_ZERO : GRAPH_GRID;
                var label = _gridLabels[grid];
                label.rectTransform.anchoredPosition = new Vector2(16f, -(y - 13f));
                label.text = (zero ? 0f : v).ToString(fmt, CultureInfo.InvariantCulture);
                grid++;
            }
            float tStep = RulerStep();
            for (float t = Mathf.Ceil(_viewStart / tStep) * tStep; t <= _viewStart + ViewSpan && grid < 120; t += tStep)
            {
                var line = GridLine(grid);
                UGUIShip.SetPixelRect(line, new Rect(GraphX(t), 0f, 1f, LAYERS_H));
                line.GetComponent<Image>().color = GRAPH_GRID;
                _gridLabels[grid].text = "";
                grid++;
            }

            for (int row = 0; row < ROW_COUNT; row++)
            {
                if (!_graphRow[row]) continue;
                var c = RowChannel(row);
                if (!ChannelHasKeys(c)) continue;
                var colour = ChannelColour(c);

                Vector2 prevPt = default;
                bool havePrev = false;
                for (float x = 0f; x <= w + GRAPH_SAMPLE_PX; x += GRAPH_SAMPLE_PX)
                {
                    float t = FractionToTime(Mathf.Min(x, w) / w);
                    var pt = new Vector2(Mathf.Min(x, w), GraphY(EvalChannel(c, t, true, 0f, Vector3.zero)));
                    if (havePrev && !(prevPt.y < -20f && pt.y < -20f) && !(prevPt.y > LAYERS_H + 20f && pt.y > LAYERS_H + 20f))
                    {
                        var seg = PoolLine(_graphLines, lines++);
                        seg.GetComponent<Image>().color = colour;
                        StretchLine(seg, prevPt, pt);
                    }
                    prevPt = pt;
                    havePrev = true;
                }

                foreach (var k in _rec.keyframes)
                {
                    if (!k.channels[(int)c].keyed) continue;
                    float x = GraphX(k.time);
                    if (x < -GRAPH_DOT || x > w + GRAPH_DOT) continue;
                    var dot = PoolDot(dots++);
                    dot.anchoredPosition = new Vector2(x, -GraphY(k.Raw(c)));
                    dot.GetComponent<Image>().color = k == _curveKey && c == _curveChannel ? PLAYHEAD_YELLOW
                        : _selectedCh.Contains((k, c)) ? Color.white
                        : colour * new Color(0.75f, 0.75f, 0.75f, 1f);
                }
            }

            for (int i = lines; i < _graphLines.Count; i++) if (_graphLines[i].gameObject.activeSelf) _graphLines[i].gameObject.SetActive(false);
            for (int i = dots; i < _graphDots.Count; i++) if (_graphDots[i].gameObject.activeSelf) _graphDots[i].gameObject.SetActive(false);
            for (int i = grid; i < _gridLines.Count; i++)
            {
                if (_gridLines[i].gameObject.activeSelf) _gridLines[i].gameObject.SetActive(false);
                _gridLabels[i].text = "";
            }

            bool showOut = false;
            bool showIn = false;
            int curveRow = _curveKey != null ? ChannelRow(_curveChannel) : -1;
            if (curveRow >= 0 && _graphRow[curveRow] && _rec.keyframes.Contains(_curveKey) && _curveKey.channels[(int)_curveChannel].keyed
                && HandlePoints(_curveKey, _curveChannel, out var outTv, out var inTv, out var prevKey, out var nextKey))
            {
                var keyPt = new Vector2(GraphX(_curveKey.time), GraphY(_curveKey.Raw(_curveChannel)));
                if (nextKey != null)
                {
                    var h = new Vector2(GraphX(outTv.x), GraphY(outTv.y));
                    StretchLine(_handleLineOut, keyPt, h);
                    _handleDotOut.gameObject.SetActive(true);
                    _handleDotOut.anchoredPosition = new Vector2(h.x, -h.y);
                    showOut = true;
                }
                if (prevKey != null)
                {
                    var h = new Vector2(GraphX(inTv.x), GraphY(inTv.y));
                    StretchLine(_handleLineIn, keyPt, h);
                    _handleDotIn.gameObject.SetActive(true);
                    _handleDotIn.anchoredPosition = new Vector2(h.x, -h.y);
                    showIn = true;
                }
            }
            if (!showOut) { _handleDotOut.gameObject.SetActive(false); _handleLineOut.gameObject.SetActive(false); }
            if (!showIn) { _handleDotIn.gameObject.SetActive(false); _handleLineIn.gameObject.SetActive(false); }
            _handleLineOut.SetAsLastSibling();
            _handleLineIn.SetAsLastSibling();
            _handleDotOut.SetAsLastSibling();
            _handleDotIn.SetAsLastSibling();
        }

        static float NiceStep(float raw)
        {
            float mag = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(raw, 0.0001f))));
            float n = raw / mag;
            return (n <= 1f ? 1f : n <= 2f ? 2f : n <= 5f ? 5f : 10f) * mag;
        }

        RectTransform GridLine(int i)
        {
            if (i >= _gridLines.Count)
            {
                var line = UGUIShip.CreatePanel(_graphPanel, new Rect(0f, 0f, 1f, 1f), GRAPH_GRID, "Grid");
                line.GetComponent<Image>().raycastTarget = false;
                line.SetAsFirstSibling();
                _gridLines.Add(line);
                var label = UGUIShip.CreateLabel(_graphPanel, new Rect(4f, 0f, 70f, 12f), "", UIScale.FS_SM - 3, HINT, TextAnchor.LowerLeft);
                label.raycastTarget = false;
                _gridLabels.Add(label);
            }
            if (!_gridLines[i].gameObject.activeSelf) _gridLines[i].gameObject.SetActive(true);
            return _gridLines[i];
        }

        RectTransform PoolLine(List<RectTransform> pool, int i)
        {
            if (i >= pool.Count) pool.Add(BuildLine(_graphPanel, Color.white));
            return pool[i];
        }

        RectTransform PoolDot(int i)
        {
            if (i >= _graphDots.Count) _graphDots.Add(BuildDiamond(_graphPanel, GRAPH_DOT, Color.white));
            var dot = _graphDots[i];
            if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
            return dot;
        }

        ReplayKeyframe GraphKeyAt(Vector2 g, out ReplayChannel channel)
        {
            channel = _curveChannel;
            ReplayKeyframe best = null;
            float bestD = GRAPH_GRAB_PX * GRAPH_GRAB_PX;
            for (int row = 0; row < ROW_COUNT; row++)
            {
                if (!_graphRow[row]) continue;
                var c = RowChannel(row);
                foreach (var k in _rec.keyframes)
                {
                    if (!k.channels[(int)c].keyed) continue;
                    float d = (new Vector2(GraphX(k.time), GraphY(k.Raw(c))) - g).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = k; channel = c; }
                }
            }
            return best;
        }

        bool GrabGraphHandle(Vector2 g)
        {
            float grab = GRAPH_GRAB_PX * GRAPH_GRAB_PX;
            if (_handleDotOut.gameObject.activeSelf && (new Vector2(_handleDotOut.anchoredPosition.x, -_handleDotOut.anchoredPosition.y) - g).sqrMagnitude <= grab)
                _dragHandle = 1;
            else if (_handleDotIn.gameObject.activeSelf && (new Vector2(_handleDotIn.anchoredPosition.x, -_handleDotIn.anchoredPosition.y) - g).sqrMagnitude <= grab)
                _dragHandle = 2;
            else return false;
            return true;
        }

        void DragGraphHandle(Vector2 g)
        {
            var k = _curveKey;
            var c = _curveChannel;
            int ci = (int)c;
            if (!HandlePoints(k, c, out var outTv, out var inTv, out var prev, out var next)) return;

            var keyPx = new Vector2(GraphX(k.time), GraphY(k.Raw(c)));
            float t = FractionToTime(g.x / _trackWidth);
            float v = GraphValue(g.y);
            bool aligned = !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));

            if ((next != null && next.time - k.time < 0.001f) || (prev != null && k.time - prev.time < 0.001f)) return;

            if (_dragHandle == 1 && next != null)
            {
                MakeCustom(k, next, c);
                float dt = next.time - k.time;
                k.channels[ci].outHandle = new Vector2(Mathf.Clamp01((t - k.time) / dt), v - k.Raw(c));
                if (aligned && prev != null)
                {
                    MakeCustom(prev, k, c);
                    var inPx = new Vector2(GraphX(inTv.x), GraphY(inTv.y));
                    var dir = (g - keyPx).normalized;
                    var mirrored = keyPx - dir * (inPx - keyPx).magnitude;
                    float pdt = k.time - prev.time;
                    k.channels[ci].inHandle = new Vector2(Mathf.Clamp01((k.time - FractionToTime(mirrored.x / _trackWidth)) / pdt),
                        GraphValue(mirrored.y) - k.Raw(c));
                }
            }
            else if (_dragHandle == 2 && prev != null)
            {
                MakeCustom(prev, k, c);
                float dt = k.time - prev.time;
                k.channels[ci].inHandle = new Vector2(Mathf.Clamp01((k.time - t) / dt), v - k.Raw(c));
                if (aligned && next != null)
                {
                    MakeCustom(k, next, c);
                    var outPx = new Vector2(GraphX(outTv.x), GraphY(outTv.y));
                    var dir = (g - keyPx).normalized;
                    var mirrored = keyPx - dir * (outPx - keyPx).magnitude;
                    float ndt = next.time - k.time;
                    k.channels[ci].outHandle = new Vector2(Mathf.Clamp01((FractionToTime(mirrored.x / _trackWidth) - k.time) / ndt),
                        GraphValue(mirrored.y) - k.Raw(c));
                }
            }
            ApplyTime();
        }

        bool HandleGraphMouse(Vector2 cursor)
        {
            var g = new Vector2(cursor.x - _trackLeft, cursor.y - LANES_Y);

            if (Input.GetKeyDown(KeyCode.Home)) FitGraphScale(true);

            if (Input.GetMouseButtonDown(2))
            {
                _graphPanning = true;
                _panMouse = Input.mousePosition;
                _panCenter = _graphCenter;
                _panViewStart = _viewStart;
                return true;
            }

            if (Input.GetMouseButtonDown(0))
            {
                CloseContextMenu();
                if (GrabGraphHandle(g)) return true;

                var k = GraphKeyAt(g, out var c);
                if (k == null)
                {
                    if (!ShiftHeld()) Deselect();
                    return true;
                }

                if (ShiftHeld())
                {
                    if (_selectedCh.Remove((k, c))) return true;
                    _selectedCh.Add((k, c));
                }
                else if (!_selectedCh.Contains((k, c)))
                {
                    Deselect();
                    _selectedCh.Add((k, c));
                }
                UGUIShip.PlaySelectSound();
                _curveKey = k;
                _curveChannel = c;
                _freeLook = false;
                BeginChannelDrag(k, TimeAt(cursor.x), GraphValue(g.y), true);
                return true;
            }

            if (Input.GetMouseButtonDown(1))
            {
                CloseContextMenu();
                var k = GraphKeyAt(g, out var c);
                if (k == null) return true;
                if (!_selectedCh.Contains((k, c)))
                {
                    Deselect();
                    _selectedCh.Add((k, c));
                }
                _curveKey = k;
                _curveChannel = c;
                ShowCurveMenu(k, (int)c, cursor, 0);
            }
            return true;
        }

        void ZoomGraphValues(float notches, Vector2 cursor)
        {
            float gy = cursor.y - LANES_Y;
            float anchor = GraphValue(gy);
            _graphScale = Mathf.Clamp(_graphScale * Mathf.Pow(1.18f, notches), 0.0005f, 20000f);
            _graphCenter = anchor - (LAYERS_H * 0.5f - gy) / _graphScale;
        }

        void BeginChannelDrag(ReplayKeyframe k, float time, float value, bool graph)
        {
            _dragChKey = k;
            _dragChGrabTime = time;
            _dragChGrabValue = value;
            _dragChMouse = Input.mousePosition;
            _dragChMoved = false;
            _dragChGraph = graph;
            _dragChSplit = false;
            _dragChValues.Clear();
            foreach (var (key, channel) in _selectedCh) _dragChValues.Add(key.Raw(channel));
        }

        void SplitSelection()
        {
            _dragKeys.Clear();
            _dragKeyStarts.Clear();
            var owners = new List<ReplayKeyframe>();
            foreach (var (key, _) in _selectedCh) if (!owners.Contains(key)) owners.Add(key);

            foreach (var key in owners)
            {
                bool whole = true;
                for (int i = 0; i < ReplayKeyframe.ChannelCount; i++)
                    if (key.channels[i].keyed && !_selectedCh.Contains((key, (ReplayChannel)i))) whole = false;

                var mover = key;
                if (!whole)
                {
                    mover = CloneFields(key);
                    mover.cut = false;
                    mover.cutToNext = false;
                    mover.shakeKind = ReplayShakeKind.None;
                    for (int i = 0; i < ReplayKeyframe.ChannelCount; i++)
                    {
                        mover.channels[i].CopyFrom(key.channels[i]);
                        bool picked = _selectedCh.Contains((key, (ReplayChannel)i));
                        mover.channels[i].keyed = key.channels[i].keyed && picked;
                        if (picked) key.channels[i].keyed = false;
                    }
                    _rec.keyframes.Add(mover);
                    for (int s = 0; s < _selectedCh.Count; s++)
                        if (_selectedCh[s].key == key) _selectedCh[s] = (mover, _selectedCh[s].channel);
                    if (_curveKey == key && mover.channels[(int)_curveChannel].keyed) _curveKey = mover;
                    if (_dragChKey == key) _dragChKey = mover;
                    Plugin.Log.LogInfo($"split the {Stamp(key.time)} keyframe so the dragged properties can leave on their own");
                }
                _dragKeys.Add(mover);
                _dragKeyStarts.Add(mover.time);
            }
            _rec.SortKeyframes();
        }

        bool HandleLayerDrag(Vector2 cursor, float time)
        {
            if (_dragHandle != 0 && (_curveKey == null || !_rec.keyframes.Contains(_curveKey))) _dragHandle = 0;
            if (_dragHandle != 0)
            {
                if (Input.GetMouseButton(0)) DragGraphHandle(new Vector2(cursor.x - _trackLeft, cursor.y - LANES_Y));
                else
                {
                    var e = _curveKey.channels[(int)_curveChannel];
                    Plugin.Log.LogInfo($"{_curveChannel} handles on {Stamp(_curveKey.time)}: out {e.outHandle}, in {e.inHandle}");
                    _dragHandle = 0;
                }
                return true;
            }

            if (_graphPanning)
            {
                if (!Input.GetMouseButton(2)) { _graphPanning = false; return true; }
                float sf = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
                var d = (Input.mousePosition - _panMouse) / sf;
                _graphCenter = _panCenter - d.y / _graphScale;
                float duration = Mathf.Max(_rec.duration, MIN_VIEW_SPAN);
                if (_trackWidth > 0f && ViewSpan < duration)
                {
                    _viewStart = Mathf.Clamp(_panViewStart - d.x / _trackWidth * ViewSpan, 0f, duration - ViewSpan);
                    RefreshRuler();
                }
                return true;
            }

            if (_scrubRow >= 0)
            {
                if (!Input.GetMouseButton(0))
                {
                    var scrubbed = KeyframeAtPlayhead();
                    if (_scrubbingValue && scrubbed != null)
                        Plugin.Log.LogInfo($"{RowChannel(_scrubRow)} scrubbed to {scrubbed.Raw(RowChannel(_scrubRow)):0.###}");
                    _scrubRow = -1;
                    _scrubbingValue = false;
                    return true;
                }
                float mx = Input.mousePosition.x;
                if (!_scrubbingValue && Mathf.Abs(mx - _scrubStartX) < DRAG_DEADZONE) return true;
                if (!_scrubbingValue)
                {
                    _scrubbingValue = true;
                    _scrubLastX = mx;
                }
                float dx = mx - _scrubLastX;
                if (Mathf.Abs(dx) < 0.5f) return true;
                _scrubLastX = mx;

                var channel = RowChannel(_scrubRow);
                CurrentPose(out var pos, out var rot, out float fov, out var lens);
                var key = KeyAtPlayhead(channel, pos, rot, fov, lens);
                key.SetRaw(channel, key.Raw(channel) + dx * ChannelScrubStep[(int)channel] * (ShiftHeld() ? 10f : CtrlHeld() ? 0.1f : 1f));
                _freeLook = false;
                ApplyTime();
                return true;
            }

            if (_dragChKey == null) return false;

            if (!Input.GetMouseButton(0))
            {
                if (_dragChMoved) Plugin.Log.LogInfo($"moved {_selectedCh.Count} property key(s), grabbed one sits at {Stamp(_dragChKey.time)}");
                _dragChKey = null;
                _dragChMoved = false;
                return true;
            }

            if (!_dragChMoved && (Input.mousePosition - _dragChMouse).magnitude < DRAG_DEADZONE) return true;
            _dragChMoved = true;

            float dt = SnapFrame(time - _dragChGrabTime);
            if (dt != 0f && !_dragChSplit)
            {
                SplitSelection();
                _dragChSplit = true;
            }
            if (_dragChSplit)
            {
                float earliest = float.MaxValue;
                float latest = float.MinValue;
                foreach (float start in _dragKeyStarts)
                {
                    earliest = Mathf.Min(earliest, start);
                    latest = Mathf.Max(latest, start);
                }
                dt = Mathf.Clamp(dt, -earliest, _rec.duration - latest);
                for (int i = 0; i < _dragKeys.Count; i++) _dragKeys[i].time = _dragKeyStarts[i] + dt;
                _rec.SortKeyframes();
            }

            if (_dragChGraph)
            {
                float dv = GraphValue(cursor.y - LANES_Y) - _dragChGrabValue;
                for (int i = 0; i < _selectedCh.Count && i < _dragChValues.Count; i++)
                    _selectedCh[i].key.SetRaw(_selectedCh[i].channel, _dragChValues[i] + dv);
            }
            else if (_dragKeys.Count == 1)
            {
                _time = _dragChKey.time;
                _freeLook = false;
            }
            ApplyTime();
            return true;
        }

        bool HandleLayerLabel(Vector2 cursor, int row)
        {
            if (cursor.x >= _trackLeft) return false;
            if (!Input.GetMouseButtonDown(0)) return true;

            float right = IsChannelRow(row) ? ValueLeft() : _trackLeft;
            if (row < (int)LayerRow.Camera || cursor.x < LabelX(row) || cursor.x >= right) return true;

            bool add = ShiftHeld();
            if (!add) Array.Clear(_graphRow, 0, ROW_COUNT);
            bool any = false;
            for (int r = (int)LayerRow.PositionX; r < ROW_COUNT; r++)
            {
                if (!IsChannelRow(r)) continue;
                bool under = r == row;
                for (int p = RowParent[r]; p >= 0 && !under; p = RowParent[p]) under = p == row;
                if (!under) continue;
                _graphRow[r] = add ? !_graphRow[r] || row != r : true;
                any = true;
            }
            if (any)
            {
                _graphSig = float.NaN;
                RelayoutLayers();
            }

            if (IsChannelRow(row))
            {
                _scrubRow = row;
                _scrubStartX = Input.mousePosition.x;
                _scrubbingValue = false;
            }
            return true;
        }

        ReplayKeyframe KeyAtPlayhead(ReplayChannel c, Vector3 pos, Quaternion rot, float fov, Vector2 lens)
        {
            var k = KeyframeAtPlayhead();
            if (k == null)
            {
                if (KeyframeSpan(out var neighbour, out _, out _)) k = CloneFields(neighbour);
                else
                {
                    k = new ReplayKeyframe();
                    if (_rec.players.Count > 0)
                    {
                        k.targetPlayerId = _rec.players[0].playerId;
                        k.lookAtPlayerId = _rec.players[0].playerId;
                    }
                }
                k.time = _time;
                k.cut = false;
                k.cutToNext = false;
                k.shakeKind = ReplayShakeKind.None;
                for (int i = 0; i < ReplayKeyframe.ChannelCount; i++)
                {
                    k.channels[i].keyed = false;
                    k.SetRaw((ReplayChannel)i, PoseRaw(k, (ReplayChannel)i, pos, rot, fov, lens));
                }
                _rec.keyframes.Add(k);
                _rec.SortKeyframes();
                Plugin.Log.LogInfo($"new camera keyframe at {Stamp(_time)} for {c}");
            }

            var e = k.channels[(int)c];
            if (!e.keyed)
            {
                e.keyed = true;
                k.SetRaw(c, PoseRaw(k, c, pos, rot, fov, lens));
            }
            FocusCurve(k, ChannelRow(c));
            return k;
        }

        void KeyChannelHere(int row)
        {
            var channel = RowChannel(row);
            CurrentPose(out var pos, out var rot, out float fov, out var lens);
            var here = KeyframeAtPlayhead();

            if (here != null && here.channels[(int)channel].keyed
                && Mathf.Abs(PoseRaw(here, channel, pos, rot, fov, lens) - here.Raw(channel)) <= 0.001f)
            {
                here.channels[(int)channel].keyed = false;
                if (!here.AnyKeyed)
                {
                    _rec.keyframes.Remove(here);
                    var window = ReplayKeyframeWindow.Instance;
                    if (window != null && window.Keyframe == here) window.Close();
                }
                _selectedCh.Remove((here, channel));
                if (_curveKey == here && _curveChannel == channel) _curveKey = null;
                Plugin.Log.LogInfo($"{channel} unkeyed at {Stamp(_time)}");
            }
            else
            {
                var key = KeyAtPlayhead(channel, pos, rot, fov, lens);
                key.SetRaw(channel, PoseRaw(key, channel, pos, rot, fov, lens));
                Plugin.Log.LogInfo($"{channel} keyed at {Stamp(_time)}: {key.Raw(channel):0.###}");
            }
            ApplyTime();
            SyncUi();
        }

        void CommitTypedValue(int row, string text)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return;
            var channel = RowChannel(row);
            CurrentPose(out var pos, out var rot, out float fov, out var lens);
            var here = KeyframeAtPlayhead();
            if (here == null || !here.channels[(int)channel].keyed)
            {
                if (Mathf.Abs(PoseWorld(channel, pos, rot, fov, lens) - value) < 0.0005f) return;
            }
            else if (Mathf.Abs(here.Raw(channel) - value) < 0.0005f) return;

            var key = KeyAtPlayhead(channel, pos, rot, fov, lens);
            key.SetRaw(channel, value);
            _freeLook = false;
            Plugin.Log.LogInfo($"{channel} typed in at {Stamp(_time)}: {value:0.###}");
            ApplyTime();
            SyncUi();
        }

        void JumpChannelKey(int row, int dir)
        {
            var channel = RowChannel(row);
            float best = dir < 0 ? float.MinValue : float.MaxValue;
            foreach (var k in _rec.keyframes)
            {
                if (!k.channels[(int)channel].keyed) continue;
                if (dir < 0 ? k.time < _time - FRAME_TIME * 0.5f && k.time > best : k.time > _time + FRAME_TIME * 0.5f && k.time < best)
                    best = k.time;
            }
            if (best == float.MinValue || best == float.MaxValue) return;
            SeekTo(best);
        }

        void FocusCurve(ReplayKeyframe key, int row)
        {
            _curveKey = key;
            if (IsChannelRow(row)) _curveChannel = RowChannel(row);
        }

        bool HandleChannelRow(Vector2 cursor, float time, int row)
        {
            var channel = RowChannel(row);

            if (Input.GetMouseButtonDown(0))
            {
                CloseContextMenu();
                var k = ChannelKeyNear(channel, time);
                if (k == null)
                {
                    if (!ShiftHeld()) Deselect();
                    return true;
                }

                if (ShiftHeld())
                {
                    if (_selectedCh.Remove((k, channel))) return true;
                    _selectedCh.Add((k, channel));
                }
                else if (!_selectedCh.Contains((k, channel)))
                {
                    Deselect();
                    _selectedCh.Add((k, channel));
                }
                UGUIShip.PlaySelectSound();
                FocusCurve(k, row);
                SeekTo(k.time);
                BeginChannelDrag(k, time, 0f, false);
                return true;
            }

            if (Input.GetMouseButtonDown(1))
            {
                CloseContextMenu();
                var k = ChannelKeyNear(channel, time);
                if (k == null)
                {
                    SeekUnlessNear(time);
                    return true;
                }
                if (!_selectedCh.Contains((k, channel)))
                {
                    Deselect();
                    _selectedCh.Add((k, channel));
                }
                FocusCurve(k, row);
                ShowCurveMenu(k, (int)channel, cursor, 0);
                return true;
            }

            return false;
        }

        void ShowCurveMenu(ReplayKeyframe key, int channel, Vector2 cursor, int expand)
        {
            var labels = new List<string>();
            var actions = new List<Action>();
            var ease = key.channels[channel < 0 ? 0 : channel];

            if (channel < 0)
            {
                labels.Add("ui.duplicate");
                actions.Add(() => { CloseContextMenu(); Duplicate(key); });
            }

            labels.Add(LocalizationService.Format("replay.easing_fmt", LocalizationService.Get(EaseIds[(int)ease.curve])));
            actions.Add(() => ShowCurveMenu(key, channel, cursor, expand == 1 ? 0 : 1));
            if (expand == 1)
                for (int i = 0; i < ReplayEase.CurveCount; i++)
                {
                    var curve = (ReplayEasingCurve)i;
                    labels.Add("      " + LocalizationService.Get(EaseIds[i]));
                    actions.Add(() => PickEasing(channel, curve, ease.direction));
                }

            if (ease.curve != ReplayEasingCurve.Linear && ease.curve != ReplayEasingCurve.Constant && ease.curve != ReplayEasingCurve.Custom)
            {
                labels.Add(LocalizationService.Format("replay.easing_direction_fmt", LocalizationService.Get(EaseDirIds[(int)ease.direction])));
                actions.Add(() => ShowCurveMenu(key, channel, cursor, expand == 2 ? 0 : 2));
                if (expand == 2)
                    for (int i = 0; i < ReplayEase.DirectionCount; i++)
                    {
                        var direction = (ReplayEasingDirection)i;
                        labels.Add("      " + LocalizationService.Get(EaseDirIds[i]));
                        actions.Add(() => PickEasing(channel, ease.curve, direction));
                    }
            }

            int selected = channel < 0 ? _selected.Count : _selectedCh.Count;
            labels.Add(selected > 1 ? LocalizationService.Format("replay.delete_keys_fmt", selected) : "replay.delete_key");
            actions.Add(DeleteSelected);

            OpenMenu(cursor, labels.Count);
            for (int i = 0; i < labels.Count; i++) MenuItem(i, labels[i], actions[i]);
        }

        void PickEasing(int channel, ReplayEasingCurve curve, ReplayEasingDirection direction)
        {
            CloseContextMenu();
            int n = 0;
            if (channel < 0)
                foreach (var k in _selected)
                {
                    foreach (var e in k.channels) { e.curve = curve; e.direction = direction; }
                    n++;
                }
            else
                foreach (var (k, c) in _selectedCh)
                {
                    k.channels[(int)c].curve = curve;
                    k.channels[(int)c].direction = direction;
                    n++;
                }

            Plugin.Log.LogInfo($"easing {curve} {direction} on {n} key(s)");
            ApplyTime();
            SyncUi();
        }
    }
}
