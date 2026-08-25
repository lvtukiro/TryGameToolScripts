using UnityEngine;

namespace Game.SequenceFrameAnimation
{
    /// <summary>
    /// Runtime player for a body sequence plus an optional weapon sequence.
    /// Assign sprites with the same frame count and canvas/pivot in the Inspector.
    /// </summary>
    public sealed class SequenceFrameAnimationPlayer : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private SpriteRenderer weaponRenderer;
        [SerializeField] private Sprite[] bodyFrames = new Sprite[0];
        [SerializeField] private Sprite[] weaponFrames = new Sprite[0];
        [SerializeField] private float frameRate = 12f;
        [SerializeField] private bool loop = true;
        [SerializeField] private bool playOnEnable = true;

        private float elapsed;
        private int frameIndex;
        private bool playing;

        public int FrameIndex => frameIndex;
        public int FrameCount => bodyFrames == null ? 0 : bodyFrames.Length;

        private void OnEnable()
        {
            playing = playOnEnable;
            frameIndex = 0;
            elapsed = 0f;
            ApplyFrame();
        }

        private void Update()
        {
            if (!playing || bodyFrames == null || bodyFrames.Length == 0)
            {
                return;
            }

            elapsed += Time.deltaTime;
            float interval = 1f / Mathf.Max(1f, frameRate);
            while (elapsed >= interval)
            {
                elapsed -= interval;
                frameIndex++;
                if (frameIndex >= bodyFrames.Length)
                {
                    if (!loop)
                    {
                        frameIndex = bodyFrames.Length - 1;
                        playing = false;
                        break;
                    }

                    frameIndex = 0;
                }

                ApplyFrame();
            }
        }

        public void Play()
        {
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
            if (bodyFrames == null || bodyFrames.Length == 0)
            {
                return;
            }

            frameIndex = Mathf.Clamp(index, 0, bodyFrames.Length - 1);
            elapsed = 0f;
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            if (bodyRenderer != null && bodyFrames != null && bodyFrames.Length > 0)
            {
                bodyRenderer.sprite = bodyFrames[Mathf.Clamp(frameIndex, 0, bodyFrames.Length - 1)];
            }

            if (weaponRenderer != null)
            {
                weaponRenderer.sprite = weaponFrames != null
                    && frameIndex >= 0
                    && frameIndex < weaponFrames.Length
                    ? weaponFrames[frameIndex]
                    : null;
            }
        }
    }
}
