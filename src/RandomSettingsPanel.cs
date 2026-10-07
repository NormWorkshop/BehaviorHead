using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BehaviorUtils;

namespace Norm
{
    public class RandomSettingsPanel
    {
        private MVRScript script;
        private RandomGazeConfig config;
        private AttentionConfig attentionConfig;
        private BodyPartsConfig bodyPartsConfig;

        private GameObject panel;
        private List<GameObject> uiObjects = new List<GameObject>();

        public bool HasPanel
        {
            get { return panel != null; }
        }

        public RandomSettingsPanel(
            MVRScript script,
            RandomGazeConfig config,
            AttentionConfig attentionConfig,
            BodyPartsConfig bodyPartsConfig)
        {
            this.script = script;
            this.config = config;
            this.attentionConfig = attentionConfig;
            this.bodyPartsConfig = bodyPartsConfig;
        }

        public GameObject Create(Transform parent, int siblingIndex)
        {
            if (config == null) return null;

            panel = new GameObject("RandomSettingsPanel");
            panel.transform.SetParent(parent, false);
            panel.transform.SetSiblingIndex(siblingIndex);

            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 4f;

            var trackPlayerToggle = script.CreateToggle(config.trackPlayer, false);
            trackPlayerToggle.transform.SetParent(panel.transform, false);
            trackPlayerToggle.label = "Track Player";
            uiObjects.Add(trackPlayerToggle.gameObject);

            var personsSlider = script.CreateSlider(config.personsWeight, false);
            personsSlider.transform.SetParent(panel.transform, false);
            personsSlider.label = "Persons Weight";
            personsSlider.valueFormat = "F2";
            uiObjects.Add(personsSlider.gameObject);

            var eyesOnlySlider = script.CreateSlider(config.eyesOnlyChance, false);
            eyesOnlySlider.transform.SetParent(panel.transform, false);
            eyesOnlySlider.label = "Eyes Only Chance";
            eyesOnlySlider.valueFormat = "F2";
            uiObjects.Add(eyesOnlySlider.gameObject);

            var virtualGroup = BHUI.SetupExpandableGroup(script, "Virtual Targets", false, panel.transform);
            if (virtualGroup != null)
            {
                uiObjects.Add(virtualGroup.gameObject);

                AddGroupSlider(virtualGroup, config.virtualTargetWeight, "Virtual Weight", "F2");
                AddGroupSlider(virtualGroup, config.nothingWeight, "Nothing Weight", "F2");
                AddGroupSlider(virtualGroup, config.selfGazeWeight, "Self Gaze Weight", "F2");
                AddGroupSlider(virtualGroup, config.virtualDistance, "Virtual Distance (m)", "F1");
                AddGroupSlider(virtualGroup, config.upBias, "Up Bias", "F2");
                AddGroupSlider(virtualGroup, config.downBias, "Down Bias", "F2");
            }

            if (attentionConfig != null)
            {
                var attentionGroup = BHUI.SetupExpandableGroup(script, "Attention", false, panel.transform);
                if (attentionGroup != null)
                {
                    uiObjects.Add(attentionGroup.gameObject);

                    AddGroupSlider(attentionGroup, attentionConfig.fatigueDelay, "Fatigue Delay (s)", "F1");
                    AddGroupSlider(attentionGroup, attentionConfig.minFatigueModifier, "Min Fatigue Modifier", "F2");
                    AddGroupSlider(attentionGroup, attentionConfig.attentionBoost, "Attention Boost", "F2");
                    AddGroupSlider(attentionGroup, attentionConfig.monotonyWindow, "Monotony Window (s)", "F1");
                    AddGroupSlider(attentionGroup, attentionConfig.monotonyThreshold, "Monotony Threshold", "F1");
                    AddGroupSlider(attentionGroup, attentionConfig.attentionDecayRate, "Decay Rate", "F2");
                }
            }

            if (bodyPartsConfig != null)
            {
                var bodyGroup = BHUI.SetupExpandableGroup(script, "Body Parts", false, panel.transform);
                if (bodyGroup != null)
                {
                    uiObjects.Add(bodyGroup.gameObject);

                    AddGroupSlider(bodyGroup, bodyPartsConfig.distanceNear, "Distance Near", "F1");
                    AddGroupSlider(bodyGroup, bodyPartsConfig.distanceFar, "Distance Far", "F1");
                    AddGroupSlider(bodyGroup, bodyPartsConfig.movementDecayTime, "Movement Decay", "F1");

                    AddGroupSlider(bodyGroup, config.headWeight, "Head Weight", "F2");
                    AddGroupSlider(bodyGroup, config.chestWeight, "Chest Weight", "F2");
                    AddGroupSlider(bodyGroup, config.hipWeight, "Hip Weight", "F2");
                    AddGroupSlider(bodyGroup, config.lHandWeight, "LHand Weight", "F2");
                    AddGroupSlider(bodyGroup, config.rHandWeight, "RHand Weight", "F2");
                    AddGroupSlider(bodyGroup, config.lArmWeight, "LArm Weight", "F2");
                    AddGroupSlider(bodyGroup, config.rArmWeight, "RArm Weight", "F2");
                }
            }

            panel.SetActive(false);
            return panel;
        }

        private void AddGroupSlider(BHUIExpandableGroup group, JSONStorableFloat storable, string label, string format)
        {
            if (group == null || storable == null) return;

            var slider = script.CreateSlider(storable, false);
            slider.transform.SetParent(group.contentContainer.transform, false);
            slider.label = label;
            slider.valueFormat = format;

            group.AddChildStorable(storable);
            uiObjects.Add(slider.gameObject);
        }

        public void SetVisible(bool visible)
        {
            if (panel != null) panel.SetActive(visible);
        }

        public void Destroy()
        {
            if (panel == null) return;

            uiObjects.Clear();
            UnityEngine.Object.Destroy(panel);
            panel = null;
        }
    }
}