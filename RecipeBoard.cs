using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Overcooked2RecipeViewer
{
    // Uses the game's original order widgets, but never adds preview cards to
    // RecipeFlowGUI's live order list.
    internal sealed class RecipeBoard
    {
        private const float HorizontalGap = 22f;
        private const float VerticalGap = 22f;
        private const float BoardPadding = 16f;
        private const float BottomSafetyPadding = 200f;
        private const float ScrollStep = 110f;
        private const float InsertZoneHalfHeight = 5f;
        private const float MinZoom = 0.5f;
        private const float MaxZoom = 2f;
        private static float s_lastZoom = 1f;

        internal enum SortPreset { Original, AutoArrange, Name, Cookware }

        private sealed class Card
        {
            internal RecipeInfo Recipe;
            internal RecipeWidgetUIController Widget;
            internal RectTransform Rect;
            internal CanvasGroup Group;
            internal GameObject DragFrame;
            internal float MinX, MaxX, MinY, MaxY;
            internal float Width, Height;
            internal float LayoutX, LayoutTop;
            internal int OriginalIndex;
            internal string LayoutId;
        }

        private sealed class CardCapture
        {
            internal int Left, Top, Width, Height;
            internal Color32[] Pixels; // Bottom-up, as returned by Texture2D.GetPixels32().
        }

        private readonly GameObject _root;
        private readonly Canvas _canvas;
        private readonly Image _shade;
        private readonly RectTransform _viewport;
        private readonly Image _viewportImage;
        private readonly List<RectTransform> _rowPanels = new List<RectTransform>();
        private readonly RectTransform _content;
        private readonly RectTransform _handleLayer;
        private readonly RectTransform _dropMarker;
        private readonly List<RectTransform> _rowHandles = new List<RectTransform>();
        private readonly ManualLogSource _logger;
        private readonly RecipeLayoutStore _layoutStore;
        private readonly List<Card> _cards = new List<Card>();
        private readonly List<List<Card>> _rows = new List<List<Card>>();
        private readonly List<float> _rowTops = new List<float>();
        private readonly List<float> _rowHeights = new List<float>();
        private float _viewportWidth;
        private float _viewportHeight;
        private float _contentWidth;
        private float _layoutWidth;
        private float _contentHeight;
        private float _layoutHeight;
        private float _scrollX;
        private float _scrollY;
        private float _zoom = s_lastZoom;
        private Card _dragged;
        private Card _placeholderCard;
        private RectTransform _placeholderRect;
        private List<Card> _dragOriginalRow;
        private int _dragOriginalIndex;
        private int _dragOriginalRowIndex;
        private Vector2 _dragOffset;
        private List<Card> _dragRow;
        private int _rowDropIndex = -1;
        private int _cardGapIndex = -1;
        private bool _ready;
        private bool _exporting;
        private bool _needsExtentRefresh;
        private UiTextKey _statusKey = UiTextKey.None;
        private object[] _statusValues = new object[0];
        private SortPreset _selectedPreset = SortPreset.Original;


        private bool _usingBuiltInPreset = true;
        private bool _usingDraft;
        private string _activeSavedLayout;

        internal float ScrollY { get { return _scrollY; } }
        internal float ViewportHeight { get { return _viewportHeight; } }
        internal float ContentHeight { get { return _contentHeight * _zoom; } }
        internal float ScrollX { get { return _scrollX; } }
        internal float ViewportWidth { get { return _viewportWidth; } }
        internal float ContentWidth { get { return _contentWidth * _zoom; } }
        internal float Zoom { get { return _zoom; } }
        internal bool Ready { get { return _ready; } }
        internal bool Exporting { get { return _exporting; } }
        internal bool Dragging { get { return _dragged != null || _dragRow != null; } }
        internal string Status { get { return RecipeUiText.Text(_statusKey, _statusValues); } }
        internal string SortLabel
        {
            get
            {
                if (_usingBuiltInPreset) return RecipeUiText.PresetLabel(_selectedPreset, false);
                return _activeSavedLayout == null ? RecipeUiText.Text(UiTextKey.Custom) :
                    RecipeUiText.LayoutName(_activeSavedLayout);
            }
        }
        internal SortPreset CurrentPreset { get { return _selectedPreset; } }
        internal bool UsingBuiltInPreset { get { return _usingBuiltInPreset; } }
        internal bool UsingDraft { get { return _usingDraft; } }
        internal string ActiveSavedLayout { get { return _activeSavedLayout; } }
        internal IList<string> SavedLayouts { get { return _layoutStore == null ? new List<string>() : _layoutStore.SavedNames(); } }
        internal bool HasDraft { get { return _layoutStore != null && _layoutStore.ReadDraft() != null; } }

        internal RecipeBoard(IList<RecipeInfo> recipes, string levelKey, ManualLogSource logger)
        {
            _logger = logger;
            try
            {
                _layoutStore = new RecipeLayoutStore(levelKey, logger);
            }
            catch (Exception exception)
            {
                _logger.LogWarning("Layout storage unavailable; preview will use built-in presets: " +
                    exception);
            }
            try
            {
                RecipeFlowGUI flow = UnityEngine.Object.FindObjectOfType<RecipeFlowGUI>();
                if (flow == null) throw new InvalidOperationException("RecipeFlowGUI is not ready.");
                FieldInfo prefabField = AccessTools.Field(typeof(RecipeFlowGUI), "m_recipeWidgetPrefab");
                RecipeWidgetUIController prefab = prefabField == null
                    ? null : prefabField.GetValue(flow) as RecipeWidgetUIController;
                if (prefab == null) throw new InvalidOperationException("Original order-card prefab was not found.");

                _root = new GameObject("RecipeViewer_Board", typeof(RectTransform),
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                _canvas = _root.GetComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 32000;
                CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                RectTransform backdrop = CreateRect("Backdrop", _root.transform);
                Stretch(backdrop, 0f, 0f, 0f, 0f);
                _shade = backdrop.gameObject.AddComponent<Image>();
                _shade.color = new Color(0.025f, 0.055f, 0.055f, 0.92f);
                _shade.raycastTarget = true;
                RectTransform toolbar = CreateRect("ToolbarPanel", backdrop);
                toolbar.anchorMin = new Vector2(0f, 1f);
                toolbar.anchorMax = new Vector2(1f, 1f);
                toolbar.pivot = new Vector2(0.5f, 1f);
                toolbar.anchoredPosition = Vector2.zero;
                toolbar.sizeDelta = new Vector2(0f, 49f);
                Image toolbarImage = toolbar.gameObject.AddComponent<Image>();
                toolbarImage.color = new Color(0.15f, 0.28f, 0.27f, 0.98f);
                toolbarImage.raycastTarget = false;

                _viewport = CreateRect("Viewport", backdrop);
                Stretch(_viewport, 72f, 52f, 36f, 48f);
                _viewportImage = _viewport.gameObject.AddComponent<Image>();
                _viewportImage.color = new Color(0.075f, 0.16f, 0.16f, 0.96f);
                _viewportImage.raycastTarget = false;
                Outline viewportBorder = _viewport.gameObject.AddComponent<Outline>();
                viewportBorder.effectColor = new Color(0.68f, 0.53f, 0.29f, 0.74f);
                viewportBorder.effectDistance = new Vector2(2f, 2f);
                _viewport.gameObject.AddComponent<RectMask2D>();
                _handleLayer = CreateRect("RowHandles", backdrop);
                _handleLayer.anchorMin = new Vector2(0f, 1f);
                _handleLayer.anchorMax = new Vector2(0f, 1f);
                _handleLayer.pivot = new Vector2(0f, 1f);
                _handleLayer.anchoredPosition = new Vector2(21f, -52f);
                _handleLayer.sizeDelta = new Vector2(38f, Screen.height - 100f);
                _dropMarker = CreateRect("RowDropMarker", _viewport);
                _dropMarker.anchorMin = new Vector2(0f, 1f);
                _dropMarker.anchorMax = new Vector2(0f, 1f);
                _dropMarker.pivot = new Vector2(0f, 1f);
                Image markerImage = _dropMarker.gameObject.AddComponent<Image>();
                markerImage.color = new Color(1f, 0.78f, 0.32f, 0.9f);
                markerImage.raycastTarget = false;
                _dropMarker.gameObject.SetActive(false);
                Canvas.ForceUpdateCanvases();
                _viewportWidth = Math.Max(320f, _viewport.rect.width);
                _viewportHeight = Math.Max(300f, _viewport.rect.height);

                _content = CreateRect("Cards", _viewport);
                _content.anchorMin = new Vector2(0f, 1f);
                _content.anchorMax = new Vector2(0f, 1f);
                _content.pivot = new Vector2(0f, 1f);
                _content.localScale = new Vector3(_zoom, _zoom, 1f);
                _contentWidth = _viewportWidth;
                _contentHeight = _viewportHeight;
                _content.sizeDelta = new Vector2(_contentWidth, _contentHeight);

                RectTransform prefabRect = prefab.transform as RectTransform;
                Vector2 nativeSize = prefabRect == null ? new Vector2(100f, 100f) : prefabRect.rect.size;
                if (nativeSize.x < 1f || nativeSize.y < 1f) nativeSize = new Vector2(100f, 100f);

                Dictionary<string, int> identityCounts = new Dictionary<string, int>();
                for (int i = 0; i < recipes.Count; i++)
                {
                    GameObject clone = GameUtils.InstantiateUIController(prefab.gameObject, _content);
                    clone.name = "RecipeViewer_" + recipes[i].Name;
                    RectTransformExtension extension = clone.GetComponent<RectTransformExtension>();
                    if (extension != null) extension.enabled = false;
                    RectTransform rect = clone.GetComponent<RectTransform>();
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(0f, 1f);
                    rect.pivot = new Vector2(0f, 1f);
                    rect.sizeDelta = nativeSize;
                    rect.localScale = Vector3.one;
                    rect.anchoredPosition = Vector2.zero;
                    RecipeWidgetUIController widget = clone.GetComponent<RecipeWidgetUIController>();
                    widget.SetupFromOrderDefinition(recipes[i].Node, i);
                    Card card = new Card();
                    card.Recipe = recipes[i];
                    card.Widget = widget;
                    card.Rect = rect;
                    card.Group = clone.AddComponent<CanvasGroup>();
                    card.OriginalIndex = i;
                    string identity = recipes[i].UniqueId + "|" + recipes[i].Name;
                    int ordinal;
                    identityCounts.TryGetValue(identity, out ordinal);
                    identityCounts[identity] = ordinal + 1;
                    card.LayoutId = identity + "|" + ordinal;
                    _cards.Add(card);
                }

                _placeholderRect = CreateRect("DropPlaceholder", _content);
                Image placeholderImage = _placeholderRect.gameObject.AddComponent<Image>();
                placeholderImage.color = new Color(1f, 0.75f, 0.35f, 0.23f);
                placeholderImage.raycastTarget = false;
                Outline placeholderBorder = _placeholderRect.gameObject.AddComponent<Outline>();
                placeholderBorder.effectColor = new Color(1f, 0.82f, 0.43f, 0.95f);
                placeholderBorder.effectDistance = new Vector2(3f, 3f);
                _placeholderRect.gameObject.SetActive(false);

                _logger.LogInfo("Recipe board created with " + _cards.Count + " original order cards.");
            }
            catch
            {
                if (_root != null) UnityEngine.Object.Destroy(_root);
                throw;
            }
        }

        internal IEnumerator FinalizeCards()
        {
            // The game creates child tiles at the end of frame.
            yield return new WaitForEndOfFrame();
            yield return null;
            if (_root == null) yield break;

            int spriteCount = 0;
            int centeredDishCount = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                Card card = _cards[i];
                if (card.Widget == null) continue;
                UI_Move[] movers = card.Widget.GetComponentsInChildren<UI_Move>(true);
                for (int j = 0; j < movers.Length; j++)
                {
                    movers[j].enabled = false;
                    UnityEngine.Object.Destroy(movers[j]);
                }
                Animator[] animators = card.Widget.GetComponentsInChildren<Animator>(true);
                for (int j = 0; j < animators.Length; j++) animators[j].enabled = false;
                ProgressBarUI[] progressBars = card.Widget.GetComponentsInChildren<ProgressBarUI>(true);
                for (int j = 0; j < progressBars.Length; j++)
                    progressBars[j].gameObject.SetActive(false);
                Graphic[] graphics = card.Widget.GetComponentsInChildren<Graphic>(true);
                for (int j = 0; j < graphics.Length; j++)
                {
                    graphics[j].material = null;
                    graphics[j].raycastTarget = false;
                    Image image = graphics[j] as Image;
                    if (image != null && image.sprite != null) spriteCount++;
                }
            }

            // Native order tiles finish their layout over more than one frame.
            yield return null;
            yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();
            _viewportWidth = Math.Max(320f, _viewport.rect.width);
            _viewportHeight = Math.Max(300f, _viewport.rect.height);
            for (int i = 0; i < _cards.Count; i++)
                if (_cards[i].Widget != null)
                    centeredDishCount += CenterFinishedDishes(_cards[i].Widget);
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < _cards.Count; i++)
            {
                Card card = _cards[i];
                MeasureCard(card);
                RectTransform frame = CreateRect("DragFrame", card.Rect);
                frame.anchorMin = new Vector2(0f, 1f);
                frame.anchorMax = new Vector2(0f, 1f);
                frame.pivot = new Vector2(0f, 1f);
                frame.anchoredPosition = new Vector2(card.MinX - 7f, card.MaxY + 7f);
                frame.sizeDelta = new Vector2(card.Width + 14f, card.Height + 14f);
                Image fill = frame.gameObject.AddComponent<Image>();
                fill.color = new Color(1f, 0.81f, 0.48f, 0.19f);
                fill.raycastTarget = false;
                Outline border = frame.gameObject.AddComponent<Outline>();
                border.effectColor = new Color(1f, 0.76f, 0.31f, 0.9f);
                border.effectDistance = new Vector2(3f, 3f);
                frame.SetAsFirstSibling();
                card.DragFrame = frame.gameObject;
                frame.gameObject.SetActive(false);
            }
            BuildRows(_cards);
            LayoutRows();
            _ready = true;
            if (_layoutStore == null || !ApplyStoredLayout(_layoutStore.ReadDraft(), null))
                ApplySortPreset(SortPreset.Cookware);
            float smallest = float.MaxValue, largest = 0f;
            for (int i = 0; i < _cards.Count; i++)
            {
                smallest = Math.Min(smallest, _cards[i].Width);
                largest = Math.Max(largest, _cards[i].Width);
            }
            _logger.LogInfo(string.Format(
                "Recipe board ready: cards={0}, rows={1}, sprite images={2}, centered dishes={7}, widths={3:0}-{4:0}, content={5:0}x{6:0}.",
                _cards.Count, _rows.Count, spriteCount,
                _cards.Count == 0 ? 0f : smallest, largest, _contentWidth, _contentHeight,
                centeredDishCount));
        }

        private static void MeasureCard(Card card)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            Graphic[] graphics = card.Widget.GetComponentsInChildren<Graphic>(true);
            Vector3[] corners = new Vector3[4];
            for (int i = 0; i < graphics.Length; i++)
            {
                if (!graphics[i].gameObject.activeInHierarchy) continue;
                RectTransform rect = graphics[i].transform as RectTransform;
                if (rect == null) continue;
                rect.GetWorldCorners(corners);
                for (int j = 0; j < 4; j++)
                {
                    Vector3 point = card.Rect.InverseTransformPoint(corners[j]);
                    minX = Math.Min(minX, point.x);
                    maxX = Math.Max(maxX, point.x);
                    minY = Math.Min(minY, point.y);
                    maxY = Math.Max(maxY, point.y);
                }
            }
            // Some order-card artwork extends beyond its Graphic's rectangle.
            // The game's tile rectangles describe the full paper and ingredient tabs.
            RecipeWidgetTile[] tiles = card.Widget.GetComponentsInChildren<RecipeWidgetTile>(true);
            for (int i = 0; i < tiles.Length; i++)
            {
                if (!tiles[i].gameObject.activeInHierarchy) continue;
                RectTransform rect = tiles[i].transform as RectTransform;
                if (rect == null) continue;
                rect.GetWorldCorners(corners);
                for (int j = 0; j < 4; j++)
                {
                    Vector3 point = card.Rect.InverseTransformPoint(corners[j]);
                    minX = Math.Min(minX, point.x);
                    maxX = Math.Max(maxX, point.x);
                    minY = Math.Min(minY, point.y);
                    maxY = Math.Max(maxY, point.y);
                }
            }
            if (minX == float.MaxValue || maxX - minX < 2f || maxY - minY < 2f)
            {
                minX = 0f;
                maxX = 260f;
                minY = -220f;
                maxY = 0f;
            }
            // The native tile may draw below its layout rect. Keep a minimum
            // full-card envelope so rows and exported captures cannot overlap.
            minX -= 8f;
            maxX += 20f;
            maxY += 8f;
            minY = Math.Min(minY - 20f, maxY - 280f);
            card.MinX = minX;
            card.MaxX = maxX;
            card.MinY = minY;
            card.MaxY = maxY;
            card.Width = maxX - minX;
            card.Height = maxY - minY;
        }

        private static int CenterFinishedDishes(RecipeWidgetUIController widget)
        {
            FieldInfo mainField = AccessTools.Field(typeof(RecipeWidgetTile), "m_mainImages");
            FieldInfo backgroundField = AccessTools.Field(typeof(RecipeWidgetTile), "m_background");
            FieldInfo backgroundTopField = AccessTools.Field(typeof(RecipeWidgetTile), "m_backgroundTop");
            if (mainField == null || backgroundField == null || backgroundTopField == null) return 0;
            int centered = 0;
            TopRecipeWidgetTile[] tops = widget.GetComponentsInChildren<TopRecipeWidgetTile>(true);
            for (int i = 0; i < tops.Length; i++)
            {
                IEnumerable images = mainField.GetValue(tops[i]) as IEnumerable;
                if (images == null) continue;
                Image onlyImage = null;
                int count = 0;
                foreach (object item in images)
                {
                    Image image = item as Image;
                    if (image == null || image.sprite == null) continue;
                    onlyImage = image;
                    count++;
                }
                if (count != 1 || onlyImage == null) continue;

                float paperLeft = float.MaxValue, paperRight = float.MinValue;
                float paperBottom = float.MaxValue, paperTop = float.MinValue;
                IncludeImageBounds(backgroundField.GetValue(tops[i]) as Image,
                    ref paperLeft, ref paperRight, ref paperBottom, ref paperTop);
                IncludeImageBounds(backgroundTopField.GetValue(tops[i]) as Image,
                    ref paperLeft, ref paperRight, ref paperBottom, ref paperTop);
                if (paperRight <= paperLeft || paperTop <= paperBottom) continue;

                // The paper's own bounds are the visual box. Ingredient-tile
                // rects can reach upward behind it and gave a falsely high
                // bottom edge, leaving the dish tiny and near the top.
                float boxBottom = paperBottom;
                float boxHeight = paperTop - paperBottom;
                if (boxHeight < 25f) continue;

                RectTransform dishRect = onlyImage.rectTransform;
                if (dishRect.parent == null) continue;
                onlyImage.preserveAspect = true;
                Vector3[] dishCorners = new Vector3[4];
                dishRect.GetWorldCorners(dishCorners);
                float dishWidth = Math.Abs(dishCorners[2].x - dishCorners[0].x);
                float dishHeight = Math.Abs(dishCorners[2].y - dishCorners[0].y);
                if (dishWidth < 1f || dishHeight < 1f) continue;
                // Recipe sprites have generous transparent margins inside their
                // UI rectangles. Allow the rectangle to exceed the paper while
                // keeping the visible dish comfortably inside the paper.
                float scale = Mathf.Min(2f,
                    Mathf.Min((paperRight - paperLeft) * 1.35f / dishWidth,
                        boxHeight * 1.35f / dishHeight));
                dishRect.pivot = new Vector2(0.5f, 0.5f);
                dishRect.localScale = dishRect.localScale * scale;
                dishRect.position = new Vector3((paperLeft + paperRight) * 0.5f,
                    (paperTop + boxBottom) * 0.5f, dishRect.position.z);
                centered++;
            }
            return centered;
        }

        private static void IncludeImageBounds(Image image, ref float left, ref float right,
            ref float bottom, ref float top)
        {
            if (image == null || !image.enabled || !image.gameObject.activeInHierarchy) return;
            RectTransform rect = image.rectTransform;
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            for (int i = 0; i < corners.Length; i++)
            {
                left = Math.Min(left, corners[i].x);
                right = Math.Max(right, corners[i].x);
                bottom = Math.Min(bottom, corners[i].y);
                top = Math.Max(top, corners[i].y);
            }
        }

        private void BuildRows(IList<Card> cards)
        {
            _rows.Clear();
            List<Card> row = new List<Card>();
            float used = BoardPadding;
            for (int i = 0; i < cards.Count; i++)
            {
                Card card = cards[i];
                float needed = (row.Count == 0 ? 0f : HorizontalGap) + card.Width;
                if (row.Count > 0 && used + needed + BoardPadding > _viewportWidth)
                {
                    _rows.Add(row);
                    row = new List<Card>();
                    used = BoardPadding;
                    needed = card.Width;
                }
                row.Add(card);
                used += needed;
            }
            if (row.Count > 0) _rows.Add(row);
        }

        private void LayoutRows()
        {
            _rowTops.Clear();
            _rowHeights.Clear();
            EnsureRowPanels();
            float top = BoardPadding;
            float widest = BoardPadding * 2f;
            for (int r = 0; r < _rows.Count; r++)
            {
                List<Card> row = _rows[r];
                float rowHeight = 0f;
                float x = BoardPadding;
                _rowTops.Add(top);
                for (int c = 0; c < row.Count; c++)
                {
                    Card card = row[c];
                    card.LayoutX = x;
                    card.LayoutTop = top;
                    if (card == _placeholderCard)
                    {
                        _placeholderRect.anchorMin = new Vector2(0f, 1f);
                        _placeholderRect.anchorMax = new Vector2(0f, 1f);
                        _placeholderRect.pivot = new Vector2(0f, 1f);
                        _placeholderRect.anchoredPosition = new Vector2(x - 5f, -top + 5f);
                        _placeholderRect.sizeDelta = new Vector2(card.Width + 10f, card.Height + 10f);
                    }
                    else card.Rect.anchoredPosition = new Vector2(x - card.MinX, -top - card.MaxY);
                    x += card.Width + HorizontalGap;
                    rowHeight = Math.Max(rowHeight, card.Height);
                }
                _rowHeights.Add(rowHeight);
                RectTransform panel = _rowPanels[r];
                panel.anchoredPosition = new Vector2(8f, -top + 8f);
                panel.sizeDelta = new Vector2(
                    Math.Max(_viewportWidth - 16f, x - HorizontalGap + BoardPadding - 8f),
                    rowHeight + 16f);
                widest = Math.Max(widest, x - HorizontalGap + BoardPadding);
                top += rowHeight + VerticalGap;
            }
            _layoutHeight = _rows.Count == 0 ? BoardPadding * 2f :
                top - VerticalGap + BoardPadding + BottomSafetyPadding;
            _layoutWidth = widest;
            _contentWidth = Math.Max(_viewportWidth / _zoom, _layoutWidth);
            _contentHeight = Math.Max(_viewportHeight / _zoom, _layoutHeight);
            _content.sizeDelta = new Vector2(_contentWidth, _contentHeight);
            _needsExtentRefresh = true;
            EnsureRowHandles();
            SetScroll(_scrollX, _scrollY);
        }

        private void RefreshContentExtent()
        {
            _needsExtentRefresh = false;
            if (_dragged != null || _dragRow != null || _exporting || _root == null) return;
            Canvas.ForceUpdateCanvases();
            _viewportWidth = Math.Max(320f, _viewport.rect.width);
            _viewportHeight = Math.Max(300f, _viewport.rect.height);
            float actualBottom = 0f;
            float actualRight = 0f;
            Vector3[] corners = new Vector3[4];
            for (int i = 0; i < _cards.Count; i++)
            {
                Graphic[] graphics = _cards[i].Widget.GetComponentsInChildren<Graphic>(true);
                for (int j = 0; j < graphics.Length; j++)
                {
                    if (!graphics[j].gameObject.activeInHierarchy) continue;
                    RectTransform rect = graphics[j].transform as RectTransform;
                    if (rect == null) continue;
                    rect.GetWorldCorners(corners);
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 point = _content.InverseTransformPoint(corners[k]);
                        actualBottom = Math.Max(actualBottom, -point.y);
                        actualRight = Math.Max(actualRight, point.x);
                    }
                }
            }
            // Base the maximum scroll on rendered artwork, not just the
            // prefab's early layout measurements. Leave enough travel to put
            // the final row in the upper half of the viewport if necessary.
            float lastRowTop = _rowTops.Count == 0 ? 0f : _rowTops[_rowTops.Count - 1];
            float scrollableBottom = Math.Max(actualBottom + 80f,
                lastRowTop + _viewportHeight / _zoom * 0.55f);
            _contentHeight = Math.Max(_viewportHeight / _zoom,
                Math.Max(_layoutHeight, scrollableBottom));
            _contentWidth = Math.Max(_viewportWidth / _zoom,
                Math.Max(_layoutWidth, actualRight + 40f));
            _content.sizeDelta = new Vector2(_contentWidth, _contentHeight);
            SetScroll(_scrollX, _scrollY);
            _logger.LogInfo(string.Format(
                "Recipe scroll bounds: visible={0:0}x{1:0}, rendered={2:0}x{3:0}, content={4:0}x{5:0}.",
                _viewportWidth, _viewportHeight, actualRight, actualBottom,
                _contentWidth, _contentHeight));
        }

        private void EnsureRowPanels()
        {
            while (_rowPanels.Count < _rows.Count)
            {
                RectTransform panel = CreateRect("RecipeRowPanel", _content);
                panel.anchorMin = new Vector2(0f, 1f);
                panel.anchorMax = new Vector2(0f, 1f);
                panel.pivot = new Vector2(0f, 1f);
                Image image = panel.gameObject.AddComponent<Image>();
                image.color = new Color(0.14f, 0.27f, 0.26f, 0.92f);
                image.raycastTarget = false;
                Outline border = panel.gameObject.AddComponent<Outline>();
                border.effectColor = new Color(0.60f, 0.46f, 0.25f, 0.55f);
                border.effectDistance = new Vector2(2f, 2f);
                panel.SetAsFirstSibling();
                _rowPanels.Add(panel);
            }
            for (int i = 0; i < _rowPanels.Count; i++)
                _rowPanels[i].gameObject.SetActive(i < _rows.Count);
        }
        private void EnsureRowHandles()
        {
            while (_rowHandles.Count < _rows.Count)
            {
                RectTransform handle = CreateRect("RowHandle", _handleLayer);
                handle.anchorMin = new Vector2(0f, 1f);
                handle.anchorMax = new Vector2(0f, 1f);
                handle.pivot = new Vector2(0f, 1f);
                handle.sizeDelta = new Vector2(36f, 38f);
                Image background = handle.gameObject.AddComponent<Image>();
                background.raycastTarget = false;
                for (int line = 0; line < 3; line++)
                {
                    RectTransform grip = CreateRect("Grip", handle);
                    grip.anchorMin = new Vector2(0f, 1f);
                    grip.anchorMax = new Vector2(0f, 1f);
                    grip.pivot = new Vector2(0f, 1f);
                    grip.anchoredPosition = new Vector2(9f, -11f - line * 7f);
                    grip.sizeDelta = new Vector2(18f, 3f);
                    Image stripe = grip.gameObject.AddComponent<Image>();
                    stripe.color = new Color(1f, 0.85f, 0.54f, 0.95f);
                    stripe.raycastTarget = false;
                }
                _rowHandles.Add(handle);
            }
            for (int i = 0; i < _rowHandles.Count; i++)
                _rowHandles[i].gameObject.SetActive(i < _rows.Count);
        }

        private void UpdateChrome(Vector2 mouse)
        {
            if (_root == null) return;
            for (int i = 0; i < _rowHandles.Count; i++)
            {
                RectTransform handle = _rowHandles[i];
                if (i >= _rows.Count) { handle.gameObject.SetActive(false); continue; }
                float y = _rowTops[i] * _zoom - _scrollY + 5f;
                bool visible = y + 38f > 0f && y < _viewportHeight;
                handle.gameObject.SetActive(visible);
                if (!visible) continue;
                handle.anchoredPosition = new Vector2(0f, -y);
                bool hover = RectTransformUtility.RectangleContainsScreenPoint(handle, mouse, null);
                handle.GetComponent<Image>().color = _dragRow == _rows[i]
                    ? new Color(0.61f, 0.37f, 0.12f, 0.95f)
                    : hover ? new Color(0.35f, 0.28f, 0.17f, 0.94f)
                    : new Color(0.16f, 0.20f, 0.20f, 0.88f);
            }
            int insertion = _dragRow != null ? _rowDropIndex : _cardGapIndex;
            if (insertion < 0 || _rows.Count == 0)
            {
                _dropMarker.gameObject.SetActive(false);
                return;
            }
            float markerTop = insertion == 0 ? BoardPadding * 0.5f :
                insertion >= _rows.Count
                    ? _rowTops[_rows.Count - 1] + _rowHeights[_rows.Count - 1] +
                        VerticalGap * 0.5f
                    : (_rowTops[insertion - 1] + _rowHeights[insertion - 1] +
                        _rowTops[insertion]) * 0.5f;
            float viewportY = markerTop * _zoom - _scrollY;
            _dropMarker.gameObject.SetActive(viewportY >= 0f && viewportY < _viewportHeight);
            _dropMarker.anchoredPosition = new Vector2(8f, -viewportY + 2f);
            _dropMarker.sizeDelta = new Vector2(_viewportWidth - 16f, 4f);
            _dropMarker.SetAsLastSibling();
        }

        private int FindCardGap(float fromTop)
        {
            for (int i = 1; i < _rows.Count; i++)
            {
                float middle = (_rowTops[i - 1] + _rowHeights[i - 1] +
                    _rowTops[i]) * 0.5f;
                if (Math.Abs(fromTop - middle) <= InsertZoneHalfHeight) return i;
            }
            return -1;
        }

        private int FindRowInsertion(float fromTop)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (fromTop < _rowTops[i] + _rowHeights[i] * 0.5f) return i;
            return _rows.Count;
        }

        private void AutoScrollDuringDrag(Vector2 mouse, bool horizontal)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, mouse, null, out local);
            float fromTop = _viewport.rect.yMax - local.y;
            float y = _scrollY;
            float x = _scrollX;
            float speed = 260f * Time.unscaledDeltaTime;
            if (fromTop < 30f) y -= speed;
            else if (fromTop > _viewportHeight - 30f) y += speed;
            if (horizontal)
            {
                float fromLeft = local.x - _viewport.rect.xMin;
                if (fromLeft < 30f) x -= speed;
                else if (fromLeft > _viewportWidth - 30f) x += speed;
            }
            SetScroll(x, y);
        }
        internal void UpdatePointer(bool blockNewDrag)
        {
            if (!_ready || _exporting || _root == null) return;
            if (_needsExtentRefresh && !Dragging) RefreshContentExtent();
            Vector2 mouse = Input.mousePosition;

            if (!Dragging && !blockNewDrag && Input.GetMouseButtonDown(0))
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (!_rowHandles[i].gameObject.activeSelf ||
                        !RectTransformUtility.RectangleContainsScreenPoint(
                            _rowHandles[i], mouse, null)) continue;
                    _dragRow = _rows[i];
                    _rowDropIndex = i;
                    for (int c = 0; c < _dragRow.Count; c++)
                        _dragRow[c].Group.alpha = 0.7f;
                    break;
                }
                if (_dragRow == null &&
                    RectTransformUtility.RectangleContainsScreenPoint(_viewport, mouse, null))
                {
                    for (int i = _cards.Count - 1; i >= 0; i--)
                    {
                        if (!ContainsPoint(_cards[i], mouse)) continue;
                        _dragged = _cards[i];
                        Vector2 local;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            _content, mouse, null, out local);
                        _dragOffset = _dragged.Rect.anchoredPosition - local;
                        for (int row = 0; row < _rows.Count; row++)
                        {
                            int index = _rows[row].IndexOf(_dragged);
                            if (index < 0) continue;
                            _dragOriginalRow = _rows[row];
                            _dragOriginalIndex = index;
                            _dragOriginalRowIndex = row;
                            _placeholderCard = new Card();
                            _placeholderCard.Width = _dragged.Width;
                            _placeholderCard.Height = _dragged.Height;
                            _rows[row][index] = _placeholderCard;
                            _placeholderRect.gameObject.SetActive(true);
                            _dragged.Group.alpha = 0.88f;
                            _dragged.DragFrame.SetActive(true);
                            LayoutRows();
                            break;
                        }
                        _dragged.Rect.SetAsLastSibling();
                        break;
                    }
                }
            }

            if (_dragRow != null)
            {
                if (Input.GetMouseButton(0))
                {
                    Vector2 local;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _content, mouse, null, out local);
                    _rowDropIndex = FindRowInsertion(-local.y);
                    AutoScrollDuringDrag(mouse, false);
                }
                if (Input.GetMouseButtonUp(0)) DropRow(mouse);
                UpdateChrome(mouse);
                return;
            }

            if (_dragged != null)
            {
                if (Input.GetMouseButton(0))
                {
                    Vector2 local;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _content, mouse, null, out local);
                    _dragged.Rect.anchoredPosition = local + _dragOffset;
                    bool overViewport = RectTransformUtility.RectangleContainsScreenPoint(
                        _viewport, mouse, null);
                    _cardGapIndex = overViewport ? FindCardGap(-local.y) : -1;
                    if (_cardGapIndex < 0 && overViewport) MovePlaceholder(local);
                    if (overViewport) AutoScrollDuringDrag(mouse, true);
                }
                if (Input.GetMouseButtonUp(0)) DropCard(mouse);
            }
            UpdateChrome(mouse);
        }

        private void DropRow(Vector2 screenPoint)
        {
            if (_dragRow == null) return;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _viewport, screenPoint, null, out local);
            bool inside = local.y <= _viewport.rect.yMax &&
                local.y >= _viewport.rect.yMin &&
                local.x >= _viewport.rect.xMin - 60f &&
                local.x <= _viewport.rect.xMax;
            bool moved = false;
            if (inside)
            {
                int from = _rows.IndexOf(_dragRow);
                int target = Math.Max(0, Math.Min(_rowDropIndex, _rows.Count));
                if (from >= 0)
                {
                    _rows.RemoveAt(from);
                    if (target > from) target--;
                    _rows.Insert(Math.Min(target, _rows.Count), _dragRow);
                    moved = true;
                    SetStatus(UiTextKey.MovedRow, from + 1, target + 1);
                    _logger.LogInfo(Status);
                }
            }
            for (int i = 0; i < _dragRow.Count; i++)
                _dragRow[i].Group.alpha = 1f;
            _dragRow = null;
            _rowDropIndex = -1;
            LayoutRows();
            if (moved) RecordCustomLayout();
        }
        private bool ContainsPoint(Card card, Vector2 screenPoint)
        {
            Vector3 bottomLeft = card.Rect.TransformPoint(new Vector3(card.MinX, card.MinY, 0f));
            Vector3 topRight = card.Rect.TransformPoint(new Vector3(card.MaxX, card.MaxY, 0f));
            Vector2 a = RectTransformUtility.WorldToScreenPoint(null, bottomLeft);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(null, topRight);
            Rect rect = Rect.MinMaxRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y),
                Math.Max(a.x, b.x), Math.Max(a.y, b.y));
            return rect.Contains(screenPoint);
        }

        private void DropCard(Vector2 screenPoint)
        {
            Card dragged = _dragged;
            if (dragged == null) return;
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(
                _viewport, screenPoint, null);
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _content, screenPoint, null, out local);
            int gap = inside ? FindCardGap(-local.y) : -1;
            List<Card> successor = gap >= 0 && gap < _rows.Count ? _rows[gap] : null;
            if (inside && gap < 0) MovePlaceholder(local);
            _dragged = null;
            List<Card> destination = null;
            int insertion = 0;
            for (int row = 0; row < _rows.Count; row++)
            {
                int index = _rows[row].IndexOf(_placeholderCard);
                if (index < 0) continue;
                destination = _rows[row];
                insertion = index;
                destination.RemoveAt(index);
                break;
            }
            if (!inside)
            {
                destination = _dragOriginalRow;
                insertion = _dragOriginalIndex;
            }
            for (int row = _rows.Count - 1; row >= 0; row--)
                if (_rows[row].Count == 0) _rows.RemoveAt(row);
            if (gap >= 0 && inside)
            {
                destination = new List<Card>();
                int rowIndex = successor == null ? Math.Min(gap, _rows.Count) : _rows.IndexOf(successor);
                if (rowIndex < 0) rowIndex = Math.Min(gap, _rows.Count);
                _rows.Insert(rowIndex, destination);
                insertion = 0;
            }
            else if (destination == null)
            {
                destination = _dragOriginalRow ?? new List<Card>();
            }
            if (!_rows.Contains(destination))
            {
                int rowIndex = inside ? _rows.Count :
                    Math.Min(_dragOriginalRowIndex, _rows.Count);
                _rows.Insert(rowIndex, destination);
            }
            destination.Insert(Math.Min(insertion, destination.Count), dragged);
            _placeholderCard = null;
            _cardGapIndex = -1;
            _placeholderRect.gameObject.SetActive(false);
            dragged.Group.alpha = 1f;
            dragged.DragFrame.SetActive(false);
            LayoutRows();
            if (inside)
            {
                SetStatus(gap >= 0 ? UiTextKey.CreatedRow : UiTextKey.MovedRecipe, dragged.Recipe.Name);
                _logger.LogInfo(Status);
                RecordCustomLayout();
            }
        }
        private void MovePlaceholder(Vector2 local)
        {
            if (_placeholderCard == null) return;
            int targetIndex = FindTargetRow(-local.y);
            List<Card> target = targetIndex < _rows.Count ? _rows[targetIndex] : null;
            int insertAt = 0;
            if (target != null)
            {
                for (int i = 0; i < target.Count; i++)
                    if (target[i] != _placeholderCard &&
                        local.x > target[i].LayoutX + target[i].Width * 0.5f) insertAt++;
            }

            List<Card> current = null;
            int currentIndex = -1;
            for (int i = 0; i < _rows.Count; i++)
            {
                int index = _rows[i].IndexOf(_placeholderCard);
                if (index >= 0) { current = _rows[i]; currentIndex = index; break; }
            }
            if (target == current && insertAt == currentIndex) return;
            if (current != null) current.Remove(_placeholderCard);
            for (int i = _rows.Count - 1; i >= 0; i--)
                if (_rows[i].Count == 0 && _rows[i] != target) _rows.RemoveAt(i);
            if (target == null)
            {
                target = new List<Card>();
                _rows.Insert(Math.Min(targetIndex, _rows.Count), target);
            }
            else if (!_rows.Contains(target)) _rows.Add(target);
            target.Insert(Math.Min(insertAt, target.Count), _placeholderCard);
            LayoutRows();
        }

        private int FindTargetRow(float fromTop)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                float end = _rowTops[i] + _rowHeights[i];
                if (fromTop <= end + VerticalGap * 0.5f) return i;
            }
            return _rows.Count;
        }

        private void SetStatus(UiTextKey key, params object[] values)
        {
            _statusKey = key;
            _statusValues = values;
        }

        private List<List<string>> SnapshotRows()
        {
            List<List<string>> snapshot = new List<List<string>>();
            for (int i = 0; i < _rows.Count; i++)
            {
                List<string> ids = new List<string>();
                for (int j = 0; j < _rows[i].Count; j++)
                    ids.Add(_rows[i][j].LayoutId);
                if (ids.Count > 0) snapshot.Add(ids);
            }
            return snapshot;
        }

        private bool ApplyStoredLayout(List<List<string>> savedRows, string savedName)
        {
            if (savedRows == null || savedRows.Count == 0) return false;
            Dictionary<string, Card> byId = new Dictionary<string, Card>();
            for (int i = 0; i < _cards.Count; i++) byId[_cards[i].LayoutId] = _cards[i];
            HashSet<Card> used = new HashSet<Card>();
            List<List<Card>> restored = new List<List<Card>>();
            for (int i = 0; i < savedRows.Count; i++)
            {
                List<Card> row = new List<Card>();
                for (int j = 0; j < savedRows[i].Count; j++)
                {
                    Card card;
                    if (byId.TryGetValue(savedRows[i][j], out card) && used.Add(card))
                        row.Add(card);
                }
                if (row.Count > 0) restored.Add(row);
            }
            if (used.Count == 0) return false;
            List<Card> added = new List<Card>();
            for (int i = 0; i < _cards.Count; i++)
                if (!used.Contains(_cards[i])) added.Add(_cards[i]);
            if (added.Count > 0)
            {
                BuildRows(added);
                restored.AddRange(_rows);
            }
            _rows.Clear();
            _rows.AddRange(restored);
            LayoutRows();
            SetScroll(0f, 0f);
            _usingBuiltInPreset = false;
            _activeSavedLayout = savedName;
            _usingDraft = savedName == null;

            if (savedName == null) SetStatus(UiTextKey.RestoreDraft, _rows.Count);
            else SetStatus(UiTextKey.LoadedSaved, savedName, _rows.Count);
            _logger.LogInfo(Status);
            return true;
        }

        internal void ApplyDraftLayout()
        {
            if (!_ready || _exporting || Dragging) return;
            if (_layoutStore == null || !ApplyStoredLayout(_layoutStore.ReadDraft(), null))
                SetStatus(UiTextKey.NoDraft);
        }

        internal void ApplySavedLayout(string name)
        {
            if (!_ready || _exporting || Dragging) return;
            if (_layoutStore == null || !ApplyStoredLayout(_layoutStore.ReadSaved(name), name))
                SetStatus(UiTextKey.SavedUnavailable);
        }

        internal void SaveCurrentLayout()
        {
            if (!_ready || _exporting || Dragging) return;
            if (_layoutStore == null)
            {
                SetStatus(UiTextKey.StorageUnavailable);
                return;
            }
            try
            {
                string name = _layoutStore.SaveNamed(SnapshotRows());
                SetStatus(UiTextKey.SavedLayout, name);
                _logger.LogInfo(Status + " File: " + _layoutStore.Path);
            }
            catch (Exception exception)
            {
                SetStatus(UiTextKey.SaveFailed);
                _logger.LogError("Recipe layout save failed: " + exception);
            }
        }

        internal void DeleteSavedLayout(string name)
        {
            if (!_ready || _exporting || Dragging || _layoutStore == null) return;
            try
            {
                if (!_layoutStore.DeleteSaved(name))
                {
                    SetStatus(UiTextKey.SavedUnavailable);
                    return;
                }
                if (_activeSavedLayout == name)
                {
                    _activeSavedLayout = null;
                    _usingBuiltInPreset = false;
                }
                SetStatus(UiTextKey.DeletedLayout, name);
                _logger.LogInfo(Status);
            }
            catch (Exception exception)
            {
                SetStatus(UiTextKey.DeleteFailed);
                _logger.LogError("Recipe layout delete failed: " + exception);
            }
        }

        private void RecordCustomLayout()
        {
            _usingBuiltInPreset = false;
            _usingDraft = true;
            _activeSavedLayout = null;
            try
            {
                if (_layoutStore == null)
                {
                    SetStatus(UiTextKey.SessionOnly);
                    return;
                }
                _layoutStore.SaveDraft(SnapshotRows());
                _logger.LogInfo("Updated custom recipe layout: " + _layoutStore.Path);
            }
            catch (Exception exception)
            {
                SetStatus(UiTextKey.AutoSaveFailed);
                _logger.LogError("Recipe layout auto-save failed: " + exception);
            }
        }
        internal void ApplySortPreset(SortPreset next)
        {
            if (!_ready || _exporting || Dragging) return;
            List<List<Card>> previousRows = new List<List<Card>>();
            for (int i = 0; i < _rows.Count; i++)
                previousRows.Add(new List<Card>(_rows[i]));
            try
            {
                if (next == SortPreset.Original)
                {
                    BuildRows(_cards);
                }
                else if (next == SortPreset.AutoArrange)
                {
                    AutoArrange();
                }
                else if (next == SortPreset.Name)
                {
                    List<Card> ordered = new List<Card>(_cards);
                    ordered.Sort(delegate(Card a, Card b)
                    {
                        int byName = StringComparer.OrdinalIgnoreCase.Compare(
                            a.Recipe.Name, b.Recipe.Name);
                        return byName != 0 ? byName :
                            a.OriginalIndex.CompareTo(b.OriginalIndex);
                    });
                    BuildRows(ordered);
                }
                else
                {
                    Dictionary<Card, string> categories = new Dictionary<Card, string>();
                    RecipeSortMetadata metadata = new RecipeSortMetadata(_logger);
                    for (int i = 0; i < _cards.Count; i++)
                    {
                        Card card = _cards[i];
                        string category = metadata.CookwareFor(card.Recipe.Node);
                        categories.Add(card, category);
                        _logger.LogInfo("Recipe sort " + next + ": " +
                            card.Recipe.Name + " | uid=" + card.Recipe.UniqueId +
                            " | group=" + category);
                    }
                    BuildCategoryRows(categories);
                }

                LayoutRows();
                SetScroll(0f, 0f);
                _selectedPreset = next;
                _usingBuiltInPreset = true;
                _usingDraft = false;
                _activeSavedLayout = null;


                SetStatus(UiTextKey.PresetApplied, next, _rows.Count);
                _logger.LogInfo(Status);
            }
            catch (Exception exception)
            {
                _rows.Clear();
                _rows.AddRange(previousRows);
                LayoutRows();
                SetStatus(UiTextKey.SortFailed);
                _logger.LogError("Recipe sort preset failed: " + exception);
            }
        }

        private void BuildCategoryRows(Dictionary<Card, string> categories)
        {
            Dictionary<string, List<Card>> groups = new Dictionary<string, List<Card>>();
            for (int i = 0; i < _cards.Count; i++)
            {
                Card card = _cards[i];
                string category = categories[card];
                List<Card> group;
                if (!groups.TryGetValue(category, out group))
                {
                    group = new List<Card>();
                    groups.Add(category, group);
                }
                group.Add(card);
            }

            List<string> names = new List<string>(groups.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            if (names.Remove("No cooking step")) names.Add("No cooking step");
            if (names.Remove("Unmapped cookware")) names.Add("Unmapped cookware");
            _rows.Clear();
            for (int i = 0; i < names.Count; i++) _rows.Add(groups[names[i]]);
        }

        internal void AutoArrange()
        {
            if (!_ready || _exporting) return;
            Dictionary<string, List<Card>> groups = new Dictionary<string, List<Card>>();
            List<string> keys = new List<string>();
            for (int i = 0; i < _cards.Count; i++)
            {
                string key = Classify(_cards[i].Recipe.Name);
                List<Card> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<Card>();
                    groups.Add(key, group);
                    keys.Add(key);
                }
                group.Add(_cards[i]);
            }
            _rows.Clear();
            for (int i = 0; i < keys.Count; i++) _rows.Add(groups[keys[i]]);
            LayoutRows();
            SetScroll(0f, 0f);

            SetStatus(UiTextKey.Grouped, _cards.Count, _rows.Count);
            _logger.LogInfo(Status);
        }

        private static string Classify(string name)
        {
            string n = (name ?? string.Empty).ToLowerInvariant();
            if (n.Contains("burger")) return "Burger";
            if (n.Contains("pizza")) return "Pizza";
            if (n.Contains("burrito")) return "Burrito";
            if (n.Contains("sushi")) return "Sushi";
            if (n.Contains("moonpie")) return "Moon pie";
            if (n.Contains("pancake")) return "Pancake";
            if (n.Contains("cake")) return "Cake";
            if (n.Contains("salad") || n.Contains("tomato_cucumber")) return "Salad";
            if (n.Contains("soup")) return "Soup";
            if (n.Contains("noodle")) return "Noodles";
            if (n.Contains("rice")) return "Rice";
            if (n.Contains("roast")) return "Roast";
            if (n.Contains("fruitplatter")) return "Fruit platter";
            if (n.Contains("smoothie") || n.Contains("drink") || n.Contains("cocktail")) return "Drinks";
            if (n.Contains("skewer") || n.Contains("kebab")) return "Skewers";
            if (n.Contains("donut")) return "Donut";
            if (n.Contains("chip") || n.Contains("nugget")) return "Chips and nuggets";
            if (n.Contains("steamed")) return "Steamed dishes";
            int separator = n.IndexOf('_');
            if (separator > 0) return n.Substring(0, separator);
            return n.Length == 0 ? "Other" : n;
        }

        internal void ZoomWheel(float wheelDelta, Vector2 screenPoint)
        {
            if (!_ready || _exporting || Dragging || _root == null) return;
            float next = Mathf.Clamp(_zoom * Mathf.Pow(1.1f, wheelDelta), MinZoom, MaxZoom);
            if (Math.Abs(next - _zoom) < 0.0001f) return;
            if (_needsExtentRefresh) RefreshContentExtent();
            Vector2 anchor = new Vector2(_viewportWidth * 0.5f, _viewportHeight * 0.5f);
            Vector2 local;
            if (RectTransformUtility.RectangleContainsScreenPoint(_viewport, screenPoint, null) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _viewport, screenPoint, null, out local))
                anchor = new Vector2(local.x - _viewport.rect.xMin,
                    _viewport.rect.yMax - local.y);
            // Scroll offsets are viewport units; card positions stay in unscaled content units.
            float targetX = (_scrollX + anchor.x) / _zoom * next - anchor.x;
            float targetY = (_scrollY + anchor.y) / _zoom * next - anchor.y;
            _zoom = next;
            s_lastZoom = next;
            _content.localScale = new Vector3(next, next, 1f);
            RefreshContentExtent();
            SetScroll(targetX, targetY);
            SetStatus(UiTextKey.Zoom, Mathf.RoundToInt(_zoom * 100f));
        }

        internal void ScrollWheel(float wheelDelta, bool horizontal)
        {
            if (!_ready || _exporting) return;
            if (horizontal) SetScroll(_scrollX - wheelDelta * ScrollStep, _scrollY);
            else SetScroll(_scrollX, _scrollY - wheelDelta * ScrollStep);
        }

        internal void ScrollPage(int direction)
        {
            if (!_ready || _exporting) return;
            SetScroll(_scrollX, _scrollY + direction * _viewportHeight * 0.8f);
        }

        internal void SetScroll(float x, float y)
        {
            _scrollX = Mathf.Clamp(x, 0f, Math.Max(0f, ContentWidth - _viewportWidth));
            _scrollY = Mathf.Clamp(y, 0f, Math.Max(0f, ContentHeight - _viewportHeight));
            if (_content != null)
                _content.anchoredPosition = new Vector2(-_scrollX, _scrollY);
            if (_ready) UpdateChrome(Input.mousePosition);
        }

        internal IEnumerator ExportPng(string path)
        {
            if (!_ready || _exporting || Dragging) yield break;
            _exporting = true;
            SetStatus(UiTextKey.Rendering);

            Color oldShade = _shade.color;
            Color oldViewport = _viewportImage.color;
            Vector2 oldContentPosition = _content.anchoredPosition;
            Vector3 oldContentScale = _content.localScale;
            RectMask2D mask = _viewport.GetComponent<RectMask2D>();
            bool oldMask = mask.enabled;
            Vector2[] oldPositions = new Vector2[_cards.Count];
            for (int i = 0; i < _cards.Count; i++) oldPositions[i] = _cards[i].Rect.anchoredPosition;

            _shade.color = new Color(0.92f, 0.89f, 0.82f, 1f);
            _viewportImage.color = _shade.color;
            _handleLayer.gameObject.SetActive(false);
            for (int i = 0; i < _rowPanels.Count; i++)
                _rowPanels[i].gameObject.SetActive(false);
            mask.enabled = false;
            _content.localScale = Vector3.one;
            _content.anchoredPosition = Vector2.zero;
            for (int i = 0; i < _cards.Count; i++) _cards[i].Group.alpha = 0f;
            Canvas.ForceUpdateCanvases();

            Vector3[] viewportCorners = new Vector3[4];
            _viewport.GetWorldCorners(viewportCorners);
            Vector2 viewport0 = RectTransformUtility.WorldToScreenPoint(null, viewportCorners[0]);
            Vector2 viewport2 = RectTransformUtility.WorldToScreenPoint(null, viewportCorners[2]);
            float scaleX = (viewport2.x - viewport0.x) / _viewportWidth;
            float scaleY = (viewport2.y - viewport0.y) / _viewportHeight;
            int outputWidth = Mathf.CeilToInt(_layoutWidth * scaleX);
            int outputHeight = Mathf.CeilToInt(_layoutHeight * scaleY);
            List<CardCapture> captures = new List<CardCapture>();
            bool failed = outputWidth < 1 || outputHeight < 1 || outputWidth > 12000 ||
                outputHeight > 100000 || (long)outputWidth * outputHeight > 200000000L;
            if (failed) SetStatus(UiTextKey.ExportTooLarge);

            for (int i = 0; i < _cards.Count && !failed; i++)
            {
                Card card = _cards[i];
                card.Group.alpha = 1f;
                card.Rect.anchoredPosition = new Vector2(40f - card.MinX, -40f - card.MaxY);
                SetStatus(UiTextKey.RenderingCard, i + 1, _cards.Count);
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return new WaitForEndOfFrame();
                try
                {
                    captures.Add(CaptureCard(card, scaleX, scaleY));
                }
                catch (Exception exception)
                {
                    _logger.LogError("Could not capture original recipe card " + card.Recipe.Name + ": " + exception);
                    SetStatus(UiTextKey.ExportFailed);
                    failed = true;
                }
                card.Group.alpha = 0f;
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].Rect.anchoredPosition = oldPositions[i];
                _cards[i].Group.alpha = 1f;
            }
            mask.enabled = oldMask;
            _shade.color = oldShade;
            _viewportImage.color = oldViewport;
            _handleLayer.gameObject.SetActive(true);
            for (int i = 0; i < _rowPanels.Count; i++)
                _rowPanels[i].gameObject.SetActive(i < _rows.Count);
            _content.localScale = oldContentScale;
            _content.anchoredPosition = oldContentPosition;
            Canvas.ForceUpdateCanvases();

            if (!failed)
            {
                try
                {
                    SetStatus(UiTextKey.SavingPng);
                    PngStreamWriter.Save(path, outputWidth, outputHeight,
                        delegate(int y, byte[] row) { FillExportScanline(y, row, outputWidth, captures); });
                    int clipboardWidth, clipboardHeight;
                    Color32[] clipboard = BuildClipboardImage(
                        captures, outputWidth, outputHeight, out clipboardWidth, out clipboardHeight);
                    string clipboardError;
                    bool copied = ClipboardImage.Copy(
                        clipboard, clipboardWidth, clipboardHeight, out clipboardError);
                    SetStatus(copied ? UiTextKey.PngCopied : UiTextKey.PngNoClipboard, path);
                    _logger.LogInfo(Status + " (" + outputWidth + "x" + outputHeight + ")");
                    if (!copied) _logger.LogWarning("Clipboard copy failed: " + clipboardError);
                }
                catch (Exception exception)
                {
                    SetStatus(UiTextKey.PngFailed);
                    _logger.LogError("Recipe board export failed: " + exception);
                }
            }
            _exporting = false;
        }

        private static CardCapture CaptureCard(Card card, float scaleX, float scaleY)
        {
            const float pad = 6f;
            Vector3 worldBottomLeft = card.Rect.TransformPoint(
                new Vector3(card.MinX - pad, card.MinY - pad, 0f));
            Vector3 worldTopRight = card.Rect.TransformPoint(
                new Vector3(card.MaxX + pad, card.MaxY + pad, 0f));
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(null, worldBottomLeft);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(null, worldTopRight);
            int screenX = Mathf.Clamp(Mathf.FloorToInt(bottomLeft.x), 0, Screen.width - 1);
            int screenY = Mathf.Clamp(Mathf.FloorToInt(bottomLeft.y), 0, Screen.height - 1);
            int width = Mathf.Clamp(Mathf.CeilToInt(topRight.x) - screenX, 1, Screen.width - screenX);
            int height = Mathf.Clamp(Mathf.CeilToInt(topRight.y) - screenY, 1, Screen.height - screenY);
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture prior = RenderTexture.active;
                try
                {
                    RenderTexture.active = null;
                    texture.ReadPixels(new Rect(screenX, screenY, width, height), 0, 0, false);
                    texture.Apply(false);
                }
                finally { RenderTexture.active = prior; }
                CardCapture capture = new CardCapture();
                capture.Left = Mathf.RoundToInt((card.LayoutX - pad) * scaleX);
                capture.Top = Mathf.RoundToInt((card.LayoutTop - pad) * scaleY);
                capture.Width = width;
                capture.Height = height;
                capture.Pixels = texture.GetPixels32();
                return capture;
            }
            finally { UnityEngine.Object.Destroy(texture); }
        }

        private static void FillExportScanline(
            int y, byte[] scanline, int outputWidth, List<CardCapture> captures)
        {
            scanline[0] = 0;
            for (int x = 0; x < outputWidth; x++)
            {
                int at = 1 + x * 3;
                scanline[at] = 235;
                scanline[at + 1] = 227;
                scanline[at + 2] = 209;
            }
            for (int i = 0; i < captures.Count; i++)
            {
                CardCapture card = captures[i];
                int localTop = y - card.Top;
                if (localTop < 0 || localTop >= card.Height) continue;
                int sourceRow = card.Height - 1 - localTop;
                for (int x = 0; x < card.Width; x++)
                {
                    int targetX = card.Left + x;
                    if (targetX < 0 || targetX >= outputWidth) continue;
                    Color32 color = card.Pixels[sourceRow * card.Width + x];
                    int at = 1 + targetX * 3;
                    scanline[at] = color.r;
                    scanline[at + 1] = color.g;
                    scanline[at + 2] = color.b;
                }
            }
        }

        private static Color32[] BuildClipboardImage(List<CardCapture> captures,
            int fullWidth, int fullHeight, out int width, out int height)
        {
            float scale = Math.Min(1f, Math.Min(4096f / fullWidth, 4096f / fullHeight));
            double scaledPixels = (double)fullWidth * fullHeight * scale * scale;
            if (scaledPixels > 12000000.0)
                scale *= (float)Math.Sqrt(12000000.0 / scaledPixels);
            width = Math.Max(1, Mathf.RoundToInt(fullWidth * scale));
            height = Math.Max(1, Mathf.RoundToInt(fullHeight * scale));
            Color32[] result = new Color32[width * height];
            Color32 paper = new Color32(235, 227, 209, 255);
            for (int i = 0; i < result.Length; i++) result[i] = paper;

            for (int i = 0; i < captures.Count; i++)
            {
                CardCapture card = captures[i];
                int left = Math.Max(0, Mathf.FloorToInt(card.Left * scale));
                int top = Math.Max(0, Mathf.FloorToInt(card.Top * scale));
                int right = Math.Min(width, Mathf.CeilToInt((card.Left + card.Width) * scale));
                int bottom = Math.Min(height, Mathf.CeilToInt((card.Top + card.Height) * scale));
                for (int y = top; y < bottom; y++)
                {
                    int sourceTop = Mathf.Clamp(Mathf.FloorToInt(y / scale) - card.Top, 0, card.Height - 1);
                    int sourceY = card.Height - 1 - sourceTop;
                    int targetY = height - 1 - y;
                    for (int x = left; x < right; x++)
                    {
                        int sourceX = Mathf.Clamp(Mathf.FloorToInt(x / scale) - card.Left, 0, card.Width - 1);
                        result[targetY * width + x] = card.Pixels[sourceY * card.Width + sourceX];
                    }
                }
            }
            return result;
        }

        internal void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
