using System.Collections.Generic;
using UnityEngine;

namespace Norm
{
    public class AttentionSystem
    {
        private AttentionConfig config;
        private BodyPartsConfig bodyPartsConfig;
        private Atom selfAtom;
        private Transform selfHeadTransform;

        private const float ScanInterval = 0.3f;
        private const float MoveThreshold = 0.02f;
        private const float ApproachThreshold = -0.05f;
        private const float MinModifier = 0.2f;
        private const float MaxModifier = 1.5f;
        private const int MovementDetectionPoints = 5;

        // C5: IOR - подавление на N выборов (счётчик вместо времени)
        public const int SuppressionSelectionCount = 2;

        // C6: Head approach
        private const float HeadApproachProximityThreshold = 0.7f;
        private const float HeadApproachMinSpeed = 0.02f;

        // C7: Dance
        private const float DanceMoveThreshold = 0.03f;
        private const float DanceEndGracePeriod = 5f;
        private const float DanceCalmDownMin = 20f;
        private const float DanceCalmDownMax = 100f;
        private const float DanceBoostMultiplier = 1.5f;
        private const float DanceBoostFullDuration = 10f;
        private const float DanceBoostFadeDuration = 25f;

        // D1: порог близости времени движения для перемешивания
        private const float TieEpsilon = 0.15f;

        private class CharacterAttentionInfo
        {
            public Atom atom;
            public Transform[] points = new Transform[7];
            public Vector3[] lastPositions = new Vector3[7];
            public float[] lastDistances = new float[7];
            public float[] pointLastMoveTime = new float[7];
            public float[] pointLastAbsoluteMoveTime = new float[7];
            public float[] pointDistancesToSelf = new float[7];
            public float[] lastPointDistancesToSelf = new float[7];
            public int[] suppressCounter = new int[7];
            public float lastMoveTime;
            public float currentModifier = 1f;
            public List<float> eventTimes = new List<float>();
            public bool initialized;
            public Transform headTransform;
            public bool wasMoving = false;
            public bool wasFatigued = false;
            public float lastMoveCheckTime = -100f;
            public bool isMonotonous = false;

            // C7: Dance
            public bool isDancing;
            public float danceStartTime;
            public float nextDanceCalmUntil;
            public float lastDanceMoveTime;
            public bool triggerDanceScan;
        }

        private List<CharacterAttentionInfo> characters = new List<CharacterAttentionInfo>();
        private float scanTimer = -1f;

        public AttentionSystem(AttentionConfig cfg, BodyPartsConfig bodyPartsCfg)
        {
            config = cfg;
            bodyPartsConfig = bodyPartsCfg;
        }

        public void SetSelfAtom(Atom atom)
        {
            selfAtom = atom;
            selfHeadTransform = null;
            characters.Clear();
        }

        public void Update(float deltaTime)
        {
            if (selfAtom != null && selfHeadTransform == null)
            {
                var headControl = selfAtom.GetStorableByID("headControl") as FreeControllerV3;
                if (headControl != null) selfHeadTransform = headControl.transform;
                else
                {
                    var headBone = selfAtom.GetStorableByID("head") as DAZBone;
                    if (headBone != null) selfHeadTransform = headBone.transform;
                }
            }

            scanTimer -= deltaTime;
            if (scanTimer <= 0f)
            {
                ScanCharacters();
                scanTimer = ScanInterval;
            }

            UpdateModifiers();
        }

