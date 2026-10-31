using System;
using System.Collections;
using BetterFG.Utilities;
using FGClient;
using MPG.Utility;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BetterFG.UI
{
    internal static class ReportMenuShell
    {
        internal static bool AnyOpen => _vm != null && _vm.gameObject.activeInHierarchy;
        internal static Transform Content => _content;
        internal static bool InputBlocked { get; private set; }

        internal static void BlockInput(bool on)
        {
            InputBlocked = on;
            if (_vm == null) return;
            var key = _vm.transform.Find("Generic_UI_LE_ReportRoundPopup_Prefab_Variant/ButtonContainer/LeftButton")
                ?.GetComponent("OnInputKeyDownClickUIButton")?.TryCast<Behaviour>();
            if (key != null) key.enabled = !on;
        }
        internal static Transform Row(int index) => _handler._settingElements[index].transform;

        private static ReportUGCPopupViewModel _vm;
        private static ReportUGCPopupInputHandler _handler;
        private static Transform _content;
        private static bool _closeOnPress;
        private static int _lastFireFrame = -1;

        internal static IEnumerator Open(string titleId, string bodyText, bool closeOnPress, Action onReady)
        {
            var rm = SingletonBehaviour<ReportManager>.Instance;
            if (rm == null) yield break;

            _vm = null;
            _handler = null;
            _content = null;
            _closeOnPress = closeOnPress;
            InputBlocked = false;
            rm.OpenVisualReportUGCPopup(titleId, "", 0, false);

            for (int attempt = 0; attempt < 12 && _vm == null; attempt++)
            {
                yield return null;
                foreach (var v in Resources.FindObjectsOfTypeAll<ReportUGCPopupViewModel>())
                    if (v != null && v.gameObject.scene.IsValid()) { _vm = v; break; }
            }
            if (_vm == null)
            {
                Plugin.Log.LogWarning("report popup never showed up, giving up on the menu");
                yield break;
            }
            yield return null;

            _handler = _vm._popupInputHandler;
            var elems = _handler._settingElements;
            if (elems.Count < 6) { _vm = null; yield break; }
            _content = elems[0].transform.parent;

            for (int i = 0; i < elems.Count; i++)
            {
                var e = elems[i];
                e.gameObject.SetActive(true);
                Neutralise(e._OnPress);
                Neutralise(e.GetComponent<Button>().onClick);
            }
            for (int i = 0; i < _content.childCount; i++)
            {
                var c = _content.GetChild(i);
                if (c.GetComponent<ReportUGCConfigurationElementViewModel>() == null) c.gameObject.SetActive(false);
            }

            var variant = _vm.transform.Find("Generic_UI_LE_ReportRoundPopup_Prefab_Variant");
            GameObjectHelper.RelabelGameLabel(variant, "TitleContainer/TitleText", titleId);
            variant.Find("BodyText").gameObject.SetActive(bodyText != "");
            GameObjectHelper.RelabelGameLabel(variant, "BodyText", bodyText);
            GameObjectHelper.RelabelGameLabel(variant, "ButtonContainer/LeftButton/Content/Text", "ui.close");
            variant.Find("ButtonContainer/RightButton").gameObject.SetActive(false);

            _lastFireFrame = Time.frameCount;
            onReady();
        }

        internal static void SetHeader(int index, string label)
        {
            var header = _content.GetChild(index == 0 ? 0 : 4);
            header.gameObject.SetActive(true);
            GameObjectHelper.RelabelGameLabel(header, "SettingsHeaderText", label);
        }

        internal static void SetRow(int index, string label, Action onPress)
        {
            var e = _handler._settingElements[index];
            Relabel(index, label);

            var call = (UnityAction)(() => Fire(onPress));
            e.AddOnPressListener(call);
            e.GetComponent<Button>().onClick.AddListener(call);
        }

        internal static void Relabel(int index, string label)
            => GameObjectHelper.RelabelGameLabel(_handler._settingElements[index].transform, "SettingsText", label);

        internal static void HideRow(int index) => _handler._settingElements[index].gameObject.SetActive(false);

        internal static void Close()
        {
            if (_vm != null) _vm.transform.Find("Generic_UI_LE_ReportRoundPopup_Prefab_Variant/BodyText").gameObject.SetActive(true);
            SingletonBehaviour<ReportManager>.Instance?.CloseVisualReportUGCPopup();
            _vm = null;
            InputBlocked = false;
        }

        private static void Neutralise(UnityEvent evt)
        {
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
                evt.SetPersistentListenerState(i, UnityEventCallState.Off);
            evt.RemoveAllListeners();
        }

        private static void Fire(Action act)
        {
            if (Time.frameCount == _lastFireFrame) return;
            _lastFireFrame = Time.frameCount;
            act();
            if (_closeOnPress) Close();
        }
    }
}
