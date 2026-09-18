using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.Onboarding;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.Assets
{
    public class BundleReaderPrompt : PromptHost
    {
        public BundleReaderPrompt(IntPtr ptr) : base(ptr) { }

        private const int CanvasOrder = 1402;
        private const int BlockerOrder = 1401;

        private static BundleReaderPrompt _live;

        private LoadingBar _bar;
        private Text _body;

        public static IEnumerator Ensure()
        {
            if (PythonRuntime.Find() != null) yield break;
            if (_live == null)
            {
                var go = new GameObject("BetterFG_BundleReaderPrompt");
                go.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(go);
                _live = go.AddComponent<BundleReaderPrompt>();
                _live.Build();
            }
            while (_live != null) yield return null;
        }

        private void Build()
        {
            CreateSurfaces("BundleReader", CanvasOrder, BlockerOrder, true);
            Swap(OnboardingPopup.Show(_canvas, "asset_export.installing_title", "asset_export.install_ask",
                Vector2.zero, new Vector2(0.5f, 0.5f),
                new[]
                {
                    new OnboardingPopup.ButtonSpec("ui.cancel", new Action(() => StartCoroutine(Finish().WrapToIl2Cpp())), OnboardingPopup.BtnGhost),
                    new OnboardingPopup.ButtonSpec("ui.ok", new Action(() => StartCoroutine(Run().WrapToIl2Cpp())), OnboardingPopup.BtnPrimary),
                }));
        }

        private IEnumerator Run()
        {
            Swap(OnboardingPopup.Show(_canvas, "asset_export.installing_title", "asset_export.installing",
                Vector2.zero, new Vector2(0.5f, 0.5f), null, null, null,
                new Action<LoadingBar>(b => _bar = b), new Action<Text>(t => _body = t)));

            var install = PythonRuntime.Ensure(new Action<string>(id => UGUIShip.RelabelText(_body, id)));
            while (install.MoveNext())
            {
                if (PythonRuntime.Progress < 0f) _bar.Busy(); else _bar.Set(PythonRuntime.Progress);
                yield return install.Current;
            }
            if (PythonRuntime.Find() != null) { yield return Finish(); yield break; }

            bool waiting = true;
            Swap(OnboardingPopup.Show(_canvas, "asset_export.installing_title", "asset_export.install_failed",
                Vector2.zero, new Vector2(0.5f, 0.5f),
                new[] { new OnboardingPopup.ButtonSpec("ui.ok", new Action(() => waiting = false), OnboardingPopup.BtnPrimary) }));
            while (waiting) yield return null;
            yield return Finish();
        }

        private IEnumerator Finish()
        {
            yield return TearDownSurfaces();
            _live = null;
            UnityEngine.Object.Destroy(gameObject);
        }
    }
}
