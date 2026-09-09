using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using BetterFG.Customization.Player;

namespace BetterFG.Features.Replay
{
    internal static class SaveReplay
    {
        public const string Extension = ".bfgreplay";
        public const string PickerFilter = "BettrFG Replay\0*.bfgreplay\0";
        public const string FramesExtension = ".bfgframes";
        public const int ContainerMagic = unchecked((int)0xBF9E0001);
        public const int FormatVersion = 23;

        public static string FramesPathFor(string path) => Path.ChangeExtension(path, FramesExtension);

        static void WriteFrameList(BinaryWriter bw, List<ReplayFrame> frames)
        {
            bw.Write(frames.Count);
            foreach (var f in frames)
            {
                bw.Write(f.t);
                bw.Write(f.pos.x); bw.Write(f.pos.y); bw.Write(f.pos.z);
                bw.Write(f.rot.x); bw.Write(f.rot.y); bw.Write(f.rot.z); bw.Write(f.rot.w);
                bw.Write(f.stateHash);
                bw.Write(f.animTime);
                bw.Write(f.ragdoll);
                bw.Write(f.upperBody.x); bw.Write(f.upperBody.y); bw.Write(f.upperBody.z); bw.Write(f.upperBody.w);
                bw.Write(f.armLeft.x); bw.Write(f.armLeft.y); bw.Write(f.armLeft.z); bw.Write(f.armLeft.w);
                bw.Write(f.armRight.x); bw.Write(f.armRight.y); bw.Write(f.armRight.z); bw.Write(f.armRight.w);
            }
        }

        static void WriteContainer(ReplayRecording rec, string path)
        {
            var json = Encoding.UTF8.GetBytes(Serialise(rec));
            using (var bw = new BinaryWriter(File.Create(path)))
            {
                bw.Write(ContainerMagic);
                bw.Write(json.Length);
                bw.Write(json);
                bw.Write(rec.players.Count);
                foreach (var p in rec.players)
                {
                    bw.Write(p.playerId);
                    WriteFrameList(bw, p.frames);
                }
                WriteFrameList(bw, rec.cameraFrames);
                WriteAudio(bw, rec);
                WriteStarchart(bw, rec);
                WriteVfx(bw, rec);
                WriteTail(bw, rec);
                WriteLevel(bw, rec);
                WriteWorld(bw, rec);
                WriteTextures(bw, rec);
                WriteSpeech(bw, rec);
                WriteGhosts(bw, rec);
                WritePowerups(bw, rec);
                WriteFrameList(bw, rec.petFrames);
                WriteMachinima(bw, rec);
            }
        }

        static void WriteMachinima(BinaryWriter bw, ReplayRecording rec)
        {
            bw.Write(rec.isMachinima);
            if (!rec.isMachinima) return;
#if MACHINIMA

            foreach (var p in rec.players)
            {
                bw.Write(p.inTime);

                bw.Write(p.moves.Count);
                foreach (var m in p.moves)
                {
                    bw.Write(m.time);
                    bw.Write(m.position.x); bw.Write(m.position.y); bw.Write(m.position.z);
                    bw.Write(m.yaw);
                    bw.Write(m.faceMotion);
                }

                bw.Write(p.actions.Count);
                foreach (var a in p.actions)
                {
                    bw.Write(a.time);
                    bw.Write((int)a.kind);
                    bw.Write(a.duration);
                    bw.Write(a.targetPlayerId);
                    bw.Write(a.targetObject);
                    bw.Write(a.payload ?? "");
                }
            }
#endif
        }

        static void WritePowerups(BinaryWriter bw, ReplayRecording rec)
        {
            bw.Write(rec.powerupEvents.Count);
            foreach (var e in rec.powerupEvents)
            {
                bw.Write(e.t);
                bw.Write(e.playerId);
                bw.Write(e.kind);
                bw.Write(e.on);
            }
        }

        static void WriteGhosts(BinaryWriter bw, ReplayRecording rec)
        {
            bw.Write(rec.ghosts.Count);
            foreach (var ghost in rec.ghosts)
            {
                bw.Write(ghost.name);
                WriteFrameList(bw, ghost.frames);
            }
        }

