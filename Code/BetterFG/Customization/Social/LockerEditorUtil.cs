using System;
using System.Collections;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.UI;
using FGClient;
using FGClient.Customiser;
using FGClient.UI;
using MPG.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.Social
{
    internal static class LockerEditorUtil
    {
        private static Sprite _plusSprite;
        private static Texture2D _previewTex;
        private static Image _preview;

        internal static string None => LocalizationService.Get("emoticon_editor.none");

        internal static bool IsMenuWheel(SocialWheelViewModel wheel)
            => wheel.GetComponentInParent<CustomiserWheelViewModel>() != null || wheel.GetComponentInParent<MainMenuWheelViewModel>() != null;

        internal static Sprite PlusSprite()
        {
            if (_plusSprite != null) return _plusSprite;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool arm = Math.Abs(x - 64) <= 11 || Math.Abs(y - 64) <= 11;
                    bool inside = Math.Abs(x - 64) <= 40 && Math.Abs(y - 64) <= 40;
                    px[y * size + x] = arm && inside ? Color.white : new Color(0f, 0f, 0f, 0f);
                }
            tex.SetPixels(px);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _plusSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            _plusSprite.hideFlags = HideFlags.HideAndDontSave;
            return _plusSprite;
        }

        internal static void ReopenAfterClose(Action open)
        {
            var popup = ReportMenuShell.Content.gameObject;
            ReportMenuShell.Close();
            BeanMonitorService.Instance.StartCoroutine(OpenSoon(open, popup).WrapToIl2Cpp());
        }

        private static IEnumerator OpenSoon(Action open, GameObject closing)
        {
            float until = Time.unscaledTime + 2f;
            while (closing != null && closing.activeInHierarchy && Time.unscaledTime < until) yield return null;
            yield return null;
            open();
        }

        private static IEnumerator UnblockSoon()
        {
            yield return new WaitForSecondsRealtime(0.3f);
            ReportMenuShell.BlockInput(false);
        }

        internal static void PickPng(string titleKey, Action<string> onPicked)
        {
            ReportMenuShell.BlockInput(true);
            WinDialogs.PickPng(LocalizationService.Get(titleKey), path =>
            {
                BeanMonitorService.Instance.StartCoroutine(UnblockSoon().WrapToIl2Cpp());
                if (string.IsNullOrEmpty(path) || !ReportMenuShell.AnyOpen) return;
                onPicked(path);
            });
        }

        internal static void PickAudio(string titleKey, Action<string> onPicked)
        {
            ReportMenuShell.BlockInput(true);
            WinDialogs.PickAudio(LocalizationService.Get(titleKey), path =>
            {
                BeanMonitorService.Instance.StartCoroutine(UnblockSoon().WrapToIl2Cpp());
                if (string.IsNullOrEmpty(path) || !ReportMenuShell.AnyOpen) return;
                onPicked(path);
            });
        }

        internal static void AddPreview(int afterRow, string imagePath)
        {
            var content = ReportMenuShell.Content;
            var go = new GameObject("BFG_LockerPreview");
            go.transform.SetParent(content, false);
            go.transform.SetSiblingIndex(ReportMenuShell.Row(afterRow).GetSiblingIndex() + 1);
            go.AddComponent<LayoutElement>().preferredHeight = 220f;
            _preview = go.AddComponent<Image>();
            _preview.preserveAspect = true;
            _preview.raycastTarget = false;
            go.SetActive(false);
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath)) ShowPreview(imagePath);
        }

        internal static void ShowPreview(string path)
        {
            if (_preview == null) return;
            if (_previewTex != null) UnityEngine.Object.Destroy(_previewTex);
            _previewTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _previewTex.LoadImage(File.ReadAllBytes(path));
            _previewTex.Apply();
            var old = _preview.sprite;
            _preview.sprite = Sprite.Create(_previewTex, new Rect(0, 0, _previewTex.width, _previewTex.height), new Vector2(0.5f, 0.5f));
            if (old != null) UnityEngine.Object.Destroy(old);
            _preview.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(ReportMenuShell.Content.GetComponent<RectTransform>());
        }

        private static ModalMessageWithInputFieldViewModel _textModal;
        private static bool _textModalPending;
        private static global::UI.OnInputKeyDownClickUIButton _okShortcut;

        internal static void OpenTextModal(string titleKey, string current, string placeholderKey, Action<string> onOk)
        {
            var d = new ModalMessageWithInputFieldData
            {
                Title = LocalizationService.Get(titleKey),
                Message = " ",
                LocaliseTitle = UIModalMessage.LocaliseOption.NotLocalised,
                LocaliseMessage = UIModalMessage.LocaliseOption.NotLocalised,
                ModalType = UIModalMessage.ModalType.MT_OK_CANCEL,
                InputText = current,
                InputTextPlaceholder = LocalizationService.Get(placeholderKey),
                TrimSpacesOnClose = true
            };
            d.OnInputFieldModalClosed = (Action<bool, string>)((ok, txt) =>
            {
                ReleaseTextModal();
                if (!ok || !ReportMenuShell.AnyOpen) return;
                onOk(txt ?? "");
            });
            _textModal = null;
            _textModalPending = SingletonBehaviour<PopupManager>.Instance.Show(PopupInteractionType.Query, d, null);
        }

        private static void ReleaseTextModal()
        {
            if (_okShortcut != null && !_textModal._inputListeners.Contains(_okShortcut)) _textModal._inputListeners.Add(_okShortcut);
            _okShortcut = null;
            _textModal = null;
            _textModalPending = false;
        }

        internal static void TickTextModal()
        {
            if (!_textModalPending) return;
            if (_textModal == null)
            {
                foreach (var vm in Resources.FindObjectsOfTypeAll<ModalMessageWithInputFieldViewModel>())
                    if (vm.gameObject.activeInHierarchy) { _textModal = vm; break; }
                if (_textModal == null) return;
                _okShortcut = _textModal._buttonOKFill.GetComponentInParent<global::UI.OnInputKeyDownClickUIButton>();
                Plugin.Log.LogInfo("text modal up, pulling its confirm shortcut: " + _textModal._inputListeners.Remove(_okShortcut));
            }
            if (!_textModal.gameObject.activeInHierarchy) { ReleaseTextModal(); return; }
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
            if (string.IsNullOrWhiteSpace(_textModal._inputField.text)) return;
            _textModal.HandleButtonOK();
        }

        internal static string FileLabel(string key, string path)
            => string.Format(LocalizationService.Get(key), string.IsNullOrEmpty(path) ? None : Path.GetFileName(path));

        internal static string SoundLabel(int i, string path)
            => string.Format(LocalizationService.Get("emoticon_editor.row_sound"), i + 1, string.IsNullOrEmpty(path) ? None : Path.GetFileName(path));
    }
}
