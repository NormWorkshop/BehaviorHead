using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BehaviorUtils;

namespace Norm
{
public class BehaviorHeadUI : MVRScript
{
private HeadController headController;
private EyesController eyesController;
private TargetsController targetsController;

public bool playerTargeted = false;
public bool gazeEnabled = true;
public bool externalControl = false;
public List<TargetGroup> targetGroups = new List<TargetGroup>();
public TargetDef activeTarget = null;
public TargetGroup activeGroup = null;
public int nextTargetId = 1;
public bool isExternalOverride = false;

private string currentTab = "";
private UIDynamicTextField emotionTextField;
private bool uiBuilt;
private UIDynamicTwinButton topTabs;
private UIDynamicTwinButton bottomTabs;

private static readonly Color ActiveTextColor = Color.white;
private static readonly Color ActiveBgColor = new Color(176f / 255f, 203f / 255f, 165f / 255f, 1f);
private Dictionary<Text, Color> originalTabTextColors = new Dictionary<Text, Color>();
private Dictionary<Image, Color> originalTabBgColors = new Dictionary<Image, Color>();
private bool tabColorsSaved = false;

private JSONStorableStringChooser externalTargetChooser;
private JSONStorableAction releaseTargetAction;
private Dictionary<string, TargetDef> externalTargetMap = new Dictionary<string, TargetDef>();

public override void Init()
{
try
{
BHUI.OnInitUI(this.CreateUIElement);
topTabs = BHUI.SetupTwinButton(this, "Head", () => SwitchTab("Head"), "Eyes", () => SwitchTab("Eyes"), false);
bottomTabs = BHUI.SetupTwinButton(this, "Targets", () => SwitchTab("Targets"), "Emotions", () => SwitchTab("Emotions"), true);
SaveTabColors();

headController = new HeadController(this, containingAtom, this);
eyesController = new EyesController(this, containingAtom, this, headController);
headController.SetEyesController(eyesController);
targetsController = new TargetsController(this, containingAtom, this, headController);

headController.OnAutoReselect += () => targetsController?.ClearSelection();

StartCoroutine(DeferredInit());
}
catch (Exception e)
{
SuperController.LogError("[BehaviorHeadUI] Init error: " + e);
}
}

private IEnumerator DeferredInit()
{
yield return new WaitForEndOfFrame();

headController.ShowUI();
eyesController.ShowUI();
targetsController.ShowUI();
SetupExternalTrigger();
CreateEmotionTabContent();

headController.SetVisible(false);
eyesController.SetVisible(false);
targetsController.SetVisible(false);
if (emotionTextField != null) emotionTextField.gameObject.SetActive(false);

uiBuilt = true;
SwitchTab("Head");
}

private void SetupExternalTrigger()
{
externalTargetChooser = new JSONStorableStringChooser(
"External Target",
new List<string>(),
"",
"External Target",
OnExternalTargetChanged
);
externalTargetChooser.isStorable = false;
externalTargetChooser.isRestorable = false;
RegisterStringChooser(externalTargetChooser);

releaseTargetAction = new JSONStorableAction("ReleaseTarget", DoReleaseTarget);
RegisterAction(releaseTargetAction);

RefreshExternalTargetChoices();
}

public void RefreshExternalTargetChoices()
{
if (externalTargetChooser == null) return;

var choices = new List<string>();
externalTargetMap.Clear();

foreach (var grp in targetGroups)
{
foreach (var t in grp.targets)
{
string label = string.Format("{0} / {1} [{2}]", grp.name, FormatTargetForExternal(t), t.targetId);
choices.Add(label);
externalTargetMap[label] = t;
}
}

string currentVal = externalTargetChooser.val;
externalTargetChooser.choices = choices;

if (!string.IsNullOrEmpty(currentVal) && choices.Contains(currentVal))
{
externalTargetChooser.valNoCallback = currentVal;
}
else
{
externalTargetChooser.valNoCallback = "";
}
}

private string FormatTargetForExternal(TargetDef t)
{
    if (t.isHold) return "[Hold]";
    if (t.isPlayer) return "[Player]";
    if (t.isRandom) return "[Random]";
if (string.IsNullOrEmpty(t.controlName)) return t.atom.name;

string controlDisplay = t.controlName;
if (controlDisplay == "headControl") controlDisplay = "HeadCtrl";
return t.atom.name + "." + controlDisplay;
}

private void OnExternalTargetChanged(string val)
{
if (string.IsNullOrEmpty(val)) return;

TargetDef def;
if (externalTargetMap.TryGetValue(val, out def))
{
headController.SetExternalOverrideTarget(def);
}
}

private void DoReleaseTarget()
{
headController.ForceAutoReselect();
if (externalTargetChooser != null)
externalTargetChooser.valNoCallback = "";
}

private void CreateEmotionTabContent()
{
emotionTextField = BHUI.SetupInfoText(this, "Интерфейс пуст", 100f, false);
emotionTextField.gameObject.SetActive(false);
}

private void SaveTabColors()
{
if (tabColorsSaved) return;
SaveTabButtonColors(topTabs?.buttonLeft, topTabs?.labelLeft);
SaveTabButtonColors(topTabs?.buttonRight, topTabs?.labelRight);
SaveTabButtonColors(bottomTabs?.buttonLeft, bottomTabs?.labelLeft);
SaveTabButtonColors(bottomTabs?.buttonRight, bottomTabs?.labelRight);
tabColorsSaved = true;
}

private void SaveTabButtonColors(Button btn, Text label)
{
if (label != null && !originalTabTextColors.ContainsKey(label))
originalTabTextColors[label] = label.color;
if (btn != null)
{
var img = btn.targetGraphic as Image;
if (img != null && !originalTabBgColors.ContainsKey(img))
originalTabBgColors[img] = img.color;
}
}

public void SwitchTab(string tab)
{
if (!uiBuilt || currentTab == tab) return;

switch (currentTab)
{
case "Head": headController?.SetVisible(false); break;
case "Eyes": eyesController?.SetVisible(false); break;
case "Targets": targetsController?.SetVisible(false); break;
case "Emotions": if (emotionTextField != null) emotionTextField.gameObject.SetActive(false); break;
}

currentTab = tab;
UpdateTabVisuals(tab);

switch (tab)
{
case "Head": headController?.SetVisible(true); break;
case "Eyes": eyesController?.SetVisible(true); break;
case "Targets": targetsController?.SetVisible(true); break;
case "Emotions": if (emotionTextField != null) emotionTextField.gameObject.SetActive(true); break;
}

Canvas.ForceUpdateCanvases();
}

private void UpdateTabVisuals(string activeTab)
{
SaveTabColors();
SetTabState(topTabs?.buttonLeft, topTabs?.labelLeft, activeTab == "Head");
SetTabState(topTabs?.buttonRight, topTabs?.labelRight, activeTab == "Eyes");
SetTabState(bottomTabs?.buttonLeft, bottomTabs?.labelLeft, activeTab == "Targets");
SetTabState(bottomTabs?.buttonRight, bottomTabs?.labelRight, activeTab == "Emotions");
}

private void SetTabState(Button btn, Text label, bool isActive)
{
if (label != null)
{
if (isActive)
{
label.color = ActiveTextColor;
label.fontStyle = FontStyle.Bold;
}
else
{
Color origColor;
label.color = originalTabTextColors.TryGetValue(label, out origColor) ? origColor : label.color;
label.fontStyle = FontStyle.Normal;
}
}
if (btn != null)
{
var img = btn.targetGraphic as Image;
if (img != null)
{
if (isActive)
img.color = ActiveBgColor;
else
{
Color origColor;
img.color = originalTabBgColors.TryGetValue(img, out origColor) ? origColor : img.color;
}
}
}
}

public void Update()
{
float dt = Time.deltaTime;
headController?.Update(dt);
eyesController?.Update(dt);
}

public void RemovePlayerTarget()
{
playerTargeted = false;
targetsController?.RebuildUI();
}

private void OnDestroy()
{
headController?.Destroy();
eyesController?.Destroy();
targetsController?.Destroy();
BHUI.OnDestroyUI();
}
}
}