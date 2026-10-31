using System;
using System.Globalization;
using BetterFG.Features.CreativeExpansion;
using BetterFG.Features.CreativeIncrements;
using BetterFG.Core;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using BetterFG.UI;
using FGClient.UI.Core;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class CreativeTypeValueTweak : BfgTweak
    {
        public CreativeTypeValueTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "creative_type_value";
        public override string TweakLabel => "tweak.type_parameter_values";
        public override bool DefaultEnabled => true;

        public static CreativeTypeValueTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private const string NumberPrompt = "ui.write_a_number";
        private const string NamePrompt = "ui.write_a_name";
        private const int NameLimit = 32;
        private static readonly string[] ConfirmNames = { "A", "Cross" };

        private ParameterNodeViewModelString _node;
        private bool _naming;
        private string _typed;
        private string _restore;
        private NavPromptHandle _glyph;
        private int _glyphIndex = -1;
        private IntPtr _glyphNode;
        private RectTransform _title;

        public override void DisableTweak() { Cancel(); ClearGlyph(); }

        void Update()
        {
            if (_node == null)
            {
                if (!UnityRoundLoader.InLevelEditor || !LevelEditorParameterMenuViewModel.IsParametersScreenOpen())
                { ClearGlyph(); return; }
                SyncGlyph();
                bool pad = NavPromptCore.ElementDownByName(ConfirmNames);
                if (pad || EnterDown()) Begin(pad);
                return;
            }

            // the node can go away under us (menu rebuild, object swap, menu closed) while we hold it
            if (!LevelEditorParameterMenuViewModel.IsParametersScreenOpen()) { Cancel(); return; }

            if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
            if (Input.GetMouseButtonDown(0)) { Commit(); return; }

            foreach (char c in Input.inputString)
            {
                if (c == '\n' || c == '\r') { Commit(); return; }
                if (c == '\b') { if (_typed.Length > 0) _typed = _typed.Substring(0, _typed.Length - 1); }
                else _typed = Filter(_typed + c);
            }

            _node.Value = _typed.Length == 0 ? Prompt() : _typed;
        }

        private static bool EnterDown() =>
            Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

        private string Prompt() => LocalizationService.Get(_naming ? NamePrompt : NumberPrompt);

        private void SyncGlyph()
        {
            var vm = LevelEditorParameterMenuViewModel._instance;
            if (vm == null) { ClearGlyph(); return; }

            var node = SelectedNode();
            IntPtr ptr = node == null ? IntPtr.Zero : node.Pointer;
            if (ptr == _glyphNode && vm.SelectedIndex == _glyphIndex && (_glyph == null || _glyph.IsAlive)) return;

            ClearGlyph();
            _glyphNode = ptr;
            _glyphIndex = vm.SelectedIndex;

            if (node == null || !Typeable(node)) return;

            var handlers = vm._inputHandlers;
            int submit = handlers != null && handlers.Count > 0 ? handlers[0].SubmitAction : -1;
            _glyph = NavPromptCore.From(NavPrompt.Report)
                .WithLabel("", "bfg_creative_typevalue")
                .CustomAnchors(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(18f, 0f))
                .NoAutoResize()
                .PollActions(submit)
                .AlsoAcceptKey(KeyCode.Return)
                .OwnGlyph()
                .AllowWhileUnfocused()
                .SpawnOn(node.transform);
            if (_glyph == null)
            {
                Plugin.Log?.LogWarning($"type glyph: nothing spawned on row {_glyphIndex}, submit action {submit}");
                return;
            }

            _glyph.GameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(40f, 32f);
            _glyph.GameObject.transform.Find("PromptText").gameObject.SetActive(false);
            _title = node.transform.Find("ParamTitle").GetComponent<RectTransform>();
            _title.offsetMin = new Vector2(42f, _title.offsetMin.y);
        }

        private void ClearGlyph()
        {
            if (_title != null) { _title.offsetMin = new Vector2(0f, _title.offsetMin.y); _title = null; }
            _glyph?.Destroy();
            _glyph = null;
            _glyphIndex = -1;
            _glyphNode = IntPtr.Zero;
        }

        private bool Typeable(ParameterNodeViewModelString node)
        {
            var data = node.ParamData;
            if (data.ParamName == CreativeGroups.PickParamName || data.UnmodifiedParamName == CreativeGroups.PickParamName)
                return CreativeGroups.HasParamGroup;
            return IsEnabled && Numeric(node);
        }

        private static bool Numeric(ParameterNodeViewModelString node) =>
            Resolve(node, out _, out _, out _, out _) || ParamSliders.IsSlider(node.ParamData.ParamName, node.ParamData.SelectionItems);

        private string Filter(string s)
        {
            string r = "";
            foreach (char c in s)
            {
                if (_naming) { if (c >= ' ' && c != '|' && c != ',' && r.Length < NameLimit) r += c; }
                else if (c == ',' || c == '.') { if (r.IndexOf('.') < 0) r += '.'; }
                else if (c == '-') { if (r.Length == 0) r += '-'; }
                else if (c >= '0' && c <= '9') r += c;
            }
            return r;
        }

        private void Begin(bool pad)
        {
            var node = SelectedNode();
            if (node == null) return;

            var data = node.ParamData;
            if (data.ParamName == CreativeGroups.PickParamName || data.UnmodifiedParamName == CreativeGroups.PickParamName)
            {
                if (!CreativeGroups.HasParamGroup) return;
                Arm(node, true, pad);
                return;
            }

            if (!IsEnabled) return;

            // only numeric nodes carry a value closure. bail on the rest (bools, colours, buttons) so
            // Enter keeps doing whatever the editor does on them
            if (!Numeric(node)) return;

            Arm(node, false, pad);
        }

        private void Arm(ParameterNodeViewModelString node, bool naming, bool pad)
        {
            _node = node;
            _naming = naming;
            _typed = "";
            _restore = node.Value;
            node.Value = Prompt();
            FGInputLockService.SetParamTypeLock(true);
            ClearGlyph();
            if (pad) OnScreenKeyboard.OpenExternal(() => _typed, v => _typed = Filter(v), ok => { if (ok) Commit(); else Cancel(); });
        }

        private void Cancel()
        {
            if (_node != null && _restore != null) _node.Value = _restore;
            End();
        }

        private void End()
        {
            _node = null;
            _naming = false;
            _typed = null;
            _restore = null;
            FGInputLockService.SetParamTypeLock(false);
            OnScreenKeyboard.CloseExternal(this);
        }

        private void Commit()
        {
            var node = _node;
            string typed = _typed;
            if (node == null) { End(); return; }

            if (_naming)
            {
                if (string.IsNullOrEmpty(typed) || !CreativeGroups.RenameParamGroup(typed, out string named))
                { Cancel(); return; }

                node.Value = named;
                var options = node.ParamData.SelectionItems;
                int at = node.SelectedOption;
                if (options != null && at >= 0 && at < options.Count) options[at] = (Il2CppSystem.String)named;
                End();
                return;
            }

            if (!float.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            { Cancel(); return; }

            if (!Resolve(node, out var floatCb, out var intCb, out float min, out float step))
            {
                var items = node.ParamData.SelectionItems;
                if (!ParamSliders.IsSlider(node.ParamData.ParamName, items)) { Cancel(); return; }

                int at = ParamSliders.Nearest(items, value);
                (node._onChangedIndex ?? node.ParamData.OnChangedIndex).Invoke(at);
                node.SelectedOption = at;
                node.Value = items[at].ToString();
                End();
                return;
            }

            try
            {
                if (intCb != null) { value = Mathf.Round(value); intCb.Invoke((int)value); }
                else floatCb.Invoke(value);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("CreativeTypeValue: writing " + value + " failed " + ex.Message);
                Cancel();
                return;
            }

            // keep the node showing what we wrote, and park its index on the nearest grid point so a
            // follow-up arrow press steps from roughly here instead of jumping back to where it was
            node.Value = Format(node, value);
            if (step > 0f)
            {
                int idx = Mathf.Max(0, Mathf.RoundToInt((value - min) / step));
                var items = node.ParamData.SelectionItems;
                int count = items != null ? items.Count : 0;
                if (count > 0 && idx > count - 1) idx = count - 1;
                node.SelectedOption = idx;
            }

            // CreativeIncrements reopens each node at round((value - min) / step) from this map, so an
            // off-grid typed value still comes back to the right place next time the menu opens
            string paramName = node.ParamData.ParamName;
            if (!string.IsNullOrEmpty(paramName)) CreativeIncrements.LastValues[paramName] = value;

            End();
        }

        private static string Format(ParameterNodeViewModelString node, float value)
        {
            string fmt = node.ParamData.numberFormat;
            try { return value.ToString(string.IsNullOrEmpty(fmt) ? "G" : fmt, CultureInfo.InvariantCulture); }
            catch { return value.ToString(CultureInfo.InvariantCulture); }
        }

        // the node the parameter menu's own input handler says is selected. same route CreativeIncrements
        // takes to the handler, so no scene scan
        internal static ParameterNodeViewModelString SelectedNode()
        {
            var vm = LevelEditorParameterMenuViewModel._instance;
            if (vm == null) return null;

            var handlers = vm._inputHandlers;
            if (handlers == null) return null;

            for (int i = 0; i < handlers.Count; i++)
            {
                var pmih = handlers[i]?.TryCast<ParametersMenuInputHandler>();
                if (pmih == null) continue;

                var go = pmih.GetSelectedObjectByIndex();
                if (go == null) return null;
                return go.GetComponent<ParameterNodeViewModelString>();
            }
            return null;
        }

        // digs the value-writing callback out of the closure ParameterUtils captured when it built the
        // node. CreateEntry wraps whatever CreateFloatEntry/CreateIntEntry passed it in a closure of its
        // own, so step through that first. min/step come back for the index we park the node on after.
        private static bool Resolve(ParameterNodeViewModelString node,
            out Il2CppSystem.Action<float> floatCallback, out Il2CppSystem.Action<int> intCallback,
            out float min, out float step)
        {
            floatCallback = null; intCallback = null; min = 0f; step = 0f;
            if (node == null) return false;

            var changed = node._onChangedIndex ?? node.ParamData.OnChangedIndex;
            var target = changed?.Target;
            if (target == null) return false;

            var wrapper = target.TryCast<ParameterUtils.__c__DisplayClass10_0>();
            if (wrapper != null) target = wrapper.onChangedIndex?.Target;
            if (target == null) return false;

            var asFloat = target.TryCast<ParameterUtils.__c__DisplayClass5_0>();
            if (asFloat != null)
            {
                min = asFloat.min; step = asFloat.step; floatCallback = asFloat.callback;
                return floatCallback != null;
            }

            var asInt = target.TryCast<ParameterUtils.__c__DisplayClass3_0>();
            if (asInt != null)
            {
                min = asInt.min; step = asInt.step; intCallback = asInt.callback;
                return intCallback != null;
            }

            return false;
        }
    }
}
