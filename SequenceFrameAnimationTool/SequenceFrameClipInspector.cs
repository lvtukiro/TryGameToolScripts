#if UNITY_EDITOR
using System;
using Game.SequenceFrameAnimation;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools.SequenceFrameAnimation
{
    /// <summary>
    /// SequenceFrameClip 的资源预览。默认 ScriptableObject Inspector 只会列出 Sprite 引用，
    /// 这里补上动作信息和可播放的逐帧预览，方便确认 clip_动作ID_1 对应的动作。
    /// </summary>
    [CustomEditor(typeof(SequenceFrameClip))]
    public sealed class SequenceFrameClipInspector : Editor
    {
        private const float PreviewHeight = 250f;
        private bool playing;
        private double previousTime;
        private double playbackTime;
        private int previewFrame;

        private SequenceFrameClip Clip => target as SequenceFrameClip;

        private void OnEnable()
        {
            previousTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            playing = false;
        }

        private void OnEditorUpdate()
        {
            if (!playing || Clip == null || Clip.FrameCount <= 0 || Clip.FrameRate <= 0f)
            {
                previousTime = EditorApplication.timeSinceStartup;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double delta = Math.Max(0d, Math.Min(0.25d, now - previousTime));
            previousTime = now;
            int frameCount = Clip.FrameCount;
            playbackTime += delta;
            int next = (int)Math.Floor(playbackTime * Clip.FrameRate);
            if (next >= frameCount)
            {
                if (Clip.Loop)
                {
                    playbackTime %= frameCount / Clip.FrameRate;
                    next = (int)Math.Floor(playbackTime * Clip.FrameRate);
                }
                else
                {
                    next = frameCount - 1;
                    playbackTime = next / Clip.FrameRate;
                    playing = false;
                }
            }

            if (next != previewFrame)
            {
                previewFrame = next;
                Repaint();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();

            SequenceFrameClip clip = Clip;
            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("动作预览", EditorStyles.boldLabel);
                if (clip == null)
                {
                    EditorGUILayout.HelpBox("未选中 SequenceFrameClip。", MessageType.Info);
                    return;
                }

                EditorGUILayout.LabelField("Action ID", clip.ActionId.ToString(), EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    "资源",
                    clip.name + " · " + clip.FrameCount + " 帧 · " +
                    clip.FrameRate.ToString("0.###") + " FPS",
                    EditorStyles.miniLabel);

                if (!clip.TryValidate(out string validationError))
                    EditorGUILayout.HelpBox(validationError, MessageType.Warning);

                DrawPreview(clip);
                DrawControls(clip);
            }
        }

        private void DrawPreview(SequenceFrameClip clip)
        {
            Rect viewport = GUILayoutUtility.GetRect(0f, PreviewHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(viewport, new Color(0.12f, 0.12f, 0.12f, 1f));

            Sprite sprite = clip.GetFrame(Mathf.Clamp(previewFrame, 0, Math.Max(0, clip.FrameCount - 1)));
            if (sprite == null)
            {
                EditorGUI.LabelField(viewport, "没有可预览的帧", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            float canvasWidth = clip.CanvasWidth > 0 ? clip.CanvasWidth : sprite.rect.width;
            float canvasHeight = clip.CanvasHeight > 0 ? clip.CanvasHeight : sprite.rect.height;
            float scale = Mathf.Min(
                (viewport.width - 18f) / canvasWidth,
                (viewport.height - 18f) / canvasHeight);
            scale = Mathf.Max(0.01f, scale);
            Vector2 canvasSize = new Vector2(canvasWidth, canvasHeight) * scale;
            Rect canvas = new Rect(
                viewport.center.x - canvasSize.x * 0.5f,
                viewport.center.y - canvasSize.y * 0.5f,
                canvasSize.x,
                canvasSize.y);
            DrawChecker(canvas, 8f);

            Rect source = sprite.textureRect;
            Rect uv = new Rect(
                source.x / sprite.texture.width,
                source.y / sprite.texture.height,
                source.width / sprite.texture.width,
                source.height / sprite.texture.height);
            float pivotX = clip.PivotNormalized.x * canvasWidth;
            float pivotY = clip.PivotNormalized.y * canvasHeight;
            Rect spriteRect = new Rect(
                canvas.x + (pivotX - sprite.pivot.x) * scale,
                canvas.y + (canvasHeight - pivotY - (sprite.rect.height - sprite.pivot.y)) * scale,
                sprite.rect.width * scale,
                sprite.rect.height * scale);
            GUI.DrawTextureWithTexCoords(spriteRect, sprite.texture, uv, true);

            Handles.color = new Color(0.25f, 0.85f, 0.45f, 0.9f);
            float footY = canvas.y + (1f - clip.PivotNormalized.y) * canvas.height;
            Handles.DrawLine(new Vector3(canvas.x, footY), new Vector3(canvas.xMax, footY));
            Handles.color = new Color(1f, 0.75f, 0.2f, 0.9f);
            float centerX = canvas.x + clip.PivotNormalized.x * canvas.width;
            Handles.DrawLine(new Vector3(centerX, canvas.y), new Vector3(centerX, canvas.yMax));
            GUI.Label(
                new Rect(viewport.x + 6f, viewport.y + 5f, viewport.width - 12f, 18f),
                "动作 " + clip.ActionId + "  ·  帧 " + (previewFrame + 1) + "/" + clip.FrameCount,
                EditorStyles.whiteMiniLabel);
        }

        private static void DrawChecker(Rect rect, float cell)
        {
            Color a = new Color(0.18f, 0.18f, 0.18f, 1f);
            Color b = new Color(0.22f, 0.22f, 0.22f, 1f);
            for (float y = rect.y; y < rect.yMax; y += cell)
            for (float x = rect.x; x < rect.xMax; x += cell)
            {
                Rect tile = new Rect(
                    x,
                    y,
                    Mathf.Min(cell, rect.xMax - x),
                    Mathf.Min(cell, rect.yMax - y));
                EditorGUI.DrawRect(
                    tile,
                    (((int)((x - rect.x) / cell) + (int)((y - rect.y) / cell)) & 1) == 0
                        ? a : b);
            }
        }

        private void DrawControls(SequenceFrameClip clip)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀", GUILayout.Width(34f)))
                {
                    previewFrame = clip.FrameCount == 0
                        ? 0 : (previewFrame - 1 + clip.FrameCount) % clip.FrameCount;
                    playbackTime = previewFrame / Math.Max(0.0001f, clip.FrameRate);
                    playing = false;
                }
                if (GUILayout.Button(playing ? "暂停" : "播放", GUILayout.Width(58f)))
                {
                    playing = !playing;
                    playbackTime = previewFrame / Math.Max(0.0001f, clip.FrameRate);
                    previousTime = EditorApplication.timeSinceStartup;
                }
                if (GUILayout.Button("▶", GUILayout.Width(34f)))
                {
                    previewFrame = clip.FrameCount == 0
                        ? 0 : (previewFrame + 1) % clip.FrameCount;
                    playbackTime = previewFrame / Math.Max(0.0001f, clip.FrameRate);
                    playing = false;
                }
                EditorGUILayout.LabelField("帧", GUILayout.Width(24f));
                int selected = EditorGUILayout.IntSlider(
                    previewFrame + 1, 1, Mathf.Max(1, clip.FrameCount));
                if (selected != previewFrame + 1)
                {
                    previewFrame = Mathf.Clamp(
                        selected - 1, 0, Mathf.Max(0, clip.FrameCount - 1));
                    playbackTime = previewFrame / Math.Max(0.0001f, clip.FrameRate);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                bool loop = EditorGUILayout.ToggleLeft("循环", clip.Loop, GUILayout.Width(65f));
                if (loop != clip.Loop)
                {
                    SerializedProperty property = serializedObject.FindProperty("loop");
                    property.boolValue = loop;
                    serializedObject.ApplyModifiedProperties();
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("定位资源", GUILayout.Width(76f)))
                    EditorGUIUtility.PingObject(clip);
            }
        }
    }
}
#endif
