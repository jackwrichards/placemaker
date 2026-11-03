// Placemaker — our headless entities cleared before a city loads (GPL-3.0).
//
// Phase: Deserialize, before Game.Serialization.ClearSystem (the game's own clear before the save is read). That system
// destroys only entities with a game component (PrefabRef, LoadedIndex, CityStatistic, ...). Ours carry none: the group
// records (PlacemakerGroup + blob), the polish look areas and a city's polish switches outlived every load. Loading a
// city a second time in one game left the first load's records next to the save's (GitHub issue #6: "duplicate group
// id", then a crash when a yard was built again: the registry kept the dead copy, whose carrier and owner lot were the
// earlier load's), and another city's groups, look areas and switches came along into the next one.

using Game;
using Game.Prefabs;
using Placemaker.Game.Neighbours;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Placemaker.Game.Groups
{
    public sealed partial class OwnEntityClearSystem : GameSystemBase
    {
        private EntityQuery _query;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _query = GetEntityQuery(new EntityQueryDesc
            {
                Any = new[]
                {
                    ComponentType.ReadOnly<PlacemakerGroup>(),
                    ComponentType.ReadOnly<PolishLookArea>(),
                    ComponentType.ReadOnly<PolishCitySwitches>(),
                    ComponentType.ReadOnly<PolishCitySettings>(),
                },
                // anything with a prefab is the game's to clear (ClearSystem, right after this)
                None = new[] { ComponentType.ReadOnly<PrefabRef>() },
            });
        }

        [Preserve]
        protected override void OnUpdate()
        {
            int n = _query.CalculateEntityCount();
            if (n == 0) return;
            EntityManager.DestroyEntity(_query);
            Mod.Log.Info($"Placemaker: {n} group record(s), look area(s) and city switch(es) of the city before cleared ahead of the load.");
        }
    }
}
