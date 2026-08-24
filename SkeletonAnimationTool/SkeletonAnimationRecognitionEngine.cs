#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Game.EditorTools.SkeletonAnimation
{
    public interface ISkeletonRecognitionEngine
    {
        SkeletonRecognitionResult RecognizeTemplateDraft(SkeletonRecognitionInput input);
    }

    public sealed class HeuristicSkeletonRecognitionEngine : ISkeletonRecognitionEngine
    {
        public SkeletonRecognitionResult RecognizeTemplateDraft(SkeletonRecognitionInput input)
        {
            SkeletonRecognitionResult result = new SkeletonRecognitionResult();
            SkeletonTemplateDocument document = new SkeletonTemplateDocument
            {
                templateId = string.IsNullOrWhiteSpace(input.SourceAssetPath)
                    ? "humanoid_robot_template"
                    : MakeSafeId(System.IO.Path.GetFileNameWithoutExtension(input.SourceAssetPath)),
                displayName = string.IsNullOrWhiteSpace(input.SourceAssetPath)
                    ? "Humanoid Robot Template"
                    : System.IO.Path.GetFileNameWithoutExtension(input.SourceAssetPath),
                category = string.IsNullOrWhiteSpace(input.PreferredCategory) ? "Humanoid" : input.PreferredCategory,
                sourceAssetPath = input.SourceAssetPath ?? string.Empty,
                notes = "由启发式识别生成的骨骼草稿；请人工校正后保存为正式模板。",
            };

            AddHumanoidDraft(document);
            bool usedImageBounds = false;
            if (input.SourceImage != null && TryEstimateSubjectBounds(input.SourceImage, out Rect bounds))
            {
                FitSkeletonToBounds(
                    document,
                    bounds,
                    Mathf.Clamp(input.BodyFitWidthScale, 0.35f, 1f),
                    Mathf.Clamp(input.BodyFitHeightScale, 0.55f, 1f),
                    input.BodyFitOffsetX,
                    input.BodyFitOffsetY);
                usedImageBounds = true;
                result.Warnings.Add(
                    $"已按图片主体范围生成骨架草稿：x={bounds.xMin:0.00}-{bounds.xMax:0.00}, y={bounds.yMin:0.00}-{bounds.yMax:0.00}。");
            }

            AddDefaultSockets(document);
            if (input.IsFrontView)
            {
                SwapLeftRightSemantics(document);
            }

            if (input.SourceImage == null)
            {
                result.Warnings.Add("未提供参考图，已按默认人形机器人比例生成草稿骨架。");
            }
            else
            {
                result.Warnings.Add(
                    usedImageBounds
                        ? "当前识别引擎为启发式草稿：已尝试用主体范围贴近角色，但不会真正读取图片语义。"
                        : "当前识别引擎为启发式草稿：图片背景疑似干扰主体估计，已退回默认人形比例，请用整体微调对齐。");
            }

            result.Template = document;
            return result;
        }

        private static void AddHumanoidDraft(SkeletonTemplateDocument document)
        {
            document.bones.Clear();

            AddBone(document, "root", "Root", string.Empty, 0.50f, 0.56f, 0.08f, 90f);
            AddBone(document, "pelvis", "Pelvis", "root", 0.50f, 0.58f, 0.08f, 90f);
            AddBone(document, "spine", "Spine", "pelvis", 0.50f, 0.42f, 0.16f, 90f);
            AddBone(document, "chest", "Chest", "spine", 0.50f, 0.30f, 0.12f, 90f);
            AddBone(document, "neck", "Neck", "chest", 0.50f, 0.22f, 0.05f, 90f);
            AddBone(document, "head", "Head", "neck", 0.50f, 0.14f, 0.10f, 90f);

            AddBone(document, "left_upper_arm", "Left Upper Arm", "chest", 0.38f, 0.33f, 0.13f, -145f);
            AddBone(document, "left_lower_arm", "Left Lower Arm", "left_upper_arm", 0.31f, 0.47f, 0.14f, -105f);
            AddBone(document, "left_hand", "Left Hand", "left_lower_arm", 0.29f, 0.61f, 0.06f, -95f);

            AddBone(document, "right_upper_arm", "Right Upper Arm", "chest", 0.62f, 0.33f, 0.13f, -35f);
            AddBone(document, "right_lower_arm", "Right Lower Arm", "right_upper_arm", 0.69f, 0.47f, 0.14f, -75f);
            AddBone(document, "right_hand", "Right Hand", "right_lower_arm", 0.71f, 0.61f, 0.06f, -85f);

            AddBone(document, "left_upper_leg", "Left Upper Leg", "pelvis", 0.43f, 0.70f, 0.17f, -105f);
            AddBone(document, "left_lower_leg", "Left Lower Leg", "left_upper_leg", 0.39f, 0.86f, 0.16f, -95f);
            AddBone(document, "left_foot", "Left Foot", "left_lower_leg", 0.37f, 0.95f, 0.08f, 0f);

            AddBone(document, "right_upper_leg", "Right Upper Leg", "pelvis", 0.57f, 0.70f, 0.17f, -75f);
            AddBone(document, "right_lower_leg", "Right Lower Leg", "right_upper_leg", 0.61f, 0.86f, 0.16f, -85f);
            AddBone(document, "right_foot", "Right Foot", "right_lower_leg", 0.63f, 0.95f, 0.08f, 0f);
        }

        private static void AddDefaultSockets(SkeletonTemplateDocument document)
        {
            document.sockets.Clear();
            AddSocket(document, "head_equipment", "Head Equipment", "head", 0f, -0.04f, "Equipment");
            AddSocket(document, "chest_equipment", "Chest Equipment", "chest", 0f, 0f, "Equipment");
            AddSocket(document, "left_hand_weapon", "Left Hand Weapon", "left_hand", -0.02f, 0.02f, "Weapon");
            AddSocket(document, "right_hand_weapon", "Right Hand Weapon", "right_hand", 0.02f, 0.02f, "Weapon");
            AddSocket(document, "back_equipment", "Back Equipment", "chest", 0f, 0.04f, "Equipment");
            AddSocket(document, "skill_fx", "Skill FX", "root", 0f, -0.08f, "Effect");
        }

        private static void SwapLeftRightSemantics(SkeletonTemplateDocument document)
        {
            if (document == null)
            {
                return;
            }

            if (document.bones != null)
            {
                for (int i = 0; i < document.bones.Count; i++)
                {
                    SkeletonBoneData bone = document.bones[i];
                    bone.boneId = SwapLeftRightToken(bone.boneId);
                    bone.parentBoneId = SwapLeftRightToken(bone.parentBoneId);
                    bone.displayName = SwapLeftRightToken(bone.displayName);
                }
            }

            if (document.sockets != null)
            {
                for (int i = 0; i < document.sockets.Count; i++)
                {
                    SkeletonSocketData socket = document.sockets[i];
                    socket.socketId = SwapLeftRightToken(socket.socketId);
                    socket.parentBoneId = SwapLeftRightToken(socket.parentBoneId);
                    socket.displayName = SwapLeftRightToken(socket.displayName);
                }
            }
        }

        private static string SwapLeftRightToken(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value
                .Replace("Left", "__TRYGAME_SIDE_TMP__")
                .Replace("Right", "Left")
                .Replace("__TRYGAME_SIDE_TMP__", "Right")
                .Replace("left", "__trygame_side_tmp__")
                .Replace("right", "left")
                .Replace("__trygame_side_tmp__", "right");
        }

        private static bool TryEstimateSubjectBounds(Texture2D image, out Rect bounds)
        {
            bounds = default;
            if (image == null)
            {
                return false;
            }

            const int sampleWidth = 96;
            const int sampleHeight = 96;
            const float edgeMargin = 0.08f;
            Color background = EstimateBackgroundColor(image);
            int minX = sampleWidth;
            int maxX = -1;
            int minY = sampleHeight;
            int maxY = -1;

            for (int y = 0; y < sampleHeight; y++)
            {
                for (int x = 0; x < sampleWidth; x++)
                {
                    float u = (x + 0.5f) / sampleWidth;
                    float v = (y + 0.5f) / sampleHeight;
                    Color color = image.GetPixelBilinear(u, v);
                    float backgroundDistance =
                        Mathf.Abs(color.r - background.r)
                        + Mathf.Abs(color.g - background.g)
                        + Mathf.Abs(color.b - background.b);
                    float saturation =
                        Mathf.Max(color.r, color.g, color.b)
                        - Mathf.Min(color.r, color.g, color.b);
                    float value = Mathf.Max(color.r, color.g, color.b);
                    bool likelySubject =
                        backgroundDistance > 0.22f
                        && value > 0.08f
                        && (saturation > 0.05f || backgroundDistance > 0.35f);
                    bool nearEdge =
                        u < edgeMargin
                        || u > 1f - edgeMargin
                        || v < edgeMargin
                        || v > 1f - edgeMargin;
                    if (!likelySubject || nearEdge)
                    {
                        continue;
                    }

                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            if (maxX <= minX || maxY <= minY)
            {
                return false;
            }

            float xMin = minX / (float)sampleWidth;
            float xMax = (maxX + 1f) / sampleWidth;
            float yMin = 1f - (maxY + 1f) / sampleHeight;
            float yMax = 1f - minY / (float)sampleHeight;
            float width = xMax - xMin;
            float height = yMax - yMin;
            if (width < 0.12f || height < 0.20f)
            {
                return false;
            }

            if (width > 0.46f || height > 0.92f)
            {
                return false;
            }

            float aspect = width / Mathf.Max(0.01f, height);
            if (aspect < 0.18f || aspect > 0.62f)
            {
                return false;
            }

            const float paddingX = 0.04f;
            const float paddingY = 0.04f;
            xMin = Mathf.Clamp01(xMin - paddingX);
            xMax = Mathf.Clamp01(xMax + paddingX);
            yMin = Mathf.Clamp01(yMin - paddingY);
            yMax = Mathf.Clamp01(yMax + paddingY);
            bounds = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return true;
        }

        private static Color EstimateBackgroundColor(Texture2D image)
        {
            Color a = image.GetPixelBilinear(0.02f, 0.02f);
            Color b = image.GetPixelBilinear(0.98f, 0.02f);
            Color c = image.GetPixelBilinear(0.02f, 0.98f);
            Color d = image.GetPixelBilinear(0.98f, 0.98f);
            return (a + b + c + d) * 0.25f;
        }

        private static void FitSkeletonToBounds(
            SkeletonTemplateDocument document,
            Rect bounds,
            float widthScale,
            float heightScale,
            float offsetX,
            float offsetY)
        {
            if (document == null || document.bones == null || document.bones.Count == 0)
            {
                return;
            }

            const float sourceMinX = 0.28f;
            const float sourceMaxX = 0.72f;
            const float sourceMinY = 0.12f;
            const float sourceMaxY = 0.95f;

            Vector2 center = bounds.center + new Vector2(offsetX, offsetY);
            float targetWidth = bounds.width * widthScale;
            float targetHeight = bounds.height * heightScale;
            float targetMinX = center.x - targetWidth * 0.5f;
            float targetMaxX = center.x + targetWidth * 0.5f;
            float targetMinY = center.y - targetHeight * 0.5f;
            float targetMaxY = center.y + targetHeight * 0.5f;
            if (targetMaxX <= targetMinX || targetMaxY <= targetMinY)
            {
                return;
            }

            for (int i = 0; i < document.bones.Count; i++)
            {
                Vector2 position = document.bones[i].normalizedPosition;
                float xRatio = Mathf.InverseLerp(sourceMinX, sourceMaxX, position.x);
                float yRatio = Mathf.InverseLerp(sourceMinY, sourceMaxY, position.y);
                document.bones[i].normalizedPosition = new Vector2(
                    Mathf.Lerp(targetMinX, targetMaxX, xRatio),
                    Mathf.Lerp(targetMinY, targetMaxY, yRatio));
            }
        }

        private static void AddBone(
            SkeletonTemplateDocument document,
            string boneId,
            string displayName,
            string parentBoneId,
            float x,
            float y,
            float length,
            float rotationDegrees)
        {
            document.bones.Add(new SkeletonBoneData
            {
                boneId = boneId,
                displayName = displayName,
                parentBoneId = parentBoneId,
                normalizedPosition = new Vector2(x, y),
                length = length,
                rotationDegrees = rotationDegrees,
                confidence = 0.75f,
            });
        }

        private static void AddSocket(
            SkeletonTemplateDocument document,
            string socketId,
            string displayName,
            string parentBoneId,
            float offsetX,
            float offsetY,
            string socketType)
        {
            document.sockets.Add(new SkeletonSocketData
            {
                socketId = socketId,
                displayName = displayName,
                parentBoneId = parentBoneId,
                normalizedOffset = new Vector2(offsetX, offsetY),
                socketType = socketType,
                locked = true,
            });
        }

        private static string MakeSafeId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "skeleton_template";
            }

            char[] chars = raw.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars).Trim('_');
        }
    }
}
#endif
