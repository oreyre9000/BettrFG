using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FGClient;
using LevelEditor;
using TMPro;
using UnityEngine;
using BettrFG.uGUI;

namespace BetterFG.Features.CustomBackgrounds
{
    internal static class DisableBackgroundRulebook
    {
        private const string MarkerHex = "#000001";
        private static readonly Vector3 MarkerPos = new Vector3(0f, 5000f, 0f);

        private static int _rowVmId = int.MinValue;
        private static BfgDisableBackgroundRow _live;
        private static GameObject _liveRoot;

        internal static bool IsOurVm(int instanceId) => instanceId == _rowVmId;

        internal static void ReapplyHide(GameObject root) => SetHidden(root, true);

        private static LevelEditorPlaceableObject FindMarker()
        {
            foreach (var lepo in LevelIO.PlaceableObjects)
            {
                if (lepo == null || !lepo.name.StartsWith(Definers.DefinerName, StringComparison.Ordinal)) continue;
                var colour = lepo.GetComponent<LevelEditorColourChangerParameter>();
                if (colour == null || !string.Equals(colour.CurrentColourHexcode, MarkerHex, StringComparison.OrdinalIgnoreCase)) continue;
                BatchTargets.HideAndDecollide(lepo);
                return lepo;
            }
            return null;
        }

        private static bool? _disabled;

        internal static void Invalidate() => _disabled = null;

        internal static bool IsDisabled()
        {
            if (_disabled.HasValue) return _disabled.Value;

            bool on = FindMarker() != null;
            if (!on)
                foreach (var m in IdentifierObjects.ReadRound(Definers.DefinerName))
                    if (string.Equals(m.Hex, MarkerHex, StringComparison.OrdinalIgnoreCase)) { on = true; break; }

            _disabled = on;
            return on;
        }

        internal static void Toggle()
        {
            bool on = !IsDisabled();
            _disabled = on;
            var existing = FindMarker();
            if (on) { if (existing == null) IdentifierObjects.Spawn(Definers.DefinerName, MarkerPos, Vector3.zero, Vector3.one, MarkerHex); }
            else if (existing != null) IdentifierObjects.Remove(existing);

            Plugin.Log.LogInfo($"level background disable -> {on}");
            if (on) BetterFG.Tweaks.Background3dTweak.Cancel();
            SetHidden(_liveRoot ?? ThemeManager._sceneBackgroundAndLighting, on);
            if (!on) BetterFG.Tweaks.Background3dTweak.ApplyIfWanted();
            _live?.Repaint();
        }

        internal static bool Sync(GameObject root)
        {
            if (GameObjectHelper.IsMainMenuUp()) return false;

            _liveRoot = root;
            bool disabled = IsDisabled();

            var host = BeanMonitorService.Instance;
            if (host != null) host.StartCoroutine(DelayedApply(root).WrapToIl2Cpp());
            else SetHidden(root, disabled);

            return disabled;
        }

        private static IEnumerator DelayedApply(GameObject root)
        {
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSeconds(0.5f);
                if (root == null) yield break;
                if (!IsDisabled()) continue;

                SetHidden(root, true);
                yield break;
            }
        }

        internal static void OnMainMenuEntered()
        {
            if (_liveRoot != null) SetHidden(_liveRoot, false);
            _liveRoot = null;
        }

        private static void SetHidden(GameObject root, bool hidden)
        {
            if (root == null) return;

            foreach (var r in root.GetComponents<Renderer>())
                if (r != null) r.forceRenderingOff = hidden;

            var t = root.transform;
            int touched = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (child.name == "LIGHTING") continue;

                foreach (var r in child.GetComponentsInChildren<Renderer>(true))
                    r.forceRenderingOff = hidden;
                touched++;
            }

            Plugin.Log.LogInfo($"background {(hidden ? "off" : "back on")} under {root.name}, {touched} child object(s)");
        }

        public static void InjectRow(global::RulebookMenuCollectionBinding binding)
        {
            try
            {
                if (binding == null) return;
                var r = RulebookRowClone.Inject(binding, BfgDisableBackgroundRow.RowName);
                if (r.Clone == null) return;

                if (r.Created)
                {
                    if (r.CloneVmId != 0) _rowVmId = r.CloneVmId;
                    var row = r.Clone.AddComponent<BfgDisableBackgroundRow>();
                    row.Bind(r.LabelTmp, r.ValueTmp);
                    _live = row;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"disable-background row inject blew up: {ex}"); }
        }
    }

    internal sealed class BfgDisableBackgroundRow : MonoBehaviour
    {
        public const string RowName = "BFG_DisableBackgroundRow";

        private TMP_Text _label;
        private TMP_Text _value;

        public BfgDisableBackgroundRow(IntPtr ptr) : base(ptr) { }

        public void Bind(TMP_Text label, TMP_Text value)
        {
            _label = label;
            _value = value;
            Paint();
        }

        public void Step(int dir) => DisableBackgroundRulebook.Toggle();
        public void Repaint() => Paint();

        private void Paint()
        {
            if (_label != null) UGUIShip.RelabelText(_label, "custombackgrounds.disable_background_label");
            if (_value != null) UGUIShip.RelabelText(_value, DisableBackgroundRulebook.IsDisabled() ? "ui.on" : "ui.off");
        }

        private void LateUpdate() => Paint();
    }
}
