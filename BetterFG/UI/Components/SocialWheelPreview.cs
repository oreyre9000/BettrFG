using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BettrFG.uGUI;
using FGClient;
using UnityEngine;

namespace BetterFG.UI.Components
{
    internal sealed class SocialWheelPreview
    {
        private static SocialWheelPreview _active;

        private readonly MonoBehaviour _host;
        private readonly Func<WheelType> _type;
        private readonly Action<int> _onSlotClicked;
        private GameObject _clone;

        internal bool Shown => _clone != null;

        internal SocialWheelPreview(MonoBehaviour host, Func<WheelType> type, Action<int> onSlotClicked = null)
        {
            _host = host;
            _type = type;
            _onSlotClicked = onSlotClicked;
        }

        internal static void OnPrimeClicked(GameObject clickedGo, int slot)
        {
            if (_active == null || _active._clone != clickedGo) return;
            if (slot >= 0) _active._onSlotClicked?.Invoke(slot);
            _active.Display();
        }

        internal void Spawn(Transform parent)
        {
            Destroy();

            var all = Resources.FindObjectsOfTypeAll<SocialPrimeHandler>();
            SocialPrimeHandler scenePrime = all != null && all.Length > 0 ? all[0] : null;
            if (scenePrime == null || parent == null) return;

            var cloneGo = UnityEngine.Object.Instantiate(scenePrime.gameObject, parent);
            cloneGo.name = "BettrFG_SocialPrimeVisualizer";
            cloneGo.hideFlags = HideFlags.None;
            cloneGo.transform.localPosition = new Vector3(200.5452f, 678.8191f, 0f);
            cloneGo.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
            cloneGo.SetActive(true);
            _clone = cloneGo;
            _active = this;

            foreach (var t in cloneGo.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "TabContentSocialPhraseOverlay") t.gameObject.SetActive(true);
                else if (t.name == "Generic_UI_Scrim_Prefab" || t.name == "scrim") t.gameObject.SetActive(false);
            }

            for (int i = 0; i < 8; i++)
            {
                float ang = (Mathf.PI * 2f) * (i / 8f) - Mathf.PI * 0.5f;
                var txt = UGUIShip.CreateLabel(cloneGo.transform, new Rect(0f, 0f, 60f, 60f),
                    i.ToString(), 40, Color.white, TextAnchor.MiddleCenter);
                txt.fontStyle = FontStyle.Bold;
                var numRt = txt.rectTransform;
                numRt.anchorMin = numRt.anchorMax = new Vector2(0.5f, 0.5f);
                numRt.pivot = new Vector2(0.5f, 0.5f);
                numRt.anchoredPosition = new Vector2(Mathf.Cos(ang) * 150f, -Mathf.Sin(ang) * 150f);
            }

            _host.StartCoroutine(DisplayNextFrame().WrapToIl2Cpp());
        }

        internal void Refresh(Transform parent)
        {
            if (_clone == null) { Spawn(parent); return; }
            Display();
        }

        internal void Destroy()
        {
            if (_active == this) _active = null;
            if (_clone == null) return;
            _clone.GetComponent<SocialPrimeHandler>()?.OnDestroy();
            UnityEngine.Object.Destroy(_clone);
            _clone = null;
        }

        private IEnumerator DisplayNextFrame()
        {
            yield return null;
            Display();
        }

        private void Display()
        {
            var prime = _clone?.GetComponent<SocialPrimeHandler>();
            if (prime == null) return;
            var type = _type();
            prime._wheelType = type;
            prime.DisplayWheel(type);
            _host.StartCoroutine(ReleaseInputNextFrame(prime).WrapToIl2Cpp());
        }

        private static IEnumerator ReleaseInputNextFrame(SocialPrimeHandler prime)
        {
            yield return null;
            if (prime == null) yield break;
            var co = prime.SetRewiredStatesForInputWheel(false);
            while (co != null && co.MoveNext()) yield return co.Current;
        }
    }
}
