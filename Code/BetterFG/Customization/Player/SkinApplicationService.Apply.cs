using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Utilities;
using BetterFG.Services;
using BetterFG.Core;
using FGClient;
using FG.Common;
using FG.Common.CMS;
using BetterFG.Customization.UI;
using BetterFG.Network;
using BetterFG.UI.Tabs;
using Il2CppInterop.Runtime;

namespace BetterFG.Customization.Player
{
    public partial class SkinApplicationService
    {
        // ── Costume ───────────────────────────────────────────────────────────

        private IEnumerator ApplyCostumeCoroutine(ActiveSkinSlot slot, GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            string pendingKey = MakeKey(bean, slot.skinInfo.file);
            if (slot.bundle == null) { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }

            Transform beanGEO = FindBeanGEO(bean);
            if (beanGEO == null) { Plugin.Log.LogError($"no GEO on {bean.name}"); pendingKeys.Remove(pendingKey); yield break; }

            var protectedClones = new HashSet<GameObject>();
            foreach (var kvp in appliedSkins)
                if (kvp.Key.StartsWith(bean.GetInstanceID() + "|") && kvp.Value?.instance != null)
                    protectedClones.Add(kvp.Value.instance);

            var disabled = new List<GameObject>();
            bool isRemote = IsRemoteInRoundBean(bean);
            bool keepLocalDonor = false;
            if (!slot.skinInfo.keepBase)
            {
                for (int i = 0; i < beanGEO.childCount; i++)
                {
                    Transform child = beanGEO.GetChild(i)?.gameObject?.transform;
                    if (child == null) continue;
                    if (protectedClones.Contains(child.gameObject)) continue;

                    bool keepLive = isRemote && child.name.Contains("Body_LOD");
                    if (!keepLive && !child.gameObject.activeSelf) continue;

                    if (!keepLive)
                    {
                        child.gameObject.SetActive(false);
                        disabled.Add(child.gameObject);
                        continue;
                    }

                    var renderers = child.GetComponentsInChildren<Renderer>(true);
                    if (renderers == null || renderers.Length == 0) continue;

                    var matMap = new Dictionary<Renderer, Material[]>();
                    Material invis = CostumePollerComponent.GetInvisibleMat();
                    foreach (var r in renderers)
                    {
                        if (r == null) continue;
                        matMap[r] = r.materials;
                        var newMats = new Material[r.materials.Length];
                        for (int j = 0; j < newMats.Length; j++) newMats[j] = invis;
                        r.materials = newMats;
                    }
                    hiddenRendererMaterials[pendingKey + "_" + i] = matMap;
                }

                var fgch = bean.GetComponent<FallguyCustomisationHandler>();
                if (fgch != null)
                {
                    var topObj = fgch._currentTopCostume != null ? fgch._currentTopCostume.CostumeObject : null;
                    var bottomObj = fgch._currentBottomCostume != null ? fgch._currentBottomCostume.CostumeObject : null;
                    if (topObj != null && topObj.activeSelf && !protectedClones.Contains(topObj) && !disabled.Contains(topObj))
                    { topObj.SetActive(false); disabled.Add(topObj); }
                    if (bottomObj != null && bottomObj.activeSelf && !protectedClones.Contains(bottomObj) && !disabled.Contains(bottomObj))
                    { bottomObj.SetActive(false); disabled.Add(bottomObj); }
                }
            }

            bool isPet = BetterFG.Customization.Pets.PetService.IsPetFgcc(bean.GetComponent<FallGuysCharacterController>());
            if (!isPet && isRemote)
            {
                float bakedRemote = (slot.type == SkinType.Costume && slot.skinInfo.skinScale > 0f) ? slot.skinInfo.skinScale : 1f;
                PlayerScaleService.ApplyToBean(bean, bakedRemote, PlayerScaleService.BeanScaleMode.Remote);
            }
            else if (!isPet)
                PlayerScaleService.ApplyLocalCostumeScale(bean, slot.type == SkinType.Costume ? slot.skinInfo : null);
            if (reason == ApplyReason.FromMenu)
                AssetManager.SpawnPoof(bean.transform.position + Vector3.up);
            AssetBundleRequest req = slot.bundle.LoadAssetAsync(slot.skinInfo.file);
            yield return req;

            try { _ = bean.name; }
            catch
            {
                pendingKeys.Remove(pendingKey);
                foreach (var go in disabled) { try { go.SetActive(true); } catch { } }
                yield break;
            }

            if (SlotDead(slot))
            {
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) });
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            if (req.asset == null)
            {
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) });
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            GameObject prefab = null;
            GameObject clone = null;
            Exception applyEx = null;
            try
            {
                    prefab = req.asset.Cast<GameObject>();
                    clone = GameObject.Instantiate(prefab, beanGEO.position, beanGEO.rotation);
                    int stripped = GameObjectHelper.StripPhysics(clone);
                    if (stripped > 0) Plugin.Log.LogInfo($"costume '{slot.skinInfo.file}' shipped {stripped} physics components, gone");
            }
            catch (Exception ex) { applyEx = ex; }

            if (applyEx != null || clone == null)
            {
                Plugin.Log.LogError($"costume instantiate failed: {applyEx?.Message}");
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) });
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            clone.transform.SetParent(beanGEO, true);
            clone.transform.localScale = Vector3.one;

            var hiddenUntilBound = clone.GetComponentsInChildren<Renderer>(true);
            foreach (var r in hiddenUntilBound) if (r != null) r.enabled = false;

            if (GameObjectHelper.IsLobbyCharacter(bean)) clone.transform.localScale = Vector3.one;
            if (GameObjectHelper.IsUICharacter(bean) || GameObjectHelper.IsLobbyCharacter(bean))
                GameObjectHelper.SetLayerRecursively(clone, LayerMask.NameToLayer("PlayerUI"));
            yield return SetupBoneSyncCoroutine(clone, bean).WrapToIl2Cpp();

            if (SlotDead(slot))
            {
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) });
                if (clone != null) GameObject.Destroy(clone);
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            BindSkinToLocalFallguy(clone, bean, "Costume");
            foreach (var r in hiddenUntilBound) if (r != null) r.enabled = true;
            SetRenderQueue(clone, 3000);

            if (!slot.skinInfo.keepBase)
            {
                var poller = clone.AddComponent<CostumePollerComponent>();
                poller.beanGEO = beanGEO;
                poller.skinClone = clone;
                poller.isRemote = isRemote;
                poller.keepLocalDonor = keepLocalDonor;
                poller.beanId = bean.GetInstanceID();
                poller.HideNow();
            }

            KillExistingAppliedAtKey(pendingKey);
            appliedSkins[pendingKey] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Costume, disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) };
            pendingKeys.Remove(pendingKey);
            Plugin.Log.LogInfo($"done '{slot.skinInfo.name}' -> {bean.name}");
            RaiseSkinApplied(slot.skinInfo, bean, reason);
            for (int f = 0; f < 5; f++) yield return null;
        }

        // ── Accessory ─────────────────────────────────────────────────────────

        private IEnumerator ApplyAccessoryCoroutine(ActiveSkinSlot slot, GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            string pendingKey = MakeKey(bean, slot.skinInfo.file);
            if (slot.bundle == null) { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }
            AssetBundleRequest req = slot.bundle.LoadAssetAsync(slot.skinInfo.file);
            yield return req;

            try { _ = bean.name; } catch { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }

            if (req.asset == null) { Plugin.Log.LogError($"asset null for '{slot.skinInfo.file}'"); pendingKeys.Remove(pendingKey); yield break; }

            Transform beanGEO = FindBeanGEO(bean);
            Transform parent = beanGEO ?? bean.transform;

            GameObject clone = null;
            Exception applyEx = null;
            try
            {
                GameObject prefab = req.asset.Cast<GameObject>();
                clone = GameObject.Instantiate(prefab, parent.position, parent.rotation);
                int stripped = GameObjectHelper.StripPhysics(clone);
                if (stripped > 0) Plugin.Log.LogInfo($"accessory '{slot.skinInfo.file}' shipped {stripped} physics components, gone");
            }
            catch (Exception ex) { applyEx = ex; }

            if (applyEx != null || clone == null) { Plugin.Log.LogError($"instantiate failed: {applyEx?.Message}"); pendingKeys.Remove(pendingKey); yield break; }

            clone.transform.SetParent(parent, true);
            clone.transform.localScale = Vector3.one;

            var hiddenUntilBound = clone.GetComponentsInChildren<Renderer>(true);
            foreach (var r in hiddenUntilBound) if (r != null) r.enabled = false;

            if (GameObjectHelper.IsUICharacter(bean) || GameObjectHelper.IsLobbyCharacter(bean))
                GameObjectHelper.SetLayerRecursively(clone, LayerMask.NameToLayer("PlayerUI"));
            if (GameObjectHelper.IsLobbyCharacter(bean))
                clone.transform.localScale = Vector3.one;

            yield return SetupBoneSyncCoroutine(clone, bean).WrapToIl2Cpp();
            if (SlotDead(slot))
            {
                if (clone != null) GameObject.Destroy(clone);
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            BindSkinToLocalFallguy(clone, bean, "Acc");
            foreach (var r in hiddenUntilBound) if (r != null) r.enabled = true;

            SetRenderQueue(clone, 3000);

            KillExistingAppliedAtKey(pendingKey);
            appliedSkins[pendingKey] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Accessory };
            pendingKeys.Remove(pendingKey);
            Plugin.Log.LogInfo($"done '{slot.skinInfo.name}' -> {bean.name}");
            RaiseSkinApplied(slot.skinInfo, bean, reason);
        }

        // ── Item ──────────────────────────────────────────────────────────────

        private void BindSkinToLocalFallguy(GameObject clone, GameObject bean, string logname)
        {
            Transform chest = null;
            foreach (var smr0 in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr0.bones == null) continue;
                foreach (var b in smr0.bones)
                    if (b != null && b.name == "Chest_C_jnt") { chest = b; break; }
                if (chest != null) break;
            }
            if (chest != null && chest.localPosition.magnitude < 0.05f)
            {
                Plugin.Log.LogInfo($"{logname}: rig reads as crushed ({chest.localPosition.magnitude:F4}), using retarget instead of bind");
                return;
            }

            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            if (fgch == null) return;

            var fallguyBindposes = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            var fallguyParent = new Dictionary<string, string>(StringComparer.Ordinal);
            var perRenderer = new List<Dictionary<string, Matrix4x4>>();
            var baseSmrs = bean.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int b = 0; b < baseSmrs.Length; b++)
            {
                var baseSmr = baseSmrs[b];
                if (baseSmr == null || baseSmr.sharedMesh == null || baseSmr.bones == null) continue;
                var bindposes = baseSmr.sharedMesh.bindposes;
                if (bindposes == null || bindposes.Length != baseSmr.bones.Length) continue;
                var mine = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
                for (int bi = 0; bi < baseSmr.bones.Length; bi++)
                {
                    var bone = baseSmr.bones[bi];
                    if (bone == null) continue;
                    mine[bone.name] = bindposes[bi];
                    if (!fallguyBindposes.ContainsKey(bone.name))
                    {
                        fallguyBindposes[bone.name] = bindposes[bi];
                        if (bone.parent != null) fallguyParent[bone.name] = bone.parent.name;
                    }
                    string lower = bone.name.ToLowerInvariant();
                    if (!fallguyBindposes.ContainsKey(lower)) fallguyBindposes[lower] = bindposes[bi];
                }
                perRenderer.Add(mine);
            }

            var smrs = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var rigOffsets = new List<BoneOffsetEntry>();
            var seenBones = new HashSet<string>(StringComparer.Ordinal);
            int rotationMismatches = 0;
            string worstBone = null;
            float worstAngle = 0f;
            foreach (var smr0 in smrs)
            {
                if (smr0.bones == null) continue;
                foreach (var b in smr0.bones)
                {
                    if (b == null || !seenBones.Add(b.name)) continue;
                    Matrix4x4 bp = default, parentBp = default;
                    bool paired = false;
                    if (fallguyParent.TryGetValue(b.name, out var parentName))
                        foreach (var dict in perRenderer)
                            if (dict.TryGetValue(b.name, out bp) && dict.TryGetValue(parentName, out parentBp)) { paired = true; break; }

                    if (paired)
                    {
                        var realRest = parentBp * bp.inverse;
                        float angle = Quaternion.Angle(b.localRotation, realRest.rotation);
                        if (angle > 1f)
                        {
                            rotationMismatches++;
                            if (angle > worstAngle) { worstAngle = angle; worstBone = b.name; }
                        }
                        if ((b.localPosition - (Vector3)realRest.GetColumn(3)).sqrMagnitude >= 0.000001f)
                            rigOffsets.Add(new BoneOffsetEntry { bone = b.name, localPosition = b.localPosition });
                    }
                }
            }
            Plugin.Log.LogInfo($"{logname}: binding all {seenBones.Count} bones, {rigOffsets.Count} carry an offset the skeleton gets driven to ({rotationMismatches} rotated off rest, worst {worstBone ?? "none"} at {worstAngle:F1} deg)");

            int bound = 0;
            int customBoneSmrs = 0;
            for (int i = 0; i < smrs.Length; i++)
            {
                var smr = smrs[i];
                if (smr == null) continue;
                try
                {
                    var oldMesh = smr.sharedMesh;
                    var oldBindposes = oldMesh != null ? oldMesh.bindposes : null;
                    var oldWeights = oldMesh != null ? oldMesh.boneWeights : null;
                    bool hasCustomBones = false;
                    if (smr.bones != null)
                    {
                        for (int bi = 0; bi < smr.bones.Length; bi++)
                        {
                            var bone = smr.bones[bi];
                            if (bone == null) continue;
                            if (!fallguyBindposes.ContainsKey(bone.name) &&
                                !fallguyBindposes.ContainsKey(bone.name.ToLowerInvariant()))
                            {
                                hasCustomBones = true;
                                break;
                            }
                        }
                    }

                    if (hasCustomBones)
                    {
                        customBoneSmrs++;
                        Plugin.Log.LogInfo($"{logname}: leaving {smr.name} on custom bones");
                        continue;
                    }

                    fgch.BindMeshToFallguy(smr);

                    if (smr.sharedMesh != null && smr.bones != null && fallguyBindposes.Count > 0)
                    {
                        var newBindposes = new Matrix4x4[smr.bones.Length];
                        bool allMatched = true;
                        for (int bi = 0; bi < smr.bones.Length; bi++)
                        {
                            var bone = smr.bones[bi];
                            if (bone == null) { allMatched = false; break; }

                            if (!fallguyBindposes.TryGetValue(bone.name, out newBindposes[bi]) &&
                                !fallguyBindposes.TryGetValue(bone.name.ToLowerInvariant(), out newBindposes[bi]))
                            {
                                allMatched = false;
                                break;
                            }
                        }

                        if (allMatched)
                        {
                            var mesh = GameObject.Instantiate(smr.sharedMesh);
                            mesh.name = smr.sharedMesh.name + "_fallguybindposes";
                            if (oldMesh != null && oldBindposes != null && oldWeights != null &&
                                oldBindposes.Length == newBindposes.Length &&
                                oldWeights.Length == oldMesh.vertexCount)
                            {
                                var verts = oldMesh.vertices;
                                var normals = oldMesh.normals;
                                for (int v = 0; v < verts.Length; v++)
                                {
                                    var bw = oldWeights[v];
                                    Vector3 p = Vector3.zero;
                                    Vector3 n = Vector3.zero;

                                    if (bw.weight0 > 0f && bw.boneIndex0 < oldBindposes.Length)
                                    {
                                        var m = newBindposes[bw.boneIndex0].inverse * oldBindposes[bw.boneIndex0];
                                        p += m.MultiplyPoint3x4(verts[v]) * bw.weight0;
                                        if (normals != null && normals.Length == verts.Length) n += m.MultiplyVector(normals[v]) * bw.weight0;
                                    }
                                    if (bw.weight1 > 0f && bw.boneIndex1 < oldBindposes.Length)
                                    {
                                        var m = newBindposes[bw.boneIndex1].inverse * oldBindposes[bw.boneIndex1];
                                        p += m.MultiplyPoint3x4(verts[v]) * bw.weight1;
                                        if (normals != null && normals.Length == verts.Length) n += m.MultiplyVector(normals[v]) * bw.weight1;
                                    }
                                    if (bw.weight2 > 0f && bw.boneIndex2 < oldBindposes.Length)
                                    {
                                        var m = newBindposes[bw.boneIndex2].inverse * oldBindposes[bw.boneIndex2];
                                        p += m.MultiplyPoint3x4(verts[v]) * bw.weight2;
                                        if (normals != null && normals.Length == verts.Length) n += m.MultiplyVector(normals[v]) * bw.weight2;
                                    }
                                    if (bw.weight3 > 0f && bw.boneIndex3 < oldBindposes.Length)
                                    {
                                        var m = newBindposes[bw.boneIndex3].inverse * oldBindposes[bw.boneIndex3];
                                        p += m.MultiplyPoint3x4(verts[v]) * bw.weight3;
                                        if (normals != null && normals.Length == verts.Length) n += m.MultiplyVector(normals[v]) * bw.weight3;
                                    }

                                    verts[v] = p;
                                    if (normals != null && normals.Length == verts.Length && n.sqrMagnitude > 0.000001f)
                                        normals[v] = n.normalized;
                                }
                                mesh.vertices = verts;
                                if (normals != null && normals.Length == verts.Length) mesh.normals = normals;
                            }
                            mesh.bindposes = newBindposes;
                            mesh.RecalculateBounds();
                            smr.sharedMesh = mesh;
                        }
                    }

                    // the bind reparents this mesh OUT of the clone, so the clone's own
                    // InvisibilitySyncComponent no longer covers it and the game's controller never
                    // knew about it — it needs its own sync or it stays solid while the wearer is invisible
                    if (smr.gameObject.GetComponent<InvisibilitySyncComponent>() == null)
                        smr.gameObject.AddComponent<InvisibilitySyncComponent>().playerObject = bean;
                    bound++;
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"{logname}: BindMeshToFallguy failed on {smr.name}: {ex.Message}"); }
            }

            if (bound > 0)
            {
                Plugin.Log.LogInfo($"{logname}: bound {bound} skin SMRs to local fallguy, customBoneSMRs={customBoneSmrs}");
            }

            if (bound == 0 || customBoneSmrs > 0) return;
            var sync = clone.GetComponent<BoneSyncComponent>();
            if (sync == null) return;
            if (rigOffsets.Count > 0) sync.DriveRealBonesFromOffsets(rigOffsets);
            else sync.enabled = false;
        }

        private IEnumerator ApplyItemCoroutine(ActiveSkinSlot slot, GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            string pendingKey = MakeKey(bean, slot.skinInfo.file);
            if (slot.bundle == null) { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }
            AssetBundleRequest req = slot.bundle.LoadAssetAsync(slot.skinInfo.file);
            yield return req;

            try { _ = bean.name; } catch { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }

            if (req.asset == null)
            {
                Plugin.Log.LogError($"asset '{slot.skinInfo.file}' not in bundle");
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            GameObject prefab = null;
            Exception applyEx = null;
            try { prefab = req.asset.Cast<GameObject>(); }
            catch (Exception ex) { applyEx = ex; }

            if (applyEx != null || prefab == null) { pendingKeys.Remove(pendingKey); yield break; }

            bool hasLeft = slot.skinInfo.left != null;
            bool hasRight = slot.skinInfo.right != null;
            int ov = slot.skinInfo.handOverride;
            bool spawnLeft = hasLeft && (ov == 0 || ov == 1 || ov == 3);
            bool spawnRight = hasRight && (ov == 0 || ov == 2 || ov == 3);

            if (spawnLeft)
                if (!appliedSkins.ContainsKey(MakeKey(bean, slot.skinInfo.file + "_L")))
                    SpawnItemOnHand(prefab, bean, slot.skinInfo, isLeft: true, slot);
            if (spawnRight)
                if (!appliedSkins.ContainsKey(MakeKey(bean, slot.skinInfo.file + "_R")))
                    SpawnItemOnHand(prefab, bean, slot.skinInfo, isLeft: false, slot);

            if (!spawnLeft && !spawnRight && !appliedSkins.ContainsKey(pendingKey))
            {
                GameObject clone = GameObject.Instantiate(prefab, bean.transform.position, bean.transform.rotation);
                GameObjectHelper.StripPhysics(clone);
                clone.transform.SetParent(bean.transform, true);
                clone.transform.localScale = Vector3.one * slot.skinInfo.scale;
                clone.AddComponent<InvisibilitySyncComponent>().playerObject = bean;
                SetRenderQueue(clone, 3000);
                if (SlotDead(slot))
                {
                    GameObject.Destroy(clone);
                    pendingKeys.Remove(pendingKey);
                    yield break;
                }
                KillExistingAppliedAtKey(pendingKey);
                appliedSkins[pendingKey] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Item };
                RaiseSkinApplied(slot.skinInfo, bean, reason);
            }

            pendingKeys.Remove(pendingKey);
        }

        private void SpawnItemOnHand(GameObject prefab, GameObject bean, SkinInfo skinInfo, bool isLeft, ActiveSkinSlot slot = null)
        {
            if (SlotDead(slot)) return;
            ItemHandInfo handInfo = isLeft ? skinInfo.left : skinInfo.right;
            if (handInfo == null) return;

            string handBoneName = isLeft ? "Wrist_L_jnt" : "Wrist_R_jnt1";
            Transform rig = bean.transform.Find(BeanVisualRig.RIG_NAME);
            Transform searchRoot = rig != null ? rig : bean.transform;
            Transform handBone = FindBoneDeep(searchRoot, handBoneName);
            Transform parent = handBone ?? searchRoot;

            GameObject clone = GameObject.Instantiate(prefab, parent.position, parent.rotation);
            GameObjectHelper.StripPhysics(clone);
            clone.transform.SetParent(parent, false);

            string hk = isLeft ? "l" : "r";
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float ox = float.TryParse(skinInfo.OwnerSetting($"item{hk}offset.{skinInfo.file}.x", "0"), System.Globalization.NumberStyles.Float, ci, out float _ox) ? _ox : 0f;
            float oy = float.TryParse(skinInfo.OwnerSetting($"item{hk}offset.{skinInfo.file}.y", "0"), System.Globalization.NumberStyles.Float, ci, out float _oy) ? _oy : 0f;
            float oz = float.TryParse(skinInfo.OwnerSetting($"item{hk}offset.{skinInfo.file}.z", "0"), System.Globalization.NumberStyles.Float, ci, out float _oz) ? _oz : 0f;
            Vector3 basePos = new Vector3(handInfo.position[0], handInfo.position[1], handInfo.position[2]);
            clone.transform.localPosition = basePos + new Vector3(ox, oy, oz);

            float rrx = float.TryParse(skinInfo.OwnerSetting($"item{hk}rot.{skinInfo.file}.x", "0"), System.Globalization.NumberStyles.Float, ci, out float _rrx) ? _rrx : 0f;
            float rry = float.TryParse(skinInfo.OwnerSetting($"item{hk}rot.{skinInfo.file}.y", "0"), System.Globalization.NumberStyles.Float, ci, out float _rry) ? _rry : 0f;
            float rrz = float.TryParse(skinInfo.OwnerSetting($"item{hk}rot.{skinInfo.file}.z", "0"), System.Globalization.NumberStyles.Float, ci, out float _rrz) ? _rrz : 0f;
            Vector3 baseRot = new Vector3(handInfo.rotation[0], handInfo.rotation[1], handInfo.rotation[2]);
            clone.transform.localEulerAngles = baseRot + new Vector3(rrx, rry, rrz);

            clone.transform.localScale = Vector3.one * skinInfo.scale;
            clone.AddComponent<InvisibilitySyncComponent>().playerObject = bean;

            if (GameObjectHelper.IsUICharacter(bean) || GameObjectHelper.IsLobbyCharacter(bean))
                GameObjectHelper.SetLayerRecursively(clone, LayerMask.NameToLayer("PlayerUI"));

            string key = MakeKey(bean, skinInfo.file + (isLeft ? "_L" : "_R"));
            SetRenderQueue(clone, 3000);
            if (SlotDead(slot))
            {
                GameObject.Destroy(clone);
                return;
            }
            KillExistingAppliedAtKey(key);
            appliedSkins[key] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Item };
            RaiseSkinApplied(skinInfo, bean, ApplyReason.FromMenu);
        }

        private static Transform FindBoneDeep(Transform root, string boneName)
        {
            if (root == null) return null;
            if (root.name == boneName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var result = FindBoneDeep(root.GetChild(i), boneName);
                if (result != null) return result;
            }
            return null;
        }

        // ── BoneSync ──────────────────────────────────────────────────────────

        private IEnumerator SetupBoneSyncCoroutine(GameObject clone, GameObject bean)
        {
            var sync = clone.AddComponent<BoneSyncComponent>();
            sync.playerObject = bean;
            sync.isRemote = IsRemoteInRoundBean(bean);
            clone.AddComponent<InvisibilitySyncComponent>().playerObject = bean;
            yield break;
        }
    }
}
