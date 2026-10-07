using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using SimpleJSON;
using MeshVR;
using AssetBundles;
namespace BehaviorUtils
{
// ==================== TRIGGER HANDLER ====================
public class BHTriggerHandler : TriggerHandler
{
public static bool Loaded { get; private set; }
private static BHTriggerHandler instance;
private RectTransform triggerActionsPrefab;
private RectTransform triggerActionMiniPrefab;
private RectTransform triggerActionDiscretePrefab;
private RectTransform triggerActionTransitionPrefab;
    public static BHTriggerHandler Instance
     {
         get
         {
             if (instance == null) instance = new BHTriggerHandler();
             return instance;
         }
     }
     public static void LoadAssets()
     {
         SuperController.singleton.StartCoroutine(Instance.LoadAssetsInternal());
     }
     private IEnumerator LoadAssetsInternal()
     {
         foreach (var x in LoadAsset("z_ui2", "TriggerActionsPanel", p => triggerActionsPrefab = p)) yield return x;
         foreach (var x in LoadAsset("z_ui2", "TriggerActionMiniPanel", p => triggerActionMiniPrefab = p)) yield return x;
         foreach (var x in LoadAsset("z_ui2", "TriggerActionDiscretePanel", p => triggerActionDiscretePrefab = p)) yield return x;
         foreach (var x in LoadAsset("z_ui2", "TriggerActionTransitionPanel", p => triggerActionTransitionPrefab = p)) yield return x;
         Loaded = true;
     }
     private IEnumerable LoadAsset(string assetBundleName, string assetName, Action<RectTransform> assign)
     {
         AssetBundleLoadAssetOperation request = AssetBundleManager.LoadAssetAsync(assetBundleName, assetName, typeof(GameObject));
         if (request == null) throw new NullReferenceException("Null request");
         yield return request;
         GameObject go = request.GetAsset<GameObject>();
         if (go == null) throw new NullReferenceException("Null GameObject");
         RectTransform prefab = go.GetComponent<RectTransform>();
         if (prefab == null) throw new NullReferenceException("Null RectTransform");
         assign(prefab);
     }
     void TriggerHandler.RemoveTrigger(Trigger t) { }
     void TriggerHandler.DuplicateTrigger(Trigger t) { throw new NotImplementedException(); }
     RectTransform TriggerHandler.CreateTriggerActionsUI() { return UnityEngine.Object.Instantiate(triggerActionsPrefab); }
     RectTransform TriggerHandler.CreateTriggerActionMiniUI() { return UnityEngine.Object.Instantiate(triggerActionMiniPrefab); }
     RectTransform TriggerHandler.CreateTriggerActionDiscreteUI() { return UnityEngine.Object.Instantiate(triggerActionDiscretePrefab); }
     RectTransform TriggerHandler.CreateTriggerActionTransitionUI() { return UnityEngine.Object.Instantiate(triggerActionTransitionPrefab); }
     void TriggerHandler.RemoveTriggerActionUI(RectTransform rt) { UnityEngine.Object.Destroy(rt?.gameObject); }
 }
 // ==================== TRIGGER EVENT ====================
 public class BHTriggerEvent
 {
     public string Name { get; private set; }
     private Trigger trigger;
     private bool needInit = true;
     private MVRScript owner;
     public BHTriggerEvent(MVRScript owner, string name)
     {
         this.owner = owner;
         Name = name;
         trigger = new Trigger();
         trigger.handler = BHTriggerHandler.Instance;
     }
     public void OpenPanel()
     {
         if (!BHTriggerHandler.Loaded) return;
         trigger.triggerActionsParent = owner.UITransform;
         trigger.InitTriggerUI();
         trigger.OpenTriggerActionsPanel();
         if (needInit)
         {
             Transform panel = trigger.triggerActionsPanel.Find("Panel");
             panel.Find("Header Text").GetComponent<Text>().text = Name;
             panel.Find("Trigger Name Text").gameObject.SetActive(false);
             Transform content = trigger.triggerActionsPanel.Find("Content");
             content.Find("Tab1/Label").GetComponent<Text>().text = "Event Actions";
             content.Find("Tab2").gameObject.SetActive(false);
             content.Find("Tab3").gameObject.SetActive(false);
             needInit = false;
         }
     }
     public void Trigger() { trigger.active = true; trigger.active = false; }
     public JSONClass GetJSON(string subScenePrefix) { return trigger.GetJSON(subScenePrefix); }
     public void RestoreFromJSON(JSONClass jc, string subScenePrefix, bool isMerge, bool setMissingToDefault)
     {
         if (jc.HasKey(Name))
         {
             JSONClass tc = jc[Name].AsObject;
             if (tc != null) trigger.RestoreFromJSON(tc, subScenePrefix, isMerge);
         }
         else if (setMissingToDefault) trigger.RestoreFromJSON(new JSONClass());
     }
     public void Remove() { trigger.handler.RemoveTrigger(trigger); }
     public void SyncAtomNames() { }
     public void Update() { }
 }
 // ==================== EXPANDABLE GROUP ====================
 public class BHUIExpandableGroup : MonoBehaviour
 {
     public Text headerLabel;
     public Text indicatorText;
     public Text modifiedIndicator;
     public Button headerButton;
     public UIDynamic contentContainer;
     public bool isExpanded = false;
     
