using System;
using UnityEngine;
using UnityEngine.UI;

namespace BettrFG.uGUI
{
    public class SmoothScroll : MonoBehaviour
    {
        public SmoothScroll(IntPtr ptr) : base(ptr) { }

        private const float Speed = 14f;

        private ScrollRect _sr;
        private Vector2 _shown;
        private Vector2 _target;

        void Awake() => _sr = GetComponent<ScrollRect>();

        void OnEnable() => _shown = _target = _sr.content.anchoredPosition;

        void LateUpdate()
        {
            var c = _sr.content;
            var pos = c.anchoredPosition;
            if (pos != _shown)
            {
                if (Input.mouseScrollDelta.y == 0f) { _shown = _target = pos; return; }
                c.anchoredPosition = _target + (pos - _shown);
                _sr.verticalNormalizedPosition = Mathf.Clamp01(_sr.verticalNormalizedPosition);
                _target = c.anchoredPosition;
            }
            if (_shown == _target) return;

            _shown = Vector2.Lerp(_shown, _target, 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime));
            if ((_target - _shown).sqrMagnitude < 0.25f) _shown = _target;
            c.anchoredPosition = _shown;
        }
    }
}
