using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BehaviorUtils;

namespace Norm
{
    public class HeadController
    {
        private MVRScript script;
        private Atom atom;
        private BehaviorHeadUI headUI;
        private EyesController eyesCtrl;
        private List<GameObject> uiObjects = new List<GameObject>();

        private JSONStorableFloat headSpeed;
        private JSONStorableFloat headMaxAngleH;
        private JSONStorableFloat headMaxAngleV;
        private JSONStorableFloat headAngleOffsetH;
        private JSONStorableFloat headAngleOffsetV;
        private JSONStorableFloat headVerticality;
        private JSONStorableBool headVerticalityAdaptive;
        private JSONStorableFloat headMicroIntensityH;
        private JSONStorableFloat headMicroIntensityV;
        private JSONStorableFloat headMicroPauseMin;
        private JSONStorableFloat headMicroPauseMax;
        private JSONStorableFloat headTurnChance;
        private JSONStorableFloat headTurnDelayMin;
        private JSONStorableFloat headTurnDelayMax;
        private JSONStorableFloat breathing;
        private JSONStorableFloat gazeAversionDownChance;
        private JSONStorableFloat gazeAversionSideChance;
        private JSONStorableFloat gazeAversionDownAngle;
        private JSONStorableFloat gazeAversionSideAngle;
        private JSONStorableFloat gazeAversionDelay;

        private const float DirectTurnSpeedMultiplierMin = 0.4f;
        private const float SingularThreshold = 0.15f;

        private FreeControllerV3 headControl;
        private FreeControllerV3 chestControl;
        private FreeControllerV3 eyeTargetControl;
        private FreeControllerV3 abdomenControl;
        private FreeControllerV3 abdomen2Control;

        private RandomGazeController randomController;
        private GlanceChainSystem chainSystem;
        private AttentionSystem attentionSystem;
        private AttentionConfig attentionConfig;
        private BodyPartsConfig bodyPartsConfig;

        private float lastStableAimHDeg = 0f;
        private float lastStableAimVDeg = 0f;
        private float smoothedVertStrength = 0f;
        private float vertStrengthVelocity = 0f;

        public float HeadSpeedVal { get { return headSpeed != null ? headSpeed.val : 5f; } }
        public float HeadAngleOffsetHVal { get { return headAngleOffsetH != null ? headAngleOffsetH.val : 0f; } }
        public float HeadAngleOffsetVVal { get { return headAngleOffsetV != null ? headAngleOffsetV.val : 0f; } }

        public TargetDef CurrentTarget
        {
            get
            {
                TargetDef chainOverride = chainSystem != null ? chainSystem.GetCurrentOverride() : null;
                return chainOverride ?? headUI.activeTarget ?? selector.CurrentAutoTarget;
            }
        }

        public RandomGazeConfig RandomConfig { get { return randomController != null ? randomController.Config : null; } }
        public AttentionConfig AttentionConfig { get { return attentionConfig; } }
        public BodyPartsConfig BodyPartsConfig { get { return bodyPartsConfig; } }

        public Action OnAutoReselect;

        public event Action<TargetGroup> OnGroupSwitched
        {
            add { selector.OnGroupSwitched += value; }
            remove { selector.OnGroupSwitched -= value; }
        }

        public float GazeAversionDownChanceVal { get { return gazeHandler.DownChanceVal; } }
        public float GazeAversionSideChanceVal { get { return gazeHandler.SideChanceVal; } }
        public float GazeAversionDownAngleVal { get { return gazeHandler.DownAngleVal; } }
        public float GazeAversionSideAngleVal { get { return gazeHandler.SideAngleVal; } }

        private float velocityH = 0.0f;
        private float velocityV = 0.0f;

        // Поля для хранения "чистого" угла головы без микро-офсетов
        private float baseHeadYaw = 0.0f;
        private float baseHeadPitch = 0.0f;

        // Поля для диагностических логов
        private float _lastCustomAimH = 0f;
        private float _lastCustomAimV = 0f;

        private TargetSelector selector;
        private GazeAversionHandler gazeHandler;
        private HeadMicroMovement microMovement;

        private float currentManualSpeed;
        private bool isGlanceMode = false;

        private float currentBoostUp = 1.0f;
        private float currentBoostDown = 1.0f;
        private float currentBoostH = 1.0f;
        private Atom lastChainAtomForBoost = null;
        private int lastChainPartForBoost = -1;
        private Transform lastChainTransformForBoost = null;

        private const float BoostH_Min = 1.5f;
        private const float BoostH_Max = 3.5f;
        private const float BoostUp_Min = 2.0f;
        private const float BoostUp_Max = 3.0f;
        private const float BoostDown_Min = 1.2f;
        private const float BoostDown_Max = 2.0f;

        private bool waitingForContact = false;
        private float contactStableTimer = 0f;
        private const float ContactThresholdDeg = 3f;
        private const float ContactStableDuration = 0.15f;
        private float contactWaitTimer = 0f;
        private const float MaxContactWaitTime = 2.0f;

        private bool directTurnScheduled = false;
        private float directTurnTimer = 0f;
        private bool directTurnSpeedActive = false;
        private float directTurnSpeed = 5f;
        private float directTurnScatterH = 0f;
        private float directTurnScatterV = 0f;

        private Vector3 savedHeadUpRef = Vector3.up;
        private bool headUpSaved = false;
        private Quaternion savedHeadLocalRotation = Quaternion.identity;
        private bool wasExternalControl = false;
        private float externalOverrideTimer = 0f;

        private float randomRegenTimer = 0f;
        private float randomValidityTimer = 0f;
        private const float RandomValidityInterval = 0.3f;
        private float lastRandomGlanceStartTime = 0f;
        private const float RandomGlanceMinBeforeInterrupt = 0.6f;
        private const float SharpMovementTimeThreshold = 0.15f;

