using System;
using System.Collections.Generic;
using System.Globalization;
using LevelEditor;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using NodeEntry = LevelEditorParameterMenuViewModel.ParameterMenuNodeEntry;

namespace BetterFG.Features.CreativeExpansion
{
    public static class ParamSliders
    {
        internal static readonly Color Dark = new Color(0.09f, 0.11f, 0.22f, 1f);
        internal static Sprite SmallCircle, BigCircle;

        private static readonly HashSet<string> _names = new HashSet<string>
        {
            "wle_length", "wle_height", "wle_depth", "wle_width", "wle_scale",
            "wle_rows", "wle_columns", "wle_size",
            "wle_param_30",
            "wle_param_movement_controller_pause_delay",
            "wle_param_102",
            "wle_param_respawn_time_min", "wle_param_respawn_time_max",
            "wle_param_points_awarded", "wle_param_points_scored", "wle_param_scoring_frequency",
            "wle_param_destructibleobject_hits_required", "wle_param_destructibleobject_points_awarded",
            "wle_param_quantity",
            "wle_param_43", "wle_max_tilt",
            "wle_param_layer_order",
        };

        public static NodeEntry Slider(this NodeEntry row)
        {
            if (row != null) _names.Add(row._nodeData.ParamName);
            return row;
        }

        internal static bool IsSlider(string paramName, Il2CppSystem.Collections.ArrayList items) =>
            paramName != null && _names.Contains(paramName) && NumericRange(items, out _, out _);

        internal static bool Number(string s, out float v)
        {
            v = 0f;
            if (string.IsNullOrEmpty(s)) return false;
            int end = 0;
            while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.' || s[end] == ',' || (end == 0 && s[end] == '-'))) end++;
            return end > 0 && float.TryParse(s.Substring(0, end).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        internal static bool NumericRange(Il2CppSystem.Collections.ArrayList items, out float min, out float max)
        {
            min = max = 0f;
            if (items == null || items.Count < 3) return false;
            return Number(items[0]?.ToString(), out min) && Number(items[1]?.ToString(), out float second)
                && Number(items[items.Count - 1]?.ToString(), out max) && min < second && second <= max;
        }

        internal static int Nearest(Il2CppSystem.Collections.ArrayList items, float value)
        {
            int best = 0;
            float bestGap = float.MaxValue;
            for (int i = 0; i < items.Count; i++)
            {
                if (!Number(items[i]?.ToString(), out float v)) continue;
                float gap = Mathf.Abs(v - value);
                if (gap < bestGap) { bestGap = gap; best = i; }
            }
            return best;
        }

        internal static void Apply(ParameterMenuCollectionBinding binding)
        {
            var content = binding._itemsParent;
            var entries = binding.GetComponent<LevelEditorParameterMenuViewModel>()?.NodeEntries;
            bool on = FeatureCreativeExpansion.On("paramsliders");

            int e = 0;
            for (int i = 0; i < content.childCount; i++)
            {
                var row = content.GetChild(i);
                if (row.name.StartsWith(ParamCategories.HeaderPrefix)) continue;

                var old = row.GetComponent<ParamSliderRow>();
                if (old != null) { old.Revert(); UnityEngine.Object.DestroyImmediate(old); }

                var entry = entries != null && e < entries.Length ? entries[e] : null;
                e++;
                if (!on || entry == null || !row.gameObject.activeSelf || entry.NodeType != ParameterNodeType.String
                    || !IsSlider(entry._nodeData.ParamName, entry._nodeData.SelectionItems)) continue;

                NumericRange(entry._nodeData.SelectionItems, out float min, out float max);
                if (SmallCircle == null)
                    foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                    {
                        if (s.name == "UI_32xCircle_Image") SmallCircle = s;
                        else if (s.name == "UI_Circle") BigCircle = s;
                    }

                var slider = row.gameObject.AddComponent<ParamSliderRow>();
                var track = slider.Init(min, max);
                var trigger = track.gameObject.AddComponent<EventTrigger>();
                foreach (var type in new[] { EventTriggerType.PointerDown, EventTriggerType.Drag })
                {
                    var entryCb = new EventTrigger.Entry { eventID = type };
                    entryCb.callback.AddListener((Action<BaseEventData>)(ev =>
                    {
                        var pe = ev.TryCast<PointerEventData>();
                        if (pe != null && slider != null) slider.DragTo(pe.position, pe.pressEventCamera);
                    }));
                    trigger.triggers.Add(entryCb);
                }
            }
        }

        internal static Image Img(string name, Transform parent, Sprite sprite, Color colour, bool sliced)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var im = go.AddComponent<Image>();
            im.sprite = sprite;
            im.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            im.color = colour;
            im.raycastTarget = false;
            return im;
        }
    }

    internal sealed class ParamSliderRow : MonoBehaviour
    {
        private const int MaxStepsPerDrag = 400;
        private static readonly string[] Arrows = { "SettingsLeftArrow", "SettingsRightArrow", "SettingsLeftArrowDisabled", "SettingsRightArrowDisabled" };
        private static readonly string[] Values = { "ParamValue", "ParamValueDisabled" };

        private float _min, _max, _current;
        private bool _numeric;
        private string _shown;
        private Vector2[] _valueAnchors;
        private TextAlignmentOptions[] _valueAlign;
        private ParameterNodeViewModelString _node;
        private TextMeshProUGUI _value;
        private GameObject _disabled;
        private CanvasGroup _selected;
        private RectTransform _area, _outline, _fill, _handle;
        private Image _ring, _knob;

        public ParamSliderRow(IntPtr ptr) : base(ptr) { }

