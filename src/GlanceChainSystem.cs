using UnityEngine;
namespace Norm
{
    public class GlanceStep
    {
        public Atom atom;
        public int bodyPart;
        public float minDuration;
        public float maxDuration;
        public bool isInterruptible;
    }

    public class GlanceChainSystem
    {
        private AttentionSystem attentionSystem;
        private RandomGazeController randomController;
        private TargetDef chainTargetDef;
        private bool isChainActive;
        private int currentStep;
        private float stepTimer;
        private bool stepReached;
        private float reachTimer;
        private float stepTotalTime;
        private Atom chainAtom;
        private Atom stepAtom;
        private int initialHandPart = -1;
        private TargetDef lastBaseTarget;
        private float chainCooldown;
        private float headCooldown;
        private float handCooldown;
        private TargetDef returnTarget;

        // C7: Multi-part scan state
        private bool isMultiScan;
        private int[] scanParts = new int[4];
        private int scanPartsCount;
        private int scanStepIndex;
        private int scanRound;
        private bool isFinalHeadStep;

        // C7: Dance Dwell state
        private bool isDanceDwell;
        private int[] dwellParts = new int[3];
        private int dwellPartsCount;
        private int dwellStepIndex;

        // C7-fix: PostScanHold state
        private bool isPostScanHold;

        // C4: Preemption
        private const float PreemptionCheckInterval = 0.3f;
        private float preemptionCheckTimer = 0f;

        // === C8: Состояния системы ===
        private const int StateIdle = 0;
        private const int StateChainActive = 1;
        private const int StateMultiScanActive = 2;
        private const int StateDanceDwellActive = 3;
        private const int StatePostScanHold = 4;
        private int currentState = StateIdle;

        // === Константы ===
        private const float TriggerChance = 0.5f;
        private const float HandDurationMin = 0.1f;
        private const float HandDurationMax = 0.2f;
        private const float HeadDurationMin = 0.8f;
        private const float HeadDurationMax = 2.0f;
        private const float HeadCooldownTime = 15.0f;
        private const float HandCooldownTime = 18.0f;
        private const float ReachThresholdDeg = 5f;
        private const float ReachTimeout = 0.5f;
        private const float MaxStepTotalTime = 1.5f;
        private const float HeadAngleMarginDeg = 10f;
        private const float SingularThreshold = 0.15f;
        private const float TriggerFieldOfViewDeg = 70f;
        private const float ReturnChance = 0.5f;
        private const float ReturnDurationMin = 0.4f;
        private const float ReturnDurationMax = 1.3f;
        private const float MaxReturnTotalTime = 8.0f;
        private const float HeadTriggerChanceBase = 0.7f;
        private const float HeadApproachSpeedMax = 0.04f;
        private const float HandAfterHeadChance = 0.5f;
        private const float ScanDurationMin = 0.2f;
        private const float ScanDurationMax = 0.5f;
        private const float MultiScanTriggerChance = 0.99f;
        private const int MultiScanMinParts = 3;
        private const int MultiScanMaxParts = 4;
        private const int MultiScanMaxRounds = 2;
        private const float HeadFinalChance = 0.7f;
        private const float RepeatScanChance = 0.5f;
        private const float MultiScanCooldownMin = 1f;
        private const float MultiScanCooldownMax = 5f;
        private const float DanceDwellChance = 0.5f;
        private const int DanceDwellMinParts = 2;
        private const int DanceDwellMaxParts = 3;
        private const float DwellBodyDurationMin = 0.4f;
        private const float DwellBodyDurationMax = 1.0f;
        private const float PostScanHoldMin = 0.5f;
        private const float PostScanHoldMax = 1.0f;

        public bool IsChainActive { get { return isChainActive; } }

        public GlanceChainSystem(AttentionSystem attention, RandomGazeController random)
        {
            attentionSystem = attention;
            randomController = random;
            chainTargetDef = new TargetDef();
            chainTargetDef.isRandom = true;
        }

