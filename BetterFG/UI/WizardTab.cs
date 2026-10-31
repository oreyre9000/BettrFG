using System;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI
{
    public class WizardTab : BetterFGTab
    {
        public WizardTab(IntPtr ptr) : base(ptr) { }

        public int EditIndex = -1;

        protected static float ROW_H => 24f * UIScale.S;

        protected static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        protected static readonly Color LABEL = new Color(1f, 1f, 1f, 0.72f);
        protected static readonly Color WHITE = Color.white;
        protected static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        protected static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        protected static readonly Color ROW_IDLE = new Color(0.12f, 0.12f, 0.12f, 1f);
        protected static readonly Color ROW_SEL = new Color(0.25f, 0.45f, 0.25f, 1f);

        protected virtual string[] StepTitles => Array.Empty<string>();
        protected virtual int PanelCount => StepTitles.Length;
        protected virtual void OnStepShown(int step) { }
        protected int Step { get; set; }

        private GameObject[] _panels;
        private Text _stepHeader, _status;
        private Button _backBtn, _nextBtn;

        protected virtual void BuildStep(int step, RectTransform root, float w, float bodyH) { }
        protected virtual int LoadEditedEntry() => -1;
        protected virtual bool SkipLoadEditedEntry => false;
        protected virtual void ResumeWip() { }
        protected virtual bool CanAdvance(int step) => true;
        protected virtual int NextStepFrom(int step) => step + 1;
        protected virtual int PrevStepFrom(int step) => step - 1;
        protected virtual void RefreshSummary() { }
        protected virtual bool Save() => false;
        protected virtual string FinishLabel => EditIndex >= 0 ? "SAVE CHANGES" : "SAVE";
        protected virtual BetterFGTab MakeListTarget() => null;

        protected virtual float HeaderHeight => 0f;
        protected virtual void BuildHeader(RectTransform contentRoot, Rect area) { }

        protected virtual bool HasRemove => false;
        protected virtual bool HasStatus => true;
        protected virtual void OnRemoveClicked() { }
        protected virtual void OnLeave() { }

        void Update() => WinDialogs.Tick();

        protected void SetStatus(string msg)
        {
            if (_status != null) _status.text = msg;
            else Plugin.Log.LogInfo($"{TabTitle}: {msg}");
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float headerLblW = w;
            if (HasRemove)
            {
                float rmW = 64f * UIScale.S;
                headerLblW = w - rmW - PAD;
                UGUIShip.CreateButton(contentRoot, new Rect(PAD + headerLblW + PAD, VPAD, rmW, LH),
                    "Remove", new Color(0.55f, 0.15f, 0.15f, 1f), WHITE, FS_SM, new Action(OnRemoveThenLeave));
            }
            _stepHeader = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, VPAD, headerLblW, LH), "", FS_SM, LABEL);

            float bodyY = VPAD + LH + SH;
            float bodyH = TabHeight - bodyY - BTN_H - (HasStatus ? LH + SH : 0f) - VPAD - SH;

            float headerH = HeaderHeight;
            if (headerH > 0f)
            {
                BuildHeader(contentRoot, new Rect(PAD, bodyY, w, headerH));
                bodyY += headerH + SH;
                bodyH -= headerH + SH;
            }

            _panels = new GameObject[PanelCount];
            for (int i = 0; i < _panels.Length; i++)
            {
                var go = new GameObject("Panel");
                go.transform.SetParent(contentRoot, false);
                UGUIShip.SetPixelRect(go.AddComponent<RectTransform>(), new Rect(0f, bodyY, TabWidth, bodyH));
                _panels[i] = go;
                BuildStep(i, go.GetComponent<RectTransform>(), w, bodyH);
            }

            float navY = bodyY + bodyH + SH;
            float bw = (w - PAD) / 2f;
            _backBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD, navY, bw, BTN_H),
                "Back", BTN_DARK, WHITE, FS_SM, new Action(OnBack));
            _nextBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD + bw + PAD * 0.5f, navY, bw, BTN_H),
                "Next", BTN_BLUE, WHITE, FS_SM, new Action(OnNext));

            if (HasStatus)
                _status = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, navY + BTN_H + SH, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            if (EditIndex >= 0 && !SkipLoadEditedEntry)
            {
                int start = LoadEditedEntry();
                if (start >= 0) Step = start;
            }
            ResumeWip();
            RefreshStep();
        }

        private void OnRemoveThenLeave()
        {
            OnRemoveClicked();
            LeaveToList();
        }

        private void OnBack()
        {
            if (Step == 0) { LeaveToList(); return; }
            int p = PrevStepFrom(Step);
            if (p < 0) { LeaveToList(); return; }
            Step = p;
            RefreshStep();
        }

        private void OnNext()
        {
            if (Step == StepTitles.Length - 1) { if (Save()) LeaveToList(); return; }
            int n = NextStepFrom(Step);
            if (n >= StepTitles.Length) { if (Save()) LeaveToList(); return; }
            Step = n;
            RefreshStep();
        }

        protected void LeaveToList()
        {
            OnLeave();
            BetterFGUIMan.Instance?.SwitchSlotTab(this, MakeListTarget());
        }

        protected void RefreshStep()
        {
            for (int i = 0; i < _panels.Length; i++) _panels[i].SetActive(i == Step);

            var titles = StepTitles;
            _stepHeader.text = $"Step {Step + 1} of {titles.Length}  -  {titles[Step]}";

            bool last = Step == titles.Length - 1;
            var nlbl = _nextBtn.GetComponentInChildren<Text>();
            if (nlbl != null) nlbl.text = last ? FinishLabel : "NEXT >";

            bool can = CanAdvance(Step);
            _nextBtn.interactable = can;
            if (nlbl != null) nlbl.color = can ? WHITE : HINT;

            var blbl = _backBtn.GetComponentInChildren<Text>();
            if (blbl != null) blbl.text = Step == 0 ? "Cancel" : "Back";

            if (last) RefreshSummary();
            OnStepShown(Step);
        }
    }
}
