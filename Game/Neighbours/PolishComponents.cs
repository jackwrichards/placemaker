// Placemaker neighbourhood polish — what is saved with the city (GPL-3.0).
// Version-first ISerializable layouts (RESEARCH-NOTES rule 5, as Game/Groups/GroupComponents.cs): the serializer packs
// components back to back without lengths, so a reader consumes exactly what its version wrote; fields are only appended.
//
//   * PolishedLot on a growable building: the player's yes/no flag (the painter, "Polish new houses"). Only yes lots are
//     ever edited. Level-ups keep the entity (and the flag); a replaced building gets the flag back by position.
//   * PolishPiece on a surface or object the polish made for a house (bed, path, paving, owned lawn, bush, rock, shared
//     green): after a load the polish still knows which of the house's pieces are its own, so Unpolish takes them away.
//   * PolishLookArea (+ PolishLookAreaPoint, PolishLookOverride buffers) on a headless entity: an area zoned with a look
//     (a Zoning type's "Zone this area"); buildings that grow in it get the look and its changes.
//   * PolishLawnOriginal on one of the house's own lawns the polish reshaped: its nodes as the game made them, so Unpolish
//     (and a later plan) gives the lawn back exactly, also after a load.
//   * PolishGarden on a polished house: its garden's outline as its last plan wrote it (neighbours planned later keep to it).
//   * PolishCitySwitches on one headless entity: the city's own Outgrow switches (new buildings, the master switch).
//   * PolishCitySettings: only read once from a city saved by a build of 2026-10-20 (its "Outgrow new buildings" moves into
//     PolishCitySwitches; NeighbourPolishSystem.Switches), kept so such saves load.