        // === C8: Основной Update (диспетчер состояний) ===
        public void Update(float deltaTime, TargetDef baseTarget, Transform headTransform, Transform reference, float maxAngleH, float maxAngleV)
        {
            if (chainCooldown > 0f)
                chainCooldown -= deltaTime;
            if (headCooldown > 0f)
                headCooldown -= deltaTime;
            if (handCooldown > 0f)
                handCooldown -= deltaTime;

            if (baseTarget != lastBaseTarget)
            {
                if (isChainActive) Reset();
                lastBaseTarget = baseTarget;
            }

            if (baseTarget == null || !baseTarget.isRandom)
            {
                if (isChainActive) Reset();
                return;
            }

            switch (currentState)
            {
                case StateIdle:
                    UpdateIdle(baseTarget, headTransform);
                    break;
                case StateChainActive:
                case StateMultiScanActive:
                case StateDanceDwellActive:
                case StatePostScanHold:
                    UpdateActiveStep(deltaTime, headTransform, reference);
                    break;
            }
        }

        // === C8: Обновление в состоянии Idle ===
        private void UpdateIdle(TargetDef baseTarget, Transform headTransform)
        {
            if (chainCooldown > 0f) return;

            // 1. Dance Dwell
            if (baseTarget.danceDwellRequested && baseTarget.randomCachedAtom != null)
            {
                baseTarget.danceDwellRequested = false;
                Atom dwellAtom = baseTarget.randomCachedAtom;
                if (attentionSystem.IsDancing(dwellAtom))
                {
                    int[] candidates = new int[7];
                    int candidateCount = attentionSystem.GetMultiScanCandidates(dwellAtom, candidates);
                    int[] validParts = new int[DanceDwellMaxParts];
                    int validCount = 0;
                    for (int i = 0; i < candidateCount && validCount < DanceDwellMaxParts; i++)
                    {
                        int p = candidates[i];
                        if (attentionSystem.IsSuppressed(dwellAtom, p)) continue;
                        if (validCount > 0 && IsPairedPart(validParts[validCount - 1], p)) continue;
                        Transform t = attentionSystem.GetCachedBodyPart(dwellAtom, p);
                        if (t != null && IsWithinTriggerFieldOfView(headTransform, t.position))
                        {
                            validParts[validCount] = p;
                            validCount++;
                        }
                    }
                    if (validCount >= DanceDwellMinParts)
                    {
                        if (UnityEngine.Random.value < DanceDwellChance)
                        {
                            StartDanceDwell(dwellAtom, validParts, validCount);
                            return;
                        }
                    }
                }
            }

            // 2. Multi-part scan (танец)
            int danceCandidateCount = 0;
            int[] danceCandidates = new int[7];
            Atom danceAtom = attentionSystem.GetDanceScanCandidate(danceCandidates, out danceCandidateCount);
            bool isDanceStart = (danceAtom != null && danceCandidateCount >= MultiScanMinParts);
            if (isDanceStart)
            {
                int[] validParts = new int[MultiScanMaxParts];
                int validCount = 0;
                for (int i = 0; i < danceCandidateCount && validCount < MultiScanMaxParts; i++)
                {
                    int p = danceCandidates[i];
                    if (attentionSystem.IsSuppressed(danceAtom, p)) continue;
                    if (validCount > 0 && IsPairedPart(validParts[validCount - 1], p)) continue;
                    Transform t = attentionSystem.GetCachedBodyPart(danceAtom, p);
                    if (t != null && IsWithinTriggerFieldOfView(headTransform, t.position))
                    {
                        validParts[validCount] = p;
                        validCount++;
                    }
                }
                if (validCount >= MultiScanMinParts)
                {
                    if (UnityEngine.Random.value < MultiScanTriggerChance)
                    {
                        StartMultiScan(danceAtom, validParts, validCount);
                        return;
                    }
                }
                // Если танец начался, блокируем другие стимулы
                return;
            }

            // 3. Head approach + Hand movement
            Atom chosenStimulusAtom = null;
            int chosenStimulusPart = -1;
            float chosenTriggerChance = 0f;

            // Голова имеет высший приоритет
            if (headCooldown <= 0f)
            {
                float approachSpeed = 0f;
                float headDistance = 0f;
                Atom approachingHead = attentionSystem.GetApproachingHead(out approachSpeed, out headDistance);
                if (approachingHead != null)
                {
                    Transform headT = attentionSystem.GetCachedBodyPart(approachingHead, BodyPart.Head);
                    if (headT != null && IsWithinTriggerFieldOfView(headTransform, headT.position))
                    {
                        float speedFactor = Mathf.Clamp01(approachSpeed / HeadApproachSpeedMax);
                        float headChance = HeadTriggerChanceBase * speedFactor;
                        if (headChance >= 0.1f)
                        {
                            chosenStimulusAtom = approachingHead;
                            chosenStimulusPart = BodyPart.Head;
                            chosenTriggerChance = headChance;
                        }
                    }
                }
            }

            // Рука проверяется только если голова не выбрана (строгий приоритет)
            if (chosenStimulusAtom == null && handCooldown <= 0f)
            {
                Atom handAtom = null;
                int handPart = -1;
                if (baseTarget.randomCachedAtom != null)
                {
                    handAtom = baseTarget.randomCachedAtom;
                    int mask = attentionSystem.GetAbsoluteMovingPartsMask(handAtom);
                    bool lHand = (mask & (1 << BodyPart.LHand)) != 0;
                    bool rHand = (mask & (1 << BodyPart.RHand)) != 0;
                    if (!lHand && !rHand) handAtom = null;
                    else handPart = lHand ? BodyPart.LHand : BodyPart.RHand;
                }
                else
                {
                    handAtom = attentionSystem.GetMostActiveHandCharacter(out handPart);
                }

             if (handAtom != null && handPart >= 0 && !attentionSystem.IsDancing(handAtom))
             {
                 Transform handTransform = attentionSystem.GetCachedBodyPart(handAtom, handPart);
                    if (handTransform != null && IsWithinTriggerFieldOfView(headTransform, handTransform.position))
                    {
                        chosenStimulusAtom = handAtom;
                        chosenStimulusPart = handPart;
                        chosenTriggerChance = TriggerChance;
                    }
                }
            }

            if (chosenStimulusAtom != null && chosenTriggerChance > 0f)
            {
                if (UnityEngine.Random.value < chosenTriggerChance)
                {
                    StartChain(chosenStimulusAtom, chosenStimulusPart, baseTarget);
                }
            }
        }

