using System;
using System.Collections;
using UnityEngine;

namespace BettrFG.uGUI
{
    public static partial class UGUIShip
    {
        public const float SquashDur = 0.3f;

        private static readonly AnimationCurve SquashCurve = new AnimationCurve(new Keyframe[]
        {
            new Keyframe(0f, 1f), new Keyframe(0.5f, 1.1f), new Keyframe(1f, 0f),
        });

        public static IEnumerator Squash(Action<float> setY, bool open)
        {
            for (float t = 0f; t < SquashDur; t += Time.unscaledDeltaTime)
            {
                float u = t / SquashDur;
                setY(SquashCurve.Evaluate(open ? 1f - u : u));
                yield return null;
            }
            setY(open ? 1f : 0f);
        }

        public static IEnumerator Squash(Transform t, bool open) =>
            Squash(y => t.localScale = new Vector3(t.localScale.x, y, t.localScale.z), open);
    }
}
