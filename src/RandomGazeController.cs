using System.Collections.Generic;
using UnityEngine;
namespace Norm
{
public class RandomGazeController
{
private RandomGazeConfig config;
private Atom selfAtom;
private Transform selfLHand;
private Transform selfRHand;
private bool handsCached = false;
    private const float HandQuickDurationMin = 0.3f;
     private const float HandQuickDurationMax = 0.7f;
     private const float StaticHandMultiplier = 0.2f;
     private const float PlayerWeightMinEffectiveFraction = 0.70f;
     private class CharacterInfo
     {
         public Atom atom;
         public Transform headTransform;
     }
     private List<CharacterInfo> characters = new List<CharacterInfo>();
     private float characterScanTimer = -1f;
     private const float CharacterScanInterval = 1.0f;
     private Transform playerCameraTransform;
     private const float HeadAngleMarginDeg = 10f;
     private const float DanceDwellChance = 0.5f;
     private const float SingularThreshold = 0.15f;
     private AttentionSystem attentionSystem;
     public RandomGazeConfig Config { get { return config; } }
     public RandomGazeController()
     {
         config = new RandomGazeConfig();
     }
     public void SetSelfAtom(Atom atom)
     {
         selfAtom = atom;
         handsCached = false;
         selfLHand = null;
         selfRHand = null;
     }
     public void SetAttentionSystem(AttentionSystem system)
     {
         attentionSystem = system;
     }
     public void Update(float deltaTime)
     {
         characterScanTimer -= deltaTime;
         if (characterScanTimer <= 0f)
         {
             ScanCharacters();
             characterScanTimer = CharacterScanInterval;
         }
     }
     public void GenerateTarget(TargetDef def, Transform reference, Vector3 origin, float maxAngleH, float maxAngleV)
     {
         if (def == null) return;
         if (reference == null)
         {
             def.randomVirtualPosition = origin;
             def.randomCachedTransform = null;
             return;
         }

         for (int attempt = 0; attempt < 5; attempt++)
         {
             GenerateTargetOnce(def, reference, origin, maxAngleH, maxAngleV);
             
             // Если цель - часть персонажа, проверку на окклюзию телами не делаем
             if (def.randomCachedAtom != null)
             {
                 return;
             }

             Vector3 targetPos = def.randomCachedTransform != null ? def.randomCachedTransform.position : def.randomVirtualPosition;
             
             if (attentionSystem == null || selfAtom == null || !attentionSystem.IsRayOccludedByCharacters(origin, targetPos, selfAtom))
             {
                 return;
             }
         }
     }