        // === C8: Обновление активного шага ===
        private void UpdateActiveStep(float deltaTime, Transform headTransform, Transform reference)
        {
            // Preemption (только для обычной цепи)
            if (currentState == StateChainActive)
            {
                preemptionCheckTimer -= deltaTime;
                if (preemptionCheckTimer <= 0f)
                {
                    preemptionCheckTimer = PreemptionCheckInterval;
                    if (handCooldown <= 0f)
                    {
                        int newHandPart;
					 Atom newAtom = attentionSystem.GetMostActiveHandCharacter(out newHandPart);
					 if (newAtom != null && newHandPart >= 0 && !attentionSystem.IsDancing(newAtom))
                        {
                            bool isDifferentStimulus = (newAtom != chainAtom) || (newHandPart != initialHandPart);
                            if (isDifferentStimulus)
                            {
                                Transform newHandTransform = attentionSystem.GetCachedBodyPart(newAtom, newHandPart);
                                if (newHandTransform != null && IsWithinTriggerFieldOfView(headTransform, newHandTransform.position))
                                {
                                    Reset();
                                    StartChain(newAtom, newHandPart, lastBaseTarget);
                                    return;
                                }
                            }
                        }
                    }
                }
            }

            stepTotalTime += deltaTime;

            if (!stepReached)
            {
                reachTimer += deltaTime;
                if (IsHeadNearTarget(headTransform, reference) || reachTimer >= ReachTimeout)
                {
                    stepReached = true;
                }
            }
            else
            {
                stepTimer -= deltaTime;

                float stepMaxTime;
                if (currentState == StatePostScanHold) stepMaxTime = PostScanHoldMax + 2.0f;
                else if (currentState == StateMultiScanActive) stepMaxTime = MaxStepTotalTime;
                else if (currentState == StateDanceDwellActive) stepMaxTime = DwellBodyDurationMax + 1.5f;
				 else if (currentStep == 2) stepMaxTime = MaxReturnTotalTime;
				 else stepMaxTime = chainTargetDef.glanceDuration + ReachTimeout + 0.3f;

                if (stepTimer <= 0f || stepTotalTime >= stepMaxTime)
                {
                    if (attentionSystem != null && stepAtom != null)
                    {
                        attentionSystem.RecordGlance(stepAtom, chainTargetDef.selectedBodyPart);
                    }

                    currentStep++;

                    if (currentState == StatePostScanHold)
                    {
                        if (isDanceDwell) FinishDanceDwell();
                        else if (isMultiScan) FinishMultiScan();
                        else FinishChain();
                    }
                    else if (currentState == StateDanceDwellActive)
                    {
                        dwellStepIndex++;
                        if (dwellStepIndex < dwellPartsCount) SetupDwellStep(dwellParts[dwellStepIndex]);
                        else StartPostScanHold();
                    }
                    else if (currentState == StateMultiScanActive)
                    {
                        if (isFinalHeadStep) StartPostScanHold();
                        else
                        {
                            scanStepIndex++;
                            if (scanStepIndex < scanPartsCount) SetupStep(scanParts[scanStepIndex], ScanDurationMin, ScanDurationMax);
                            else
                            {
                                int lastPart = scanParts[scanPartsCount - 1];
                                if (lastPart == BodyPart.Head) StartPostScanHold();
                                else
                                {
                                    if (UnityEngine.Random.value < HeadFinalChance)
                                    {
                                        isFinalHeadStep = true;
                                        SetupStep(BodyPart.Head, ScanDurationMin, ScanDurationMax);
                                    }
                                    else if (UnityEngine.Random.value < RepeatScanChance && scanRound < MultiScanMaxRounds - 1)
                                    {
                                        StartNextMultiScanRound(headTransform);
                                    }
                                    else StartPostScanHold();
                                }
                            }
                        }
                    }
                    else // StateChainActive
                    {
                        if (currentStep == 1)
                        {
                            if (initialHandPart == -1)
                            {
                                if (UnityEngine.Random.value < HandAfterHeadChance)
                                {
                                    int handPart = PickVisibleHand(chainAtom, headTransform);
                                    if (handPart >= 0) SetupStep(handPart, HandDurationMin, HandDurationMax);
                                    else FinishChain();
                                }
                                else FinishChain();
                            }
                            else SetupStep(BodyPart.Head, HeadDurationMin, HeadDurationMax);
                        }
                        else if (currentStep == 2)
                        {
                            if (returnTarget != null && UnityEngine.Random.value < ReturnChance)
                            {
                                chainTargetDef.isRandom = returnTarget.isRandom;
                                chainTargetDef.isPlayer = returnTarget.isPlayer;
                                chainTargetDef.isHold = returnTarget.isHold;
                                chainTargetDef.atom = returnTarget.atom;
                                chainTargetDef.controlName = returnTarget.controlName;
                                chainTargetDef.randomCachedTransform = returnTarget.randomCachedTransform;
                                chainTargetDef.randomVirtualPosition = returnTarget.randomVirtualPosition;
                                chainTargetDef.randomCachedAtom = returnTarget.randomCachedAtom;
                                chainTargetDef.selectedBodyPart = returnTarget.selectedBodyPart;
                                chainTargetDef.glanceType = returnTarget.glanceType;
                                float baseDuration = UnityEngine.Random.Range(ReturnDurationMin, ReturnDurationMax);
                                chainTargetDef.glanceDuration = baseDuration;
                                stepTimer = baseDuration;
                                stepReached = false;
                                reachTimer = 0f;
                                stepTotalTime = 0f;
                                stepAtom = returnTarget.randomCachedAtom;
                            }
                            else FinishChain();
                        }
                        else FinishChain();
                    }
                }
            }
        }

