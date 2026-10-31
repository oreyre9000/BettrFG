using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.UI;
using BetterFG.Nametag;
using BetterFG.Network;
using BetterFG.Services;
using FG.Common;
using FGClient;
using MPG.Utility;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace BetterFG.Customization.Player
{
    public class NametagSurface
    {
        public TextMeshProUGUI partyText;
        public Transform partyRoot;
        public bool skip;

        public static NametagSurface None => new NametagSurface { skip = true };
        public static NametagSurface Party(TextMeshProUGUI text, Transform root)
            => new NametagSurface { partyText = text, partyRoot = root };
    }

    public static class CustomizationHandler
    {
        public static MonoBehaviour Host => NetworkClient.Instance;

        public static void Dress(BfgProfile p, GameObject bean, NametagSurface tag = null)
        {
            var host = Host;
            if (host == null || p == null || bean == null) return;
            host.StartCoroutine(Apply(p, bean, tag).WrapToIl2Cpp());
        }

        public static void ApplyToLobbyBean(BfgProfile p, GameObject bean, GameObject plinthHolder, GameObject plinthMesh,
                                            TMPro.TextMeshProUGUI nameTmp, Transform partyTagTransform = null)
        {
            if (p == null || bean == null) return;

            Dress(p, bean, nameTmp != null ? NametagSurface.Party(nameTmp, partyTagTransform) : NametagSurface.None);

            if (plinthHolder == null || plinthMesh == null) return;

            var host = Host;
            if (host == null) return;
            if (p.plinth != null && !string.IsNullOrEmpty(p.plinth.file))
                host.StartCoroutine(LoadPlinthBytesThenApply(p.plinth, plinthHolder, plinthMesh).WrapToIl2Cpp());
            if (p.Get("menu.plinth.col.on") == "true")
                host.StartCoroutine(TintPartyPlinth(p, plinthHolder, plinthMesh).WrapToIl2Cpp());
        }

        static IEnumerator LoadPlinthBytesThenApply(PlinthEmbed pe, GameObject holder, GameObject mesh)
        {
            var slot = new PlinthSlot { holderGO = holder, meshGO = mesh, type = PlinthType.MainMenu };

            if (pe.source == "game")
            {
                var menuApp = MenuCustomizationApplication.Instance;
                if (menuApp != null) menuApp.ApplyProfileGamePlinthToSlot(pe.file, slot);
                yield break;
            }

            byte[] bytes = null;

            if (!string.IsNullOrEmpty(pe.bundleB64))
            {
                try { bytes = Convert.FromBase64String(pe.bundleB64); }
                catch (Exception ex) { Plugin.Log.LogWarning("plinth bytes: " + ex.Message); }
            }
            else
            {
                string repoRaw = !string.IsNullOrEmpty(pe.repoUrl) ? pe.repoUrl.TrimEnd('/')
                    : (Services.RepoRegistry.Instance?.Active?.RawBase ?? "");
                string folder = !string.IsNullOrEmpty(pe.folder) ? pe.folder : $"Plinths/{pe.file}";
                var www = UnityWebRequest.Get($"{repoRaw}/{folder}/{pe.file}");
                yield return www.SendWebRequest();
                if (www.result == UnityWebRequest.Result.Success)
                    bytes = www.downloadHandler.data;
                www.Dispose();
            }
            if (bytes == null) { Plugin.Log.LogWarning($"plinth '{pe.file}' never downloaded, holder stays stock"); yield break; }

            var menu = MenuCustomizationApplication.Instance;
            if (menu == null) yield break;
            menu.ApplyProfilePlinthToSlot(new SkinInfo { name = pe.file, file = pe.file, type = "plinth" }, bytes, slot);
        }

        static IEnumerator TintPartyPlinth(BfgProfile p, GameObject holder, GameObject mesh)
        {
            var col = new Color(p.GetFloat("menu.plinth.col.r", 1f), p.GetFloat("menu.plinth.col.g", 1f), p.GetFloat("menu.plinth.col.b", 1f));
            Renderer[] rends = null;
            if (p.plinth != null && !string.IsNullOrEmpty(p.plinth.file))
            {
                float waited = 0f;
                Transform clone = null;
                while (waited < 10f && (clone = holder.transform.Find("BetterFG_Plinth")) == null)
                { yield return new WaitForSeconds(0.25f); waited += 0.25f; }
                if (clone == null) { Plugin.Log.LogWarning("plinth tint: clone never showed up"); yield break; }
                yield return null;
                rends = clone.GetComponentsInChildren<Renderer>(true);
            }
            else if (mesh != null)
                rends = mesh.GetComponentsInChildren<Renderer>(true);

            if (rends == null) yield break;
            foreach (var r in rends)
                if (r != null && r.material != null)
                    r.material.color = new Color(col.r, col.g, col.b, r.material.color.a);
        }

        static readonly Dictionary<int, (GameObject bean, BfgProfile profile)> _dressed = new Dictionary<int, (GameObject, BfgProfile)>();

        public static List<(GameObject bean, BfgProfile profile)> Dressed()
        {
            var list = new List<(GameObject, BfgProfile)>();
            foreach (var kv in new List<int>(_dressed.Keys))
            {
                var d = _dressed[kv];
                if (d.bean == null) _dressed.Remove(kv);
                else list.Add(d);
            }
            return list;
        }

        internal static BfgProfile ProfileFor(GameObject bean)
            => bean != null && _dressed.TryGetValue(bean.GetInstanceID(), out var d) ? d.profile : null;

        public static void Undress(GameObject bean)
        {
            _dressed.Remove(bean.GetInstanceID());
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.ClearProfileOverride(bean);
            var app = CustomizationServices.ApplicationService;
            app.RemoveAllSkins(bean);

            var sel = bean.GetComponent<MainMenuFallGuy>()?.customisationSelection;
            if (sel != null) SingletonBehaviour<CustomisationManager>.Instance.ApplyCustomisationsToFallGuy(bean, sel, -1, true);
            else Plugin.Log.LogWarning($"{bean.name} lost its profile but has no selection of its own to go back to");

            Plugin.Log.LogInfo($"undressed {bean.name}");
        }

        public static IEnumerator Apply(BfgProfile p, GameObject bean, NametagSurface tag = null)
        {
            if (p == null || bean == null) yield break;

            _dressed[bean.GetInstanceID()] = (bean, p);
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.ApplyProfileOverride(bean, p);

            var app = CustomizationServices.ApplicationService;
            var loader = CustomizationServices.LoaderService;
            if (app == null || loader == null) yield break;

            if (p.scale > 0f)
                PlayerScaleService.ApplySkinScaleToBean(bean, p.scale,
                    GameObjectHelperIsMenuBean(bean) ? PlayerScaleService.BeanScaleMode.Local
                                                     : PlayerScaleService.BeanScaleMode.Remote);

            foreach (var entry in p.skins)
            {
                ActiveSkinSlot slot = null;
                yield return loader.ResolveProfileSlot(entry, new Action<ActiveSkinSlot>(s => slot = s), p.settings).WrapToIl2Cpp();
                if (slot == null || bean == null) continue;
                yield return app.ApplySkinToBean(slot, bean).WrapToIl2Cpp();
            }

            if (bean == null) yield break;

            bool cosmeticsDone = false;
            app.ApplyProfileCosmeticsToBean(
                p.Get(BfgProfile.CosmeticIds), p.Get(BfgProfile.CosmeticColour),
                p.Get(BfgProfile.CosmeticPattern), p.Get(BfgProfile.CosmeticFaceplate),
                bean, (Action)(() => cosmeticsDone = true), p.teamId);
            while (!cosmeticsDone) { if (bean == null) yield break; yield return null; }

            if (bean == null) yield break;

            SkinApplicationService.ApplyEntriesToBean(p.TexEntries(), bean);
            Social.EmoteInjectionService.StampProfileBean(bean, p);

            if (tag != null && tag.skip) yield break;

            if (tag != null && tag.partyText != null)
            {
                if (p.nametag != null) ApplyToPartyTag(p, tag);
                yield break;
            }

            if (p.nametag != null || p.Crown().enabled)
                yield return PollAndApplyToHud(p, bean).WrapToIl2Cpp();
        }

        static bool GameObjectHelperIsMenuBean(GameObject bean)
            => Utilities.GameObjectHelper.IsUICharacter(bean) || Utilities.GameObjectHelper.IsLobbyCharacter(bean);

        public static void ApplyToPartyTag(BfgProfile p, NametagSurface tag)
        {
            var nt = p.nametag;
            string fallback = StripRich(tag.partyText.text);
            NametagIconApplicator.ApplyRemoteToNameplate(tag.partyText, fallback, nt);

            var vm = tag.partyRoot != null ? tag.partyRoot
                   : (tag.partyText.transform.parent != null ? tag.partyText.transform.parent : tag.partyText.transform);
            NametagIconApplicator.ApplyPartyBacking(vm, nt.backingEnabled, nt.backingPath, nt.backingOffX, nt.backingOffY, nt.backingScale);
            NametagIconApplicator.ApplyNickname(vm, true, enabled: true, nt.nickname ?? "");
        }

        static IEnumerator PollAndApplyToHud(BfgProfile p, GameObject bean)
        {
            var info = p.nametag;
            float elapsed = 0f;
            while (elapsed < 8f)
            {
                if (bean == null) yield break;
                var display = RemoteNametagResolver.TryGetDisplayForBean(bean);
                var tmp = display != null ? NametagIconApplicator.TryGetNameText(display) : null;
                if (tmp != null)
                {
                    ApplyToHudDisplay(p, display);
                    if (info == null) yield break;

                    bool hide = info.platformHide == "true";
                    string customSprite = info.platformCustom ?? "";
                    if (hide || !string.IsNullOrEmpty(customSprite))
                        yield return NetworkClient.PollAndApplyPlatformIcon(bean, hide, customSprite).WrapToIl2Cpp();
                    yield break;
                }
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }
            Plugin.Log.LogWarning($"gave up waiting on a nametag for {(bean != null ? bean.name : "a dead bean")}");
        }

        public static void ApplyToHudDisplay(BfgProfile p, PlayerInfoDisplay display)
        {
            var tmp = NametagIconApplicator.TryGetNameText(display);
            var crown = p.Crown();
            if (crown.enabled) CrownRankService.ApplyCrownTo(display, crown, position: false);

            var info = p.nametag;
            if (info != null)
            {
                NametagIconApplicator.ApplyRemoteToDisplay(display, tmp.text, info);
                var vm = tmp.transform.parent != null ? tmp.transform.parent : tmp.transform;
                NametagIconApplicator.ApplyBacking(vm, info.backingEnabled, info.backingPath, info.backingOffX, info.backingOffY, info.backingScale);
                NametagIconApplicator.ApplyNickname(vm, false, enabled: true, info.nickname ?? "");
            }
            CrownRankService.SetTagSwap(display, crown.enabled && crown.swapSide);
        }

        static string StripRich(string s) => string.IsNullOrEmpty(s) ? s
            : System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>", "").Trim();
    }
}
