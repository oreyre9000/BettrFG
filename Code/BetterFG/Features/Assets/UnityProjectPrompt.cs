using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.Onboarding;
using BetterFG.Services;
using BetterFG.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.Assets
{
    public class UnityProjectPrompt : PromptHost
    {
        public UnityProjectPrompt(IntPtr ptr) : base(ptr) { }

        private const int CanvasOrder = 1402;
        private const int BlockerOrder = 1401;

        private static UnityProjectPrompt _live;

        private InputField _field;
        private Action<bool> _done;

        public static void Open(Action<bool> done = null)
        {
            if (_live != null) { done?.Invoke(UnityProject.Configured); return; }

            var go = new GameObject("BetterFG_UnityProjectPrompt");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            var p = go.AddComponent<UnityProjectPrompt>();
            p._done = done;
            _live = p;
            p.Build();
        }

        private void Build()
        {
            CreateSurfaces("UnityProject", CanvasOrder, BlockerOrder);
            ShowPopup();
        }

        private void ShowPopup()
        {
            Swap(OnboardingPopup.Show(_canvas, "asset_export.project_title",
                "asset_export.project_body", Vector2.zero, new Vector2(0.5f, 0.5f),
                new[]
                {
                    new OnboardingPopup.ButtonSpec("asset_export.project_cancel",
                        new Action(Cancel), OnboardingPopup.BtnGhost),
                    new OnboardingPopup.ButtonSpec("asset_export.project_browse",
                        new Action(Browse), OnboardingPopup.BtnGhost),
                    new OnboardingPopup.ButtonSpec("asset_export.project_save",
                        new Action(Save), OnboardingPopup.BtnPrimary),
                },
                UnityProject.AssetsPath,
                new Action<InputField>(f => _field = f)));
        }

        private void Browse()
        {
            WinDialogs.PickFolder(LocalizationService.Get("asset_export.project_title"),
                new Action<string>(picked =>
                {
                    string resolved = UnityProject.Resolve(picked);
                    if (resolved == null)
                    {
                        Plugin.Log.LogWarning("that folder isn't a Unity project: " + picked);
                        return;
                    }
                    if (_field != null) _field.text = resolved;
                }));
        }

        private void Save()
        {
            string resolved = UnityProject.Resolve(_field != null ? _field.text : null);
            if (resolved == null)
            {
                Plugin.Log.LogWarning("no Assets folder in there, not saving it");
                return;
            }
            UnityProject.Set(resolved);
            Plugin.Log.LogInfo("exports will land in " + resolved);
            Finish(true);
        }

        private void Cancel() => Finish(false);

        private void Finish(bool ok)
        {
            var cb = _done;
            _done = null;
            StartCoroutine(TearDown(cb, ok).WrapToIl2Cpp());
        }

        private IEnumerator TearDown(Action<bool> cb, bool ok)
        {
            yield return TearDownSurfaces();
            if (_live == this) _live = null;
            UnityEngine.Object.Destroy(gameObject);
            cb?.Invoke(ok);
        }
    }
}
