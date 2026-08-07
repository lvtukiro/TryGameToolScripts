#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// 2.0b 备战间 Prefab Builder 共用的 Editor UI 工厂。
    /// 这里只负责生成和绑定序列化对象，不包含任何运行时业务逻辑。
    /// </summary>
    internal static class BattlePreparationEditorUiFactory
    {
        internal static readonly Color OverlayColor = new Color(0.025f, 0.035f, 0.055f, 0.82f);
        internal static readonly Color PanelColor = new Color(0.075f, 0.095f, 0.14f, 0.97f);
        internal static readonly Color PanelLightColor = new Color(0.12f, 0.15f, 0.21f, 0.97f);
        internal static readonly Color CellColor = new Color(0.10f, 0.13f, 0.18f, 0.98f);
        internal static readonly Color AccentColor = new Color(0.19f, 0.70f, 0.91f, 1f);
        internal static readonly Color AccentMutedColor = new Color(0.12f, 0.38f, 0.52f, 1f);
        internal static readonly Color WarningColor = new Color(0.93f, 0.36f, 0.30f, 1f);
        internal static readonly Color TextColor = new Color(0.94f, 0.97f, 1f, 1f);
        internal static readonly Color SubtleTextColor = new Color(0.68f, 0.75f, 0.84f, 1f);

        internal readonly struct ButtonParts
        {
            internal ButtonParts(GameObject gameObject, Button button, Image image, Text text)
            {
                GameObject = gameObject;
                Button = button;
                Image = image;
                Text = text;
            }

            internal GameObject GameObject { get; }
            internal Button Button { get; }
            internal Image Image { get; }
            internal Text Text { get; }
            internal RectTransform Rect => GameObject != null
                ? GameObject.GetComponent<RectTransform>()
                : null;
        }

        internal readonly struct ScrollParts
        {
            internal ScrollParts(ScrollRect scrollRect, RectTransform viewport, RectTransform content)
            {
                ScrollRect = scrollRect;
                Viewport = viewport;
                Content = content;
            }

            internal ScrollRect ScrollRect { get; }
            internal RectTransform Viewport { get; }
            internal RectTransform Content { get; }
        }

        internal static GameObject NewUiObject(string name, Transform parent)
        {
            GameObject value = new GameObject(name, typeof(RectTransform));
            value.layer = LayerMask.NameToLayer("UI");
            value.transform.SetParent(parent, false);
            return value;
        }

        internal static RectTransform NewRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            RectTransform rect = NewUiObject(name, parent).GetComponent<RectTransform>();
            SetRect(rect, anchorMin, anchorMax, offsetMin, offsetMax);
            return rect;
        }

        internal static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        internal static void Stretch(RectTransform rect, float margin = 0f)
        {
            SetRect(
                rect,
                Vector2.zero,
                Vector2.one,
                new Vector2(margin, margin),
                new Vector2(-margin, -margin));
        }

        internal static void Place(
            RectTransform rect,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 position,
            Vector2 size)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        internal static Image AddImage(
            GameObject target,
            Color color,
            Sprite sprite = null,
            bool raycastTarget = false,
            bool preserveAspect = false)
        {
            Image image = target.GetComponent<Image>();
            if (image == null)
            {
                image = target.AddComponent<Image>();
            }

            image.color = color;
            image.sprite = sprite;
            image.raycastTarget = raycastTarget;
            image.preserveAspect = preserveAspect;
            return image;
        }

        internal static Text AddText(
            GameObject target,
            string text,
            int fontSize,
            TextAnchor alignment = TextAnchor.MiddleCenter,
            Color? color = null,
            bool raycastTarget = false)
        {
            Text label = target.GetComponent<Text>();
            if (label == null)
            {
                label = target.AddComponent<Text>();
            }

            label.font = ResolveFont();
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color ?? TextColor;
            label.text = text ?? string.Empty;
            label.raycastTarget = raycastTarget;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        internal static Text AddTextChild(
            string name,
            Transform parent,
            string text,
            int fontSize,
            TextAnchor alignment = TextAnchor.MiddleCenter,
            Color? color = null,
            float margin = 0f)
        {
            GameObject child = NewUiObject(name, parent);
            Stretch(child.GetComponent<RectTransform>(), margin);
            return AddText(child, text, fontSize, alignment, color);
        }

        internal static ButtonParts AddButton(
            string name,
            Transform parent,
            string label,
            Color? color = null,
            int fontSize = 22,
            Sprite icon = null)
        {
            GameObject gameObject = NewUiObject(name, parent);
            Image background = AddImage(
                gameObject,
                color ?? AccentMutedColor,
                null,
                true);
            Button button = gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            Text text;
            if (icon == null)
            {
                text = AddTextChild("Text", gameObject.transform, label, fontSize);
            }
            else
            {
                GameObject iconObject = NewUiObject("Icon", gameObject.transform);
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                SetRect(
                    iconRect,
                    new Vector2(0f, 0.12f),
                    new Vector2(0.34f, 0.88f),
                    new Vector2(10f, 0f),
                    Vector2.zero);
                AddImage(iconObject, Color.white, icon, false, true);

                GameObject textObject = NewUiObject("Text", gameObject.transform);
                RectTransform textRect = textObject.GetComponent<RectTransform>();
                SetRect(
                    textRect,
                    new Vector2(0.34f, 0f),
                    Vector2.one,
                    Vector2.zero,
                    new Vector2(-8f, 0f));
                text = AddText(textObject, label, fontSize);
            }

            return new ButtonParts(gameObject, button, background, text);
        }

        internal static GameObject AddPanel(
            string name,
            Transform parent,
            Color? color = null,
            bool blocksRaycasts = true)
        {
            GameObject panel = NewUiObject(name, parent);
            AddImage(panel, color ?? PanelColor, null, blocksRaycasts);
            return panel;
        }

        internal static ScrollParts AddVerticalScroll(
            string name,
            Transform parent,
            float spacing,
            Vector4 padding,
            bool useGrid,
            Vector2 cellSize,
            int constraintCount)
        {
            GameObject scrollObject = NewUiObject(name, parent);
            AddImage(scrollObject, new Color(0f, 0f, 0f, 0.001f), null, true);

            GameObject viewportObject = NewUiObject("Viewport", scrollObject.transform);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            Stretch(viewport, 4f);
            AddImage(viewportObject, new Color(0f, 0f, 0f, 0.001f), null, false);
            viewportObject.AddComponent<RectMask2D>();

            GameObject contentObject = NewUiObject("Content", viewportObject.transform);
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            if (useGrid)
            {
                GridLayoutGroup layout = contentObject.AddComponent<GridLayoutGroup>();
                layout.padding = new RectOffset(
                    Mathf.RoundToInt(padding.x),
                    Mathf.RoundToInt(padding.y),
                    Mathf.RoundToInt(padding.z),
                    Mathf.RoundToInt(padding.w));
                layout.spacing = new Vector2(spacing, spacing);
                layout.cellSize = cellSize;
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                layout.constraintCount = Mathf.Max(1, constraintCount);
                layout.childAlignment = TextAnchor.UpperLeft;
            }
            else
            {
                VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(
                    Mathf.RoundToInt(padding.x),
                    Mathf.RoundToInt(padding.y),
                    Mathf.RoundToInt(padding.z),
                    Mathf.RoundToInt(padding.w));
                layout.spacing = spacing;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
            }

            ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            return new ScrollParts(scroll, viewport, content);
        }

        internal static Component AddRuntimeComponent(GameObject target, string fullTypeName)
        {
            Type type = ResolveRuntimeComponentType(fullTypeName);
            Component component = target.GetComponent(type);
            return component != null ? component : target.AddComponent(type);
        }

        internal static bool AreRuntimeTypesAvailable(IEnumerable<string> fullTypeNames)
        {
            if (fullTypeNames == null)
            {
                return false;
            }

            foreach (string fullTypeName in fullTypeNames)
            {
                if (TryResolveRuntimeComponentType(fullTypeName) == null)
                {
                    return false;
                }
            }

            return true;
        }

        internal static Type ResolveRuntimeComponentType(string fullTypeName)
        {
            Type type = TryResolveRuntimeComponentType(fullTypeName);
            if (type == null)
            {
                throw new InvalidOperationException(
                    $"Runtime UI component type is unavailable: {fullTypeName ?? "<null>"}");
            }

            return type;
        }

        internal static void SetObject(Component component, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = FindRequiredProperty(component, propertyName);
            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                throw new InvalidOperationException(
                    $"Serialized property is not an object reference: " +
                    $"type={component.GetType().FullName}, property={propertyName}, " +
                    $"actual={property.propertyType}");
            }

            property.objectReferenceValue = value;
            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetInt(Component component, string propertyName, int value)
        {
            SerializedProperty property = FindRequiredProperty(component, propertyName);
            if (property.propertyType != SerializedPropertyType.Integer
                && property.propertyType != SerializedPropertyType.Enum)
            {
                throw new InvalidOperationException(
                    $"Serialized property is not an integer/enum: " +
                    $"type={component.GetType().FullName}, property={propertyName}, " +
                    $"actual={property.propertyType}");
            }

            if (property.propertyType == SerializedPropertyType.Enum)
            {
                property.enumValueIndex = value;
            }
            else
            {
                property.intValue = value;
            }
            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetBool(Component component, string propertyName, bool value)
        {
            SerializedProperty property = FindRequiredProperty(component, propertyName);
            if (property.propertyType != SerializedPropertyType.Boolean)
            {
                throw new InvalidOperationException(
                    $"Serialized property is not a bool: " +
                    $"type={component.GetType().FullName}, property={propertyName}, " +
                    $"actual={property.propertyType}");
            }

            property.boolValue = value;
            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetSerializedRect(
            Component component,
            string propertyName,
            Rect value)
        {
            SerializedProperty property = FindRequiredProperty(component, propertyName);
            if (property.propertyType != SerializedPropertyType.Rect)
            {
                throw new InvalidOperationException(
                    $"Serialized property is not a Rect: " +
                    $"type={component.GetType().FullName}, property={propertyName}, " +
                    $"actual={property.propertyType}");
            }

            property.rectValue = value;
            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetObjects(
            Component component,
            string propertyName,
            IReadOnlyList<UnityEngine.Object> values)
        {
            SerializedProperty property = FindRequiredProperty(component, propertyName);
            if (!property.isArray)
            {
                throw new InvalidOperationException(
                    $"Serialized property is not an array/list: " +
                    $"type={component.GetType().FullName}, property={propertyName}");
            }

            int count = values?.Count ?? 0;
            property.arraySize = count;
            for (int index = 0; index < count; index++)
            {
                SerializedProperty item = property.GetArrayElementAtIndex(index);
                if (item.propertyType != SerializedPropertyType.ObjectReference)
                {
                    throw new InvalidOperationException(
                        $"Serialized collection item is not an object reference: " +
                        $"type={component.GetType().FullName}, property={propertyName}, " +
                        $"index={index}, actual={item.propertyType}");
                }

                item.objectReferenceValue = values[index];
            }

            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetInts(
            Component component,
            string propertyName,
            IReadOnlyList<int> values)
        {
            SerializedProperty property = FindRequiredProperty(component, propertyName);
            if (!property.isArray)
            {
                throw new InvalidOperationException(
                    $"Serialized property is not an array/list: " +
                    $"type={component.GetType().FullName}, property={propertyName}");
            }

            int count = values?.Count ?? 0;
            property.arraySize = count;
            for (int index = 0; index < count; index++)
            {
                SerializedProperty item = property.GetArrayElementAtIndex(index);
                if (item.propertyType == SerializedPropertyType.Enum)
                {
                    item.enumValueIndex = values[index];
                }
                else if (item.propertyType == SerializedPropertyType.Integer)
                {
                    item.intValue = values[index];
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Serialized collection item is not an integer/enum: " +
                        $"type={component.GetType().FullName}, property={propertyName}, " +
                        $"index={index}, actual={item.propertyType}");
                }
            }

            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static SerializedProperty FindRequiredProperty(
            UnityEngine.Object target,
            string propertyName)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
            {
                throw new ArgumentException(
                    $"Serialized property target/name is invalid: " +
                    $"target={target != null}, property={propertyName ?? "<null>"}");
            }

            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new MissingFieldException(target.GetType().FullName, propertyName);
            }

            return property;
        }

        internal static void AddBuilderMarker(GameObject root, string markerName)
        {
            if (root == null || string.IsNullOrEmpty(markerName))
            {
                return;
            }

            Transform existing = FindChildRecursive(root.transform, markerName);
            if (existing != null)
            {
                existing.gameObject.SetActive(false);
                return;
            }

            GameObject marker = new GameObject(markerName);
            marker.transform.SetParent(root.transform, false);
            marker.SetActive(false);
        }

        internal static bool ContainsBuilderMarker(GameObject root, string markerName)
        {
            return root != null
                && !string.IsNullOrEmpty(markerName)
                && FindChildRecursive(root.transform, markerName) != null;
        }

        internal static Transform FindChildRecursive(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform child = root.GetChild(index);
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child;
                }

                Transform nested = FindChildRecursive(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        internal static void DestroyChildIfPresent(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
            {
                return;
            }

            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                Transform child = parent.GetChild(index);
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        internal static void SavePrefab(GameObject root, string assetPath)
        {
            if (root == null || string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentException(
                    $"Cannot save prefab: root={root != null}, path={assetPath ?? "<null>"}");
            }

            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            EnsureAssetDirectory(directory);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            if (saved == null)
            {
                throw new InvalidOperationException($"Prefab save returned null: {assetPath}");
            }
        }

        internal static void EnsureAssetDirectory(string assetDirectory)
        {
            if (string.IsNullOrWhiteSpace(assetDirectory)
                || !assetDirectory.StartsWith("Assets", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Asset directory is invalid: {assetDirectory ?? "<null>"}");
            }

            string[] parts = assetDirectory.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(current, parts[index]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        throw new IOException($"Failed to create asset directory: {next}");
                    }
                }

                current = next;
            }
        }

        internal static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font != null
                ? font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        internal static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
            {
                return;
            }

            root.layer = layer;
            for (int index = 0; index < root.transform.childCount; index++)
            {
                SetLayerRecursively(root.transform.GetChild(index).gameObject, layer);
            }
        }

        private static Type TryResolveRuntimeComponentType(string fullTypeName)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
            {
                return null;
            }

            foreach (Type type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                if (string.Equals(type.FullName, fullTypeName, StringComparison.Ordinal))
                {
                    return type;
                }
            }

            return null;
        }
    }
}
#endif