using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public struct PolishedLot : IComponentData, IQueryTypeParameter, ISerializable
    {
        public const int CurrentVersion = 5;
        public bool Yes;
        /// <summary>(v2) How the garden is dressed: 0 by the zone (the settings), 1 garden, 2 natural, 3 tidy, 4 yard
        /// (NeighbourPolishSystem.ThemeNames). Never renumber.</summary>
        public byte Theme;
        /// <summary>(v3) The theme was picked with the painter: "Redo polished buildings" keeps it. Else it came from the
        /// setting for the building's kind when the lot was polished.</summary>
        public bool Picked;
        /// <summary>(v4) The look the lot was polished with (the painter's options; new buildings: the options then):
        /// Parts (NeighbourPolishSystem.Part* bits) and the painted-garden share 0–100. Own false (older lots): the
        /// settings.</summary>
        public bool Own;
        public byte Parts, Painted;
        /// <summary>(v5) The look it was polished with (PolishLooks.Hash of its id; 0: none, a lot polished before looks).
        /// The changes not saved to the look are in its PolishLookOverride buffer.</summary>
        public int Look;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(Yes);
            writer.Write(Theme);
            writer.Write(Picked);
            writer.Write(Own);
            writer.Write(Parts);
            writer.Write(Painted);
            writer.Write(Look);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out Yes);
            if (version >= 2) reader.Read(out Theme); else Theme = 0;
            if (version >= 3) reader.Read(out Picked); else Picked = Theme != 0;   // v2: only the painter gave a theme
            if (version >= 4) { reader.Read(out Own); reader.Read(out Parts); reader.Read(out Painted); }
            else { Own = false; Parts = 0; Painted = 0; }
            if (version >= 5) reader.Read(out Look); else Look = 0;
        }
    }

    /// <summary>Save format: never renumber.</summary>
    public enum PolishPieceKind : byte
    {
        /// <summary>Bed, path, paving, owned lawn, bush or rock: re-planned with the garden.</summary>
        Garden = 0,
        /// <summary>The shared green in the middle of a ring (and its bed): only a plan that sees the whole ring makes it.</summary>
        Common = 1,
    }

    public struct PolishPiece : IComponentData, IQueryTypeParameter, ISerializable
    {
        public const int CurrentVersion = 1;
        public PolishPieceKind Kind;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write((byte)Kind);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out byte kind);
            Kind = (PolishPieceKind)kind;
        }
    }

    [InternalBufferCapacity(0)]
    /// <summary>A setting the lot was polished with that its look does not have (the look changed in the tool and not
    /// saved): knob (PolishLooks.Hash of its name; "theme": the base theme's index) and value.</summary>
    public struct PolishLookOverride : IBufferElementData, ISerializable
    {
        public int Knob;
        public float Value;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(Knob);
            writer.Write(Value);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Knob);
            reader.Read(out Value);
        }
    }

    /// <summary>Outgrow's settings kept with the city (one headless entity; a city without one: the defaults).</summary>
    public struct PolishCitySettings : IComponentData, IQueryTypeParameter, ISerializable
    {
        public const int CurrentVersion = 1;
        /// <summary>"Outgrow new buildings": buildings that grow from now on are outgrown (a new city: off).</summary>
        public bool NewBuildings;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(NewBuildings);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out NewBuildings);
        }
    }

    /// <summary>An area zoned with a look: buildings that grow in it get it (NeighbourPolishSystem.Areas).</summary>
    public struct PolishLookArea : IComponentData, IQueryTypeParameter, ISerializable
    {
        public const int CurrentVersion = 2;
        /// <summary>PolishLooks.Hash of the look's id.</summary>
        public int Look;
        /// <summary>Newer areas win where they overlap.</summary>
        public int Order;
        public float2 Min, Max;
        /// <summary>(v2) Only buildings of this zone (PolishLooks.Hash of NeighbourPolishSystem.ZoneKey; 0: any zone).</summary>
        public int Zone;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(Look);
            writer.Write(Order);
            writer.Write(Min);
            writer.Write(Max);
            writer.Write(Zone);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out Look);
            reader.Read(out Order);
            reader.Read(out Min);
            reader.Read(out Max);
            if (version >= 2) reader.Read(out Zone); else Zone = 0;
        }
    }

    [InternalBufferCapacity(0)]
    public struct PolishLookAreaPoint : IBufferElementData, ISerializable
    {
        public float2 Position;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(Position);
        public void Deserialize<TReader>(TReader reader) where TReader : IReader => reader.Read(out Position);
    }

    public struct PolishLawnOriginal : IBufferElementData, ISerializable
    {
        public float3 Position;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(Position);
        public void Deserialize<TReader>(TReader reader) where TReader : IReader => reader.Read(out Position);
    }

    /// <summary>A polished house's garden as its last plan wrote it (the outline its fences and lawn follow). The plans of
    /// its neighbours keep off it and meet it on its real edges, also after a load (kept only in memory, a city loaded
    /// again had its gardens planned through each other: hedges crossing, strips nobody fenced).</summary>
    [InternalBufferCapacity(0)]
    public struct PolishGarden : IBufferElementData, ISerializable
    {
        public float2 Position;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter => writer.Write(Position);
        public void Deserialize<TReader>(TReader reader) where TReader : IReader => reader.Read(out Position);
    }

    /// <summary>The city's own Outgrow switches (NeighbourPolishSystem.Switches), on one headless entity. A city without it
    /// (new, or saved before the switches were per city) has new buildings off and Outgrow running.</summary>
    public struct PolishCitySwitches : IComponentData, IQueryTypeParameter, ISerializable
    {
        public const int CurrentVersion = 2;
        /// <summary>"Outgrow new buildings": buildings that grow get their zone's look.</summary>
        public bool NewBuildings;
        /// <summary>The master switch is off: nothing new outgrows and nothing is planned; nothing is put back either.</summary>
        public bool Paused;
        /// <summary>"Outgrow again when a building levels up" (version 2; a city saved before has it on).</summary>
        public bool LevelUps;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(NewBuildings);
            writer.Write(Paused);
            writer.Write(LevelUps);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out NewBuildings);
            reader.Read(out Paused);
            LevelUps = true;
            if (version >= 2) reader.Read(out LevelUps);
        }
    }
}