        // === Запуск обычной цепи ===
        private void StartChain(Atom atom, int startPart, TargetDef baseTarget)
        {
            currentState = StateChainActive;
            isChainActive = true;
            chainAtom = atom;
            initialHandPart = (startPart == BodyPart.LHand || startPart == BodyPart.RHand) ? startPart : -1;
            currentStep = 0;

            if (baseTarget != null)
            {
                returnTarget = new TargetDef();
                returnTarget.isRandom = baseTarget.isRandom;
                returnTarget.isPlayer = baseTarget.isPlayer;
                returnTarget.isHold = baseTarget.isHold;
                returnTarget.atom = baseTarget.atom;
                returnTarget.controlName = baseTarget.controlName;
                returnTarget.randomCachedTransform = baseTarget.randomCachedTransform;
                returnTarget.randomVirtualPosition = baseTarget.randomVirtualPosition;
                returnTarget.randomCachedAtom = baseTarget.randomCachedAtom;
                returnTarget.selectedBodyPart = baseTarget.selectedBodyPart;
                returnTarget.glanceType = baseTarget.glanceType;
                returnTarget.glanceDuration = baseTarget.glanceDuration;
                returnTarget.durationMin = baseTarget.durationMin;
                returnTarget.durationMax = baseTarget.durationMax;
                returnTarget.speedMin = baseTarget.speedMin;
                returnTarget.speedMax = baseTarget.speedMax;
                returnTarget.anchorYaw = baseTarget.anchorYaw;
                returnTarget.anchorPitch = baseTarget.anchorPitch;
                returnTarget.headTurnEnabled = baseTarget.headTurnEnabled;
                returnTarget.gazeAversionEnabled = baseTarget.gazeAversionEnabled;
            }
            else returnTarget = null;

            if (startPart == BodyPart.Head) SetupStep(BodyPart.Head, HeadDurationMin, HeadDurationMax);
            else SetupStep(startPart, HandDurationMin, HandDurationMax);
        }

