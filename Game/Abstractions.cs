// Seams inside the game layer, so the tool/UI side and the build/persistence side can be
// developed and tested independently. Implementations register themselves as ECS systems;
// consumers fetch them with World.GetOrCreateSystemManaged<T>() via the static Services
// accessor below (set in OnCreate of each implementation).

using System.Collections.Generic;
using Placemaker.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game
{
    // ───────────────────────── Build ─────────────────────────

    public enum BuildPhase { Idle = 0, Preparing = 1, Materializing = 2, Validating = 3, Applying = 4, Settling = 5, Done = 6, Failed = 7, Cancelled = 8 }

    public sealed class BuildRequest
    {
        public ComposedPlan Plan;
        public Recipe Recipe;              // stored in the receipt for Edit
        public AreaShape Area;             // stored in the receipt for Edit
        /// <summary>Existing group to replace (Edit). 0 = new build.</summary>
        public int ReplaceGroup;
        public string CustomName = "";
        /// <summary>Built as separate pieces: the group is born ungrouped (GroupFlags.Ungrouped).</summary>
        public bool Ungrouped;
    }

    public struct BuildStatus
    {
        public BuildPhase Phase;
        public float Progress;             // 0..1
        public int GroupId;                // set when Done
        /// <summary>Localisation key + args explaining a failure (game rejection reason etc.).</summary>
        public string MessageKey;
        public object[] MessageArgs;
        /// <summary>
        /// World positions of game errors (red markers). Only set while Phase == Failed and cleared by
        /// Start, Acknowledge and ClearErrors — draw them only while they are present.
        /// </summary>
        public List<float3> ErrorPositions;
        /// <summary>
        /// (gamebuild round 2) What was fixed or skipped (Done) — localisation key + args, optional map
        /// position. Severity Warning = something left out, Info = repaired. Never null after Start.
        /// </summary>
        public List<PlanIssue> Warnings;
        /// <summary>(s53) What did not make it into the build, with reasons (Done); null when the service has nothing to report.</summary>
        public Placemaker.Core.Build.BuildAudit Audit;
    }

    public interface IBuildService
    {
        /// <summary>Start a build. Returns false (and a reason in Status) if one is already running.</summary>
        bool Start(BuildRequest request);
        BuildStatus Status { get; }
        void Cancel();
        /// <summary>Clears Done/Failed back to Idle after the tool consumed the result.</summary>
        void Acknowledge();
        /// <summary>(gamebuild round 2) Drops the error positions of a failed build (the user changed something).</summary>
        void ClearErrors();
    }

    // ───────────────────────── Groups (built things) ─────────────────────────

    public sealed class GroupInfo
    {
        public int Id;
        public string RecipeId = "";
        public string RecipeNameKey = "";
        public string CustomName = "";
        public List<PlanStat> Stats = new List<PlanStat>();
        public double Upkeep;
        public int MemberCount;
    }

    public sealed class GroupReceipt
    {
        public int FormatVersion;
        public Recipe Recipe;
        public AreaShape Area;
    }

    public interface IGroupRegistry
    {
        /// <summary>Group owning this entity (any member: net, object, area), 0 if none.</summary>
        int GroupOf(Entity e);
        GroupInfo Get(int groupId);
        GroupReceipt GetReceipt(int groupId);
        IReadOnlyCollection<int> AllGroups { get; }
        /// <summary>Members for highlighting (nets, objects, areas).</summary>
        void GetMembers(int groupId, List<Entity> into);
        /// <summary>Remove the whole group safely (phase-correct cascade). Returns false if not found.</summary>
        bool RequestRemove(int groupId);
        void Rename(int groupId, string name);
        /// <summary>Ungrouped (GroupFlags.Ungrouped): being dissolved, or only its parking frame is left.</summary>
        bool IsUngrouped(int groupId);
        /// <summary>
        /// Ungroup: dissolve the build for good (cannot be undone). Its pieces become plain city pieces, as if built by hand;
        /// the record, the owner lot and the companion buildings go. Returns false if the group is not found or being removed.
        /// </summary>
        bool Ungroup(int groupId);
    }

    // ───────────────────────── Assets ─────────────────────────

    public sealed class CatalogItem
    {
        public string Id = "";             // prefab name (stable, used in recipes/saves)
        public string DisplayName = "";    // localised
        public string Icon = "";           // thumbnail url
        public string[] Tags = System.Array.Empty<string>();
        public AssetInfo Info;
        public Entity Prefab;
    }

    public interface IAssetCatalog
    {
        bool Ready { get; }
        /// <summary>Bumped whenever lists change (new mods/assets loaded).</summary>
        int Version { get; }
        IReadOnlyList<CatalogItem> Items(string category);
        /// <summary>Resolve an AssetRef to a prefab entity: Id → Fallbacks → category default for the role. Entity.Null if none.</summary>
        Entity Resolve(AssetRef asset, string category);
        /// <summary>AssetInfo for every catalogued item (for SiteContext / pricing).</summary>
        Dictionary<string, AssetInfo> InfoMap();
        /// <summary>
        /// (gamebuild round 2, US5) Replaces every automatic AssetRef (empty Id) in the plan, in place,
        /// with a concrete catalog prefab name — deterministic per (category, Variation / net index) —
        /// so preview, Pricing.Of and the build use the same assets. Idempotent; explicit Ids are kept.
        /// Call after Compose, before preview/pricing/Start. Returns the number of refs resolved.
        /// </summary>
        int ResolveAutomatic(ComposedPlan plan);
    }

    // ───────────────────────── Service locator ─────────────────────────

    /// <summary>
    /// Set by each implementing system in OnCreate. Kept static (not DI) because CS2 owns system
    /// construction; null-checked by consumers so a missing service degrades instead of crashing.
    /// </summary>
    public static class Services
    {
        public static IBuildService Build;
        public static IGroupRegistry Groups;
        public static IAssetCatalog Assets;

        public static void Reset() { Build = null; Groups = null; Assets = null; }
    }
}
