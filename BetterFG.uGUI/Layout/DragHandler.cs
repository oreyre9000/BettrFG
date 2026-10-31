using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

namespace BettrFG.uGUI
{
    // �� DragHandler �����������������������������������������������������������
    public class DragHandler : MonoBehaviour
    {
        public DragHandler(IntPtr ptr) : base(ptr) { }

        private RectTransform _self;
        private RectTransform _target;
        private RectTransform _parentRt;
        private Canvas _canvas;
        private bool _dragging;
        private Vector2 _dragOffset;

        public DragHandler SetTarget(RectTransform target)
        {
            _target = target;
            return this;
        }

        public void Awake()
        {
            _self = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
        }

        public void Start()
        {
            if (_target == null) _target = _self;

            var p = _target.parent;
            while (p != null)
            {
                var prt = p.GetComponent<RectTransform>();
                if (prt != null) { _parentRt = prt; break; }
                p = p.parent;
            }
        }

        public void Update()
        {
            if (_self == null || _target == null || _parentRt == null) return;
            if (!_dragging && _canvas != null && !_canvas.enabled) return;

            var mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);

            if (Input.GetMouseButtonDown(0))
            {
                if (IsDirectHit(mouse))
                {
                    _dragging = true;
                    _dragOffset = _target.anchoredPosition - ScreenToAnchored(mouse);
                }
            }

            if (Input.GetMouseButtonUp(0))
                _dragging = false;

            if (_dragging)
                _target.anchoredPosition = ScreenToAnchored(mouse) + _dragOffset;
        }

        private bool IsDirectHit(Vector2 mouse)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _self, mouse, null, out var local)) return false;
            if (!_self.rect.Contains(local)) return false;

            var windowRoot = _target;
            for (int i = 0; i < windowRoot.childCount; i++)
            {
                var child = windowRoot.GetChild(i)?.GetComponent<RectTransform>();
                if (child == null || child == _self) continue;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    child, mouse, null, out var childLocal)
                    && child.rect.Contains(childLocal))
                    return false;
            }
            return true;
        }

        private Vector2 ScreenToAnchored(Vector2 mouse)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentRt, mouse, null, out var local);
            return local;
        }
    }
}