     private MVRScript script;
     private List<GameObject> childGameObjects = new List<GameObject>();
     private List<JSONStorableParam> childStorables = new List<JSONStorableParam>();
     private Func<bool> modifiedChecker;
     private Action<bool> onToggleCallback;
     
     private static List<BHUIExpandableGroup> openGroups = new List<BHUIExpandableGroup>();
     
     public static void CloseAllExcept(BHUIExpandableGroup except)
     {
         for (int i = openGroups.Count - 1; i >= 0; i--)
         {
             if (openGroups[i] != except && openGroups[i] != null)
             {
                 openGroups[i].SetExpanded(false);
             }
         }
     }
     
     public void SetExpanded(bool expanded)
     {
         if (isExpanded == expanded) return;
         isExpanded = expanded;
         
         if (expanded)
         {
             CloseAllExcept(this);
             if (!openGroups.Contains(this)) openGroups.Add(this);
         }
         else
         {
             openGroups.Remove(this);
         }
         
         if (contentContainer != null)
         {
             contentContainer.gameObject.SetActive(expanded);
             float newHeight = expanded ? CalculateContentHeight() : 0f;
             contentContainer.height = newHeight;
             
             // Принудительный пересчёт layout
             RectTransform rt = contentContainer.GetComponent<RectTransform>();
             if (rt != null)
                 LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
         }
         
         if (indicatorText != null)
             indicatorText.text = expanded ? "▲" : "▼";
         
         if (onToggleCallback != null)
             onToggleCallback(expanded);
         
         Canvas.ForceUpdateCanvases();
     }
     
     private float CalculateContentHeight()
     {
         if (contentContainer == null) return 0f;
         
         var vlg = contentContainer.GetComponent<VerticalLayoutGroup>();
         float spacing = vlg != null ? vlg.spacing : 0f;
         float paddingTop = vlg != null && vlg.padding != null ? vlg.padding.top : 0f;
         float paddingBottom = vlg != null && vlg.padding != null ? vlg.padding.bottom : 0f;
         
         float totalHeight = paddingTop + paddingBottom;
         int childCount = 0;
         
         foreach (Transform child in contentContainer.transform)
         {
             if (child.gameObject.activeSelf)
             {
                 var le = child.GetComponent<LayoutElement>();
                 if (le != null)
                 {
                     float h = le.preferredHeight > 0 ? le.preferredHeight : le.minHeight;
                     totalHeight += h > 0 ? h : 50f;
                 }
                 else
                 {
                     totalHeight += 50f;
                 }
                 childCount++;
             }
         }
         
         if (childCount > 1)
             totalHeight += spacing * (childCount - 1);
         
         return totalHeight;
     }
     
     public void SetOnToggleCallback(Action<bool> callback)
     {
         onToggleCallback = callback;
     }
     
     public void SetModifiedChecker(Func<bool> checker)
     {
         modifiedChecker = checker;
         UpdateModifiedIndicator();
     }
     
     public void UpdateModifiedIndicator()
     {
         if (modifiedIndicator == null) return;
         bool modified = modifiedChecker != null && modifiedChecker();
         modifiedIndicator.text = modified ? "*" : "";
     }
     