        // === C7: Запуск обхода ===
        private void StartMultiScan(Atom atom, int[] parts, int count)
        {
            currentState = StateMultiScanActive;
            isChainActive = true;
            isMultiScan = true;
            isDanceDwell = false;
            isPostScanHold = false;
            chainAtom = atom;
            initialHandPart = -1;
            currentStep = 0;
            scanRound = 0;
            scanStepIndex = 0;
            isFinalHeadStep = false;
            returnTarget = null;

            scanPartsCount = count > MultiScanMaxParts ? MultiScanMaxParts : count;
            for (int i = 0; i < scanPartsCount; i++) scanParts[i] = parts[i];

            SetupStep(scanParts[0], ScanDurationMin, ScanDurationMax);
        }

        // === C7: Повторный раунд обхода ===
        private void StartNextMultiScanRound(Transform headTransform)
        {
            int[] candidates = new int[7];
            int candidateCount = attentionSystem.GetMultiScanCandidates(chainAtom, candidates);
            int[] validParts = new int[MultiScanMaxParts];
            int validCount = 0;
            for (int i = 0; i < candidateCount && validCount < MultiScanMaxParts; i++)
            {
                int p = candidates[i];
                if (attentionSystem.IsSuppressed(chainAtom, p)) continue;
                if (validCount > 0 && IsPairedPart(validParts[validCount - 1], p)) continue;
                Transform t = attentionSystem.GetCachedBodyPart(chainAtom, p);
                if (t != null && IsWithinTriggerFieldOfView(headTransform, t.position))
                {
                    validParts[validCount] = p;
                    validCount++;
                }
            }

            if (validCount >= MultiScanMinParts)
            {
                scanRound++;
                scanPartsCount = validCount;
                for (int i = 0; i < validCount; i++) scanParts[i] = validParts[i];
                scanStepIndex = 0;
                isFinalHeadStep = false;
                SetupStep(scanParts[0], ScanDurationMin, ScanDurationMax);
            }
            else StartPostScanHold();
        }

