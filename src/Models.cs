using System.Collections.Generic;
using UnityEngine;
namespace Norm
{
    public static class BodyPart
    {
        public const int Head = 0;
        public const int Chest = 1;
        public const int Hip = 2;
        public const int LHand = 3;
        public const int RHand = 4;
        public const int LArm = 5;
        public const int RArm = 6;
    }
    public static class GlanceType
    {
        public const int Quick = 0;
        public const int Sustained = 1;
    }
    public class TargetDef
    {
        public int targetId;
        public Atom atom;
        public string controlName;
        public bool isPlayer;
        public bool isHold = false;
        public bool isRandom = false;
        public float weight = 1f;
        public float durationMin = 1f;
        public float durationMax = 5f;
        public float speedMin = 5f;
        public float speedMax = 5f;
        public int cooldown = 2;
        public float headMasterOffset = -1f;
        public float scatter = 0f;
        public float anchorYaw = 0f;
        public float anchorPitch = 0f;
        public bool watchPlayer = true;
        public bool watchPersons = true;
        public Transform cachedTransform;
        public Vector3 cachedScatterOffset;
        public Transform cachedLEye;
        public Transform cachedREye;
        public Transform cachedMouth;
        public Transform cachedHeadControl;
        public bool faceBonesCached;
        public bool isGroupExpanded = false;
		public int cooldownCounter = 0;
        public bool gazeAversionEnabled = true;
        public bool defaultGazeAversion = true;
        public bool headTurnEnabled = true;
        public Vector3 randomVirtualPosition;
        public Transform randomCachedTransform;
        public Atom randomCachedAtom;
        public int selectedBodyPart = BodyPart.Head;
        public int glanceType = GlanceType.Quick;
        public float glanceDuration = 0f;
		public bool danceDwellRequested = false;
        public string GetLabel()
        {
            if (isRandom) return "[Random]";
            if (isHold) return "[Hold]";
            if (isPlayer) return "[Player]";
            if (atom != null) return string.Format("{0}/{1}", atom.name, controlName);
            return "???";
        }
    }
    public class TargetGroup
    {
        public string name;
        public float weight = 1f;
        public float switchMin = 5f;
        public float switchMax = 15f;
        public List<TargetDef> targets = new List<TargetDef>();
        public TargetGroup() : this("unnamed") { }
        public TargetGroup(string groupName)
        {
            name = groupName;
        }
    }
    public class RandomGazeConfig
    {
        public JSONStorableFloat eyesOnlyChance;
        public JSONStorableFloat nothingWeight;
        public JSONStorableFloat selfGazeWeight;
        public JSONStorableFloat virtualTargetWeight;
        public JSONStorableFloat virtualDistance;
        public JSONStorableFloat upBias;
        public JSONStorableFloat downBias;
        public JSONStorableBool trackPlayer;
        public JSONStorableFloat personsWeight;
        public JSONStorableFloat headWeight;
        public JSONStorableFloat chestWeight;
        public JSONStorableFloat hipWeight;
        public JSONStorableFloat lHandWeight;
        public JSONStorableFloat rHandWeight;
        public JSONStorableFloat lArmWeight;
        public JSONStorableFloat rArmWeight;
		public const float PlayerWeightMultiplier = 0.6f;
        public RandomGazeConfig()
        {
            eyesOnlyChance = new JSONStorableFloat("Random Eyes Only Chance", 0.65f, 0f, 1f, true, true);
            eyesOnlyChance.storeType = JSONStorableParam.StoreType.Full;
            nothingWeight = new JSONStorableFloat("Random Nothing Weight", 0.3f, 0f, 2f, true, true);
            nothingWeight.storeType = JSONStorableParam.StoreType.Full;
            selfGazeWeight = new JSONStorableFloat("Random Self Gaze Weight", 0.1f, 0f, 2f, true, true);
            selfGazeWeight.storeType = JSONStorableParam.StoreType.Full;
            virtualTargetWeight = new JSONStorableFloat("Random Virtual Target Weight", 0.5f, 0f, 2f, true, true);
            virtualTargetWeight.storeType = JSONStorableParam.StoreType.Full;
            virtualDistance = new JSONStorableFloat("Random Virtual Distance", 3.0f, 0.5f, 10f, true, true);
            virtualDistance.storeType = JSONStorableParam.StoreType.Full;
            upBias = new JSONStorableFloat("Random Up Bias", 0.3f, 0f, 1f, true, true);
            upBias.storeType = JSONStorableParam.StoreType.Full;
            downBias = new JSONStorableFloat("Random Down Bias", 0.7f, 0f, 1f, true, true);
            downBias.storeType = JSONStorableParam.StoreType.Full;
            trackPlayer = new JSONStorableBool("Random Track Player", true);
            trackPlayer.isStorable = true;
            trackPlayer.isRestorable = true;
            trackPlayer.storeType = JSONStorableParam.StoreType.Full;
            personsWeight = new JSONStorableFloat("Random Persons Weight", 1.0f, 0f, 2f, true, true);
            personsWeight.storeType = JSONStorableParam.StoreType.Full;
            headWeight = new JSONStorableFloat("BodyParts Head Weight", 2.0f, 0f, 5f, true, true);
            headWeight.storeType = JSONStorableParam.StoreType.Full;
            chestWeight = new JSONStorableFloat("BodyParts Chest Weight", 0.5f, 0f, 5f, true, true);
            chestWeight.storeType = JSONStorableParam.StoreType.Full;
            hipWeight = new JSONStorableFloat("BodyParts Hip Weight", 0.8f, 0f, 5f, true, true);
            hipWeight.storeType = JSONStorableParam.StoreType.Full;
            lHandWeight = new JSONStorableFloat("BodyParts LHand Weight", 0.4f, 0f, 5f, true, true);
            lHandWeight.storeType = JSONStorableParam.StoreType.Full;
            rHandWeight = new JSONStorableFloat("BodyParts RHand Weight", 0.4f, 0f, 5f, true, true);
            rHandWeight.storeType = JSONStorableParam.StoreType.Full;
            lArmWeight = new JSONStorableFloat("BodyParts LArm Weight", 0.2f, 0f, 5f, true, true);
            lArmWeight.storeType = JSONStorableParam.StoreType.Full;
            rArmWeight = new JSONStorableFloat("BodyParts RArm Weight", 0.2f, 0f, 5f, true, true);
            rArmWeight.storeType = JSONStorableParam.StoreType.Full;
        }
    }
    public class AttentionConfig
    {
        public JSONStorableFloat fatigueDelay;
        public JSONStorableFloat minFatigueModifier;
        public JSONStorableFloat attentionBoost;
        public JSONStorableFloat monotonyWindow;
        public JSONStorableFloat monotonyThreshold;
        public JSONStorableFloat attentionDecayRate;
		public AttentionConfig()
        {
            fatigueDelay = new JSONStorableFloat("Attention Fatigue Delay", 10f, 1f, 30f, true, true);
            fatigueDelay.storeType = JSONStorableParam.StoreType.Full;
            minFatigueModifier = new JSONStorableFloat("Attention Min Fatigue Modifier", 0.4f, 0.1f, 1f, true, true);
            minFatigueModifier.storeType = JSONStorableParam.StoreType.Full;
            attentionBoost = new JSONStorableFloat("Attention Boost", 0.8f, 1f, 3f, true, true);
            attentionBoost.storeType = JSONStorableParam.StoreType.Full;
            monotonyWindow = new JSONStorableFloat("Attention Monotony Window", 5f, 1f, 15f, true, true);
            monotonyWindow.storeType = JSONStorableParam.StoreType.Full;
            monotonyThreshold = new JSONStorableFloat("Attention Monotony Threshold", 10f, 1f, 20f, true, true);
            monotonyThreshold.storeType = JSONStorableParam.StoreType.Full;
            attentionDecayRate = new JSONStorableFloat("Attention Decay Rate", 0.5f, 0.1f, 2f, true, true);
            attentionDecayRate.storeType = JSONStorableParam.StoreType.Full;
        }
    }
    public class BodyPartsConfig
    {
        public JSONStorableFloat distanceNear;
        public JSONStorableFloat distanceFar;
        public JSONStorableFloat movementDecayTime;
        public BodyPartsConfig()
        {
            distanceNear = new JSONStorableFloat("BodyParts Distance Near", 0.5f, 0.1f, 2f, true, true);
            distanceNear.storeType = JSONStorableParam.StoreType.Full;
            distanceFar = new JSONStorableFloat("BodyParts Distance Far", 2.0f, 1f, 5f, true, true);
            distanceFar.storeType = JSONStorableParam.StoreType.Full;
            movementDecayTime = new JSONStorableFloat("BodyParts Movement Decay", 2.0f, 0.5f, 10f, true, true);
            movementDecayTime.storeType = JSONStorableParam.StoreType.Full;
        }
    }
}