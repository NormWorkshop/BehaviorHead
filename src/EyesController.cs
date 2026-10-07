using System;
using System.Collections.Generic;
using UnityEngine;
using BehaviorUtils;

namespace Norm
{
    public class EyesController
    {
        private MVRScript script;
        private Atom atom;
        private BehaviorHeadUI headUI;
        private HeadController headCtrl;
        private List<GameObject> uiObjects = new List<GameObject>();

        private JSONStorableFloat eyesSaccadeSpeed;
        private JSONStorableFloat eyesMaxAngleHoriz;
        private JSONStorableFloat eyesMaxAngleDown;
        private JSONStorableFloat eyesMaxAngleUp;
        private JSONStorableFloat convergeAdjust;
        private JSONStorableFloat microSaccadeAngle;
        private JSONStorableFloat microSaccadeIntervalMin;
        private JSONStorableFloat microSaccadeIntervalMax;
        private JSONStorableBool autoBlinkEnabled;
        private JSONStorableFloat blinkIntervalMin;
        private JSONStorableFloat blinkIntervalMax;
        private JSONStorableFloat blinkDurationMin;
        private JSONStorableFloat blinkDurationMax;
        private JSONStorableAction blinkNowAction;
        private JSONStorableFloat closeEyesAmount;
        private JSONStorableFloat closeEyesSlider;
        private JSONStorableFloat eyesOnlyChance;
        private JSONStorableFloat blinkOnTargetChance;
        private JSONStorableFloat pupilSize;

        public float EyesOnlyChance { get { return eyesOnlyChance != null ? eyesOnlyChance.val : 0f; } }
        public float EyesMaxAngleHorizVal { get { return eyesMaxAngleHoriz != null ? eyesMaxAngleHoriz.val : 25f; } }
        public float EyesMaxAngleDownVal { get { return eyesMaxAngleDown != null ? eyesMaxAngleDown.val : 17f; } }
        public float EyesMaxAngleUpVal { get { return eyesMaxAngleUp != null ? eyesMaxAngleUp.val : 12f; } }

        private FreeControllerV3 eyeTargetControl;
        private EyesControl eyeControl;
        private EyesControl.LookMode savedLookMode;
        private DAZMorph eyesClosedMorph;
        private DAZMorph pupilsMorph;
        private float savedPupilValue = 0f;
        private LookAtWithLimits lEyeLimits;
        private LookAtWithLimits rEyeLimits;
        private float savedLMaxRight, savedLMaxLeft, savedLMaxDown, savedLMaxUp;
        private float savedRMaxRight, savedRMaxLeft, savedRMaxDown, savedRMaxUp;
        private DAZMeshEyelidControl eyelidControl;
        private JSONStorableBool nativeBlinkEnabled;
        private bool nativeBlinkWasEnabled;
        private bool savedEyeTargetHidden;
        private float currentCloseValue = 0f;
        private const float BlinkDisableThreshold = 0.30f;
        private float blinkTimer = 0f;
        private float blinkDuration = 0f;
        private bool isBlinking = false;
        private float blinkStartValue = 0f;
        private float nextAutoBlinkTime = 0f;
        private float blinkCooldownEnd = 0f;
        private const float MinGapBetweenBlinks = 0.4f;
        private float _saccadeYaw = 0f;
        private float _saccadePitch = 0f;
        private float _nextSaccadeTime = 0f;
        private FreeControllerV3 headControl;
        private float gazeOffsetYaw = 0f;
        private float gazeOffsetPitch = 0f;
        private const float MinConvergenceDistance = 0.25f;
        private const float IPD = 0.064f;
        private Transform leftEyeTarget;
        private Transform rightEyeTarget;
        private GameObject leftEyeTargetGo;
        private GameObject rightEyeTargetGo;
        private Vector3 _leftEyeVelocity = Vector3.zero;
        private Vector3 _rightEyeVelocity = Vector3.zero;
        private Vector3 savedEyeTargetLocalPos = Vector3.zero;
        private bool savedEyeTargetPos = false;
        private FaceRig playerFaceRig;
        private const float PlayerRigEyesDistance = 0.024f;
        private const float PlayerRigMouthDistance = 0.0848f;
        private const float FaceVisibleDotThreshold = 0.3f;
        private const float WeightLEye = 0.35f;
        private const float WeightREye = 0.35f;
        private const float WeightMouth = 0.30f;
        private const float FaceFocusSaccadeAngle = 1.5f;
        private int currentFaceFocusPoint = 0;
        private float nextFaceFocusSwitchTime = 0f;
        private TargetDef lastFaceTarget = null;
        private const float HoldScanInterval = 0.2f;
        private const float HoldAversionLockMin = 0.2f;
        private const float HoldAversionLockMax = 0.8f;
        private const float HoldAversionAvertMin = 1.5f;
        private const float HoldAversionAvertMax = 3.8f;
        private float _holdScanTimer = 0f;
        private TargetDef _holdScanTarget = null;
        private bool _holdTracking = false;
        private bool _holdAverting = false;
        private float _holdAversionTimer = 0f;
        private float _holdAversionDuration = 0f;
        private Dictionary<Atom, TargetDef> _holdTempDefs = new Dictionary<Atom, TargetDef>();
        private TargetDef _holdPlayerDef = null;

