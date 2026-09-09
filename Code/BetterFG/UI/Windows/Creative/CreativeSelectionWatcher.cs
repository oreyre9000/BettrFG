using System;
using System.Collections;
using BetterFG.Core;
using BetterFG.Features.UnityRound.Editor;
using FG.Common;
using FGClient.UI.Core;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;

namespace BetterFG.UI.Windows.Creative
{
    // Watches the level-editor selection. While ≥1 object is selected it shows a nav prompt
    // ("Batch edit"); pressing it opens the BatchEditWindow. The prompt is torn down the moment
    // the selection is empty or the editor is exited, and the window closes itself once nothing's
    // selected. Persistent singleton spawned from Plugin.InitGameObjects.
    public class CreativeSelectionWatcher : MonoBehaviour
    {
        public CreativeSelectionWatcher(IntPtr ptr) : base(ptr) { }

        public static CreativeSelectionWatcher Instance { get; private set; }

        private NavPromptHandle _prompt;
        private bool _promptIsLink;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            Utilities.BfgProbe.Flush();
            Services.DiscordPresenceService.FlushPendingPush();

            // this component is DontDestroyOnLoad, so without the editor gate every round paid a
            // LevelEditorManager + GetMultiselectHandler interop round trip on every frame for nothing
            if (!UnityRoundLoader.InLevelEditor) { DestroyPrompt(); return; }

            var handler = LevelEditorManager.Instance?.GetMultiselectHandler();
            if (handler == null) { DestroyPrompt(); return; }
            if (handler.IsPlacingOrCloning) return;

            bool shouldPrompt = BatchEditWindow.FeatureEnabled && UnityRoundLoader.InLevelEditor && BatchRecolour.SelectionCount() >= 1;

            // turned off while the window's up → close it
            if (!BatchEditWindow.FeatureEnabled && BatchEditWindow.Instance != null)
                BatchEditWindow.Instance.Close();

            // the label has to say "Link" the moment a controller joins the selection, otherwise there's
            // nothing telling you the page exists. NavPrompt labels are baked at spawn, so flip = respawn.
            bool linkNow = shouldPrompt && BatchLink.Controller() != null;
            if (linkNow != _promptIsLink) { DestroyPrompt(); _promptIsLink = linkNow; }

            if (shouldPrompt) EnsurePrompt();
            else DestroyPrompt();

            if (_prompt != null && _prompt.IsAlive && _prompt.IsPressed())
                OpenWindow();
        }

        void LateUpdate()
        {
            if (!UnityRoundLoader.InLevelEditor) return;
            Features.CreativeGroups.CreativeGroups.TickGroupDrag();
        }

        private void EnsurePrompt()
        {
            if (_prompt != null && _prompt.IsAlive) return;
            var parent = NavPromptCore.GetCustomNavPromptRoot();
            if (parent == null) return;
            // LE_Edit is the editor "edit selection" glyph. AllowWhileUnfocused because the editor's
            // own UI owns focus while you're placing/selecting, so the default gameplay-focus gate
            // would swallow the press.
            _prompt = NavPromptCore.From(NavPrompt.Report)
                .WithLabel(_promptIsLink ? "Link to controller" : "Batch edit", "bfg_creative_batchedit")
                .AnchoredAt(NavPromptAnchor.BottomCenter)
                .AllowWhileUnfocused()
                .PollActions(RewiredConsts.Action.Menu_Report)
                .SpawnOn(parent);
        }

        private void DestroyPrompt()
        {
            if (_prompt == null) return;
            _prompt.Destroy();
            _prompt = null;
        }

        private void OpenWindow()
        {
            if (BatchEditWindow.Instance != null) return;
            Patches.BatchEditBlockPlacePatch.BlockedAPlace = false;
            var go = new GameObject("BetterFG_BatchEditWindow");
            go.AddComponent<BatchEditWindow>().Configure();
        }

        // the window destroys itself on close, so it can't run this itself — we host it. one frame after
        // the window's gone (AnyOpen already false) replay the place the prefix swallowed, so the
        // batch-edited selection commits at its current spot. only if we actually blocked one.
        public void PlaceAfterFrame()
        {
            StartCoroutine(PlaceNextFrame().WrapToIl2Cpp());
        }

        private IEnumerator PlaceNextFrame()
        {
            yield return null;
            if (!Patches.BatchEditBlockPlacePatch.BlockedAPlace) yield break;
            Patches.BatchEditBlockPlacePatch.BlockedAPlace = false;
            var h = Patches.BatchEditBlockPlacePatch.LiveHandler;
            if (h != null) h.PlaceMultiSelection();
        }
    }
}
