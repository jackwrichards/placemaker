// Placemaker — system registration of the game-build workstream (GPL-3.0).
// Called once from Mod.OnLoad by the lead: BuildRegistration.Register(updateSystem).

using Game;
using Placemaker.Game.Assets;
using Placemaker.Game.Economy;
using Placemaker.Game.Groups;

namespace Placemaker.Game.Build
{
    public static class BuildRegistration
    {
        public static void Register(UpdateSystem updateSystem)
        {
            // ── Prefabs ──────────────────────────────────────────────────────────────
            // Runtime prefabs must be added in PrefabUpdate BEFORE PrefabInitializeSystem, or they
            // miss Initialize/LateInitialize and never get an archetype (RESEARCH-NOTES 1). Both
            // run unconditionally so saves that reference the clones by name always resolve (rule 3).
            updateSystem.UpdateBefore<SurfaceCloneSystem, global::Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateBefore<ParkingCompanionPrefabSystem, global::Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            // Industrial: the game's vehicles as static props (car yards, staff parking, machines), before any save loads.
            updateSystem.UpdateBefore<Placemaker.Game.Assets.VehiclePropSystem, global::Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateBefore<ParkCompanionPrefabSystem, global::Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            // Slow drive paths inside car parks (25 km/h, dearer: through traffic keeps to the streets; PLT parity).
            updateSystem.UpdateBefore<Placemaker.Game.Assets.SlowPathPrefabSystem, global::Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            // Stalls without painted markings (the parking lane stays; "Space markings" off).
            updateSystem.UpdateBefore<Placemaker.Game.Assets.InvisibleBayPrefabSystem, global::Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            // "Placemaker Area" owner lot (US7): one selectable/bulldozable thing per build.
            updateSystem.UpdateBefore<OwnerAreaPrefabSystem, global::Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);

            // ── Catalog & selection UI ─────────────────────────────────────────────────
            // UIUpdate ticks while the simulation is paused (GameSimulation does not), and
            // SelectedInfoUISystem drives its sections there.
            updateSystem.UpdateAt<AssetCatalogSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<PlacemakerGroupSection>(SystemUpdatePhase.UIUpdate);
            // Dynamic decals: code-made grime drawn from the strokes on the group records (never entities; docs/DYNAMIC-DECALS.md).
            updateSystem.UpdateAt<Placemaker.Game.Decals.DynamicDecalSystem>(SystemUpdatePhase.UIUpdate);

            // ── Build transaction ───────────────────────────────────────────────────────
            // ToolUpdate: tools run here and ToolOutputSystem reads the active tool's applyMode right
            // after. The service decides first, then the build tool hands the mode to the game.
            updateSystem.UpdateAt<BuildToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateBefore<BuildService, BuildToolSystem>(SystemUpdatePhase.ToolUpdate);
            // Ghost preview: temporary surfaces are created in Modification1 and triangulated before ModificationEnd;
            // tagging them there (still Updated) lets AreaBatchSystem (PreCulling) render their material.
            updateSystem.UpdateAt<GhostSurfaceSystem>(SystemUpdatePhase.ModificationEnd);

            // ── Neighbourhood polish ───────────────────────────────────────────────────
            // Modification4B: after LaneSystem's barrier (the houses' fence lanes exist and were just reset), before
            // Net.OverrideSystem, culling and rendering in Modification5+ (a rebuilt house is re-stitched in the same frame).
            updateSystem.UpdateAt<global::Placemaker.Game.Neighbours.NeighbourPolishSystem>(SystemUpdatePhase.Modification4B);
            // its lawn edits: next frame, before Areas.GeometrySystem re-triangulates Updated surfaces (Modification2B)
            updateSystem.UpdateBefore<global::Placemaker.Game.Neighbours.NeighbourLawnSystem, global::Game.Areas.GeometrySystem>(SystemUpdatePhase.Modification2B);
            // its ground paint: one terrain-material stamp per frame (as a build's paint step)
            updateSystem.UpdateAt<global::Placemaker.Game.Neighbours.NeighbourPaintSystem>(SystemUpdatePhase.ToolUpdate);
            // its shared-green surfaces: permanent definitions right before GenerateAreasSystem (as zone spawns are made)
            updateSystem.UpdateBefore<global::Placemaker.Game.Neighbours.NeighbourAreaSystem, global::Game.Tools.GenerateAreasSystem>(SystemUpdatePhase.Modification1);
            // pieces a garden no longer has: deleted at PostTool, where the game deletes a building's own pieces
            updateSystem.UpdateAfter<global::Placemaker.Game.Neighbours.NeighbourDeleteSystem, global::Game.Objects.SubElementDeleteSystem>(SystemUpdatePhase.PostTool);
            // its bushes: owned objects, permanent definitions right before GenerateObjectsSystem
            updateSystem.UpdateBefore<global::Placemaker.Game.Neighbours.NeighbourObjectSystem, global::Game.Tools.GenerateObjectsSystem>(SystemUpdatePhase.Modification1);
            // its pieces the game hid this frame (Objects.OverrideSystem, Modification5): shown again before culling
            updateSystem.UpdateAt<global::Placemaker.Game.Neighbours.NeighbourShowSystem>(SystemUpdatePhase.ModificationEnd);

            // ── Groups ─────────────────────────────────────────────────────────────────
            // Removal marks Deleted in PreTool: SubElementDeleteSystem (PostTool) cascades and
            // Game.Net.ReferencesSystem (Modification2B) clears node references in the same frame
            // (RESEARCH-NOTES 11–12).
            updateSystem.UpdateAt<GroupRemovalSystem>(SystemUpdatePhase.PreTool);
            // After a load: objects on a missing see-through decal copy of older builds are deleted (issue #2), as above.
            updateSystem.UpdateAt<OldDecalCleanupSystem>(SystemUpdatePhase.PreTool);
            // ModificationEnd: deleted members of this frame still exist (CleanUp is at frame end), so
            // carrier SubNet links are dropped before anything reads them next frame; runs while paused.
            updateSystem.UpdateAt<GroupRegistrySystem>(SystemUpdatePhase.ModificationEnd);
            // Before every load: our headless records go with the city before, as the game's ClearSystem does with its own
            // entities (issue #6: the records outlived the load and showed up twice).
            updateSystem.UpdateBefore<OwnEntityClearSystem, global::Game.Serialization.ClearSystem>(SystemUpdatePhase.Deserialize);

            // ── Economy (isolated; failures disable it, never the build) ───────────────
            // Fee/comfort must be written AFTER ParkingLaneDataSystem, which zeroes them for lanes whose
            // root owner is not a parking building (RESEARCH-NOTES 17).
            updateSystem.UpdateAfter<ParkingEconomySystem, global::Game.Pathfind.ParkingLaneDataSystem>(SystemUpdatePhase.ModificationEnd);
            // Companion components must exist before Game.Buildings.InitializeSystem sees the Building.
            updateSystem.UpdateBefore<ParkingCompanionSystem, global::Game.Buildings.InitializeSystem>(SystemUpdatePhase.Modification2);
            updateSystem.UpdateBefore<ParkCompanionSystem, global::Game.Buildings.InitializeSystem>(SystemUpdatePhase.Modification2);
            updateSystem.UpdateAt<ParkCompanionCleanupSystem>(SystemUpdatePhase.PreTool);
            // Upkeep usage / workers are set before vanilla charges upkeep and hires.
            updateSystem.UpdateBefore<ParkingCompanionUpkeepSystem, global::Game.Simulation.CityServiceUpkeepSystem>(SystemUpdatePhase.GameSimulation);
        }
    }
}
