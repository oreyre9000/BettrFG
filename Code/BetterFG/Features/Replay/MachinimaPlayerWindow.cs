#if MACHINIMA
using System;
using BetterFG.UI;
using UnityEngine;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    internal class MachinimaPlayerWindow
    {
        public static MachinimaPlayerWindow Instance;

        const float ROW = ReplayWindowKit.ROW;
        const float PAD = ReplayWindowKit.PAD;

        readonly ReplayPlayer _player;
        readonly Action _onClose;
        readonly RectTransform _root;
        readonly float _width;
        RectTransform _content;

        public ReplayPlayer Player => _player;

        MachinimaPlayerWindow(Transform parent, Rect rect, ReplayPlayer player, Action onClose)
        {
            _player = player;
            _onClose = onClose;
            _width = rect.width;
            _root = UGUIShip.CreatePanel(parent, rect, Color.clear, "MachinimaPlayerWindow");
            ReplayWindowKit.EditBackdrop(_root);
            Rebuild();
        }

        public static MachinimaPlayerWindow Show(Transform parent, Rect rect, ReplayPlayer player, Action onClose)
        {
            Instance?.Close();
            Instance = new MachinimaPlayerWindow(parent, rect, player, onClose);
            return Instance;
        }

        public void Close()
        {
            if (Instance == this) Instance = null;
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _onClose?.Invoke();
        }

        void Rebuild()
        {
            if (_content != null) UnityEngine.Object.Destroy(_content.gameObject);
            _content = ReplayWindowKit.Content(_root);

            ReplayWindowKit.Title(_content, _width, "machinima.player_window_title", Vector3.zero);

            float y = ReplayWindowKit.HEAD + 4f;
            float inner = _width - PAD * 2f;

            ReplayWindowKit.Stripe(_content, y, _width, 0);
            UGUIShip.CreateLabel(_content, new Rect(PAD, y, ReplayWindowKit.LABEL_W, ROW),
                "ui.name", UIScale.FS_SM, ReplayWindowKit.HINT);
            var nameField = UGUIShip.CreateInputField(_content,
                new Rect(PAD + ReplayWindowKit.LABEL_W, y, inner - ReplayWindowKit.LABEL_W, ROW),
                "machinima.player_name_hint", new Color(0f, 0f, 0f, 0.55f), Color.white, UIScale.FS_SM);
            nameField.text = _player.name;
            nameField.onEndEdit.AddListener(new Action<string>(OnNameChanged));
            y += ROW;

            ReplayWindowKit.Stripe(_content, y, _width, 1);
            UGUIShip.CreateLabel(_content, new Rect(PAD, y, inner, ROW),
                BetterFG.Services.LocalizationService.Format("machinima.zone_fmt",
                    Stamp(_player.inTime), Stamp(_player.outTime)), UIScale.FS_SM, Color.white);
            y += ROW + 4f;

            Button("machinima.edit_bean", ReplayWindowKit.BTN_DARK, y, new Action(EditLook)); y += ROW;
            Button("machinima.open_timeline", ReplayWindowKit.BTN_DARK, y, new Action(OpenTimeline)); y += ROW;
            Button("ui.delete", ReplayWindowKit.BTN_RED, y, new Action(Delete)); y += ROW;

            _root.sizeDelta = new Vector2(_width, y + PAD);
        }

        void Button(string label, Color colour, float y, Action onClick)
        {
            UGUIShip.CreateButton(_content, new Rect(PAD, y, _width - PAD * 2f, ROW),
                label, colour, Color.white, UIScale.FS_SM, onClick);
        }

        static string Stamp(float seconds)
        {
            var t = TimeSpan.FromSeconds(Mathf.Max(seconds, 0f));
            return string.Format("{0:D2}:{1:D2}.{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
        }

        void OnNameChanged(string value)
        {
            _player.name = (value ?? "").Trim();
            ReplayViewer.Instance?.RefreshAfterPlayerRename();
        }

        void EditLook() => ReplayViewer.Instance?.EditPlayerLook(_player);

        void OpenTimeline() => ReplayViewer.Instance?.EnterSolo(_player);

        void Delete() => ReplayViewer.Instance?.DeletePlayer(_player);
    }
}
#endif
