using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using FG.Common.Character;
using UnityEngine;

namespace BetterFG.Features.CustomizeFallGuys
{
    public static class BeanLookAt
    {
        const float Range = 10f, Cone = 110f, MaxYaw = 75f, MaxPitch = 25f;
        const float RootShare = 0.35f, ChestShare = 0.8f, BodySmooth = 0.35f, HeadSmooth = 0.25f;
        const float DelayMin = 0.1f, DelayMax = 0.25f;
        const float OftenShare = 0.3f, IdleDist = 5f, IdleTime = 5f;

        class Bean
        {
            public GameObject Go;
            public RagdollController Rc;
            public Transform Root, Chest, Head, HipL, HipR;
            public bool Resolved, Often, Glancing;
            public Vector3 Pos, HeadPos, Anchor;
            public float AnchorTime, NextGlance;
            public Bean HeadTarget, Pending;
            public float HeadDelay, PendingSince;
            public float BodyYaw, BodyYawVel, HeadYaw, HeadYawVel, HeadPitch, HeadPitchVel;
            public Quaternion RootSaved, ChestSaved, HeadSaved, HipLSaved, HipRSaved;
            public bool Posed;
        }

        static readonly List<Bean> _beans = new List<Bean>();
        static readonly Dictionary<IntPtr, Bean> _byRagdoll = new Dictionary<IntPtr, Bean>();
        static bool _on, _waitingForPlay;
        static int _frame = -1;
        static Coroutine _restoreLoop;

        public static void OnBeanSpawned(GameObject bean, bool isLocalPlayer)
        {
            Prune();
            _beans.Add(new Bean
            {
                Go = bean,
                Often = !isLocalPlayer && UnityEngine.Random.value < OftenShare,
                AnchorTime = Time.time,
                NextGlance = Time.time + UnityEngine.Random.Range(2f, 20f),
                HeadDelay = UnityEngine.Random.Range(DelayMin, DelayMax),
            });
        }

        internal static void AfterReplayPose(GameObject bean)
        {
            if (!_on) return;
            if (Time.frameCount != _frame) BeginFrame();
            foreach (var b in _beans)
            {
                if (b.Go is null || b.Go.m_CachedPtr != bean.m_CachedPtr) continue;
                if (!b.Posed && Live(b)) Pose(b, 1f);
                return;
            }
        }

        static void Prune()
        {
            for (int i = _beans.Count - 1; i >= 0; i--)
            {
                if (_beans[i].Go != null) continue;
                if (_beans[i].Rc is not null) _byRagdoll.Remove(_beans[i].Rc.m_CachedPtr);
                _beans.RemoveAt(i);
            }
        }

        internal static void SetOn(bool on)
        {
            if (_on == on) return;
            _on = on;
            Utilities.PatchGate.Request(BoneSyncComponent.GateKey, "bean look at", on);
            if (on)
            {
                _restoreLoop = FallGuyEyeDriver.Instance.StartCoroutine(RestoreAfterRender().WrapToIl2Cpp());
                return;
            }
            FallGuyEyeDriver.Instance.StopCoroutine(_restoreLoop);
            Restore();
        }

        public static void OnRoundStart()
        {
            _waitingForPlay = true;
            foreach (var b in _beans) b.Resolved = false;
        }

        public static void OnPlayingStarted()
        {
            _waitingForPlay = false;
            float now = Time.time;
            foreach (var b in _beans)
            {
                b.AnchorTime = now;
                b.Glancing = false;
                b.NextGlance = now + UnityEngine.Random.Range(b.Often ? 2f : 10f, b.Often ? 8f : 40f);
            }
        }

        public static void OnMainMenuEntered()
        {
            _waitingForPlay = false;
            _beans.Clear();
            _byRagdoll.Clear();
        }

        static void Resolve(Bean b)
        {
            b.Resolved = true;
            if (b.Rc is not null) _byRagdoll.Remove(b.Rc.m_CachedPtr);
            b.Rc = b.Go.GetComponentInChildren<RagdollController>(true);
            if (b.Rc != null) _byRagdoll[b.Rc.m_CachedPtr] = b;
            b.Root = b.Chest = b.Head = b.HipL = b.HipR = null;
            foreach (var smr in b.Go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.name != "Body_LOD0") continue;
                foreach (var bone in smr.bones)
                {
                    if (bone == null) continue;
                    switch (bone.name)
                    {
                        case "Chest_C_jnt": b.Chest = bone; break;
                        case "Head_C_jnt01": b.Head = bone; break;
                        case "Hip_L_jnt": b.HipL = bone; break;
                        case "Hip_R_jnt": b.HipR = bone; break;
                    }
                }
                if (b.Chest != null) b.Root = b.Chest.parent;
                return;
            }
        }

        static bool Live(Bean b) => b.Root != null && b.Chest != null && b.Head != null && b.HipL != null && b.HipR != null && b.Go.activeInHierarchy;

        static void BeginFrame()
        {
            _frame = Time.frameCount;
            Prune();
            foreach (var b in _beans)
            {
                if (!b.Resolved) Resolve(b);
                if (!Live(b)) continue;
                b.Pos = b.Go.transform.position;
                b.HeadPos = b.Head.position;
            }
        }

