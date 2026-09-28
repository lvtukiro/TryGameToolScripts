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
            /// <summary>
            /// 创建 `ButtonParts`，记录`gameObject`（gameObject）、`button`（button）、`image`（image）和`text`（文本）字段，形成可供后续流程传递的数据对象。
            /// </summary>
            internal ButtonParts(GameObject gameObject, Button button, Image image, Text text)
            {
                GameObject = gameObject;
                Button = button;
                Image = image;
                Text = text;
            }
            /// <summary>
            /// 获取当前 `ButtonParts` 实例的游戏`Object`。
            /// </summary>

            internal GameObject GameObject { get; }
            /// <summary>
            /// 获取当前 `ButtonParts` 实例的按钮。
            /// </summary>
            internal Button Button { get; }
            /// <summary>
            /// 获取当前 `ButtonParts` 实例的图像。
            /// </summary>
            internal Image Image { get; }
            /// <summary>
            /// 获取当前 `ButtonParts` 实例的文本。
            /// </summary>
            internal Text Text { get; }
            /// <summary>
            /// 获取当前 `ButtonParts` 实例的矩形。
            /// </summary>
            internal RectTransform Rect => GameObject != null
                ? GameObject.GetComponent<RectTransform>()
                : null;
        }

        internal readonly struct ScrollParts
        {
            /// <summary>
            /// 创建 `ScrollParts`，记录`scrollRect`（scrollRect）、`viewport`（viewport）和`content`（内容）字段，形成可供后续流程传递的数据对象。
            /// </summary>
            internal ScrollParts(ScrollRect scrollRect, RectTransform viewport, RectTransform content)
            {
                ScrollRect = scrollRect;
                Viewport = viewport;
                Content = content;
            }
            /// <summary>
            /// 获取当前 `ScrollParts` 实例的`Scroll`矩形。
            /// </summary>

            internal ScrollRect ScrollRect { get; }
            /// <summary>
            /// 获取当前 `ScrollParts` 实例的`Viewport`。
            /// </summary>
            internal RectTransform Viewport { get; }
            /// <summary>
            /// 获取当前 `ScrollParts` 实例的`Content`。
            /// </summary>
            internal RectTransform Content { get; }
        }
        /// <summary>
        /// 创建并配置新文件界面对象，然后返回生成的界面对象。
        /// </summary>

        internal static GameObject NewUiObject(string name, Transform parent)
        {
            GameObject value = new GameObject(name, typeof(RectTransform));
            value.layer = LayerMask.NameToLayer("UI");
            value.transform.SetParent(parent, false);
            return value;
        }
        /// <summary>
        /// 创建并配置新文件矩形，然后返回生成的界面对象。
        /// </summary>

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
        /// <summary>
        /// 设置矩形。
        /// </summary>

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
        /// <summary>
        /// 更新`Stretch`。
        /// </summary>

        internal static void Stretch(RectTransform rect, float margin = 0f)
        {
            SetRect(
                rect,
                Vector2.zero,
                Vector2.one,
                new Vector2(margin, margin),
                new Vector2(-margin, -margin));
        }
        /// <summary>
        /// 更新放置。
        /// </summary>

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
        /// <summary>
        /// 添加图像。
        /// </summary>

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
        /// <summary>
        /// 添加文本。
        /// </summary>

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
        /// <summary>
        /// 添加文本子路径。
        /// </summary>

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
        /// <summary>
        /// 添加按钮。
        /// </summary>

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
        /// <summary>
        /// 添加面板。
        /// </summary>

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
        /// <summary>
        /// 添加垂直`Scroll`。
        /// </summary>

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
        /// <summary>
        /// 添加运行时组件。
        /// </summary>

        internal static Component AddRuntimeComponent(GameObject target, string fullTypeName)
        {
            Type type = ResolveRuntimeComponentType(fullTypeName);
            Component component = target.GetComponent(type);
            return component != null ? component : target.AddComponent(type);
        }
        /// <summary>
        /// 检查 `fullTypeNames` 是否全部满足“运行时类型`Available`”条件。
        /// </summary>

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
        /// <summary>
        /// 解析运行时组件类型。
        /// </summary>

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
        /// <summary>
        /// 设置对象。
        /// </summary>

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
        /// <summary>
        /// 设置整数。
        /// </summary>

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
        /// <summary>
        /// 设置`Bool`。
        /// </summary>

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
        /// <summary>
        /// 设置`Serialized`矩形。
        /// </summary>

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
        /// <summary>
        /// 设置`Objects`。
        /// </summary>

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
        /// <summary>
        /// 设置`Ints`。
        /// </summary>

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
        /// <summary>
        /// 查找必需`Property`。
        /// </summary>

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
        /// <summary>
        /// 添加构建器标记。
        /// </summary>

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
        /// <summary>
        /// 判断包含`Builder`标记是否满足当前条件。
        /// </summary>

        internal static bool ContainsBuilderMarker(GameObject root, string markerName)
        {
            return root != null
                && !string.IsNullOrEmpty(markerName)
                && FindChildRecursive(root.transform, markerName) != null;
        }
        /// <summary>
        /// 查找子路径`Recursive`。
        /// </summary>

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
        /// <summary>
        /// 销毁子路径如果存在。
        /// </summary>

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
        /// <summary>
        /// 保存预制体。
        /// </summary>

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
        /// <summary>
        /// 确保资源目录。
        /// </summary>

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
        /// <summary>
        /// 解析`Font`。
        /// </summary>

        internal static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font != null
                ? font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
        /// <summary>
        /// 设置层级递归。
        /// </summary>

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
        /// <summary>
        /// 根据 `fullTypeName` 解析运行时组件类型；操作完成时返回 `true`，前置条件不满足时返回 `false`。
        /// </summary>

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