     private void GenerateTargetOnce(TargetDef def, Transform reference, Vector3 origin, float maxAngleH, float maxAngleV)
     {
         float nothingW = config.nothingWeight.val;
         float selfGazeW = config.selfGazeWeight.val;
         float virtualW = config.virtualTargetWeight.val;
         float playerW = 0f;
         float personsW = 0f;
         if (config.trackPlayer.val && playerCameraTransform != null)
         {
             if (IsWithinHeadLimits(reference, playerCameraTransform.position, origin, maxAngleH, maxAngleV))
             {
                 float minPersonModifier = 1f;
                 if (attentionSystem != null)
                     minPersonModifier = attentionSystem.GetMinEffectiveModifier();
                 playerW = config.personsWeight.val * minPersonModifier * PlayerWeightMinEffectiveFraction;
             }
         }
         List<CharacterInfo> visibleCharacters = new List<CharacterInfo>();
         for (int i = 0; i < characters.Count; i++)
         {
             var c = characters[i];
             if (c == null || c.headTransform == null) continue;
             if (IsWithinHeadLimits(reference, c.headTransform.position, origin, maxAngleH, maxAngleV))
                 visibleCharacters.Add(c);
         }
         if (visibleCharacters.Count > 0)
         {
             personsW = config.personsWeight.val;
             if (attentionSystem != null)
             {
                 float maxModifier = 0f;
                 for (int i = 0; i < visibleCharacters.Count; i++)
                 {
                     float mod = attentionSystem.GetModifierForCharacter(visibleCharacters[i].atom);
                     if (mod > maxModifier) maxModifier = mod;
                 }
                 personsW *= maxModifier;
             }
         }
         float totalWeight = nothingW + selfGazeW + virtualW + playerW + personsW;
         if (totalWeight < 0.001f)
         {
             GenerateNothing(def, reference, origin);
             return;
         }
         float roll = UnityEngine.Random.Range(0f, totalWeight);
         if (roll < nothingW)
         {
             GenerateNothing(def, reference, origin);
         }
         else if (roll < nothingW + selfGazeW)
         {
             GenerateSelfHands(def, reference, origin, maxAngleH, maxAngleV);
         }
         else if (roll < nothingW + selfGazeW + playerW)
         {
             GeneratePlayerTarget(def);
         }
         else if (roll < nothingW + selfGazeW + playerW + personsW)
         {
             GeneratePersonTarget(def, visibleCharacters, reference, origin, maxAngleH, maxAngleV);
         }
         else
         {
             GenerateVirtualPoint(def, reference, origin, maxAngleH, maxAngleV);
         }
     }
     public bool IsLiveTargetOutOfRange(TargetDef def, Transform reference, Vector3 origin, float maxAngleH, float maxAngleV)
     {
         if (def == null || reference == null) return false;
         if (def.randomCachedTransform == null) return false;
         return !IsWithinHeadLimits(reference, def.randomCachedTransform.position, origin, maxAngleH, maxAngleV);
     }
     private void GeneratePlayerTarget(TargetDef def)
     {
         def.randomCachedTransform = playerCameraTransform;
         def.randomVirtualPosition = playerCameraTransform.position;
         def.randomCachedAtom = null;
         def.selectedBodyPart = BodyPart.Head;
         def.glanceType = UnityEngine.Random.value < 0.5f ? GlanceType.Quick : GlanceType.Sustained;
         def.glanceDuration = 0f;
     }
     private void GeneratePersonTarget(TargetDef def, List<CharacterInfo> visible, Transform reference, Vector3 origin, float maxAngleH, float maxAngleV)
     {
         if (visible.Count == 0)
         {
             GenerateVirtualPoint(def, reference, origin, maxAngleH, maxAngleV);
             return;
         }
         float totalCharWeight = 0f;
         float[] charWeights = new float[visible.Count];
         for (int i = 0; i < visible.Count; i++)
         {
             float mod = 1f;
             if (attentionSystem != null && visible[i].atom != null)
             {
                 mod = attentionSystem.GetModifierForCharacter(visible[i].atom);
             }
             charWeights[i] = mod;
             totalCharWeight += mod;
         }
         CharacterInfo chosen = visible[0];
         if (totalCharWeight > 0.001f)
         {
             float roll = UnityEngine.Random.Range(0f, totalCharWeight);
             float cumulative = 0f;
             for (int i = 0; i < visible.Count; i++)
             {
                 cumulative += charWeights[i];
                 if (roll <= cumulative)
                 {
                     chosen = visible[i];
                     break;
                 }
             }
         }
         Transform selectedPoint = SelectBodyPart(chosen.atom, def);
         if (selectedPoint == null)
         {
             GenerateVirtualPoint(def, reference, origin, maxAngleH, maxAngleV);
             return;
         }
         def.randomCachedTransform = selectedPoint;
         def.randomVirtualPosition = selectedPoint.position;
         def.randomCachedAtom = chosen.atom;
         int part = def.selectedBodyPart;
         bool isHand = (part == BodyPart.LHand || part == BodyPart.RHand);
         float quickChance;
         if (part == BodyPart.Head)
             quickChance = 0.25f;
         else if (isHand || part == BodyPart.LArm || part == BodyPart.RArm)
             quickChance = 1.0f;
         else
             quickChance = 0.50f;
         def.glanceType = UnityEngine.Random.value < quickChance ? GlanceType.Quick : GlanceType.Sustained;
         if (isHand)
             def.glanceDuration = UnityEngine.Random.Range(HandQuickDurationMin, HandQuickDurationMax);
         else
             def.glanceDuration = 0f;
         if (attentionSystem != null && chosen.atom != null && attentionSystem.IsDancing(chosen.atom))
         {
             if (UnityEngine.Random.value < DanceDwellChance)
             {
                 def.danceDwellRequested = true;
             }
         }
     }
     private Transform SelectBodyPart(Atom atom, TargetDef def)
     {
         if (atom == null || attentionSystem == null) return null;
         Transform[] parts = new Transform[7];
         for (int i = 0; i < 7; i++)
         {
             Transform cached = attentionSystem.GetCachedBodyPart(atom, i);
             parts[i] = cached != null ? cached : GetBodyPartTransform(atom, i);
         }
         bool dupL = (parts[BodyPart.LArm] != null && parts[BodyPart.LArm] == parts[BodyPart.LHand]);
         bool dupR = (parts[BodyPart.RArm] != null && parts[BodyPart.RArm] == parts[BodyPart.RHand]);
         float[] finalWeights = new float[7];
         float totalWeight = 0f;
         for (int i = 0; i < 7; i++)
         {
             Transform part = parts[i];
             if (part == null)
             {
                 finalWeights[i] = 0f;
                 continue;
             }
             if (i == BodyPart.LArm && dupL) { finalWeights[i] = 0f; continue; }
             if (i == BodyPart.RArm && dupR) { finalWeights[i] = 0f; continue; }
             float baseWeight = GetBaseWeightByIndex(i);
             float distanceFactor = attentionSystem.GetDistanceFactor(atom, part);
             float movementFactor = attentionSystem.GetMovementFactor(atom, part);
             finalWeights[i] = baseWeight * distanceFactor * movementFactor;
             if (i == BodyPart.LHand || i == BodyPart.RHand)
             {
                 if (!attentionSystem.IsPointMoving(atom, part))
                     finalWeights[i] *= StaticHandMultiplier;
             }
             if (finalWeights[i] > 0f && attentionSystem.IsSuppressed(atom, i))
             {
                 finalWeights[i] = 0f;
             }
             totalWeight += finalWeights[i];
         }
         if (totalWeight < 0.001f)
         {
             totalWeight = 0f;
             for (int i = 0; i < 7; i++)
             {
                 Transform part = parts[i];
                 if (part == null) { finalWeights[i] = 0f; continue; }
                 if (i == BodyPart.LArm && dupL) { finalWeights[i] = 0f; continue; }
                 if (i == BodyPart.RArm && dupR) { finalWeights[i] = 0f; continue; }
                 float baseWeight = GetBaseWeightByIndex(i);
                 float distanceFactor = attentionSystem.GetDistanceFactor(atom, part);
                 float movementFactor = attentionSystem.GetMovementFactor(atom, part);
                 finalWeights[i] = baseWeight * distanceFactor * movementFactor;
                 if (i == BodyPart.LHand || i == BodyPart.RHand)
                 {
                     if (!attentionSystem.IsPointMoving(atom, part))
                         finalWeights[i] *= StaticHandMultiplier;
                 }
                 totalWeight += finalWeights[i];
             }
         }
         if (totalWeight < 0.001f) return null;
         float roll = UnityEngine.Random.Range(0f, totalWeight);
         float cumulative = 0f;
         for (int i = 0; i < 7; i++)
         {
             cumulative += finalWeights[i];
             if (roll < cumulative)
             {
                 Transform part = parts[i];
                 if (part != null)
                 {
                     attentionSystem.DecrementSuppression(atom);
                     def.selectedBodyPart = i;
                     return part;
                 }
             }
         }
         return parts[BodyPart.Head] != null ? parts[BodyPart.Head] : GetBodyPartTransform(atom, BodyPart.Head);
     }
     private float GetBaseWeightByIndex(int i)
     {
         switch (i)
         {
             case BodyPart.Head: return config.headWeight.val;
             case BodyPart.Chest: return config.chestWeight.val;
             case BodyPart.Hip: return config.hipWeight.val;
             case BodyPart.LHand: return config.lHandWeight.val;
             case BodyPart.RHand: return config.rHandWeight.val;
             case BodyPart.LArm: return config.lArmWeight.val;
             case BodyPart.RArm: return config.rArmWeight.val;
         }
         return 1f;
     }
     private Transform SelectRandomBodyPart(Atom atom, TargetDef def)
     {
         if (atom == null) return null;
         float[] weights = new float[7];
         weights[0] = config.headWeight.val;
         weights[1] = config.chestWeight.val;
         weights[2] = config.hipWeight.val;
         weights[3] = config.lHandWeight.val;
         weights[4] = config.rHandWeight.val;
         weights[5] = config.lArmWeight.val;
         weights[6] = config.rArmWeight.val;
         float totalWeight = 0f;
         for (int i = 0; i < 7; i++) totalWeight += weights[i];
         if (totalWeight < 0.001f) return null;
         float roll = UnityEngine.Random.Range(0f, totalWeight);
         float cumulative = 0f;
         for (int i = 0; i < 7; i++)
         {
             cumulative += weights[i];
             if (roll < cumulative)
             {
                 Transform part = GetBodyPartTransform(atom, i);
                 if (part != null)
                 {
                     def.selectedBodyPart = i;
                     return part;
                 }
             }
         }
         return GetBodyPartTransform(atom, BodyPart.Head);
     }
     private Transform GetBodyPartTransform(Atom atom, int partIndex)
     {
         if (atom == null) return null;
         switch (partIndex)
         {
             case BodyPart.Head:
                 var headControl = atom.GetStorableByID("headControl") as FreeControllerV3;
                 if (headControl != null) return headControl.transform;
                 var headBone = atom.GetStorableByID("head") as DAZBone;
                 if (headBone != null) return headBone.transform;
                 break;
             case BodyPart.Chest:
                 var chestBone = atom.GetStorableByID("chest") as DAZBone;
                 if (chestBone != null) return chestBone.transform;
                 break;
             case BodyPart.Hip:
                 var hipBone = atom.GetStorableByID("hip") as DAZBone;
                 if (hipBone != null) return hipBone.transform;
                 break;
             case BodyPart.LHand:
                 var lHandControl = atom.GetStorableByID("lHand") as FreeControllerV3;
                 if (lHandControl != null) return lHandControl.transform;
                 var lHandBone = FindBone(atom, "lHand");
                 if (lHandBone != null) return lHandBone;
                 break;
             case BodyPart.RHand:
                 var rHandControl = atom.GetStorableByID("rHand") as FreeControllerV3;
                 if (rHandControl != null) return rHandControl.transform;
                 var rHandBone = FindBone(atom, "rHand");
                 if (rHandBone != null) return rHandBone;
                 break;
             case BodyPart.LArm:
                 var lArmCtrl = atom.GetStorableByID("lArmControl") as FreeControllerV3;
                 if (lArmCtrl != null) return lArmCtrl.transform;
                 var lShldrBone = FindBone(atom, "lShldr");
                 if (lShldrBone != null) return lShldrBone;
                 var lShoulderBone = FindBone(atom, "lShoulder");
                 if (lShoulderBone != null) return lShoulderBone;
                 var lForeArmFallback = FindBone(atom, "lForeArm");
                 if (lForeArmFallback != null) return lForeArmFallback;
                 break;
             case BodyPart.RArm:
                 var rArmCtrl = atom.GetStorableByID("rArmControl") as FreeControllerV3;
                 if (rArmCtrl != null) return rArmCtrl.transform;
                 var rShldrBone = FindBone(atom, "rShldr");
                 if (rShldrBone != null) return rShldrBone;
                 var rShoulderBone = FindBone(atom, "rShoulder");
                 if (rShoulderBone != null) return rShoulderBone;
                 var rForeArmFallback = FindBone(atom, "rForeArm");
                 if (rForeArmFallback != null) return rForeArmFallback;
                 break;
         }
         return null;
     }
     private Transform FindBone(Atom atom, string boneName)
     {
         if (atom == null) return null;
         var bones = atom.GetComponentsInChildren<DAZBone>();
         if (bones == null) return null;
         for (int i = 0; i < bones.Length; i++)
         {
             if (bones[i] != null && bones[i].name == boneName)
                 return bones[i].transform;
         }
         return null;
     }
     private void GenerateNothing(TargetDef def, Transform reference, Vector3 origin)
     {
         def.randomCachedTransform = null;
         def.randomCachedAtom = null;
         def.selectedBodyPart = BodyPart.Head;
         def.glanceType = GlanceType.Quick;
         def.glanceDuration = 0f;
         float yaw = UnityEngine.Random.Range(-10f, 10f);
         float pitch = UnityEngine.Random.Range(-10f, 10f);
         Vector3 localDir = DirectionFromAngles(yaw, pitch);
         Vector3 worldDir = reference.TransformDirection(localDir);
         def.randomVirtualPosition = origin + worldDir.normalized * 10f;
     }
     private void GenerateSelfHands(TargetDef def, Transform reference, Vector3 origin, float maxAngleH, float maxAngleV)
     {
         CacheSelfHands();
         if (selfLHand == null && selfRHand == null)
         {
             GenerateVirtualPoint(def, reference, origin, maxAngleH, maxAngleV);
             return;
         }
         List<Transform> validHands = new List<Transform>();
         if (selfLHand != null && IsWithinHeadLimits(reference, selfLHand.position, origin, maxAngleH, maxAngleV))
             validHands.Add(selfLHand);
         if (selfRHand != null && IsWithinHeadLimits(reference, selfRHand.position, origin, maxAngleH, maxAngleV))
             validHands.Add(selfRHand);
         if (validHands.Count == 0)
         {
             GenerateVirtualPoint(def, reference, origin, maxAngleH, maxAngleV);
             return;
         }
         int idx = validHands.Count > 1 ? UnityEngine.Random.Range(0, validHands.Count) : 0;
         Transform hand = validHands[idx];
         def.randomCachedTransform = hand;
         def.randomVirtualPosition = hand.position;
         def.randomCachedAtom = null;
         def.selectedBodyPart = hand == selfLHand ? BodyPart.LHand : BodyPart.RHand;
         def.glanceType = GlanceType.Sustained;
         def.glanceDuration = 0f;
     }
     private void GenerateVirtualPoint(TargetDef def, Transform reference, Vector3 origin, float maxAngleH, float maxAngleV)
     {
         def.randomCachedTransform = null;
         def.randomCachedAtom = null;
         def.selectedBodyPart = BodyPart.Head;
         def.glanceType = GlanceType.Quick;
         def.glanceDuration = 0f;
         float yaw = UnityEngine.Random.Range(-maxAngleH, maxAngleH);
         float pitch;
         bool lookUp = UnityEngine.Random.value < 0.5f;
         if (lookUp)
         {
             float maxUp = maxAngleV * config.upBias.val;
             pitch = UnityEngine.Random.Range(0f, maxUp);
         }
         else
         {
             float maxDown = maxAngleV * config.downBias.val;
             pitch = UnityEngine.Random.Range(-maxDown, 0f);
         }
         Vector3 localDir = DirectionFromAngles(yaw, pitch);
         Vector3 worldDir = reference.TransformDirection(localDir);
         def.randomVirtualPosition = origin + worldDir.normalized * config.virtualDistance.val;
     }
     private Vector3 DirectionFromAngles(float yawDeg, float pitchDeg)
     {
         float yawRad = yawDeg * Mathf.Deg2Rad;
         float pitchRad = pitchDeg * Mathf.Deg2Rad;
         return new Vector3(
             Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
             Mathf.Sin(pitchRad),
             Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)
         );
     }
     private bool IsWithinHeadLimits(Transform reference, Vector3 targetPos, Vector3 origin, float maxAngleH, float maxAngleV)
     {
         Vector3 dir = targetPos - origin;
         if (dir.sqrMagnitude < 0.0001f) return true;
         Vector3 localDir = reference.InverseTransformDirection(dir);
         float horizontalMagnitude = new Vector2(localDir.x, localDir.z).magnitude;
         if (horizontalMagnitude < SingularThreshold) return true;
         float yawDeg = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
         float pitchDeg = Mathf.Atan2(localDir.y, horizontalMagnitude) * Mathf.Rad2Deg;
         float maxYaw = maxAngleH - HeadAngleMarginDeg;
         float maxPitch = maxAngleV - HeadAngleMarginDeg;
         if (maxYaw < 0f) maxYaw = 0f;
         if (maxPitch < 0f) maxPitch = 0f;
         return Mathf.Abs(yawDeg) <= maxYaw && Mathf.Abs(pitchDeg) <= maxPitch;
     }
     private void ScanCharacters()
     {
         characters.Clear();
         playerCameraTransform = null;
         var sc = SuperController.singleton;
         if (sc == null) return;
         if (sc.centerCameraTarget != null)
             playerCameraTransform = sc.centerCameraTarget.transform;
         List<Atom> atoms = null;
         try { atoms = sc.GetAtoms(); }
         catch (System.Exception) { return; }
         if (atoms == null) return;
         for (int i = 0; i < atoms.Count; i++)
         {
             var a = atoms[i];
             if (a == null) continue;
             if (a == selfAtom) continue;
             if (a.type != "Person") continue;
             if (!a.on) continue;
             Transform headT = null;
             var hc = a.GetStorableByID("headControl") as FreeControllerV3;
             if (hc != null)
             {
                 headT = hc.transform;
             }
             else
             {
                 var headBone = a.GetStorableByID("head") as DAZBone;
                 if (headBone != null) headT = headBone.transform;
             }
             if (headT == null) continue;
             var info = new CharacterInfo();
             info.atom = a;
             info.headTransform = headT;
             characters.Add(info);
         }
     }
     private void CacheSelfHands()
     {
         if (handsCached || selfAtom == null) return;
         DAZBone[] bones = selfAtom.GetComponentsInChildren<DAZBone>();
         if (bones != null)
         {
             for (int i = 0; i < bones.Length; i++)
             {
                 if (bones[i] == null) continue;
                 if (bones[i].name == "lHand") selfLHand = bones[i].transform;
                 else if (bones[i].name == "rHand") selfRHand = bones[i].transform;
             }
         }
         if (selfLHand == null || selfRHand == null)
         {
             Transform rescale = selfAtom.transform.Find("rescale2");
             if (rescale != null)
             {
                 DAZBone[] subBones = rescale.GetComponentsInChildren<DAZBone>();
                 if (subBones != null)
                 {
                     for (int i = 0; i < subBones.Length; i++)
                     {
                         if (subBones[i] == null) continue;
                         if (selfLHand == null && subBones[i].name == "lHand") selfLHand = subBones[i].transform;
                         if (selfRHand == null && subBones[i].name == "rHand") selfRHand = subBones[i].transform;
                     }
                 }
             }
         }
         if (selfLHand == null || selfRHand == null)
         {
             var lHandStorable = selfAtom.GetStorableByID("lHand");
             var rHandStorable = selfAtom.GetStorableByID("rHand");
             if (lHandStorable != null) selfLHand = lHandStorable.transform;
             if (rHandStorable != null) selfRHand = rHandStorable.transform;
         }
         handsCached = true;
     }
 }
}