        public RectTransform Init(float min, float max)
        {
            _min = min;
            _max = max;
            _node = GetComponent<ParameterNodeViewModelString>();
            _value = transform.Find("ParamValue").GetComponent<TextMeshProUGUI>();
            _disabled = transform.Find("ParamValueDisabled").gameObject;
            var selected = transform.Find("BackgroundSelected");
            _selected = selected.GetComponent<CanvasGroup>();

            foreach (var p in Arrows) transform.Find(p).localScale = Vector3.zero;

            _valueAnchors = new Vector2[Values.Length * 2];
            _valueAlign = new TextAlignmentOptions[Values.Length];
            for (int i = 0; i < Values.Length; i++)
            {
                var t = transform.Find(Values[i]).GetComponent<TextMeshProUGUI>();
                var r = t.rectTransform;
                _valueAnchors[i * 2] = r.anchorMin;
                _valueAnchors[i * 2 + 1] = r.anchorMax;
                _valueAlign[i] = t.alignment;
                r.anchorMin = new Vector2(0.87f, r.anchorMin.y);
                r.anchorMax = new Vector2(0.96f, r.anchorMax.y);
                t.alignment = TextAlignmentOptions.MidlineRight;
            }

            _area = new GameObject("BFG_SliderArea").AddComponent<RectTransform>();
            _area.gameObject.layer = gameObject.layer;
            _area.SetParent(transform, false);
            _area.anchorMin = new Vector2(0.45f, 0f);
            _area.anchorMax = new Vector2(0.86f, 1f);
            _area.offsetMin = Vector2.zero;
            _area.offsetMax = Vector2.zero;

            _outline = ParamSliders.Img("TrackOutline", _area, ParamSliders.SmallCircle, Color.white, true).rectTransform;
            _outline.anchorMin = new Vector2(0f, 0.5f);
            _outline.anchorMax = new Vector2(1f, 0.5f);
            _outline.sizeDelta = new Vector2(0f, 26f);
            _outline.GetComponent<Image>().raycastTarget = true;
            var inner = ParamSliders.Img("Track", _outline, ParamSliders.SmallCircle, ParamSliders.Dark, true).rectTransform;
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(3f, 3f);
            inner.offsetMax = new Vector2(-3f, -3f);
            _fill = ParamSliders.Img("Fill", inner, ParamSliders.SmallCircle, selected.GetComponent<Image>().color, true).rectTransform;
            _fill.anchorMin = Vector2.zero;
            _fill.anchorMax = new Vector2(0f, 1f);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.offsetMin = Vector2.zero;
            _fill.offsetMax = Vector2.zero;

            _handle = new GameObject("Handle").AddComponent<RectTransform>();
            _handle.gameObject.layer = gameObject.layer;
            _handle.SetParent(_outline, false);
            _handle.anchorMin = _handle.anchorMax = new Vector2(0f, 0.5f);
            _handle.sizeDelta = new Vector2(36f, 36f);
            _ring = ParamSliders.Img("Ring", _handle, ParamSliders.BigCircle, Color.white, false);
            _ring.rectTransform.anchorMin = Vector2.zero;
            _ring.rectTransform.anchorMax = Vector2.one;
            _ring.rectTransform.sizeDelta = Vector2.zero;
            _knob = ParamSliders.Img("Knob", _handle, ParamSliders.BigCircle, Color.white, false);
            _knob.rectTransform.anchorMin = Vector2.zero;
            _knob.rectTransform.anchorMax = Vector2.one;
            _knob.rectTransform.offsetMin = new Vector2(4f, 4f);
            _knob.rectTransform.offsetMax = new Vector2(-4f, -4f);
            return _outline;
        }

        public void DragTo(Vector2 screen, Camera cam)
        {
            if (_disabled.activeSelf) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_outline, screen, cam, out var local)) return;
            float f = Mathf.Clamp01((local.x - _outline.rect.xMin - 18f) / Mathf.Max(1f, _outline.rect.width - 36f));
            int target = Mathf.RoundToInt(f * (_node._numOptions - 1));
            for (int i = 0; i < MaxStepsPerDrag && _node.SelectedOption != target; i++)
            {
                int before = _node.SelectedOption;
                if (target > before) _node.Increment();
                else _node.Decrement();
                if (_node.SelectedOption == before) break;
            }
        }

        public void Revert()
        {
            foreach (var p in Arrows) transform.Find(p).localScale = Vector3.one;
            for (int i = 0; i < Values.Length; i++)
            {
                var t = transform.Find(Values[i]).GetComponent<TextMeshProUGUI>();
                t.rectTransform.anchorMin = _valueAnchors[i * 2];
                t.rectTransform.anchorMax = _valueAnchors[i * 2 + 1];
                t.alignment = _valueAlign[i];
            }
            DestroyImmediate(_area.gameObject);
        }

        private void LateUpdate()
        {
            string now = _value.text;
            if (now != _shown)
            {
                _shown = now;
                _numeric = ParamSliders.Number(now, out _current);
                _area.gameObject.SetActive(_numeric);
            }
            if (_numeric)
            {
                float hx = Mathf.Lerp(18f, Mathf.Max(18f, _outline.rect.width - 18f), Mathf.InverseLerp(_min, _max, _current));
                _handle.anchoredPosition = new Vector2(hx, 0f);
                _fill.sizeDelta = new Vector2(Mathf.Max(0f, hx - 3f), 0f);
            }

            bool off = _disabled.activeSelf;
            var c = new Color(1f, 1f, 1f, off ? 0.4f : 1f);
            _ring.color = c;
            _knob.color = c;
            _handle.localScale = !off && _selected.alpha > 0.5f ? new Vector3(1.18f, 1.18f, 1f) : Vector3.one;
        }
    }
}
