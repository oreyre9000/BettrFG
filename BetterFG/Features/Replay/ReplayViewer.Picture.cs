using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.UI;
using FG.Common;
using UnityEngine;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    public partial class ReplayViewer
    {
        NavPromptHandle _shotPrompt;
        Coroutine _shotFade;

        const float SHOT_PROMPT_HOLD = 3f;
        const float SHOT_PROMPT_FADE = 0.5f;

        void SetShotPrompt(bool show)
        {
            if (show == (_shotPrompt != null && _shotPrompt.IsAlive)) return;

            if (_shotFade != null) { StopCoroutine(_shotFade); _shotFade = null; }

            if (!show)
            {
                _shotPrompt.Destroy();
                _shotPrompt = null;
                return;
            }

            _shotPrompt = NavPromptCore.From(NavPrompt.Report)
                .WithLabel(BetterFG.Services.LocalizationService.Get("nav.take_picture"), "bfg_replay_take_picture")
                .AnchoredAt(NavPromptAnchor.TopRight, new Vector2(-24f, -24f))
                .Width_(220f)
                .PollActions(RewiredConsts.Action.Menu_Report)
                .AllowWhileUnfocused()
                .SpawnOn(_canvas.transform);

            if (_shotPrompt == null)
            {
                Plugin.Log.LogWarning("picture prompt didn't spawn, so there's no way to snap one this session");
                return;
            }

            _shotFade = StartCoroutine(FadeShotPrompt().WrapToIl2Cpp());
        }

        IEnumerator FadeShotPrompt()
        {
            var go = _shotPrompt.GameObject;
            var group = go.GetComponent<CanvasGroup>();
            if (group == null) group = go.AddComponent<CanvasGroup>();
            group.alpha = 1f;

            yield return new WaitForSecondsRealtime(SHOT_PROMPT_HOLD);

            for (float t = 0f; t < SHOT_PROMPT_FADE; t += Time.unscaledDeltaTime)
            {
                if (go == null) yield break;
                group.alpha = 1f - t / SHOT_PROMPT_FADE;
                yield return null;
            }

            if (go != null) group.alpha = 0f;
            _shotFade = null;
        }

        void TakePicture() => StartCoroutine(PictureRoutine().WrapToIl2Cpp());

        IEnumerator PictureRoutine()
        {
            yield return new WaitForEndOfFrame();

            string path = ReplayImages.Capture(_cam, _rec, ReplayName());
            if (string.IsNullOrEmpty(path)) yield break;

            var toast = UGUIShip.CreateLabel(_canvas.transform, new Rect(0f, 12f, Screen.width, 26f),
                "saved " + System.IO.Path.GetFileName(path) + "  ·  it's in the Images tab",
                UIScale.FS, Color.white, TextAnchor.MiddleCenter);

            yield return new WaitForSecondsRealtime(2.5f);
            if (toast != null) Destroy(toast.gameObject);
        }
    }
}