        public HeadController(MVRScript owner, Atom owningAtom, BehaviorHeadUI headUI)
        {
            script = owner;
            atom = owningAtom;
            this.headUI = headUI;

            if (atom != null)
            {
                headControl = atom.GetStorableByID("headControl") as FreeControllerV3;
                chestControl = atom.GetStorableByID("chestControl") as FreeControllerV3;
                abdomenControl = atom.GetStorableByID("abdomenControl") as FreeControllerV3;
                abdomen2Control = atom.GetStorableByID("abdomen2Control") as FreeControllerV3;
                eyeTargetControl = atom.GetStorableByID("eyeTargetControl") as FreeControllerV3;
            }

            selector = new TargetSelector(headUI, headControl, chestControl);
            gazeHandler = new GazeAversionHandler(headControl);
            microMovement = new HeadMicroMovement();

            attentionConfig = new AttentionConfig();
            bodyPartsConfig = new BodyPartsConfig();
            attentionSystem = new AttentionSystem(attentionConfig, bodyPartsConfig);
            if (atom != null) attentionSystem.SetSelfAtom(atom);

            randomController = new RandomGazeController();
            if (atom != null) randomController.SetSelfAtom(atom);
            randomController.SetAttentionSystem(attentionSystem);

            chainSystem = new GlanceChainSystem(attentionSystem, randomController);

            RegisterRandomConfigStorables();
            RegisterAttentionConfigStorables();
            RegisterBodyPartsConfigStorables();
        }

        public void SetEyesController(EyesController ctrl)
        {
            eyesCtrl = ctrl;
            gazeHandler.SetEyesController(ctrl);
        }

        public void NotifyGroupManuallySelected(TargetGroup grp)
        {
            selector.NotifyGroupManuallySelected(grp);
        }

        public void SetManualTarget(TargetDef t)
        {
            if (t != null) currentManualSpeed = (t.speedMin == t.speedMax) ? t.speedMin : UnityEngine.Random.Range(t.speedMin, t.speedMax);

            if (t != null && t.isRandom && randomController != null)
            {
                Transform reference = chestControl != null ? chestControl.transform : (headControl != null ? headControl.transform.parent : null);
                Vector3 origin = headControl != null ? headControl.transform.position : Vector3.zero;
                float maxH = headMaxAngleH != null ? headMaxAngleH.val : 120f;
                float maxV = headMaxAngleV != null ? headMaxAngleV.val : 55f;
                randomController.GenerateTarget(t, reference, origin, maxH, maxV);
                randomRegenTimer = GetRandomTargetDuration(t);
                lastRandomGlanceStartTime = Time.time;
            }

            float eyesOnlyChance;
            if (t != null && t.isRandom && randomController != null)
            {
                eyesOnlyChance = randomController.Config.eyesOnlyChance.val;
            }
            else if (eyesCtrl != null)
            {
                eyesOnlyChance = eyesCtrl.EyesOnlyChance;
            }
            else
            {
                eyesOnlyChance = 0f;
            }

            if (eyesOnlyChance > 0f)
                isGlanceMode = UnityEngine.Random.value < eyesOnlyChance;
            else
                isGlanceMode = false;

            RollBoost();

            waitingForContact = false;
            directTurnScheduled = false;
            directTurnSpeedActive = false;
            contactWaitTimer = 0f;
            selector.ResetAngleCheck();
            velocityH = 0f;
            velocityV = 0f;
            gazeHandler.ResetAll();
            selector.ResolveActiveGroup();
            selector.RollScatterOffset(t);
        }

        public void SetExternalOverrideTarget(TargetDef t)
        {
            SetManualTarget(t);
            headUI.activeTarget = t;
            headUI.isExternalOverride = true;
            float duration = UnityEngine.Random.Range(t.durationMin, t.durationMax);
            externalOverrideTimer = duration;
        }

        private void RollBoost()
        {
            float t = UnityEngine.Random.value * UnityEngine.Random.value;
            currentBoostH = BoostH_Min + (BoostH_Max - BoostH_Min) * t;

            t = UnityEngine.Random.value * UnityEngine.Random.value;
            currentBoostUp = BoostUp_Min + (BoostUp_Max - BoostUp_Min) * t;

            t = UnityEngine.Random.value * UnityEngine.Random.value;
            currentBoostDown = BoostDown_Min + (BoostDown_Max - BoostDown_Min) * t;
        }

        public void NotifyNewTarget(TargetDef target)
        {
            if (target != null && target.isRandom && randomController != null)
            {
                Transform reference = chestControl != null ? chestControl.transform : (headControl != null ? headControl.transform.parent : null);
                Vector3 origin = headControl != null ? headControl.transform.position : Vector3.zero;
                float maxH = headMaxAngleH != null ? headMaxAngleH.val : 120f;
                float maxV = headMaxAngleV != null ? headMaxAngleV.val : 55f;
                randomController.GenerateTarget(target, reference, origin, maxH, maxV);
                randomRegenTimer = GetRandomTargetDuration(target);
                lastRandomGlanceStartTime = Time.time;
            }

            float eyesOnlyChance;
            if (target != null && target.isRandom && randomController != null)
            {
                eyesOnlyChance = randomController.Config.eyesOnlyChance.val;
            }
            else if (eyesCtrl != null)
            {
                eyesOnlyChance = eyesCtrl.EyesOnlyChance;
            }
            else
            {
                eyesOnlyChance = 0f;
            }

            if (eyesOnlyChance > 0f)
                isGlanceMode = UnityEngine.Random.value < eyesOnlyChance;
            else
                isGlanceMode = false;

            RollBoost();

            directTurnScheduled = false;
            directTurnSpeedActive = false;
            waitingForContact = false;
            contactWaitTimer = 0f;
            selector.ResetAngleCheck();
            velocityH = 0f;
            velocityV = 0f;
            gazeHandler.ResetAll();
            selector.ResolveActiveGroup();
            selector.RollScatterOffset(target);

            if (eyesCtrl != null) eyesCtrl.OnTargetSwitch();
        }

