using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BettrFG.uGUI;
using FGClient;
using FGClient.UI.Core;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Features.OptionExpand
{
    public class OptionExpander : MonoBehaviour
    {
        public OptionExpander(IntPtr ptr) : base(ptr) { }

        public static OptionExpander Instance { get; private set; }

        private static readonly string[] PadExpandNames = { "Y", "Triangle" };
        private const string RowPrefix = "BFG_OptionValueRow_";

        private SettingsInputHandler _handler;
        private NavigationPromptData _data;
        private bool _injected;

        private const int MinValuesToExpand = 5;

        private SettingConfigurationViewModel _expandedRow;
        private int _expandedIndex = -1;
        private readonly List<GameObject> _valueRows = new List<GameObject>();
        private readonly List<TMP_Text> _valueMarkers = new List<TMP_Text>();
        private readonly List<string> _valueKeys = new List<string>();
        private Func<string> _readCurrent;
        private string _lastCurrent;
        private UnityAction _onMoved;

        void Awake()
        {
            Instance = this;
        }

        internal static void OnHandlerEnabled(SettingsInputHandler handler)
        {
            if (Instance == null) return;
            Instance._handler = handler;
        }

        internal static void OnHandlerDisabled(SettingsInputHandler handler)
        {
            if (Instance == null || Instance._handler != handler) return;
            Instance.Collapse();
            Instance._handler = null;
        }

        void Update()
        {
            bool live = _handler != null && _handler.isActiveAndEnabled;

            if (live != _injected)
            {
                if (live)
                {
                    if (_data == null)
                        _data = NavPromptInjection.BuildData(NavPrompt.Report,
                            LocalizationService.Get("option_expand.prompt"), "option_expand.prompt",
                            RewiredConsts.Action.Menu_Report, RewiredConsts.Category.Menu);

                    NavPromptInjection.Add(NavPromptInjection.OptionExpand, Toggle, _data,
                        btn => NavPromptCore.ApplyOwnGlyphByElement(btn, KeyCode.N,
                            NavPromptCore.CurrentPadElementByName(PadExpandNames)));
                }
                else
                {
                    NavPromptInjection.Remove(NavPromptInjection.OptionExpand);
                    Collapse();
                }
                _injected = live;
            }

            if (!live) return;

            if (KeybindService.KeyDown(KeyCode.N) || NavPromptCore.ElementDownByName(PadExpandNames))
                Toggle();

        }

        private void Toggle()
        {
            if (_handler == null) return;

            var elems = _handler._settingElements;
            if (elems == null) return;

            int idx = _handler._selectedSettingIndex;
            if (idx < 0 || idx >= elems.Count) return;

            var row = elems[idx];
            if (row == null) return;

            if (_expandedRow == row) { Collapse(); return; }

            Collapse();
            Expand(row, idx);
        }

        private void Expand(SettingConfigurationViewModel row, int index)
        {
            var read = BuildReader(row.gameObject);
            if (read == null) { Plugin.Log.LogInfo($"{row.gameObject.name} has nothing readable to expand"); return; }

            var values = ReadValues(row.gameObject, read);
            if (values == null || values.Count < MinValuesToExpand)
            {
                Plugin.Log.LogInfo($"{row.gameObject.name} only has {(values == null ? 0 : values.Count)} values, not worth expanding");
                return;
            }

            var elems = _handler._settingElements;
            var parent = row.transform.parent;
            int sibling = row.transform.GetSiblingIndex();

            var holder = new GameObject("BFG_OptionExpandHolder");
            holder.SetActive(false);

            for (int i = 0; i < values.Count; i++)
            {
                var go = UnityEngine.Object.Instantiate(row.gameObject, holder.transform);
                go.name = RowPrefix + i;

                var deadVm = go.GetComponent<OptionViewModel>();
                if (deadVm != null) UnityEngine.Object.DestroyImmediate(deadVm);

                go.transform.SetParent(parent, false);
                go.transform.SetSiblingIndex(sibling + 1 + i);
                go.SetActive(true);

                WireValueRow(go, values[i]);

                var cfg = go.GetComponent<SettingConfigurationViewModel>();
                if (cfg != null)
                {
                    _handler.AddSettingElement(cfg, false);
                    elems.Remove(cfg);
                    elems.Insert(index + 1 + i, cfg);
                }

                _valueRows.Add(go);
                _valueKeys.Add(values[i].Key);
            }

            UnityEngine.Object.Destroy(holder);

            _expandedRow = row;
            _expandedIndex = index;
            _readCurrent = read;
            _lastCurrent = null;
            RefreshMarkers();

            _onMoved = (UnityAction)RefreshMarkers;
            row._OnMoveLeft.AddListener(_onMoved);
            row._OnMoveRight.AddListener(_onMoved);

            Plugin.Log.LogInfo($"expanded {row.gameObject.name}, {values.Count} values under it");
        }

        private void RefreshMarkers()
        {
            if (_readCurrent == null) return;

            string now = _readCurrent();
            if (now == _lastCurrent) return;
            _lastCurrent = now;

            for (int i = 0; i < _valueMarkers.Count && i < _valueKeys.Count; i++)
            {
                var marker = _valueMarkers[i];
                if (marker == null) continue;
                marker.text = LocalizationService.Get(_valueKeys[i] == now
                    ? "option_expand.selected"
                    : "option_expand.not_selected");
            }
        }

        private void Collapse()
        {
            if (_expandedRow != null && _onMoved != null)
            {
                _expandedRow._OnMoveLeft.RemoveListener(_onMoved);
                _expandedRow._OnMoveRight.RemoveListener(_onMoved);
                _onMoved = null;
            }

            var elems = _handler != null ? _handler._settingElements : null;

            if (_handler != null && elems != null && elems.Count > 0 && _expandedRow != null)
            {
                int back = -1;
                for (int i = 0; i < elems.Count; i++)
                    if (elems[i] == _expandedRow) { back = i; break; }
                if (back < 0) back = Mathf.Clamp(_expandedIndex, 0, elems.Count - 1);
                _handler.SelectSettingIndex(back, false);
            }

            for (int i = 0; i < _valueRows.Count; i++)
            {
                var go = _valueRows[i];
                if (go == null) continue;

                if (elems != null)
                {
                    var cfg = go.GetComponent<SettingConfigurationViewModel>();
                    if (cfg != null) elems.Remove(cfg);
                }
                UnityEngine.Object.Destroy(go);
            }

            _valueRows.Clear();
            _valueMarkers.Clear();
            _valueKeys.Clear();
            _readCurrent = null;
            _lastCurrent = null;
            _expandedRow = null;
            _expandedIndex = -1;
        }

        private void WireValueRow(GameObject go, ValueEntry entry)
        {
            var texts = go.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t.name == "SettingsText")
                {
                    var loc = t.gameObject.GetComponent("LocalisedStaticLabel")?.TryCast<Behaviour>();
                    if (loc != null) loc.enabled = false;
                    t.text = entry.Label;
                }
                else if (t.name == "SettingsValueText")
                {
                    var bind = t.gameObject.GetComponent("TextBinding")?.TryCast<Behaviour>();
                    if (bind != null) bind.enabled = false;
                    t.text = string.Empty;
                    _valueMarkers.Add(t);
                }
            }

            foreach (var child in go.GetComponentsInChildren<Transform>(true))
                if (child.name.IndexOf("Arrow", StringComparison.OrdinalIgnoreCase) >= 0)
                    child.gameObject.SetActive(false);

            var img = go.GetComponent<Image>();
            if (img == null) img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.01f);
            img.raycastTarget = true;

            int delta = entry.Delta;

            var cfg = go.GetComponent<SettingConfigurationViewModel>();
            if (cfg != null)
            {
                cfg._OnMoveLeft = new UnityEvent();
                cfg._OnMoveRight = new UnityEvent();
                cfg._OnMoveLeft.AddListener((UnityAction)(() => Choose(delta)));
                cfg._OnMoveRight.AddListener((UnityAction)(() => Choose(delta)));
                cfg.AddOnPressListener((UnityAction)(() => Choose(delta)));
            }

            var trigger = go.AddComponent<EventTrigger>();
            var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener((UnityAction<BaseEventData>)(_ => Choose(delta)));
            trigger.triggers.Add(click);
        }

        private void Choose(int delta)
        {
            var target = _expandedRow;
            if (target == null) return;

            for (int i = 0; i < Math.Abs(delta); i++)
            {
                if (delta > 0) target.OnMoveRight();
                else target.OnMoveLeft();
            }

            RefreshMarkers();
        }

        private sealed class ValueEntry
        {
            public string Label;
            public string Key;
            public int Delta;
        }

        private static Func<string> BuildReader(GameObject rowGo)
        {
            try
            {
                var option = rowGo.GetComponent<OptionViewModel>();
                if (option == null) return null;

                object wrapper = ConcreteWrapper(option);
                if (wrapper == null) return null;

                var strProp = FindDisplayProperty(wrapper.GetType());
                if (strProp == null) return null;

                return () => strProp.GetValue(wrapper) as string ?? string.Empty;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"no readable value on {rowGo.name}: {ex.Message}");
                return null;
            }
        }

        private static PropertyInfo FindDisplayProperty(Type type)
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                if (p.PropertyType != typeof(string) || !p.CanRead) continue;
                if (p.Name.EndsWith("Str", StringComparison.Ordinal) || p.Name == "ValueString") return p;
            }
            return null;
        }

        private static List<ValueEntry> ReadValues(GameObject rowGo, Func<string> read)
        {
            var cfg = rowGo.GetComponent<SettingConfigurationViewModel>();
            if (cfg == null) return null;

            TMP_Text shown = null;
            var texts = rowGo.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
                if (texts[i].name == "SettingsValueText") { shown = texts[i]; break; }
            if (shown == null) return null;

            try { return Walk(cfg, read, shown); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"walking {rowGo.name} threw, leaving it alone: {ex.Message}");
                return null;
            }
        }

        private static List<ValueEntry> Walk(SettingConfigurationViewModel cfg, Func<string> read, TMP_Text shown)
        {
            string start = read();
            if (string.IsNullOrEmpty(start)) return null;

            string startLabel = shown.text;
            var up = new List<ValueEntry>();
            var down = new List<ValueEntry>();
            var seen = new HashSet<string> { start };

            for (int i = 0; i < 48; i++)
            {
                cfg.OnMoveRight();
                string k = read();
                if (k == start || !seen.Add(k)) break;
                up.Add(new ValueEntry { Key = k, Label = shown.text, Delta = i + 1 });
            }
            for (int i = 0; i < up.Count; i++) cfg.OnMoveLeft();

            for (int i = 0; i < 48; i++)
            {
                cfg.OnMoveLeft();
                string k = read();
                if (k == start || !seen.Add(k)) break;
                down.Add(new ValueEntry { Key = k, Label = shown.text, Delta = -(i + 1) });
            }
            for (int i = 0; i < down.Count; i++) cfg.OnMoveRight();

            if (read() != start)
            {
                Plugin.Log.LogWarning($"walk drifted off {start} and landed on {read()}, not expanding this one");
                return null;
            }

            var list = new List<ValueEntry>(down.Count + 1 + up.Count);
            for (int i = down.Count - 1; i >= 0; i--) list.Add(down[i]);
            list.Add(new ValueEntry { Key = start, Label = startLabel, Delta = 0 });
            list.AddRange(up);
            return list;
        }

        private static object ConcreteWrapper(Il2CppObjectBase obj)
        {
            var klass = IL2CPP.il2cpp_object_get_class(obj.Pointer);
            string name = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(klass));
            string ns = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_namespace(klass));
            if (string.IsNullOrEmpty(name)) return null;

            string full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            var type = typeof(OptionViewModel).Assembly.GetType(full);
            if (type == null) return null;

            return Activator.CreateInstance(type, obj.Pointer);
        }
    }

    [HarmonyPatch(typeof(SettingsInputHandler), "OnEnable")]
    internal static class OptionExpanderEnablePatch
    {
        [HarmonyPostfix]
        public static void Postfix(SettingsInputHandler __instance) => OptionExpander.OnHandlerEnabled(__instance);
    }

    [HarmonyPatch(typeof(SettingsInputHandler), "OnDisable")]
    internal static class OptionExpanderDisablePatch
    {
        [HarmonyPostfix]
        public static void Postfix(SettingsInputHandler __instance) => OptionExpander.OnHandlerDisabled(__instance);
    }
}
