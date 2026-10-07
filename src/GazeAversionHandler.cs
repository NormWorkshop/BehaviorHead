using UnityEngine;
namespace Norm
{
public class GazeAversionHandler
{
private EyesController eyesCtrl;
private FreeControllerV3 headControl;
private JSONStorableFloat downChance;
private JSONStorableFloat sideChance;
private JSONStorableFloat downAngle;
private JSONStorableFloat sideAngle;
private JSONStorableFloat delay;
private bool pending;
private float pendingTimer;
private bool active;
private float yaw;
private float pitch;
private float baseYaw;
private float basePitch;
private bool waitingForMutualGaze;
private bool targetWasLookingAtUs;
private float mutualGazeWaitTimer;
private float mutualGazeCheckTimer;
private const float MutualGazeAngleDeg = 14f;
private static readonly float MutualGazeDotThreshold = Mathf.Cos(MutualGazeAngleDeg * Mathf.Deg2Rad);
private const float MutualGazeCheckInterval = 0.2f;
private const float MutualGazeMaxWait = 0.8f;
public bool IsPending { get { return pending; } }
public bool IsActive { get { return active; } }
public bool IsWaitingForMutualGaze { get { return waitingForMutualGaze; } }
public float DownChanceVal { get { return downChance != null ? downChance.val : 0.2f; } }
public float SideChanceVal { get { return sideChance != null ? sideChance.val : 0.2f; } }
public float DownAngleVal { get { return downAngle != null ? downAngle.val : 12f; } }
public float SideAngleVal { get { return sideAngle != null ? sideAngle.val : 7f; } }
public GazeAversionHandler(FreeControllerV3 headControl)
{
this.headControl = headControl;
}
public void SetEyesController(EyesController ctrl) { eyesCtrl = ctrl; }
public void SetParams(JSONStorableFloat dc, JSONStorableFloat sc, JSONStorableFloat da, JSONStorableFloat sa, JSONStorableFloat dl)
{
downChance = dc;
sideChance = sc;
downAngle = da;
sideAngle = sa;
delay = dl;
}
public bool Update(float deltaTime, TargetDef activeDef, Transform reference, Transform headTransform, float targetDuration)
{
bool directTurnNeeded = false;
if (pending)
{
pendingTimer -= deltaTime;
if (pendingTimer <= 0f)
{
pending = false;
StartAversion(yaw, pitch);
}
}
if (waitingForMutualGaze && activeDef != null)
{
mutualGazeWaitTimer += deltaTime;
mutualGazeCheckTimer += deltaTime;
if (mutualGazeCheckTimer >= MutualGazeCheckInterval)
{
mutualGazeCheckTimer = 0f;
bool isLooking = IsPersonLookingAtUs(activeDef);
if (isLooking && !targetWasLookingAtUs)
{
waitingForMutualGaze = false;
TriggerAversion(reference, headTransform);
directTurnNeeded = true;
}
targetWasLookingAtUs = isLooking;
}
if (waitingForMutualGaze && mutualGazeWaitTimer >= MutualGazeMaxWait)
{
waitingForMutualGaze = false;
TriggerAversion(reference, headTransform);
directTurnNeeded = true;
}
}
return directTurnNeeded;
}
public void HandleContact(TargetDef activeDef, Transform reference, Transform headTransform)
{
if (activeDef == null || !activeDef.gazeAversionEnabled || activeDef.isHold) return;
if (activeDef.isPlayer)
{
TriggerAversion(reference, headTransform);
}
else if (activeDef.isRandom)
{
if (IsRandomAversionTarget(activeDef))
TriggerAversion(reference, headTransform);
}
else if (IsPersonHeadTarget(activeDef))
{
TriggerAversion(reference, headTransform);
}
}
public void Reset()
{
if (!active && !pending) return;
active = false;
pending = false;
pendingTimer = 0f;
yaw = 0f;
pitch = 0f;
baseYaw = 0f;
basePitch = 0f;
if (eyesCtrl != null) eyesCtrl.StopGazeOffset();
}
public void ResetAll()
{
waitingForMutualGaze = false;
targetWasLookingAtUs = false;
mutualGazeWaitTimer = 0f;
mutualGazeCheckTimer = 0f;
Reset();
}
public bool TryGetAimAngles(out float h, out float v)
{
if (!active) { h = 0f; v = 0f; return false; }
h = baseYaw + yaw;
v = basePitch + pitch;
return true;
}
private bool IsRandomAversionTarget(TargetDef def)
{
if (def == null) return false;
if (def.selectedBodyPart != BodyPart.Head) return false;
return def.randomCachedAtom != null || def.randomCachedTransform != null;
}
private void TriggerAversion(Transform reference, Transform headTransform)
{
Vector3 currentForwardRef = reference.InverseTransformDirection(headTransform.forward);
baseYaw = Mathf.Atan2(currentForwardRef.x, currentForwardRef.z) * Mathf.Rad2Deg;
basePitch = Mathf.Atan2(currentForwardRef.y, new Vector2(currentForwardRef.x, currentForwardRef.z).magnitude) * Mathf.Rad2Deg;
float roll = UnityEngine.Random.value;
float dc = downChance != null ? downChance.val : 0.2f;
float sc = sideChance != null ? sideChance.val : 0.2f;
float da = downAngle != null ? downAngle.val : 12f;
float sa = sideAngle != null ? sideAngle.val : 7f;
float dl = delay != null ? delay.val : 0.6f;
if (roll < dc)
{
pending = true;
pendingTimer = dl;
yaw = 0f;
pitch = -da;
}
else if (roll < dc + sc)
{
pending = true;
pendingTimer = dl;
yaw = UnityEngine.Random.value < 0.5f ? -sa : sa;
pitch = 0f;
}
}
private void StartAversion(float yawDeg, float pitchDeg)
{
active = true;
yaw = yawDeg;
pitch = pitchDeg;
if (eyesCtrl != null) eyesCtrl.StartGazeOffset(yawDeg, pitchDeg);
}
private bool IsPersonHeadTarget(TargetDef def)
{
if (def == null) return false;
if (def.isPlayer) return false;
if (def.isHold) return false;
if (def.isRandom) return false;
if (def.atom == null) return false;
return def.controlName == "headControl" || def.controlName == "head";
}
private Transform GetTargetHead(TargetDef def)
{
if (def == null) return null;
if (def.isRandom)
{
if (def.randomCachedAtom == null) return null;
var hcRandom = def.randomCachedAtom.GetStorableByID("headControl") as FreeControllerV3;
if (hcRandom != null) return hcRandom.transform;
return null;
}
if (def.cachedHeadControl != null) return def.cachedHeadControl;
if (def.atom == null) return null;
var hc = def.atom.GetStorableByID("headControl") as FreeControllerV3;
if (hc != null) { def.cachedHeadControl = hc.transform; return hc.transform; }
if (!string.IsNullOrEmpty(def.controlName))
{
var fc = def.atom.GetStorableByID(def.controlName) as FreeControllerV3;
if (fc != null) { def.cachedHeadControl = fc.transform; return fc.transform; }
}
return null;
}
private bool IsPersonLookingAtUs(TargetDef def)
{
if (def == null || headControl == null) return false;
Transform targetHead = GetTargetHead(def);
if (targetHead == null) return false;
Vector3 ourHeadPos = headControl.transform.position;
Vector3 dirToUs = ourHeadPos - targetHead.position;
if (dirToUs.sqrMagnitude < 0.0001f) return false;
dirToUs.Normalize();
float dot = Vector3.Dot(targetHead.forward, dirToUs);
return dot > MutualGazeDotThreshold;
}
}
}