        internal static void AfterRagdoll(RagdollController rc)
        {
            if (!_on) return;
            if (Time.frameCount != _frame) BeginFrame();
            if (!_byRagdoll.TryGetValue(rc.m_CachedPtr, out var b) || b.Posed || !Live(b)) return;
            Pose(b, Mathf.Clamp01(rc.OverallPinStrength));
        }

        static bool WantsToLook(Bean b, float now)
        {
            if (_waitingForPlay) return false;
            if ((b.Pos - b.Anchor).sqrMagnitude > IdleDist * IdleDist)
            {
                b.Anchor = b.Pos;
                b.AnchorTime = now;
            }
            if (now >= b.NextGlance)
            {
                b.Glancing = !b.Glancing;
                b.NextGlance = now + (b.Glancing
                    ? UnityEngine.Random.Range(b.Often ? 2f : 1.5f, b.Often ? 5f : 3f)
                    : UnityEngine.Random.Range(b.Often ? 2f : 20f, b.Often ? 6f : 45f));
            }
            return b.Glancing || now - b.AnchorTime >= IdleTime;
        }

        static void Pose(Bean b, float w)
        {
            float dt = Time.deltaTime, now = Time.time;
            var fwd = Vector3.ProjectOnPlane(b.Go.transform.forward, Vector3.up).normalized;

            Bean near = null;
            if (WantsToLook(b, now))
            {
                float best = Range * Range;
                foreach (var o in _beans)
                {
                    if (o == b || !Live(o)) continue;
                    var to = o.Pos - b.Pos;
                    float d = to.sqrMagnitude;
                    if (d >= best || Vector3.Angle(fwd, Vector3.ProjectOnPlane(to, Vector3.up)) > Cone) continue;
                    best = d;
                    near = o;
                }
            }

            if (near != b.Pending) { b.Pending = near; b.PendingSince = now; }
            if (b.HeadTarget != b.Pending && now - b.PendingSince >= b.HeadDelay)
            {
                b.HeadTarget = b.Pending;
                b.HeadDelay = UnityEngine.Random.Range(DelayMin, DelayMax);
            }
            if (b.HeadTarget != null && !Live(b.HeadTarget)) b.HeadTarget = null;

            float bodyWant = 0f, headWant = 0f, pitchWant = 0f;
            if (near != null)
                bodyWant = Mathf.Clamp(Vector3.SignedAngle(fwd, Vector3.ProjectOnPlane(near.HeadPos - b.HeadPos, Vector3.up), Vector3.up), -MaxYaw, MaxYaw);
            if (b.HeadTarget != null)
            {
                var to = b.HeadTarget.HeadPos - b.HeadPos;
                var flat = Vector3.ProjectOnPlane(to, Vector3.up);
                if (Vector3.Angle(fwd, flat) <= Cone)
                {
                    headWant = Mathf.Clamp(Vector3.SignedAngle(fwd, flat, Vector3.up), -MaxYaw, MaxYaw);
                    pitchWant = Mathf.Clamp(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg, -MaxPitch, MaxPitch);
                }
            }

            b.BodyYaw = Mathf.SmoothDamp(b.BodyYaw, bodyWant, ref b.BodyYawVel, BodySmooth, Mathf.Infinity, dt);
            b.HeadYaw = Mathf.SmoothDamp(b.HeadYaw, headWant, ref b.HeadYawVel, HeadSmooth, Mathf.Infinity, dt);
            b.HeadPitch = Mathf.SmoothDamp(b.HeadPitch, pitchWant, ref b.HeadPitchVel, HeadSmooth, Mathf.Infinity, dt);

            float rootYaw = b.BodyYaw * RootShare * w;
            float chestYaw = b.BodyYaw * ChestShare * w;
            float headYaw = Mathf.Clamp(b.HeadYaw, -MaxYaw, MaxYaw) * w;

            b.RootSaved = b.Root.localRotation;
            b.ChestSaved = b.Chest.localRotation;
            b.HeadSaved = b.Head.localRotation;
            b.HipLSaved = b.HipL.localRotation;
            b.HipRSaved = b.HipR.localRotation;
            b.Posed = true;

            var hipL = b.HipL.rotation;
            var hipR = b.HipR.rotation;
            b.Root.rotation = Quaternion.AngleAxis(rootYaw, Vector3.up) * b.Root.rotation;
            b.HipL.rotation = hipL;
            b.HipR.rotation = hipR;
            b.Chest.rotation = Quaternion.AngleAxis(chestYaw - rootYaw, Vector3.up) * b.Chest.rotation;
            var right = Quaternion.AngleAxis(headYaw, Vector3.up) * Vector3.Cross(Vector3.up, fwd);
            b.Head.rotation = Quaternion.AngleAxis(b.HeadPitch * w, right) * Quaternion.AngleAxis(headYaw - chestYaw, Vector3.up) * b.Head.rotation;
        }

        static void Restore()
        {
            foreach (var b in _beans)
            {
                if (!b.Posed) continue;
                b.Posed = false;
                if (b.Go == null) continue;
                b.Root.localRotation = b.RootSaved;
                b.Chest.localRotation = b.ChestSaved;
                b.Head.localRotation = b.HeadSaved;
                b.HipL.localRotation = b.HipLSaved;
                b.HipR.localRotation = b.HipRSaved;
            }
        }

        static IEnumerator RestoreAfterRender()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                Restore();
            }
        }
    }
}
