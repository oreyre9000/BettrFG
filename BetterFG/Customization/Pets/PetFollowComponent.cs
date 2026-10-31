using System;
using System.Collections.Generic;
using BetterFG.Services;
using FG.Common.Character;
using UnityEngine;

namespace BetterFG.Customization.Pets
{
    // drives a live pet bean around with plain Rigidbody force + manual drag - the same shape
    // FallGuysLib10dot8's own NPCBehaviour/Companion class uses (a proven reference for this exact
    // problem). the real MotorFunctionMovement/MotorAgent resource-arbitration system turned out not
    // to be worth fighting for a synthetic SpawnBeanUtils bean - AddForce+clamp+drag sidesteps all of
    // that. fgcc stays alive; Unity's own Rigidbody/Collider physics already holds this bean up
    // against real level geometry on its own (confirmed working), so grounding needs nothing extra
    // here - only movement/rotation are ours.
    //
    // jumping DOES route through the game's own system: a gap or obstacle ahead fires MotorTaskJump
    // so MotorFunctionJump does the real liftoff (anim + sound). grounded / floor state is read off
    // the fgcc's own IsTouchingGround / GroundMask, not a hand-rolled raycast.
    public class PetFollowComponent : MonoBehaviour
    {
        public PetFollowComponent(IntPtr ptr) : base(ptr) { }

        // which formation slot this pet holds - PetService sets it right after AddComponent. keeps
        // multiple pets from all walking the exact same line behind you.
        public int SlotIndex;

        // set by RemotePetService for another player's pet - follows this bean instead of
        // BeanMonitorService.LocalPlayerBean. null for your own pets.
        public GameObject OwnerOverride;

        const float FollowDistance = 1.6f;
        const float SideOffset = 0.7f;
        const float ArriveRadius = 0.3f;
        const float WalkForce = 1800f;
        const float MaxSpeed = 6f;
        const float RotationSpeed = 15f;
        const float HorizontalDrag = 4f;
        const float VerticalDrag = 0.1f;
        const float TargetSmoothSpeed = 6f;
        const float CollisionScanInterval = 0.25f;
        const float BeanPairInterval = 5f;
        const float DynamicIgnoreRadius = 4f; // loose props within this get paired off before the pet can shove them
        const float TeleportBackDistance = 15f;
        const float TeleportBackCheckInterval = 2f;

        const float JumpCooldown = 0.35f;
        const float GapProbeAhead = 1.4f;  // how far in front of the pet to look for a floor - jump before the edge, not on it
        const float GapProbeDrop = 1.6f;   // a floor deeper than this counts as "no floor ahead"
        const float GravityScale = 0.5f;   // small light bean - full gravity kills the jump arc and drops it like a rock
        const float OwnerMoveThreshold = 1.3f; // owner speed (u/s) below this = "barely moving", pet holds its bearing instead of chasing behind
        const float IdleOuterRadius = 2.6f;    // ...only ambles back toward you once it drifts past this
        const float IdleInnerRadius = 1.8f;
        const float IdleActionMin = 2.5f;  // gap between idle antics
        const float IdleActionMax = 6f;
        const float GrabIntervalMin = 0.7f; // while facing you: gap between grabs
        const float GrabIntervalMax = 3f;
        const float GrabHoldMin = 0.12f;    // ...and how long each grab is held
        const float GrabHoldMax = 0.9f;

        Rigidbody _rb;
        FallGuysCharacterController _fgcc;
        MotorFunctionBeingGrabbed _beingGrabbed;
        MotorFunctionJump _jumpFn;
        MotorTaskJump _jumpTask;
        MotorTaskGrab _grabTask;
        RagdollController _ragdoll;
        float _collisionScanTimer;
        float _beanPairTimer;
        float _teleportBackTimer;
        float _jumpTimer;
        float _idleTimer;
        float _faceOwnerUntil;
        float _grabReleaseAt;
        float _grabNextAt;
        Vector3 _smoothedTarget;
        Vector3 _lastOwnerPos;
        float _ownerSpeed;
        bool _targetInit;
        Collider[] _ownColliders;
        readonly Dictionary<IntPtr, bool> _paired = new Dictionary<IntPtr, bool>();

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null) { _rb.drag = 0f; _rb.angularDrag = 5f; } // drag replaced by ApplyDrag below, same as the reference