        public EyesController(MVRScript owner, Atom owningAtom, BehaviorHeadUI headUI, HeadController headCtrl)
        {
            script = owner;
            atom = owningAtom;
            this.headUI = headUI;
            this.headCtrl = headCtrl;
            if (atom != null)
            {
                eyeTargetControl = atom.GetStorableByID("eyeTargetControl") as FreeControllerV3;
                if (eyeTargetControl != null)
                {
                    savedEyeTargetHidden = eyeTargetControl.hidden;
                    savedEyeTargetLocalPos = eyeTargetControl.transform.localPosition;
                    savedEyeTargetPos = true;
                }
                eyeControl = atom.GetStorableByID("Eyes") as EyesControl;
                if (eyeControl != null)
                {
                    savedLookMode = eyeControl.currentLookMode;
                }
                eyelidControl = atom.GetStorableByID("EyelidControl") as DAZMeshEyelidControl;
                if (eyelidControl != null)
                {
                    nativeBlinkEnabled = eyelidControl.GetBoolJSONParam("blinkEnabled");
                    nativeBlinkWasEnabled = nativeBlinkEnabled != null && nativeBlinkEnabled.val;
                    if (nativeBlinkEnabled != null) nativeBlinkEnabled.val = false;
                }
                DAZBone[] bones = atom.GetComponentsInChildren<DAZBone>();
                foreach (var bone in bones)
                {
                    if (bone.name == "lEye") lEyeLimits = bone.GetComponent<LookAtWithLimits>();
                    else if (bone.name == "rEye") rEyeLimits = bone.GetComponent<LookAtWithLimits>();
                }
                SaveEyeLimits();
                JSONStorable geometry = atom.GetStorableByID("geometry");
                DAZCharacterSelector dcs = geometry as DAZCharacterSelector;
                GenerateDAZMorphsControlUI morphUI = dcs != null ? dcs.morphsControlUI : null;
                if (morphUI != null)
                {
                    eyesClosedMorph = morphUI.GetMorphByDisplayName("Eyes Closed");
                    pupilsMorph = morphUI.GetMorphByDisplayName("Pupils Dialate");
                    if (pupilsMorph != null)
                        savedPupilValue = pupilsMorph.morphValue;
                }
                headControl = atom.GetStorableByID("headControl") as FreeControllerV3;
                leftEyeTargetGo = new GameObject("BehaviorHead_LeftEyeTarget");
                leftEyeTarget = leftEyeTargetGo.transform;
                rightEyeTargetGo = new GameObject("BehaviorHead_RightEyeTarget");
                rightEyeTarget = rightEyeTargetGo.transform;
                var cam = SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null ? SuperController.singleton.centerCameraTarget.transform : null;
                if (cam != null)
                    playerFaceRig = FaceRig.Create(cam, PlayerRigMouthDistance, PlayerRigEyesDistance);
            }
        }

        public void StartGazeOffset(float yawDeg, float pitchDeg)
        {
            gazeOffsetYaw = yawDeg;
            gazeOffsetPitch = pitchDeg;
        }

        public void StopGazeOffset()
        {
            if (Mathf.Abs(gazeOffsetYaw) < 0.001f && Mathf.Abs(gazeOffsetPitch) < 0.001f)
                return;
            gazeOffsetYaw = 0f;
            gazeOffsetPitch = 0f;
        }

        private void SaveEyeLimits()
        {
            if (lEyeLimits != null)
            {
                savedLMaxRight = lEyeLimits.MaxRight;
                savedLMaxLeft = lEyeLimits.MaxLeft;
                savedLMaxDown = lEyeLimits.MaxDown;
                savedLMaxUp = lEyeLimits.MaxUp;
            }
            if (rEyeLimits != null)
            {
                savedRMaxRight = rEyeLimits.MaxRight;
                savedRMaxLeft = rEyeLimits.MaxLeft;
                savedRMaxDown = rEyeLimits.MaxDown;
                savedRMaxUp = rEyeLimits.MaxUp;
            }
        }

        private void ApplyEyeLimits()
        {
            if (lEyeLimits == null || rEyeLimits == null) return;
            if (eyesMaxAngleHoriz == null || eyesMaxAngleDown == null || eyesMaxAngleUp == null) return;
            float h = eyesMaxAngleHoriz.val;
            float d = eyesMaxAngleDown.val;
            float u = eyesMaxAngleUp.val;
            lEyeLimits.MaxRight = h;
            lEyeLimits.MaxLeft = h;
            lEyeLimits.MaxDown = d;
            lEyeLimits.MaxUp = u;
            rEyeLimits.MaxRight = h;
            rEyeLimits.MaxLeft = h;
            rEyeLimits.MaxDown = d;
            rEyeLimits.MaxUp = u;
        }

        private void RestoreEyeLimits()
        {
            if (lEyeLimits != null)
            {
                lEyeLimits.MaxRight = savedLMaxRight;
                lEyeLimits.MaxLeft = savedLMaxLeft;
                lEyeLimits.MaxDown = savedLMaxDown;
                lEyeLimits.MaxUp = savedLMaxUp;
            }
            if (rEyeLimits != null)
            {
                rEyeLimits.MaxRight = savedRMaxRight;
                rEyeLimits.MaxLeft = savedRMaxLeft;
                rEyeLimits.MaxDown = savedRMaxDown;
                rEyeLimits.MaxUp = savedRMaxUp;
            }
        }

