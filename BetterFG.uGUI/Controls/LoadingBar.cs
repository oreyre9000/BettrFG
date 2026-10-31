using System;
using UnityEngine;

namespace BettrFG.uGUI
{
    public class LoadingBar : MonoBehaviour
    {
        public LoadingBar(IntPtr ptr) : base(ptr) { }

        private const float SweepWidth = 0.3f;
        private const float SweepSpeed = 0.7f;

        private RectTransform _fill;
        private Vector2 _inset;
        private bool _busy;
        private float _phase;

        public float Value { get; private set; }
        public bool IsBusy => _busy;

        internal void Bind(RectTransform fill, float inset)
        {
            _fill = fill;
            _inset = new Vector2(inset, inset);
            Set(0f);
        }

        public void Set(float t)
        {
            _busy = false;
            Value = Mathf.Clamp01(t);
            Apply(0f, Value);
        }

        public void Busy()
        {
            if (_busy) return;
            _busy = true;
            _phase = 0f;
        }

        void Update()
        {
            if (!_busy) return;
            _phase = Mathf.Repeat(_phase + Time.unscaledDeltaTime * SweepSpeed, 1f);
            float head = _phase * (1f + SweepWidth);
            Apply(head - SweepWidth, head);
        }

        private void Apply(float a, float b)
        {
            _fill.anchorMin = new Vector2(Mathf.Clamp01(a), 0f);
            _fill.anchorMax = new Vector2(Mathf.Clamp01(b), 1f);
            _fill.offsetMin = _inset;
            _fill.offsetMax = -_inset;
        }
    }
}
