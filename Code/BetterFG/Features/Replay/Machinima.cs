#if MACHINIMA
using System;
using System.Collections.Generic;
using FG.Common;
using UnityEngine;

namespace BetterFG.Features.Replay
{
    public enum MachinimaActionKind { Jump, Dive, GrabObject, GrabPlayer, Phrase, Emote }

    public class MachinimaKey
    {
        public float time;
    }

    public class MachinimaMove : MachinimaKey
    {
        public Vector3 position;
        public float yaw;
        public bool faceMotion = true;
    }

    public class MachinimaAction : MachinimaKey
    {
        public MachinimaActionKind kind;
        public float duration = 1f;
        public uint targetPlayerId;
        public int targetObject = -1;
        public string payload = "";

        public static string KindLabel(MachinimaActionKind k)
        {
            switch (k)
            {
                case MachinimaActionKind.Dive: return "machinima.action.dive";
                case MachinimaActionKind.GrabObject: return "machinima.action.grab_object";
                case MachinimaActionKind.GrabPlayer: return "machinima.action.grab_player";
                case MachinimaActionKind.Phrase: return "machinima.action.phrase";
                case MachinimaActionKind.Emote: return "machinima.action.emote";
                default: return "machinima.action.jump";
            }
        }
    }

    internal static class MachinimaBaker
    {
        public const float MinZone = 0.5f;
        public const float Step = 1f / 60f;
        const float Arrive = 0.2f;
        const float StopSpeed = 0.08f;
        const int StopProbeSteps = 240;
        const int MaxSteps = 36000;
        const float SegTimeout = 30f;

        static CharacterControllerData _data;

        static CharacterControllerData Data()
        {
            if (_data != null) return _data;

            foreach (var d in Resources.FindObjectsOfTypeAll<CharacterControllerData>()) { _data = d; break; }
            if (_data == null) Plugin.Log.LogWarning("no CharacterControllerData loaded yet, machinima beans have nothing to walk with");
            else Plugin.Log.LogInfo($"machinima movement is the game's own: {_data.normalMaxSpeed} u/s flat out, turn {_data.normalTurnSpeed}, jump {_data.jumpForce}, dive {_data.diveForce} at {_data.diveAngleInDegrees} deg, gravity x{_data.gravityScale}");
            return _data;
        }

        static Vector3 StepVelocity(CharacterControllerData d, Vector3 vel, Quaternion rot, Vector3 dir, float input)
        {
            var target = dir * (d.normalMaxSpeed * input);
            var diff = target - vel;
            float diffMag = diff.magnitude;

            var scaling = input > 0f ? d.normalVelocityScalingCurve : d.normalDragScalingCurve;
            var blended = Vector3.MoveTowards(vel, target, scaling.Evaluate(diffMag / (d.normalMaxSpeed * 2f)) * diffMag * Step);
            if (input <= 0f) return blended;

            var inv = Quaternion.Inverse(rot);
            var localVel = inv * vel;
            float accel = d.forwardAccCurve.Evaluate(Mathf.Clamp01(localVel.magnitude / d.normalMaxSpeed));
            var pushed = Vector3.MoveTowards(localVel, inv * target, Step * accel);

            float swing = vel.sqrMagnitude > 0.0001f ? Vector3.Angle(vel.normalized, dir) : 0f;
            return rot * Vector3.MoveTowards(pushed, inv * blended, d.turnModCurve.Evaluate(Mathf.Clamp01(swing / 180f)) * diffMag);
        }

        static float StoppingDistance(CharacterControllerData d, Vector3 vel, Quaternion rot)
        {
            float travelled = 0f;
            for (int n = 0; n < StopProbeSteps && vel.magnitude > StopSpeed; n++)
            {
                vel = StepVelocity(d, vel, rot, Vector3.zero, 0f);
                travelled += vel.magnitude * Step;
            }
            return travelled;
        }

