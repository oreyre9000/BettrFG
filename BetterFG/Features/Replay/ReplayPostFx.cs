using BetterFG.Core;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace BetterFG.Features.Replay
{
    internal class ReplayPostFx
    {
        public const int VolumeLayer = 0;

        readonly ReplayRecording _rec;
        readonly GameObject _go;
        readonly PostProcessVolume _volume;
        readonly ColorGrading _grading;
        readonly Vignette _vignette;
        readonly ChromaticAberration _chroma;
        readonly Bloom _bloom;
        readonly DepthOfField _dof;
        readonly MotionBlur _motion;

        readonly GameObject _sharpenGo;
        readonly Material _sharpenMat;
        readonly MeshRenderer _sharpenRenderer;

        public ReplayPostFx(ReplayRecording rec, Transform parent)
        {
            _rec = rec;

            _go = new GameObject("BettrFG_ReplayPostFx");
            _go.transform.SetParent(parent, false);
            _go.layer = VolumeLayer;

            var profile = ScriptableObject.CreateInstance<PostProcessProfile>();

            _grading = profile.AddSettings<ColorGrading>();
            _grading.enabled.Override(true);
            _grading.gradingMode.Override(GradingMode.LowDefinitionRange);
            _grading.colorFilter.overrideState = true;
            _grading.contrast.overrideState = true;
            _grading.saturation.overrideState = true;
            _grading.temperature.overrideState = true;
            _grading.tint.overrideState = true;

            _vignette = profile.AddSettings<Vignette>();
            _vignette.enabled.Override(true);
            _vignette.intensity.overrideState = true;

            _chroma = profile.AddSettings<ChromaticAberration>();
            _chroma.enabled.Override(true);
            _chroma.intensity.overrideState = true;

            _bloom = profile.AddSettings<Bloom>();
            _bloom.enabled.Override(true);
            _bloom.intensity.overrideState = true;
            _bloom.threshold.overrideState = true;

            _dof = profile.AddSettings<DepthOfField>();
            _dof.enabled.Override(false);
            _dof.focusDistance.overrideState = true;
            _dof.aperture.overrideState = true;
            _dof.focalLength.overrideState = true;
            _dof.kernelSize.Override(KernelSize.Medium);

            _motion = profile.AddSettings<MotionBlur>();
            _motion.enabled.Override(false);
            _motion.shutterAngle.overrideState = true;
            _motion.sampleCount.overrideState = true;

            _volume = _go.AddComponent<PostProcessVolume>();
            _volume.isGlobal = true;
            _volume.priority = 1000f;
            _volume.weight = 0f;
            _volume.sharedProfile = profile;

            var sharpenShader = AssetManager.GetShader("bettrfg_overlay_sharpen");
            if (sharpenShader != null)
            {
                _sharpenGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _sharpenGo.name = "BettrFG_ReplaySharpen";
                _sharpenGo.transform.SetParent(parent, false);
                _sharpenGo.layer = VolumeLayer;

                var collider = _sharpenGo.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.Destroy(collider);

                _sharpenMat = new Material(sharpenShader);
                _sharpenRenderer = _sharpenGo.GetComponent<MeshRenderer>();
                _sharpenRenderer.sharedMaterial = _sharpenMat;
                _sharpenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _sharpenRenderer.receiveShadows = false;
                _sharpenRenderer.enabled = false;
            }
            else Plugin.Log.LogWarning("bettrfg_overlay_sharpen shader missing, sharpening is off");
        }

        public void Apply(float time, Camera cam)
        {
            var keys = _rec.postFxKeyframes;
            if (keys.Count == 0)
            {
                _volume.weight = 0f;
                if (_sharpenRenderer != null) _sharpenRenderer.enabled = false;
                return;
            }

            ReplayPostFxKeyframe a, b;
            float raw = 0f;

            if (time <= keys[0].time) { a = keys[0]; b = null; }
            else if (time >= keys[keys.Count - 1].time) { a = keys[keys.Count - 1]; b = null; }
            else
            {
                int i = 0;
                while (i + 1 < keys.Count && keys[i + 1].time <= time) i++;
                a = keys[i];
                b = keys[i + 1];
                float den = b.time - a.time;
                raw = den > 0f ? Mathf.Clamp01((time - a.time) / den) : 0f;
            }

            _volume.weight = 1f;
            float exposure = b != null ? Mathf.Lerp(a.exposure, b.exposure, raw) : a.exposure;
            float mul = Mathf.Pow(2f, exposure);
            _grading.colorFilter.value = new Color(mul, mul, mul, 1f);
            _grading.contrast.value = b != null ? Mathf.Lerp(a.contrast, b.contrast, raw) : a.contrast;
            _grading.saturation.value = b != null ? Mathf.Lerp(a.saturation, b.saturation, raw) : a.saturation;
            _grading.temperature.value = b != null ? Mathf.Lerp(a.temperature, b.temperature, raw) : a.temperature;
            _grading.tint.value = b != null ? Mathf.Lerp(a.tint, b.tint, raw) : a.tint;
            _vignette.intensity.value = Mathf.Clamp01(b != null ? Mathf.Lerp(a.vignette, b.vignette, raw) : a.vignette);
            _chroma.intensity.value = Mathf.Clamp01(b != null ? Mathf.Lerp(a.chromaticAberration, b.chromaticAberration, raw) : a.chromaticAberration);
            _bloom.intensity.value = Mathf.Max(0f, b != null ? Mathf.Lerp(a.bloomIntensity, b.bloomIntensity, raw) : a.bloomIntensity);
            _bloom.threshold.value = Mathf.Max(0f, b != null ? Mathf.Lerp(a.bloomThreshold, b.bloomThreshold, raw) : a.bloomThreshold);

            float dof = Mathf.Clamp01(b != null ? Mathf.Lerp(a.dofStrength, b.dofStrength, raw) : a.dofStrength);
            _dof.enabled.value = dof > 0.001f;
            if (_dof.enabled.value)
            {
                _dof.focusDistance.value = Mathf.Max(0.1f, b != null ? Mathf.Lerp(a.dofDistance, b.dofDistance, raw) : a.dofDistance);
                _dof.aperture.value = Mathf.Lerp(22f, 1.4f, dof);
                _dof.focalLength.value = Mathf.Lerp(20f, 130f, dof);
            }

            float blur = Mathf.Clamp01(b != null ? Mathf.Lerp(a.motionBlur, b.motionBlur, raw) : a.motionBlur);
            _motion.enabled.value = blur > 0.001f;
            if (_motion.enabled.value)
            {
                _motion.shutterAngle.value = blur * 360f;
                _motion.sampleCount.value = Mathf.Clamp(a.motionBlurSamples, 4, 32);
            }

            if (_sharpenRenderer == null) return;

            float amount = Mathf.Max(0f, b != null ? Mathf.Lerp(a.sharpenAmount, b.sharpenAmount, raw) : a.sharpenAmount);
            bool on = amount > 0.001f && cam != null;
            _sharpenRenderer.enabled = on;
            if (!on) return;

            float radius = Mathf.Max(0.01f, b != null ? Mathf.Lerp(a.sharpenRadius, b.sharpenRadius, raw) : a.sharpenRadius);

            var t = cam.transform;
            float dist = Mathf.Max(cam.nearClipPlane * 4f, 0.15f);
            _sharpenGo.transform.SetPositionAndRotation(t.position + t.forward * dist, t.rotation);

            float halfH = dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * cam.aspect;
            _sharpenGo.transform.localScale = new Vector3(halfW * 2f, halfH * 2f, 1f);

            _sharpenMat.SetFloat("_Amount", amount);
            _sharpenMat.SetFloat("_Radius", radius);
        }

        public void Release()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_sharpenGo != null) UnityEngine.Object.Destroy(_sharpenGo);
        }
    }
}