            // GetMotorFunction<T> throws KeyNotFoundException instead of returning null when this
            // bean's MotorAgentConfiguration doesn't carry that function - synthetic/fallback beans
            // don't always get the same function set as a real networked spawn, so this is a normal
            // "not present" case here, not a bug to fix upstream
            _fgcc = GetComponent<FallGuysCharacterController>();
            var agent = _fgcc != null ? _fgcc.MotorAgent : null;
            try { _beingGrabbed = agent != null ? agent.GetMotorFunction<MotorFunctionBeingGrabbed>() : null; }
            catch { _beingGrabbed = null; }

            // jump goes through the motor system so it carries the real liftoff anim + footstep/jump
            // sound, not a bare Rigidbody nudge. MotorTaskJump.isRequested is the same flag the input
            // layer sets for the local player; MotorFunctionJump consumes it on its next tick.
            try { _jumpFn = agent != null ? agent.GetMotorFunction<MotorFunctionJump>() : null; }
            catch { _jumpFn = null; }
            try { _jumpTask = agent != null ? agent.MotorTasks?.GetTask<MotorTaskJump>() : null; }
            catch { _jumpTask = null; }

            // grab-at-air idle antic - same isRequested flag, no target set (the local player grabs
            // nothing all the time when the button's pressed with no target in range)
            try { _grabTask = agent != null ? agent.MotorTasks?.GetTask<MotorTaskGrab>() : null; }
            catch { _grabTask = null; }

            // PetBeanBuilder disables the upper-body ragdoll at spawn, but on a quick re-spawn cycle
            // (toggling the pet off/on a few times) something flips it back on and it stays on. keep
            // it pinned off from here - the pet is gameplay-neutral and should never ragdoll.
            _ragdoll = _fgcc != null ? _fgcc._ragdollController : null;

            // this bean's own collider set is fixed at spawn - no costume/pattern change adds one -
            // so fetch it once instead of every scan
            _ownColliders = GetComponentsInChildren<Collider>(true);

            // the pet spawns right next to the owner (PetBeanBuilder) - do this synchronously before
            // any physics step can run, not on Update()'s throttled scan, or the initial near-overlap
            // can shove the (real, dynamic-rigidbody) player before the ignore-pairing exists
            IgnoreCollisions(pairAgainstOtherPets: true);

