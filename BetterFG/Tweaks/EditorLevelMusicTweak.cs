using System;
using Events;
using FG.Common;
using MPG.Utility;

namespace BetterFG.Tweaks
{
    public class EditorLevelMusicTweak : BfgTweak
    {
        public EditorLevelMusicTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "editor_level_music";
        public override string TweakLabel => "tweak.editor_level_music";
        public override string TweakTooltip => "tweak.editor_level_music.tip";

        AudioLevelEditorStateListener _listener;
        IntPtr _ours;
        bool _reload;
        EventSystem.Handle _musicHandle;

        public override void EnableTweak()
        {
            if (_musicHandle == null)
                _musicHandle = Broadcaster.Instance.Register<LevelEditorMusicChangedEvent>(new Action<LevelEditorMusicChangedEvent>(OnMusicChanged));
        }

        public override void DisableTweak()
        {
            _reload = false;
            if (_listener != null && _listener._isEditingMusicPlaying && _listener._editingMusicEvent.Pointer == _ours)
            {
                var audio = LevelEditorManager.Instance.Audio;
                audio.StopBuildMusic();
                audio.StartBuildMusic();
                Plugin.Log.LogInfo("editor level music off, fraggle build music back");
            }
            _ours = IntPtr.Zero;
        }

        void OnMusicChanged(LevelEditorMusicChangedEvent e)
        {
            if (IsEnabled) _reload = true;
        }

        void Update()
        {
            var lem = LevelEditorManager.Instance;
            if (lem == null) { _listener = null; return; }
            if (_listener == null) _listener = FindObjectOfType<AudioLevelEditorStateListener>();
            if (_listener == null) return;

            if (_reload)
            {
                _reload = false;
                lem.Audio.UnloadGameplayMusicAmbienceAndPreparationPhaseMusic();
                lem.Audio.LoadGameplayMusicAmbienceAndPreparationPhaseMusic();
                _ours = IntPtr.Zero;
            }

            if (_listener._isEditingMusicPlaying && _listener._editingMusicEvent.Pointer != _ours) Swap();
        }

        void Swap()
        {
            var old = _listener._editingMusicEvent;
            if (BetterFG.Features.CreativeExpansion.YoutubeLevelMusic.HasVideo)
            {
                _ours = old.Pointer;
                return;
            }

            var track = _listener._musicEvent;
            if (string.IsNullOrEmpty(track))
            {
                _ours = old.Pointer;
                Plugin.Log.LogInfo("level has no track picked, leaving the build music alone");
                return;
            }

            old.GetPaused(out bool paused);
            AudioManager.StopAndReleaseAudioEvent(ref old, false);
            var inst = AudioManager.CreateAudio(track, null, 60f);
            inst.Start(null, false);
            if (paused) inst.SetPaused(true);
            _listener._editingMusicEvent = inst;
            _ours = inst.Pointer;
            Plugin.Log.LogInfo($"editor music now {track}{(paused ? ", paused for now" : "")}");
        }
    }
}
