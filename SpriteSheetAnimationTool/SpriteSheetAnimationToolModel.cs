#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.EditorTools.SpriteSheetAnimation
{
    /// <summary>
    /// 帧图角色动画工具的编辑文档。
    ///
    /// 这个文档与视频拆帧工具的窗口独立，但导出的字段保持与
    /// SequenceFrameAnimationDocument 兼容，方便后续继续生成预制体和 Clip。
    /// </summary>
    [Serializable]
    public sealed class SpriteSheetAnimationDocument
    {
        public string animationId = "new_sprite_sheet_animation";
        public string displayName = "New Sprite Sheet Animation";
        public int actionId;
        public float frameRate = 12f;
        public bool loop = true;
        public bool defaultFacingLeft = true;
        public int canvasWidth;
        public int canvasHeight;
        // 标准化时人物内容的等比缩放百分比；输出画布尺寸与内容缩放分开配置。
        public float contentScalePercent = 100f;
        public float footBaselineNormalized;
        public Vector2 canvasCenterOffsetNormalized;
        public Vector2 pivotNormalized = new Vector2(0.5f, 0f);
        // 帧图角色通常需要透明背景，因此新文档默认启用纯色背景处理；
        // 已有透明通道的图片会自动保留 Alpha，不会重复抠图。
        public bool removeBackground = true;
        public Color backgroundKeyColor = Color.white;
        public float backgroundTolerance = 0.08f;
        public List<SpriteSheetFrameData> frames = new List<SpriteSheetFrameData>();
    }

    /// <summary>
    /// 网格切出的单帧。sourceFilePath 指向工具临时输出，
    /// exportedAssetPath 指向最终 Assets 内的 PNG。
    /// </summary>
    [Serializable]
    public sealed class SpriteSheetFrameData
    {
        public int sourceFrameIndex;
        public string sourceFilePath = string.Empty;
        public string exportedAssetPath = string.Empty;
        public bool selected = true;
    }
}
#endif
