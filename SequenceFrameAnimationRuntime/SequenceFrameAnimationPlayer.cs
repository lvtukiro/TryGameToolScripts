using UnityEngine;

namespace Game.SequenceFrameAnimation
{
    /// <summary>
    /// 实现说明：该注释描述当前模块的边界条件和运行时处理。
    /// </summary>
    /// 实现说明：该注释描述当前模块的边界条件和运行时处理。
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
        /// <summary>
        /// FrameIndex：执行当前模块的FrameIndex逻辑。
        /// </summary>

        public int FrameIndex => frameIndex;
        /// <summary>
        /// FrameCount：执行当前模块的FrameCount逻辑。
        /// </summary>
        public int FrameCount => frames == null ? 0 : frames.Length;
        /// <summary>
        /// OnEnable：执行当前模块的OnEnable逻辑。
        /// </summary>

        private void OnEnable()
        {
            playing = playOnEnable;
            frameIndex = 0;
            elapsed = 0f;
            ApplyFrame();
        }
        /// <summary>
        /// Update：执行当前模块的Update逻辑。
        /// </summary>

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
        /// <summary>
        /// Play：执行当前模块的Play逻辑。
        /// </summary>

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
        /// <summary>
        /// Pause：执行当前模块的Pause逻辑。
        /// </summary>

        public void Pause()
        {
            playing = false;
        }
        /// <summary>
        /// Stop：执行当前模块的Stop逻辑。
        /// </summary>

        public void Stop()
        {
            playing = false;
            frameIndex = 0;
            elapsed = 0f;
            ApplyFrame();
        }
        /// <summary>
        /// SetFrame：执行当前模块的SetFrame逻辑。
        /// </summary>

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
        /// <summary>
        /// ApplyFrame：执行当前模块的ApplyFrame逻辑。
        /// </summary>

        private void ApplyFrame()
        {
            if (frameRenderer != null && frames != null && frames.Length > 0)
            {
                frameRenderer.sprite = frames[Mathf.Clamp(frameIndex, 0, frames.Length - 1)];
            }
        }
    }
}
