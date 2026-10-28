// Placemaker — persistent description of one built group (GPL-3.0).
// Pure data: the game layer stores the encoded bytes on the group's record entity, so the
// whole receipt (recipe + area + summary) survives save/load without any managed state.

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Build
{
    /// <summary>Flags kept with a group. Values are save format: never renumber.</summary>
    [Flags]
    public enum GroupFlags
    {
        None = 0,
        /// <summary>A prefab referenced by the receipt is not loaded (mod/asset pack missing).</summary>
        MissingAssets = 1,
        /// <summary>Some members were removed or replaced by other tools since the build.</summary>
        Modified = 2,
        /// <summary>Plan included a permanent terrain change (lake basin) that removal cannot undo.</summary>
        PermanentTerrain = 4,
        /// <summary>Group runs the parking economy (fee, upkeep, companion).</summary>
        ParkingEconomy = 8,
        /// <summary>
        /// Ungrouped by the player (or built with "One group" off): the build is dissolved for good, its pieces become plain
        /// city pieces (GroupRegistrySystem.Dissolve). Saves from before the dissolve (when Ungroup could be undone) carry it
        /// on records that still stand: they are dissolved after the load.
        /// </summary>
        Ungrouped = 16,
        /// <summary>
        /// Dissolved, all but its parking frame: the bare carrier that the stall markings' parking lanes need to find their
        /// driving lanes, with those markings and lanes. Not a build any more (not in Manage, no lot, no upkeep); the record
        /// keeps the carrier's driving-lane list right across loads and goes with the last marking or driving lane.
        /// </summary>
        Dissolved = 32,
    }

    /// <summary>
    /// Everything Placemaker remembers about a build. Encoded with <see cref="ReceiptCodec"/>.
    /// Field additions go to the end of the codec with a version bump; never reorder.
    /// </summary>
    public sealed class GroupRecord
    {
        public int GroupId;
        public string RecipeId = "";
        public string RecipeNameKey = "";
        public string CustomName = "";
        public GroupFlags Flags;
        /// <summary>Simulation-independent build stamp (DateTime.UtcNow.Ticks) for sorting in lists.</summary>
        public long BuiltAtTicks;
        public double Cost;
        public double Upkeep;
        public List<PlanStat> Stats = new List<PlanStat>();
        /// <summary>Generator facts summed over layers (e.g. "parking.capacity").</summary>
        public Dictionary<string, double> Facts = new Dictionary<string, double>(StringComparer.Ordinal);
        /// <summary>Every prefab name the build used — the missing-asset check compares these with the loaded prefabs.</summary>
        public List<string> PrefabNames = new List<string>();
        /// <summary>Receipt for Edit/repair. May be null when the recipe could not be stored.</summary>
        public Recipe Recipe;
        public AreaShape Area;
        /// <summary>
        /// (s17, receipt v2) Park stats of the build: the player's settings and the values applied in game at build
        /// time. Null = not a park type, or built before s17.
        /// </summary>
        public ParkStatsRecord ParkStats;

        public GroupRecord Clone()
        {
            // Round-tripping through the codec is the simplest deep copy that is guaranteed to
            // match what the save holds.
            return ReceiptCodec.Decode(ReceiptCodec.Encode(this)) ?? new GroupRecord { GroupId = GroupId };
        }
    }
}
