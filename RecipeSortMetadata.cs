using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Overcooked2RecipePreview
{
    // CookingStepData assets in the game's resources name the actual utensil.
    // Scene station types are only a fallback for unknown/modded step assets.
    internal sealed class RecipeSortMetadata
    {
        private readonly Dictionary<int, HashSet<string>> _specific =
            new Dictionary<int, HashSet<string>>();
        private readonly Dictionary<int, HashSet<string>> _stations =
            new Dictionary<int, HashSet<string>>();
        private readonly string _mixingEquipment;

        internal RecipeSortMetadata(ManualLogSource logger)
        {
            HashSet<string> mixingTypes = new HashSet<string>();
            try
            {
                CookingHandler[] handlers = UnityEngine.Object.FindObjectsOfType<CookingHandler>();
                for (int i = 0; i < handlers.Length; i++)
                {
                    CookingHandler handler = handlers[i];
                    if (handler == null || handler.m_cookingType == null) continue;
                    Add(_stations, handler.m_cookingType,
                        StationName(handler.m_stationType));
                }

                MixingHandler[] mixers = UnityEngine.Object.FindObjectsOfType<MixingHandler>();
                for (int i = 0; i < mixers.Length; i++)
                {
                    if (mixers[i] == null || mixers[i].m_mixingType == null) continue;
                    string name = KnownStepName(mixers[i].m_mixingType.name);
                    if (name != null) mixingTypes.Add(name);
                }

                FieldInfo handlerField = AccessTools.Field(
                    typeof(ServerCookableContainer), "m_cookingHandler");
                FieldInfo containerField = AccessTools.Field(
                    typeof(ServerCookableContainer), "m_cookableContainer");
                if (handlerField != null && containerField != null)
                {
                    ServerCookableContainer[] containers =
                        UnityEngine.Object.FindObjectsOfType<ServerCookableContainer>();
                    for (int i = 0; i < containers.Length; i++)
                    {
                        ServerCookingHandler handler =
                            handlerField.GetValue(containers[i]) as ServerCookingHandler;
                        CookableContainer container =
                            containerField.GetValue(containers[i]) as CookableContainer;
                        if (handler == null || container == null) continue;
                        Add(_specific, handler.AccessCookingType,
                            ContainerName(container.m_cosmeticsPrefab));
                    }
                }

                FieldInfo ladleStep = AccessTools.Field(typeof(LadleContainer), "m_cookingStep");
                if (ladleStep != null)
                {
                    LadleContainer[] ladles =
                        UnityEngine.Object.FindObjectsOfType<LadleContainer>();
                    for (int i = 0; i < ladles.Length; i++)
                        Add(_specific, ladleStep.GetValue(ladles[i]) as CookingStepData,
                            "Cooking pot");
                }
                logger.LogInfo("Recipe sort cookware mapping: specific steps=" +
                    _specific.Count + ", station steps=" + _stations.Count +
                    ", mixing types=" + mixingTypes.Count + ".");
            }
            catch (Exception exception)
            {
                logger.LogWarning("Could not fully inspect kitchen cookware: " +
                    exception.Message);
            }
            _mixingEquipment = mixingTypes.Count == 1
                ? First(mixingTypes) : "Mixing / blending";
        }

        internal string CookwareFor(OrderDefinitionNode recipe)
        {
            Dictionary<int, CookingStepData> steps =
                new Dictionary<int, CookingStepData>();
            bool mixing = false;
            CollectSteps(recipe, new HashSet<int>(), steps, ref mixing);

            HashSet<string> labels = new HashSet<string>();
            bool unmapped = false;
            foreach (CookingStepData step in steps.Values)
            {
                string known = KnownStepName(step.name);
                if (known != null)
                {
                    labels.Add(known);
                    continue;
                }
                HashSet<string> found;
                if (!_specific.TryGetValue(step.GetInstanceID(), out found) &&
                    !_stations.TryGetValue(step.GetInstanceID(), out found))
                {
                    unmapped = true;
                    continue;
                }
                foreach (string label in found) labels.Add(label);
            }

            // Mixing is preparatory when a recipe also has a cooking step.
            // Sort such recipes by their actual cooking equipment, so steamed
            // fish and steamed dumplings stay together, as do oven dishes.
            if (labels.Count == 0 && mixing) labels.Add(_mixingEquipment);
            if (labels.Count == 0)
                return unmapped ? "Unmapped cookware" : "No cooking step";
            if (unmapped) labels.Add("Unknown step");

            List<string> ordered = new List<string>(labels);
            ordered.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(" + ", ordered.ToArray());
        }

        private static void CollectSteps(OrderDefinitionNode node, HashSet<int> path,
            Dictionary<int, CookingStepData> steps, ref bool mixing)
        {
            if (node == null) return;
            int id = node.GetInstanceID();
            if (!path.Add(id)) return;
            try
            {
                CookedCompositeOrderNode cooked = node as CookedCompositeOrderNode;
                if (cooked != null && cooked.m_cookingStep != null)
                    steps[cooked.m_cookingStep.GetInstanceID()] = cooked.m_cookingStep;
                if (node is MixedCompositeOrderNode) mixing = true;

                CompositeOrderNode composite = node as CompositeOrderNode;
                if (composite == null) return;
                VisitChildren(composite.m_composition, path, steps, ref mixing);
                VisitChildren(composite.m_optional, path, steps, ref mixing);
            }
            finally { path.Remove(id); }
        }

        private static void VisitChildren(OrderDefinitionNode[] children,
            HashSet<int> path, Dictionary<int, CookingStepData> steps, ref bool mixing)
        {
            if (children == null) return;
            for (int i = 0; i < children.Length; i++)
                CollectSteps(children[i], path, steps, ref mixing);
        }

        // Exact CookingStepData asset names were checked in resources.assets.
        // Unknown names fall back to live scene links rather than dish-name guesses.
        private static string KnownStepName(string name)
        {
            switch (name)
            {
                case "DeepFatFryer": return "Deep fryer";
                case "FlameThrower": return "Flamethrower";
                case "FryingPan": return "Frying pan";
                case "GriddlePan": return "Griddle pan";
                case "HotPot": return "Hot pot";
                case "KebabSkewer": return "Kebab skewer";
                case "MixingBowl": return "Mixing bowl";
                case "OvenCakeTin":
                case "OvenPot":
                case "OvenTray": return "Oven";
                case "Pot": return "Cooking pot";
                case "RoastingTray":
                case "DLC09_RoastingTray": return "Roasting tray";
                case "Steamer": return "Steamer";
                case "ToastingFork": return "Toasting fork";
                default: return null;
            }
        }

        private static string First(HashSet<string> labels)
        {
            foreach (string label in labels) return label;
            return null;
        }

        private static void Add(Dictionary<int, HashSet<string>> map,
            CookingStepData step, string label)
        {
            if (step == null || string.IsNullOrEmpty(label)) return;
            int id = step.GetInstanceID();
            HashSet<string> labels;
            if (!map.TryGetValue(id, out labels))
            {
                labels = new HashSet<string>();
                map.Add(id, labels);
            }
            labels.Add(label);
        }

        private static string StationName(CookingStationType type)
        {
            switch (type)
            {
                case CookingStationType.Hob: return "Hob (pan / pot)";
                case CookingStationType.Oven: return "Oven";
                case CookingStationType.DeepFatFryer: return "Deep fryer";
                case CookingStationType.FirePit: return "Fire pit";
                case CookingStationType.Flamethrower: return "Flamethrower";
                case CookingStationType.Barbeque: return "Barbecue";
                case CookingStationType.FloorBurner: return "Floor burner";
                default: return null;
            }
        }

        private static string ContainerName(GameObject prefab)
        {
            if (prefab == null) return null;
            if (prefab.GetComponentInChildren<FryingPanCosmeticDecisions>(true) != null)
                return "Frying pan";
            if (prefab.GetComponentInChildren<GriddlePanCosmeticDecisions>(true) != null)
                return "Griddle pan";
            if (prefab.GetComponentInChildren<WokCosmeticDecisions>(true) != null)
                return "Wok";
            if (prefab.GetComponentInChildren<SteamedSpecialCosmeticDecisions>(true) != null)
                return "Steamer";
            return null;
        }
    }
}