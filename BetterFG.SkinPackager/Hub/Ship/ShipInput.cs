using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Editor
{
    internal sealed class ShipInput
    {
        private static readonly MethodInfo Activate = typeof(InputField).GetMethod("ActivateInputFieldInternal", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly ShipHost _host;
        private readonly PointerEventData _ped;
        private readonly List<Graphic> _hits = new List<Graphic>();
        private bool _pressed;

        public ShipInput(ShipHost host)
        {
            _host = host;
            _ped = new PointerEventData(host.Events) { pointerId = -1, button = PointerEventData.InputButton.Left };
        }

        public bool Busy => _pressed;

        private Vector2 ToPixels(Vector2 local) => new Vector2(local.x * _host.Scale, (_host.Height - local.y) * _host.Scale);

        private RaycastResult Raycast(Vector2 pos)
        {
            var cam = _host.Camera;
            Graphic best = null;
            Canvas bestCanvas = null;
            int bestOrder = int.MinValue, bestDepth = int.MinValue;
            foreach (var canvas in _host.Canvas.GetComponentsInChildren<Canvas>(false))
            {
                if (!canvas.isActiveAndEnabled) continue;
                int order = canvas.overrideSorting || canvas.isRootCanvas ? canvas.sortingOrder : canvas.rootCanvas.sortingOrder;
                var graphics = GraphicRegistry.GetGraphicsForCanvas(canvas);
                for (int i = 0; i < graphics.Count; i++)
                {
                    var g = graphics[i];
                    if (g == null || !g.raycastTarget || !g.isActiveAndEnabled || g.depth == -1 || g.canvasRenderer.cull) continue;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, pos, cam)) continue;
                    if (!g.Raycast(pos, cam)) continue;
                    if (order < bestOrder || (order == bestOrder && g.depth <= bestDepth)) continue;
                    best = g; bestCanvas = canvas; bestOrder = order; bestDepth = g.depth;
                }
            }
            if (best == null) return new RaycastResult();
            var module = bestCanvas.GetComponentInParent<GraphicRaycaster>();
            return new RaycastResult
            {
                gameObject = best.gameObject,
                module = module,
                screenPosition = pos,
                depth = best.depth,
                sortingOrder = bestOrder,
                worldPosition = new Vector3(pos.x, pos.y, 0f),
            };
        }

        public void Move(Vector2 local)
        {
            var pos = ToPixels(local);
            _ped.delta = pos - _ped.position;
            _ped.position = pos;
            BettrFG.uGUI.Input.mousePosition = pos;
            var rr = Raycast(pos);
            _ped.pointerCurrentRaycast = rr;
            EnterExit(rr.gameObject);

            if (!_pressed || _ped.pointerDrag == null) return;
            if (!_ped.dragging && (_ped.pressPosition - pos).sqrMagnitude >= _host.Events.pixelDragThreshold * _host.Events.pixelDragThreshold * _host.Scale * _host.Scale)
            {
                ExecuteEvents.Execute(_ped.pointerDrag, _ped, ExecuteEvents.beginDragHandler);
                _ped.dragging = true;
            }
            if (!_ped.dragging) return;
            if (_ped.pointerPress != _ped.pointerDrag && _ped.pointerPress != null)
            {
                ExecuteEvents.Execute(_ped.pointerPress, _ped, ExecuteEvents.pointerUpHandler);
                _ped.eligibleForClick = false;
                _ped.pointerPress = null;
                _ped.rawPointerPress = null;
            }
            ExecuteEvents.Execute(_ped.pointerDrag, _ped, ExecuteEvents.dragHandler);
        }

        public void Leave()
        {
            if (_pressed) return;
            BettrFG.uGUI.Input.mousePosition = new Vector3(-99999f, -99999f, 0f);
            EnterExit(null);
        }

        private void EnterExit(GameObject target)
        {
            if (target == null || _ped.pointerEnter == null)
            {
                foreach (var h in _ped.hovered) if (h != null) ExecuteEvents.Execute(h, _ped, ExecuteEvents.pointerExitHandler);
                _ped.hovered.Clear();
                if (target == null) { _ped.pointerEnter = null; return; }
            }
            if (_ped.pointerEnter == target && target != null) return;

            Transform common = null;
            if (_ped.pointerEnter != null)
                for (var t = target.transform; t != null; t = t.parent)
                    if (_ped.hovered.Contains(t.gameObject)) { common = t; break; }

            if (_ped.pointerEnter != null)
                for (var t = _ped.pointerEnter.transform; t != null && t != common; t = t.parent)
                {
                    ExecuteEvents.Execute(t.gameObject, _ped, ExecuteEvents.pointerExitHandler);
                    _ped.hovered.Remove(t.gameObject);
                }

            _ped.pointerEnter = target;
            for (var t = target.transform; t != null && t != common; t = t.parent)
            {
                ExecuteEvents.Execute(t.gameObject, _ped, ExecuteEvents.pointerEnterHandler);
                _ped.hovered.Add(t.gameObject);
            }
        }

        public void Down(Vector2 local, int clickCount)
        {
            Move(local);
            _pressed = true;
            BettrFG.uGUI.Input.Down = BettrFG.uGUI.Input.Held = true;
            var go = _ped.pointerCurrentRaycast.gameObject;
            _ped.eligibleForClick = true;
            _ped.delta = Vector2.zero;
            _ped.dragging = false;
            _ped.useDragThreshold = true;
            _ped.pressPosition = _ped.position;
            _ped.pointerPressRaycast = _ped.pointerCurrentRaycast;
            _ped.clickCount = clickCount;
            _ped.clickTime = UnityEngine.Time.realtimeSinceStartup;

            var selectHandler = ExecuteEvents.GetEventHandler<ISelectHandler>(go);
            if (selectHandler != _host.Events.currentSelectedGameObject) _host.Events.SetSelectedGameObject(null, _ped);

            if (go == null) { _ped.pointerPress = _ped.rawPointerPress = _ped.pointerDrag = null; return; }

            var press = ExecuteEvents.ExecuteHierarchy(go, _ped, ExecuteEvents.pointerDownHandler);
            if (press == null) press = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            _ped.pointerPress = press;
            _ped.rawPointerPress = go;
            _ped.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(go);
            if (_ped.pointerDrag != null) ExecuteEvents.Execute(_ped.pointerDrag, _ped, ExecuteEvents.initializePotentialDrag);

            FocusField();
        }

        public void Up(Vector2 local)
        {
            Move(local);
            _pressed = false;
            BettrFG.uGUI.Input.Up = true;
            BettrFG.uGUI.Input.Held = false;
            var go = _ped.pointerCurrentRaycast.gameObject;

            if (_ped.pointerPress != null) ExecuteEvents.Execute(_ped.pointerPress, _ped, ExecuteEvents.pointerUpHandler);
            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            if (_ped.pointerPress != null && _ped.pointerPress == clickHandler && _ped.eligibleForClick)
            {
                ExecuteEvents.Execute(_ped.pointerPress, _ped, ExecuteEvents.pointerClickHandler);
            }
            else if (_ped.pointerDrag != null && _ped.dragging)
                ExecuteEvents.ExecuteHierarchy(go, _ped, ExecuteEvents.dropHandler);

            if (_ped.pointerDrag != null && _ped.dragging) ExecuteEvents.Execute(_ped.pointerDrag, _ped, ExecuteEvents.endDragHandler);

            _ped.eligibleForClick = false;
            _ped.pointerPress = _ped.rawPointerPress = _ped.pointerDrag = null;
            _ped.dragging = false;
            FocusField();
        }

        public void Scroll(Vector2 local, float lines)
        {
            Move(local);
            _ped.scrollDelta = new Vector2(0f, -lines);
            var target = ExecuteEvents.GetEventHandler<IScrollHandler>(_ped.pointerCurrentRaycast.gameObject);
            if (target != null) ExecuteEvents.ExecuteHierarchy(target, _ped, ExecuteEvents.scrollHandler);
            _ped.scrollDelta = Vector2.zero;
        }

        private InputField SelectedField
        {
            get
            {
                var sel = _host.Events.currentSelectedGameObject;
                var f = sel != null ? sel.GetComponent<InputField>() : null;
                return f != null && f.IsInteractable() ? f : null;
            }
        }

        private void FocusField()
        {
            var f = SelectedField;
            if (f != null && !f.isFocused) Activate.Invoke(f, null);
        }

        public bool Key(Event e)
        {
            var f = SelectedField;
            if (f == null) return false;
            if (!f.isFocused) Activate.Invoke(f, null);

            bool enter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || (e.keyCode == KeyCode.None && e.character == '\n');
            if ((enter && f.lineType != InputField.LineType.MultiLineNewline) || e.keyCode == KeyCode.Escape)
            {
                if (e.keyCode == KeyCode.None) return true;
                f.DeactivateInputField();
                _host.Events.SetSelectedGameObject(null);
                return true;
            }
            if (e.keyCode == KeyCode.Tab || e.character == '\t') return true;
            f.ProcessEvent(e);
            f.ForceLabelUpdate();
            return true;
        }

        public void Blur()
        {
            var f = SelectedField;
            if (f != null) f.DeactivateInputField();
            _host.Events.SetSelectedGameObject(null);
        }
    }
}
