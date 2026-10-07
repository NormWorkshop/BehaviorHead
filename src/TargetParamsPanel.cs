using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BehaviorUtils;
namespace Norm
{
public class TargetParamsPanel
{
private MVRScript script;
private BehaviorHeadUI headUI;
private HeadController headCtrl;
private float globalDefaultSpeed;
private Action onRowLabelsChanged;
    private GameObject panel;
     private TargetDef target;
     private List<JSONStorableParam> storables = new List<JSONStorableParam>();
     public bool HasPanel { get { return panel != null; } }
     public TargetParamsPanel(MVRScript script, BehaviorHeadUI headUI, HeadController headCtrl, float globalDefaultSpeed, Action onRowLabelsChanged)
     {
         this.script = script;
         this.headUI = headUI;
         this.headCtrl = headCtrl;
         this.globalDefaultSpeed = globalDefaultSpeed;
         this.onRowLabelsChanged = onRowLabelsChanged;
     }
     public GameObject Create(Transform parent, int siblingIndex, TargetDef t)
     {
         target = t;
         storables.Clear();
         panel = new GameObject("TargetParams");
         panel.transform.SetParent(parent, false);
         panel.transform.SetSiblingIndex(siblingIndex);
         var vlg = panel.AddComponent<VerticalLayoutGroup>();
         vlg.childControlHeight = false; vlg.childControlWidth = true;
         vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
         vlg.spacing = 4f;
         var wp = BHUI.SetupSliderFloat(script, "Weight", t.weight, 0f, 10f, true, panel.transform);
         storables.Add(wp.storableFloat);
         wp.storableFloat.setCallbackFunction = v => { t.weight = v; NotifyRowLabelsChanged(); };
         var cp = BHUI.SetupSliderInt(script, "Cooldown", t.cooldown, 0, 5, true, panel.transform);
         storables.Add(cp.storableFloat);
         cp.storableFloat.setCallbackFunction = v => t.cooldown = Mathf.RoundToInt(v);
         var group = BHUI.SetupExpandableGroup(script, "Duration & Speed", true, panel.transform);
         if (group != null)
         {
             group.SetOnToggleCallback(expanded => { t.isGroupExpanded = expanded; });
             BHSliderPair dmax = null;
             var dmin = BHUI.SetupSliderFloat(script, "Dur Min (s)", t.durationMin, 0f, 30f, true, group.contentContainer.transform);
             storables.Add(dmin.storableFloat);
             group.AddChildStorable(dmin.storableFloat);
             dmin.storableFloat.setCallbackFunction = v =>
             {
                 t.durationMin = v;
                 if (v > t.durationMax)
                 {
                     t.durationMax = v;
                     if (dmax != null) dmax.storableFloat.val = v;
                 }
                 group.UpdateModifiedIndicator();
             };
             dmax = BHUI.SetupSliderFloat(script, "Dur Max (s)", t.durationMax, 0f, 30f, true, group.contentContainer.transform);
             storables.Add(dmax.storableFloat);
             group.AddChildStorable(dmax.storableFloat);
             dmax.storableFloat.setCallbackFunction = v =>
             {
                 t.durationMax = v;
                 if (v < t.durationMin)
                 {
                     t.durationMin = v;
                     dmin.storableFloat.val = v;
                 }
                 group.UpdateModifiedIndicator();
             };
             BHSliderPair smax = null;
             var smin = BHUI.SetupSliderFloat(script, "Speed Min", t.speedMin, 0.1f, 20f, true, group.contentContainer.transform);
             storables.Add(smin.storableFloat);
             group.AddChildStorable(smin.storableFloat);
             smin.storableFloat.setCallbackFunction = v =>
             {
                 t.speedMin = v;
                 if (v > t.speedMax)
                 {
                     t.speedMax = v;
                     if (smax != null) smax.storableFloat.val = v;
                 }
                 headCtrl.RefreshCurrentSpeed(t);
                 group.UpdateModifiedIndicator();
             };
             smax = BHUI.SetupSliderFloat(script, "Speed Max", t.speedMax, 0.1f, 20f, true, group.contentContainer.transform);
             storables.Add(smax.storableFloat);
             group.AddChildStorable(smax.storableFloat);
             smax.storableFloat.setCallbackFunction = v =>
             {
                 t.speedMax = v;
                 if (v < t.speedMin)
                 {
                     t.speedMin = v;
                     smin.storableFloat.val = v;
                 }
                 headCtrl.RefreshCurrentSpeed(t);
                 group.UpdateModifiedIndicator();
             };
             group.SetModifiedChecker(() =>
             {
                 return !Mathf.Approximately(t.durationMin, 1f) ||
                        !Mathf.Approximately(t.durationMax, 5f) ||
                        !Mathf.Approximately(t.speedMin, globalDefaultSpeed) ||
                        !Mathf.Approximately(t.speedMax, globalDefaultSpeed);
             });
             if (t.isGroupExpanded)
             {
                 script.StartCoroutine(WaitAndExpandGroup(group));
             }
         }
         JSONStorableBool watchPlayerStorable = null;
         JSONStorableBool watchPersonsStorable = null;
         JSONStorableBool watchGazeStorable = null;
         BHSliderPair yawSlider = null;
         BHSliderPair pitchSlider = null;
         BHUIExpandableGroup watchGroup = null;
         BHUIExpandableGroup anchorGroup = null;
         JSONStorableBool gazeAversionStorable = null;
         JSONStorableBool headTurnStorable = null;
         BHSliderPair sc = null;
         if (t.isHold)
         {
             watchGroup = BHUI.SetupExpandableGroup(script, "Watch", true, panel.transform);
             if (watchGroup != null)
             {
                 watchPlayerStorable = new JSONStorableBool("Watch Player", t.watchPlayer, v => { t.watchPlayer = v; watchGroup.UpdateModifiedIndicator(); });
                 watchPlayerStorable.storeType = JSONStorableParam.StoreType.Full;
                 script.RegisterBool(watchPlayerStorable);
                 var watchPlayerToggle = script.CreateToggle(watchPlayerStorable, true);
                 watchPlayerToggle.transform.SetParent(watchGroup.contentContainer.transform, false);
                 storables.Add(watchPlayerStorable);
                 watchGroup.AddChildStorable(watchPlayerStorable);
                 watchPersonsStorable = new JSONStorableBool("Watch Persons", t.watchPersons, v => { t.watchPersons = v; watchGroup.UpdateModifiedIndicator(); });
                 watchPersonsStorable.storeType = JSONStorableParam.StoreType.Full;
                 script.RegisterBool(watchPersonsStorable);
                 var watchPersonsToggle = script.CreateToggle(watchPersonsStorable, true);
                 watchPersonsToggle.transform.SetParent(watchGroup.contentContainer.transform, false);
                 storables.Add(watchPersonsStorable);
                 watchGroup.AddChildStorable(watchPersonsStorable);
                 watchGazeStorable = new JSONStorableBool("Glance Around", t.gazeAversionEnabled, v => { t.gazeAversionEnabled = v; watchGroup.UpdateModifiedIndicator(); });
                 watchGazeStorable.storeType = JSONStorableParam.StoreType.Full;
                 script.RegisterBool(watchGazeStorable);
                 var watchGazeToggle = script.CreateToggle(watchGazeStorable, true);
                 watchGazeToggle.transform.SetParent(watchGroup.contentContainer.transform, false);
                 storables.Add(watchGazeStorable);
                 watchGroup.AddChildStorable(watchGazeStorable);
                 watchGroup.SetModifiedChecker(() =>
                 {
                     return !t.watchPlayer || !t.watchPersons || !t.gazeAversionEnabled;
                 });
             }
             anchorGroup = BHUI.SetupExpandableGroup(script, "Anchor", true, panel.transform);
             if (anchorGroup != null)
             {
                 yawSlider = BHUI.SetupSliderFloat(script, "Anchor Yaw", t.anchorYaw, -120f, 120f, true, anchorGroup.contentContainer.transform);
                 storables.Add(yawSlider.storableFloat);
                 anchorGroup.AddChildStorable(yawSlider.storableFloat);
                 yawSlider.storableFloat.setCallbackFunction = v => { t.anchorYaw = v; anchorGroup.UpdateModifiedIndicator(); };
                 pitchSlider = BHUI.SetupSliderFloat(script, "Anchor Pitch", t.anchorPitch, -55f, 55f, true, anchorGroup.contentContainer.transform);
                 storables.Add(pitchSlider.storableFloat);
                 anchorGroup.AddChildStorable(pitchSlider.storableFloat);
                 pitchSlider.storableFloat.setCallbackFunction = v => { t.anchorPitch = v; anchorGroup.UpdateModifiedIndicator(); };
                 var homeBtn = BHUI.SetupButton(script, "Home", () =>
                 {
                     t.anchorYaw = 0f;
                     t.anchorPitch = 0f;
                     yawSlider.storableFloat.valNoCallback = 0f;
                     pitchSlider.storableFloat.valNoCallback = 0f;
                     anchorGroup.UpdateModifiedIndicator();
                     if (headUI.activeTarget == t)
                         headCtrl.SetManualTarget(t);
                 }, true, anchorGroup.contentContainer.transform);
                 anchorGroup.SetModifiedChecker(() =>
                 {
                     return Mathf.Abs(t.anchorYaw) > 0.01f || Mathf.Abs(t.anchorPitch) > 0.01f;
                 });
                 anchorGroup.UpdateModifiedIndicator();
             }
         }
         else if (t.isRandom)
         {
             // [Random]: только базовые настройки, показанные выше
         }
         else
         {
             sc = BHUI.SetupSliderFloat(script, "Scatter (deg)", t.scatter, 0f, 30f, true, panel.transform);
             storables.Add(sc.storableFloat);
             sc.storableFloat.setCallbackFunction = v => { t.scatter = v; };
             gazeAversionStorable = new JSONStorableBool("Gaze Aversion", t.gazeAversionEnabled, v => { t.gazeAversionEnabled = v; });
             gazeAversionStorable.storeType = JSONStorableParam.StoreType.Full;
             script.RegisterBool(gazeAversionStorable);
             var gazeToggle = script.CreateToggle(gazeAversionStorable, true);
             gazeToggle.transform.SetParent(panel.transform, false);
             storables.Add(gazeAversionStorable);
             headTurnStorable = new JSONStorableBool("Head Turn", t.headTurnEnabled, v => { t.headTurnEnabled = v; });
             headTurnStorable.storeType = JSONStorableParam.StoreType.Full;
             script.RegisterBool(headTurnStorable);
             var headTurnToggle = script.CreateToggle(headTurnStorable, true);
             headTurnToggle.transform.SetParent(panel.transform, false);
             storables.Add(headTurnStorable);
         }
         var resetBtn = BHUI.SetupButton(script, "Reset to Global", () =>
         {
             t.weight = 1f;
             t.durationMin = 1f;
             t.durationMax = 5f;
             t.cooldown = 2;
             t.speedMin = globalDefaultSpeed;
             t.speedMax = globalDefaultSpeed;
             t.scatter = 0f;
             t.gazeAversionEnabled = t.defaultGazeAversion;
             t.headTurnEnabled = !t.isRandom;
             t.anchorYaw = 0f;
             t.anchorPitch = 0f;
             t.watchPlayer = true;
             t.watchPersons = true;
             wp.storableFloat.val = t.weight;
             cp.storableFloat.val = t.cooldown;
             if (sc != null) sc.storableFloat.val = t.scatter;
             if (group != null)
             {
                 foreach (var s in storables)
                 {
                     var f = s as JSONStorableFloat;
                     if (f != null && (f.name.Contains("Dur") || f.name.Contains("Speed")))
                     {
                         if (f.name.Contains("Dur Min")) f.val = t.durationMin;
                         else if (f.name.Contains("Dur Max")) f.val = t.durationMax;
                         else if (f.name.Contains("Speed Min")) f.val = t.speedMin;
                         else if (f.name.Contains("Speed Max")) f.val = t.speedMax;
                     }
                 }
                 group.UpdateModifiedIndicator();
             }
             if (watchPlayerStorable != null) watchPlayerStorable.val = t.watchPlayer;
             if (watchPersonsStorable != null) watchPersonsStorable.val = t.watchPersons;
             if (watchGazeStorable != null) watchGazeStorable.val = t.gazeAversionEnabled;
             if (yawSlider != null) yawSlider.storableFloat.val = t.anchorYaw;
             if (pitchSlider != null) pitchSlider.storableFloat.val = t.anchorPitch;
             if (watchGroup != null) watchGroup.UpdateModifiedIndicator();
             if (anchorGroup != null) anchorGroup.UpdateModifiedIndicator();
             if (gazeAversionStorable != null) gazeAversionStorable.val = t.gazeAversionEnabled;
             if (headTurnStorable != null) headTurnStorable.val = t.headTurnEnabled;
             if (headUI.activeTarget == t)
                 headCtrl.SetManualTarget(t);
             NotifyRowLabelsChanged();
         }, true, panel.transform);
         return panel;
     }
     public void Destroy()
     {
         if (panel == null) return;
         if (target != null) target.isGroupExpanded = false;
         foreach (var p in storables)
         {
             var f = p as JSONStorableFloat;
             if (f != null) script.DeregisterFloat(f);
             var b = p as JSONStorableBool;
             if (b != null) script.DeregisterBool(b);
         }
         storables.Clear();
         UnityEngine.Object.Destroy(panel);
         panel = null;
         target = null;
     }
     private void NotifyRowLabelsChanged()
     {
         if (onRowLabelsChanged != null) onRowLabelsChanged();
     }
     private IEnumerator WaitAndExpandGroup(BHUIExpandableGroup group)
     {
         yield return null;
         if (group != null)
             group.SetExpanded(true);
     }
 }
}