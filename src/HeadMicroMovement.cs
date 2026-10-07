using UnityEngine;

namespace Norm
{
    public class HeadMicroMovement
    {
        private JSONStorableFloat intensityH;
        private JSONStorableFloat intensityV;
        private JSONStorableFloat pauseMin;
        private JSONStorableFloat pauseMax;
        private JSONStorableFloat breathing;

        private float offsetYaw = 0f;
        private float offsetPitch = 0f;
        private float targetOffsetYaw = 0f;
        private float targetOffsetPitch = 0f;
        private float offsetVelocityYaw = 0f;
        private float offsetVelocityPitch = 0f;
        private float timer = 0f;
        private bool active = false;
        private const float ActiveDuration = 1.5f;

        public HeadMicroMovement() { }

        public void SetParams(
            JSONStorableFloat intensH,
            JSONStorableFloat intensV,
            JSONStorableFloat pMin,
            JSONStorableFloat pMax,
            JSONStorableFloat breath)
        {
            intensityH = intensH;
            intensityV = intensV;
            pauseMin = pMin;
            pauseMax = pMax;
            breathing = breath;
        }

        public void Update(float deltaTime, bool skipMovement, out float microH, out float microV, out float breathOffset)
        {
            if (!skipMovement)
            {
                timer -= deltaTime;
                if (active)
                {
                    if (timer <= 0f)
                    {
                        active = false;
                        targetOffsetYaw = 0f;
                        targetOffsetPitch = 0f;
                        float pMin = pauseMin != null ? pauseMin.val : 1f;
                        float pMax = pauseMax != null ? pauseMax.val : 5f;
                        if (pMax < pMin) pMax = pMin;
                        timer = UnityEngine.Random.Range(pMin, pMax);
                    }
                }
                else
                {
                    if (timer <= 0f)
                    {
                        active = true;
                        timer = ActiveDuration;
                        float maxHDeg = (intensityH != null ? intensityH.val : 0.20f) * 6f;
                        float maxVDeg = (intensityV != null ? intensityV.val : 0.10f) * 6f;
                        targetOffsetYaw = UnityEngine.Random.Range(-maxHDeg, maxHDeg);
                        targetOffsetPitch = UnityEngine.Random.Range(-maxVDeg, maxVDeg);
                    }
                }
                float microSmoothTime = 1.0f / 20f;
                offsetYaw = Mathf.SmoothDampAngle(offsetYaw, targetOffsetYaw, ref offsetVelocityYaw, microSmoothTime, Mathf.Infinity, deltaTime);
                offsetPitch = Mathf.SmoothDampAngle(offsetPitch, targetOffsetPitch, ref offsetVelocityPitch, microSmoothTime, Mathf.Infinity, deltaTime);
            }
         else
         {
             offsetYaw = 0f;
             offsetPitch = 0f;
             offsetVelocityYaw = 0f;
             offsetVelocityPitch = 0f;
         }

            microH = offsetYaw;
            microV = offsetPitch;
            breathOffset = ComputeBreathOffset();
        }

        private float ComputeBreathOffset()
        {
            if (breathing == null || breathing.val <= 0.001f) return 0f;
            const float cycleDuration = 4.5f;
            const float inhaleFraction = 0.3f;
            const float exhaleFraction = 0.5f;
            float phase = (Time.time % cycleDuration) / cycleDuration;
            float offset;
            if (phase < inhaleFraction)
            {
                float t = phase / inhaleFraction;
                offset = -Mathf.SmoothStep(0f, 1f, t);
            }
            else if (phase < inhaleFraction + exhaleFraction)
            {
                float t = (phase - inhaleFraction) / exhaleFraction;
                offset = -1f + Mathf.SmoothStep(0f, 1f, t);
            }
            else { offset = 0f; }
            const float maxAmplitudeDeg = 1.5f;
            return offset * breathing.val * maxAmplitudeDeg;
        }
    }
}