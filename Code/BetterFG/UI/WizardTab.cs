using System;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI
{
    // shared shell for a "linear multi-step form ending in Save" tab (skin texture override, font
    // override, ...). subclasses supply the step titles, build each step's panel content, say whether
    // the current step can advance, and do the actual save. everything else — panel switching, the
    // step header, Back/Next button behaviour and labels, the status line — lives here once so a new
    // wizard is just: list the steps, build them, validate, save.
    // NOT abstract, on purpose — IL2Cpp class injection can't build a vtable through an abstract
    // method in the hierarchy ("VTable method was null even though base type isn't abstract" kills
    // plugin load entirely). every "abstract" member below is virtual with a stub instead, matching
    // how Tab/SwitchTab already do it.
    public class WizardTab : Tab
    {
        public WizardTab(IntPtr ptr) : base(ptr) { }

        public int EditIndex = -1;

        protected static float ROW_H => 24f * UIScale.S;

        protected static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        protected static readonly Color LABEL = new Color(1f, 1f, 1f, 0.72f);
        protected static readonly Color WHITE = UGUIShip.WHITE;
        protected static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        protected static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        protected static readonly Color ROW_IDLE = new Color(0.12f, 0.12f, 0.12f, 1f);
        protected static readonly Color ROW_SEL = new Color(0.25f, 0.45f, 0.25f, 1f);

        protected virtual string[] StepTitles => Array.Empty<string>();
        // settable so ResumeWip can jump straight to the step the user was on before a linked tab
        protected int Step { get; set; }

        private GameObject[] _panels;
        private Text _stepHeader, _status;
        private Button _backBtn, _nextBtn;

        // build step `step`'s content into root, sized w x bodyH — called once per step, in order
        protected virtual void BuildStep(int step, RectTransform root, float w, float bodyH) { }
        // EditIndex >= 0: restore saved state and return the step to open on, or -1 to stay on step 0
        protected virtual int LoadEditedEntry() => -1;
        // true to skip the LoadEditedEntry disk reload entirely - for a subclass resuming WIP state
        // handed over from a linked tab instead (see ResumeWip), where re-reading from disk would
        // stomp the in-progress edit
        protected virtual bool SkipLoadEditedEntry => false;
        // called once, after every step panel exists (and after LoadEditedEntry, unless skipped) -
        // for a subclass resuming state handed over from an out-of-band linked tab (e.g. a "back"
        // link on a sub-tab), distinct from LoadEditedEntry's disk-backed reload. no-op by default.
        protected virtual void ResumeWip() { }
        protected virtual bool CanAdvance(int step) => true;
        protected virtual int NextStepFrom(int step) => step + 1;
        protected virtual int PrevStepFrom(int step) => step - 1;
        protected virtual void RefreshSummary() { }
        // validate + persist; return true to leave back to the list, false (with a SetStatus) to stay
        protected virtual bool Save() => false;
        protected virtual string FinishLabel => EditIndex >= 0 ? "SAVE CHANGES" : "SAVE";
        protected virtual Tab MakeListTarget() => null;

        // optional fixed-height region between the step header and the step panels, e.g. a live preview
        // that should stay visible across every step (and every step change) instead of living inside
        // one step. 0 (default) means no header - existing wizards are unaffected.
        protected virtual float HeaderHeight => 0f;
        protected virtual void BuildHeader(RectTransform contentRoot, Rect area) { }

        // optional small button next to the step header, visible on every step - resets/clears this
        // wizard's whole setting instead of saving it, then leaves to the list same as Save.
        protected virtual bool HasRemove => false;
        protected virtual void OnRemoveClicked() { }
        // called right before switching away to the list tab, on both save and cancel - a subclass
        // that live-previews edits before Save persists them can revert here
        protected virtual void OnLeave() { }
        public override Tab MakeFallbackTab() => MakeListTarget();

        protected void SetStatus(string msg) { if (_status != null) UGUIShip.RelabelText(_status, msg); }

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float headerLblW = w;
            if (HasRemove)
            {
                float rmW = 64f * UIScale.S;
                headerLblW = w - rmW - PAD;
                UGUIShip.CreateButton(contentRoot, new Rect(PAD + headerLblW + PAD, VPAD, rmW, LH),
                    "ui.remove", new Color(0.55f, 0.15f, 0.15f, 1f), WHITE, FS_SM, new Action(OnRemoveThenLeave));
            }
            _stepHeader = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, VPAD, headerLblW, LH), "", FS_SM, LABEL);

            float bodyY = VPAD + LH + SH;
            float bodyH = TabHeight - bodyY - BTN_H - LH - VPAD - SH * 2f;

            float headerH = HeaderHeight;
            if (headerH > 0f)
            {
                BuildHeader(contentRoot, new Rect(PAD, bodyY, w, headerH));
                bodyY += headerH + SH;
                bodyH -= headerH + SH;
            }

            var titles = StepTitles;
            _panels = new GameObject[titles.Length];
            for (int i = 0; i < titles.Length; i++)
            {
                _panels[i] = MakePanel(contentRoot, bodyY, bodyH);
                BuildStep(i, _panels[i].GetComponent<RectTransform>(), w, bodyH);
            }

            float navY = bodyY + bodyH + SH;
            float bw = (w - PAD) / 2f;
            _backBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD, navY, bw, BTN_H),
                "ui.back", BTN_DARK, WHITE, FS_SM, new Action(OnBack));
            _nextBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD + bw + PAD * 0.5f, navY, bw, BTN_H),
                "ui.next", BTN_BLUE, WHITE, FS_SM, new Action(OnNext));

            _status = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, navY + BTN_H + SH, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            if (EditIndex >= 0 && !SkipLoadEditedEntry)
            {
                int start = LoadEditedEntry();
                if (start >= 0) Step = start;
            }
            ResumeWip();
            RefreshStep();
        }

        private GameObject MakePanel(RectTransform parent, float y, float h)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rt, new Rect(0f, y, TabWidth, h));
            return go;
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
            _stepHeader.text = $"Step {Step + 1} of {titles.Length}  -  {LocalizationService.Get(titles[Step])}";

            bool last = Step == titles.Length - 1;
            var nlbl = _nextBtn.GetComponentInChildren<Text>();
            if (nlbl != null) UGUIShip.RelabelText(nlbl, last ? FinishLabel : "NEXT >");

            bool can = CanAdvance(Step);
            _nextBtn.interactable = can;
            if (nlbl != null) nlbl.color = can ? WHITE : HINT;

            var blbl = _backBtn.GetComponentInChildren<Text>();
            if (blbl != null) UGUIShip.RelabelText(blbl, Step == 0 ? "ui.cancel_2" : "ui.back");

            if (last) RefreshSummary();
        }
    }
}
