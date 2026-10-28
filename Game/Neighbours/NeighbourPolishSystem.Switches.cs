// Placemaker neighbourhood polish — the city's own Outgrow switches (GPL-3.0).
//
// "Auto outgrow new buildings", "... when a building levels up", "... when a building changes", "... when a building nearby
// is removed" and the master switch belong to the city, not the mod: they are saved with it (a headless entity with
// PolishCitySwitches) and read back when it loads. A new city, or one saved without them, starts with all four off and
// Outgrow running. Nothing ever switches them by itself.
//
// The master switch shows or hides Outgrow (the user, 25 Oct: "a hide and show switch ... and then reset all the buildings
// to their default zone type"). Off: every outgrown building is put back as the game made it, a few a frame, but keeps its
// flag and outgrow type; nothing is planned or written while it is off. On: every one of them is planned again, a patch of
// town a frame. "New buildings" (the city's, the user 27 Oct after players asked for "no outgrow until I paint it"): Zone
// default gives each new building its zone type's outgrow type ("Off": none), also while Outgrow is off (the type is the
// setting, the switch only shows it); Off gives none, until painted. A new city: Off; one saved before: Zone default.

using Game.Common;
using Unity.Collections;
using Unity.Entities;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        /// <summary>The master switch is off (this city's): Outgrow is paused.</summary>
        public static bool Paused { get; private set; }

        private static bool s_switchesDirty;
        private bool _switchesRead;
        private EntityQuery _switches, _oldCitySettings;

        /// <summary>"Outgrow new buildings" for this city (saved with it).</summary>
        public static void SetNewBuildings(bool on)
        {
            if (PolishNew == on) return;
            PolishNew = on;
            s_switchesDirty = true;
            Mod.Log.Info($"Placemaker neighbourhood polish: outgrow new buildings {(on ? "on" : "off")} (this city)");
        }

        /// <summary>"Outgrow again when a building levels up" for this city (saved with it; NeighbourPolishSystem.LevelUps).</summary>
        public static void SetLevelUps(bool on)
        {
            if (PolishLevelUps == on) return;
            PolishLevelUps = on;
            s_switchesDirty = true;
            Mod.Log.Info($"Placemaker neighbourhood polish: outgrow again when a building levels up {(on ? "on" : "off")} (this city)");
        }

        /// <summary>"Outgrow again when the game changes a building" for this city (saved with it; NeighbourPolishSystem.LevelUps).</summary>
        public static void SetGameChanges(bool on)
        {
            if (PolishGameChanges == on) return;
            PolishGameChanges = on;
            s_switchesDirty = true;
            Mod.Log.Info($"Placemaker neighbourhood polish: outgrow again when the game changes a building {(on ? "on" : "off")} (this city)");
        }

        /// <summary>"Outgrow again when the game removes a building nearby" for this city (saved with it).</summary>
        public static void SetGameRemovals(bool on)
        {
            if (PolishGameRemovals == on) return;
            PolishGameRemovals = on;
            s_switchesDirty = true;
            Mod.Log.Info($"Placemaker neighbourhood polish: outgrow again when the game removes a building nearby {(on ? "on" : "off")} (this city)");
        }

        /// <summary>The master switch for this city (saved with it): off hides Outgrow (every outgrown building put back, its
        /// type kept), on shows it again (every one planned again).</summary>
        public static void SetRunning(bool on)
        {
            if (Paused == !on) return;
            Paused = !on;
            s_switchesDirty = true;
            if (on) { s_hideAll = false; s_typeUntyped = true; Redo(); } else s_hideAll = true;
            Mod.Log.Info($"Placemaker neighbourhood polish: Outgrow {(on ? "shown: every outgrown building planned again" : "hidden: every outgrown building put back, its type kept")} (this city)");
        }

        private static bool s_hideAll, s_typeUntyped;

        /// <summary>Outgrow on: every building that never had an outgrow type (it stood before the city was outgrown) gets its
        /// zone's default (a zone set to Off: none), so on always means every building as it is set up.</summary>
        private void TypeUntyped()
        {
            s_typeUntyped = false;
            int n = 0;
            using (var all = _growables.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                    if (!EntityManager.HasComponent<PolishedLot>(e) && IsGrowable(e)) { s_stamps.Add((e, true, "zone", null)); n++; }
            if (n > 0) Mod.Log.Info($"Placemaker neighbourhood polish: Outgrow on, {n} buildings without an outgrow type get their zone's default.");
        }

        /// <summary>Hidden: every outgrown building put back as the game made it, its flag and type kept (DrainRestores).</summary>
        private void StartHideAll()
        {
            s_hideAll = false;
            _passBuild = null;
            int n = 0;
            using (var all = _growables.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                    if (IsYes(e)) { _restoreQueue.Add(e); n++; }
            _dirty.Clear();
            _first.Clear();
            _described.Clear();
            _removeAllCount = 0;
            Mod.Log.Info($"Placemaker neighbourhood polish: Outgrow hidden, {n} outgrown buildings to put back (they keep their types).");
        }

        private void CreateSwitchesQuery()
        {
            _switches = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<PolishCitySwitches>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            // a city saved by a build of 2026-10-20 kept "Outgrow new buildings" in PolishCitySettings: read once, moved here
            _oldCitySettings = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PolishCitySettings>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        /// <summary>Another city: its switches are read on its first frame (none saved: the defaults).</summary>
        private void ForgetSwitches()
        {
            _switchesRead = false;
            s_switchesDirty = false;
            PolishNew = true;
            Paused = false;
            PolishLevelUps = PolishGameChanges = PolishGameRemovals = false;
            s_cityLooksDirty = false;
        }


        /// <summary>Reads the city's switches once after a load, then writes them whenever the player changes one.</summary>
        private void SwitchesUpdate()
        {
            if (!_switchesRead)
            {
                _switchesRead = true;
                if (!s_switchesDirty)
                {
                    // a city with nothing kept yet: a new one starts with Outgrow off; one saved before the switches were kept
                    // (it has outgrown buildings) goes on as it was, on
                    bool legacy = false;
                    using (var flags = _flaggedLots.ToComponentDataArray<PolishedLot>(Allocator.Temp))
                        foreach (var f in flags) if (f.Yes) { legacy = true; break; }
                    using (var all = _switches.ToComponentDataArray<PolishCitySwitches>(Allocator.Temp))
                    {
                        // "New buildings": the city's; a new city: Off (none until painted), an old one goes on with Zone default
                        PolishNew = all.Length > 0 ? all[0].NewBuildings : legacy;
                        Paused = all.Length > 0 ? all[0].Paused : !legacy;
                        if (all.Length == 0) s_switchesDirty = true;   // (kept from now on: next time it reads them)
                        PolishLevelUps = all.Length > 0 && all[0].LevelUps;
                        PolishGameChanges = all.Length > 0 && all[0].GameChanges;
                        PolishGameRemovals = all.Length > 0 && all[0].GameRemovals;
                        if (all.Length == 0 && !_oldCitySettings.IsEmptyIgnoreFilter)
                            using (var old = _oldCitySettings.ToComponentDataArray<PolishCitySettings>(Allocator.Temp))
                            {
                                PolishNew = old[0].NewBuildings;
                                s_switchesDirty = true;   // written as switches below
                            }
                        if (!_oldCitySettings.IsEmptyIgnoreFilter) EntityManager.DestroyEntity(_oldCitySettings);
                        // the zone defaults: the city's own; a city that kept none yet: an old one keeps the mod's (as they were
                        // before they were per city), a new one starts with the built-in ones
                        using (var ents = _switches.ToEntityArray(Allocator.Temp))
                        {
                            if (ents.Length > 0 && EntityManager.HasBuffer<PolishCityZoneLook>(ents[0])) ReadCityLooks(EntityManager.GetBuffer<PolishCityZoneLook>(ents[0], true));
                            else
                            {
                                var settings = global::Placemaker.Game.Tool.ToolRegistration.Settings;
                                if (legacy && settings != null)
                                {
                                    SetZoneThemes(settings.PolishThemeHomes, settings.PolishThemeShops, settings.PolishThemeOffices, settings.PolishThemeIndustry);
                                    SetZoneLooks(settings.PolishZoneLooks);
                                }
                                else
                                {
                                    s_zoneLooks.Clear();
                                    foreach (var k in new[] { "home", "shop", "office", "industry" }) s_zoneTheme[k] = KindAuto;
                                }
                                s_cityLooksDirty = true;
                            }
                        }
                        Mod.Log.Info($"Placemaker neighbourhood polish: this city's switches {(all.Length > 0 ? "read" : "not saved yet (the defaults)")}: new buildings {(PolishNew ? "on" : "off")}, level-ups {(PolishLevelUps ? "outgrown again" : "keep their gardens")}, the game's changes {(PolishGameChanges ? "outgrown again" : "keep their gardens")}, the game's removals {(PolishGameRemovals ? "outgrown again" : "keep the gardens around")}, Outgrow {(Paused ? "paused" : "running")}");
                    }
                    StatusVersion++;
                }
            }
            if (!s_switchesDirty && !s_cityLooksDirty) return;
            bool looks = s_cityLooksDirty;
            s_switchesDirty = false;
            s_cityLooksDirty = false;
            var value = new PolishCitySwitches { NewBuildings = PolishNew, Paused = Paused, LevelUps = PolishLevelUps, GameChanges = PolishGameChanges, GameRemovals = PolishGameRemovals };
            using (var all = _switches.ToEntityArray(Allocator.Temp))
            {
                Entity e;
                if (all.Length == 0) { e = EntityManager.CreateEntity(); EntityManager.AddComponentData(e, value); looks = true; }
                else
                {
                    e = all[0];
                    EntityManager.SetComponentData(e, value);
                    for (int k = 1; k < all.Length; k++) EntityManager.DestroyEntity(all[k]);
                }
                if (looks) WriteCityLooks(e);
            }
            StatusVersion++;
        }
    }
}
