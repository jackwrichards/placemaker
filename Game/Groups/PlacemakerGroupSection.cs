// Placemaker — "Part of <name> — Edit / Remove" section in the vanilla selected-info panel (GPL-3.0).
// Replaces ParkingLotTool's Harmony raycast lift: nets and objects are already clickable in the
// default tool, so the section only has to recognise members (and their owned sub-objects, e.g. a
// street light of one of our roads) through the PlacemakerMember tag.
//
// Phase: UIUpdate (SelectedInfoUISystem drives sections there). The UI workstream renders the
// section by its type name "Placemaker.Game.Groups.PlacemakerGroupSection". Triggers in the
// "Placemaker" binding group: editGroup/removeGroup are handled by the tool workstream;
// renameGroup(int groupId, string name) is handled here.

using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using Game.Common;
using Game.UI.InGame;
using Placemaker.Core;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Placemaker.Game.Groups
{
    public sealed partial class PlacemakerGroupSection : InfoSectionBase
    {
        private GroupRegistrySystem _registry;

        private int _groupId;
        private string _name = "";
        private string _recipeNameKey = "";
        private readonly List<PlanStat> _stats = new List<PlanStat>();
        private double _upkeep;
        private int _memberCount;
        private bool _missingAssets;
        private bool _modified;
        private bool _removing;
        /// <summary>(P2-1) The parking fee (Game.Net.ParkingLane.m_ParkingFee on every stall): -1 = not a car park.</summary>
        private int _fee = -1;
        private bool _feeOn;
        private global::Placemaker.Game.Economy.ParkingEconomySystem _economy;

        protected override string group => nameof(PlacemakerGroupSection);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _registry = World.GetOrCreateSystemManaged<GroupRegistrySystem>();
            m_InfoUISystem.AddMiddleSection(this);
            AddBinding(new TriggerBinding<int, string>("Placemaker", "renameGroup", OnRename));
            _economy = World.GetOrCreateSystemManaged<global::Placemaker.Game.Economy.ParkingEconomySystem>();
            // (P2-1) the parking fee of a placed car park, in the game's own terms (money per park), no rebuild (as Parking Lot Tool)
            AddBinding(new TriggerBinding<int, int>("Placemaker", "setGroupFee", OnSetFee));
            AddBinding(new TriggerBinding<int, bool>("Placemaker", "setGroupFeeOn", OnSetFeeOn));
        }

        private void OnRename(int groupId, string name)
        {
            try
            {
                _registry.Rename(groupId, name);
                m_InfoUISystem.RequestUpdate();
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker rename failed: " + e.Message);
            }
        }

        private void OnSetFee(int groupId, int fee)
        {
            try { _economy.SetFee(groupId, fee); m_InfoUISystem.RequestUpdate(); }
            catch (Exception e) { Mod.Log.Warn("Placemaker fee failed: " + e.Message); }
        }

        private void OnSetFeeOn(int groupId, bool on)
        {
            try { _economy.SetFeeOn(groupId, on); m_InfoUISystem.RequestUpdate(); }
            catch (Exception e) { Mod.Log.Warn("Placemaker fee failed: " + e.Message); }
        }

        protected override void Reset()
        {
            _fee = -1;
            _feeOn = false;
            _groupId = 0;
            _name = "";
            _recipeNameKey = "";
            _stats.Clear();
            _upkeep = 0;
            _memberCount = 0;
            _missingAssets = false;
            _modified = false;
            _removing = false;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            visible = ResolveGroup(selectedEntity) != 0;
        }

        /// <summary>Member itself, or the first tagged owner up to 3 levels (sub-objects of our nets).</summary>
        private int ResolveGroup(Entity e)
        {
            try
            {
                for (int depth = 0; depth < 4 && e != Entity.Null && EntityManager.Exists(e); depth++)
                {
                    int id = _registry.GroupOf(e);
                    if (id != 0) return id;
                    if (!EntityManager.HasComponent<Owner>(e)) return 0;
                    e = EntityManager.GetComponentData<Owner>(e).m_Owner;
                }
            }
            catch (Exception) { }
            return 0;
        }

        protected override void OnProcess()
        {
            _groupId = ResolveGroup(selectedEntity);
            var info = _groupId != 0 ? _registry.Get(_groupId) : null;
            if (info == null) return;
            _name = info.CustomName ?? "";
            _recipeNameKey = info.RecipeNameKey ?? "";
            _stats.AddRange(info.Stats);
            _upkeep = info.Upkeep;
            _memberCount = info.MemberCount;
            _missingAssets = _registry.HasMissingAssets(_groupId);
            var rec = _registry.GetRecord(_groupId);
            _modified = rec != null && (rec.Flags & Core.Build.GroupFlags.Modified) != 0;
            _removing = _registry.IsRemoving(_groupId);
            if (_economy != null && _economy.TryFee(_groupId, out int amount, out bool on)) { _fee = amount; _feeOn = on; }
        }

        public override void OnWriteProperties(IJsonWriter writer)
        {
            writer.PropertyName("groupId");
            writer.Write(_groupId);
            writer.PropertyName("name");
            writer.Write(_name);
            writer.PropertyName("recipeNameKey");
            writer.Write(_recipeNameKey);
            writer.PropertyName("stats");
            writer.ArrayBegin(_stats.Count);
            foreach (var s in _stats)
            {
                writer.TypeBegin("Placemaker.Stat");
                writer.PropertyName("key"); writer.Write(s.Key ?? "");
                writer.PropertyName("value"); writer.Write(s.Value);
                writer.PropertyName("unit"); writer.Write(s.Unit ?? "");
                writer.TypeEnd();
            }
            writer.ArrayEnd();
            writer.PropertyName("upkeep");
            writer.Write(_upkeep);
            writer.PropertyName("memberCount");
            writer.Write(_memberCount);
            // Extras (UI may ignore): repair offer, "edited by other tools" hint, removal in progress.
            writer.PropertyName("missingAssets");
            writer.Write(_missingAssets);
            writer.PropertyName("modified");
            writer.Write(_modified);
            writer.PropertyName("removing");
            writer.Write(_removing);
            writer.PropertyName("fee");
            writer.Write(_fee);
            writer.PropertyName("feeOn");
            writer.Write(_feeOn);
        }

        [Preserve]
        public PlacemakerGroupSection() { }
    }
}