        static void WriteSpeech(BinaryWriter bw, ReplayRecording rec)
        {
            bw.Write(rec.speechOptions.Count);
            foreach (var option in rec.speechOptions)
            {
                bw.Write(option.itemId);
                bw.Write(option.speechId);
                bw.Write(option.duration);
                bw.Write(option.text);
                bw.Write(option.hasText);
                bw.Write(option.shiny);
                bw.Write(option.image == null ? 0 : option.image.Length);
                if (option.image != null && option.image.Length > 0) bw.Write(option.image);
            }

            bw.Write(rec.speechEvents.Count);
            foreach (var speech in rec.speechEvents)
            {
                bw.Write(speech.t);
                bw.Write(speech.end);
                bw.Write(speech.playerId);
                bw.Write(speech.option);
            }
        }

        static void WriteTextures(BinaryWriter bw, ReplayRecording rec)
        {
            int packed = 0;
            foreach (var p in rec.players)
                foreach (var tex in p.bfgTextures)
                {
                    bw.Write(tex.overrides.Count);
                    foreach (var ov in tex.overrides)
                    {
                        var bytes = TextureBytes(ov);
                        bw.Write(bytes == null ? 0 : bytes.Length);
                        if (bytes == null || bytes.Length == 0) continue;
                        bw.Write(bytes);
                        packed++;
                    }
                }

            if (packed > 0) Plugin.Log.LogInfo($"{packed} custom textures packed into the replay");
        }

        static byte[] TextureBytes(SkinTexOverride ov)
        {
            if (ov.texData != null && ov.texData.Length > 0) return ov.texData;
            try
            {
                if (!string.IsNullOrEmpty(ov.texPath) && File.Exists(ov.texPath))
                    return File.ReadAllBytes(ov.texPath);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't pack the texture '{ov.texName}': {ex.Message}"); }
            return null;
        }

        static void WriteWorld(BinaryWriter bw, ReplayRecording rec)
        {
            bw.Write(rec.worldObjects.Count);
            foreach (var obj in rec.worldObjects)
            {
                bw.Write(obj.path);
                bw.Write(obj.guid);
                bw.Write(obj.owner);
                bw.Write(obj.prefab);
                bw.Write(obj.spawnTime);
                bw.Write(obj.despawnTime);

                bw.Write(obj.frames.Count);
                foreach (var f in obj.frames)
                {
                    bw.Write(f.t);
                    bw.Write(f.pos.x); bw.Write(f.pos.y); bw.Write(f.pos.z);
                    bw.Write(f.rot.x); bw.Write(f.rot.y); bw.Write(f.rot.z); bw.Write(f.rot.w);
                    bw.Write(f.scale.x); bw.Write(f.scale.y); bw.Write(f.scale.z);
                    bw.Write(f.stateHash);
                    bw.Write(f.animTime);
                    bw.Write(f.active);
                }

                bw.Write(obj.states.Count);
                foreach (var s in obj.states)
                {
                    bw.Write(s.t);
                    bw.Write(s.state);
                }
            }
        }

        static void WriteLevel(BinaryWriter bw, ReplayRecording rec)
        {
            if (string.IsNullOrEmpty(rec.levelJson)) { bw.Write(0); return; }

            using (var ms = new MemoryStream())
            {
                var raw = Encoding.UTF8.GetBytes(rec.levelJson);
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                    gz.Write(raw, 0, raw.Length);

                var packed = ms.ToArray();
                bw.Write(packed.Length);
                bw.Write(packed);
            }
        }

        static void WriteAudio(BinaryWriter bw, ReplayRecording rec)
        {
            bw.Write(rec.audioKeys.Count);
            foreach (var key in rec.audioKeys) bw.Write(key);

            bw.Write(rec.audioParamNames.Count);
            foreach (var name in rec.audioParamNames) bw.Write(name);

            bw.Write(rec.audioParams.Count);
            foreach (var p in rec.audioParams) { bw.Write(p.name); bw.Write(p.value); }

            bw.Write(rec.audioEvents.Count);
            foreach (var e in rec.audioEvents)
            {
                bw.Write(e.t);
                bw.Write(e.end);
                bw.Write(e.playerId);
                bw.Write(e.pos.x); bw.Write(e.pos.y); bw.Write(e.pos.z);
                bw.Write(e.key);
                bw.Write(e.paramStart);
                bw.Write(e.paramCount);
            }
        }

        static void WriteStarchart(BinaryWriter bw, ReplayRecording rec)
        {
            bw.Write(rec.starchartPaths.Count);
            foreach (var path in rec.starchartPaths) bw.Write(path);

            bw.Write(rec.starchartEvents.Count);
            foreach (var e in rec.starchartEvents)
            {
                bw.Write(e.t);
                bw.Write(e.pathStart);
                bw.Write(e.pathCount);
            }
        }

        static void WriteVfx(BinaryWriter bw, ReplayRecording rec)
        {
            bw.Write(rec.diveSlideVfxEvents.Count);
            foreach (var e in rec.diveSlideVfxEvents)
            {
                bw.Write(e.t);
                bw.Write(e.playerId);
            }
        }

        static void WriteTail(BinaryWriter bw, ReplayRecording rec)
        {
            bw.Write(rec.tailPrefab);
            bw.Write(rec.tailBoneName);
            bw.Write(rec.tailLocalPos.x); bw.Write(rec.tailLocalPos.y); bw.Write(rec.tailLocalPos.z);
            bw.Write(rec.tailLocalRot.x); bw.Write(rec.tailLocalRot.y); bw.Write(rec.tailLocalRot.z); bw.Write(rec.tailLocalRot.w);
            bw.Write(rec.tailLocalScale.x); bw.Write(rec.tailLocalScale.y); bw.Write(rec.tailLocalScale.z);

            bw.Write(rec.tailEvents.Count);
            foreach (var e in rec.tailEvents)
            {
                bw.Write(e.t);
                bw.Write(e.playerId);
                bw.Write(e.enabled);
            }
        }

        public static string ReplayDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BettrFG", "Replays");

        public static string Write(ReplayRecording rec)
        {
            if (rec == null) return null;
            try
            {
                Directory.CreateDirectory(ReplayDir);
                string path = Path.Combine(ReplayDir, FileName(rec));
                WriteContainer(rec, path);
                ReplayThumbnail.Write(rec.thumbJpg, path);

                int frames = 0;
                foreach (var p in rec.players) frames += p.frames.Count;
                Plugin.Log.LogInfo($"replay written: {Path.GetFileName(path)} — {rec.players.Count} players, {frames} frames, {rec.cameraFrames.Count} camera, {rec.duration:0.0}s");
                return path;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"replay save failed for {rec.roundName}: {ex.Message}");
                return null;
            }
        }