        public void ShowUI()
        {
            var pair = BHUI.SetupSliderFloat(script, "Saccade Speed (s)", 0.03f, 0.001f, 0.3f, false);
            eyesSaccadeSpeed = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Max Horizontal Angle", 25f, 0f, 80f, false);
            eyesMaxAngleHoriz = pair.storableFloat;
            eyesMaxAngleHoriz.setCallbackFunction = v => ApplyEyeLimits();
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Max Down Angle", 17f, 0f, 60f, false);
            eyesMaxAngleDown = pair.storableFloat;
            eyesMaxAngleDown.setCallbackFunction = v => ApplyEyeLimits();
            uiObjects.Add(pair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Max Up Angle", 12f, 0f, 60f, false);
            eyesMaxAngleUp = pair.storableFloat;
            eyesMaxAngleUp.setCallbackFunction = v => ApplyEyeLimits();
            uiObjects.Add(pair.uiSlider.gameObject);

            var caPair = BHUI.SetupSliderFloat(script, "Converge Adjust", 0f, -1f, 1f, false);
            convergeAdjust = caPair.storableFloat;
            uiObjects.Add(caPair.uiSlider.gameObject);

            pair = BHUI.SetupSliderFloat(script, "Saccade Angle", 2.5f, 0f, 5f, false);
            microSaccadeAngle = pair.storableFloat;
            uiObjects.Add(pair.uiSlider.gameObject);

            var minPair = BHUI.SetupSliderFloat(script, "Saccade Interval Min", 0.2f, 0f, 2f, false);
            var maxPair = BHUI.SetupSliderFloat(script, "Saccade Interval Max", 1.0f, 0f, 2f, false);
            microSaccadeIntervalMin = minPair.storableFloat;
            microSaccadeIntervalMax = maxPair.storableFloat;
            microSaccadeIntervalMin.setCallbackFunction = v => { if (v > microSaccadeIntervalMax.val) microSaccadeIntervalMax.val = v; };
            microSaccadeIntervalMax.setCallbackFunction = v => { if (v < microSaccadeIntervalMin.val) microSaccadeIntervalMin.val = v; };
            uiObjects.Add(minPair.uiSlider.gameObject);
            uiObjects.Add(maxPair.uiSlider.gameObject);

            var eoSlider = BHUI.SetupSliderFloat(script, "Eyes-Only Chance", 0.5f, 0f, 1f, false);
            eyesOnlyChance = eoSlider.storableFloat;
            uiObjects.Add(eoSlider.uiSlider.gameObject);

            var btSlider = BHUI.SetupSliderFloat(script, "Blink on Target Chance", 1.0f, 0f, 1f, false);
            blinkOnTargetChance = btSlider.storableFloat;
            uiObjects.Add(btSlider.uiSlider.gameObject);

            autoBlinkEnabled = new JSONStorableBool("Auto Blink Enabled", true, v =>
            {
                if (!v)
                {
                    isBlinking = false;
                    if (eyesClosedMorph != null) eyesClosedMorph.morphValue = 0f;
                }
                else
                {
                    nextAutoBlinkTime = Time.time + UnityEngine.Random.Range(blinkIntervalMin.val, blinkIntervalMax.val);
                }
                if (nativeBlinkEnabled != null && nativeBlinkEnabled.val)
                    nativeBlinkEnabled.val = false;
            });
            script.RegisterBool(autoBlinkEnabled);
            var autoBlinkToggle = script.CreateToggle(autoBlinkEnabled, true);
            uiObjects.Add(autoBlinkToggle.gameObject);

            var bMinI = BHUI.SetupSliderFloat(script, "Blink Interval Min (s)", 1f, 0.1f, 10f, true);
            var bMaxI = BHUI.SetupSliderFloat(script, "Blink Interval Max (s)", 7f, 0.1f, 10f, true);
            blinkIntervalMin = bMinI.storableFloat;
            blinkIntervalMax = bMaxI.storableFloat;
            blinkIntervalMin.setCallbackFunction = v => { if (v > blinkIntervalMax.val) blinkIntervalMax.val = v; };
            blinkIntervalMax.setCallbackFunction = v => { if (v < blinkIntervalMin.val) blinkIntervalMin.val = v; };
            uiObjects.Add(bMinI.uiSlider.gameObject);
            uiObjects.Add(bMaxI.uiSlider.gameObject);

            var bMinD = BHUI.SetupSliderFloat(script, "Blink Duration Min (s)", 0.1f, 0.05f, 2f, true);
            var bMaxD = BHUI.SetupSliderFloat(script, "Blink Duration Max (s)", 0.4f, 0.05f, 2f, true);
            blinkDurationMin = bMinD.storableFloat;
            blinkDurationMax = bMaxD.storableFloat;
            blinkDurationMin.setCallbackFunction = v => { if (v > blinkDurationMax.val) blinkDurationMax.val = v; };
            blinkDurationMax.setCallbackFunction = v => { if (v < blinkDurationMin.val) blinkDurationMin.val = v; };
            uiObjects.Add(bMinD.uiSlider.gameObject);
            uiObjects.Add(bMaxD.uiSlider.gameObject);

            blinkNowAction = new JSONStorableAction("Blink now", () =>
            {
                if (!autoBlinkEnabled.val) return;
                if (isBlinking) return;
                StartBlink();
            });
            script.RegisterAction(blinkNowAction);
            var blinkNowBtn = BHUI.SetupButton(script, "Blink now", () => { if (blinkNowAction.actionCallback != null) blinkNowAction.actionCallback.Invoke(); }, true);
            uiObjects.Add(blinkNowBtn.gameObject);

            closeEyesAmount = new JSONStorableFloat("Close Eyes Amount", 0.8f, 0f, 1f, true, true);
            script.RegisterFloat(closeEyesAmount);
            var ceSlider = BHUI.SetupSliderFloat(script, "Close Eyes Amount", 0.8f, 0f, 1f, true);
            closeEyesAmount = ceSlider.storableFloat;
            uiObjects.Add(ceSlider.uiSlider.gameObject);

            closeEyesSlider = new JSONStorableFloat("Close Eyes", 0f, 0f, 1f, true, true);
            script.RegisterFloat(closeEyesSlider);
            var csSlider = BHUI.SetupSliderFloat(script, "Close Eyes", 0f, 0f, 1f, true);
            closeEyesSlider = csSlider.storableFloat;
            uiObjects.Add(csSlider.uiSlider.gameObject);

            var psSlider = BHUI.SetupSliderFloat(script, "Pupil Size", 0.5f, -2f, 2f, true);
            pupilSize = psSlider.storableFloat;
            uiObjects.Add(psSlider.uiSlider.gameObject);

            ApplyEyeLimits();
        }

        private void StartBlink()
        {
            if (isBlinking) return;
            if (currentCloseValue >= BlinkDisableThreshold * closeEyesAmount.val) return;
            isBlinking = true;
            blinkTimer = 0f;
            blinkStartValue = currentCloseValue;
            blinkDuration = UnityEngine.Random.Range(blinkDurationMin.val, blinkDurationMax.val);
        }

        public void OnTargetSwitch()
        {
            if (!autoBlinkEnabled.val) return;
            if (isBlinking) return;
            if (Time.time < blinkCooldownEnd) return;
            if (currentCloseValue >= BlinkDisableThreshold * closeEyesAmount.val) return;
            if (blinkOnTargetChance != null && UnityEngine.Random.value < blinkOnTargetChance.val)
            {
                StartBlink();
            }
        }

        public void SetVisible(bool visible)
        {
            foreach (var go in uiObjects) if (go != null) go.SetActive(visible);
        }

        private bool IsFaceTarget(TargetDef def)
        {
            if (def == null) return false;
            if (def.isPlayer) return true;
            if (def.isHold) return false;
            if (def.isRandom) return false;
            if (def.atom == null || def.atom.type != "Person") return false;
            return def.controlName == "headControl" || def.controlName == "head";
        }

        private void EnsureFaceBonesCached(TargetDef def)
        {
            if (def == null || def.faceBonesCached) return;
            if (def.isPlayer)
            {
                def.faceBonesCached = true;
                return;
            }
            if (def.atom == null) return;
            def.cachedLEye = null;
            def.cachedREye = null;
            def.cachedMouth = null;
            def.cachedHeadControl = null;
            var hc = def.atom.GetStorableByID("headControl") as FreeControllerV3;
            if (hc != null)
                def.cachedHeadControl = hc.transform;
            DAZBone[] bones = def.atom.GetComponentsInChildren<DAZBone>();
            if (bones != null)
            {
                foreach (var bone in bones)
                {
                    if (bone == null) continue;
                    if (bone.name == "lEye") def.cachedLEye = bone.transform;
                    else if (bone.name == "rEye") def.cachedREye = bone.transform;
                    else if (bone.name == "tongue03") def.cachedMouth = bone.transform;
                }
            }
            def.faceBonesCached = true;
        }

        private bool IsFaceVisible(TargetDef def, Vector3 viewerHeadPos)
        {
            if (def.isPlayer)
            {
                var cam = SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null ? SuperController.singleton.centerCameraTarget.transform : null;
                if (cam == null) return false;
                Vector3 camPos = cam.position;
                Vector3 camForward = cam.forward;
                Vector3 dirToViewerFromCam = (viewerHeadPos - camPos).normalized;
                float dotPlayer = Vector3.Dot(camForward, dirToViewerFromCam);
                return dotPlayer > FaceVisibleDotThreshold;
            }
            if (def == null || def.cachedHeadControl == null) return false;
            Vector3 targetHeadPos = def.cachedHeadControl.position;
            Vector3 targetHeadForward = def.cachedHeadControl.forward;
            Vector3 dirToViewer = (viewerHeadPos - targetHeadPos).normalized;
            float dotTarget = Vector3.Dot(targetHeadForward, dirToViewer);
            return dotTarget > FaceVisibleDotThreshold;
        }

        private int PickFaceFocusPoint()
        {
            float r = UnityEngine.Random.value;
            int picked;
            if (r < WeightLEye)
                picked = 0;
            else if (r < WeightLEye + WeightREye)
                picked = 1;
            else
                picked = 2;
            if (picked == currentFaceFocusPoint)
            {
                r = UnityEngine.Random.value;
                if (r < WeightLEye)
                    picked = 0;
                else if (r < WeightLEye + WeightREye)
                    picked = 1;
                else
                    picked = 2;
            }
            return picked;
        }

        private Vector3 GetFaceFocusPosition(TargetDef def, int focusPoint)
        {
            if (def.isPlayer && playerFaceRig != null)
            {
                Transform t = null;
                if (focusPoint == 0) t = playerFaceRig.lEye;
                else if (focusPoint == 1) t = playerFaceRig.rEye;
                else if (focusPoint == 2) t = playerFaceRig.mouth;
                if (t != null) return t.position;
                var cam = SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null ? SuperController.singleton.centerCameraTarget.transform : null;
                return cam != null ? cam.position : Vector3.zero;
            }
            Transform bone = null;
            if (focusPoint == 0) bone = def.cachedLEye;
            else if (focusPoint == 1) bone = def.cachedREye;
            else if (focusPoint == 2) bone = def.cachedMouth;
            if (bone != null) return bone.position;
            if (def.cachedHeadControl != null) return def.cachedHeadControl.position;
            return GetTargetPosition(def);
        }

        private void SmoothReturnEyesToHome(float deltaTime)
        {
            if (!savedEyeTargetPos) return;
            if (leftEyeTarget == null || rightEyeTarget == null) return;
            Vector3 homeWorldPos;
            if (eyeTargetControl != null && eyeTargetControl.transform.parent != null)
                homeWorldPos = eyeTargetControl.transform.parent.TransformPoint(savedEyeTargetLocalPos);
            else
                homeWorldPos = savedEyeTargetLocalPos;
            leftEyeTarget.position = Vector3.SmoothDamp(leftEyeTarget.position, homeWorldPos, ref _leftEyeVelocity, 0.2f);
            rightEyeTarget.position = Vector3.SmoothDamp(rightEyeTarget.position, homeWorldPos, ref _rightEyeVelocity, 0.2f);
        }

        private TargetDef ScanHoldCandidates(TargetDef holdDef, Vector3 headPos, Transform headTransform)
        {
            TargetDef best = null;
            float bestDist = float.MaxValue;
            if (holdDef.watchPlayer)
            {
                var cam = SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null ? SuperController.singleton.centerCameraTarget.transform : null;
                if (cam != null)
                {
                    Vector3 camPos = cam.position;
                    Vector3 camForward = cam.forward;
                    Vector3 dirToViewer = (headPos - camPos).normalized;
                    float dot = Vector3.Dot(camForward, dirToViewer);
                    if (dot > FaceVisibleDotThreshold)
                    {
                        float dist = Vector3.Distance(headPos, camPos);
                        if (dist < bestDist && IsWithinEyeLimits(headPos, camPos, headTransform))
                        {
                            if (_holdPlayerDef == null)
                                _holdPlayerDef = new TargetDef { isPlayer = true };
                            best = _holdPlayerDef;
                            bestDist = dist;
                        }
                    }
                }
            }
            if (holdDef.watchPersons)
            {
                var atoms = SuperController.singleton != null ? SuperController.singleton.GetAtoms() : null;
                if (atoms != null)
                {
                    foreach (var a in atoms)
                    {
                        if (a == null || a == atom || a.type != "Person" || !a.on) continue;
                        var hc = a.GetStorableByID("headControl") as FreeControllerV3;
                        if (hc == null) continue;
                        Vector3 targetHeadPos = hc.transform.position;
                        Vector3 targetHeadForward = hc.transform.forward;
                        Vector3 dirToViewer = (headPos - targetHeadPos).normalized;
                        float dot = Vector3.Dot(targetHeadForward, dirToViewer);
                        if (dot > FaceVisibleDotThreshold)
                        {
                            float dist = Vector3.Distance(headPos, targetHeadPos);
                            if (dist < bestDist && IsWithinEyeLimits(headPos, targetHeadPos, headTransform))
                            {
                                best = GetOrCreateHoldTempDef(a);
                                bestDist = dist;
                            }
                        }
                    }
                }
            }
            return best;
        }

        private bool IsWithinEyeLimits(Vector3 headPos, Vector3 targetPos, Transform headTransform)
        {
            Vector3 dir = (targetPos - headPos).normalized;
            Vector3 localDir = headTransform.InverseTransformDirection(dir);
            float yaw = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
            float horizontalDist = new Vector2(localDir.x, localDir.z).magnitude;
            float pitch = Mathf.Atan2(localDir.y, horizontalDist) * Mathf.Rad2Deg;
            return Mathf.Abs(yaw) <= EyesMaxAngleHorizVal
                && pitch <= EyesMaxAngleUpVal
                && pitch >= -EyesMaxAngleDownVal;
        }

        private TargetDef GetOrCreateHoldTempDef(Atom a)
        {
            TargetDef def;
            if (!_holdTempDefs.TryGetValue(a, out def))
            {
                def = new TargetDef { atom = a, controlName = "headControl" };
                _holdTempDefs[a] = def;
            }
            return def;
        }

        private void TriggerHoldAversion()
        {
            _holdAverting = true;
            _holdAversionDuration = UnityEngine.Random.Range(HoldAversionAvertMin, HoldAversionAvertMax);
        }

        public void Update(float deltaTime)
        {
            if (atom == null) return;
            if (pupilsMorph != null && pupilSize != null)
                pupilsMorph.morphValue = pupilSize.val;

            if (headUI.gazeEnabled && !headUI.externalControl)
            {
                if (eyeControl != null)
                {
                    if (eyeControl.currentLookMode != EyesControl.LookMode.Custom)
                        eyeControl.currentLookMode = EyesControl.LookMode.Custom;
                    if (leftEyeTarget != null && eyeControl.lookAt1.target != leftEyeTarget)
                        eyeControl.lookAt1.target = leftEyeTarget;
                    if (rightEyeTarget != null && eyeControl.lookAt2.target != rightEyeTarget)
                        eyeControl.lookAt2.target = rightEyeTarget;
                }
                if (eyeTargetControl != null && !eyeTargetControl.hidden)
                    eyeTargetControl.hidden = true;

                Transform headTransform = headControl != null ? headControl.transform : atom.transform;
                Vector3 headPos = headTransform.position;
                Vector3 headForward = headTransform.forward;
                TargetDef currentTarget = headCtrl.CurrentTarget;
                Vector3 lookTargetDir;
                bool gazeOffsetActive = Mathf.Abs(gazeOffsetYaw) > 0.001f || Mathf.Abs(gazeOffsetPitch) > 0.001f;
                bool isFaceFocus = false;

                if (gazeOffsetActive)
                {
                    lookTargetDir = headTransform.TransformDirection(
                        Quaternion.Euler(-gazeOffsetPitch, gazeOffsetYaw, 0f) * Vector3.forward);
                }
                else if (currentTarget != null && currentTarget.isHold)
                {
                    _holdScanTimer += deltaTime;
                    if (_holdScanTimer >= HoldScanInterval)
                    {
                        _holdScanTimer = 0f;
                        _holdScanTarget = ScanHoldCandidates(currentTarget, headPos, headTransform);
                    }
                    if (_holdAverting)
                    {
                        lastFaceTarget = null;
                        lookTargetDir = headForward;
                    }
                    else if (_holdScanTarget != null)
                    {
                        Vector3 targetPos = GetTargetPosition(_holdScanTarget);
                        Vector3 dirToTarget = (targetPos - headPos).normalized;
                        Vector3 localDir = headTransform.InverseTransformDirection(dirToTarget);
                        float yaw = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
                        float horizontalDist = new Vector2(localDir.x, localDir.z).magnitude;
                        float pitch = Mathf.Atan2(localDir.y, horizontalDist) * Mathf.Rad2Deg;
                        bool withinLimits = Mathf.Abs(yaw) <= EyesMaxAngleHorizVal
                            && pitch <= EyesMaxAngleUpVal
                            && pitch >= -EyesMaxAngleDownVal;
                        if (withinLimits)
                        {
                            _holdTracking = true;
                            if (IsFaceTarget(_holdScanTarget))
                            {
                                EnsureFaceBonesCached(_holdScanTarget);
                                if (lastFaceTarget != _holdScanTarget)
                                {
                                    lastFaceTarget = _holdScanTarget;
                                    currentFaceFocusPoint = PickFaceFocusPoint();
                                    nextFaceFocusSwitchTime = Time.time + UnityEngine.Random.Range(
                                        microSaccadeIntervalMin.val, microSaccadeIntervalMax.val);
                                }
                                if (Time.time >= nextFaceFocusSwitchTime)
                                {
                                    currentFaceFocusPoint = PickFaceFocusPoint();
                                    nextFaceFocusSwitchTime = Time.time + UnityEngine.Random.Range(
                                        microSaccadeIntervalMin.val, microSaccadeIntervalMax.val);
                                }
                                if (IsFaceVisible(_holdScanTarget, headPos))
                                {
                                    targetPos = GetFaceFocusPosition(_holdScanTarget, currentFaceFocusPoint);
                                    isFaceFocus = true;
                                    dirToTarget = (targetPos - headPos).normalized;
                                    localDir = headTransform.InverseTransformDirection(dirToTarget);
                                    yaw = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
                                    horizontalDist = new Vector2(localDir.x, localDir.z).magnitude;
                                    pitch = Mathf.Atan2(localDir.y, horizontalDist) * Mathf.Rad2Deg;
                                }
                            }
                            else
                            {
                                lastFaceTarget = null;
                            }
                            pitch = -pitch;
                            lookTargetDir = headTransform.TransformDirection(Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward);
                        }
                        else
                        {
                            _holdScanTarget = null;
                            _holdTracking = false;
                            lastFaceTarget = null;
                            lookTargetDir = headForward;
                        }
                    }
                    else
                    {
                        _holdTracking = false;
                        lastFaceTarget = null;
                        lookTargetDir = headForward;
                    }
                }
                else if (currentTarget != null)
                {
                    Vector3 targetPos;
                    if (IsFaceTarget(currentTarget))
                    {
                        EnsureFaceBonesCached(currentTarget);
                        if (lastFaceTarget != currentTarget)
                        {
                            lastFaceTarget = currentTarget;
                            currentFaceFocusPoint = PickFaceFocusPoint();
                            nextFaceFocusSwitchTime = Time.time + UnityEngine.Random.Range(
                                microSaccadeIntervalMin.val, microSaccadeIntervalMax.val);
                        }
                        if (Time.time >= nextFaceFocusSwitchTime)
                        {
                            currentFaceFocusPoint = PickFaceFocusPoint();
                            nextFaceFocusSwitchTime = Time.time + UnityEngine.Random.Range(
                                microSaccadeIntervalMin.val, microSaccadeIntervalMax.val);
                        }
                        if (IsFaceVisible(currentTarget, headPos))
                        {
                            targetPos = GetFaceFocusPosition(currentTarget, currentFaceFocusPoint);
                            isFaceFocus = true;
                        }
                        else
                        {
                            targetPos = GetTargetPosition(currentTarget);
                        }
                    }
                    else
                    {
                        lastFaceTarget = null;
                        targetPos = GetTargetPosition(currentTarget);
                    }
                    Vector3 dirToTarget = (targetPos - headPos).normalized;
                    Vector3 localDir = headTransform.InverseTransformDirection(dirToTarget);
                    float yaw = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
                    float horizontalDist = new Vector2(localDir.x, localDir.z).magnitude;
                    float pitch = Mathf.Atan2(localDir.y, horizontalDist) * Mathf.Rad2Deg;
                    pitch = -pitch;
                    lookTargetDir = headTransform.TransformDirection(Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward);
                }
                else
                {
                    lastFaceTarget = null;
                    lookTargetDir = headForward;
                }

                if (currentTarget != null && currentTarget.isHold && currentTarget.gazeAversionEnabled && _holdTracking)
                {
                    _holdAversionTimer += deltaTime;
                    if (_holdAversionTimer >= _holdAversionDuration)
                    {
                        _holdAversionTimer = 0f;
                        if (_holdAverting)
                        {
                            _holdAverting = false;
                            _holdAversionDuration = UnityEngine.Random.Range(HoldAversionLockMin, HoldAversionLockMax);
                        }
                        else
                        {
                            TriggerHoldAversion();
                        }
                    }
                }
                else
                {
                    if (_holdAverting)
                    {
                        _holdAverting = false;
                    }
                    _holdAversionTimer = 0f;
                    _holdAversionDuration = 0f;
                }

                if (!gazeOffsetActive && Time.time >= _nextSaccadeTime)
                {
                    float interval = UnityEngine.Random.Range(microSaccadeIntervalMin.val, microSaccadeIntervalMax.val);
                    _nextSaccadeTime = Time.time + interval;
                    float saccadeAngle = isFaceFocus ? FaceFocusSaccadeAngle : microSaccadeAngle.val;
                    _saccadeYaw = UnityEngine.Random.Range(-saccadeAngle, saccadeAngle);
                    _saccadePitch = UnityEngine.Random.Range(-saccadeAngle, saccadeAngle);
                }

                if (!gazeOffsetActive)
                {
                    Quaternion saccadeRot = Quaternion.AngleAxis(_saccadeYaw, headTransform.up) *
                        Quaternion.AngleAxis(_saccadePitch, headTransform.right);
                    lookTargetDir = saccadeRot * lookTargetDir;
                }

                float realTargetDistance = 1.5f;
                if (currentTarget != null && !gazeOffsetActive)
                {
                    if (currentTarget.isHold && _holdTracking && _holdScanTarget != null)
                    {
                        Vector3 realPos = IsFaceTarget(_holdScanTarget)
                            ? GetFaceFocusPosition(_holdScanTarget, currentFaceFocusPoint)
                            : GetTargetPosition(_holdScanTarget);
                        realTargetDistance = Vector3.Distance(headPos, realPos);
                    }
                    else if (!currentTarget.isHold)
                    {
                        Vector3 realPos = IsFaceTarget(currentTarget)
                            ? GetFaceFocusPosition(currentTarget, currentFaceFocusPoint)
                            : GetTargetPosition(currentTarget);
                        realTargetDistance = Vector3.Distance(headPos, realPos);
                    }
                }

                float convergenceDistance = Mathf.Max(realTargetDistance, MinConvergenceDistance);
                Vector3 finalTargetPos = headPos + lookTargetDir.normalized * convergenceDistance;
                Vector3 headRight = headTransform.right;
                float offset = (convergeAdjust != null ? convergeAdjust.val : 0f) * (IPD * 4f);
                Vector3 leftTargetPos = finalTargetPos - headRight * offset;
                Vector3 rightTargetPos = finalTargetPos + headRight * offset;
                float effectiveSaccadeSpeed = eyesSaccadeSpeed.val;
                if (currentTarget != null && currentTarget.isHold)
                    effectiveSaccadeSpeed = eyesSaccadeSpeed.val * 0.35f;
                if (leftEyeTarget != null)
                    leftEyeTarget.position = Vector3.SmoothDamp(leftEyeTarget.position, leftTargetPos, ref _leftEyeVelocity, effectiveSaccadeSpeed);
                if (rightEyeTarget != null)
                    rightEyeTarget.position = Vector3.SmoothDamp(rightEyeTarget.position, rightTargetPos, ref _rightEyeVelocity, effectiveSaccadeSpeed);
            }
            else if (headUI.externalControl)
            {
                if (eyeControl != null && eyeControl.currentLookMode != savedLookMode)
                    eyeControl.currentLookMode = savedLookMode;
                if (eyeTargetControl != null && eyeTargetControl.hidden)
                    eyeTargetControl.hidden = false;
            }
            else if (!headUI.gazeEnabled)
            {
                SmoothReturnEyesToHome(deltaTime);
            }

            if (eyesClosedMorph == null) return;
            float targetClose = closeEyesSlider.val * closeEyesAmount.val;
            currentCloseValue = Mathf.MoveTowards(currentCloseValue, targetClose, deltaTime * 3f);
            eyesClosedMorph.morphValue = currentCloseValue;
            if (targetClose >= BlinkDisableThreshold * closeEyesAmount.val) { isBlinking = false; return; }
            if (!autoBlinkEnabled.val) { isBlinking = false; return; }
            if (!isBlinking && Time.time >= nextAutoBlinkTime && Time.time >= blinkCooldownEnd)
            {
                StartBlink();
            }
            if (isBlinking)
            {
                blinkTimer += deltaTime;
                float closeDuration = blinkDuration * 0.3f;
                float openDuration = blinkDuration * 0.7f;
                float morphValue;
                if (blinkTimer <= closeDuration)
                {
                    float t = blinkTimer / closeDuration;
                    morphValue = Mathf.SmoothStep(blinkStartValue, 0.8f, t);
                }
                else
                {
                    float t = (blinkTimer - closeDuration) / openDuration;
                    t = Mathf.Clamp01(t);
                    morphValue = Mathf.SmoothStep(0.8f, blinkStartValue, t);
                }
                eyesClosedMorph.morphValue = morphValue;
                if (blinkTimer >= blinkDuration)
                {
                    eyesClosedMorph.morphValue = blinkStartValue;
                    isBlinking = false;
                    blinkCooldownEnd = Time.time + MinGapBetweenBlinks;
                    nextAutoBlinkTime = Time.time + UnityEngine.Random.Range(blinkIntervalMin.val, blinkIntervalMax.val);
                }
            }
        }

        private Vector3 GetTargetPosition(TargetDef def)
        {
            if (def == null) return Vector3.zero;
            if (def.isRandom)
            {
                return headCtrl.GetRandomTargetPosition(def);
            }
            if (def.isPlayer)
            {
                var cam = SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null ? SuperController.singleton.centerCameraTarget.transform : null;
                return cam != null ? cam.position : Vector3.zero;
            }
            if (def.isHold) return Vector3.zero;
            if (def.cachedTransform == null && def.atom != null)
            {
                if (!string.IsNullOrEmpty(def.controlName))
                {
                    var fc = def.atom.GetStorableByID(def.controlName) as FreeControllerV3;
                    def.cachedTransform = fc != null ? fc.transform : def.atom.transform;
                }
                else def.cachedTransform = def.atom.mainController != null ? def.atom.mainController.transform : def.atom.transform;
            }
            Vector3 basePos = def.cachedTransform != null ? def.cachedTransform.position : Vector3.zero;
            if (def.scatter > 0.001f)
                basePos += def.cachedScatterOffset;
            return basePos;
        }

        public void Destroy()
        {
            RestoreEyeLimits();
            if (eyeControl != null) eyeControl.currentLookMode = savedLookMode;
            if (nativeBlinkEnabled != null) nativeBlinkEnabled.val = nativeBlinkWasEnabled;
            if (eyeTargetControl != null) eyeTargetControl.hidden = savedEyeTargetHidden;
            if (pupilsMorph != null) pupilsMorph.morphValue = savedPupilValue;
            if (eyesClosedMorph != null) eyesClosedMorph.morphValue = 0f;
            if (leftEyeTargetGo != null) UnityEngine.Object.Destroy(leftEyeTargetGo);
            if (rightEyeTargetGo != null) UnityEngine.Object.Destroy(rightEyeTargetGo);
            if (playerFaceRig != null) playerFaceRig.Destroy();
        }
    }

    class FaceRig
    {
        public GameObject owner;
        public Transform lEye;
        public Transform rEye;
        public Transform mouth;

        public static FaceRig Create(Transform parent, float mouthDistance, float eyeDistance)
        {
            var owner = new GameObject("BehaviorHead_PlayerFaceRig");
            owner.transform.SetParent(parent, false);
            var mouth = new GameObject("Mouth").transform;
            mouth.SetParent(owner.transform, false);
            mouth.localPosition = new Vector3(0, -mouthDistance, 0);
            var lEye = new GameObject("LEye").transform;
            lEye.SetParent(owner.transform, false);
            lEye.localPosition = new Vector3(-eyeDistance, 0, 0);
            var rEye = new GameObject("REye").transform;
            rEye.SetParent(owner.transform, false);
            rEye.localPosition = new Vector3(eyeDistance, 0, 0);
            return new FaceRig { owner = owner, mouth = mouth, lEye = lEye, rEye = rEye };
        }

        public void Destroy()
        {
            if (owner != null) UnityEngine.Object.Destroy(owner);
        }
    }
}