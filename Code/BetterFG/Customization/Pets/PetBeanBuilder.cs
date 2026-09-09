using System;
using System.Collections;
using BetterFG.Customization.Player;
using BetterFG.Services;
using BetterFG.Utilities;
using FallGuysIK;
using FGClient;
using FG.Common;
using FG.Common.Character;
using MPGNetObject = FG.Common.MPGNetObject;
using UnityEngine;

namespace BetterFG.Customization.Pets
{
    // shared "spawn a dressed, gameplay-neutral bean off a PetData" build step - used by the live
    // follow pet and the wizard's render-texture preview so the two never drift apart
    internal static class PetBeanBuilder
    {
        public static IEnumerator Build(PetData data, Action<GameObject> onBean, bool forPreview = false, GameObject ownerOverride = null)
        {
            var fgcc = SpawnFallbackBean();
            if (fgcc == null)
            {
                Plugin.Log.LogWarning($"pet '{data.name}': no PB_FallGuyBot around, giving up - preview/pet will stay empty");
                onBean(null);
                yield break;
            }
            // pets are never on anyone's team, in team rounds or otherwise
            fgcc.SetTeamID(-1);
            Plugin.Log.LogInfo($"pet '{data.name}': bean spawned ({fgcc.gameObject.name}, preview={forPreview}, team={fgcc.TeamID})");
            if (!forPreview) PetService.Instance?.RegisterLiveFgcc(fgcc);

            var bean = fgcc.gameObject;
            bean.name = "BettrFG_Pet_" + data.name;

            if (forPreview)
            {
                fgcc.enabled = false;
                foreach (var rb in bean.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
                foreach (var col in bean.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(col);
            }

            ApplyLookManually(data, fgcc);
            ForceNonTeamLook(data, fgcc);

            // the cloned bean lands wherever it was instantiated, not near you - snap it to
            // PetFollowComponent's own resting spot rather than dead-center on the owner, so its
            // collider never starts out overlapping yours (that overlap is what was shoving you on
            // spawn/respawn before this).
            var owner = ownerOverride != null ? ownerOverride : BeanMonitorService.LocalPlayerBean;
            if (owner != null)
            {
                var ownerTf = owner.transform;
                Vector3 restSpot = ownerTf.position - ownerTf.forward * 1.6f + ownerTf.right * 0.7f; // PetFollowComponent.FollowDistance/SideOffset
                bean.transform.SetPositionAndRotation(restSpot, ownerTf.rotation);
            }

            // fgcc stays ALIVE - Unity's own Rigidbody/Collider physics holds this bean up against
            // real level geometry just fine on its own (confirmed), and destroying fgcc entirely (an
            // earlier attempt) turned out to cost the animator/skin pipeline too. only the input side
            // gets cut, same shape chaosmod's own NPC spawn uses: no local/network input reaches this
            // bean, so it can't respond to your actual controller. PetFollowComponent drives it with
            // plain Rigidbody force (FallGuysLib10dot8's NPCBehaviour/Companion shape) rather than the
            // real MotorFunctionMovement/MotorAgent system - that needs proper resource ownership and
            // update-manager registration a synthetic bean doesn't reliably get.
            var input = bean.GetComponentInChildren<FallGuysCharacterControllerInput>(true);
            if (input != null) UnityEngine.Object.Destroy(input);
            fgcc.IsLocalPlayer = false;
            fgcc.IsControlledLocally = false;
            fgcc.IsRemoteCharacterOnClient = true;

            // GrabController is what actually lets a grabbing player's hand attach to this bean -
            // destroy it outright instead of relying on the invulnerability-window flag, which
            // stopped reliably holding once this stopped being a locally-ticked character
            var grabController = bean.GetComponentInChildren<GrabController>(true);
            if (grabController != null) UnityEngine.Object.Destroy(grabController);
            // the fuller set chaosmod's own proven NPC spawn clears too - nothing about this bean
            // should look like a real networked local-player connection to anything that goes
            // looking for one
            fgcc._netMotorAgentState = null;
            fgcc._netMotorTasks = null;
            fgcc.ConnectionToServer = null;

            // without this the scaled-down upper body still ragdoll-simulates at the original
            // mass/scale and flails - same fix the qual-time ghost already needed
            if (fgcc._ragdollController != null)
                fgcc._ragdollController._upperBodyEnabled = false;

            var loader = CustomizationServices.LoaderService;
            var app = CustomizationServices.ApplicationService;
            if (loader != null && app != null)
                foreach (var entry in data.skins)
                {
                    if (SkinTypeParser.FromString(entry.type) == SkinType.Emote) continue;
                    ActiveSkinSlot slot = null;
                    yield return loader.ResolveProfileSlot(entry, new Action<ActiveSkinSlot>(s => slot = s));
                    if (slot == null || bean == null) continue;
                    yield return app.ApplySkinToBean(slot, bean);
                }

            if (data.skinTexEntries != null)
                foreach (var entry in data.skinTexEntries)
                    if (entry.enabled) SkinApplicationService.ApplyEntryToBean(entry, bean);

            // plain transform scale - it's the one thing that reliably keeps the costume (attached
            // separately via ApplySkinToBean above) scaled together with the body, since everything
            // is parented under the bean and inherits its scale. PlayerScaleService's non-destructive
            // wrapper (tried here previously) fixed the IK torso-stretch but broke that: a costume
            // applied through ApplySkinToBean isn't something ScaleAppliedSkins picks up, so it was
            // left at scale 1 while the body scaled around it. kill the IK controller directly
            // instead - that's what actually fights a scaled root once the real motor is live.
            var character = bean.transform.Find("Character");
            var ik = character != null ? character.GetComponent<FallGuyIkController>() : null;
            if (ik != null) ik.enabled = false;
            bean.transform.localScale = new Vector3(data.scale, data.scale, data.scale);

            // give the pet the local player's custom eyes/face, same as every other bean the mod
            // dresses (ReplayViewer.DressEyes does the identical Apply + two delayed re-applies)
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.Apply(bean, true);
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.ApplyLater(bean, 0.5f, true);
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.ApplyLater(bean, 1.5f, true);

            // the bean's animator comes up wedged in the spawn pose; bouncing the Animator's enabled
            // flag a frame later kicks its state machine back to life. done on the Animator component
            // only - deactivating the whole Character GameObject (an earlier attempt) permanently
            // wrecks the ragdoll's and fgcc's cached bone transforms, so UpdateCharacterRotation /
            // CalculateFloorRaycastOrigin then NRE every pumped frame.
            var anim = character != null ? character.GetComponent<Animator>() : null;
            if (anim != null)
            {
                anim.enabled = false;
                yield return null;
                if (bean == null) yield break;
                anim.enabled = true;
            }

            onBean(bean);
        }

        // PB_FallGuyBot is a real AI bot prefab (has its own BehaviorTree, GrabController, etc), kept
        // loaded (inactive) the whole session - find it by name among every loaded
        // FallGuysCharacterController (same Resources.FindObjectsOfTypeAll approach ReplayViewer's
        // LoadBeanPrefab already uses to find a character prefab outside a round) and clone it.
        static FallGuysCharacterController SpawnFallbackBean()
        {
            var template = GameObjectHelper.FindDefaultBotBean();
            if (template == null) return null;

            var clone = UnityEngine.Object.Instantiate(template);
            clone.SetActive(true);

            // the bot prefab carries an MPGNetObject component, and Instantiate copies it - the clone
            // then looks "network-aware" to the networking layer despite never being registered
            // (NetID 0), which both spams "destroyed while still network-aware!" and leaves a bogus
            // entry for the unspawn path to walk. this clone is purely client-side, strip it.
            foreach (var n in clone.GetComponentsInChildren<MPGNetObject>(true))
                if (n != null) UnityEngine.Object.DestroyImmediate(n);

            // this bean brought its own bot AI along - shut every Behaviour off it down so nothing
            // fights PetFollowComponent's manual movement or the preview's static parked pose
            foreach (var b in clone.GetComponents<Behaviour>())
                if (b != null && b.GetType().Name == "BehaviorTree") b.enabled = false;

            return clone.GetComponent<FallGuysCharacterController>();
        }

        // the cloned bot bean comes with its own look, not the pet's - push the pet's
        // top/bottom/pattern/faceplate through the handler by hand
        static void ApplyLookManually(PetData data, FallGuysCharacterController fgcc)
        {
            var fch = fgcc.GetComponent<FallguyCustomisationHandler>();
            if (fch == null) return;
            fch.EnsureIsInitialized();

            if (!string.IsNullOrEmpty(data.costumeTop))
            {
                CostumeOption opt = null;
                try { opt = SkinApplicationService.FindOptionByName(SkinTexCategory.Upper, data.costumeTop)?.TryCast<CostumeOption>(); } catch { }
                if (opt != null) fch.UpdateCostumeOption(opt, false);
            }
            if (!string.IsNullOrEmpty(data.costumeBottom))
            {
                CostumeOption opt = null;
                try { opt = SkinApplicationService.FindOptionByName(SkinTexCategory.Lower, data.costumeBottom)?.TryCast<CostumeOption>(); } catch { }
                if (opt != null) fch.UpdateCostumeOption(opt, false);
            }
            if (!string.IsNullOrEmpty(data.pattern))
            {
                SkinPatternOption opt = null;
                try { opt = SkinApplicationService.FindOptionByName(SkinTexCategory.Pattern, data.pattern)?.TryCast<SkinPatternOption>(); } catch { }
                if (opt != null) fch.UpdatePatternTexture(opt);
            }
            if (!string.IsNullOrEmpty(data.faceplate))
            {
                FaceplateOption opt = null;
                try { opt = SkinApplicationService.FindOptionByName(SkinTexCategory.Faceplate, data.faceplate)?.TryCast<FaceplateOption>(); } catch { }
                if (opt != null) fch.UpdateFaceplateColours(opt);
            }
        }

        // A teamless bean spawned during a team round gets its costume built on the uncoloured
        // "team textures" variant, and the local player's live ColourOption in a team round is
        // their team colour, which paints nothing without a team - so the pet comes out white.
        // Rebuild both costume halves as non-team and push a real (non-team) ColourOption.
        static void ForceNonTeamLook(PetData data, FallGuysCharacterController fgcc)
        {
            var fch = fgcc.GetComponent<FallguyCustomisationHandler>();
            if (fch == null) return;
            fch.EnsureIsInitialized();

            CostumeOption top = null, bot = null;
            try { top = SkinApplicationService.FindOptionByName(SkinTexCategory.Upper, data.costumeTop)?.TryCast<CostumeOption>(); } catch { }
            try { bot = SkinApplicationService.FindOptionByName(SkinTexCategory.Lower, data.costumeBottom)?.TryCast<CostumeOption>(); } catch { }
            try { if (top != null) fch.UpdateCostumeOption(top, false); } catch { }
            try { if (bot != null) fch.UpdateCostumeOption(bot, false); } catch { }

            ColourOption col = null;
            if (!string.IsNullOrEmpty(data.colour))
                try { col = SkinApplicationService.FindOptionByName(SkinTexCategory.Colour, data.colour)?.TryCast<ColourOption>(); } catch { }
            if (col != null) { try { fch.UpdateColourOption(col); } catch { } }
        }

    }
}
