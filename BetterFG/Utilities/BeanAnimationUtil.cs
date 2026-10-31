using BetterFG.Services;
using UnityEngine;

namespace BetterFG.Utilities
{
    public static class BeanAnimationUtil
    {
        public static Animator FindAnimator(GameObject bean)
        {
            if (bean == null) return null;
            var wrapper = bean.transform.Find(PlayerScaleService.WRAPPER_NAME);
            var character = (wrapper != null ? wrapper : bean.transform).Find("Character");
            return character?.GetComponent<Animator>();
        }
    }
}
