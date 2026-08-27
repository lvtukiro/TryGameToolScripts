using UnityEngine;

namespace Game.SequenceFrameAnimation
{
    /// <summary>
    /// Runtime player for complete character frames.
    /// Each frame already contains the character and its current weapon.
    /// </summary>
    public sealed class SequenceFrameAnimationPlayer : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer frameRenderer;
        [SerializeField] private Sprite[] frames = new Sprite[0];
        [SerializeField] private float frameRate = 12f;
        [SerializeField] private bool loop = true;
        [SerializeField] private bool playOnEnable = true;

        private float elapsed;
        private int frameIndex;
        private bool playing;

        public int FrameIndex => frameIndex;
        public int FrameCount => frames == null ? 0 : frames.Length;

        private void OnEnable()
        {
            playing = playOnEnable;
            frameIndex = 0;
            elapsed = 0f;
            ApplyFrame();
        }

        private void Update()
        {
            if (!playing || frames == null || frames.Length == 0)
            {
                return;
            }

            elapsed += Time.deltaTime;
            float interval = 1f / Mathf.Max(1f, frameRate);
            while (elapsed >= interval)
            {
                elapsed -= interval;
                frameIndex++;
                if (frameIndex >= frames.Length)
                {
                    if (!loop)
                    {
                        // 非循环动作播完停在最后一帧，方便查看动作结束姿态。
                        // Play() 检测到已经停在末帧时会把播放位置重新置为 0，
                        // 因而可以直接再次点击播放重播，而不必在这里闪回首帧。
                        frameIndex = frames.Length - 1;
                        playing = false;
                        elapsed = 0f;
                        ApplyFrame();
                        break;
                    }

                    frameIndex = 0;
                }

                ApplyFrame();
            }
        }

        public void Play()
        {
            if (!loop && !playing && frames != null && frames.Length > 0
                && frameIndex >= frames.Length - 1)
            {
                frameIndex = 0;
                elapsed = 0f;
                ApplyFrame();
            }

            playing = true;
        }

        public void Pause()
        {
            playing = false;
        }

        public void Stop()
        {
            playing = false;
            frameIndex = 0;
            elapsed = 0f;
            ApplyFrame();
        }

        public void SetFrame(int index)
        {
            if (frames == null || frames.Length == 0)
            {
                return;
            }

            frameIndex = Mathf.Clamp(index, 0, frames.Length - 1);
            elapsed = 0f;
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            if (frameRenderer != null && frames != null && frames.Length > 0)
            {
                frameRenderer.sprite = frames[Mathf.Clamp(frameIndex, 0, frames.Length - 1)];
            }
        }
    }
}