     public void AddChildGameObject(GameObject go)
     {
         childGameObjects.Add(go);
     }
     
     public void AddChildStorable(JSONStorableParam storable)
     {
         childStorables.Add(storable);
     }
     
     public void DestroyChildren()
     {
         foreach (var s in childStorables)
         {
             if (s is JSONStorableFloat) script.DeregisterFloat(s as JSONStorableFloat);
             else if (s is JSONStorableBool) script.DeregisterBool(s as JSONStorableBool);
         }
         childStorables.Clear();
         
         foreach (var go in childGameObjects)
         {
             if (go != null) UnityEngine.Object.Destroy(go);
         }
         childGameObjects.Clear();
     }
     
     public void SetScript(MVRScript s)
     {
         script = s;
     }
 }
 // ==================== UI HELPERS ====================
 public class BHSliderPair
 {
     public UIDynamicSlider uiSlider;
     public JSONStorableFloat storableFloat;
 }
 public class UIDynamicUtils : UIDynamic { }
 public class UIDynamicTwinButton : UIDynamicUtils
 {
     public Text labelLeft;
     public Text labelRight;
     public Button buttonLeft;
     public Button buttonRight;
 }
 public class UIDynamicLabelXButton : UIDynamicUtils
 {
     public Text label;
     public Button button;
 }
 public static class BHUI
 {
     public delegate Transform CreateUIElement(Transform prefab, bool rightSide);
     private static CreateUIElement ourCreateUIElement;
     private static GameObject ourTwinButtonPrefab;
     private static GameObject ourLabelXButtonPrefab;
     public static UIDynamicToggle SetupToggle(MVRScript script, string label, bool defaultValue, bool rightSide)
     {
         JSONStorableBool storable = new JSONStorableBool(label, defaultValue);
         storable.storeType = JSONStorableParam.StoreType.Full;
         UIDynamicToggle toggle = script.CreateToggle(storable, rightSide);
         script.RegisterBool(storable);
         return toggle;
     }
     public static UIDynamicToggle SetupToggle(MVRScript script, string label, bool defaultValue, bool rightSide, Transform parent)
     {
         var toggle = SetupToggle(script, label, defaultValue, rightSide);
         if (parent != null) toggle.transform.SetParent(parent, false);
         return toggle;
     }
     public static BHSliderPair SetupSliderFloat(MVRScript script, string label, float defaultValue, float minValue, float maxValue, bool rightSide)
     {
         JSONStorableFloat storable = new JSONStorableFloat(label, defaultValue, minValue, maxValue, true, true);
         storable.storeType = JSONStorableParam.StoreType.Full;
         UIDynamicSlider slider = script.CreateSlider(storable, rightSide);
         script.RegisterFloat(storable);
         return new BHSliderPair { uiSlider = slider, storableFloat = storable };
     }
     public static BHSliderPair SetupSliderFloat(MVRScript script, string label, float defaultValue, float minValue, float maxValue, bool rightSide, Transform parent)
     {
         var pair = SetupSliderFloat(script, label, defaultValue, minValue, maxValue, rightSide);
         if (parent != null) pair.uiSlider.transform.SetParent(parent, false);
         return pair;
     }
     public static BHSliderPair SetupSliderInt(MVRScript script, string label, int defaultValue, int minValue, int maxValue, bool rightSide)
     {
         JSONStorableFloat storable = new JSONStorableFloat(label, defaultValue, minValue, maxValue, true, true);
         storable.storeType = JSONStorableParam.StoreType.Full;
         UIDynamicSlider slider = script.CreateSlider(storable, rightSide);
         slider.slider.wholeNumbers = true;
         slider.valueFormat = "F0";
         script.RegisterFloat(storable);
         return new BHSliderPair { uiSlider = slider, storableFloat = storable };
     }
     public static BHSliderPair SetupSliderInt(MVRScript script, string label, int defaultValue, int minValue, int maxValue, bool rightSide, Transform parent)
     {
         var pair = SetupSliderInt(script, label, defaultValue, minValue, maxValue, rightSide);
         if (parent != null) pair.uiSlider.transform.SetParent(parent, false);
         return pair;
     }
     public static UIDynamicButton SetupButton(MVRScript script, string label, UnityAction callback, bool rightSide)
     {
         UIDynamicButton button = script.CreateButton(label, rightSide);
         if (callback != null) button.button.onClick.AddListener(callback);
         return button;
     }
     public static UIDynamicButton SetupButton(MVRScript script, string label, UnityAction callback, bool rightSide, Transform parent)
     {
         UIDynamicButton button = SetupButton(script, label, callback, rightSide);
         if (parent != null) button.transform.SetParent(parent, false);
         return button;
     }
     public static JSONStorableAction SetupAction(MVRScript script, string name, JSONStorableAction.ActionCallback callback)
     {
         JSONStorableAction action = new JSONStorableAction(name, callback);
         script.RegisterAction(action);
         return action;
     }
     public static UIDynamic SetupSpacer(MVRScript script, float height, bool rightSide)
     {
         UIDynamic spacer = script.CreateSpacer(rightSide);
         spacer.height = height;
         return spacer;
     }
     public static UIDynamic SetupColoredSpacer(MVRScript script, float height, Color color, bool rightSide)
     {
         UIDynamic spacer = script.CreateSpacer(rightSide);
         spacer.height = height;
         Image img = spacer.gameObject.AddComponent<Image>();
         img.color = color;
         LayoutElement le = spacer.GetComponent<LayoutElement>();
         if (le != null) { le.minHeight = height; le.preferredHeight = height; }
         return spacer;
     }
     public static UIDynamicTextField SetupInfoText(MVRScript script, string text, float height, bool rightSide)
     {
         JSONStorableString storable = new JSONStorableString("Info", text);
         UIDynamicTextField textfield = script.CreateTextField(storable, rightSide);
         textfield.height = height;
         return textfield;
     }
     public static JSONStorableStringChooser SetupStringChooser(MVRScript script, string label, List<string> entries, bool rightSide)
     {
         string defaultEntry = entries.Count > 0 ? entries[0] : "";
         JSONStorableStringChooser storable = new JSONStorableStringChooser(label, entries, defaultEntry, label);
         script.CreateScrollablePopup(storable, rightSide);
         script.RegisterStringChooser(storable);
         return storable;
     }
     public static JSONStorableStringChooser SetupStringChooser(MVRScript script, string label, List<string> entries, int defaultIndex, bool rightSide)
     {
         string defaultEntry = (defaultIndex >= 0 && defaultIndex < entries.Count) ? entries[defaultIndex] : "";
         JSONStorableStringChooser storable = new JSONStorableStringChooser(label, entries, defaultEntry, label);
         script.CreateScrollablePopup(storable, rightSide);
         script.RegisterStringChooser(storable);
         return storable;
     }
     public static JSONStorableStringChooser SetupStringChooser(MVRScript script, string label, List<string> entries, bool rightSide, out UIDynamicPopup popup)
     {
         string defaultEntry = entries.Count > 0 ? entries[0] : "";
         JSONStorableStringChooser storable = new JSONStorableStringChooser(label, entries, defaultEntry, label);
         popup = script.CreateScrollablePopup(storable, rightSide);
         script.RegisterStringChooser(storable);
         return storable;
     }
     public static JSONStorableStringChooser SetupStringChooser(MVRScript script, string label, List<string> entries, int defaultIndex, bool rightSide, out UIDynamicPopup popup)
     {
         string defaultEntry = (defaultIndex >= 0 && defaultIndex < entries.Count) ? entries[defaultIndex] : "";
         JSONStorableStringChooser storable = new JSONStorableStringChooser(label, entries, defaultEntry, label);
         popup = script.CreateScrollablePopup(storable, rightSide);
         script.RegisterStringChooser(storable);
         return storable;
     }
     public static JSONStorableStringChooser SetupFilterableStringChooser(MVRScript script, string label, List<string> entries, float popupHeight, bool rightSide)
     {
         string defaultEntry = entries.Count > 0 ? entries[0] : "";
         JSONStorableStringChooser storable = new JSONStorableStringChooser(label, entries, defaultEntry, label);
         UIDynamicPopup popup = script.CreateFilterablePopup(storable, rightSide);
         popup.popupPanelHeight = popupHeight;
         popup.labelWidth = -8;
         script.RegisterStringChooser(storable);
         return storable;
     }
     public static JSONStorableStringChooser SetupFilterableStringChooser(MVRScript script, string label, List<string> entries, int defaultIndex, float popupHeight, bool rightSide)
     {
         string defaultEntry = (defaultIndex >= 0 && defaultIndex < entries.Count) ? entries[defaultIndex] : "";
         JSONStorableStringChooser storable = new JSONStorableStringChooser(label, entries, defaultEntry, label);
         UIDynamicPopup popup = script.CreateFilterablePopup(storable, rightSide);
         popup.popupPanelHeight = popupHeight;
         popup.labelWidth = -8;
         script.RegisterStringChooser(storable);
         return storable;
     }
     public static JSONStorableStringChooser SetupFilterableStringChooser(MVRScript script, string label, List<string> entries, float popupHeight, bool rightSide, out UIDynamicPopup popup)
     {
         string defaultEntry = entries.Count > 0 ? entries[0] : "";
         JSONStorableStringChooser storable = new JSONStorableStringChooser(label, entries, defaultEntry, label);
         popup = script.CreateFilterablePopup(storable, rightSide);
         popup.popupPanelHeight = popupHeight;
         popup.labelWidth = -8;
         script.RegisterStringChooser(storable);
         return storable;
     }
     public static JSONStorableStringChooser SetupScrollablePopup(MVRScript script, List<string> entries, bool rightSide, out UIDynamicPopup popup)
     {
         string defaultEntry = entries.Count > 0 ? entries[0] : "";
         JSONStorableStringChooser storable = new JSONStorableStringChooser("", entries, defaultEntry, "");
         popup = script.CreateScrollablePopup(storable, rightSide);
         popup.label = "";
         popup.labelWidth = 0;
         script.RegisterStringChooser(storable);
         return storable;
     }
     public static void OnInitUI(CreateUIElement createUIElementCallback) { ourCreateUIElement = createUIElementCallback; }
     public static void OnDestroyUI()
     {
         if (ourTwinButtonPrefab != null) { UnityEngine.Object.Destroy(ourTwinButtonPrefab); ourTwinButtonPrefab = null; }
         if (ourLabelXButtonPrefab != null) { UnityEngine.Object.Destroy(ourLabelXButtonPrefab); ourLabelXButtonPrefab = null; }
     }
     public static BHUIExpandableGroup SetupExpandableGroup(MVRScript script, string title, bool rightSide, Transform parent)
     {
         // Создаём кнопку заголовка
         var headerBtn = script.CreateButton(title, rightSide);
         headerBtn.button.onClick.RemoveAllListeners();
         
         var label = headerBtn.button.GetComponentInChildren<Text>();
         if (label != null)
         {
             label.alignment = TextAnchor.MiddleLeft;
             label.fontSize = 29;
             var labelRt = label.GetComponent<RectTransform>();
             labelRt.offsetMin = new Vector2(12, 0);
         }
         
         // Добавляем индикатор стрелки
         var indGo = new GameObject("Indicator");
         indGo.transform.SetParent(headerBtn.button.transform, false);
         RectTransform indRt = indGo.AddComponent<RectTransform>();
         indRt.anchorMax = new Vector2(1, 1);
         indRt.anchorMin = new Vector2(1, 0);
         indRt.offsetMax = new Vector2(-11, -7);
         indRt.offsetMin = new Vector2(-34, 7);
         Text indText = indGo.AddComponent<Text>();
         indText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
         indText.fontSize = 20;
         indText.alignment = TextAnchor.MiddleCenter;
         indText.color = new Color(0.3f, 0.3f, 0.3f, 1f);
         indText.text = "▼";
         
         // Добавляем индикатор изменений
         var modGo = new GameObject("Modified");
         modGo.transform.SetParent(headerBtn.button.transform, false);
         RectTransform modRt = modGo.AddComponent<RectTransform>();
         modRt.anchorMax = new Vector2(1, 1);
         modRt.anchorMin = new Vector2(1, 0);
         modRt.offsetMax = new Vector2(-40, -9);
         modRt.offsetMin = new Vector2(-60, 7);
         Text modText = modGo.AddComponent<Text>();
         modText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
         modText.fontSize = 30;
         modText.alignment = TextAnchor.MiddleCenter;
         modText.color = new Color(0.2f, 0.2f, 0.2f, 1f);
         modText.text = "";
         
         // Создаём контейнер для контента (spacer с фиксированной высотой)
         var contentDynamic = script.CreateSpacer(rightSide);
         contentDynamic.height = 0f;
         contentDynamic.gameObject.SetActive(false);
         
         // Тёмный фон для визуального выделения группы
         Image groupBg = contentDynamic.gameObject.GetComponent<Image>();
         if (groupBg == null) groupBg = contentDynamic.gameObject.AddComponent<Image>();
         groupBg.color = new Color(0.12f, 0.12f, 0.12f, 0.6f);
         
         // VerticalLayoutGroup для размещения дочерних элементов
         var vlg = contentDynamic.gameObject.GetComponent<VerticalLayoutGroup>();
         if (vlg == null) vlg = contentDynamic.gameObject.AddComponent<VerticalLayoutGroup>();
         vlg.childControlHeight = false;
         vlg.childControlWidth = true;
         vlg.childForceExpandWidth = true;
         vlg.childForceExpandHeight = false;
         vlg.spacing = 4f;
         vlg.padding = new RectOffset(6, 6, 4, 4);
         
         // Создаём компонент группы
         BHUIExpandableGroup group = headerBtn.gameObject.AddComponent<BHUIExpandableGroup>();
         group.SetScript(script);
         group.headerButton = headerBtn.button;
         group.headerLabel = label;
         group.indicatorText = indText;
         group.modifiedIndicator = modText;
         group.contentContainer = contentDynamic;
         
         // Настраиваем клик
         headerBtn.button.onClick.AddListener(() =>
         {
             group.SetExpanded(!group.isExpanded);
         });
         
         if (parent != null)
         {
             headerBtn.transform.SetParent(parent, false);
             contentDynamic.transform.SetParent(parent, false);
         }
         
         return group;
     }
     public static UIDynamicTwinButton SetupTwinButton(MVRScript script, string leftLabel, UnityAction leftCallback, string rightLabel, UnityAction rightCallback, bool rightSide)
     {
         if (ourTwinButtonPrefab == null)
         {
             ourTwinButtonPrefab = new GameObject("TwinButton");
             ourTwinButtonPrefab.SetActive(false);
             RectTransform rt = ourTwinButtonPrefab.AddComponent<RectTransform>();
             rt.anchorMax = new Vector2(0, 1); rt.anchorMin = new Vector2(0, 1);
             rt.offsetMax = new Vector2(535, -500); rt.offsetMin = new Vector2(10, -600);
             LayoutElement le = ourTwinButtonPrefab.AddComponent<LayoutElement>();
             le.flexibleWidth = 1; le.minHeight = 50; le.minWidth = 350; le.preferredHeight = 50; le.preferredWidth = 500;
             RectTransform buttonTransform = script.manager.configurableScrollablePopupPrefab.transform.Find("Button") as RectTransform;
             buttonTransform = UnityEngine.Object.Instantiate(buttonTransform, ourTwinButtonPrefab.transform);
             buttonTransform.name = "ButtonLeft";
             buttonTransform.anchorMax = new Vector2(0.5f, 1.0f); buttonTransform.anchorMin = new Vector2(0.0f, 0.0f);
             buttonTransform.offsetMax = new Vector2(-3, 0); buttonTransform.offsetMin = new Vector2(0, 0);
             Button buttonLeft = buttonTransform.GetComponent<Button>();
             Text labelLeft = buttonTransform.Find("Text").GetComponent<Text>();
             buttonTransform = UnityEngine.Object.Instantiate(buttonTransform, ourTwinButtonPrefab.transform);
             buttonTransform.name = "ButtonRight";
             buttonTransform.anchorMax = new Vector2(1.0f, 1.0f); buttonTransform.anchorMin = new Vector2(0.5f, 0.0f);
             buttonTransform.offsetMax = new Vector2(0, 0); buttonTransform.offsetMin = new Vector2(3, 0);
             Button buttonRight = buttonTransform.GetComponent<Button>();
             Text labelRight = buttonTransform.Find("Text").GetComponent<Text>();
             UIDynamicTwinButton uid = ourTwinButtonPrefab.AddComponent<UIDynamicTwinButton>();
             uid.labelLeft = labelLeft; uid.labelRight = labelRight;
             uid.buttonLeft = buttonLeft; uid.buttonRight = buttonRight;
         }
         if (ourCreateUIElement == null)
         {
             SuperController.LogError("BHUI.SetupTwinButton: You need to call BHUI.OnInitUI(script.CreateUIElement) first.");
             return null;
         }
         Transform t = ourCreateUIElement(ourTwinButtonPrefab.transform, rightSide);
         UIDynamicTwinButton twin = t.GetComponent<UIDynamicTwinButton>();
         twin.labelLeft.text = leftLabel; twin.labelRight.text = rightLabel;
         twin.buttonLeft.onClick.AddListener(leftCallback);
         twin.buttonRight.onClick.AddListener(rightCallback);
         t.gameObject.SetActive(true);
         return twin;
     }
     public static UIDynamicLabelXButton SetupLabelXButton(MVRScript script, string label, UnityAction removeCallback, bool rightSide)
     {
         if (ourLabelXButtonPrefab == null)
         {
             ourLabelXButtonPrefab = new GameObject("LabelXButton");
             ourLabelXButtonPrefab.SetActive(false);
             RectTransform rt = ourLabelXButtonPrefab.AddComponent<RectTransform>();
             rt.anchorMax = new Vector2(0, 1); rt.anchorMin = new Vector2(0, 1);
             rt.offsetMax = new Vector2(535, -500); rt.offsetMin = new Vector2(10, -600);
             LayoutElement le = ourLabelXButtonPrefab.AddComponent<LayoutElement>();
             le.flexibleWidth = 1; le.minHeight = 50; le.minWidth = 350; le.preferredHeight = 50; le.preferredWidth = 500;
             RectTransform backgroundTransform = script.manager.configurableScrollablePopupPrefab.transform.Find("Background") as RectTransform;
             backgroundTransform = UnityEngine.Object.Instantiate(backgroundTransform, ourLabelXButtonPrefab.transform);
             backgroundTransform.name = "Background";
             backgroundTransform.anchorMax = new Vector2(1, 1); backgroundTransform.anchorMin = new Vector2(0, 0);
             backgroundTransform.offsetMax = new Vector2(0, 0); backgroundTransform.offsetMin = new Vector2(0, -10);
             RectTransform buttonTransform = script.manager.configurableScrollablePopupPrefab.transform.Find("Button") as RectTransform;
             buttonTransform = UnityEngine.Object.Instantiate(buttonTransform, ourLabelXButtonPrefab.transform);
             buttonTransform.name = "Button";
             buttonTransform.anchorMax = new Vector2(1, 1); buttonTransform.anchorMin = new Vector2(1, 0);
             buttonTransform.offsetMax = new Vector2(0, 0); buttonTransform.offsetMin = new Vector2(-60, -10);
             Button buttonButton = buttonTransform.GetComponent<Button>();
             Text buttonText = buttonTransform.Find("Text").GetComponent<Text>();
             buttonText.text = "X";
             RectTransform labelTransform = buttonText.rectTransform;
             labelTransform = UnityEngine.Object.Instantiate(labelTransform, ourLabelXButtonPrefab.transform);
             labelTransform.name = "Text";
             labelTransform.anchorMax = new Vector2(1, 1); labelTransform.anchorMin = new Vector2(0, 0);
             labelTransform.offsetMax = new Vector2(-65, 0); labelTransform.offsetMin = new Vector2(5, -10);
             Text labelText = labelTransform.GetComponent<Text>();
             labelText.verticalOverflow = VerticalWrapMode.Overflow;
             UIDynamicLabelXButton uid = ourLabelXButtonPrefab.AddComponent<UIDynamicLabelXButton>();
             uid.label = labelText; uid.button = buttonButton;
         }
         if (ourCreateUIElement == null)
         {
             SuperController.LogError("BHUI.SetupLabelXButton: You need to call BHUI.OnInitUI(script.CreateUIElement) first.");
             return null;
         }
         Transform t = ourCreateUIElement(ourLabelXButtonPrefab.transform, rightSide);
         UIDynamicLabelXButton element = t.GetComponent<UIDynamicLabelXButton>();
         element.label.text = label;
         element.button.onClick.AddListener(removeCallback);
         t.gameObject.SetActive(true);
         return element;
     }
     public static UIDynamicLabelXButton SetupClickableLabelXButton(MVRScript script, string label, UnityAction removeCallback, UnityAction<BaseEventData> clickCallback, bool rightSide)
     {
         UIDynamicLabelXButton element = SetupLabelXButton(script, label, removeCallback, rightSide);
         if (element != null && element.label != null)
         {
             element.label.raycastTarget = true;
             EventTrigger trigger = element.label.gameObject.GetComponent<EventTrigger>();
             if (trigger == null) trigger = element.label.gameObject.AddComponent<EventTrigger>();
             trigger.triggers.Clear();
             EventTrigger.Entry entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
             entry.callback.AddListener(clickCallback);
             trigger.triggers.Add(entry);
         }
         return element;
     }
     public static void RemoveUIElements(MVRScript script, List<object> menuElements)
     {
         for (int i = 0; i < menuElements.Count; ++i)
         {
             if (menuElements[i] is JSONStorableParam)
             {
                 JSONStorableParam jsp = menuElements[i] as JSONStorableParam;
                 if (jsp is JSONStorableFloat) script.RemoveSlider(jsp as JSONStorableFloat);
                 else if (jsp is JSONStorableBool) script.RemoveToggle(jsp as JSONStorableBool);
                 else if (jsp is JSONStorableColor) script.RemoveColorPicker(jsp as JSONStorableColor);
                 else if (jsp is JSONStorableString) script.RemoveTextField(jsp as JSONStorableString);
                 else if (jsp is JSONStorableStringChooser)
                 {
                     JSONStorableStringChooser jssc = jsp as JSONStorableStringChooser;
                     RectTransform popupPanel = jssc.popup?.popupPanel;
                     script.RemovePopup(jssc);
                     if (popupPanel != null) UnityEngine.Object.Destroy(popupPanel.gameObject);
                 }
             }
             else if (menuElements[i] is UIDynamic)
             {
                 UIDynamic uid = menuElements[i] as UIDynamic;
                 if (uid is UIDynamicButton) script.RemoveButton(uid as UIDynamicButton);
                 else if (uid is UIDynamicUtils) script.RemoveSpacer(uid);
                 else if (uid is UIDynamicSlider) script.RemoveSlider(uid as UIDynamicSlider);
                 else if (uid is UIDynamicToggle) script.RemoveToggle(uid as UIDynamicToggle);
                 else if (uid is UIDynamicColorPicker) script.RemoveColorPicker(uid as UIDynamicColorPicker);
                 else if (uid is UIDynamicTextField) script.RemoveTextField(uid as UIDynamicTextField);
                 else if (uid is UIDynamicPopup)
                 {
                     UIDynamicPopup uidp = uid as UIDynamicPopup;
                     RectTransform popupPanel = uidp.popup?.popupPanel;
                     script.RemovePopup(uidp);
                     if (popupPanel != null) UnityEngine.Object.Destroy(popupPanel.gameObject);
                 }
                 else script.RemoveSpacer(uid);
             }
         }
         menuElements.Clear();
     }
     public static void AdjustSliderRange(JSONStorableFloat slider)
     {
         float m = Mathf.Log10(slider.val);
         m = Mathf.Max(Mathf.Ceil(m), 1);
         slider.max = Mathf.Pow(10, m);
     }
     public static void AdjustMaxSliderFromMin(float minValue, JSONStorableFloat maxSlider)
     {
         if (maxSlider.slider != null) maxSlider.max = maxSlider.slider.maxValue;
         float v = Mathf.Max(minValue, maxSlider.val);
         float m = Mathf.Max(v, maxSlider.max);
         m = Mathf.Max(Mathf.Ceil(Mathf.Log10(m)), 1);
         maxSlider.max = Mathf.Pow(10, m);
         maxSlider.valNoCallback = v;
     }
     public static void AdjustMinSliderFromMax(float maxValue, JSONStorableFloat minSlider)
     {
         if (minSlider.slider != null) minSlider.min = minSlider.slider.minValue;
         float v = Mathf.Min(maxValue, minSlider.val);
         minSlider.min = Mathf.Min(minSlider.min, maxValue);
         minSlider.valNoCallback = v;
     }
 }
}
