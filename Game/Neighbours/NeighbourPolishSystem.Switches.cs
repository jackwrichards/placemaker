// Placemaker neighbourhood polish — the city's own Outgrow switches (GPL-3.0).
//
// "Outgrow new buildings", "Outgrow again when a building levels up" and the master switch belong to the city, not the mod: they are saved with it (a headless entity
// with PolishCitySwitches) and read back when it loads. A city without them (a new one, or one saved before) starts with
// new buildings off and Outgrow running. Nothing ever switches them by itself.
//
// The master switch off pauses Outgrow: no building is newly given a look (new buildings, zoned areas), nothing is planned
// or written, and nothing is put back. What the painter stamps meanwhile is kept and fitted once it is on again.

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

        /// <summary>The master switch for this city (saved with it): off pauses Outgrow and puts nothing back.</summary>
        public static void SetRunning(bool on)
        {
            if (Paused == !on) return;
            Paused = !on;
            s_switchesDirty = true;
            Mod.Log.Info($"Placemaker neighbourhood polish: Outgrow {(on ? "running" : "paused")} (this city)");
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
            PolishNew = false;
            Paused = false;
            PolishLevelUps = true;
        }

        /// <summary>Reads the city's switches once after a load, then writes them whenever the player changes one.</summary>
        private void SwitchesUpdate()
        {
            if (!_switchesRead)
            {
                _switchesRead = true;
                if (!s_switchesDirty)
                {
                    using (var all = _switches.ToComponentDataArray<PolishCitySwitches>(Allocator.Temp))
                    {
                        PolishNew = all.Length > 0 && all[0].NewBuildings;
                        Paused = all.Length > 0 && all[0].Paused;
                        PolishLevelUps = all.Length == 0 || all[0].LevelUps;
                        if (all.Length == 0 && !_oldCitySettings.IsEmptyIgnoreFilter)
                            using (var old = _oldCitySettings.ToComponentDataArray<PolishCitySettings>(Allocator.Temp))
                            {
                                PolishNew = old[0].NewBuildings;
                                s_switchesDirty = true;   // written as switches below
                            }
                        if (!_oldCitySettings.IsEmptyIgnoreFilter) EntityManager.DestroyEntity(_oldCitySettings);
                        Mod.Log.Info($"Placemaker neighbourhood polish: this city's switches {(all.Length > 0 ? "read" : "not saved yet (the defaults)")}: new buildings {(PolishNew ? "on" : "off")}, level-ups {(PolishLevelUps ? "outgrown again" : "keep their gardens")}, Outgrow {(Paused ? "paused" : "running")}");
                    }
                    StatusVersion++;
                }
            }
            if (!s_switchesDirty) return;
            s_switchesDirty = false;
            var value = new PolishCitySwitches { NewBuildings = PolishNew, Paused = Paused, LevelUps = PolishLevelUps };
            using (var all = _switches.ToEntityArray(Allocator.Temp))
            {
                if (all.Length == 0) EntityManager.AddComponentData(EntityManager.CreateEntity(), value);
                else
                {
                    EntityManager.SetComponentData(all[0], value);
                    for (int k = 1; k < all.Length; k++) EntityManager.DestroyEntity(all[k]);
                }
            }
            StatusVersion++;
        }
    }
}
