using System.Globalization;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Features.Onboarding;
using BetterFG.Services;
using BettrFG.uGUI;
using Rewired;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.UI
{
    public static class OnScreenKeyboard
    {
        private const float KeyW = 34f;
        private const float KeyH = 28f;
        private const float Gap = 4f;
        private const float PadX = 12f;
        private const float PadTop = 12f;
        private const float PadBottom = 10f;
        private const float HintH = 16f;
        private const int Cols = 10;
        private const int ActionCols = 4;
        private const float Width = PadX * 2f + Cols * KeyW + (Cols - 1) * Gap;
        private const float Height = PadTop + 5f * KeyH + 5f * Gap + HintH + PadBottom;

        private static readonly string[] Lower = { "1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm,.-" };
        private static readonly string[] Upper = { "!@#$%^&*()", "QWERTYUIOP", "ASDFGHJKL\"", "ZXCVBNM<>_" };
        private static readonly string[] ActionIds =
            { "keyboard.key_shift", "keyboard.key_space", "keyboard.key_back", "keyboard.key_done" };
        private static readonly float[] ActionW = { 84f, 112f, 84f, 84f };

        // rewired element names, aliased across pad brands the way the existing prompt polls do.
        private static readonly string[] UpNames = { "D-Pad Up", "DPad Up", "Hat Up" };
        private static readonly string[] DownNames = { "D-Pad Down", "DPad Down", "Hat Down" };
        private static readonly string[] LeftNames = { "D-Pad Left", "DPad Left", "Hat Left" };
        private static readonly string[] RightNames = { "D-Pad Right", "DPad Right", "Hat Right" };
        private static readonly string[] StickXNames = { "Left Stick X", "Left Stick Horizontal" };
        private static readonly string[] StickYNames = { "Left Stick Y", "Left Stick Vertical" };
        private static readonly string[] MoveXNames = { "Right Stick X", "Right Stick Horizontal" };
        private static readonly string[] MoveYNames = { "Right Stick Y", "Right Stick Vertical" };
        private static readonly string[] HatXNames = { "D-Pad X", "DPad X", "Hat X" };
        private static readonly string[] HatYNames = { "D-Pad Y", "DPad Y", "Hat Y" };
        private static readonly string[] ConfirmNames = { "A", "Cross" };
        private static readonly string[] CancelNames = { "B", "Circle" };
        private static readonly string[] DeleteNames = { "X", "Square" };
        private static readonly string[] ShiftNames = { "Y", "Triangle" };

        private static readonly Color Idle = new Color(0.30f, 0.30f, 0.32f, 1f);
        private static readonly Color Sel = new Color(0.35f, 0.85f, 0.35f, 1f);

        private const float MoveSpeed = 240f;
        private const float MoveDeadzone = 0.2f;
        private const string KeyPosX = "keyboard.posX";
        private const string KeyPosY = "keyboard.posY";
        private static readonly Vector2 DefaultPos = new Vector2(0f, 70f);
        private static Vector2 _pos;
        private static bool _posLoaded;

        private static Canvas _canvas;
        private static GameObject _popup;
        private static RectTransform _popupRt;
        private static Image[] _cells;
        private static Text[] _labels;
        private static int _row, _col;
        private static bool _shift;
        private static float _repeatAt;
        private static int _fieldId, _dismissedId;
        private static InputField _uiField;
        private static TMP_InputField _tmpField;

        public static bool IsOpen => _popup != null;

        internal static bool Tick(MonoBehaviour host)
        {
            var sel = EventSystem.current?.currentSelectedGameObject;
            int id = sel == null ? 0 : sel.GetInstanceID();
            if (id != _fieldId)
            {
                _fieldId = id;
                // a dismissal only lasts as long as that field stays selected, otherwise coming back
                // to the same field later finds the keyboard permanently refusing to open.
                _dismissedId = 0;
                bool ours = sel != null && (BetterFGUIMan.Instance?.IsOurField(sel.transform) ?? false);
                _uiField = ours ? sel.GetComponent<InputField>() : null;
                _tmpField = ours && _uiField == null ? sel.GetComponent<TMP_InputField>() : null;
                Close(host);
            }

            bool onPad = ReInput.controllers.GetLastActiveController()?.type == ControllerType.Joystick;
            if (!onPad || (_uiField == null && _tmpField == null))
            {
                Close(host);
                return false;
            }

            if (!IsOpen)
            {
                if (_dismissedId == id) return false;
                Open(host);
            }

            FGInputLockService.SetControllerLock(true);
            FGInputLockService.SetRewiredMouseSuppressed(false);
            Drive(host);
            return IsOpen;
        }

        private static void Drive(MonoBehaviour host)
        {
            float x = NavPromptCore.AxisByName(StickXNames);
            float y = NavPromptCore.AxisByName(StickYNames);
            if (Mathf.Abs(x) < 0.5f) x = NavPromptCore.AxisByName(HatXNames);
            if (Mathf.Abs(y) < 0.5f) y = NavPromptCore.AxisByName(HatYNames);

            int dx = 0, dy = 0;
            if (NavPromptCore.ElementHeldByName(RightNames) || x > 0.5f) dx = 1;
            else if (NavPromptCore.ElementHeldByName(LeftNames) || x < -0.5f) dx = -1;
            if (NavPromptCore.ElementHeldByName(UpNames) || y > 0.5f) dy = 1;
            else if (NavPromptCore.ElementHeldByName(DownNames) || y < -0.5f) dy = -1;

            if (dx == 0 && dy == 0) _repeatAt = 0f;
            else if (Time.unscaledTime >= _repeatAt)
            {
                _repeatAt = Time.unscaledTime + (_repeatAt == 0f ? 0.35f : 0.09f);
                Move(dx, dy);
            }

            float mx = NavPromptCore.AxisByName(MoveXNames);
            float my = NavPromptCore.AxisByName(MoveYNames);
            if (Mathf.Abs(mx) > MoveDeadzone || Mathf.Abs(my) > MoveDeadzone)
            {
                SetPos(_pos + new Vector2(mx, my) * MoveSpeed * Time.unscaledDeltaTime);
                SettingsService.Set(KeyPosX, _pos.x.ToString(CultureInfo.InvariantCulture));
                SettingsService.Set(KeyPosY, _pos.y.ToString(CultureInfo.InvariantCulture));
            }

            if (NavPromptCore.ElementDownByName(ShiftNames)) { _shift = !_shift; Relabel(); }
            if (NavPromptCore.ElementDownByName(DeleteNames)) Backspace();
            if (NavPromptCore.ElementDownByName(ConfirmNames)) Press(host);
            if (NavPromptCore.ElementDownByName(CancelNames))
            {
                _dismissedId = _fieldId;
                // a field of ours living in the GAME's hierarchy is only reachable by pad through
                // this keyboard, so dismissing it has to hand the selection back as well.
                if (BetterFGUIMan.IsAdoptedField(_fieldId)) EventSystem.current.SetSelectedGameObject(null);
                Close(host);
            }
        }

        private static void Move(int dx, int dy)
        {
            if (dy != 0)
            {
                int oldLen = RowLen(_row);
                _row = (_row - dy + 5) % 5;
                int len = RowLen(_row);
                _col = Mathf.RoundToInt(_col * (len - 1f) / (oldLen - 1f));
            }
            if (dx != 0)
            {
                int len = RowLen(_row);
                _col = (_col + dx + len) % len;
            }
            Highlight();
        }

        private static void Press(MonoBehaviour host)
        {
            if (_row < 4)
            {
                FieldText = FieldText + (_shift ? Upper : Lower)[_row][_col];
                return;
            }
            switch (_col)
            {
                case 0: _shift = !_shift; Relabel(); break;
                case 1: FieldText = FieldText + " "; break;
                case 2: Backspace(); break;
                default:
                    EventSystem.current.SetSelectedGameObject(null);
                    Close(host);
                    break;
            }
        }

        private static void Backspace()
        {
            string t = FieldText;
            if (t.Length > 0) FieldText = t.Substring(0, t.Length - 1);
        }

        private static string FieldText
        {
            get => _uiField != null ? _uiField.text : _tmpField.text;
            set
            {
                if (_uiField != null) { _uiField.text = value; _uiField.caretPosition = _uiField.text.Length; }
                else { _tmpField.text = value; _tmpField.caretPosition = _tmpField.text.Length; }
            }
        }

        private static string PadName(string[] names)
        {
            int id = NavPromptCore.CurrentPadElementByName(names);
            return id >= 0 ? ControllerBindService.ButtonName(id) : names[0];
        }

        private static void LoadPos()
        {
            if (_posLoaded) return;
            _posLoaded = true;
            float x = float.TryParse(SettingsService.Get(KeyPosX, ""), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var px) ? px : DefaultPos.x;
            float y = float.TryParse(SettingsService.Get(KeyPosY, ""), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var py) ? py : DefaultPos.y;
            _pos = new Vector2(x, y);
        }

        private static void SetPos(Vector2 p)
        {
            var refSize = UIScaleService.CurrentRef;
            float halfW = refSize.x * 0.5f;
            p.x = Mathf.Clamp(p.x, -halfW + Width * 0.5f, halfW - Width * 0.5f);
            p.y = Mathf.Clamp(p.y, 0f, Mathf.Max(0f, refSize.y - Height));
            _pos = p;
            _popupRt.anchoredPosition = p;
        }

        private static int RowLen(int row) => row < 4 ? Cols : ActionCols;

        private static int Index(int row, int col) => row < 4 ? row * Cols + col : Cols * 4 + col;

        private static void Open(MonoBehaviour host)
        {
            if (_canvas == null)
            {
                _canvas = UGUIShip.CreateCanvas("BetterFG_KeyboardCanvas");
                _canvas.sortingOrder = 32000;
                Object.Destroy(_canvas.GetComponent<GraphicRaycaster>());
            }

            LoadPos();

            _row = 1;
            _col = 0;
            _shift = false;
            _repeatAt = 0f;
            _cells = new Image[Cols * 4 + ActionCols];
            _labels = new Text[Cols * 4 + ActionCols];

            var root = new GameObject("Keyboard_Popup");
            root.transform.SetParent(_canvas.transform, false);
            var rt = root.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(Width, Height);
            _popupRt = rt;
            SetPos(_pos);

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(root.transform, false);
            var panelRt = panelGo.AddComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.offsetMin = new Vector2(-10f, -10f);
            panelRt.offsetMax = new Vector2(10f, 10f);
            UGUIShip.ApplyDeluxPanelDark(panelGo.AddComponent<Image>(), 2f);

            for (int r = 0; r < 4; r++)
                for (int c = 0; c < Cols; c++)
                    MakeCell(root.transform, Index(r, c),
                        new Rect(PadX + c * (KeyW + Gap), PadTop + r * (KeyH + Gap), KeyW, KeyH),
                        Lower[r][c].ToString());

            float ax = PadX;
            float ay = PadTop + 4f * (KeyH + Gap);
            for (int c = 0; c < ActionCols; c++)
            {
                MakeCell(root.transform, Index(4, c), new Rect(ax, ay, ActionW[c], KeyH), ActionIds[c]);
                ax += ActionW[c] + Gap;
            }

            var hint = UGUIShip.CreateLabel(root.transform,
                new Rect(PadX, Height - PadBottom - HintH, Width - PadX * 2f, HintH),
                "keyboard.hint", 10, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleCenter);
            hint.text = string.Format(LocalizationService.Get("keyboard.hint"),
                PadName(ConfirmNames), PadName(CancelNames), PadName(DeleteNames), PadName(ShiftNames));

            Highlight();
            rt.localScale = new Vector3(0f, 1f, 1f);
            _popup = root;
            host.StartCoroutine(PromptHost.OpenPopup(root).WrapToIl2Cpp());
        }

        private static void MakeCell(Transform parent, int index, Rect rect, string label)
        {
            var cell = UGUIShip.CreatePanel(parent, rect, Idle, "Key");
            var go = cell.gameObject;
            // the delux fill slice is what carries the tint, so the highlight writes to it, not the
            // base image. label goes on after so it draws over the fill and shine.
            var img = go.GetComponent<Image>();
            _cells[index] = UGUIShip.ApplyDeluxSkin(go, img, Idle, true, 3f) ?? img;
            _labels[index] = UGUIShip.CreateLabel(cell, new Rect(0f, 0f, rect.width, rect.height),
                label, 13, UGUIShip.WHITE, TextAnchor.MiddleCenter);
        }

        private static void Highlight()
        {
            int sel = Index(_row, _col);
            for (int i = 0; i < _cells.Length; i++) _cells[i].color = i == sel ? Sel : Idle;
        }

        private static void Relabel()
        {
            var rows = _shift ? Upper : Lower;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < Cols; c++)
                    _labels[Index(r, c)].text = rows[r][c].ToString();
        }

        private static void Close(MonoBehaviour host)
        {
            if (_popup == null) return;
            host.StartCoroutine(PromptHost.CloseThenDestroy(_popup).WrapToIl2Cpp());
            _popup = null;
            _popupRt = null;
            _cells = null;
            _labels = null;
        }
    }
}
