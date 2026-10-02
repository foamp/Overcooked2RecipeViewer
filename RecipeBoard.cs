using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Overcooked2RecipePreview
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
        private readonly RectTransform _content;
        private readonly ManualLogSource _logger;
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
        private Card _dragged;
        private Card _placeholderCard;
        private RectTransform _placeholderRect;
        private List<Card> _dragOriginalRow;
        private int _dragOriginalIndex;
        private Vector2 _dragOffset;
        private bool _ready;
        private bool _exporting;
        private bool _needsExtentRefresh;
        private string _status = string.Empty;

        internal float ScrollY { get { return _scrollY; } }
        internal float ViewportHeight { get { return _viewportHeight; } }
        internal float ContentHeight { get { return _contentHeight; } }
        internal float ScrollX { get { return _scrollX; } }
        internal float ViewportWidth { get { return _viewportWidth; } }
        internal float ContentWidth { get { return _contentWidth; } }
        internal bool Ready { get { return _ready; } }
        internal bool Exporting { get { return _exporting; } }
        internal bool Dragging { get { return _dragged != null; } }
        internal string Status { get { return _status; } }

        internal RecipeBoard(IList<RecipeInfo> recipes, ManualLogSource logger)
        {
            _logger = logger;
            try
            {
                RecipeFlowGUI flow = UnityEngine.Object.FindObjectOfType<RecipeFlowGUI>();
                if (flow == null) throw new InvalidOperationException("RecipeFlowGUI is not ready.");
                FieldInfo prefabField = AccessTools.Field(typeof(RecipeFlowGUI), "m_recipeWidgetPrefab");
                RecipeWidgetUIController prefab = prefabField == null
                    ? null : prefabField.GetValue(flow) as RecipeWidgetUIController;
                if (prefab == null) throw new InvalidOperationException("Original order-card prefab was not found.");

                _root = new GameObject("RecipePreview_Board", typeof(RectTransform),
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
                _shade.color = new Color(0f, 0f, 0f, 0.78f);
                _shade.raycastTarget = true;

                _viewport = CreateRect("Viewport", backdrop);
                Stretch(_viewport, 24f, 52f, 36f, 48f);
                _viewport.gameObject.AddComponent<RectMask2D>();
                Canvas.ForceUpdateCanvases();
                _viewportWidth = Math.Max(320f, _viewport.rect.width);
                _viewportHeight = Math.Max(300f, _viewport.rect.height);

                _content = CreateRect("Cards", _viewport);
                _content.anchorMin = new Vector2(0f, 1f);
                _content.anchorMax = new Vector2(0f, 1f);
                _content.pivot = new Vector2(0f, 1f);
                _contentWidth = _viewportWidth;
                _contentHeight = _viewportHeight;
                _content.sizeDelta = new Vector2(_contentWidth, _contentHeight);

                RectTransform prefabRect = prefab.transform as RectTransform;
                Vector2 nativeSize = prefabRect == null ? new Vector2(100f, 100f) : prefabRect.rect.size;
                if (nativeSize.x < 1f || nativeSize.y < 1f) nativeSize = new Vector2(100f, 100f);

                for (int i = 0; i < recipes.Count; i++)
                {
                    GameObject clone = GameUtils.InstantiateUIController(prefab.gameObject, _content);
                    clone.name = "RecipePreview_" + recipes[i].Name;
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
                    _cards.Add(card);
                }

                _placeholderRect = CreateRect("DropPlaceholder", _content);
                Image placeholderImage = _placeholderRect.gameObject.AddComponent<Image>();
                placeholderImage.color = new Color(0.28f, 0.72f, 0.96f, 0.22f);
                placeholderImage.raycastTarget = false;
                Outline placeholderBorder = _placeholderRect.gameObject.AddComponent<Outline>();
                placeholderBorder.effectColor = new Color(0.34f, 0.83f, 1f, 0.9f);
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
                fill.color = new Color(0.55f, 0.78f, 0.97f, 0.18f);
                fill.raycastTarget = false;
                Outline border = frame.gameObject.AddComponent<Outline>();
                border.effectColor = new Color(0.36f, 0.82f, 1f, 0.85f);
                border.effectDistance = new Vector2(3f, 3f);
                frame.SetAsFirstSibling();
                card.DragFrame = frame.gameObject;
                frame.gameObject.SetActive(false);
            }
            BuildInitialRows();
            LayoutRows();
            _ready = true;
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

        private void BuildInitialRows()
        {
            _rows.Clear();
            List<Card> row = new List<Card>();
            float used = BoardPadding;
            for (int i = 0; i < _cards.Count; i++)
            {
                Card card = _cards[i];
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
                widest = Math.Max(widest, x - HorizontalGap + BoardPadding);
                top += rowHeight + VerticalGap;
            }
            _layoutHeight = _rows.Count == 0 ? BoardPadding * 2f :
                top - VerticalGap + BoardPadding + BottomSafetyPadding;
            _layoutWidth = widest;
            _contentWidth = Math.Max(_viewportWidth, _layoutWidth);
            _contentHeight = Math.Max(_viewportHeight, _layoutHeight);
            _content.sizeDelta = new Vector2(_contentWidth, _contentHeight);
            _needsExtentRefresh = true;
            SetScroll(_scrollX, _scrollY);
        }

        private void RefreshContentExtent()
        {
            _needsExtentRefresh = false;
            if (_dragged != null || _exporting || _root == null) return;
            Canvas.ForceUpdateCanvases();
            _viewportWidth = Math.Max(320f, _viewport.rect.width);
            _viewportHeight = Math.Max(300f, _viewport.rect.height);
            float actualBottom = 0f;
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
                        actualBottom = Math.Max(actualBottom,
                            -_content.InverseTransformPoint(corners[k]).y);
                }
            }
            // Base the maximum scroll on rendered artwork, not just the
            // prefab's early layout measurements. Leave enough travel to put
            // the final row in the upper half of the viewport if necessary.
            float lastRowTop = _rowTops.Count == 0 ? 0f : _rowTops[_rowTops.Count - 1];
            float scrollableBottom = Math.Max(actualBottom + 80f,
                lastRowTop + _viewportHeight * 0.55f);
            _contentHeight = Math.Max(_viewportHeight,
                Math.Max(_layoutHeight, scrollableBottom));
            _content.sizeDelta = new Vector2(_contentWidth, _contentHeight);
            SetScroll(_scrollX, _scrollY);
            _logger.LogInfo(string.Format(
                "Recipe scroll bounds: visible={0:0}, renderedBottom={1:0}, content={2:0}, maximum={3:0}.",
                _viewportHeight, actualBottom, _contentHeight,
                Math.Max(0f, _contentHeight - _viewportHeight)));
        }

        internal void UpdatePointer()
        {
            if (!_ready || _exporting || _root == null) return;
            if (_needsExtentRefresh && _dragged == null) RefreshContentExtent();
            Vector2 mouse = Input.mousePosition;
            if (_dragged == null && Input.GetMouseButtonDown(0) &&
                RectTransformUtility.RectangleContainsScreenPoint(_viewport, mouse, null))
            {
                for (int i = _cards.Count - 1; i >= 0; i--)
                {
                    if (!ContainsPoint(_cards[i], mouse)) continue;
                    _dragged = _cards[i];
                    Vector2 local;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(_content, mouse, null, out local);
                    _dragOffset = _dragged.Rect.anchoredPosition - local;
                    for (int row = 0; row < _rows.Count; row++)
                    {
                        int index = _rows[row].IndexOf(_dragged);
                        if (index < 0) continue;
                        _dragOriginalRow = _rows[row];
                        _dragOriginalIndex = index;
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

            if (_dragged == null) return;
            if (Input.GetMouseButton(0))
            {
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_content, mouse, null, out local);
                _dragged.Rect.anchoredPosition = local + _dragOffset;
                MovePlaceholder(local);
                Vector2 viewportLocal;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, mouse, null, out viewportLocal);
                float topDistance = _viewport.rect.yMax - viewportLocal.y;
                if (topDistance < 35f) SetScroll(_scrollX, _scrollY - 220f * Time.unscaledDeltaTime);
                if (topDistance > _viewportHeight - 35f)
                    SetScroll(_scrollX, _scrollY + 220f * Time.unscaledDeltaTime);
            }
            if (Input.GetMouseButtonUp(0)) DropCard(mouse);
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
            _dragged = null;
            if (dragged == null) return;
            if (_placeholderCard != null)
            {
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
                if (!RectTransformUtility.RectangleContainsScreenPoint(_viewport, screenPoint, null))
                {
                    destination = _dragOriginalRow;
                    insertion = _dragOriginalIndex;
                }
                if (destination == null) destination = _dragOriginalRow;
                if (destination == null) destination = new List<Card>();
                if (!_rows.Contains(destination)) _rows.Add(destination);
                destination.Insert(Math.Min(insertion, destination.Count), dragged);
                for (int row = _rows.Count - 1; row >= 0; row--)
                    if (_rows[row].Count == 0) _rows.RemoveAt(row);
            }
            _placeholderCard = null;
            _placeholderRect.gameObject.SetActive(false);
            dragged.Group.alpha = 1f;
            dragged.DragFrame.SetActive(false);
            LayoutRows();
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
            _status = "Grouped " + _cards.Count + " recipes into " + _rows.Count + " categories.";
            _logger.LogInfo(_status);
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

        internal void ScrollWheel(float wheelDelta, bool horizontal)
        {
            if (horizontal) SetScroll(_scrollX - wheelDelta * ScrollStep, _scrollY);
            else SetScroll(_scrollX, _scrollY - wheelDelta * ScrollStep);
        }

        internal void ScrollPage(int direction)
        {
            SetScroll(_scrollX, _scrollY + direction * _viewportHeight * 0.8f);
        }

        internal void SetScroll(float x, float y)
        {
            _scrollX = Mathf.Clamp(x, 0f, Math.Max(0f, _contentWidth - _viewportWidth));
            _scrollY = Mathf.Clamp(y, 0f, Math.Max(0f, _contentHeight - _viewportHeight));
            if (_content != null)
                _content.anchoredPosition = new Vector2(-_scrollX, _scrollY);
        }

        internal IEnumerator ExportPng(string path)
        {
            if (!_ready || _exporting || _dragged != null) yield break;
            _exporting = true;
            _status = "Rendering original order cards...";

            Color oldShade = _shade.color;
            Vector2 oldContentPosition = _content.anchoredPosition;
            RectMask2D mask = _viewport.GetComponent<RectMask2D>();
            bool oldMask = mask.enabled;
            Vector2[] oldPositions = new Vector2[_cards.Count];
            for (int i = 0; i < _cards.Count; i++) oldPositions[i] = _cards[i].Rect.anchoredPosition;

            _shade.color = new Color(0.92f, 0.89f, 0.82f, 1f);
            mask.enabled = false;
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
            if (failed) _status = "Board dimensions are too large to export safely.";

            for (int i = 0; i < _cards.Count && !failed; i++)
            {
                Card card = _cards[i];
                card.Group.alpha = 1f;
                card.Rect.anchoredPosition = new Vector2(40f - card.MinX, -40f - card.MaxY);
                _status = "Rendering card " + (i + 1) + "/" + _cards.Count + "...";
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
                    _status = "Image export failed. See BepInEx log.";
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
            _content.anchoredPosition = oldContentPosition;
            Canvas.ForceUpdateCanvases();

            if (!failed)
            {
                try
                {
                    _status = "Saving full-length PNG...";
                    PngStreamWriter.Save(path, outputWidth, outputHeight,
                        delegate(int y, byte[] row) { FillExportScanline(y, row, outputWidth, captures); });
                    int clipboardWidth, clipboardHeight;
                    Color32[] clipboard = BuildClipboardImage(
                        captures, outputWidth, outputHeight, out clipboardWidth, out clipboardHeight);
                    string clipboardError;
                    bool copied = ClipboardImage.Copy(
                        clipboard, clipboardWidth, clipboardHeight, out clipboardError);
                    _status = copied
                        ? "Saved full PNG and copied image: " + path
                        : "Saved full PNG (clipboard unavailable): " + path;
                    _logger.LogInfo(_status + " (" + outputWidth + "x" + outputHeight + ")");
                    if (!copied) _logger.LogWarning("Clipboard copy failed: " + clipboardError);
                }
                catch (Exception exception)
                {
                    _status = "PNG save failed. See BepInEx log.";
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
