using Game;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TryGameTools.Editor
{
    /// <summary>
    /// 一次性修复 HomeMain 1.3 测试入口引用。
    /// 用 Unity Prefab API 创建并绑定节点，避免手工编辑 prefab YAML。
    /// </summary>
    public static class HomeMainPrefabRepairTool
    {
        private const string PrefabPath = "Assets/TryGameBuildRes/gui/ui_game/win_home_main.prefab";
        private const string RepairFlagPath = "Temp/RepairHomeMain13References.flag";

        [InitializeOnLoadMethod]
        private static void RunRequestedRepairAfterCompile()
        {
            if (!File.Exists(RepairFlagPath))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(RepairFlagPath))
                {
                    return;
                }

                RepairHomeMain13References();
                File.Delete(RepairFlagPath);
            };
        }

        [MenuItem("TryGame/HomeMain/Repair 1.3 References")]
        public static void RepairHomeMain13ReferencesFromMenu()
        {
            RepairHomeMain13References();
        }

        public static void RepairHomeMain13References()
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                GUIMonoHomeMain mono = prefabRoot.GetComponent<GUIMonoHomeMain>();
                if (mono == null)
                {
                    Debug.LogError("[HomeMainPrefabRepairTool] win_home_main.prefab 缺少 GUIMonoHomeMain。");
                    return;
                }

                RectTransform rootRect = prefabRoot.GetComponent<RectTransform>();
                if (rootRect == null)
                {
                    Debug.LogError("[HomeMainPrefabRepairTool] win_home_main.prefab 根节点缺少 RectTransform。");
                    return;
                }

                mono.travelButton = EnsureButton(rootRect, "TravelButton", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(96f, -96f), new Vector2(140f, 42f));
                mono.travelButtonText = EnsureButtonText(mono.travelButton, "TravelButtonText", "#UI_HomeMain_GoEntrance");

                mono.homeEntranceRoot = EnsureRect(rootRect, "HomeEntranceRoot", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
                mono.homeEntranceRoot.SetActive(false);

                RectTransform entranceRect = mono.homeEntranceRoot.GetComponent<RectTransform>();
                mono.merchantButton = EnsureButton(entranceRect, "MerchantButton", new Vector2(0.68f, 0.32f), new Vector2(0.68f, 0.32f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96f, 128f));
                mono.merchantImage = mono.merchantButton.GetComponent<Image>();

                mono.merchantDialogRoot = EnsureRect(entranceRect, "MerchantDialogRoot", new Vector2(0.72f, 0.42f), new Vector2(0.72f, 0.42f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(180f, 100f)).gameObject;
                EnsurePanelImage(mono.merchantDialogRoot, new Color(0f, 0f, 0f, 0.55f));
                mono.merchantDialogRoot.SetActive(false);

                RectTransform dialogRect = mono.merchantDialogRoot.GetComponent<RectTransform>();
                mono.merchantShopButton = EnsureButton(dialogRect, "MerchantShopButton", new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(140f, 34f));
                mono.merchantShopButtonText = EnsureButtonText(mono.merchantShopButton, "MerchantShopButtonText", "#UI_HomeShop_OpenShop");
                mono.merchantCancelButton = EnsureButton(dialogRect, "MerchantCancelButton", new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(140f, 34f));
                mono.merchantCancelButtonText = EnsureButtonText(mono.merchantCancelButton, "MerchantCancelButtonText", "#UI_Common_Cancel");

                mono.shopPanelRoot = EnsureRect(rootRect, "HomeShopPanelRoot", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 420f)).gameObject;
                EnsurePanelImage(mono.shopPanelRoot, new Color(0f, 0f, 0f, 0.72f));
                mono.shopPanelRoot.SetActive(false);

                RectTransform shopRect = mono.shopPanelRoot.GetComponent<RectTransform>();
                mono.shopTitleText = EnsureText(shopRect, "ShopTitleText", "#UI_HomeShop_Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(240f, 36f), 24, TextAnchor.MiddleCenter);
                mono.shopCloseButton = EnsureButton(shopRect, "ShopCloseButton", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(42f, 42f));
                EnsureButtonText(mono.shopCloseButton, "ShopCloseButtonText", "X");

                mono.shopItemViews = new HomeShopItemView[4];
                for (int i = 0; i < mono.shopItemViews.Length; i++)
                {
                    mono.shopItemViews[i] = EnsureShopItemView(shopRect, i);
                }

                EditorUtility.SetDirty(mono);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log("[HomeMainPrefabRepairTool] HomeMain 1.3 引用修复完成。");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static HomeShopItemView EnsureShopItemView(RectTransform parent, int index)
        {
            RectTransform row = EnsureRect(parent, "ShopItemRow" + index, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -82f - index * 72f), new Vector2(540f, 58f));
            EnsurePanelImage(row.gameObject, new Color(1f, 1f, 1f, 0.12f));

            HomeShopItemView view = new HomeShopItemView();
            view.root = row.gameObject;
            view.iconImage = EnsureImage(row, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(46f, 46f));
            view.nameText = EnsureText(row, "NameText", string.Empty, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(78f, 0f), new Vector2(190f, 46f), 16, TextAnchor.MiddleLeft);
            view.priceText = EnsureText(row, "PriceText", string.Empty, new Vector2(0.58f, 0.5f), new Vector2(0.58f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 40f), 16, TextAnchor.MiddleCenter);
            view.stockText = EnsureText(row, "StockText", string.Empty, new Vector2(0.74f, 0.5f), new Vector2(0.74f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 40f), 16, TextAnchor.MiddleCenter);
            view.stateText = EnsureText(row, "StateText", string.Empty, new Vector2(0.9f, 0.5f), new Vector2(0.9f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90f, 40f), 16, TextAnchor.MiddleCenter);
            return view;
        }

        private static Button EnsureButton(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = EnsureRect(parent, name, anchorMin, anchorMax, pivot, anchoredPosition, size);
            Image image = rect.GetComponent<Image>();
            if (image == null)
            {
                image = rect.gameObject.AddComponent<Image>();
            }

            image.color = new Color(1f, 1f, 1f, 0.78f);
            Button button = rect.GetComponent<Button>();
            if (button == null)
            {
                button = rect.gameObject.AddComponent<Button>();
            }

            button.targetGraphic = image;
            return button;
        }

        private static Text EnsureButtonText(Button button, string name, string text)
        {
            RectTransform parent = button.transform as RectTransform;
            return EnsureText(parent, name, text, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 18, TextAnchor.MiddleCenter);
        }

        private static Text EnsureText(RectTransform parent, string name, string text, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size, int fontSize, TextAnchor alignment)
        {
            RectTransform rect = EnsureRect(parent, name, anchorMin, anchorMax, pivot, anchoredPosition, size);
            Text label = rect.GetComponent<Text>();
            if (label == null)
            {
                label = rect.gameObject.AddComponent<Text>();
            }

            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private static Image EnsureImage(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = EnsureRect(parent, name, anchorMin, anchorMax, pivot, anchoredPosition, size);
            Image image = rect.GetComponent<Image>();
            if (image == null)
            {
                image = rect.gameObject.AddComponent<Image>();
            }

            image.color = Color.white;
            image.preserveAspect = true;
            return image;
        }

        private static void EnsurePanelImage(GameObject target, Color color)
        {
            Image image = target.GetComponent<Image>();
            if (image == null)
            {
                image = target.AddComponent<Image>();
            }

            image.color = color;
        }

        private static RectTransform EnsureRect(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            Transform child = parent.Find(name);
            RectTransform rect;
            if (child == null)
            {
                GameObject go = new GameObject(name, typeof(RectTransform));
                rect = go.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
            }
            else
            {
                rect = child as RectTransform;
                if (rect == null)
                {
                    Debug.LogError("[HomeMainPrefabRepairTool] 节点不是 RectTransform：" + name);
                    return null;
                }
            }

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            return rect;
        }
    }
}
