using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using BehaviorUtils;
namespace Norm
{
public class TargetsController
{
private MVRScript script;
private Atom atom;
private BehaviorHeadUI headUI;
private HeadController headCtrl;
private List<GameObject> uiObjects = new List<GameObject>();
    private JSONStorableBool gazeEnabledStorable;
     private JSONStorableBool externalControlStorable;
     private UIDynamicButton headerGroupsBtn;
     private UIDynamicButton headerTargetsBtn;
     private JSONStorableFloat groupWeight;
     private JSONStorableFloat groupSwitchMin;
     private JSONStorableFloat groupSwitchMax;
     private UIDynamicTwinButton groupTwinBtn;
     private JSONStorableStringChooser atomChooser;
     private JSONStorableStringChooser controlChooser;
     private UIDynamicPopup atomPopup, controlPopup;
     private UIDynamicButton refreshBtn;
     private UIDynamicButton addTargetBtn;
     private JSONStorableStringChooser groupChooser;
     private UIDynamicPopup groupPopup;
     private List<UIDynamicLabelXButton> targetEntries = new List<UIDynamicLabelXButton>();
     private Dictionary<UIDynamicLabelXButton, Color> rowOriginalColors = new Dictionary<UIDynamicLabelXButton, Color>();
     private Dictionary<UIDynamicLabelXButton, Color> rowOriginalTextColors = new Dictionary<UIDynamicLabelXButton, Color>();
     private Dictionary<TargetDef, UIDynamicLabelXButton> targetToRow = new Dictionary<TargetDef, UIDynamicLabelXButton>();
     private HashSet<TargetDef> loggedInvalidTargets = new HashSet<TargetDef>();
     private TargetParamsPanel paramsPanel;
     private TargetDef selectedTarget;
     private UIDynamicLabelXButton highlightedEntry;
     private List<Atom> allAtoms = new List<Atom>();
     private List<string> currentControls = new List<string>();
     private float globalDefaultSpeed = 5f;
     private Coroutine atomCheckCoroutine;
     private const float AtomCheckInterval = 2f;
     private List<string> lastAtomNames = new List<string>();
	 private bool isRandomMode = false;
	private RandomSettingsPanel randomSettingsPanel;
	private UIDynamicButton toggleViewBtn;
     private static readonly Color HighlightColor = new Color(176f / 255f, 203f / 255f, 165f / 255f, 1f);
     private static readonly Color InvalidRowBgColor = new Color(0.95f, 0.6f, 0.6f, 1f);
     private static readonly Color InvalidRowTextColor = new Color(0.25f, 0.1f, 0.1f, 1f);
     private const int MaxNameLength = 22;
     private static readonly HashSet<string> SelfTargetExclusions = new HashSet<string>
     {
         "head", "headControl", "lEye", "rEye", "neckControl", "rShoulderControl", "lShoulderControl"
     };
     public TargetsController(MVRScript owner, Atom owningAtom, BehaviorHeadUI headUI, HeadController headCtrl)
     {
         script = owner;
         atom = owningAtom;
         this.headUI = headUI;
         this.headCtrl = headCtrl;
     }
    public void ShowUI()
    {
        globalDefaultSpeed = headCtrl.HeadSpeedVal;
        paramsPanel = new TargetParamsPanel(script, headUI, headCtrl, globalDefaultSpeed, RefreshAllRowLabels);
        toggleViewBtn = BHUI.SetupButton(script, " ►      Random Settings", ToggleViewMode, false);
        uiObjects.Add(toggleViewBtn.gameObject);
        gazeEnabledStorable = new JSONStorableBool("Gaze Enabled", true, v => headUI.gazeEnabled = v);
         script.RegisterBool(gazeEnabledStorable);
         var toggleGaze = script.CreateToggle(gazeEnabledStorable, false);
         uiObjects.Add(toggleGaze.gameObject);
         externalControlStorable = new JSONStorableBool("Pause", false, v => headUI.externalControl = v);
         script.RegisterBool(externalControlStorable);
         var toggleExternal = script.CreateToggle(externalControlStorable, false);
         uiObjects.Add(toggleExternal.gameObject);
         uiObjects.Add(BHUI.SetupSpacer(script, 22.5f, false).gameObject);
        headerTargetsBtn = CreateHeaderButton("Targets", false);
        uiObjects.Add(headerTargetsBtn.gameObject);
        var randomConfig = headCtrl.RandomConfig;
        if (randomConfig != null && toggleViewBtn != null)
        {
            randomSettingsPanel = new RandomSettingsPanel(script, randomConfig, headCtrl.AttentionConfig, headCtrl.BodyPartsConfig);
            var panelParent = toggleViewBtn.transform.parent;
            int panelSibling = toggleViewBtn.transform.GetSiblingIndex() + 1;
            randomSettingsPanel.Create(panelParent, panelSibling);
        }
        RefreshAtomList();
         atomChooser = new JSONStorableStringChooser("Target", new List<string> { "[Player]", "[Hold]", "[Random]" }, "[Player]", "Target",
             new JSONStorableStringChooser.SetStringCallback(OnAtomChanged));
         refreshBtn = BHUI.SetupButton(script, "Refresh", () => { RefreshAtomList(); UpdateAtomChoices(); ValidateAllTargets(); }, false);
         uiObjects.Add(refreshBtn.gameObject);
         atomPopup = script.CreateFilterablePopup(atomChooser, false);
         atomPopup.popupPanelHeight = 300f;
         uiObjects.Add(atomPopup.gameObject);
         controlChooser = new JSONStorableStringChooser("Target Control", new List<string> { "  " }, "  ", "Target Control");
         controlPopup = script.CreateFilterablePopup(controlChooser, false);
         controlPopup.popupPanelHeight = 300f;
         uiObjects.Add(controlPopup.gameObject);
         addTargetBtn = BHUI.SetupButton(script, "Add to Group", AddTargetToGroup, false);
         uiObjects.Add(addTargetBtn.gameObject);
         uiObjects.Add(BHUI.SetupSpacer(script, 22.5f, false).gameObject);
         headerGroupsBtn = CreateHeaderButton("Groups", false);
         uiObjects.Add(headerGroupsBtn.gameObject);
         groupTwinBtn = BHUI.SetupTwinButton(script, "Add", AddGroup, "Remove", RemoveGroup, false);
         uiObjects.Add(groupTwinBtn.gameObject);
         groupWeight = new JSONStorableFloat("Group Weight", 1f, 0f, 10f);
         groupWeight.isStorable = false;
         groupWeight.isRestorable = false;
         var wSlider = script.CreateSlider(groupWeight, false);
         uiObjects.Add(wSlider.gameObject);
         var minPair = BHUI.SetupSliderFloat(script, "Group Switch Min (s)", 5f, 0f, 60f, false);
         var maxPair = BHUI.SetupSliderFloat(script, "Group Switch Max (s)", 15f, 0f, 60f, false);
         groupSwitchMin = minPair.storableFloat;
         groupSwitchMax = maxPair.storableFloat;
         groupSwitchMin.isStorable = false;
         groupSwitchMin.isRestorable = false;
         groupSwitchMax.isStorable = false;
         groupSwitchMax.isRestorable = false;
         try { script.DeregisterFloat(groupSwitchMin); } catch (Exception) { }
         try { script.DeregisterFloat(groupSwitchMax); } catch (Exception) { }
         groupSwitchMin.setCallbackFunction = v =>
         {
             var grp = GetCurrentGroup(); if (grp != null) grp.switchMin = v;
             if (v > groupSwitchMax.val) groupSwitchMax.val = v;
         };
         groupSwitchMax.setCallbackFunction = v =>
         {
             var grp = GetCurrentGroup(); if (grp != null) grp.switchMax = v;
             if (v < groupSwitchMin.val) groupSwitchMin.val = v;
         };
         uiObjects.Add(minPair.uiSlider.gameObject);
         uiObjects.Add(maxPair.uiSlider.gameObject);
         groupChooser = BHUI.SetupScrollablePopup(script, GetSortedGroupNames(), true, out groupPopup);
         groupChooser.setCallbackFunction = OnGroupChanged;
         groupChooser.isStorable = false;
         groupChooser.isRestorable = false;
         try { script.DeregisterStringChooser(groupChooser); } catch (Exception) { }
         groupWeight.setCallbackFunction = v =>
         {
             var grp = GetCurrentGroup();
             if (grp != null)
             {
                 grp.weight = v;
                 RefreshGroupLabels();
             }
         };
         UpdateAtomChoices();
         if (headUI.targetGroups.Count > 0)
         {
             RebuildTargetList();
             OnGroupChanged(groupChooser.val);
             UpdateGroupHeader();
         }
         headCtrl.ResetAutoTarget();
         SubscribeAtomEvents();
         headCtrl.OnGroupSwitched += OnGroupAutoSwitched;
         atomCheckCoroutine = script.StartCoroutine(PeriodicAtomCheck());
     }
     private void SubscribeAtomEvents()
     {
         if (SuperController.singleton != null)
             SuperController.singleton.onAtomUIDsChangedHandlers += OnAtomUIDsChanged;
     }
     private void UnsubscribeAtomEvents()
     {
         if (SuperController.singleton != null)
             SuperController.singleton.onAtomUIDsChangedHandlers -= OnAtomUIDsChanged;
     }
     private void OnAtomUIDsChanged(List<string> uids)
     {
         RefreshAtomList();
         UpdateAtomChoices();
         ValidateAllTargets();
     }
     private IEnumerator PeriodicAtomCheck()
     {
         while (true)
         {
             yield return new WaitForSeconds(AtomCheckInterval);
             RefreshAtomList();
             UpdateAtomChoices();
 			ValidateAllTargets();
         }
     }
     private bool IsTargetAtomValid(TargetDef def)
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
     private void ValidateAllTargets()
     {
         foreach (var grp in headUI.targetGroups)
         {
             foreach (var t in grp.targets)
             {
                 bool valid = IsTargetAtomValid(t);
                 if (!valid && !loggedInvalidTargets.Contains(t))
                 {
                     loggedInvalidTargets.Add(t);
                     string atomName = t.atom != null ? t.atom.name : "?";
                     string ctrlName = string.IsNullOrEmpty(t.controlName) ? "?" : t.controlName;
                     SuperController.LogError("BehaviorHead: Target removed from scene: " + atomName + "." + ctrlName + " (group: " + grp.name + ")");
                 }
                 else if (valid && loggedInvalidTargets.Contains(t))
                 {
                     loggedInvalidTargets.Remove(t);
                 }
                 UIDynamicLabelXButton entry;
                 if (targetToRow.TryGetValue(t, out entry))
                 {
                     ApplyRowValidityStyle(entry, valid);
                 }
             }
         }
         RefreshAllRowLabels();
     }
     private void ApplyRowValidityStyle(UIDynamicLabelXButton entry, bool valid)
     {
         if (entry == null) return;
         var bg = entry.transform.Find("Background");
         Image bgImg = bg != null ? bg.GetComponent<Image>() : null;
         if (valid)
         {
             Color origBg;
             if (bgImg != null && rowOriginalColors.TryGetValue(entry, out origBg))
                 bgImg.color = origBg;
             Color origText;
             if (entry.label != null && rowOriginalTextColors.TryGetValue(entry, out origText))
                 entry.label.color = origText;
         }
         else
         {
             if (bgImg != null) bgImg.color = InvalidRowBgColor;
             if (entry.label != null) entry.label.color = InvalidRowTextColor;
         }
     }
     private UIDynamicButton CreateHeaderButton(string text, bool rightSide)
     {
         var btn = script.CreateButton(text, rightSide);
         btn.button.interactable = false;
         btn.buttonColor = new Color(0, 0, 0, 0);
         var label = btn.button.GetComponentInChildren<Text>();
         if (label != null)
         {
             label.color = Color.white;
             label.fontStyle = FontStyle.Normal;
             label.fontSize = 28;
             label.alignment = TextAnchor.MiddleLeft;
         }
         return btn;
     }
     private void UpdateGroupHeader()
     {
         if (headerGroupsBtn == null) return;
         var grp = GetCurrentGroup();
         if (grp != null)
             headerGroupsBtn.label = "Groups (" + FormatGroupLabel(grp) + ")";
         else
             headerGroupsBtn.label = "Groups";
     }
     private void RebuildTargetList()
     {
         foreach (var entry in targetEntries)
         {
             script.RemoveSpacer(entry);
             UnityEngine.Object.Destroy(entry.gameObject);
         }
         targetEntries.Clear();
         rowOriginalColors.Clear();
         rowOriginalTextColors.Clear();
         targetToRow.Clear();
         paramsPanel.Destroy();
         HighlightRow(null);
         selectedTarget = null;
         headUI.activeTarget = null;
         headUI.isExternalOverride = false;
         var grp = GetCurrentGroup();
         if (grp == null || grp.targets.Count == 0)
         {
             headCtrl.ForceAutoReselect();
             return;
         }
        foreach (var t in grp.targets)
            CreateTargetRow(t);
        headCtrl.ForceAutoReselect();
        UpdateViewModeVisibility();
    }
 	private void RebuildTargetListQuiet()
 	{
 		foreach (var entry in targetEntries)
 		{
 			script.RemoveSpacer(entry);
 			UnityEngine.Object.Destroy(entry.gameObject);
 		}
 		targetEntries.Clear();
 		rowOriginalColors.Clear();
 		rowOriginalTextColors.Clear();
 		targetToRow.Clear();
 		paramsPanel.Destroy();
 		HighlightRow(null);
 		selectedTarget = null;
 		var grp = GetCurrentGroup();
 		if (grp == null || grp.targets.Count == 0) return;
        foreach (var t in grp.targets)
            CreateTargetRow(t);
        UpdateViewModeVisibility();
    }
     private void CreateTargetRow(TargetDef t)
     {
         UIDynamicLabelXButton entry = null;
         entry = BHUI.SetupClickableLabelXButton(
             script, "  ",
             () => RemoveTarget(t),
             (data) => OnRowClicked(t, entry),
             true);
         entry.transform.SetAsLastSibling();
         var bg = entry.transform.Find("Background");
         if (bg != null)
         {
             var img = bg.GetComponent<Image>();
             if (img != null)
                 rowOriginalColors[entry] = img.color;
         }
         if (entry.label != null && !rowOriginalTextColors.ContainsKey(entry))
             rowOriginalTextColors[entry] = entry.label.color;
         targetEntries.Add(entry);
         targetToRow[t] = entry;
         UpdateRowLabel(t, entry);
         ApplyRowValidityStyle(entry, IsTargetAtomValid(t));
     }
     private string FormatTargetLabel(TargetDef t)
     {
         if (t.isHold) return "Hold";
         if (t.isPlayer) return "Player";
         if (t.isRandom) return "Random";
         if (t.atom == null) return "?";
         if (string.IsNullOrEmpty(t.controlName)) return t.atom.name;
         if (t.atom.type != "Person") return t.atom.name;
         string controlDisplay = t.controlName;
         if (controlDisplay == "headControl") controlDisplay = "HeadCtrl";
         return t.atom.name + "." + controlDisplay;
     }
     private int GetTargetProbabilityPercent(TargetDef t)
     {
         var grp = GetCurrentGroup();
         if (grp == null || grp.targets.Count == 0) return 0;
         float totalWeight = 0f;
         foreach (var target in grp.targets)
             totalWeight += target.weight;
         if (totalWeight < 0.001f) return 0;
         return Mathf.RoundToInt(t.weight / totalWeight * 100f);
     }
     private void UpdateRowLabel(TargetDef t, UIDynamicLabelXButton entry)
     {
         if (entry == null || entry.label == null) return;
         string baseLabel = FormatTargetLabel(t);
         if (!IsTargetAtomValid(t))
         {
             entry.label.text = string.Format("{0} [{1}] · Removed", baseLabel, t.targetId);
             return;
         }
         int percent = GetTargetProbabilityPercent(t);
         entry.label.text = string.Format("{0} [{1}] · {2}%", baseLabel, t.targetId, percent);
     }
     private void RefreshAllRowLabels()
     {
         var grp = GetCurrentGroup();
         if (grp == null) return;
         foreach (var t in grp.targets)
         {
             UIDynamicLabelXButton entry;
             if (targetToRow.TryGetValue(t, out entry))
                 UpdateRowLabel(t, entry);
         }
     }
     private void OnRowClicked(TargetDef t, UIDynamicLabelXButton entry)
     {
         if (selectedTarget == t)
         {
             paramsPanel.Destroy();
             HighlightRow(null);
             headUI.activeTarget = null;
             headUI.isExternalOverride = false;
             selectedTarget = null;
             headCtrl.ForceAutoReselect();
             return;
         }
         paramsPanel.Destroy();
         Transform parent = entry.transform.parent;
         int siblingIndex = entry.transform.GetSiblingIndex() + 1;
         paramsPanel.Create(parent, siblingIndex, t);
         selectedTarget = t;
         HighlightRow(entry);
         headUI.activeTarget = t;
         headUI.isExternalOverride = false;
         headCtrl.SetManualTarget(t);
     }
     private void HighlightRow(UIDynamicLabelXButton entry)
     {
         if (highlightedEntry != null)
         {
             RestoreRowColor(highlightedEntry);
         }
         highlightedEntry = entry;
         if (entry != null)
         {
             var bg = entry.transform.Find("Background");
             if (bg != null)
             {
                 var img = bg.GetComponent<Image>();
                 if (img != null) img.color = HighlightColor;
             }
         }
     }
     private void RestoreRowColor(UIDynamicLabelXButton entry)
     {
         if (entry == null) return;
         var bg = entry.transform.Find("Background");
         if (bg != null)
         {
             var img = bg.GetComponent<Image>();
             if (img != null && rowOriginalColors.ContainsKey(entry))
                 img.color = rowOriginalColors[entry];
         }
     }
     private void RemoveTarget(TargetDef t)
     {
         var grp = GetCurrentGroup();
         if (grp == null) return;
         if (grp.targets.Contains(t))
         {
             grp.targets.Remove(t);
         }
         RemoveTargetRow(t);
         if (selectedTarget == t)
         {
             paramsPanel.Destroy();
             HighlightRow(null);
             selectedTarget = null;
         }
         if (headUI.activeTarget == t)
         {
             headUI.activeTarget = null;
             headUI.isExternalOverride = false;
         }
         loggedInvalidTargets.Remove(t);
         headCtrl.ForceAutoReselect();
         RefreshAllRowLabels();
         RefreshGroupLabels();
         RefreshAtomList();
         UpdateAtomChoices();
         headUI.RefreshExternalTargetChoices();
     }
     private void RemoveTargetRow(TargetDef t)
     {
         UIDynamicLabelXButton entry;
         if (targetToRow.TryGetValue(t, out entry))
         {
             script.RemoveSpacer(entry);
             UnityEngine.Object.Destroy(entry.gameObject);
             targetEntries.Remove(entry);
             rowOriginalColors.Remove(entry);
             rowOriginalTextColors.Remove(entry);
             targetToRow.Remove(t);
         }
     }
     private string GetNextAvailableGroupName()
     {
         int idx = 1;
         while (headUI.targetGroups.Any(g => g.name == "Group " + idx)) idx++;
         return "Group " + idx;
     }
     private int ExtractGroupNumber(string name)
     {
         if (name == null) return 0;
         var parts = name.Split(' ');
         if (parts.Length == 2)
         {
             int num;
             if (int.TryParse(parts[1], out num)) return num;
         }
         return 0;
     }
     private string FormatGroupLabel(TargetGroup g)
     {
         if (g == null) return "";
         int percent = GetGroupProbabilityPercent(g);
         if (percent > 0)
             return g.name + " · " + percent + "%";
         return g.name;
     }
     private int GetGroupProbabilityPercent(TargetGroup g)
     {
         if (g == null || g.targets.Count == 0) return 0;
         float totalWeight = 0f;
         foreach (var grp in headUI.targetGroups)
         {
             if (grp.targets.Count > 0)
                 totalWeight += grp.weight;
         }
         if (totalWeight < 0.001f) return 0;
         return Mathf.RoundToInt(g.weight / totalWeight * 100f);
     }
     private string ExtractGroupName(string formattedName)
     {
         if (string.IsNullOrEmpty(formattedName)) return "";
         int sepIdx = formattedName.IndexOf(" · ");
         return sepIdx >= 0 ? formattedName.Substring(0, sepIdx) : formattedName;
     }
     private List<string> GetSortedGroupNames()
     {
         return headUI.targetGroups
             .OrderBy(g => ExtractGroupNumber(g.name))
             .Select(g => FormatGroupLabel(g))
             .ToList();
     }
     private void UpdateAtomChoices()
     {
         List<string> names = new List<string> { "[Player]", "[Hold]", "[Random]" };
         foreach (var a in allAtoms) if (a != null) names.Add(a.name);
         bool changed = names.Count != lastAtomNames.Count;
         if (!changed)
         {
             for (int i = 0; i < names.Count; i++)
             {
                 if (names[i] != lastAtomNames[i])
                 {
                     changed = true;
                     break;
                 }
             }
         }
         if (!changed) return;
         lastAtomNames = names;
         atomChooser.choices = names;
         if (!names.Contains(atomChooser.val)) atomChooser.val = names[0];
         OnAtomChanged(atomChooser.val);
     }
     private TargetGroup GetCurrentGroup()
     {
         string val = groupChooser?.val;
         if (string.IsNullOrEmpty(val)) return null;
         string name = ExtractGroupName(val);
         return headUI.targetGroups.FirstOrDefault(g => g.name == name);
     }
     private void OnGroupChanged(string name)
     {
         UpdateGroupHeader();
         if (string.IsNullOrEmpty(name)) { RebuildTargetList(); return; }
         var grp = GetCurrentGroup();
         if (grp != null)
         {
             groupWeight.valNoCallback = grp.weight;
             groupSwitchMin.valNoCallback = grp.switchMin;
             groupSwitchMax.valNoCallback = grp.switchMax;
             headUI.activeGroup = grp;
             headCtrl.NotifyGroupManuallySelected(grp);
         }
         RebuildTargetList();
     }
     private void OnGroupAutoSwitched(TargetGroup grp)
     {
         if (grp == null || groupChooser == null) return;
         var sorted = GetSortedGroupNames();
         groupChooser.choices = sorted;
         string formatted = FormatGroupLabel(grp);
         if (sorted.Contains(formatted))
         {
             groupChooser.valNoCallback = formatted;
         }
         else
         {
             for (int i = 0; i < sorted.Count; i++)
             {
                 if (ExtractGroupName(sorted[i]) == grp.name)
                 {
                     groupChooser.valNoCallback = sorted[i];
                     break;
                 }
             }
         }
         groupWeight.valNoCallback = grp.weight;
         groupSwitchMin.valNoCallback = grp.switchMin;
         groupSwitchMax.valNoCallback = grp.switchMax;
         UpdateGroupHeader();
 		RebuildTargetListQuiet();
     }
     private void AddGroup()
     {
         var name = GetNextAvailableGroupName();
         headUI.targetGroups.Add(new TargetGroup(name) { weight = 1f });
         RefreshGroupList();
         groupChooser.val = FormatGroupLabel(headUI.targetGroups.Last());
         headUI.RefreshExternalTargetChoices();
     }
     private void RemoveGroup()
     {
         if (headUI.targetGroups.Count == 0) return;
         var grp = GetCurrentGroup();
         if (grp != null)
         {
             foreach (var t in grp.targets)
                 loggedInvalidTargets.Remove(t);
             headUI.targetGroups.Remove(grp);
         }
         RefreshGroupList();
         headUI.RefreshExternalTargetChoices();
     }
     private void RefreshGroupList()
     {
         var currentGroup = GetCurrentGroup();
         var sorted = GetSortedGroupNames();
         groupChooser.choices = sorted;
         if (sorted.Count > 0)
         {
             if (currentGroup != null)
             {
                 string formatted = FormatGroupLabel(currentGroup);
                 if (sorted.Contains(formatted))
                     groupChooser.val = formatted;
                 else
                     groupChooser.val = sorted[0];
             }
             else if (!sorted.Contains(groupChooser.val))
             {
                 groupChooser.val = sorted[0];
             }
         }
         else groupChooser.val = "";
     }
     private void RefreshGroupLabels()
     {
         if (groupChooser == null) return;
         var currentGroup = GetCurrentGroup();
         var sorted = GetSortedGroupNames();
         groupChooser.choices = sorted;
         if (sorted.Count > 0)
         {
             string targetName = currentGroup != null
                 ? currentGroup.name
                 : ExtractGroupName(groupChooser.val);
             bool found = false;
             for (int i = 0; i < sorted.Count; i++)
             {
                 if (ExtractGroupName(sorted[i]) == targetName)
                 {
                     groupChooser.valNoCallback = sorted[i];
                     found = true;
                     break;
                 }
             }
             if (!found)
                 groupChooser.valNoCallback = sorted[0];
         }
         UpdateGroupHeader();
     }
     private void RefreshAtomList()
     {
         allAtoms.Clear();
         if (SuperController.singleton != null)
         {
             bool old = SuperController.singleton.showHiddenAtoms;
             SuperController.singleton.showHiddenAtoms = true;
             try
             {
                 allAtoms.AddRange(SuperController.singleton.GetAtoms());
             }
             finally
             {
                 SuperController.singleton.showHiddenAtoms = old;
             }
         }
     }
     private void OnAtomChanged(string name)
     {
         if (name == "[Player]") currentControls = new List<string> { "Camera" };
         else if (name == "[Hold]") currentControls = new List<string>();
         else if (name == "[Random]") currentControls = new List<string>();
         else
         {
             var a = allAtoms.FirstOrDefault(x => x.name == name);
             bool isSelf = (a == atom);
             currentControls = a != null ? GetAllControlsAndBones(a, isSelf) : new List<string>();
         }
         controlChooser.choices = currentControls;
         controlChooser.val = currentControls.Count > 0 ? currentControls[0] : "   ";
     }
     private List<string> GetAllControlsAndBones(Atom a, bool excludeSelfParts)
     {
         var res = new List<string>();
         if (a == null) return res;
         var controlNames = new HashSet<string>();
         if (a.freeControllers != null)
         {
             foreach (var fc in a.freeControllers)
             {
                 if (fc == null || string.IsNullOrEmpty(fc.name)) continue;
                 if (excludeSelfParts && SelfTargetExclusions.Contains(fc.name)) continue;
                 controlNames.Add(fc.name);
                 res.Add(fc.name);
             }
         }
         var bones = a.GetComponentsInChildren<DAZBone>();
         if (bones != null)
         {
             foreach (var b in bones)
             {
                 if (b == null || string.IsNullOrEmpty(b.name)) continue;
                 if (excludeSelfParts && SelfTargetExclusions.Contains(b.name)) continue;
                 if (controlNames.Contains(b.name + "Control")) continue;
                 res.Add(b.name);
             }
         }
         if (res.Count == 0 && a.mainController != null) res.Add("control");
         return res;
     }
     private void AddTargetToGroup()
     {
         if (headUI.targetGroups.Count == 0) AddGroup();
         var grp = GetCurrentGroup();
         if (grp == null) return;
         var def = new TargetDef();
         def.targetId = headUI.nextTargetId++;
         if (atomChooser.val == "[Player]") def.isPlayer = true;
         else if (atomChooser.val == "[Hold]")
         {
             def.isHold = true;
             def.anchorYaw = 0f;
             def.anchorPitch = 0f;
             def.watchPlayer = true;
             def.watchPersons = true;
         }
         else if (atomChooser.val == "[Random]")
         {
             def.isRandom = true;
         }
         else
         {
             def.atom = allAtoms.FirstOrDefault(a => a.name == atomChooser.val);
             def.controlName = controlChooser.val;
         }
         def.weight = 1f;
         def.durationMin = 1f;
         def.durationMax = 5f;
         def.speedMin = globalDefaultSpeed;
         def.speedMax = globalDefaultSpeed;
         def.scatter = 0f;
		bool defaultGazeAversion = def.isPlayer || def.isHold ||
		(def.atom != null && def.atom.type == "Person" && def.controlName == "headControl") ||
		def.isRandom;
		def.gazeAversionEnabled = defaultGazeAversion;
		def.defaultGazeAversion = defaultGazeAversion;
		def.headTurnEnabled = true;
		if (def.isRandom)
		{
		def.headTurnEnabled = false;
		}
         grp.targets.Add(def);
         CreateTargetRow(def);
         if (selectedTarget != null)
         {
             paramsPanel.Destroy();
             HighlightRow(null);
             selectedTarget = null;
         }
         headUI.activeGroup = grp;
         headCtrl.NotifyGroupManuallySelected(grp);
         headUI.activeTarget = null;
         headUI.isExternalOverride = false;
         headCtrl.ForceAutoReselect();
        RefreshAllRowLabels();
        RefreshGroupLabels();
        headUI.RefreshExternalTargetChoices();
        UpdateViewModeVisibility();
    }
	 