        public float GetModifierForCharacter(Atom atom)
        {
            if (atom == null) return 1f;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom == atom)
                    return characters[i].currentModifier;
            }
            return 1f;
        }

        public bool IsDancing(Atom atom)
        {
            if (atom == null) return false;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom == atom)
                    return characters[i].isDancing;
            }
            return false;
        }

        public float GetMinEffectiveModifier()
        {
            if (config == null) return 1f;
            if (config.minFatigueModifier == null || config.attentionBoost == null) return 1f;
            float idleMin = config.minFatigueModifier.val;
            float monotonyMin = config.attentionBoost.val * 0.5f;
            float candidate = idleMin < monotonyMin ? idleMin : monotonyMin;
            if (candidate < MinModifier) candidate = MinModifier;
            if (candidate > MaxModifier) candidate = MaxModifier;
            return candidate;
        }

        public List<Transform> GetMovingBodyParts(Atom atom)
        {
            List<Transform> result = new List<Transform>();
            if (atom == null) return result;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom == atom)
                {
                    float now = Time.time;
                    float decayTime = bodyPartsConfig != null ? bodyPartsConfig.movementDecayTime.val : 2f;
                    for (int p = 0; p < MovementDetectionPoints; p++)
                    {
                        if (characters[i].points[p] == null) continue;
                        float timeSinceMove = now - characters[i].pointLastMoveTime[p];
                        if (timeSinceMove < decayTime)
                        {
                            result.Add(characters[i].points[p]);
                        }
                    }
                    break;
                }
            }
            return result;
        }

        public int GetMovingPartsMask(Atom atom)
        {
            if (atom == null) return 0;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom != atom) continue;
                float now = Time.time;
                float decayTime = bodyPartsConfig != null ? bodyPartsConfig.movementDecayTime.val : 2f;
                int mask = 0;
                for (int p = 0; p < MovementDetectionPoints; p++)
                {
                    if (characters[i].points[p] == null) continue;
                    if (now - characters[i].pointLastMoveTime[p] < decayTime)
                        mask |= (1 << p);
                }
                return mask;
            }
            return 0;
        }

        public int GetAbsoluteMovingPartsMask(Atom atom)
        {
            if (atom == null) return 0;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom != atom) continue;
                float now = Time.time;
                float decayTime = bodyPartsConfig != null ? bodyPartsConfig.movementDecayTime.val : 2f;
                int mask = 0;
                for (int p = 0; p < MovementDetectionPoints; p++)
                {
                    if (characters[i].points[p] == null) continue;
                    if (now - characters[i].pointLastAbsoluteMoveTime[p] < decayTime)
                        mask |= (1 << p);
                }
                return mask;
            }
            return 0;
        }

        public Atom GetMostActiveHandCharacter(out int handPart)
        {
            handPart = -1;
            Atom bestAtom = null;
            float bestTime = float.MaxValue;
            float now = Time.time;
            float decayTime = bodyPartsConfig != null ? bodyPartsConfig.movementDecayTime.val : 2f;

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom == null || !characters[i].atom.on) continue;
                for (int p = BodyPart.LHand; p <= BodyPart.RHand; p++)
                {
                    if (characters[i].points[p] == null) continue;
                    float dt = now - characters[i].pointLastAbsoluteMoveTime[p];
                    if (dt < decayTime && dt < bestTime)
                    {
                        bestTime = dt;
                        bestAtom = characters[i].atom;
                        handPart = p;
                    }
                }
            }
            return bestAtom;
        }

        public Transform GetCachedBodyPart(Atom atom, int partIndex)
        {
            if (atom == null || partIndex < 0 || partIndex >= 7) return null;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom != atom) continue;
                return characters[i].points[partIndex];
            }
            return null;
        }

        public float GetDistanceFactor(Atom atom, Transform point)
        {
            if (atom == null || point == null || selfHeadTransform == null) return 1f;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom == atom)
                {
                    for (int p = 0; p < 7; p++)
                    {
                        if (characters[i].points[p] == point)
                        {
                            float distance = characters[i].pointDistancesToSelf[p];
                            float near = bodyPartsConfig != null ? bodyPartsConfig.distanceNear.val : 0.5f;
                            float far = bodyPartsConfig != null ? bodyPartsConfig.distanceFar.val : 2.0f;
                            float t = Mathf.Clamp01((far - distance) / (far - near));
                            return Mathf.Lerp(0.5f, 2.0f, t);
                        }
                    }
                    break;
                }
            }
            return 1f;
        }

        public float GetMovementFactor(Atom atom, Transform point)
        {
            if (atom == null || point == null) return 1f;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom == atom)
                {
                    for (int p = 0; p < MovementDetectionPoints; p++)
                    {
                        if (characters[i].points[p] == point)
                        {
                            float now = Time.time;
                            float timeSinceMove = now - characters[i].pointLastAbsoluteMoveTime[p];
                            float decayTime = bodyPartsConfig != null ? bodyPartsConfig.movementDecayTime.val : 2f;
                            return 1f + 2f * Mathf.Exp(-timeSinceMove / decayTime);
                        }
                    }
                    break;
                }
            }
            return 1f;
        }

        public bool IsPointMoving(Atom atom, Transform point)
        {
            if (atom == null || point == null) return false;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom == atom)
                {
                    float now = Time.time;
                    float decayTime = bodyPartsConfig != null ? bodyPartsConfig.movementDecayTime.val : 2f;
                    for (int p = 0; p < 7; p++)
                    {
                        if (characters[i].points[p] == point)
                        {
                            float timeSinceMove = now - characters[i].pointLastAbsoluteMoveTime[p];
                            return timeSinceMove < decayTime;
                        }
                    }
                    break;
                }
            }
            return false;
        }

        public bool GetFastestMovingPointIndex(Atom atom, out int partIndex, out float timeSinceMove)
        {
            partIndex = -1;
            timeSinceMove = float.MaxValue;
            if (atom == null) return false;

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom != atom) continue;
                float now = Time.time;
                float best = float.MaxValue;
                int bestIdx = -1;
                for (int p = 0; p < MovementDetectionPoints; p++)
                {
                    if (characters[i].points[p] == null) continue;
                    float dt = now - characters[i].pointLastAbsoluteMoveTime[p];
                    if (dt < best) { best = dt; bestIdx = p; }
                }
                if (bestIdx < 0) return false;
                partIndex = bestIdx;
                timeSinceMove = best;
                return true;
            }
            return false;
        }

        // C5: IOR - запись подавления (счётчик выборов)
        public void RecordGlance(Atom atom, int partIndex)
        {
            if (atom == null || partIndex < 0 || partIndex >= 7) return;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom == atom)
                {
                    characters[i].suppressCounter[partIndex] = SuppressionSelectionCount;
                    return;
                }
            }
        }

        // C5: IOR - проверка подавления
        public bool IsSuppressed(Atom atom, int partIndex)
        {
            if (atom == null || partIndex < 0 || partIndex >= 7) return false;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom == atom)
                    return characters[i].suppressCounter[partIndex] > 0;
            }
            return false;
        }

        // C5: IOR - декремент счётчиков после успешного выбора
        public void DecrementSuppression(Atom atom)
        {
            if (atom == null) return;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom == atom)
                {
                    for (int p = 0; p < 7; p++)
                    {
                        if (characters[i].suppressCounter[p] > 0)
                            characters[i].suppressCounter[p]--;
                    }
                    return;
                }
            }
        }

		 public bool IsRayOccludedByCharacters(Vector3 rayOrigin, Vector3 rayTarget, Atom excludeAtom)
		 {
			 Vector3 dir = rayTarget - rayOrigin;
			 float maxDist = dir.magnitude;
			 if (maxDist < 0.001f) return false;
			 dir /= maxDist;

			 for (int i = 0; i < characters.Count; i++)
			 {
				 var info = characters[i];
				 if (info.atom == null || !info.atom.on) continue;
				 if (info.atom == excludeAtom) continue;

				 if (IsSphereOnRay(rayOrigin, dir, maxDist, info.points[BodyPart.Head], 0.10f)) return true;
				 if (IsSphereOnRay(rayOrigin, dir, maxDist, info.points[BodyPart.Chest], 0.25f)) return true;
				 if (IsSphereOnRay(rayOrigin, dir, maxDist, info.points[BodyPart.Hip], 0.25f)) return true;
			 }
			 return false;
		 }

		 private bool IsSphereOnRay(Vector3 origin, Vector3 dir, float maxDist, Transform sphereCenter, float radius)
		 {
			 if (sphereCenter == null) return false;
			 Vector3 toCenter = sphereCenter.position - origin;
			 float projection = Vector3.Dot(toCenter, dir);
			 if (projection < 0f || projection > maxDist) return false;
			 Vector3 closestPoint = origin + dir * projection;
			 float distSq = (closestPoint - sphereCenter.position).sqrMagnitude;
			 return distSq <= radius * radius;
		 }


        // C6: поиск приближающейся головы
        public Atom GetApproachingHead(out float approachSpeed, out float currentDistance)
        {
            approachSpeed = 0f;
            currentDistance = float.MaxValue;
            Atom bestAtom = null;
            float bestSpeed = HeadApproachMinSpeed;

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom == null || !characters[i].atom.on) continue;
                if (characters[i].points[0] == null) continue;

                float dist = characters[i].pointDistancesToSelf[0];
                float lastDist = characters[i].lastPointDistancesToSelf[0];
                float speed = lastDist - dist;

                if (dist <= HeadApproachProximityThreshold && speed >= bestSpeed)
                {
                    bestSpeed = speed;
                    bestAtom = characters[i].atom;
                    approachSpeed = speed;
                    currentDistance = dist;
                }
            }
            return bestAtom;
        }

        // C7: получение отфильтрованных частей для обхода
        public int GetMultiScanCandidates(Atom atom, int[] outParts)
        {
            if (atom == null || outParts == null) return 0;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom != atom) continue;
                return GetMultiScanCandidatesInternal(characters[i], outParts);
            }
            return 0;
        }

        // C7: поиск лучшего персонажа для обхода
        public Atom GetMultiScanCandidate(int[] outParts, out int count)
        {
            count = 0;
            Atom bestAtom = null;
            int bestCount = 0;
            float bestRecentTime = -1f;
            int[] tempParts = new int[7];

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].atom == null || !characters[i].atom.on) continue;
                int tempCount = GetMultiScanCandidatesInternal(characters[i], tempParts);
                if (tempCount < 2) continue;

                float recentTime = GetMostRecentAbsoluteMoveTime(characters[i]);
                if (tempCount > bestCount || (tempCount == bestCount && recentTime > bestRecentTime))
                {
                    bestCount = tempCount;
                    bestAtom = characters[i].atom;
                    bestRecentTime = recentTime;
                    if (outParts != null)
                    {
                        for (int p = 0; p < tempCount && p < outParts.Length; p++)
                        {
                            outParts[p] = tempParts[p];
                        }
                    }
                }
            }
            count = bestCount;
            return bestAtom;
        }

        // C7: Получение атома, начавшего танец после периода спокойствия
        public Atom GetDanceScanCandidate(int[] outParts, out int count)
        {
            count = 0;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].triggerDanceScan)
                {
                    characters[i].triggerDanceScan = false;
                    count = GetMultiScanCandidatesInternal(characters[i], outParts);
                    return characters[i].atom;
                }
            }
            return null;
        }

        private int GetMultiScanCandidatesInternal(CharacterAttentionInfo info, int[] outParts)
        {
            float now = Time.time;
            float decayTime = bodyPartsConfig != null ? bodyPartsConfig.movementDecayTime.val : 2f;
            int candidateCount = 0;
            int[] candidates = new int[5];
            float[] moveTimes = new float[5];

            for (int p = 0; p < 7; p++)
            {
                if (p == BodyPart.LHand || p == BodyPart.RHand) continue;
                if (info.points[p] == null) continue;
                if (now - info.pointLastAbsoluteMoveTime[p] >= decayTime) continue;
                candidates[candidateCount] = p;
                moveTimes[candidateCount] = info.pointLastAbsoluteMoveTime[p];
                candidateCount++;
            }

            bool hasChestOrArm = false;
            for (int c = 0; c < candidateCount; c++)
            {
                if (candidates[c] == BodyPart.Chest || candidates[c] == BodyPart.LArm || candidates[c] == BodyPart.RArm)
                {
                    hasChestOrArm = true;
                    break;
                }
            }

            if (!hasChestOrArm)
            {
                for (int c = candidateCount - 1; c >= 0; c--)
                {
                    if (candidates[c] == BodyPart.Head)
                    {
                        for (int s = c; s < candidateCount - 1; s++)
                        {
                            candidates[s] = candidates[s + 1];
                            moveTimes[s] = moveTimes[s + 1];
                        }
                        candidateCount--;
                    }
                }
            }

            if (candidateCount <= 1) return 0;

            // Сортировка по убыванию времени последнего движения
            for (int a = 0; a < candidateCount - 1; a++)
            {
                for (int b = a + 1; b < candidateCount; b++)
                {
                    if (moveTimes[b] > moveTimes[a])
                    {
                        int tmpP = candidates[a]; candidates[a] = candidates[b]; candidates[b] = tmpP;
                        float tmpT = moveTimes[a]; moveTimes[a] = moveTimes[b]; moveTimes[b] = tmpT;
                    }
                }
            }

            // D1: перемешивание групп кандидатов с близким временем движения
            int groupStart = 0;
            while (groupStart < candidateCount)
            {
                int groupEnd = groupStart + 1;
                while (groupEnd < candidateCount && Mathf.Abs(moveTimes[groupEnd] - moveTimes[groupStart]) < TieEpsilon)
                {
                    groupEnd++;
                }

                // Fisher-Yates shuffle для группы [groupStart, groupEnd)
                for (int a = groupEnd - 1; a > groupStart; a--)
                {
                    int b = UnityEngine.Random.Range(groupStart, a + 1);
                    if (a != b)
                    {
                        int tmpP = candidates[a]; candidates[a] = candidates[b]; candidates[b] = tmpP;
                        float tmpT = moveTimes[a]; moveTimes[a] = moveTimes[b]; moveTimes[b] = tmpT;
                    }
                }
                groupStart = groupEnd;
            }

            int count = candidateCount > outParts.Length ? outParts.Length : candidateCount;
            for (int c = 0; c < count; c++)
            {
                outParts[c] = candidates[c];
            }
            return count;
        }

        private float GetMostRecentAbsoluteMoveTime(CharacterAttentionInfo info)
        {
            float best = -1f;
            for (int p = 0; p < 7; p++)
            {
                if (info.points[p] == null) continue;
                if (info.pointLastAbsoluteMoveTime[p] > best)
                    best = info.pointLastAbsoluteMoveTime[p];
            }
            return best;
        }

        private string GetBodyPartName(int partIndex)
        {
            switch (partIndex)
            {
                case BodyPart.Head: return "Head";
                case BodyPart.Chest: return "Chest";
                case BodyPart.Hip: return "Hip";
                case BodyPart.LHand: return "LHand";
                case BodyPart.RHand: return "RHand";
                case BodyPart.LArm: return "LArm";
                case BodyPart.RArm: return "RArm";
                default: return "Unknown";
            }
        }

        private void ScanCharacters()
        {
            var sc = SuperController.singleton;
            if (sc == null) return;

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

                CharacterAttentionInfo info = null;
                for (int j = 0; j < characters.Count; j++)
                {
                    if (characters[j].atom == a)
                    {
                        info = characters[j];
                        break;
                    }
                }

                if (info == null)
                {
                    info = new CharacterAttentionInfo();
                    info.atom = a;
                    characters.Add(info);
                }

                CachePoints(a, info);
            }

            // Удаление персонажей, которых больше нет в сцене
            for (int i = characters.Count - 1; i >= 0; i--)
            {
                bool found = false;
                for (int j = 0; j < atoms.Count; j++)
                {
                    if (atoms[j] == characters[i].atom)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    characters.RemoveAt(i);
                }
            }
        }

        private void CachePoints(Atom atom, CharacterAttentionInfo info)
        {
            var headControl = atom.GetStorableByID("headControl") as FreeControllerV3;
            if (headControl != null)
            {
                info.points[0] = headControl.transform;
                info.headTransform = headControl.transform;
            }
            else
            {
                var headBone = FindBone(atom, "head");
                if (headBone != null)
                {
                    info.points[0] = headBone;
                    info.headTransform = headBone;
                }
            }

            info.points[1] = FindBone(atom, "chest");
            info.points[2] = FindBone(atom, "hip");

            var lHandControl = atom.GetStorableByID("lHand") as FreeControllerV3;
            if (lHandControl != null) info.points[3] = lHandControl.transform;
            else info.points[3] = FindBone(atom, "lHand");

            var rHandControl = atom.GetStorableByID("rHand") as FreeControllerV3;
            if (rHandControl != null) info.points[4] = rHandControl.transform;
            else info.points[4] = FindBone(atom, "rHand");

            var lArmCtrl = atom.GetStorableByID("lArmControl") as FreeControllerV3;
            if (lArmCtrl != null) info.points[5] = lArmCtrl.transform;
            else info.points[5] = FindBone(atom, "lShldr");
            if (info.points[5] == null) info.points[5] = FindBone(atom, "lShoulder");
            if (info.points[5] == null) info.points[5] = FindBone(atom, "lForeArm");

            var rArmCtrl = atom.GetStorableByID("rArmControl") as FreeControllerV3;
            if (rArmCtrl != null) info.points[6] = rArmCtrl.transform;
            else info.points[6] = FindBone(atom, "rShldr");
            if (info.points[6] == null) info.points[6] = FindBone(atom, "rShoulder");
            if (info.points[6] == null) info.points[6] = FindBone(atom, "rForeArm");
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

        private void UpdateModifiers()
        {
            float now = Time.time;
            float fatigueDelay = config.fatigueDelay.val;
            float minMod = config.minFatigueModifier.val;
            float boost = config.attentionBoost.val;
            float window = config.monotonyWindow.val;
            float threshold = config.monotonyThreshold.val;
            float decayRate = config.attentionDecayRate.val;

            for (int i = 0; i < characters.Count; i++)
            {
                var info = characters[i];
                if (info.atom == null || !info.atom.on)
                {
                    characters.RemoveAt(i);
                    i--;
                    continue;
                }

                bool moved = false;

                if (!info.initialized)
                {
                    for (int p = 0; p < 7; p++)
                    {
                        if (info.points[p] != null)
                        {
                            info.lastPositions[p] = info.points[p].position;
                            info.pointLastMoveTime[p] = now;
                            info.pointLastAbsoluteMoveTime[p] = now;
                            info.suppressCounter[p] = 0;

                            if (info.headTransform != null)
                                info.lastDistances[p] = Vector3.Distance(info.points[p].position, info.headTransform.position);

                            if (selfHeadTransform != null)
                            {
                                info.pointDistancesToSelf[p] = Vector3.Distance(info.points[p].position, selfHeadTransform.position);
                                info.lastPointDistancesToSelf[p] = info.pointDistancesToSelf[p];
                            }
                        }
                    }
                    info.lastMoveTime = now;
                    info.lastMoveCheckTime = now;
                    info.initialized = true;
                    moved = true;
                }
                else
                {
                    bool checkMovement = (now - info.lastMoveCheckTime >= ScanInterval);
                    if (checkMovement)
                    {
                        info.lastMoveCheckTime = now;
                        bool hipMovedDance = false;
                        bool chestMovedDance = false;

                        for (int p = 0; p < 7; p++)
                        {
                            if (info.points[p] == null) continue;
                            Vector3 current = info.points[p].position;
                            float dist = Vector3.Distance(current, info.lastPositions[p]);

                            if (dist > MoveThreshold)
                            {
                                info.pointLastAbsoluteMoveTime[p] = now;
                                if (p < MovementDetectionPoints)
                                {
                                    info.pointLastMoveTime[p] = now;
                                    moved = true;
                                }
                            }

                            if (p == BodyPart.Hip && dist >= DanceMoveThreshold) hipMovedDance = true;
                            if (p == BodyPart.Chest && dist >= DanceMoveThreshold) chestMovedDance = true;

                            info.lastPositions[p] = current;

                            if (info.headTransform != null && p != 0)
                            {
                                float currentDist = Vector3.Distance(current, info.headTransform.position);
                                float distChange = currentDist - info.lastDistances[p];
                                if (p < MovementDetectionPoints && distChange < ApproachThreshold)
                                {
                                    info.pointLastMoveTime[p] = now;
                                    moved = true;
                                }
                                info.lastDistances[p] = currentDist;
                            }

                            if (selfHeadTransform != null)
                            {
                                info.lastPointDistancesToSelf[p] = info.pointDistancesToSelf[p];
                                info.pointDistancesToSelf[p] = Vector3.Distance(current, selfHeadTransform.position);
                            }
                        }

                        // C7: Dance detection
                        if (hipMovedDance && chestMovedDance)
                        {
                            if (!info.isDancing)
                            {
                                info.isDancing = true;
                                info.danceStartTime = now;
                                if (now >= info.nextDanceCalmUntil)
                                {
                                    info.triggerDanceScan = true;
                                }
                            }
                            info.lastDanceMoveTime = now;
                        }
                        else if (info.isDancing)
                        {
                            if (now - info.lastDanceMoveTime >= DanceEndGracePeriod)
                            {
                                info.isDancing = false;
                                info.nextDanceCalmUntil = now + UnityEngine.Random.Range(DanceCalmDownMin, DanceCalmDownMax);
                            }
                        }

                        if (moved)
                        {
                            info.lastMoveTime = now;
                            info.eventTimes.Add(now);
                        }

                        // Monotony check
                        float windowStart = now - window;
                        int recentEvents = 0;
                        for (int e = info.eventTimes.Count - 1; e >= 0; e--)
                        {
                            if (info.eventTimes[e] >= windowStart)
                            {
                                recentEvents++;
                            }
                            else
                            {
                                info.eventTimes.RemoveRange(0, e + 1);
                                break;
                            }
                        }
                        info.isMonotonous = (recentEvents > threshold);
                    }
                }

                // Calculate modifier
                float timeSinceMove = now - info.lastMoveTime;
                float rawModifier = 1f;

                if (moved)
                {
                    rawModifier = boost;
                    info.wasMoving = true;
                    info.wasFatigued = false;
                }
                else
                {
                    if (timeSinceMove < fatigueDelay)
                    {
                        rawModifier = boost;
                    }
                    else
                    {
                        info.wasMoving = false;
                        float t = (timeSinceMove - fatigueDelay) * decayRate;
                        t = Mathf.Clamp01(t);
                        rawModifier = Mathf.Lerp(boost, minMod, t);
                        if (t >= 1f)
                        {
                            info.wasFatigued = true;
                        }
                    }
                }

                if (info.isMonotonous && !info.isDancing)
                {
                    rawModifier *= 0.5f;
                }

                // Dance boost
                float currentBoost = 1f;
                float danceDuration = now - info.danceStartTime;
                if (info.isDancing)
                {
                    if (danceDuration <= DanceBoostFullDuration)
                    {
                        currentBoost = DanceBoostMultiplier;
                    }
                    else
                    {
                        float fadeT = (danceDuration - DanceBoostFullDuration) / DanceBoostFadeDuration;
                        currentBoost = Mathf.Lerp(DanceBoostMultiplier, 1.0f, Mathf.Clamp01(fadeT));
                    }
                }
                else if (danceDuration > DanceBoostFullDuration)
                {
                    float fadeT = (danceDuration - DanceBoostFullDuration) / DanceBoostFadeDuration;
                    currentBoost = Mathf.Lerp(DanceBoostMultiplier, 1.0f, Mathf.Clamp01(fadeT));
                }

                rawModifier *= currentBoost;
                info.currentModifier = Mathf.Clamp(rawModifier, MinModifier, 3.0f);
            }
        }
    }
}