        public static void Bake(ReplayPlayer p)
        {
            p.frames.Clear();
            p.moves.Sort((a, b) => a.time.CompareTo(b.time));
            p.actions.Sort((a, b) => a.time.CompareTo(b.time));
            if (p.moves.Count == 0) return;

            var d = Data();
            if (d == null) return;

            float t = Mathf.Max(p.moves[0].time, p.inTime);
            p.moves[0].time = t;

            var pos = p.moves[0].position;
            var vel = Vector3.zero;
            var rot = Quaternion.identity;
            if (p.moves.Count > 1)
            {
                var first = p.moves[1].position - pos;
                first.y = 0f;
                if (first.sqrMagnitude > 0.0001f) rot = Quaternion.LookRotation(first.normalized, Vector3.up);
            }
            p.frames.Add(new ReplayFrame { t = t, pos = pos, rot = rot });

            int wp = 1;
            int ai = 0;
            bool holding = false;
            bool braking = false;
            bool airborne = false;
            float closest = float.MaxValue;
            float segStart = t;
            float airY = 0f;
            float airVy = 0f;
            float diveUntil = -1f;
            float busyUntil = t;

            for (int guard = 0; guard < MaxSteps; guard++)
            {
                if (wp >= p.moves.Count && !airborne && ai >= p.actions.Count && t >= p.outTime - Step) break;

                while (ai < p.actions.Count && p.actions[ai].time <= t)
                {
                    var a = p.actions[ai++];
                    if (a.kind == MachinimaActionKind.Jump)
                    {
                        airborne = true;
                        airVy = d.jumpForce.y;
                        vel += rot * new Vector3(0f, 0f, d.jumpForce.z);
                        busyUntil = Mathf.Max(busyUntil, a.time + a.duration);
                    }
                    else if (a.kind == MachinimaActionKind.Dive)
                    {
                        airborne = true;
                        float pitch = d.diveAngleInDegrees * Mathf.Deg2Rad;
                        vel = rot * Vector3.forward * (Mathf.Cos(pitch) * d.diveForce);
                        airVy = Mathf.Sin(pitch) * d.diveForce;
                        diveUntil = a.time + a.duration;
                        busyUntil = Mathf.Max(busyUntil, diveUntil);
                    }
                }

                bool diving = t < diveUntil;
                var dir = Vector3.zero;
                float input = 0f;

                if (wp < p.moves.Count)
                {
                    var to = p.moves[wp];
                    var flat = to.position - pos;
                    flat.y = 0f;
                    float dist = flat.magnitude;
                    float flatSpeed = new Vector2(vel.x, vel.z).magnitude;

                    if (!holding && (dist <= Arrive
                        || (!braking && dist > closest && dist < 1.5f)
                        || (braking && flatSpeed <= StopSpeed)
                        || t - segStart > SegTimeout))
                        holding = true;

                    if (holding && t >= to.time)
                    {
                        to.time = t;
                        wp++;
                        holding = false;
                        braking = false;
                        closest = float.MaxValue;
                        segStart = t;
                    }
                    else if (!holding && !diving)
                    {
                        closest = dist;
                        dir = flat / dist;
                        if (wp == p.moves.Count - 1 && !braking && dist < d.normalMaxSpeed * 1.5f)
                            braking = StoppingDistance(d, vel, rot) >= dist;
                        input = braking ? 0f : 1f;
                    }
                    busyUntil = Mathf.Max(busyUntil, t);
                }

                if (!(diving && airborne))
                {
                    vel = StepVelocity(d, vel, rot, diving ? Vector3.zero : dir, diving ? 0f : input);
                    vel.y = 0f;
                }

                if (airborne)
                {
                    airVy = Mathf.Max(airVy + Physics.gravity.y * d.gravityScale * Step, -d.maxGravityVelocity);
                    airY += airVy * Step;
                    if (airY <= 0f) { airY = 0f; airVy = 0f; airborne = false; }
                    busyUntil = Mathf.Max(busyUntil, t);
                }

                pos += vel * Step;

                float groundY;
                if (wp < p.moves.Count)
                {
                    var from = p.moves[wp - 1];
                    var to = p.moves[wp];
                    var full = to.position - from.position;
                    full.y = 0f;
                    var left = to.position - pos;
                    left.y = 0f;
                    groundY = Mathf.Lerp(from.position.y, to.position.y,
                        Mathf.Clamp01(1f - left.magnitude / Mathf.Max(full.magnitude, 0.0001f)));
                }
                else groundY = p.moves[p.moves.Count - 1].position.y;
                pos.y = groundY + airY;

                var heading = new Vector3(vel.x, 0f, vel.z);
                bool turns = wp >= p.moves.Count || p.moves[wp].faceMotion;
                if (turns && !diving && heading.sqrMagnitude > 0.04f) rot = Quaternion.LookRotation(heading.normalized, Vector3.up);

                t += Step;
                p.frames.Add(new ReplayFrame { t = t, pos = pos, rot = rot });
            }

            p.outTime = Mathf.Max(p.outTime, busyUntil + MinZone);
            if (p.frames[p.frames.Count - 1].t < p.outTime)
                p.frames.Add(new ReplayFrame { t = p.outTime, pos = pos, rot = rot });
        }

        public static void BakeAll(ReplayRecording rec)
        {
            if (!rec.isMachinima) return;
            foreach (var p in rec.players) Bake(p);
            float end = 0f;
            foreach (var p in rec.players) end = Mathf.Max(end, p.outTime);
            foreach (var k in rec.keyframes) end = Mathf.Max(end, k.time);
            float was = rec.duration;
            rec.duration = Mathf.Max(was, Mathf.Max(end, MinZone * 2f));
            if (rec.trimEnd <= 0f || rec.trimEnd >= was - 0.001f) rec.trimEnd = rec.duration;
        }

        public static MachinimaAction ActiveAction(ReplayPlayer p, float time, out float local)
        {
            local = 0f;
            MachinimaAction best = null;
            for (int i = 0; i < p.actions.Count; i++)
            {
                var a = p.actions[i];
                if (a.kind == MachinimaActionKind.Phrase) continue;
                if (time < a.time || time > a.time + a.duration) continue;
                if (best != null && a.time <= best.time) continue;
                best = a;
            }
            if (best != null) local = time - best.time;
            return best;
        }
    }
}
#endif
