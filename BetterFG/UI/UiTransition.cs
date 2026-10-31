using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;

namespace BetterFG.UI
{
    public static class UiTransition
    {
        private const float Duration = 0.24f;
        private const float StartScale = 0.8f;
        private const float PeakScale = 1.015f;
        private const float PeakAt = 0.5f;

        private sealed class Pop
        {
            public RectTransform Area;
            public Vector3 BaseScale;
            public Vector3 BasePos;
            public float CenterOffset;
            public bool Done;
        }

        private static readonly Dictionary<int, Pop> _running = new Dictionary<int, Pop>();

        public static void Run(RectTransform area, Action apply) => Run(area, apply, () => area);

        public static void Run(GameObject fromPanel, GameObject toPanel, Action apply)
        {
            if (fromPanel == toPanel) { apply(); return; }
            Run(fromPanel != null ? fromPanel.GetComponent<RectTransform>() : null, apply,
                () => toPanel != null ? toPanel.GetComponent<RectTransform>() : null);
        }

        public static void Run(RectTransform fromArea, Action apply, Func<RectTransform> toArea)
        {
            apply();
            var area = toArea();
            var host = BetterFGUIMan.Instance;
            if (host == null || area == null || !area.gameObject.activeInHierarchy) return;

            int key = area.GetInstanceID();
            if (_running.TryGetValue(key, out var old)) Finish(old);

            var pop = new Pop
            {
                Area = area,
                BaseScale = area.localScale,
                BasePos = area.localPosition,
                CenterOffset = (0.5f - area.pivot.x) * area.rect.width * area.localScale.x,
            };
            _running[key] = pop;
            Apply(pop, StartScale);
            Features.Onboarding.OnboardingPopup.AddCornerPopsAround(area);
            host.StartCoroutine(Animate(pop, key).WrapToIl2Cpp());
        }

        private static float ScaleAt(float t)
        {
            if (t < PeakAt) return Mathf.Lerp(StartScale, PeakScale, 1f - (1f - t / PeakAt) * (1f - t / PeakAt));
            return Mathf.Lerp(PeakScale, 1f, Mathf.SmoothStep(0f, 1f, (t - PeakAt) / (1f - PeakAt)));
        }

        private static void Apply(Pop p, float s)
        {
            p.Area.localScale = new Vector3(p.BaseScale.x * s, p.BaseScale.y, p.BaseScale.z);
            p.Area.localPosition = p.BasePos + p.Area.localRotation * new Vector3(p.CenterOffset * (1f - s), 0f, 0f);
        }

        private static void Finish(Pop p)
        {
            if (p.Done) return;
            p.Done = true;
            if (p.Area == null) return;
            p.Area.localScale = p.BaseScale;
            p.Area.localPosition = p.BasePos;
        }

        private static IEnumerator Animate(Pop p, int key)
        {
            float t = 0f;
            while (t < Duration && !p.Done)
            {
                if (p.Area == null) yield break;
                Apply(p, ScaleAt(t / Duration));
                yield return null;
                t += Time.unscaledDeltaTime;
            }
            Finish(p);
            if (_running.TryGetValue(key, out var cur) && cur == p) _running.Remove(key);
        }
    }
}