            Plugin.Log.LogInfo($"pet follow: rb={_rb != null} beingGrabbed={_beingGrabbed != null} jumpFn={_jumpFn != null} jumpTask={_jumpTask != null} grabTask={_grabTask != null}");
        }

        // where a pet in the given formation slot sits behind the owner: alternate sides, each pair
        // steps further out and a little deeper so no two pets share a lane
        public static Vector3 FormationSpot(Transform ownerTf, int slot)
        {
            int pair = slot / 2;
            float side = (slot % 2 == 0 ? 1f : -1f) * (SideOffset + 0.75f * pair);
            float back = FollowDistance + 0.45f * slot;
            return ownerTf.position - ownerTf.forward * back + ownerTf.right * side;
        }

        void FixedUpdate()
        {
            if (_rb == null) return;
            var owner = OwnerOverride != null ? OwnerOverride : BeanMonitorService.LocalPlayerBean;
            if (owner == null) return;

            // held frozen through the round-start turbulence (see PetService.FrozenForRoundStart) -
            // the game flings synthetic beans around while it reconciles the drop-in
            if (PetService.Instance != null && PetService.Instance.FrozenForRoundStart)
            {
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                return;
            }

            ApplyDrag();
            ReleaseGrabIfDue();

            // shave part of gravity back off - mass-independent so it's a clean fraction of g
            _rb.AddForce(-Physics.gravity * (1f - GravityScale), ForceMode.Acceleration);

            var ownerTf = owner.transform;
            float dt = Time.fixedDeltaTime;
            Vector3 opos = ownerTf.position;
            if (!_targetInit) _lastOwnerPos = opos;
            // owner's travel speed (position only - turning the camera doesn't count)
            _ownerSpeed = Mathf.Lerp(_ownerSpeed, Vector3.Distance(opos, _lastOwnerPos) / Mathf.Max(dt, 1e-4f), dt * 8f);
            _lastOwnerPos = opos;

            Vector3 rawTarget;
            if (_ownerSpeed > OwnerMoveThreshold)
                rawTarget = FormationSpot(ownerTf, SlotIndex); // fanned out behind you, one lane per slot
            else
            {
                // barely moving / just turning - the behind-and-side spot rotates with your facing,
                // which is why the pet kept swinging around behind you. instead hold whatever bearing
                // it already has and only close the gap if it's drifted too far out.
                Vector3 fromOwner = transform.position - opos; fromOwner.y = 0f;
                float d = fromOwner.magnitude;
                rawTarget = d > IdleOuterRadius ? opos + fromOwner / d * IdleInnerRadius : transform.position;
            }

            if (!_targetInit) { _smoothedTarget = rawTarget; _targetInit = true; }
            _smoothedTarget = Vector3.Lerp(_smoothedTarget, rawTarget, dt * TargetSmoothSpeed);

            // same teleport-back safety net NPCBehaviour has - a fall off a ledge, a shortcut through
            // a portal/checkpoint, anything that puts real distance between the pet and you shouldn't
            // leave it stranded trying to walk back across the whole level
            _teleportBackTimer += Time.fixedDeltaTime;
            if (_teleportBackTimer >= TeleportBackCheckInterval)
            {
                _teleportBackTimer = 0f;
                if (Vector3.Distance(transform.position, ownerTf.position) >= TeleportBackDistance)
                {
                    transform.SetPositionAndRotation(rawTarget, ownerTf.rotation);
                    _smoothedTarget = rawTarget;
                    _rb.velocity = Vector3.zero;
                }
            }

            Vector3 toTarget = _smoothedTarget - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            if (dist <= ArriveRadius) { IdleAntics(ownerTf); return; }

            Vector3 dir = toTarget / dist;
            FaceTowards(dir);

            MaybeJump(dir);

            // match the owner's pace - and a bit over so it can actually close the gap when it's fallen
            // behind, not just tail at the exact same speed forever. floors at MaxSpeed for repositioning.
            float cap = Mathf.Max(MaxSpeed, _ownerSpeed * 1.35f);
            float accel = WalkForce * (cap / MaxSpeed);

            Vector3 horizontalVel = new Vector3(_rb.velocity.x, 0f, _rb.velocity.z);
            if (horizontalVel.magnitude < cap)
                _rb.AddForce(dir * accel, ForceMode.Force);

            horizontalVel = new Vector3(_rb.velocity.x, 0f, _rb.velocity.z);
            if (horizontalVel.magnitude > cap)
            {
                horizontalVel = horizontalVel.normalized * cap;
                _rb.velocity = new Vector3(horizontalVel.x, _rb.velocity.y, horizontalVel.z);
            }
        }

        // ask the motor system to jump when the floor drops away just ahead (a ledge/gap) or something
        // is blocking the path in front. detection reads the fgcc's own GroundMask so it only sees real
        // level geometry; the jump itself is MotorTaskJump -> MotorFunctionJump (real liftoff + sound),
        // never a Rigidbody nudge. grounded gate + cooldown keep it from firing mid-air or spamming.
        void MaybeJump(Vector3 dir)
        {
            _jumpTimer -= Time.fixedDeltaTime;
            if (_jumpTimer > 0f || _jumpTask == null || _fgcc == null) return;
            if (!_fgcc.IsTouchingGround) return;

            int mask = FallGuysCharacterController.GroundMaskSet && FallGuysCharacterController.GroundMask.value != 0
                ? FallGuysCharacterController.GroundMask.value : Physics.DefaultRaycastLayers;
            Vector3 chest = transform.position + Vector3.up * 0.5f;

            bool gapAhead = !Physics.Raycast(chest + dir * GapProbeAhead, Vector3.down, GapProbeDrop, mask, QueryTriggerInteraction.Ignore);
            bool blockedAhead = Physics.Raycast(chest, dir, GapProbeAhead, mask, QueryTriggerInteraction.Ignore);
            if (!gapAhead && !blockedAhead) return;

            bool canJump = true;
            try { if (_jumpFn != null) canJump = _jumpFn.CanJump(); } catch { }
            if (!canJump) return;

            try { _jumpTask.isRequested = true; } catch (Exception ex) { Plugin.Log.LogWarning($"pet jump: task request threw {ex.Message}"); return; }
            _jumpTimer = JumpCooldown;
            Plugin.Log.LogInfo($"pet jump: {(gapAhead ? "gap" : "obstacle")} ahead, requested MotorTaskJump");
        }

        // the controller only rotates the bean toward its own DesiredRotation - setting transform
        // .rotation by hand just gets fought back (the shake). feed the game's own target instead.
        void FaceTowards(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            if (_fgcc != null) { try { _fgcc.SetDesiredRotation(rot); return; } catch { } }
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.fixedDeltaTime * RotationSpeed);
        }

        // once the pet's parked next to you it just stands there - give it something to do: now and
        // then turn to look at you for a couple seconds, or grab at the air. both are pulled straight
        // off the motor system, no bespoke animation.
        void IdleAntics(Transform ownerTf)
        {
            // while it's turned to look at you, grab at the air at random intervals for random holds
            if (_faceOwnerUntil > Time.time)
            {
                FaceTowards(ownerTf.position - transform.position);
                if (_grabTask != null && _grabReleaseAt <= 0f && Time.time >= _grabNextAt)
                {
                    try { _grabTask.isRequested = true; } catch { }
                    _grabReleaseAt = Time.time + UnityEngine.Random.Range(GrabHoldMin, GrabHoldMax);
                    _grabNextAt = _grabReleaseAt + UnityEngine.Random.Range(GrabIntervalMin, GrabIntervalMax);
                }
            }

            _idleTimer -= Time.fixedDeltaTime;
            if (_idleTimer > 0f) return;
            _idleTimer = UnityEngine.Random.Range(IdleActionMin, IdleActionMax);

            // sometimes turn to look at you for a bit (grabs ride along inside that window); other
            // times just keep standing
            if (UnityEngine.Random.value < 0.6f)
            {
                _faceOwnerUntil = Time.time + UnityEngine.Random.Range(2.5f, 5f);
                _grabNextAt = Time.time + UnityEngine.Random.Range(GrabIntervalMin, GrabIntervalMax);
            }
        }

        void ReleaseGrabIfDue()
        {
            if (_grabReleaseAt <= 0f || Time.time < _grabReleaseAt) return;
            _grabReleaseAt = 0f;
            if (_grabTask != null) try { _grabTask.isRequested = false; } catch { }
        }

        // manual per-axis drag (matches NPCBehaviour.ApplyCustomDrag) - this is the piece that was
        // actually missing before: nothing was damping velocity between pushes, so any repeated force
        // (e.g. against a wall) had nothing bleeding it off and could run away
        void ApplyDrag()
        {
            Vector3 v = _rb.velocity;
            float hf = 1f / (1f + HorizontalDrag * Time.fixedDeltaTime);
            v.x *= hf; v.z *= hf;
            float vf = 1f / (1f + VerticalDrag * Time.fixedDeltaTime);
            v.y *= vf;
            _rb.velocity = v;
        }

        void Update()
        {
            // the same brief-invulnerability window the game itself uses (e.g. right after
            // elimination) so a bean can't be grabbed - kept perpetually topped up instead of
            // patching the grabber's own CanBeGrabbed/CheckForGrabTarget methods
            _beingGrabbed?.StartInvulnerablityWindow();

            if (_ragdoll != null && _ragdoll.m_CachedPtr != IntPtr.Zero && _ragdoll._upperBodyEnabled)
                _ragdoll._upperBodyEnabled = false;

            _collisionScanTimer -= Time.deltaTime;
            if (_collisionScanTimer > 0f) return;
            _collisionScanTimer = CollisionScanInterval;

            _beanPairTimer -= CollisionScanInterval;
            bool pairBeans = _beanPairTimer <= 0f;
            if (pairBeans) _beanPairTimer = BeanPairInterval;

            IgnoreCollisions(pairAgainstOtherPets: false, pairAgainstAllBeans: pairBeans);
        }

        static readonly Collider[] _overlapScratch = new Collider[32];

        // sweeps every OTHER live player bean and excludes collision against this pet. re-fetches
        // the other side's colliders every pass instead of caching once - a remote-flagged bean
        // (IsControlledLocally=false) appears to have its collider/rigidbody setup touched by the
        // game's own remote-character path, which left stale cached colliders paired against
        // colliders that no longer existed while the real, current ones went unpaired.
        //
        // was Resources.FindObjectsOfTypeAll<FallGuysCharacterController>() - a full-scene type
        // scan every 0.25s was the actual FPS hit players felt while the pet followed them around.
        // BeanMonitorService already tracks every live player bean incrementally for other
        // features, so reuse that list instead of re-scanning the whole scene ourselves.
        //
        // pairAgainstOtherPets only runs from Awake: Physics.IgnoreCollision is symmetric and a
        // pet's own collider set never changes after spawn (we own it, nothing external touches
        // it), so a pair only needs setting up ONCE, by whichever pet spawns second. Redoing every
        // pet-vs-every-other-pet pairing on every periodic scan was pure O(petCount^2) waste for
        // nothing - the pairs were already permanent.
        void IgnoreCollisions(bool pairAgainstOtherPets, bool pairAgainstAllBeans = true)
        {
            if (_ownColliders == null || _ownColliders.Length == 0) return;

            void IgnoreAgainst(GameObject other)
            {
                if (other == null || other == gameObject) return;
                foreach (var oc in other.GetComponentsInChildren<Collider>(true))
                {
                    if (oc is null || oc.m_CachedPtr == IntPtr.Zero) continue;
                    bool live = oc.enabled && oc.gameObject.activeInHierarchy;
                    bool known = _paired.TryGetValue(oc.m_CachedPtr, out bool wasLive);
                    _paired[oc.m_CachedPtr] = live;
                    if (known && (wasLive || !live)) continue;
                    foreach (var pc in _ownColliders)
                        if (pc != null) Physics.IgnoreCollision(pc, oc, true);
                }
            }

            IgnoreAgainst(OwnerOverride != null ? OwnerOverride : BeanMonitorService.LocalPlayerBean);
            if (pairAgainstAllBeans)
                foreach (var bean in BeanMonitorService.GetTrackedBeans())
                    IgnoreAgainst(bean);
            // and every other pet - otherwise a pack of them shoves each other around
            if (pairAgainstOtherPets && PetService.Instance != null)
                foreach (var petGo in PetService.Instance.LivePetObjects)
                    IgnoreAgainst(petGo);

            // any loose prop nearby - grabbables, dodgeballs, anything with a live (non-kinematic)
            // rigidbody, plus level-editor destructibles (those take a collision hit and can shift
            // even while kinematic) - gets knocked across the screen when the pet brushes it. pair
            // it off before contact. a small overlap check, not the scene-wide rigidbody scan that
            // used to be the fps hit. NonAlloc + a shared static buffer - this ran every 0.25s per
            // pet and was handing back a fresh array (GC garbage) every single time.
            int count = Physics.OverlapSphereNonAlloc(transform.position, DynamicIgnoreRadius, _overlapScratch, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var oc = _overlapScratch[i];
                if (oc == null) continue;
                var body = oc.attachedRigidbody;
                bool liveBody = body != null && !body.isKinematic && body.gameObject != gameObject;

                // GetComponentInParent<Responder>() != null matched every collider under the same
                // hierarchy - static level geometry parented alongside a destructible piece - so the
                // pet fell straight through the floor. Only the responder's own ActiveCollider is
                // the actual destructible piece, and only while destruction is actually enabled on it.
                var responder = oc.GetComponentInParent<LevelEditorDestructibleObjectResponder>();
                bool destructible = responder != null && responder.IsDestructionEnabled && oc == responder.ActiveCollider;
                if (!liveBody && !destructible) continue;
                foreach (var pc in _ownColliders)
                    if (pc != null) Physics.IgnoreCollision(pc, oc, true);
            }
        }
    }
}
