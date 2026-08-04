#if UNITY_EDITOR
using Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class PetFoodBarPrefabBuilder
{
    private const string PrefabPath =
        "Assets/Resources/TryGameBuildRes/gui/ui_game/sub_home_pet_food_bar.prefab";

    [InitializeOnLoadMethod]
    private static void ScheduleEnsurePrefab()
    {
        EditorApplication.delayCall -= EnsurePrefab;
        EditorApplication.delayCall += EnsurePrefab;
    }

    [MenuItem("TryGame/Pet/Rebuild Food Bar Prefab")]
    public static void RebuildPrefab()
    {
        BuildPrefab(true);
    }

    private static void EnsurePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            BuildPrefab(false);
        }
    }

    private static void BuildPrefab(bool log)
    {
        GameObject root = NewUiObject("HomePetFoodBarSubViewRoot", null);
        HomePetFoodBarSubView subView = root.AddComponent<HomePetFoodBarSubView>();

        GameObject miniBackgroundObject = NewUiObject("MiniFoodTrayBackground", root.transform);
        RectTransform miniBackground = miniBackgroundObject.GetComponent<RectTransform>();
        Center(miniBackground);
        Image miniBackgroundImage = miniBackgroundObject.AddComponent<Image>();
        miniBackgroundImage.color = new Color(0.08f, 0.06f, 0.04f, 0.96f);
        miniBackgroundImage.raycastTarget = true;
        miniBackgroundObject.SetActive(false);

        GameObject panelObject = NewUiObject("BarPanel", root.transform);
        RectTransform panel = panelObject.GetComponent<RectTransform>();
        Center(panel);
        panel.sizeDelta = new Vector2(340f, 92f);
        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0.08f, 0.06f, 0.04f, 0.88f);
        panelImage.raycastTarget = false;

        GameObject viewportObject = NewUiObject("Viewport", panelObject.transform);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        Stretch(viewport, new Vector2(8f, 8f), new Vector2(-8f, -8f));
        Image viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
        viewportImage.raycastTarget = false;
        viewportObject.AddComponent<RectMask2D>();

        GameObject contentObject = NewUiObject("Content", viewportObject.transform);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 0.5f);
        content.anchorMax = new Vector2(0f, 0.5f);
        content.pivot = new Vector2(0f, 0.5f);
        HorizontalLayoutGroup layout = contentObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = panelObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;

        GameObject emptyObject = NewUiObject("Empty", panelObject.transform);
        RectTransform emptyRect = emptyObject.GetComponent<RectTransform>();
        Stretch(emptyRect, Vector2.zero, Vector2.zero);
        Text emptyText = emptyObject.AddComponent<Text>();
        emptyText.font = ResolveFont();
        emptyText.fontSize = 20;
        emptyText.alignment = TextAnchor.MiddleCenter;
        emptyText.color = new Color(1f, 1f, 1f, 0.8f);
        emptyText.text = "暂无食物";
        emptyText.raycastTarget = false;

        GameObject itemTemplate = BuildFoodItemTemplate(content);
        GameObject emptyTemplate = BuildEmptySlotTemplate(content);

        GameObject dragObject = NewUiObject("PetFoodDragVisual", root.transform);
        Image dragImage = dragObject.AddComponent<Image>();
        dragImage.preserveAspect = true;
        dragImage.raycastTarget = false;
        dragImage.rectTransform.sizeDelta = new Vector2(72f, 72f);
        dragObject.SetActive(false);

        subView.EditorConfigure(
            miniBackground,
            panel,
            viewport,
            content,
            layout,
            scroll,
            emptyText,
            dragImage,
            itemTemplate,
            emptyTemplate);
        panelObject.SetActive(false);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (log)
        {
            Debug.Log($"[PetFoodBarPrefabBuilder] 已重建：{PrefabPath}");
        }
    }

    private static GameObject BuildFoodItemTemplate(RectTransform parent)
    {
        GameObject item = NewUiObject("FoodItemTemplate", parent);
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(64f, 64f);
        LayoutElement layout = item.AddComponent<LayoutElement>();
        layout.preferredWidth = 64f;
        layout.preferredHeight = 64f;
        Image background = item.AddComponent<Image>();
        background.color = new Color(0.18f, 0.14f, 0.10f, 0.9f);
        item.AddComponent<HomePetFoodBarItemView>();

        GameObject iconObject = NewUiObject("Icon", item.transform);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.12f, 0.12f);
        iconRect.anchorMax = new Vector2(0.88f, 0.88f);
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;
        Image icon = iconObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        GameObject countObject = NewUiObject("Count", item.transform);
        RectTransform countRect = countObject.GetComponent<RectTransform>();
        countRect.anchorMin = new Vector2(0.45f, 0f);
        countRect.anchorMax = new Vector2(1f, 0.4f);
        countRect.offsetMin = Vector2.zero;
        countRect.offsetMax = new Vector2(-5f, 2f);
        Text count = countObject.AddComponent<Text>();
        count.font = ResolveFont();
        count.fontSize = 18;
        count.alignment = TextAnchor.LowerRight;
        count.color = Color.white;
        count.raycastTarget = false;
        Outline outline = countObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(1f, -1f);
        item.SetActive(false);
        return item;
    }

    private static GameObject BuildEmptySlotTemplate(RectTransform parent)
    {
        GameObject item = NewUiObject("EmptySlotTemplate", parent);
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(64f, 64f);
        LayoutElement layout = item.AddComponent<LayoutElement>();
        layout.preferredWidth = 64f;
        layout.preferredHeight = 64f;
        Image background = item.AddComponent<Image>();
        background.color = new Color(0.18f, 0.14f, 0.10f, 0.72f);
        background.raycastTarget = false;
        item.SetActive(false);
        return item;
    }

    private static GameObject NewUiObject(string name, Transform parent)
    {
        GameObject value = new GameObject(name, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        return value;
    }

    private static void Center(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void Stretch(
        RectTransform rect,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static Font ResolveFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return font != null
            ? font
            : Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
#endif