        public static void WriteTo(ReplayRecording rec, string path)
        {
            if (rec == null || string.IsNullOrEmpty(path)) return;
            try { WriteContainer(rec, path); LoadReplay.Remember(path); }
            catch (Exception ex) { Plugin.Log.LogWarning($"replay export to {path} failed: {ex.Message}"); }
        }

        static string FileName(ReplayRecording rec)
        {
            string label = string.IsNullOrEmpty(rec.name) ? rec.roundName : rec.name;
            if (string.IsNullOrEmpty(label)) label = rec.roundId;
            if (string.IsNullOrEmpty(label)) label = "ui.round";
            label = string.Concat(label.Split(Path.GetInvalidFileNameChars()));
            return DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + label + Extension;
        }

        static string Serialise(ReplayRecording rec)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            Num(sb, "version", FormatVersion, true);
            Str(sb, "replayName", rec.name);
            Str(sb, "recordedAt", rec.recordedAt);
            Str(sb, "buildHash", rec.buildHash);
            Str(sb, "roundId", rec.roundId);
            Str(sb, "roundName", rec.roundName);
            Str(sb, "sceneName", rec.sceneName);
            Str(sb, "archetypeId", rec.archetypeId);
            Str(sb, "shareCode", rec.shareCode);
            Num(sb, "levelVersion", rec.levelVersion);
            Str(sb, "backgroundScene", rec.backgroundScene);
            Bool(sb, "isUgc", rec.isUgc);
            Bool(sb, "isMachinima", rec.isMachinima);
            Bool(sb, "isFinal", rec.isFinal);
            Num(sb, "squadSize", (int)rec.squadSize);
            sb.Append(",\"duration\":").Append(rec.duration.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            Float(sb, "trimStart", rec.trimStart);
            Float(sb, "trimEnd", rec.trimEnd);
            Str(sb, "petId", rec.petId);

            sb.Append(",\"sets\":[");
            for (int i = 0; i < rec.sets.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('{');
                Str(sb, "key", rec.sets[i].key, true);
                Str(sb, "chosen", rec.sets[i].chosen);
                Str(sb, "path", rec.sets[i].path);
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"keyframes\":[");
            for (int i = 0; i < rec.keyframes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var k = rec.keyframes[i];
                sb.Append('{');
                Float(sb, "time", k.time, true);
                Num(sb, "cameraType", (int)k.cameraType);
                Num(sb, "lookAt", (int)k.lookAt);
                Num(sb, "easingCurve", (int)k.easingCurve);
                Num(sb, "easingDirection", (int)k.easingDirection);
                Float(sb, "px", k.position.x);
                Float(sb, "py", k.position.y);
                Float(sb, "pz", k.position.z);
                Float(sb, "rx", k.rotation.x);
                Float(sb, "ry", k.rotation.y);
                Float(sb, "rz", k.rotation.z);
                Float(sb, "rw", k.rotation.w);
                Float(sb, "fov", k.fov);
                Num(sb, "targetPlayerId", (int)k.targetPlayerId);
                Num(sb, "lookAtPlayerId", (int)k.lookAtPlayerId);
                Num(sb, "targetObject", k.targetObject);
                Num(sb, "lookAtObject", k.lookAtObject);
                Float(sb, "speed", k.speed);
                Num(sb, "cut", k.cut ? 1 : 0);
                Num(sb, "cutToNext", k.cutToNext ? 1 : 0);
                Num(sb, "shakeKind", (int)k.shakeKind);
                Num(sb, "shakeTier", k.shakeTier);
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"visibilityKeyframes\":[");
            for (int i = 0; i < rec.visibilityKeyframes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var v = rec.visibilityKeyframes[i];
                sb.Append('{');
                Float(sb, "time", v.time, true);
                Bool(sb, "showPhrases", v.showPhrases);
                Num(sb, "crowns", (int)v.crowns);
                Str(sb, "crownOnlyPlayers", string.Join("|", v.crownOnlyPlayers));
                Num(sb, "names", (int)v.names);
                Str(sb, "nameOnlyPlayers", string.Join("|", v.nameOnlyPlayers));
                Num(sb, "playersMode", (int)v.players);
                Str(sb, "onlyPlayers", string.Join("|", v.onlyPlayers));
                Bool(sb, "showGhosts", v.showGhosts);
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"postFxKeyframes\":[");
            for (int i = 0; i < rec.postFxKeyframes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var fx = rec.postFxKeyframes[i];
                sb.Append('{');
                Float(sb, "time", fx.time, true);
                Float(sb, "exposure", fx.exposure);
                Float(sb, "contrast", fx.contrast);
                Float(sb, "saturation", fx.saturation);
                Float(sb, "temperature", fx.temperature);
                Float(sb, "tint", fx.tint);
                Float(sb, "vignette", fx.vignette);
                Float(sb, "chromaticAberration", fx.chromaticAberration);
                Float(sb, "bloomIntensity", fx.bloomIntensity);
                Float(sb, "bloomThreshold", fx.bloomThreshold);
                Float(sb, "sharpenAmount", fx.sharpenAmount);
                Float(sb, "sharpenRadius", fx.sharpenRadius);
                Float(sb, "dofStrength", fx.dofStrength);
                Float(sb, "dofDistance", fx.dofDistance);
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"players\":[");
            for (int i = 0; i < rec.players.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var p = rec.players[i];
                sb.Append('{');
                sb.Append("\"playerId\":").Append(p.playerId);
                Str(sb, "name", p.name);
                Str(sb, "generatedName", p.generatedName);
                Str(sb, "accountId", p.accountId);
                Str(sb, "platformId", p.platformId);
                Num(sb, "teamId", p.teamId);
                Num(sb, "squadId", (int)p.squadId);
                Str(sb, "partyId", p.partyId);
                Bool(sb, "isLocal", p.isLocal);
                Bool(sb, "isBot", p.isBot);
                Str(sb, "colour", p.colour);
                Str(sb, "pattern", p.pattern);
                Str(sb, "costumeTop", p.costumeTop);
                Str(sb, "costumeBottom", p.costumeBottom);
                Str(sb, "costumeFull", p.costumeFull);
                Str(sb, "faceplate", p.faceplate);
                Str(sb, "victoryPose", p.victoryPose);
                Str(sb, "nickname", p.nickname);
                Str(sb, "nameplate", p.nameplate);
                Num(sb, "fameEarnedBadge", p.fameEarnedBadge);
                Num(sb, "crownsEarned", p.crownsEarned);
                Str(sb, "fameUpdatedAt", p.fameUpdatedAt.ToString("o"));
                Float(sb, "bfgScale", p.bfgScale);
                Float(sb, "outTime", p.outTime);
                Str(sb, "bfgCosmetics", p.bfgCosmetics);
                Str(sb, "bfgColour", p.bfgColour);
                Str(sb, "bfgPattern", p.bfgPattern);
                Str(sb, "bfgFaceplate", p.bfgFaceplate);
                Bool(sb, "hasNametag", p.nametag != null);
                if (p.nametag != null)
                {
                    var nt = p.nametag;
                    Float(sb, "ntR", nt.r);
                    Float(sb, "ntG", nt.g);
                    Float(sb, "ntB", nt.b);
                    Bool(sb, "ntBold", nt.bold);
                    Bool(sb, "ntItalic", nt.italic);
                    Str(sb, "ntCustomName", nt.customName);
                    Str(sb, "ntIconMode", nt.iconMode);
                    Str(sb, "ntIconCountry", nt.iconCountry);
                    Str(sb, "ntIconPath", nt.iconPath);
                    Float(sb, "ntIconScale", nt.iconScale);
                    Float(sb, "ntIconOffX", nt.iconOffX);
                    Float(sb, "ntIconOffY", nt.iconOffY);
                    Str(sb, "ntPlatformHide", nt.platformHide);
                    Str(sb, "ntPlatformCustom", nt.platformCustom);
                    Str(sb, "ntNameStyle", nt.nameStyle);
                    Str(sb, "ntGradientColors", nt.gradientColors);
                    Float(sb, "ntGradientAngle", nt.gradientAngle);
                    Bool(sb, "ntOutlineEnabled", nt.outlineEnabled);
                    Float(sb, "ntOutlineR", nt.outlineR);
                    Float(sb, "ntOutlineG", nt.outlineG);
                    Float(sb, "ntOutlineB", nt.outlineB);
                    Float(sb, "ntOutlineWidth", nt.outlineWidth);
                    Str(sb, "ntOutlineMode", nt.outlineMode);
                    Bool(sb, "ntBackingEnabled", nt.backingEnabled);
                    Str(sb, "ntBackingPath", nt.backingPath);
                    Float(sb, "ntBackingOffX", nt.backingOffX);
                    Float(sb, "ntBackingOffY", nt.backingOffY);
                    Float(sb, "ntBackingScale", nt.backingScale);
                    Str(sb, "ntNickname", nt.nickname);
                }

                sb.Append(",\"bfgSkins\":[");
                for (int s = 0; s < p.bfgSkins.Count; s++)
                {
                    if (s > 0) sb.Append(',');
                    var skin = p.bfgSkins[s];
                    sb.Append('{');
                    Str(sb, "file", skin.file, true);
                    Str(sb, "type", skin.type);
                    Str(sb, "source", skin.source);
                    Str(sb, "localPath", skin.localPath);
                    Str(sb, "repoUrl", skin.repoUrl);
                    Str(sb, "folder", skin.folder);
                    Num(sb, "hand", skin.hand);
                    sb.Append('}');
                }
                sb.Append(']');

                sb.Append(",\"bfgTextures\":[");
                for (int t = 0; t < p.bfgTextures.Count; t++)
                {
                    if (t > 0) sb.Append(',');
                    var tex = p.bfgTextures[t];
                    sb.Append('{');
                    Str(sb, "entryName", tex.entryName, true);
                    Str(sb, "matNames", string.Join("|", tex.matNames));
                    sb.Append(",\"overrides\":[");
                    for (int o = 0; o < tex.overrides.Count; o++)
                    {
                        if (o > 0) sb.Append(',');
                        sb.Append('{');
                        Str(sb, "texName", tex.overrides[o].texName, true);
                        sb.Append('}');
                    }
                    sb.Append(']');
                    sb.Append('}');
                }
                sb.Append(']');

                sb.Append('}');
            }
            sb.Append(']');

            sb.Append('}');
            return sb.ToString();
        }

        static void Str(StringBuilder sb, string key, string value, bool first = false)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(key).Append("\":\"").Append(Escape(value)).Append('"');
        }

        static void Float(StringBuilder sb, string key, float value, bool first = false)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(key).Append("\":")
              .Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        static void Num(StringBuilder sb, string key, int value, bool first = false)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(key).Append("\":").Append(value);
        }

        static void Bool(StringBuilder sb, string key, bool value)
        {
            sb.Append(",\"").Append(key).Append("\":").Append(value ? "true" : "false");
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
