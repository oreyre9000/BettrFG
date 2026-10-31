using UnityEngine;

namespace BetterFG.Customization.Player
{
    public class BaseBodyGuard : MonoBehaviour
    {
        public GameObject owner;

        void OnEnable()
        {
            if (owner == null) { Destroy(this); return; }
            gameObject.SetActive(false);
        }
    }
}