        public void ForceAutoReselect()
        {
            headUI.activeTarget = null;
            headUI.isExternalOverride = false;
            externalOverrideTimer = 0f;
            randomRegenTimer = 0f;
            randomValidityTimer = 0f;
            isGlanceMode = false;

            selector.Reset();
            waitingForContact = false;
            directTurnScheduled = false;
            directTurnSpeedActive = false;
            contactWaitTimer = 0f;
            velocityH = 0f;
            velocityV = 0f;
            baseHeadYaw = 0f;
            baseHeadPitch = 0f;

            gazeHandler.ResetAll();
            selector.ResolveActiveGroup();
            selector.InitGroupSwitchTimer();

            if (chainSystem != null) chainSystem.Reset();
            if (OnAutoReselect != null) OnAutoReselect();
        }

        public void ResetAutoTarget()
        {
            selector.Reset();
            waitingForContact = false;
            directTurnScheduled = false;
            directTurnSpeedActive = false;
            contactWaitTimer = 0f;
            randomRegenTimer = 0f;
            randomValidityTimer = 0f;
            velocityH = 0f;
            velocityV = 0f;
            baseHeadYaw = 0f;
            baseHeadPitch = 0f;

            gazeHandler.ResetAll();
            selector.ResolveActiveGroup();
            selector.InitGroupSwitchTimer();

            if (chainSystem != null) chainSystem.Reset();
            if (OnAutoReselect != null) OnAutoReselect();
        }

        public void RefreshCurrentSpeed(TargetDef t)
        {
            if (t == null) return;

            if (headUI.activeTarget == t)
                currentManualSpeed = (t.speedMin == t.speedMax) ? t.speedMin : UnityEngine.Random.Range(t.speedMin, t.speedMax);
            else if (t == selector.CurrentAutoTarget)
                selector.RefreshAutoSpeed(t);
        }

        public Vector3 GetRandomTargetPosition(TargetDef def)
        {
            if (def == null || !def.isRandom) return Vector3.zero;
            return selector.GetTargetPosition(def);
        }

        private float GetRandomTargetDuration(TargetDef def)
        {
            if (def == null) return 1f;
            if (def.glanceDuration > 0.05f) return def.glanceDuration;
            return UnityEngine.Random.Range(def.durationMin, def.durationMax);
        }

        public Vector3 GetActiveTargetPosition()
        {
            TargetDef activeDef = headUI.activeTarget ?? selector.CurrentAutoTarget;
            if (activeDef == null) return Vector3.zero;
            return selector.GetTargetPosition(activeDef);
        }