        // === C7: Запуск Dance Dwell ===
        private void StartDanceDwell(Atom atom, int[] parts, int count)
        {
            currentState = StateDanceDwellActive;
            isChainActive = true;
            isMultiScan = false;
            isDanceDwell = true;
            isPostScanHold = false;
            chainAtom = atom;
            initialHandPart = -1;
            currentStep = 0;
            scanRound = 0;
            scanStepIndex = 0;
            isFinalHeadStep = false;
            returnTarget = null;

            dwellPartsCount = count > DanceDwellMaxParts ? DanceDwellMaxParts : count;
            for (int i = 0; i < dwellPartsCount; i++) dwellParts[i] = parts[i];

            dwellStepIndex = 0;
            SetupDwellStep(dwellParts[0]);
        }

        private void SetupDwellStep(int bodyPart)
        {
            Transform partTransform = attentionSystem.GetCachedBodyPart(chainAtom, bodyPart);
            if (partTransform == null) { Reset(); return; }

            chainTargetDef.randomCachedTransform = partTransform;
            chainTargetDef.randomCachedAtom = chainAtom;
            chainTargetDef.randomVirtualPosition = partTransform.position;
            chainTargetDef.selectedBodyPart = bodyPart;
            chainTargetDef.glanceType = GlanceType.Sustained;

            float minD = (bodyPart == BodyPart.Head) ? HeadDurationMin : DwellBodyDurationMin;
            float maxD = (bodyPart == BodyPart.Head) ? HeadDurationMax : DwellBodyDurationMax;
            chainTargetDef.glanceDuration = UnityEngine.Random.Range(minD, maxD);

            stepTimer = chainTargetDef.glanceDuration;
            stepReached = false;
            reachTimer = 0f;
            stepTotalTime = 0f;
            stepAtom = chainAtom;
        }

        private void FinishDanceDwell()
        {
            Reset();
            chainCooldown = UnityEngine.Random.Range(MultiScanCooldownMin, MultiScanCooldownMax);
        }

        // === C7-fix: задержка на последней точке ===
        private void StartPostScanHold()
        {
            currentState = StatePostScanHold;
            int holdPart = chainTargetDef.selectedBodyPart;
            float holdDuration = UnityEngine.Random.Range(PostScanHoldMin, PostScanHoldMax);
            SetupStep(holdPart, holdDuration, holdDuration);
            stepReached = true;
        }

        // === Настройка шага ===
        private void SetupStep(int bodyPart, float minDur, float maxDur)
        {
            Transform partTransform = attentionSystem.GetCachedBodyPart(chainAtom, bodyPart);
            if (partTransform == null) { Reset(); return; }

            chainTargetDef.randomCachedTransform = partTransform;
            chainTargetDef.randomCachedAtom = chainAtom;
            chainTargetDef.randomVirtualPosition = partTransform.position;
            chainTargetDef.selectedBodyPart = bodyPart;
            chainTargetDef.glanceType = GlanceType.Quick;
            chainTargetDef.glanceDuration = UnityEngine.Random.Range(minDur, maxDur);

            stepTimer = chainTargetDef.glanceDuration;
            stepReached = false;
            reachTimer = 0f;
            stepTotalTime = 0f;
            stepAtom = chainAtom;
        }

