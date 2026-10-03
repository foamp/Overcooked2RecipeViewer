using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Overcooked2RecipePreview
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class RecipePreviewPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "io.github.overcooked2.recipepreview";
        public const string PluginName = "Overcooked 2 Recipe Preview";
        public const string PluginVersion = "0.8.3";

        private static RecipePreviewPlugin s_instance;

        private Harmony _harmony;
        private ConfigEntry<KeyboardShortcut> _toggleShortcut;
        private readonly ConfigurationManagerAttributes _shortcutLabels = new ConfigurationManagerAttributes();
        private float _nextLanguageCheck;
        private Font _chineseFont;
        private Font _englishFont;
        private bool _uiFontApplied;
        private bool _uiFontChinese;
        private readonly List<RecipeInfo> _recipes = new List<RecipeInfo>();
        private LevelConfigBase _currentLevelConfig;
        private LevelConfigBase _lastLevelConfig;
        private string _lastSceneName = string.Empty;
        private float _lastCaptureAt = -100f;
        private bool _overlayVisible;
        private RecipeBoard _nativeOverlay;
        private float _nextToggleTime;
        private bool _presetOpen;
        private Vector2 _presetScroll;
        private GUIStyle _buttonStyle;
        private GUIStyle _selectedStyle;
        private GUIStyle _menuFrameStyle;
        private GUIStyle _menuBoxStyle;
        private GUIStyle _headingStyle;
        private GUIStyle _statusStyle;
        private readonly List<Texture2D> _uiTextures = new List<Texture2D>();

        internal static RecipePreviewPlugin Instance
        {
            get { return s_instance; }
        }

        private void Awake()
        {
            s_instance = this;
            RefreshUiLanguage();
            _toggleShortcut = Config.Bind(
                "Keyboard shortcuts",
                "Toggle all recipe images",
                new KeyboardShortcut(KeyCode.Insert),
                new ConfigDescription(RecipeUiText.Text(UiTextKey.ShortcutDescription), null, _shortcutLabels));
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded. Toggle key: " + _toggleShortcut.Value);
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup >= _nextLanguageCheck) RefreshUiLanguage();
            if (_toggleShortcut != null &&
                _toggleShortcut.Value.IsDown() &&
                Time.realtimeSinceStartup >= _nextToggleTime)
            {
                _nextToggleTime = Time.realtimeSinceStartup + 0.35f;
                ToggleRecipeOverlay();
            }

            if (_overlayVisible && Input.GetKeyDown(KeyCode.PageDown))
            {
                if (_nativeOverlay != null) _nativeOverlay.ScrollPage(1);
            }
            if (_overlayVisible && Input.GetKeyDown(KeyCode.PageUp))
            {
                if (_nativeOverlay != null) _nativeOverlay.ScrollPage(-1);
            }
            if (_overlayVisible && !_presetOpen && _nativeOverlay != null && Input.mouseScrollDelta.y != 0f)
            {
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    _nativeOverlay.ZoomWheel(Input.mouseScrollDelta.y, Input.mousePosition);
                else
                    _nativeOverlay.ScrollWheel(Input.mouseScrollDelta.y,
                        Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            }
            if (_overlayVisible && _nativeOverlay != null) _nativeOverlay.UpdatePointer(_presetOpen);
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }

            HideRecipeOverlay();
            for (int i = 0; i < _uiTextures.Count; i++)
                if (_uiTextures[i] != null) UnityEngine.Object.Destroy(_uiTextures[i]);
            _uiTextures.Clear();
            if (_chineseFont != null) UnityEngine.Object.Destroy(_chineseFont);

            if (ReferenceEquals(s_instance, this))
            {
                s_instance = null;
            }
        }

        internal void Capture(int levelIndex, string sceneName, LevelConfigBase levelConfig)
        {
            try
            {
                if (levelConfig == null)
                {
                    Logger.LogDebug("Recipe preview hook reached without a level config: " + sceneName);
                    return;
                }

                float now = Time.realtimeSinceStartup;
                if (ReferenceEquals(_lastLevelConfig, levelConfig) &&
                    string.Equals(_lastSceneName, sceneName, StringComparison.Ordinal) &&
                    now - _lastCaptureAt < 5f)
                {
                    return;
                }

                _lastLevelConfig = levelConfig;
                _currentLevelConfig = levelConfig;
                _lastSceneName = sceneName ?? string.Empty;
                _lastCaptureAt = now;
                _recipes.Clear();
                _recipes.AddRange(RecipeCollector.Collect(levelConfig, Logger));

                string configName = levelConfig.name;
                string levelText = levelIndex < 0 ? "Level index: <unknown>" : "Level index: " + levelIndex;
                string levelDescription = string.Format(
                    "{0} | Scene: {1} | Config: {2}",
                    levelText,
                    string.IsNullOrEmpty(sceneName) ? "<unknown>" : sceneName,
                    string.IsNullOrEmpty(configName) ? "<unnamed>" : configName);

                Logger.LogInfo("=== Recipe preview ===");
                Logger.LogInfo(levelDescription);
                Logger.LogInfo("Possible recipes: " + _recipes.Count);
                for (int i = 0; i < _recipes.Count; i++)
                {
                    RecipeInfo recipe = _recipes[i];
                    Logger.LogInfo(string.Format(
                        "{0}. {1} | uid={2} | type={3}",
                        i + 1,
                        recipe.Name,
                        recipe.UniqueId,
                        recipe.NodeType));
                }
            }
            catch (Exception exception)
            {
                Logger.LogError("Failed to collect recipes before loading the level.");
                Logger.LogError(exception);
            }
        }

        private void ToggleRecipeOverlay()
        {
            if (_overlayVisible)
            {
                if (_nativeOverlay != null && _nativeOverlay.Exporting) return;
                HideRecipeOverlay();
                return;
            }

            ShowRecipeOverlay();
        }

        private void ShowRecipeOverlay()
        {
            LevelConfigBase levelConfig = GameUtils.GetLevelConfig();
            if (levelConfig == null)
            {
                levelConfig = _currentLevelConfig;
            }

            if (levelConfig == null)
            {
                Logger.LogWarning("Cannot show recipe images: no current level configuration is available.");
                return;
            }

            _currentLevelConfig = levelConfig;
            _recipes.Clear();
            _recipes.AddRange(RecipeCollector.Collect(levelConfig, Logger));
            if (_recipes.Count == 0)
            {
                Logger.LogWarning("Cannot show recipe images: the current level has no recipe definitions.");
                return;
            }

            try
            {
                string levelKey = levelConfig.GetType().FullName + "|" + levelConfig.name;
                _nativeOverlay = new RecipeBoard(_recipes, levelKey, Logger);
                StartCoroutine(_nativeOverlay.FinalizeCards());
                _overlayVisible = true;
                Logger.LogInfo("Showing " + _recipes.Count + " draggable original recipe cards.");
            }
            catch (Exception exception)
            {
                if (_nativeOverlay != null) _nativeOverlay.Destroy();
                _nativeOverlay = null;
                Logger.LogError("Could not create the original recipe cards: " + exception);
            }
        }

        private void RefreshUiLanguage()
        {
            _nextLanguageCheck = Time.realtimeSinceStartup + 0.5f;
            RecipeUiText.RefreshLanguage(Logger);
            _shortcutLabels.DispName = RecipeUiText.Text(UiTextKey.ShortcutName);
            _shortcutLabels.Category = RecipeUiText.Text(UiTextKey.ShortcutCategory);
            _shortcutLabels.Description = RecipeUiText.Text(UiTextKey.ShortcutDescription);
        }

        private void ApplyUiFont()
        {
            bool chinese = RecipeUiText.Chinese;
            if (_uiFontApplied && _uiFontChinese == chinese) return;
            if (chinese && _chineseFont == null)
            {
                try
                {
                    _chineseFont = Font.CreateDynamicFontFromOSFont(
                        new string[] { "Microsoft YaHei", "SimHei", "Arial Unicode MS" }, 14);
                }
                catch (Exception exception)
                {
                    Logger.LogWarning("Could not create Chinese UI font; using default font: " + exception);
                }
            }
            Font font = chinese && _chineseFont != null ? _chineseFont : _englishFont;
            _buttonStyle.font = font;
            _selectedStyle.font = font;
            _headingStyle.font = font;
            _statusStyle.font = font;
            _uiFontChinese = chinese;
            _uiFontApplied = true;
        }

        private Texture2D Swatch(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0, 0, color);
            texture.Apply();
            _uiTextures.Add(texture);
            return texture;
        }

        private void EnsureUiStyles()
        {
            if (_buttonStyle != null) return;
            Texture2D normal = Swatch(new Color(0.15f, 0.28f, 0.27f, 1f));
            Texture2D hover = Swatch(new Color(0.25f, 0.41f, 0.37f, 1f));
            Texture2D pressed = Swatch(new Color(0.55f, 0.36f, 0.16f, 1f));
            Texture2D selected = Swatch(new Color(0.56f, 0.41f, 0.20f, 1f));
            Texture2D frame = Swatch(new Color(0.82f, 0.66f, 0.36f, 1f));
            Texture2D menu = Swatch(new Color(0.085f, 0.18f, 0.18f, 0.98f));

            _buttonStyle = new GUIStyle(GUI.skin.button);
            _englishFont = _buttonStyle.font;
            _buttonStyle.border = new RectOffset(0, 0, 0, 0);
            _buttonStyle.normal.background = normal;
            _buttonStyle.hover.background = hover;
            _buttonStyle.active.background = pressed;
            _buttonStyle.normal.textColor = new Color(1f, 0.94f, 0.77f);
            _buttonStyle.hover.textColor = Color.white;
            _buttonStyle.active.textColor = Color.white;
            _buttonStyle.fontSize = 14;
            _buttonStyle.fontStyle = FontStyle.Bold;
            _buttonStyle.alignment = TextAnchor.MiddleCenter;
            _buttonStyle.padding = new RectOffset(8, 8, 3, 3);
            _selectedStyle = new GUIStyle(_buttonStyle);
            _selectedStyle.normal.background = selected;
            _selectedStyle.normal.textColor = Color.white;
            _menuFrameStyle = new GUIStyle(GUI.skin.box);
            _menuFrameStyle.border = new RectOffset(0, 0, 0, 0);
            _menuFrameStyle.normal.background = frame;
            _menuBoxStyle = new GUIStyle(_menuFrameStyle);
            _menuBoxStyle.normal.background = menu;
            _headingStyle = new GUIStyle(GUI.skin.label);
            _headingStyle.normal.textColor = new Color(1f, 0.94f, 0.77f);
            _headingStyle.fontSize = 14;
            _headingStyle.fontStyle = FontStyle.Bold;
            _statusStyle = new GUIStyle(_headingStyle);
            _statusStyle.fontSize = 12;
            _statusStyle.normal.textColor = new Color(1f, 0.79f, 0.42f);
        }
        private void OnGUI()
        {
            if (!_overlayVisible || _nativeOverlay == null) return;

            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            GUI.depth = -1000;

            try
            {
                EnsureUiStyles();
                ApplyUiFont();
                GUI.color = Color.white;
                GUI.Label(new Rect(24f, 9f, Screen.width - 840f, 24f),
                    RecipeUiText.Text(UiTextKey.Heading, _recipes.Count,
                        Mathf.RoundToInt(_nativeOverlay.Zoom * 100f)),
                    _headingStyle);
                if (!string.IsNullOrEmpty(_nativeOverlay.Status))
                    GUI.Label(new Rect(24f, 28f, Screen.width - 50f, 24f), _nativeOverlay.Status, _statusStyle);
                GUI.enabled = _nativeOverlay.Ready && !_nativeOverlay.Exporting && !_nativeOverlay.Dragging;
                Rect presetButton = new Rect(Screen.width - 820f, 10f, 205f, 30f);
                IList<string> savedLayouts = _nativeOverlay.SavedLayouts;
                bool hasDraft = _nativeOverlay.HasDraft;
                int entryCount = 4 + (hasDraft ? 1 : 0) + savedLayouts.Count;
                Rect presetMenu = new Rect(presetButton.x, 43f, 205f,
                    Math.Min(350f, 10f + entryCount * 34f));
                if (GUI.Button(presetButton, RecipeUiText.Text(UiTextKey.PresetButton, _nativeOverlay.SortLabel),
                    _buttonStyle))
                {
                    _presetOpen = !_presetOpen;
                    _presetScroll = Vector2.zero;
                }
                if (_presetOpen)
                {
                    GUI.Box(presetMenu, "", _menuFrameStyle);
                    GUI.Box(new Rect(presetMenu.x + 2f, presetMenu.y + 2f,
                        presetMenu.width - 4f, presetMenu.height - 4f), "", _menuBoxStyle);
                    Rect scrollArea = new Rect(presetMenu.x + 4f, presetMenu.y + 4f,
                        presetMenu.width - 8f, presetMenu.height - 8f);
                    _presetScroll = GUI.BeginScrollView(scrollArea, _presetScroll,
                        new Rect(0f, 0f, presetMenu.width - 30f, entryCount * 34f));
                    string[] labels = {
                        RecipeUiText.PresetLabel(RecipeBoard.SortPreset.Original, true),
                        RecipeUiText.PresetLabel(RecipeBoard.SortPreset.AutoArrange, true),
                        RecipeUiText.PresetLabel(RecipeBoard.SortPreset.Name, true),
                        RecipeUiText.PresetLabel(RecipeBoard.SortPreset.Cookware, true) };
                    int entry = 0;
                    for (int i = 0; i < labels.Length; i++, entry++)
                    {
                        if (GUI.Button(new Rect(1f, entry * 34f, presetMenu.width - 30f, 32f),
                            labels[i],
                            _nativeOverlay.UsingBuiltInPreset &&
                            _nativeOverlay.CurrentPreset == (RecipeBoard.SortPreset)i
                                ? _selectedStyle : _buttonStyle))
                        {
                            _nativeOverlay.ApplySortPreset((RecipeBoard.SortPreset)i);
                            _presetOpen = false;
                        }
                    }
                    if (hasDraft)
                    {
                        if (GUI.Button(new Rect(1f, entry * 34f, presetMenu.width - 30f, 32f),
                            RecipeUiText.Text(UiTextKey.CustomLastEdit),
                            _nativeOverlay.UsingDraft
                                ? _selectedStyle : _buttonStyle))
                        {
                            _nativeOverlay.ApplyDraftLayout();
                            _presetOpen = false;
                        }
                        entry++;
                    }
                    for (int i = 0; i < savedLayouts.Count; i++, entry++)
                    {
                        string name = savedLayouts[i];
                        if (GUI.Button(new Rect(1f, entry * 34f, presetMenu.width - 64f, 32f),
                            RecipeUiText.LayoutName(name),
                            _nativeOverlay.ActiveSavedLayout == name
                                ? _selectedStyle : _buttonStyle))
                        {
                            _nativeOverlay.ApplySavedLayout(name);
                            _presetOpen = false;
                        }
                        if (GUI.Button(new Rect(presetMenu.width - 60f, entry * 34f, 29f, 32f),
                            new GUIContent("X", RecipeUiText.Text(UiTextKey.DeleteTooltip)), _buttonStyle))
                        {
                            _nativeOverlay.DeleteSavedLayout(name);
                            break;
                        }
                    }
                    GUI.EndScrollView();
                    if (Event.current.type == EventType.MouseDown &&
                        !presetButton.Contains(Event.current.mousePosition) &&
                        !presetMenu.Contains(Event.current.mousePosition))
                        _presetOpen = false;
                }
                if (GUI.Button(new Rect(Screen.width - 605f, 10f, 150f, 30f),
                    RecipeUiText.Text(UiTextKey.SaveLayout), _buttonStyle))
                    _nativeOverlay.SaveCurrentLayout();
                if (GUI.Button(new Rect(Screen.width - 445f, 10f, 118f, 30f), RecipeUiText.Text(UiTextKey.SavePng), _buttonStyle))
                {
                    string fileName = "recipes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                        Guid.NewGuid().ToString("N").Substring(0, 6) + ".png";
                    string exportPath = Path.Combine(Paths.BepInExRootPath,
                        Path.Combine("RecipePreviewExports", fileName));
                    StartCoroutine(_nativeOverlay.ExportPng(exportPath));
                }
                if (GUI.Button(new Rect(Screen.width - 319f, 10f, 185f, 30f), RecipeUiText.Text(UiTextKey.OpenPngFolder), _buttonStyle))
                {
                    string folder = Path.Combine(Paths.BepInExRootPath, "RecipePreviewExports");
                    Directory.CreateDirectory(folder);
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
                }
                GUI.enabled = previousEnabled && !_nativeOverlay.Exporting;
                float scrollbarHeight = Math.Max(50f, Screen.height - 105f);
                GUI.color = new Color(0.91f, 0.75f, 0.43f, 1f);
                float newScroll = GUI.VerticalScrollbar(
                    new Rect(Screen.width - 25f, 52f, 18f, scrollbarHeight),
                    _nativeOverlay.ScrollY,
                    _nativeOverlay.ViewportHeight,
                    0f,
                    _nativeOverlay.ContentHeight);
                if (Math.Abs(newScroll - _nativeOverlay.ScrollY) > 0.1f)
                    _nativeOverlay.SetScroll(_nativeOverlay.ScrollX, newScroll);
                if (_nativeOverlay.ContentWidth > _nativeOverlay.ViewportWidth + 1f)
                {
                    float newScrollX = GUI.HorizontalScrollbar(
                        new Rect(24f, Screen.height - 29f, Screen.width - 64f, 18f),
                        _nativeOverlay.ScrollX,
                        _nativeOverlay.ViewportWidth,
                        0f,
                        _nativeOverlay.ContentWidth);
                    if (Math.Abs(newScrollX - _nativeOverlay.ScrollX) > 0.1f)
                        _nativeOverlay.SetScroll(newScrollX, _nativeOverlay.ScrollY);
                }
                GUI.color = Color.white;
                if (GUI.Button(new Rect(Screen.width - 126f, 10f, 104f, 30f), RecipeUiText.Text(UiTextKey.Close), _buttonStyle))
                    HideRecipeOverlay();
                if (_presetOpen && !string.IsNullOrEmpty(GUI.tooltip))
                    GUI.Label(new Rect(24f, Screen.height - 51f, Screen.width - 70f, 20f),
                        GUI.tooltip, _statusStyle);
            }
            catch (Exception exception)
            {
                Logger.LogError("Recipe preview controls failed: " + exception);
                HideRecipeOverlay();
            }
            finally
            {
                GUI.enabled = previousEnabled;
                GUI.color = previousColor;
                GUI.depth = previousDepth;
            }
        }

        private void HideRecipeOverlay()
        {
            if (_overlayVisible)
            {
                _overlayVisible = false;
                _presetOpen = false;
                if (_nativeOverlay != null) _nativeOverlay.Destroy();
                _nativeOverlay = null;
                Logger.LogInfo("Recipe card overlay hidden.");
            }
        }
    }

    internal sealed class RecipeInfo
    {
        internal RecipeInfo(OrderDefinitionNode node, string name, int uniqueId, string nodeType)
        {
            Node = node;
            Name = name;
            UniqueId = uniqueId;
            NodeType = nodeType;
        }

        internal OrderDefinitionNode Node { get; private set; }
        internal string Name { get; private set; }
        internal int UniqueId { get; private set; }
        internal string NodeType { get; private set; }
    }

    internal static class RecipeCollector
    {
        internal static List<RecipeInfo> Collect(LevelConfigBase levelConfig, ManualLogSource logger)
        {
            List<RecipeInfo> result = new List<RecipeInfo>();
            HashSet<int> seenInstanceIds = new HashSet<int>();

            if (levelConfig == null)
            {
                return result;
            }

            // This is the game's own polymorphic API. Campaign and Horde configs
            // override it, and dynamic campaign configs aggregate every phase.
            try
            {
                AddNodes(levelConfig.GetAllRecipes(), result, seenInstanceIds);
            }
            catch (Exception exception)
            {
                logger.LogWarning("LevelConfigBase.GetAllRecipes() failed; using field fallbacks.");
                logger.LogWarning(exception.Message);
            }

            // Field fallbacks make scripted/manual rounds and unusual multi-round
            // configs visible even where the game's GetAllRecipes() only returns
            // the first/base round pool.
            KitchenLevelConfigBase kitchenConfig = levelConfig as KitchenLevelConfigBase;
            if (kitchenConfig != null)
            {
                try
                {
                    AddRound(kitchenConfig.GetRoundData(), result, seenInstanceIds);
                }
                catch (Exception exception)
                {
                    logger.LogWarning("Could not inspect the selected kitchen round: " + exception.Message);
                }
            }

            CampaignLevelConfig campaignConfig = levelConfig as CampaignLevelConfig;
            if (campaignConfig != null && campaignConfig.m_rounds != null)
            {
                for (int i = 0; i < campaignConfig.m_rounds.Length; i++)
                {
                    AddRound(campaignConfig.m_rounds[i], result, seenInstanceIds);
                }
            }

            ScriptedCampaignLevelConfig scriptedConfig = levelConfig as ScriptedCampaignLevelConfig;
            if (scriptedConfig != null && scriptedConfig.m_rounds != null)
            {
                for (int i = 0; i < scriptedConfig.m_rounds.Length; i++)
                {
                    AddRound(scriptedConfig.m_rounds[i], result, seenInstanceIds);
                }
            }

            SinglePlayerLevelConfig singlePlayerConfig = levelConfig as SinglePlayerLevelConfig;
            if (singlePlayerConfig != null)
            {
                AddEntries(singlePlayerConfig.RecipeOrder, result, seenInstanceIds);
            }

            return result;
        }

        private static void AddRound(
            RoundData round,
            List<RecipeInfo> result,
            HashSet<int> seenInstanceIds)
        {
            if (round == null)
            {
                return;
            }

            AddRecipeList(round.m_recipes, result, seenInstanceIds);

            ScriptedRoundData scriptedRound = round as ScriptedRoundData;
            if (scriptedRound != null)
            {
                AddEntries(scriptedRound.m_manualOrder, result, seenInstanceIds);
            }

            DynamicRoundData dynamicRound = round as DynamicRoundData;
            if (dynamicRound != null && dynamicRound.Phases != null)
            {
                for (int i = 0; i < dynamicRound.Phases.Length; i++)
                {
                    DynamicRoundData.Phase phase = dynamicRound.Phases[i];
                    if (phase != null)
                    {
                        AddRecipeList(phase.Recipes, result, seenInstanceIds);
                    }
                }
            }
        }

        private static void AddRecipeList(
            RecipeList recipeList,
            List<RecipeInfo> result,
            HashSet<int> seenInstanceIds)
        {
            if (recipeList != null)
            {
                AddEntries(recipeList.m_recipes, result, seenInstanceIds);
            }
        }

        private static void AddEntries(
            RecipeList.Entry[] entries,
            List<RecipeInfo> result,
            HashSet<int> seenInstanceIds)
        {
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                RecipeList.Entry entry = entries[i];
                if (entry != null)
                {
                    AddNode(entry.m_order, result, seenInstanceIds);
                }
            }
        }

        private static void AddNodes(
            IEnumerable<OrderDefinitionNode> nodes,
            List<RecipeInfo> result,
            HashSet<int> seenInstanceIds)
        {
            if (nodes == null)
            {
                return;
            }

            foreach (OrderDefinitionNode node in nodes)
            {
                AddNode(node, result, seenInstanceIds);
            }
        }

        private static void AddNode(
            OrderDefinitionNode node,
            List<RecipeInfo> result,
            HashSet<int> seenInstanceIds)
        {
            if (node == null)
            {
                return;
            }

            int instanceId = node.GetInstanceID();
            if (!seenInstanceIds.Add(instanceId))
            {
                return;
            }

            string displayName = string.IsNullOrEmpty(node.name) ? "<unnamed recipe>" : node.name;
            result.Add(new RecipeInfo(node, displayName, node.m_uID, node.GetType().Name));
        }
    }

    [HarmonyPatch]
    internal static class LoadKitchenPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo[] methods = typeof(PlayerLobbyFlowroutine).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "LoadKitchen" &&
                    parameters.Length == 4 &&
                    parameters[0].ParameterType == typeof(int) &&
                    parameters[1].ParameterType == typeof(string) &&
                    parameters[3].ParameterType == typeof(GameSession.GameLevelSettings))
                {
                    return method;
                }
            }

            throw new MissingMethodException(
                "Could not find PlayerLobbyFlowroutine.LoadKitchen(int, string, Sprite, GameLevelSettings).");
        }

        private static void Prefix(object[] __args)
        {
            RecipePreviewPlugin plugin = RecipePreviewPlugin.Instance;
            if (plugin == null || __args == null || __args.Length < 4)
            {
                return;
            }

            int levelIndex = (int)__args[0];
            string sceneName = __args[1] as string;
            GameSession.GameLevelSettings settings = __args[3] as GameSession.GameLevelSettings;
            LevelConfigBase levelConfig = null;

            if (settings != null && settings.SceneDirectoryVarientEntry != null)
            {
                levelConfig = settings.SceneDirectoryVarientEntry.LevelConfig;
            }

            plugin.Capture(levelIndex, sceneName, levelConfig);
        }
    }

    // Some installed level/arcade mods can bypass PlayerLobbyFlowroutine. This
    // lower hook is reached immediately before LoadingScreenFlow starts loading
    // a scene and reads the already-selected config from GameSession.
    [HarmonyPatch]
    internal static class LoadingScreenLoadScenePatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo[] methods = typeof(LoadingScreenFlow).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "LoadScene" &&
                    parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(string) &&
                    parameters[1].ParameterType == typeof(GameState))
                {
                    return method;
                }
            }

            throw new MissingMethodException(
                "Could not find LoadingScreenFlow.LoadScene(string, GameState).");
        }

        private static void Prefix(object[] __args)
        {
            RecipePreviewPlugin plugin = RecipePreviewPlugin.Instance;
            if (plugin == null || __args == null || __args.Length < 1)
            {
                return;
            }

            string sceneName = __args[0] as string;
            LevelConfigBase levelConfig = null;

            GameSession session = GameUtils.GetGameSession();
            if (session != null)
            {
                GameSession.GameLevelSettings settings = session.LevelSettings;
                if (settings != null && settings.SceneDirectoryVarientEntry != null)
                {
                    levelConfig = settings.SceneDirectoryVarientEntry.LevelConfig;
                    if (string.IsNullOrEmpty(sceneName))
                    {
                        sceneName = settings.SceneDirectoryVarientEntry.SceneName;
                    }
                }
            }

            plugin.Capture(-1, sceneName, levelConfig);
        }
    }
}
