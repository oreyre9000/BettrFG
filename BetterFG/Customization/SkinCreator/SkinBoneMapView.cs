using System;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.SkinCreator
{
    internal sealed class SkinBoneMapView
    {
        static readonly Color ACCENT = new Color(0.98f, 0.78f, 0.18f, 1f);
        static readonly Color DIM = new Color(1f, 1f, 1f, 0.35f);
        const float PICK_RADIUS = 18f;

        static Sprite _circle;

        readonly ImportedModel _m;
        readonly string[] _beanNames;
        readonly int[] _map;
        readonly Camera _cam;
        readonly Transform _stage;
        readonly RectTransform _canvasRt;
        readonly Vector3 _origin;
        readonly float _k;

        readonly GameObject _root, _dotRoot, _tip;
        readonly Text _tipText;
        readonly ScrollRect _scroll;
        readonly Button[] _rows, _clears;
        readonly Text[] _vals;
        readonly Image[] _dots, _lines;
        int _sel;

        public SkinBoneMapView(ImportedModel model, string[] beanNames, int[] map, Camera cam, Transform stageObject,
            Canvas canvas, RectTransform panel, float stageHeight)
        {
            _m = model;
            _beanNames = beanNames;
            _map = map;
            _cam = cam;
            _stage = stageObject;
            _canvasRt = canvas.GetComponent<RectTransform>();
            _k = model.Fit(stageHeight, out _origin);
            if (_circle == null) _circle = MakeCircle();

            _root = new GameObject("BoneMap");
            var rrt = _root.AddComponent<RectTransform>();
            rrt.SetParent(panel, false);
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;
            SkinCreatorRows.Header(_root.transform, "skin_creator.header_bones", "skin_creator.pair_hint");

            float w = panel.sizeDelta.x - UGUIShip.SCROLLBAR_INSET * 2f;
            var (sr, content) = SkinCreatorRows.List(rrt, SkinCreatorRows.ContentHeight(beanNames.Length));
            _scroll = sr;

            int n = beanNames.Length;
            _rows = new Button[n];
            _clears = new Button[n];
            _vals = new Text[n];
            for (int i = 0; i < n; i++)
            {
                int bi = i;
                var row = SkinCreatorRows.Row(content, i, w, beanNames[i], w * 0.4f);
                row.onClick.AddListener(new Action(() => { _sel = _sel == bi ? -1 : bi; Refresh(); }));
                _rows[i] = row;
                _vals[i] = UGUIShip.CreateLabel(row.transform, new Rect(w * 0.42f, 0f, w * 0.42f, SkinCreatorRows.RowH), "", SkinCreatorRows.FsRow,
                    ACCENT);
                _clears[i] = UGUIShip.CreateRowEndButton(row.transform, -6f, 28f, SkinCreatorRows.RowH, "X", UGUIShip.BTN_REMOVE,
                    new Action(() => { _map[bi] = -1; Refresh(); }));
            }

            _dotRoot = new GameObject("BoneMapDots");
            var drt = _dotRoot.AddComponent<RectTransform>();
            drt.SetParent(canvas.transform, false);
            drt.anchorMin = Vector2.zero;
            drt.anchorMax = Vector2.one;
            drt.offsetMin = drt.offsetMax = Vector2.zero;

            int m = model.BoneNames.Length;
            _lines = new Image[m];
            for (int i = 0; i < m; i++)
            {
                if (model.BoneParent[i] < 0) continue;
                var lg = new GameObject("Line" + i);
                lg.transform.SetParent(drt, false);
                _lines[i] = lg.AddComponent<Image>();
                _lines[i].color = new Color(1f, 1f, 1f, 0.28f);
                _lines[i].raycastTarget = false;
            }

            _dots = new Image[m];
            for (int i = 0; i < m; i++)
            {
                int di = i;
                var dg = new GameObject("Dot" + i);
                dg.transform.SetParent(drt, false);
                dg.AddComponent<RectTransform>().sizeDelta = new Vector2(16f, 16f);
                _dots[i] = dg.AddComponent<Image>();
                _dots[i].sprite = _circle;
                var btn = dg.AddComponent<Button>();
                btn.targetGraphic = _dots[i];
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(new Action(() => Pick(di)));
            }

            _tip = new GameObject("BoneTip");
            _tip.transform.SetParent(drt, false);
            var bg = _tip.AddComponent<Image>();
            UGUIShip.ApplyDeluxPanelDark(bg);
            bg.raycastTarget = false;
            var trt = _tip.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(180f, 26f);
            trt.pivot = Vector2.zero;
            _tipText = UGUIShip.CreateLabel(_tip.transform, new Rect(8f, 0f, 170f, 26f), "", 10, UGUIShip.WHITE);
            _tipText.raycastTarget = false;
            _tip.SetActive(false);

            _sel = 0;
            Refresh();
        }

        static Sprite MakeCircle()
        {
            var t = new Texture2D(48, 48, TextureFormat.RGBA32, false);
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++)
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(24f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(24f, 24f)))));
            t.Apply();
            return Sprite.Create(t, new Rect(0f, 0f, 48f, 48f), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        void Pick(int bone)
        {
            if (_sel >= 0)
            {
                for (int i = 0; i < _map.Length; i++) if (_map[i] == bone) _map[i] = -1;
                _map[_sel] = bone;
                int next = -1;
                for (int s = 1; s <= _map.Length; s++)
                {
                    int c = (_sel + s) % _map.Length;
                    if (_map[c] < 0) { next = c; break; }
                }
                _sel = next;
            }
            else
            {
                for (int i = 0; i < _map.Length; i++) if (_map[i] == bone) { _sel = i; break; }
            }
            Refresh();
            if (_sel >= 0 && _map.Length > 1) _scroll.verticalNormalizedPosition = 1f - (float)_sel / (_map.Length - 1);
        }

        void Refresh()
        {
            var used = new bool[_m.BoneNames.Length];
            foreach (int f in _map) if (f >= 0) used[f] = true;
            for (int i = 0; i < _rows.Length; i++)
            {
                UGUIShip.PaintListRow(_rows[i], i, i == _sel);
                bool paired = _map[i] >= 0;
                _vals[i].text = paired ? _m.BoneNames[_map[i]] : "-";
                _vals[i].color = paired ? ACCENT : DIM;
                _clears[i].gameObject.SetActive(paired);
            }
            for (int i = 0; i < _dots.Length; i++)
                _dots[i].color = used[i] ? ACCENT : new Color(1f, 1f, 1f, _sel >= 0 ? 0.95f : 0.6f);
        }

        public void Tick()
        {
            var mouse = (Vector2)Input.mousePosition;
            int hot = -1;
            float best = PICK_RADIUS;
            for (int i = 0; i < _dots.Length; i++)
            {
                var sp = _cam.WorldToScreenPoint(_stage.position + (_m.BonePos[i] - _origin) * _k);
                bool vis = sp.z > 0f;
                _dots[i].gameObject.SetActive(vis);
                if (!vis) continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, new Vector2(sp.x, sp.y), null, out var lp);
                _dots[i].rectTransform.anchoredPosition = lp;
                float d = Vector2.Distance(mouse, new Vector2(sp.x, sp.y));
                if (d < best) { best = d; hot = i; }
            }

            for (int i = 0; i < _lines.Length; i++)
            {
                var line = _lines[i];
                if (line == null) continue;
                int p = _m.BoneParent[i];
                bool vis = _dots[i].gameObject.activeSelf && _dots[p].gameObject.activeSelf;
                line.gameObject.SetActive(vis);
                if (!vis) continue;
                var a = _dots[p].rectTransform.anchoredPosition;
                var b = _dots[i].rectTransform.anchoredPosition;
                line.rectTransform.anchoredPosition = (a + b) * 0.5f;
                line.rectTransform.sizeDelta = new Vector2((b - a).magnitude, 2f);
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            }

            for (int i = 0; i < _dots.Length; i++) _dots[i].rectTransform.localScale = Vector3.one * (i == hot ? 1.6f : 1f);
            _tip.SetActive(hot >= 0);
            if (hot < 0) return;
            _tipText.text = _m.BoneNames[hot];
            _tip.GetComponent<RectTransform>().anchoredPosition = _dots[hot].rectTransform.anchoredPosition + new Vector2(12f, 8f);
            _tip.transform.SetAsLastSibling();
        }

        public void Destroy()
        {
            UnityEngine.Object.Destroy(_root);
            UnityEngine.Object.Destroy(_dotRoot);
        }
    }
}
