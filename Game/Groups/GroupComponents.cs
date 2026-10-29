// Placemaker — persistent ECS components for build groups (GPL-3.0).
// Version-first ISerializable layouts (RESEARCH-NOTES rule 5): the CS2 serializer packs
// components back to back without lengths, so readers must consume exactly what the writer
// wrote for that version. Fields are only ever appended behind a version check.
//
// Ownership model (see docs/proposals/gamebuild.md): one headless record entity per group plus a
// PlacemakerMember tag on every top-level member. Members stay vanilla top-level entities, so
// vanilla tools (bulldozer, Move It, upgrades) keep working on them; Placemaker finds its group
// through the tag in O(1).

using Colossal.Serialization.Entities;
using Placemaker.Core.Build;
using Unity.Entities;

namespace Placemaker.Game.Groups
{
    /// <summary>Tag on every member (net edge/node, object, area, water source, carrier, companion).</summary>
    public struct PlacemakerMember : IComponentData, IQueryTypeParameter, ISerializable
    {
        public const int CurrentVersion = 1;
        public int GroupId;
        public MemberKind Kind;
        public MemberRole Role;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(GroupId);
            writer.Write((byte)Kind);
            writer.Write((byte)Role);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out GroupId);
            reader.Read(out byte kind);
            reader.Read(out byte role);
            Kind = (MemberKind)kind;
            Role = (MemberRole)role;
            // version >= 2: read appended fields here.
        }
    }

    /// <summary>Why a member exists — drives carrier restoration and economy. Save format: never renumber.</summary>
    public enum MemberRole : byte
    {
        Generic = 0,
        ParkingAisle = 1,     // drivable invisible path inside a parking lot (carrier SubNet)
        ParkingBay = 2,       // parking decal with a real ParkingLane
        Carrier = 3,          // bare owner of parking objects
        Companion = 4,        // hidden economy building
        WaterSource = 5,
        Surface = 6,
        Zoning = 7,
        /// <summary>The "Placemaker Area" lot covering the outline: the ONE selectable/bulldozable thing (US7).</summary>
        OwnerArea = 8,
        /// <summary>(s19) Invisible physical "Placemaker Zone Block" lot: blocks the road zone cells under the build.</summary>
        ZoneBlocker = 9,
        /// <summary>(s17) Hidden 1-cell park building carrying the build's park stats (Economy/ParkCompanionSystem).</summary>
        ParkCompanion = 10,
    }

    /// <summary>The group record. Lives on a headless entity together with <see cref="PlacemakerGroupBlob"/>.</summary>
    public struct PlacemakerGroup : IComponentData, IQueryTypeParameter, ISerializable
    {
        public const int CurrentVersion = 2;
        public int GroupId;
        public int Flags;          // GroupFlags
        public Entity Carrier;     // parking carrier or Entity.Null
        public Entity Companion;   // economy companion building or Entity.Null
        public int BuiltMembers;   // members at build time (Modified detection)
        /// <summary>v2: owner lot area ("Placemaker Area" prefab) or Entity.Null (old groups, prefab unavailable).</summary>
        public Entity Owner;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(GroupId);
            writer.Write(Flags);
            writer.Write(Carrier);
            writer.Write(Companion);
            writer.Write(BuiltMembers);
            writer.Write(Owner);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out GroupId);
            reader.Read(out Flags);
            reader.Read(out Carrier);
            reader.Read(out Companion);
            reader.Read(out BuiltMembers);
            Owner = Entity.Null;
            if (version >= 2) reader.Read(out Owner);
        }
    }

    /// <summary>
    /// Encoded <see cref="GroupRecord"/> (ReceiptCodec, itself versioned). One byte per element —
    /// ECS components cannot hold managed strings; ParkingLotTool used the same trick for its texts.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct PlacemakerGroupBlob : IBufferElementData, ISerializable
    {
        public byte Value;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(Value);
        public void Deserialize<TReader>(TReader reader) where TReader : IReader => reader.Read(out Value);
    }

    /// <summary>
    /// (dynamic decals) The build's grime strokes, encoded by Core/Decals/Dynamic/DecalCodec (version first), on the group record.
    /// Regenerated into code-made decals after a load (Game/Decals/DynamicDecalSystem); never an entity or a prefab. Without the
    /// mod the game skips this component type like any other unknown one: the city simply loses the grime.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct PlacemakerDecalBlob : IBufferElementData, ISerializable
    {
        public byte Value;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(Value);
        public void Deserialize<TReader>(TReader reader) where TReader : IReader => reader.Read(out Value);
    }

    /// <summary>Removal in progress (persisted so a save during removal resumes it after load).</summary>
    public struct PlacemakerRemoval : IComponentData, IQueryTypeParameter, ISerializable
    {
        public const int CurrentVersion = 1;
        public int Passes;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(Passes);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out Passes);
            Passes = 0;   // recheck references after loading instead of trusting an old pass count
        }
    }

    /// <summary>Record still being built: excluded from empty-group cleanup. Not serialized on purpose.</summary>
    public struct PlacemakerUnderConstruction : IComponentData, IQueryTypeParameter { }
}
