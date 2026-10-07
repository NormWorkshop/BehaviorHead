using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Norm
{
public class TargetSelector
{
private BehaviorHeadUI headUI;
private FreeControllerV3 headControl;
private FreeControllerV3 chestControl;
private JSONStorableFloat headMaxAngleH;
private JSONStorableFloat headMaxAngleV;
    private const float HeadAngleMarginDeg = 10f;
     private const float AngleCheckInterval = 1f;
     private const float SingularThreshold = 0.15f;
     private TargetDef currentAutoTarget;
     private float nextTargetSwitchTime;
     private float currentTargetDuration = 5f;
     private float nextGroupSwitchTime = 0f;
     private float nextAngleCheckTime = 0f;
     private float currentAutoSpeed;

     public TargetDef CurrentAutoTarget { get { return currentAutoTarget; } }
     public float CurrentAutoSpeed { get { return currentAutoSpeed; } }
     public float CurrentTargetDuration { get { return currentTargetDuration; } }
     public float NextTargetSwitchTime
     {
         get { return nextTargetSwitchTime; }
         set { nextTargetSwitchTime = value; }
     }
     public Action<TargetGroup> OnGroupSwitched;
     public TargetSelector(BehaviorHeadUI headUI, FreeControllerV3 headControl, FreeControllerV3 chestControl)
     {
         this.headUI = headUI;
         this.headControl = headControl;
         this.chestControl = chestControl;
     }
     public void SetAngleLimits(JSONStorableFloat maxH, JSONStorableFloat maxV)
     {
         headMaxAngleH = maxH;
         headMaxAngleV = maxV;
     }
     public TargetDef Update(float deltaTime, bool waitingForContact, out bool groupSwitched)
     {
         groupSwitched = false;
         if (headUI.targetGroups.Count > 1 && !waitingForContact && Time.time >= nextGroupSwitchTime)
         {
             SwitchToNextGroup();
             groupSwitched = true;
         }
         var group = ResolveActiveGroup();
         if (group == null || group.targets.Count == 0)
         {
             currentAutoTarget = null;
             nextTargetSwitchTime = Time.time + 1f;
             return null;
         }
         if (currentAutoTarget != null && (!group.targets.Contains(currentAutoTarget) || !IsTargetValid(currentAutoTarget)))
         {
             currentAutoTarget = null;
             nextTargetSwitchTime = 0f;
         }
         if (group.targets.Count == 1 && currentAutoTarget != null && !waitingForContact && !currentAutoTarget.isRandom)
         {
             nextTargetSwitchTime = Time.time + currentTargetDuration;
         }
         else if (!waitingForContact && (currentAutoTarget == null || Time.time >= nextTargetSwitchTime))
         {
             group = headUI.activeGroup;
             if (group == null || group.targets.Count == 0)
             {
                 currentAutoTarget = null;
                 nextTargetSwitchTime = Time.time + 1f;
                 return null;
             }
             List<TargetDef> candidates = new List<TargetDef>();
             List<float> candidateWeights = new List<float>();
             foreach (var t in group.targets)
             {
                 if (!IsTargetValid(t)) continue;
                 if (!IsTargetWithinHeadLimits(t)) continue;
                 if (t == currentAutoTarget && group.targets.Count > 1) continue;
                 float effectiveWeight = t.weight;
                 if (t.cooldownCounter > 0) effectiveWeight *= 0.5f;
                 if (effectiveWeight > 0.001f)
                 {
                     candidates.Add(t);
                     candidateWeights.Add(effectiveWeight);
                 }
             }
             if (candidates.Count == 0)
             {
                 currentAutoTarget = null;
                 nextTargetSwitchTime = Time.time + 1f;
                 return null;
             }
             float totalWeight = 0f;
             for (int i = 0; i < candidateWeights.Count; i++) totalWeight += candidateWeights[i];
             float randomPoint = UnityEngine.Random.Range(0f, totalWeight);
             float cumulative = 0f;
             TargetDef chosen = candidates[0];
             for (int i = 0; i < candidates.Count; i++)
             {
                 cumulative += candidateWeights[i];
                 if (randomPoint <= cumulative) { chosen = candidates[i]; break; }
             }
             foreach (var t in group.targets)
             {
                 if (t == chosen) t.cooldownCounter = t.cooldown;
                 else if (t.cooldownCounter > 0) t.cooldownCounter--;
             }
             currentAutoTarget = chosen;
             currentAutoSpeed = (chosen.speedMin == chosen.speedMax) ? chosen.speedMin : UnityEngine.Random.Range(chosen.speedMin, chosen.speedMax);
             currentTargetDuration = UnityEngine.Random.Range(chosen.durationMin, chosen.durationMax);
             nextTargetSwitchTime = float.PositiveInfinity;
             return chosen;
         }
         if (Time.time >= nextAngleCheckTime)
         {
             nextAngleCheckTime = Time.time + AngleCheckInterval;
             if (currentAutoTarget != null
                 && !currentAutoTarget.isHold
                 && !currentAutoTarget.isRandom
                 && !IsTargetWithinHeadLimits(currentAutoTarget))
             {
                 currentAutoTarget = null;
                 nextTargetSwitchTime = 0f;
             }
         }
         return null;
     }
     public void OnManualTargetSet()
     {
         currentAutoTarget = null;
         nextTargetSwitchTime = float.PositiveInfinity;
         nextAngleCheckTime = 0f;
     }
     public void Reset()
     {
         currentAutoTarget = null;
         nextTargetSwitchTime = 0f;
         nextAngleCheckTime = 0f;
         if (headUI != null && headUI.targetGroups != null)
         {
             for (int g = 0; g < headUI.targetGroups.Count; g++)
             {
                 var grp = headUI.targetGroups[g];
                 if (grp == null || grp.targets == null) continue;
                 for (int t = 0; t < grp.targets.Count; t++)
                 {
                     if (grp.targets[t] != null) grp.targets[t].cooldownCounter = 0;
                 }
             }
         }
     }
     public void ResetAngleCheck()
     {
         nextAngleCheckTime = 0f;
     }
     public void RefreshAutoSpeed(TargetDef t)
     {
         if (t == null || t != currentAutoTarget) return;
         currentAutoSpeed = (t.speedMin == t.speedMax) ? t.speedMin : UnityEngine.Random.Range(t.speedMin, t.speedMax);
     }
     public void NotifyGroupManuallySelected(TargetGroup grp)
     {
         if (grp == null) return;
         nextAngleCheckTime = 0f;
         if (grp.switchMin > 0f || grp.switchMax > 0f)
             nextGroupSwitchTime = Time.time + UnityEngine.Random.Range(grp.switchMin, grp.switchMax);
         else
             nextGroupSwitchTime = float.PositiveInfinity;
     }
     public void InitGroupSwitchTimer()
     {
         var grp = headUI.activeGroup;
         if (grp == null) { nextGroupSwitchTime = float.PositiveInfinity; return; }
         if (grp.switchMin > 0f || grp.switchMax > 0f)
             nextGroupSwitchTime = Time.time + UnityEngine.Random.Range(grp.switchMin, grp.switchMax);
         else
             nextGroupSwitchTime = float.PositiveInfinity;
     }
     public TargetGroup ResolveActiveGroup()
     {
         var grp = headUI.activeGroup;
         if (grp == null || !headUI.targetGroups.Contains(grp) || grp.targets.Count == 0)
         {
             grp = headUI.targetGroups.FirstOrDefault(g => g.targets.Count > 0);
             headUI.activeGroup = grp;
         }
         return grp;
     }
 	private void SwitchToNextGroup()
 	{
 		if (headUI.targetGroups.Count <= 1) return;
 		string activeName = headUI.activeGroup != null ? headUI.activeGroup.name : "";
 		var candidates = new List<TargetGroup>();
 		var weights = new List<float>();
 		foreach (var g in headUI.targetGroups)
 		{
 			if (g.name == activeName) continue;
 			if (g.targets.Count == 0) continue;
 			bool hasValidTarget = false;
 			foreach (var t in g.targets)
 			{
 				if (IsTargetValid(t)) { hasValidTarget = true; break; }
 			}
 			if (!hasValidTarget) continue;
 			if (g.weight > 0.001f)
 			{
 				candidates.Add(g);
 				weights.Add(g.weight);
 			}
 		}
 		if (candidates.Count == 0) return;
 		float totalWeight = 0f;
 		for (int i = 0; i < weights.Count; i++) totalWeight += weights[i];
 		float randomPoint = UnityEngine.Random.Range(0f, totalWeight);
 		float cumulative = 0f;
 		TargetGroup chosen = candidates[0];
 		for (int i = 0; i < candidates.Count; i++)
 		{
 			cumulative += weights[i];
 			if (randomPoint <= cumulative) { chosen = candidates[i]; break; }
 		}
 		headUI.activeGroup = chosen;
 		currentAutoTarget = null;
 		nextAngleCheckTime = 0f;
 		if (chosen.switchMin > 0f || chosen.switchMax > 0f)
 			nextGroupSwitchTime = Time.time + UnityEngine.Random.Range(chosen.switchMin, chosen.switchMax);
 		else
 			nextGroupSwitchTime = float.PositiveInfinity;
 		if (OnGroupSwitched != null) OnGroupSwitched(chosen);
 	}
     public bool IsTargetValid(TargetDef def)
     {
         if (def == null) return false;
         if (def.isHold) return true;
         if (def.isPlayer) return true;
         if (def.isRandom) return true;
         if (def.atom == null) return false;
         var sc = SuperController.singleton;
         if (sc == null) return true;
         return sc.GetAtomByUid(def.atom.uid) != null;
     }
     private bool IsTargetWithinHeadLimits(TargetDef def)
     {
         if (def == null || headControl == null) return false;
         if (def.isHold) return true;
         if (def.isRandom) return true;
         if (headMaxAngleH == null || headMaxAngleV == null) return true;
         Transform reference = chestControl != null ? chestControl.transform : headControl.transform.parent;
         if (reference == null) reference = headControl.transform;
         Vector3 targetPos = GetTargetPosition(def);
         Vector3 headPos = headControl.transform.position;
         Vector3 dir = targetPos - headPos;
         if (dir.sqrMagnitude < 0.0001f) return true;
         Vector3 localDir = reference.InverseTransformDirection(dir);
         float horizontalMagnitude = new Vector2(localDir.x, localDir.z).magnitude;
         if (horizontalMagnitude < SingularThreshold) return true;
         float yawDeg = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
         float pitchDeg = Mathf.Atan2(localDir.y, horizontalMagnitude) * Mathf.Rad2Deg;
         float maxYaw = headMaxAngleH.val - HeadAngleMarginDeg;
         float maxPitch = headMaxAngleV.val - HeadAngleMarginDeg;
         if (maxYaw < 0f) maxYaw = 0f;
         if (maxPitch < 0f) maxPitch = 0f;
         return Mathf.Abs(yawDeg) <= maxYaw && Mathf.Abs(pitchDeg) <= maxPitch;
     }
     public void RollScatterOffset(TargetDef def)
     {
         if (def == null || def.scatter <= 0.001f)
         {
             if (def != null) def.cachedScatterOffset = Vector3.zero;
             return;
         }
         Vector3 basePos = GetBaseTargetPosition(def);
         Vector3 headPos = headControl.transform.position;
         Vector3 baseDir = basePos - headPos;
         float distance = baseDir.magnitude;
         if (distance < 0.001f) { def.cachedScatterOffset = Vector3.zero; return; }
         baseDir /= distance;
         float theta = UnityEngine.Random.Range(0f, def.scatter);
         float phi = UnityEngine.Random.Range(0f, 360f);
         Vector3 up = Vector3.up;
         if (Mathf.Abs(Vector3.Dot(baseDir, up)) > 0.99f) up = Vector3.right;
         Vector3 perpAxis = Vector3.Cross(baseDir, up).normalized;
         Quaternion tiltRot = Quaternion.AngleAxis(theta, perpAxis);
         Vector3 tiltedDir = tiltRot * baseDir;
         Quaternion azimuthRot = Quaternion.AngleAxis(phi, baseDir);
         Vector3 scatteredDir = azimuthRot * tiltedDir;
         def.cachedScatterOffset = (scatteredDir - baseDir) * distance;
     }
     private Vector3 GetBaseTargetPosition(TargetDef def)
     {
         if (def == null) return Vector3.zero;
         if (def.isHold) return Vector3.zero;
         if (def.isRandom)
         {
             if (def.randomCachedTransform != null) return def.randomCachedTransform.position;
             return def.randomVirtualPosition;
         }
         if (def.isPlayer)
         {
             var cam = SuperController.singleton != null ? SuperController.singleton.lookCamera != null ? SuperController.singleton.lookCamera.transform : null : null;
             return cam != null ? cam.position : Vector3.zero;
         }
         if (def.cachedTransform == null && def.atom != null)
         {
             if (!string.IsNullOrEmpty(def.controlName))
             {
                 var fc = def.atom.GetStorableByID(def.controlName) as FreeControllerV3;
                 def.cachedTransform = fc != null ? fc.transform : def.atom.transform;
             }
             else def.cachedTransform = def.atom.mainController != null ? def.atom.mainController.transform : def.atom.transform;
         }
         return def.cachedTransform != null ? def.cachedTransform.position : Vector3.zero;
     }
     public Vector3 GetTargetPosition(TargetDef def)
     {
         Vector3 basePos = GetBaseTargetPosition(def);
         if (def != null && def.scatter > 0.001f) basePos += def.cachedScatterOffset;
         return basePos;
     }
 }
}