    private void ToggleViewMode()
    {
        isRandomMode = !isRandomMode;
        if (isRandomMode)
        {
            paramsPanel.Destroy();
            HighlightRow(null);
            selectedTarget = null;
        }
        UpdateViewModeVisibility();
    }
    private void UpdateViewModeVisibility()
    {
        bool showNormal = !isRandomMode;
        foreach (var go in uiObjects)
        {
            if (go == null) continue;
            if (toggleViewBtn != null && go == toggleViewBtn.gameObject) continue;
            go.SetActive(showNormal);
        }
        foreach (var entry in targetEntries)
        {
            if (entry != null) entry.gameObject.SetActive(showNormal);
        }
        if (groupPopup != null) groupPopup.gameObject.SetActive(showNormal);
        if (randomSettingsPanel != null)
            randomSettingsPanel.SetVisible(isRandomMode);
        if (toggleViewBtn != null && toggleViewBtn.label != null)
            toggleViewBtn.label = isRandomMode ? " ◄        Back to Targets" : " ►      Random Settings";
    }	 
	 
    public void SetVisible(bool visible)
    {
        foreach (var go in uiObjects) if (go != null) go.SetActive(visible);
        foreach (var entry in targetEntries) if (entry != null) entry.gameObject.SetActive(visible);
        if (groupPopup != null) groupPopup.gameObject.SetActive(visible);
        if (visible)
        {
            UpdateViewModeVisibility();
        }
        else
        {
            if (randomSettingsPanel != null) randomSettingsPanel.SetVisible(false);
            paramsPanel.Destroy();
            HighlightRow(null);
            selectedTarget = null;
            headUI.activeTarget = null;
            headUI.isExternalOverride = false;
            headCtrl.ForceAutoReselect();
        }
    }
     public void ClearSelection()
     {
         paramsPanel.Destroy();
         HighlightRow(null);
         selectedTarget = null;
     }
     public void RebuildUI() { RebuildTargetList(); }
     public void Destroy()
     {
         if (headCtrl != null)
             headCtrl.OnGroupSwitched -= OnGroupAutoSwitched;
         if (atomCheckCoroutine != null)
         {
             script.StopCoroutine(atomCheckCoroutine);
             atomCheckCoroutine = null;
         }
         UnsubscribeAtomEvents();
        loggedInvalidTargets.Clear();
        paramsPanel.Destroy();
        if (randomSettingsPanel != null)
        {
            randomSettingsPanel.Destroy();
            randomSettingsPanel = null;
        }
        foreach (var entry in targetEntries) { script.RemoveSpacer(entry); UnityEngine.Object.Destroy(entry.gameObject); }
         targetEntries.Clear();
         targetToRow.Clear();
         rowOriginalColors.Clear();
         rowOriginalTextColors.Clear();
         foreach (var go in uiObjects)
         {
             if (go != null)
             {
                 var uid = go.GetComponent<UIDynamic>();
                 if (uid != null) script.RemoveSpacer(uid);
                 else UnityEngine.Object.Destroy(go);
             }
         }
         uiObjects.Clear();
         if (atomPopup != null) script.RemovePopup(atomPopup);
         if (controlPopup != null) script.RemovePopup(controlPopup);
         if (groupPopup != null) script.RemovePopup(groupPopup);
         try { if (groupChooser != null) script.DeregisterStringChooser(groupChooser); } catch (Exception) { }
     }
 }
}