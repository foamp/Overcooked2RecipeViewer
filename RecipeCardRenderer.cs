using System;
using UnityEngine;

namespace Overcooked2RecipeViewer
{
    // Reuses finalized viewer widgets synchronously, restoring each in finally.
    // The real game's order widgets and camera are never moved or modified.
    internal sealed class RecipeCardRenderer : IDisposable
    {
        internal const int MaxCardPixels = 4000000;
        private readonly GameObject _root, _cameraObject;
        private readonly RectTransform _rect;
        private readonly Canvas _canvas;
        private readonly Camera _camera;

        internal RecipeCardRenderer()
        {
            try
            {
            _root = new GameObject("RecipeViewer_ExportCanvas", typeof(RectTransform), typeof(Canvas));
            _root.hideFlags = HideFlags.HideAndDontSave;
            _root.layer = 5;
            _rect = _root.GetComponent<RectTransform>();
            _rect.pivot = new Vector2(0f, 1f);
            _rect.position = new Vector3(100000f, 100000f, 0f);
            _canvas = _root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _cameraObject = new GameObject("RecipeViewer_ExportCamera", typeof(Camera));
            _cameraObject.hideFlags = HideFlags.HideAndDontSave;
            _camera = _cameraObject.GetComponent<Camera>();
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.cullingMask = 1 << 5;
            _camera.nearClipPlane = 1f; _camera.farClipPlane = 200f;
            _camera.allowHDR = false; _camera.allowMSAA = false;
            _canvas.worldCamera = _camera;
            }
            catch { Dispose(); throw; }
        }

        internal static bool SafeCard(int width, int height)
        {
            return width > 0 && height > 0 && width <= Math.Min(8192, SystemInfo.maxTextureSize) &&
                height <= Math.Min(8192, SystemInfo.maxTextureSize) && (long)width * height <= MaxCardPixels;
        }

        internal byte[] Capture(RectTransform card, float minX, float maxY,
            int width, int height, float pad)
        {
            if (!SafeCard(width, height)) throw new InvalidOperationException("Card exceeds safe texture dimensions.");
            Transform parent = card.parent;
            int sibling = card.GetSiblingIndex();
            Vector2 position = card.anchoredPosition;
            Vector3 oldScale = card.localScale;
            Quaternion rotation = card.localRotation;
            Vector3 oldLocalPosition = card.localPosition;
            Transform[] children = card.GetComponentsInChildren<Transform>(true);
            int[] layers = new int[children.Length];
            for (int i = 0; i < children.Length; i++) layers[i] = children[i].gameObject.layer;
            RenderTexture prior = RenderTexture.active;
            RenderTexture target = null;
            Texture2D readback = null;
            try
            {
                // One output pixel per native card UI unit; no export multiplier.
                float unitsWidth = width, unitsHeight = height;
                _rect.sizeDelta = new Vector2(unitsWidth, unitsHeight);
                _camera.transform.position = _rect.position + new Vector3(unitsWidth * 0.5f, -unitsHeight * 0.5f, -100f);
                _camera.orthographicSize = unitsHeight * 0.5f;
                _camera.aspect = (float)width / height;
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                target.hideFlags = HideFlags.HideAndDontSave;
                target.antiAliasing = 1;
                if (!target.Create()) throw new InvalidOperationException("Could not allocate export render target.");
                readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
                _camera.targetTexture = target;
                card.SetParent(_rect, false);
                for (int i = 0; i < children.Length; i++) children[i].gameObject.layer = 5;
                card.localScale = Vector3.one; card.localRotation = Quaternion.identity;
                card.anchoredPosition3D = new Vector3(pad - minX, -pad - maxY, 0f);
                Canvas.ForceUpdateCanvases();
                _camera.backgroundColor = Color.black;
                _camera.Render();
                byte[] black = Read(target, readback, width, height);
                _camera.backgroundColor = Color.white;
                _camera.Render();
                byte[] white = Read(target, readback, width, height);
                RecipeExport.RecoverAlpha(black, white, QualitySettings.activeColorSpace == ColorSpace.Linear);
                bool visible = false;
                for (int i = 3; i < black.Length; i += 4)
                    if (black[i] != 0) { visible = true; break; }
                if (!visible) throw new InvalidOperationException("Original recipe card did not render; export cancelled to avoid missing content.");
                return black;
            }
            finally
            {
                if (card != null && parent != null)
                {
                    for (int i = 0; i < children.Length; i++)
                        if (children[i] != null) children[i].gameObject.layer = layers[i];
                    card.SetParent(parent, false);
                    card.SetSiblingIndex(sibling);
                    card.localScale = oldScale; card.localRotation = rotation;
                    card.localPosition = oldLocalPosition; card.anchoredPosition = position;
                }
                _camera.targetTexture = null;
                RenderTexture.active = prior;
                if (readback != null) UnityEngine.Object.Destroy(readback);
                if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
                Canvas.ForceUpdateCanvases();
            }
        }

        private static byte[] Read(RenderTexture target, Texture2D texture, int width, int height)
        {
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            texture.Apply(false);
            Color32[] colors = texture.GetPixels32();
            byte[] bytes = new byte[checked(width * height * 4)];
            for (int i = 0; i < colors.Length; i++)
            {
                int at = i * 4;
                bytes[at] = colors[i].r; bytes[at + 1] = colors[i].g;
                bytes[at + 2] = colors[i].b; bytes[at + 3] = colors[i].a;
            }
            return bytes;
        }

        public void Dispose()
        {
            if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
            if (_cameraObject != null) UnityEngine.Object.Destroy(_cameraObject);
        }
    }
}
