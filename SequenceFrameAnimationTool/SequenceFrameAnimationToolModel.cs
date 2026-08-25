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
        public float frameRate = 12f;
        public bool loop = true;
        public int canvasWidth;
        public int canvasHeight;
        public List<SequenceFrameData> bodyFrames = new List<SequenceFrameData>();
        public List<SequenceFrameLayerData> layers = new List<SequenceFrameLayerData>();
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

    [Serializable]
    public sealed class SequenceFrameLayerData
    {
        public string layerId = "weapon";
        public string displayName = "Weapon";
        public string layerType = "Weapon";
        public bool enabled = true;
        public int sortingOrder = 10;
        public List<string> frameAssetPaths = new List<string>();
    }
}
#endif