        public void ShowUI()
        {
            var pair = BHUI.SetupSliderFloat(script, "Head Speed", 5f, 0.1f, 20f, false);
            headSpeed = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Head Max Angle H", 120f, 0f, 120f, false);
            headMaxAngleH = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Head Max Angle V", 55f, 0f, 90f, false);
            headMaxAngleV = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Head Angle Offset H", 0f, -20f, 20f, false);
            headAngleOffsetH = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Head Angle Offset V", -2f, -30f, 30f, false);
            headAngleOffsetV = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Head Verticality", 1f, 0f, 1f, false);
            headVerticality = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            headVerticalityAdaptive = new JSONStorableBool("Head Verticality Adaptive", true);
            headVerticalityAdaptive.storeType = JSONStorableParam.StoreType.Full;
            script.RegisterBool(headVerticalityAdaptive);
            var adaptiveToggle = script.CreateToggle(headVerticalityAdaptive, false);
            uiObjects.Add(adaptiveToggle.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Head Micro Intensity H", 0.20f, 0f, 0.5f, false);
            headMicroIntensityH = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Head Micro Intensity V", 0.10f, 0f, 0.5f, false);
            headMicroIntensityV = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            var minPair = BHUI.SetupSliderFloat(script, "Micro Pause Min", 1.0f, 0f, 10f, false);
            var maxPair = BHUI.SetupSliderFloat(script, "Micro Pause Max", 5.0f, 0f, 10f, false);
            headMicroPauseMin = minPair.storableFloat;
            headMicroPauseMax = maxPair.storableFloat;

            headMicroPauseMin.setCallbackFunction = v => { if (v > headMicroPauseMax.val) headMicroPauseMax.val = v; };
            headMicroPauseMax.setCallbackFunction = v => { if (v < headMicroPauseMin.val) headMicroPauseMin.val = v; };

            uiObjects.Add(minPair.uiSlider.gameObject);
            uiObjects.Add(maxPair.uiSlider.gameObject);

            var brPair = BHUI.SetupSliderFloat(script, "Breathing", 0.5f, 0f, 1f, false);
            breathing = brPair.storableFloat;
            uiObjects.Add(brPair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Head Turn Chance", 0.5f, 0f, 1f, true);
            headTurnChance = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            var turnMinPair = BHUI.SetupSliderFloat(script, "Head Turn Delay Min", 0.15f, 0f, 3f, true);
            var turnMaxPair = BHUI.SetupSliderFloat(script, "Head Turn Delay Max", 1.3f, 0f, 3f, true);
            headTurnDelayMin = turnMinPair.storableFloat;
            headTurnDelayMax = turnMaxPair.storableFloat;

            headTurnDelayMin.setCallbackFunction = v => { if (v > headTurnDelayMax.val) headTurnDelayMax.val = v; };
            headTurnDelayMax.setCallbackFunction = v => { if (v < headTurnDelayMin.val) headTurnDelayMin.val = v; };

            uiObjects.Add(turnMinPair.uiSlider.gameObject);
            uiObjects.Add(turnMaxPair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Gaze Aversion Down Chance", 0.2f, 0f, 1f, true);
            gazeAversionDownChance = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Gaze Aversion Side Chance", 0.2f, 0f, 1f, true);
            gazeAversionSideChance = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Gaze Aversion Down Angle", 12f, 0f, 45f, true);
            gazeAversionDownAngle = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Gaze Aversion Side Angle", 7f, 0f, 30f, true);
            gazeAversionSideAngle = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Gaze Aversion Delay", 0.60f, 0f, 1f, true);
            gazeAversionDelay = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            selector.SetAngleLimits(headMaxAngleH, headMaxAngleV);
            gazeHandler.SetParams(gazeAversionDownChance, gazeAversionSideChance, gazeAversionDownAngle, gazeAversionSideAngle, gazeAversionDelay);
            microMovement.SetParams(headMicroIntensityH, headMicroIntensityV, headMicroPauseMin, headMicroPauseMax, breathing);
        }

        public void SetVisible(bool visible)
        {
            foreach (var go in uiObjects) if (go != null) go.SetActive(visible);
        }

        private void SmoothReturnToHome(float deltaTime)
        {
            if (headControl == null || !headUpSaved) return;

            Transform reference = chestControl != null ? chestControl.transform : headControl.transform.parent;
            if (reference == null) reference = headControl.transform;

            Vector3 currentDir = reference.InverseTransformDirection(headControl.transform.forward);
            float horizontalMagnitude = new Vector2(currentDir.x, currentDir.z).magnitude;
            float currentHDeg = horizontalMagnitude < SingularThreshold ? lastStableAimHDeg : Mathf.Atan2(currentDir.x, currentDir.z) * Mathf.Rad2Deg;
            float currentVDeg = Mathf.Atan2(currentDir.y, horizontalMagnitude) * Mathf.Rad2Deg;

            float aimHDeg = 0f;
            float aimVDeg = 0f;

            float speed = headSpeed != null ? headSpeed.val : 5f;
            float smoothTime = 1.0f / Mathf.Max(speed, 0.001f);

            float newHDeg = Mathf.SmoothDampAngle(currentHDeg, aimHDeg, ref velocityH, smoothTime, Mathf.Infinity, deltaTime);
            float newVDeg = Mathf.SmoothDampAngle(currentVDeg, aimVDeg, ref velocityV, smoothTime, Mathf.Infinity, deltaTime);

            float finalHDeg = Mathf.Clamp(newHDeg, -headMaxAngleH.val, headMaxAngleH.val);
            float finalVDeg = Mathf.Clamp(newVDeg, -headMaxAngleV.val, headMaxAngleV.val);

            if (finalHDeg >= headMaxAngleH.val && velocityH > 0f) velocityH = 0f;
            if (finalHDeg <= -headMaxAngleH.val && velocityH < 0f) velocityH = 0f;
            if (finalVDeg >= headMaxAngleV.val && velocityV > 0f) velocityV = 0f;
            if (finalVDeg <= -headMaxAngleV.val && velocityV < 0f) velocityV = 0f;

            float finalHRad = finalHDeg * Mathf.Deg2Rad;
            float finalVRad = finalVDeg * Mathf.Deg2Rad;
            Vector3 newDir = new Vector3(Mathf.Sin(finalHRad) * Mathf.Cos(finalVRad), Mathf.Sin(finalVRad), Mathf.Cos(finalHRad) * Mathf.Cos(finalVRad));
            newDir = reference.TransformDirection(newDir);

            Vector3 up = reference.TransformDirection(savedHeadUpRef);
            headControl.transform.rotation = Quaternion.LookRotation(newDir, up);
        }

        public void Update(float deltaTime)
        {
            if (headControl == null) return;

            if (attentionSystem != null)
                attentionSystem.Update(deltaTime);

            if (randomController != null)
                randomController.Update(deltaTime);

            Transform headTransform = headControl.transform;
            Transform reference = chestControl != null ? chestControl.transform : headTransform.parent;
            if (reference == null) reference = headTransform;

            TargetDef baseTarget = headUI.activeTarget ?? selector.CurrentAutoTarget;

            if (chainSystem != null)
            {
                float maxH = headMaxAngleH != null ? headMaxAngleH.val : 120f;
                float maxV = headMaxAngleV != null ? headMaxAngleV.val : 55f;
                chainSystem.Update(deltaTime, baseTarget, headTransform, reference, maxH, maxV);
            }

            if (headUI.externalControl)
            {
                wasExternalControl = true;
                return;
            }

            if (wasExternalControl)
            {
                wasExternalControl = false;
                velocityH = 0f;
                velocityV = 0f;
                baseHeadYaw = 0f;
                baseHeadPitch = 0f;
            }

            if (!headUI.gazeEnabled)
            {
                SmoothReturnToHome(deltaTime);
                return;
            }

            if (headUI.isExternalOverride && headUI.activeTarget != null)
            {
                externalOverrideTimer -= deltaTime;
                if (externalOverrideTimer <= 0f)
                {
                    ForceAutoReselect();
                }
            }

            if (headUI.activeTarget != null)
            {
                bool stillExists = false;
                foreach (var grp in headUI.targetGroups)
                {
                    if (grp.targets.Contains(headUI.activeTarget) && selector.IsTargetValid(headUI.activeTarget))
                    {
                        stillExists = true;
                        break;
                    }
                }

                if (!stillExists)
                {
                    ForceAutoReselect();
                    return;
                }
            }

            if (headUI.activeTarget == null)
            {
                float nts = selector.NextTargetSwitchTime;
                if (float.IsInfinity(nts) || nts < 0f || nts > Time.time + 60f)
                {
                    if (!waitingForContact) ResetAutoTarget();
                }
            }

            if (headUI.activeTarget != null)
            {
                selector.OnManualTargetSet();
                waitingForContact = false;
                directTurnScheduled = false;
                directTurnSpeedActive = false;
                contactWaitTimer = 0f;
                gazeHandler.Reset();
            }
            else
            {
                bool groupSwitched;
                TargetDef oldAutoTarget = selector.CurrentAutoTarget;
                TargetDef newTarget = selector.Update(deltaTime, waitingForContact, out groupSwitched);

                if (groupSwitched)
                {
                    waitingForContact = false;
                    contactWaitTimer = 0f;
                    directTurnScheduled = false;
                    directTurnSpeedActive = false;
                    velocityH = 0f;
                    velocityV = 0f;
                    baseHeadYaw = 0f;
                    baseHeadPitch = 0f;
                    gazeHandler.ResetAll();
                }

                if (newTarget != null)
                {
                    if (attentionSystem != null && oldAutoTarget != null && oldAutoTarget.isRandom && oldAutoTarget.randomCachedAtom != null)
                    {
                        attentionSystem.RecordGlance(oldAutoTarget.randomCachedAtom, oldAutoTarget.selectedBodyPart);
                    }

                    NotifyNewTarget(newTarget);
                    waitingForContact = true;
                    contactStableTimer = 0f;
                    contactWaitTimer = 0f;
                }
            }

            if (waitingForContact && headUI.activeTarget == null && selector.CurrentAutoTarget == null)
            {
                waitingForContact = false;
                contactStableTimer = 0f;
                contactWaitTimer = 0f;
            }

            if (!headUpSaved)
            {
                savedHeadUpRef = reference.InverseTransformDirection(headTransform.up);
                savedHeadLocalRotation = Quaternion.Inverse(reference.rotation) * headTransform.rotation;
                headUpSaved = true;
            }

            TargetDef chainOverride = chainSystem != null ? chainSystem.GetCurrentOverride() : null;
            TargetDef activeDef = chainOverride ?? headUI.activeTarget ?? selector.CurrentAutoTarget;
            bool isChainActive = chainOverride != null;

            if (directTurnSpeedActive && (isChainActive || gazeHandler.IsActive)) directTurnSpeedActive = false;

            if (isChainActive)
            {
                if (chainOverride.randomCachedAtom != lastChainAtomForBoost ||
                    chainOverride.selectedBodyPart != lastChainPartForBoost ||
                    chainOverride.randomCachedTransform != lastChainTransformForBoost)
                {
                    lastChainAtomForBoost = chainOverride.randomCachedAtom;
                    lastChainPartForBoost = chainOverride.selectedBodyPart;
                    lastChainTransformForBoost = chainOverride.randomCachedTransform;
                    RollBoost();
                }
            }
            else
            {
                lastChainAtomForBoost = null;
                lastChainPartForBoost = -1;
                lastChainTransformForBoost = null;
            }

            if (activeDef == null)
            {
                SmoothReturnToHome(deltaTime);
                return;
            }

            if (activeDef.isRandom && headUI.activeTarget != null && randomController != null && !isChainActive)
            {
                randomRegenTimer -= deltaTime;
                if (randomRegenTimer <= 0f)
                {
                    if (attentionSystem != null && activeDef.randomCachedAtom != null)
                    {
                        attentionSystem.RecordGlance(activeDef.randomCachedAtom, activeDef.selectedBodyPart);
                    }

                    Vector3 regenOrigin = headTransform.position;
                    float regenMaxH = headMaxAngleH != null ? headMaxAngleH.val : 120f;
                    float regenMaxV = headMaxAngleV != null ? headMaxAngleV.val : 55f;
                    randomController.GenerateTarget(activeDef, reference, regenOrigin, regenMaxH, regenMaxV);
                    randomRegenTimer = GetRandomTargetDuration(activeDef);
                    lastRandomGlanceStartTime = Time.time;
                }
            }

            if (activeDef.isRandom && randomController != null && !isChainActive)
            {
                randomValidityTimer -= deltaTime;
                if (randomValidityTimer <= 0f)
                {
                    randomValidityTimer = RandomValidityInterval;

                    Vector3 checkOrigin = headTransform.position;
                    float checkMaxH = headMaxAngleH != null ? headMaxAngleH.val : 120f;
                    float checkMaxV = headMaxAngleV != null ? headMaxAngleV.val : 55f;

                    if (randomController.IsLiveTargetOutOfRange(activeDef, reference, checkOrigin, checkMaxH, checkMaxV))
                    {
                        if (attentionSystem != null && activeDef.randomCachedAtom != null)
                        {
                            attentionSystem.RecordGlance(activeDef.randomCachedAtom, activeDef.selectedBodyPart);
                        }

                        randomController.GenerateTarget(activeDef, reference, checkOrigin, checkMaxH, checkMaxV);

                        if (headUI.activeTarget != null)
                            randomRegenTimer = GetRandomTargetDuration(activeDef);
                    }
                }
            }

            Vector3 lookTarget = headTransform.position + reference.forward;
            float currentSpeed = headSpeed.val;
            bool useCustomAim = false;
            float customAimHDeg = 0f;
            float customAimVDeg = 0f;

            if (activeDef != null)
            {
                float targetSpeed = (activeDef.speedMin == activeDef.speedMax) ? headSpeed.val :
                    ((activeDef == selector.CurrentAutoTarget) ? selector.CurrentAutoSpeed : currentManualSpeed);

                if (directTurnSpeedActive) currentSpeed = directTurnSpeed;
                else currentSpeed = targetSpeed;

                if ((isGlanceMode || isChainActive) && !activeDef.isHold)
                {
                    Vector3 targetPos = selector.GetTargetPosition(activeDef);
                    Vector3 toTarget = targetPos - headTransform.position;

                    if (toTarget.sqrMagnitude > 0.0001f && eyesCtrl != null)
                    {
                        Vector3 targetDirRef = reference.InverseTransformDirection(toTarget.normalized);
                        float targetYawRef = Mathf.Atan2(targetDirRef.x, targetDirRef.z) * Mathf.Rad2Deg;
                        float targetPitchRef = Mathf.Atan2(targetDirRef.y, new Vector2(targetDirRef.x, targetDirRef.z).magnitude) * Mathf.Rad2Deg;

                        float maxEyeYaw = eyesCtrl.EyesMaxAngleHorizVal;
                        float maxEyePitchUp = eyesCtrl.EyesMaxAngleUpVal;
                        float maxEyePitchDown = eyesCtrl.EyesMaxAngleDownVal;

                        float comfortYawLimit = maxEyeYaw / currentBoostH;
                        float comfortPitchUpLimit = maxEyePitchUp / currentBoostUp;
                        float comfortPitchDownLimit = maxEyePitchDown / currentBoostDown;

                        // ВАРИАНТ A: используем базу вместо чтения из головы
                        float currentHeadYawRef = baseHeadYaw;
                        float currentHeadPitchRef = baseHeadPitch;

                        float desiredHeadYaw = Mathf.Clamp(currentHeadYawRef,
                            targetYawRef - comfortYawLimit,
                            targetYawRef + comfortYawLimit);

                        float desiredHeadPitch = Mathf.Clamp(currentHeadPitchRef,
                            targetPitchRef - comfortPitchUpLimit,
                            targetPitchRef + comfortPitchDownLimit);

                        customAimHDeg = Mathf.Clamp(desiredHeadYaw, -headMaxAngleH.val, headMaxAngleH.val);
                        customAimVDeg = Mathf.Clamp(desiredHeadPitch, -headMaxAngleV.val, headMaxAngleV.val);
                        useCustomAim = true;

                        // Сохраняем для логов и обновления базы
                        _lastCustomAimH = customAimHDeg;
                        _lastCustomAimV = customAimVDeg;
                    }
                    else { lookTarget = headTransform.position + reference.forward; }
                }
                else if (!activeDef.isHold) { lookTarget = selector.GetTargetPosition(activeDef); }
            }

            if (headUI.externalControl && eyeTargetControl != null) lookTarget = eyeTargetControl.transform.position;

            float aimHDeg;
            float aimVDeg;
            float avH, avV;

            if (!isChainActive && gazeHandler.TryGetAimAngles(out avH, out avV))
            {
                aimHDeg = avH;
                aimVDeg = avV;
            }
            else if (activeDef.isHold)
            {
                aimHDeg = activeDef.anchorYaw;
                aimVDeg = activeDef.anchorPitch;
            }
            else if (useCustomAim)
            {
                aimHDeg = customAimHDeg;
                aimVDeg = customAimVDeg;
            }
            else
            {
                Vector3 targetDir = reference.InverseTransformDirection((lookTarget - headTransform.position).normalized);
                float horizontalMagnitude = new Vector2(targetDir.x, targetDir.z).magnitude;

                if (horizontalMagnitude < SingularThreshold)
                {
                    aimHDeg = lastStableAimHDeg;
                    aimVDeg = lastStableAimVDeg;
                }
                else
                {
                    float targetYawDeg = Mathf.Atan2(targetDir.x, targetDir.z) * Mathf.Rad2Deg;
                    float targetPitchDeg = Mathf.Atan2(targetDir.y, horizontalMagnitude) * Mathf.Rad2Deg;

                    aimHDeg = targetYawDeg;
                    aimVDeg = targetPitchDeg;

                    lastStableAimHDeg = aimHDeg;
                    lastStableAimVDeg = aimVDeg;
                }
            }

            if (directTurnSpeedActive)
            {
                aimHDeg += directTurnScatterH;
                aimVDeg += directTurnScatterV;
            }

            aimHDeg += headAngleOffsetH.val;
            aimVDeg += headAngleOffsetV.val;

            aimHDeg = Mathf.Clamp(aimHDeg, -headMaxAngleH.val, headMaxAngleH.val);
            aimVDeg = Mathf.Clamp(aimVDeg, -headMaxAngleV.val, headMaxAngleV.val);

            if (waitingForContact && activeDef != null && !isChainActive)
            {
                Vector3 currentDir = reference.InverseTransformDirection(headTransform.forward);
                float currentYawDeg = Mathf.Atan2(currentDir.x, currentDir.z) * Mathf.Rad2Deg;
                float currentPitchDeg = Mathf.Atan2(currentDir.y, new Vector2(currentDir.x, currentDir.z).magnitude) * Mathf.Rad2Deg;

                float yawDiff = Mathf.Abs(Mathf.DeltaAngle(currentYawDeg, aimHDeg));
                float pitchDiff = Mathf.Abs(Mathf.DeltaAngle(currentPitchDeg, aimVDeg));

                if (yawDiff <= ContactThresholdDeg && pitchDiff <= ContactThresholdDeg)
                {
                    contactStableTimer += deltaTime;
                    if (contactStableTimer >= ContactStableDuration)
                    {
                        waitingForContact = false;
                        selector.NextTargetSwitchTime = Time.time + GetRandomTargetDuration(activeDef);
                        gazeHandler.HandleContact(activeDef, reference, headTransform);

                        if (!gazeHandler.IsPending && !gazeHandler.IsActive && !gazeHandler.IsWaitingForMutualGaze)
                            ScheduleDirectTurnIfNeeded(activeDef);
                    }
                }
                else { contactStableTimer = 0f; }

                contactWaitTimer += deltaTime;
                if (contactWaitTimer >= MaxContactWaitTime)
                {
                    waitingForContact = false;
                    selector.NextTargetSwitchTime = Time.time + GetRandomTargetDuration(activeDef);
                    gazeHandler.HandleContact(activeDef, reference, headTransform);

                    if (!gazeHandler.IsPending && !gazeHandler.IsActive && !gazeHandler.IsWaitingForMutualGaze)
                        ScheduleDirectTurnIfNeeded(activeDef);
                }
            }

            bool directTurnFromMutual = !isChainActive && gazeHandler.Update(deltaTime, activeDef, reference, headTransform, selector.CurrentTargetDuration);
            if (directTurnFromMutual) ScheduleDirectTurnIfNeeded(activeDef);

            if (directTurnScheduled)
            {
                directTurnTimer -= deltaTime;
                if (directTurnTimer <= 0f)
                {
                    directTurnScheduled = false;

                    if (UnityEngine.Random.value < headTurnChance.val)
                    {
                        isGlanceMode = false;
                        selector.NextTargetSwitchTime = Time.time + GetRandomTargetDuration(activeDef);
                        gazeHandler.ResetAll();

                        directTurnSpeed = UnityEngine.Random.Range(currentSpeed * DirectTurnSpeedMultiplierMin, currentSpeed);
                        directTurnScatterH = UnityEngine.Random.Range(-8f, 8f);
                        directTurnScatterV = UnityEngine.Random.Range(-8f, 8f);
                        directTurnSpeedActive = true;
                    }
                }
            }

            bool skipMicro = activeDef != null && activeDef.isHold;
            float microH, microV, breathOffset;
            microMovement.Update(deltaTime, skipMicro, out microH, out microV, out breathOffset);

            float aimWithMicroH = Mathf.Clamp(aimHDeg + microH, -headMaxAngleH.val, headMaxAngleH.val);
            float aimWithMicroV = Mathf.Clamp(aimVDeg + microV + breathOffset, -headMaxAngleV.val, headMaxAngleV.val);

            Vector3 actualDirNow = reference.InverseTransformDirection(headTransform.forward);
            float actualHDegNow = Mathf.Atan2(actualDirNow.x, actualDirNow.z) * Mathf.Rad2Deg;
            float actualVDegNow = Mathf.Atan2(actualDirNow.y, new Vector2(actualDirNow.x, actualDirNow.z).magnitude) * Mathf.Rad2Deg;

            float finalHDeg;
            float finalVDeg;

            if (directTurnSpeedActive)
            {
                float tHRad = aimWithMicroH * Mathf.Deg2Rad;
                float tVRad = aimWithMicroV * Mathf.Deg2Rad;
                Vector3 targetDir = new Vector3(
                    Mathf.Sin(tHRad) * Mathf.Cos(tVRad),
                    Mathf.Sin(tVRad),
                    Mathf.Cos(tHRad) * Mathf.Cos(tVRad));

                float angleError = Vector3.Angle(actualDirNow, targetDir);
                float stepFraction = Mathf.Min(currentSpeed * deltaTime, 0.25f);

                Vector3 turnedDir = Vector3.RotateTowards(actualDirNow, targetDir, angleError * stepFraction * Mathf.Deg2Rad, 0f);

                finalHDeg = Mathf.Clamp(Mathf.Atan2(turnedDir.x, turnedDir.z) * Mathf.Rad2Deg, -headMaxAngleH.val, headMaxAngleH.val);
                finalVDeg = Mathf.Clamp(Mathf.Atan2(turnedDir.y, new Vector2(turnedDir.x, turnedDir.z).magnitude) * Mathf.Rad2Deg, -headMaxAngleV.val, headMaxAngleV.val);

                velocityH = 0f;
                velocityV = 0f;
            }
            else
            {
                float smoothTime = 1.0f / Mathf.Max(currentSpeed, 0.001f);

                float newHDeg = Mathf.SmoothDampAngle(actualHDegNow, aimWithMicroH, ref velocityH, smoothTime, Mathf.Infinity, deltaTime);
                float newVDeg = Mathf.SmoothDampAngle(actualVDegNow, aimWithMicroV, ref velocityV, smoothTime, Mathf.Infinity, deltaTime);

                finalHDeg = Mathf.Clamp(newHDeg, -headMaxAngleH.val, headMaxAngleH.val);
                finalVDeg = Mathf.Clamp(newVDeg, -headMaxAngleV.val, headMaxAngleV.val);

                if (finalHDeg >= headMaxAngleH.val && velocityH > 0f) velocityH = 0f;
                if (finalHDeg <= -headMaxAngleH.val && velocityH < 0f) velocityH = 0f;
                if (finalVDeg >= headMaxAngleV.val && velocityV > 0f) velocityV = 0f;
                if (finalVDeg <= -headMaxAngleV.val && velocityV < 0f) velocityV = 0f;
            }

            float finalHRad = finalHDeg * Mathf.Deg2Rad;
            float finalVRad = finalVDeg * Mathf.Deg2Rad;
            Vector3 newDir = new Vector3(Mathf.Sin(finalHRad) * Mathf.Cos(finalVRad), Mathf.Sin(finalVRad), Mathf.Cos(finalHRad) * Mathf.Cos(finalVRad));
            newDir = reference.TransformDirection(newDir);

            Vector3 up = reference.TransformDirection(savedHeadUpRef);

            float targetVertStrength = headVerticality != null ? headVerticality.val : 0f;
            if (targetVertStrength > 0.001f)
            {
                if (headVerticalityAdaptive != null && headVerticalityAdaptive.val)
                {
                    Vector3 upSum = Vector3.zero;
                    int upCount = 0;

                    if (abdomenControl != null) { upSum += abdomenControl.transform.up; upCount++; }
                    if (abdomen2Control != null) { upSum += abdomen2Control.transform.up; upCount++; }
                    if (reference != null) { upSum += reference.up; upCount++; }

                    Vector3 avgUp = upCount > 0 ? (upSum / upCount).normalized : Vector3.up;
                    float uprightness = Mathf.Clamp01(Vector3.Dot(avgUp, Vector3.up));
                    float adaptiveFactor = Mathf.Clamp01((uprightness - 0.2f) / 0.5f);
                    targetVertStrength *= adaptiveFactor;
                }
            }
            else { targetVertStrength = 0f; }

            float vertSmoothTime = 1.0f / Mathf.Max(currentSpeed, 0.001f);
            smoothedVertStrength = Mathf.SmoothDamp(smoothedVertStrength, targetVertStrength, ref vertStrengthVelocity, vertSmoothTime, Mathf.Infinity, deltaTime);

            if (smoothedVertStrength > 0.001f) up = Vector3.Slerp(up, Vector3.up, smoothedVertStrength).normalized;

            // ИСПРАВЛЕНИЕ 6: Обновляем базу из "чистой" цели, игнорируя инерцию SmoothDamp
            if (useCustomAim)
            {
                baseHeadYaw = _lastCustomAimH;
                baseHeadPitch = _lastCustomAimV;
            }
            else
            {
                baseHeadYaw = finalHDeg - microH;
                baseHeadPitch = finalVDeg - microV - breathOffset;
            }

            headTransform.rotation = Quaternion.LookRotation(newDir, up);
        }

        private void ScheduleDirectTurnIfNeeded(TargetDef def)
        {
            if (def == null || !def.headTurnEnabled) return;
            if (def.isHold) return;

            if (isGlanceMode && headTurnChance != null && headTurnDelayMin != null && headTurnDelayMax != null)
            {
                directTurnTimer = UnityEngine.Random.Range(headTurnDelayMin.val, headTurnDelayMax.val);
                directTurnScheduled = true;
            }
        }

        private static IEnumerator SmoothReturnCoroutine(Transform headTransform, Transform reference, Quaternion startRotation, Quaternion savedLocalRotation, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (headTransform == null || reference == null) yield break;

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                t = Mathf.SmoothStep(0f, 1f, t);

                Quaternion target = reference.rotation * savedLocalRotation;
                headTransform.rotation = Quaternion.Slerp(startRotation, target, t);

                yield return null;
            }

            if (headTransform != null && reference != null)
                headTransform.rotation = reference.rotation * savedLocalRotation;
        }

        public void Destroy()
        {
            DeregisterRandomConfigStorables();
            DeregisterAttentionConfigStorables();
            DeregisterBodyPartsConfigStorables();

            if (headControl != null && headUpSaved)
            {
                Transform reference = chestControl != null ? chestControl.transform : headControl.transform.parent;
                if (reference == null) reference = headControl.transform;

                Quaternion targetRotation = reference.rotation * savedHeadLocalRotation;
                Quaternion startRotation = headControl.transform.rotation;

                if (Quaternion.Angle(startRotation, targetRotation) < 0.5f)
                {
                    headControl.transform.rotation = targetRotation;
                    return;
                }

                float returnDuration = 3.5f / Mathf.Max(headSpeed != null ? headSpeed.val : 5f, 0.001f);

                if (SuperController.singleton != null)
                    SuperController.singleton.StartCoroutine(SmoothReturnCoroutine(headControl.transform, reference, startRotation, savedHeadLocalRotation, returnDuration));
                else
                    headControl.transform.rotation = targetRotation;
            }
        }

        private void RegisterRandomConfigStorables()
        {
            if (script == null || randomController == null) return;

            var cfg = randomController.Config;
            if (cfg == null) return;

            try
            {
                script.RegisterFloat(cfg.eyesOnlyChance);
                script.RegisterFloat(cfg.nothingWeight);
                script.RegisterFloat(cfg.selfGazeWeight);
                script.RegisterFloat(cfg.virtualTargetWeight);
                script.RegisterFloat(cfg.virtualDistance);
                script.RegisterFloat(cfg.upBias);
                script.RegisterFloat(cfg.downBias);
                script.RegisterBool(cfg.trackPlayer);
                script.RegisterFloat(cfg.personsWeight);
                script.RegisterFloat(cfg.headWeight);
                script.RegisterFloat(cfg.chestWeight);
                script.RegisterFloat(cfg.hipWeight);
                script.RegisterFloat(cfg.lHandWeight);
                script.RegisterFloat(cfg.rHandWeight);
                script.RegisterFloat(cfg.lArmWeight);
                script.RegisterFloat(cfg.rArmWeight);
            }
            catch (System.Exception e)
            {
                SuperController.LogError("[BehaviorHead] RegisterRandomConfigStorables error: " + e);
            }
        }

        private void DeregisterRandomConfigStorables()
        {
            if (script == null || randomController == null) return;

            var cfg = randomController.Config;
            if (cfg == null) return;

            try
            {
                script.DeregisterFloat(cfg.eyesOnlyChance);
                script.DeregisterFloat(cfg.nothingWeight);
                script.DeregisterFloat(cfg.selfGazeWeight);
                script.DeregisterFloat(cfg.virtualTargetWeight);
                script.DeregisterFloat(cfg.virtualDistance);
                script.DeregisterFloat(cfg.upBias);
                script.DeregisterFloat(cfg.downBias);
                script.DeregisterBool(cfg.trackPlayer);
                script.DeregisterFloat(cfg.personsWeight);
                script.DeregisterFloat(cfg.headWeight);
                script.DeregisterFloat(cfg.chestWeight);
                script.DeregisterFloat(cfg.hipWeight);
                script.DeregisterFloat(cfg.lHandWeight);
                script.DeregisterFloat(cfg.rHandWeight);
                script.DeregisterFloat(cfg.lArmWeight);
                script.DeregisterFloat(cfg.rArmWeight);
            }
            catch (System.Exception) { }
        }

        private void RegisterAttentionConfigStorables()
        {
            if (script == null || attentionConfig == null) return;

            try
            {
                script.RegisterFloat(attentionConfig.fatigueDelay);
                script.RegisterFloat(attentionConfig.minFatigueModifier);
                script.RegisterFloat(attentionConfig.attentionBoost);
                script.RegisterFloat(attentionConfig.monotonyWindow);
                script.RegisterFloat(attentionConfig.monotonyThreshold);
                script.RegisterFloat(attentionConfig.attentionDecayRate);
            }
            catch (System.Exception e)
            {
                SuperController.LogError("[BehaviorHead] RegisterAttentionConfigStorables error: " + e);
            }
        }

        private void DeregisterAttentionConfigStorables()
        {
            if (script == null || attentionConfig == null) return;

            try
            {
                script.DeregisterFloat(attentionConfig.fatigueDelay);
                script.DeregisterFloat(attentionConfig.minFatigueModifier);
                script.DeregisterFloat(attentionConfig.attentionBoost);
                script.DeregisterFloat(attentionConfig.monotonyWindow);
                script.DeregisterFloat(attentionConfig.monotonyThreshold);
                script.DeregisterFloat(attentionConfig.attentionDecayRate);
            }
            catch (System.Exception) { }
        }

        private void RegisterBodyPartsConfigStorables()
        {
            if (script == null || bodyPartsConfig == null) return;

            try
            {
                script.RegisterFloat(bodyPartsConfig.distanceNear);
                script.RegisterFloat(bodyPartsConfig.distanceFar);
                script.RegisterFloat(bodyPartsConfig.movementDecayTime);
            }
            catch (System.Exception e)
            {
                SuperController.LogError("[BehaviorHead] RegisterBodyPartsConfigStorables error: " + e);
            }
        }

        private void DeregisterBodyPartsConfigStorables()
        {
            if (script == null || bodyPartsConfig == null) return;

            try
            {
                script.DeregisterFloat(bodyPartsConfig.distanceNear);
                script.DeregisterFloat(bodyPartsConfig.distanceFar);
                script.DeregisterFloat(bodyPartsConfig.movementDecayTime);
            }
            catch (System.Exception) { }
        }
    }
}