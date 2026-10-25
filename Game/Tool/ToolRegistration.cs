// Placemaker — registration of the game-tool workstream (systems, settings, key bindings,
// localisation). Called once from Mod.OnLoad by the lead: ToolRegistration.Register(updateSystem, this);
// and ToolRegistration.Unregister() from Mod.OnDispose.

using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.IO.AssetDatabase;
using Game;
using Game.Input;
using Game.SceneFlow;
using Placemaker.Game.Localization;
using Placemaker.Game.Overlay;
using Placemaker.Game.Settings;
using Placemaker.Game.UI;

namespace Placemaker.Game.Tool
{
    public static class ToolRegistration
    {
        public static PlacemakerSettings Settings { get; private set; }
        public static LayerDefaultsStore Defaults { get; private set; }
        private static PlacemakerToolSystem s_Tool;

        public static void Register(UpdateSystem updateSystem, Mod mod)
        {
            ModPaths.Initialize(mod);

            Settings = new PlacemakerSettings(mod);
            try
            {
                AssetDatabase.global.LoadSettings(nameof(Placemaker), Settings, new PlacemakerSettings(mod));
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: settings not loaded, using defaults: " + e.Message); }
            Settings.PushToServices();
            Settings.RegisterInOptionsUI();
            // Must run before any GetAction() (actions come from the ProxyBinding properties).
            Settings.RegisterKeyBindings();
            Defaults = new LayerDefaultsStore();

            LocalizationLoader.Register(SettingsLocale);

            updateSystem.UpdateAt<PlacemakerToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<PlacemakerUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<PlacemakerTooltipSystem>(SystemUpdatePhase.UITooltip);
            updateSystem.UpdateAt<PlacemakerOverlaySystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAt<Placemaker.Game.Overlay.OutgrowDebugOverlaySystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAt<FilledPreviewSystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateBefore<ZoneGridOpacitySystem, global::Game.Prefabs.ZoneSystem>(SystemUpdatePhase.PrefabUpdate);
            s_Tool = updateSystem.World.GetOrCreateSystemManaged<PlacemakerToolSystem>();

            PlacemakerSettings.RemoveEverythingRequested += OnRemoveEverything;
            PlacemakerSettings.ResetDefaultsRequested += OnResetDefaults;
            Mod.Log.Info("Placemaker tool registered");
        }

        public static void Unregister()
        {
            PlacemakerSettings.RemoveEverythingRequested -= OnRemoveEverything;
            PlacemakerSettings.ResetDefaultsRequested -= OnResetDefaults;
            LocalizationLoader.Unregister();
            try { Settings?.UnregisterInOptionsUI(); } catch (Exception) { }
            Settings = null;
            s_Tool = null;
        }

        private static void OnRemoveEverything() => s_Tool?.RemoveEverything();
        private static void OnResetDefaults() => s_Tool?.ResetAllDefaults();

        /// <summary>
        /// CS2 derives the Options page's localisation ids from type and property names; map them to
        /// our readable keys ("Placemaker.Settings.<Property>" / ".Desc") from Lang/en-US.tool.json.
        /// </summary>
        private static Dictionary<string, string> SettingsLocale(Dictionary<string, string> strings)
        {
            var s = Settings;
            var r = new Dictionary<string, string>();
            if (s == null) return r;
            string T(string key, string fallback) => strings.TryGetValue(key, out var v) ? v : fallback;

            r[s.GetSettingsLocaleID()] = T("Placemaker.Name", "Placemaker");
            r[s.GetOptionTabLocaleID(PlacemakerSettings.SectionMain)] = T("Placemaker.Settings.Tab.Main", "Placemaker");
            foreach (var g in new[] { PlacemakerSettings.GroupGeneral, PlacemakerSettings.GroupBrush, PlacemakerSettings.GroupBuild, PlacemakerSettings.GroupNeighbourhood, PlacemakerSettings.GroupKeys, PlacemakerSettings.GroupMaintenance })
                r[s.GetOptionGroupLocaleID(g)] = T("Placemaker.Settings.Group." + g, g);
            // Every option of the class (a hand list left newer options showing their raw ids); one that asks for confirmation
            // gets a warning text too
            var props = typeof(PlacemakerSettings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
            foreach (var prop in props)
                if (Attribute.IsDefined(prop, typeof(global::Game.Settings.SettingsUIConfirmationAttribute)))
                    r[s.GetOptionWarningLocaleID(prop.Name)] = T("Placemaker.Settings." + prop.Name + ".Warning", "Are you sure?");
            foreach (var p in Array.ConvertAll(props, x => x.Name))
            {
                r[s.GetOptionLabelLocaleID(p)] = T("Placemaker.Settings." + p, p);
                r[s.GetOptionDescLocaleID(p)] = T("Placemaker.Settings." + p + ".Desc", "");
            }
            r[s.GetOptionWarningLocaleID(nameof(PlacemakerSettings.RemoveEverything))] = T("Placemaker.Settings.RemoveEverything.Warning", "Remove everything Placemaker built?");
            r[s.GetOptionWarningLocaleID(nameof(PlacemakerSettings.ResetDefaults))] = T("Placemaker.Settings.ResetDefaults.Warning", "Reset all Placemaker options and saved defaults?");
            foreach (PreviewDetail d in Enum.GetValues(typeof(PreviewDetail)))
                r[s.GetEnumValueLocaleID(d)] = T("Placemaker.Settings.PreviewDetail." + d, d.ToString());
            foreach (SketchStyle k in Enum.GetValues(typeof(SketchStyle)))
                r[s.GetEnumValueLocaleID(k)] = T("Placemaker.Settings.SketchStyle." + k, k == SketchStyle.SitePlan ? "Site plan" : "Classic");
            r[s.GetBindingMapLocaleID()] = T("Placemaker.Name", "Placemaker");
            foreach (var a in new[] { PlacemakerSettings.ActionToggleTool, PlacemakerSettings.ActionPrimary, PlacemakerSettings.ActionUndo, PlacemakerSettings.ActionRedo, PlacemakerSettings.ActionRedoAlt })
                r[s.GetBindingKeyLocaleID(a)] = T("Placemaker.Settings.Action." + a, a);
            return r;
        }

        /// <summary>The global hotkey (polled by the UI system, which runs while the tool is closed).</summary>
        internal static ProxyAction ToggleAction
        {
            get
            {
                try { return Settings?.GetAction(PlacemakerSettings.ActionToggleTool); }
                catch (Exception) { return null; }
            }
        }

        internal static bool InGame
        {
            get
            {
                try { return GameManager.instance != null && (GameManager.instance.gameMode & GameMode.Game) != 0; }
                catch (Exception) { return false; }
            }
        }
    }
}
