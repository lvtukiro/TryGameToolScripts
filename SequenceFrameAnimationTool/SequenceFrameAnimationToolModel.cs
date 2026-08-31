#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Game.EditorTools.SequenceFrameAnimation
{
    [Serializable]
    public sealed class SequenceFrameAnimationDocument
    {
        public string animationId = "new_sequence_animation";
        public string displayName = "New Sequence Animation";
        public int actionId;
        public float frameRate = 12f;
        public bool loop = true;
        // 序列帧原图默认面向左；生成 Clip 时会一并写入，后续可在动作页切换。
        public bool defaultFacingLeft = true;
        public int canvasWidth;
        public int canvasHeight;
        public UnityEngine.Vector2 pivotNormalized = new UnityEngine.Vector2(0.5f, 0f);
        public bool removeBackground;
        public UnityEngine.Color backgroundKeyColor = UnityEngine.Color.white;
        public float backgroundTolerance = 0.08f;
        // 每一帧都是人物与当前武器合成后的完整画面，不再拆分身体/武器图层。
        public List<SequenceFrameData> frames = new List<SequenceFrameData>();
    }

    [Serializable]
    public sealed class SequenceFrameData
    {
        public int sourceFrameIndex;
        public string sourceFilePath = string.Empty;
        public string exportedAssetPath = string.Empty;
        public float differenceScore;
        public bool selected = true;
    }

}
#endif
