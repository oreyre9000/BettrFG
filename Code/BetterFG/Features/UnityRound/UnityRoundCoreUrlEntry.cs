using System;
using BetterFG.Core;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using BetterFG.Tweaks;
using BettrFG.uGUI;
using LevelEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Features.UnityRound
{
    internal static class UnityRoundCoreUrlEntry
    {
        private static ParameterNodeViewModelString _node;
        private static InputField _field;
        private static Canvas _canvas;
        private static string _restore;
        private static Action _onDone;

        internal const string UrlParamName = "Share URL";

        private static readonly string[] ConfirmNames = { "A", "Cross" };

        internal static bool Active => _node != null;

        private static Action _rebuild;

        internal static void SetRebuild(Action rebuild) => _rebuild = rebuild;

        private static bool ConfirmPressed()
            => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
               || NavPromptCore.ElementDownByName(ConfirmNames);

        private static void Begin(ParameterNodeViewModelString node, Action onDone)
        {
            End();
            _node = node;
            _restore = node.Value;
            _onDone = onDone;

            if (_canvas == null)
            {
                _canvas = UGUIShip.CreateCanvas("BetterFG_UrlEntryCanvas");
                _canvas.sortingOrder = 31000;
            }

            _field = UGUIShip.CreateInputField(_canvas.transform, new Rect(-6000f, -6000f, 400f, 32f),
                "unityroundcore.url_prompt", new Color(0f, 0f, 0f, 0f), Color.white, 14);
            UGUIShip.SetInputText(_field, SettingsService.Get(UnityRoundLoader.ShareUrlSettingKey, ""), false);
            _field.onEndEdit.AddListener(new Action<string>(Commit));

            EventSystem.current?.SetSelectedGameObject(_field.gameObject);
            _field.ActivateInputField();
            FGInputLockService.SetParamTypeLock(true);

            _node.Value = LocalizationService.Get("unityroundcore.url_prompt");
        }

        internal static void Tick()
        {
            if (_node == null)
            {
                if (!LevelEditorParameterMenuViewModel.IsParametersScreenOpen()) return;
                if (!ConfirmPressed()) return;

                var node = CreativeTypeValueTweak.SelectedNode();
                if (node == null) return;

                var data = node.ParamData;
                if (data == null) return;
                if (data.ParamName != UrlParamName && data.UnmodifiedParamName != UrlParamName) return;

                Begin(node, _rebuild);
                return;
            }

            if (_field == null || !LevelEditorParameterMenuViewModel.IsParametersScreenOpen()) { Cancel(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }

            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject != _field.gameObject) { Commit(_field.text); return; }

            string typed = _field.text;
            _node.Value = string.IsNullOrEmpty(typed) ? LocalizationService.Get("unityroundcore.url_prompt") : typed;
        }

        private static void Commit(string text)
        {
            if (_node == null) return;

            string url = (text ?? "").Trim();
            var onDone = _onDone;
            End();

            if (url.Length == 0) return;

            SettingsService.Set(UnityRoundLoader.ShareUrlSettingKey, url);

            string code = UnityRoundLoader.ShareCodeFromUrl(url);
            if (string.IsNullOrEmpty(code))
                Plugin.Log.LogWarning($"unity round core: '{url}' isn't a github link to a round's info.json, description left alone");
            else if (UnityRoundLoader.SetLevelDescription(code, out string error))
                Plugin.Log.LogInfo($"unity round core: description set to {code}");
            else
                Plugin.Log.LogWarning($"unity round core: couldn't set the description: {error}");

            onDone?.Invoke();
        }

        private static void Cancel()
        {
            if (_node != null && _restore != null) _node.Value = _restore;
            var onDone = _onDone;
            End();
            onDone?.Invoke();
        }

        private static void End()
        {
            if (_field != null)
            {
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _field.gameObject)
                    EventSystem.current.SetSelectedGameObject(null);
                UnityEngine.Object.Destroy(_field.gameObject);
                _field = null;
            }

            if (_node != null) FGInputLockService.SetParamTypeLock(false);
            _node = null;
            _restore = null;
            _onDone = null;
        }
    }
}
