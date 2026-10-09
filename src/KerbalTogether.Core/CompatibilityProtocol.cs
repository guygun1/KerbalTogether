using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KerbalTogether.Core
{
    public enum CompatibilityKind : byte { Hello = 1, Authority = 2, PartFields = 3, Scenario = 4 }

    public class SavedValue
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
    }

    // Preserves duplicate values, nested nodes and literal braces without reparsing ConfigNode text.
    public class SavedNode
    {
        public string Name { get; set; } = "";
        public List<SavedValue> Values { get; set; } = new List<SavedValue>();
        public List<SavedNode> Children { get; set; } = new List<SavedNode>();
    }

    public class CompatibilityPacket
    {
        public const string Channel = "KerbalTogether.Compatibility.v1";
        public const int MaxBytes = 256 * 1024;
        public CompatibilityKind Kind { get; set; }
        public string Owner { get; set; } = "";
        public long Epoch { get; set; }
        public long Sequence { get; set; }
        public Guid Vessel { get; set; }
        public uint Part { get; set; }
        public int ModuleIndex { get; set; }
        public string ModuleType { get; set; } = "";
        public bool ScenarioAuthority { get; set; }
        public SavedNode State { get; set; } = new SavedNode();
        public string Key => Kind == CompatibilityKind.Scenario ? "scenario:" + ModuleType : Vessel + ":" + Part + ":" + ModuleIndex + ":" + ModuleType;

        public byte[] Encode()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(0x4b544731); writer.Write((byte)Kind);
                writer.Write(Owner); writer.Write(Epoch); writer.Write(Sequence);
                writer.Write(Vessel.ToByteArray()); writer.Write(Part); writer.Write(ModuleIndex);
                writer.Write(ModuleType); writer.Write(ScenarioAuthority);
                var budget = 8192;
                WriteNode(writer, State, 0, ref budget);
                writer.Flush();
                if (stream.Length > MaxBytes) throw new InvalidDataException("Compatibility snapshot exceeds 256 KiB.");
                return stream.ToArray();
            }
        }

        public static CompatibilityPacket Decode(byte[] bytes, int count)
        {
            if (count < 0 || count > MaxBytes || count > bytes.Length) throw new InvalidDataException("Invalid compatibility packet size.");
            using (var stream = new MemoryStream(bytes, 0, count, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadInt32() != 0x4b544731) throw new InvalidDataException("Unknown compatibility protocol.");
                var packet = new CompatibilityPacket { Kind = (CompatibilityKind)reader.ReadByte(), Owner = reader.ReadString(), Epoch = reader.ReadInt64(), Sequence = reader.ReadInt64(), Vessel = new Guid(reader.ReadBytes(16)), Part = reader.ReadUInt32(), ModuleIndex = reader.ReadInt32(), ModuleType = reader.ReadString(), ScenarioAuthority = reader.ReadBoolean() };
                if (!Enum.IsDefined(typeof(CompatibilityKind), packet.Kind)) throw new InvalidDataException("Unknown compatibility message kind.");
                var budget = 8192;
                packet.State = ReadNode(reader, 0, ref budget);
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing compatibility data.");
                return packet;
            }
        }

        private static void WriteNode(BinaryWriter w, SavedNode node, int depth, ref int budget)
        {
            if (depth > 32 || --budget < 0) throw new InvalidDataException("Snapshot tree exceeds bounds.");
            w.Write(node.Name); w.Write(node.Values.Count);
            foreach (var value in node.Values) { if (--budget < 0) throw new InvalidDataException("Too many saved values."); w.Write(value.Name); w.Write(value.Value); }
            w.Write(node.Children.Count);
            foreach (var child in node.Children) WriteNode(w, child, depth + 1, ref budget);
        }

        private static SavedNode ReadNode(BinaryReader r, int depth, ref int budget)
        {
            if (depth > 32 || --budget < 0) throw new InvalidDataException("Snapshot tree exceeds bounds.");
            var node = new SavedNode { Name = r.ReadString() };
            var values = r.ReadInt32();
            if (values < 0 || values > budget) throw new InvalidDataException("Invalid saved value count.");
            for (var i = 0; i < values; i++) { budget--; node.Values.Add(new SavedValue { Name = r.ReadString(), Value = r.ReadString() }); }
            var children = r.ReadInt32();
            if (children < 0 || children > budget) throw new InvalidDataException("Invalid saved child count.");
            for (var i = 0; i < children; i++) node.Children.Add(ReadNode(r, depth + 1, ref budget));
            return node;
        }
    }

    public sealed class CompatibilityAuthority
    {
        public string Leader { get; private set; } = "";
        public long Epoch { get; private set; }
        private readonly List<string> participants = new List<string>();
        private readonly Dictionary<string, long> sequences = new Dictionary<string, long>();
        public bool IsParticipant(string player) => participants.Contains(player);

        public bool Join(string player)
        {
            if (!participants.Contains(player)) participants.Add(player);
            if (Leader.Length != 0) return false;
            Leader = player; Epoch++; return true;
        }

        public bool Leave(string player)
        {
            participants.Remove(player);
            sequences.Clear(); // Reconnects and ownership handovers receive fresh sequence streams.
            if (Leader != player) return false;
            Leader = participants.Count > 0 ? participants[0] : "";
            Epoch++; return true;
        }

        public bool Accept(CompatibilityPacket packet, string sender, string vesselOwner, bool scenarios)
        {
            if (!participants.Contains(sender) || packet.Sequence <= 0) return false;
            if (packet.Kind == CompatibilityKind.PartFields)
            {
                if (packet.Vessel == Guid.Empty || packet.ModuleIndex < 0 || vesselOwner != sender) return false;
            }
            else if (packet.Kind == CompatibilityKind.Scenario)
            {
                if (!scenarios || sender != Leader || packet.Epoch != Epoch || packet.ModuleType.Length == 0) return false;
            }
            else return false;
            var key = sender + "|" + packet.Key;
            if (sequences.TryGetValue(key, out var previous) && packet.Sequence <= previous) return false;
            if (sequences.Count >= 32768 && !sequences.ContainsKey(key)) return false;
            sequences[key] = packet.Sequence;
            packet.Owner = sender; // Never trust a client-supplied owner.
            return true;
        }
    }
}
