#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.EditorTools.SkeletonAnimation
{
    [Serializable]
    public sealed class SkeletonTemplateDocument
    {
        public string templateId = "new_template";
        public string displayName = "New Skeleton Template";
        public string category = "Humanoid";
        public string sourceAssetPath = string.Empty;
        public string notes = string.Empty;
        public int version = 1;
        public List<SkeletonBoneData> bones = new List<SkeletonBoneData>();
        public List<SkeletonSocketData> sockets = new List<SkeletonSocketData>();
        public List<SkeletonBoneVisualData> visualParts = new List<SkeletonBoneVisualData>();
        public List<SkeletonViewTemplateData> viewTemplates = new List<SkeletonViewTemplateData>();
    }

    [Serializable]
    public sealed class SkeletonBoneVisualData
    {
        public string boneId = string.Empty;
        public string assetPath = string.Empty;
    }

    [Serializable]
    public sealed class SkeletonBoneData
    {
        public string boneId = string.Empty;
        public string displayName = string.Empty;
        public string parentBoneId = string.Empty;
        public Vector2 normalizedPosition = new Vector2(0.5f, 0.5f);
        public float length = 0.1f;
        public float rotationDegrees;
        public bool locked;
        public float confidence = 1f;
    }

    [Serializable]
    public sealed class SkeletonSocketData
    {
        public string socketId = string.Empty;
        public string displayName = string.Empty;
        public string parentBoneId = string.Empty;
        public Vector2 normalizedOffset;
        public float rotationDegrees;
        public string socketType = "Equipment";
        public bool locked = true;
    }

    [Serializable]
    public sealed class SkeletonViewTemplateData
    {
        public string viewId = "front";
        public string displayName = "Front";
        public string notes = string.Empty;
        public List<SkeletonBoneData> bones = new List<SkeletonBoneData>();
        public List<SkeletonSocketData> sockets = new List<SkeletonSocketData>();
    }

    [Serializable]
    public sealed class SkeletonAnimationDocument
    {
        public string animationId = "new_animation";
        public string displayName = "New Animation";
        public float frameRate = 30f;
        public string templateId = string.Empty;
        public List<SkeletonAnimationKeyframeData> keyframes = new List<SkeletonAnimationKeyframeData>();
        public List<SkeletonActionFrameSelectionData> frameSelections =
            new List<SkeletonActionFrameSelectionData>();
    }

    [Serializable]
    public sealed class SkeletonAnimationKeyframeData
    {
        public int frame;
        public List<SkeletonBonePoseData> bonePoses = new List<SkeletonBonePoseData>();
    }

    [Serializable]
    public sealed class SkeletonBonePoseData
    {
        public string boneId = string.Empty;
        public Vector2 normalizedPosition;
        public float rotationDegrees;
    }

    [Serializable]
    public sealed class SkeletonActionFrameSelectionData
    {
        public int frameIndex;
        public string sourceFilePath = string.Empty;
        public float differenceScore;
        public bool autoSelected;
        public bool selected;
        public bool manualOverride;
    }

    public sealed class SkeletonRecognitionInput
    {
        public Texture2D SourceImage;
        public string SourceAssetPath = string.Empty;
        public SkeletonTemplateDocument ExistingTemplate;
        public string PreferredCategory = "Humanoid";
        public float BodyFitWidthScale = 0.68f;
        public float BodyFitHeightScale = 0.96f;
        public float BodyFitOffsetX;
        public float BodyFitOffsetY;
        public bool IsFrontView;
    }

    public sealed class SkeletonRecognitionResult
    {
        public SkeletonTemplateDocument Template;
        public readonly List<string> Warnings = new List<string>();
    }
}
#endif