        // === C6: Выбор видимой руки ===
        private int PickVisibleHand(Atom atom, Transform headTransform)
        {
            int[] hands = { BodyPart.LHand, BodyPart.RHand };
            if (UnityEngine.Random.value < 0.5f) { int temp = hands[0]; hands[0] = hands[1]; hands[1] = temp; }
            for (int i = 0; i < 2; i++)
            {
                int p = hands[i];
                if (attentionSystem.IsSuppressed(atom, p)) continue;
                Transform t = attentionSystem.GetCachedBodyPart(atom, p);
                if (t != null && IsWithinTriggerFieldOfView(headTransform, t.position)) return p;
            }
            return -1;
        }

        // === Завершение обычной цепи ===
        private void FinishChain()
        {
            bool wasHead = (initialHandPart == -1);
            Reset();
            if (wasHead) headCooldown = HeadCooldownTime;
            else handCooldown = HandCooldownTime;
        }

        // === C7: Завершение обхода ===
        private void FinishMultiScan()
        {
            Reset();
            chainCooldown = UnityEngine.Random.Range(MultiScanCooldownMin, MultiScanCooldownMax);
        }

        // === Вспомогательные методы ===
        private bool IsHeadNearTarget(Transform headTransform, Transform reference)
        {
            if (chainTargetDef.randomCachedTransform == null) return true;
            Vector3 targetPos = chainTargetDef.randomCachedTransform.position;
            Vector3 headPos = headTransform.position;
            Vector3 dir = targetPos - headPos;
            if (dir.sqrMagnitude < 0.0001f) return true;
            Vector3 localDir = reference.InverseTransformDirection(dir);
            float hMag = new Vector2(localDir.x, localDir.z).magnitude;
            if (hMag < SingularThreshold) return true;
            float targetYaw = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
            float targetPitch = Mathf.Atan2(localDir.y, hMag) * Mathf.Rad2Deg;
            Vector3 currentDir = reference.InverseTransformDirection(headTransform.forward);
            float currentYaw = Mathf.Atan2(currentDir.x, currentDir.z) * Mathf.Rad2Deg;
            float currentPitch = Mathf.Atan2(currentDir.y, new Vector2(currentDir.x, currentDir.z).magnitude) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw)) <= ReachThresholdDeg &&
                   Mathf.Abs(Mathf.DeltaAngle(currentPitch, targetPitch)) <= ReachThresholdDeg;
        }

        private bool IsWithinTriggerFieldOfView(Transform headTransform, Vector3 targetPos)
        {
            Vector3 dir = targetPos - headTransform.position;
            if (dir.sqrMagnitude < 0.0001f) return true;
            return Vector3.Angle(headTransform.forward, dir) <= TriggerFieldOfViewDeg;
        }

        public TargetDef GetCurrentOverride()
        {
            return isChainActive ? chainTargetDef : null;
        }

        public void Reset()
        {
            isChainActive = false;
            isMultiScan = false;
            isDanceDwell = false;
            isPostScanHold = false;
            currentState = StateIdle;
            currentStep = -1;
            stepTimer = 0f;
            stepReached = false;
            reachTimer = 0f;
            stepTotalTime = 0f;
            chainAtom = null;
            stepAtom = null;
            initialHandPart = -1;
            returnTarget = null;
            scanPartsCount = 0;
            scanStepIndex = 0;
            scanRound = 0;
            isFinalHeadStep = false;
            dwellPartsCount = 0;
            dwellStepIndex = 0;
        }

        private bool IsPairedPart(int part1, int part2)
        {
            return (part1 == BodyPart.LArm && part2 == BodyPart.RArm) ||
                   (part1 == BodyPart.RArm && part2 == BodyPart.LArm) ||
                   (part1 == BodyPart.LHand && part2 == BodyPart.RHand) ||
                   (part1 == BodyPart.RHand && part2 == BodyPart.LHand);
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
    }
}