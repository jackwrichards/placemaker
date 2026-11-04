// Game runner lab: level houses up on request (lab op "levelUp"), through the game's own debug level-up
// (BuildingUpkeepSystem.DebugLevelUp: the house joins the level-up queue, the simulation swaps in the next level's model
// on the same lot and rebuilds its fences and lawns). Lookups come from this system: SystemBase hands them out.

using System.Collections.Generic;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Unity.Entities;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteLabShapeSystem
    {
        public int LevelUp(IEnumerable<Entity> buildings)
        {
            var upkeep = World.GetExistingSystemManaged<BuildingUpkeepSystem>();
            if (upkeep == null) return -1;
            EntityManager.CompleteAllTrackedJobs();
            var conditions = GetComponentLookup<BuildingCondition>(false);
            var spawnables = GetComponentLookup<SpawnableBuildingData>(true);
            var prefabRefs = GetComponentLookup<PrefabRef>(true);
            var zones = GetComponentLookup<ZoneData>(true);
            var properties = GetComponentLookup<BuildingPropertyData>(true);
            conditions.Update(this);
            spawnables.Update(this);
            prefabRefs.Update(this);
            zones.Update(this);
            properties.Update(this);
            int n = 0;
            foreach (var b in buildings)
            {
                if (!conditions.HasComponent(b) || !prefabRefs.HasComponent(b)) continue;
                upkeep.DebugLevelUp(b, conditions, spawnables, prefabRefs, zones, properties);
                n++;
            }
            return n;
        }
    }
}
