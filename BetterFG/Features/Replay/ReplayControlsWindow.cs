using System;
using BetterFG.UI;
using UnityEngine;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    internal class ReplayControlsWindow
    {
        public static ReplayControlsWindow Instance;

        const float ROW = ReplayWindowKit.ROW;
        const float PAD = ReplayWindowKit.PAD;

        readonly Action _onClose;
        readonly Action<float> _onSpeedChanged;
        readonly RectTransform _root;
        readonly float _width;
        readonly float _initSpeedT;
        readonly float _resetSpeedT;

        ReplayControlsWindow(Transform parent, Rect rect, float initSpeedT, float resetSpeedT, Action<float> onSpeedChanged, Action onClose)
        {
            _onSpeedChanged = onSpeedChanged;
            _onClose = onClose;
            _width = rect.width;
            _initSpeedT = initSpeedT;
            _resetSpeedT = resetSpeedT;
            _root = UGUIShip.CreatePanel(parent, rect, Color.clear, "ReplayControlsWindow");
            ReplayWindowKit.MainBackdrop(_root);
            Rebuild();
        }

        public static ReplayControlsWindow Show(Transform parent, Rect rect, float initSpeedT, float resetSpeedT,
            Action<float> onSpeedChanged, Action onClose)
        {
            Instance?.Close();
            Instance = new ReplayControlsWindow(parent, rect, initSpeedT, resetSpeedT, onSpeedChanged, onClose);
            return Instance;
        }

        public void Close()
        {
            if (Instance == this) Instance = null;
            ReplayWindowKit.Close(_root);
            _onClose?.Invoke();
        }

        void Rebuild()
        {
            var content = ReplayWindowKit.Content(_root);
            ReplayWindowKit.Title(content, _width, "ui.controls");

            float y = ReplayWindowKit.HEAD + 4f;

            ReplayWindowKit.Stripe(content, y, _width, 0);
            UGUIShip.CreateLabel(content, new Rect(PAD, y, _width - PAD * 2f, ROW), "ui.speed", UIScale.FS_SM, ReplayWindowKit.HINT);
            y += ROW;

            UGUIShip.CreateSlider(content, PAD, y, _width - PAD * 2f, "", _initSpeedT, ROW, PAD, UIScale.FS_SM,
                _onSpeedChanged, null, null, false, _resetSpeedT);
            y += ROW + 6f;

            UGUIShip.CreateButton(content, new Rect(PAD, y, _width - PAD * 2f, ROW + 4f), "ui.close_2",
                ReplayWindowKit.BTN_DARK, Color.white, UIScale.FS_SM, new Action(Close));
            y += ROW + 4f;

            _root.sizeDelta = new Vector2(_width, y + PAD);
        }
    }
}
