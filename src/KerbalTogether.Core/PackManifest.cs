using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Serialization;

namespace KerbalTogether.Core
{
    public class PackFile
    {
        public string Path { get; set; }
        public string Hash { get; set; }
    }

    public class PackMod
    {
        public string Id { get; set; }
        public string Version { get; set; }
    }

    public class PackManifest
    {
        public int Schema { get; set; } = 1;
        public string GameVersion { get; set; }
        public List<PackFile> Files { get; set; } = new List<PackFile>();
        public List<PackMod> Mods { get; set; } = new List<PackMod>();

        public static PackManifest Scan(string kspRoot)
        {
            var data = System.IO.Path.Combine(kspRoot, "GameData");
            if (!Directory.Exists(data)) throw new DirectoryNotFoundException("Choose the KSP folder containing GameData.");
            var build = System.IO.Path.Combine(kspRoot, "buildID64.txt");
            var result = new PackManifest { GameVersion = File.Exists(build) ? File.ReadAllText(build).Trim() : "Unknown" };
            foreach (var file in Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var relative = file.Substring(data.Length + 1).Replace('\\', '/');
                if (!Include(relative)) continue;
                using (var stream = File.OpenRead(file))
                using (var sha = SHA256.Create())
                    result.Files.Add(new PackFile { Path = relative, Hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") });
            }
            return result;
        }

        public static bool Include(string path)
        {
            var segments = path.Split('/');
            if (segments.Any(s => s.Equals("PluginData", StringComparison.OrdinalIgnoreCase))) return false;
            if (path.StartsWith("KerbalTogether/", StringComparison.OrdinalIgnoreCase)) return false;
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext == ".dll" || ext == ".cfg" || ext == ".version" || ext == ".xml";
        }

        public void Save(string path)
        {
            using (var stream = File.Create(path)) new XmlSerializer(typeof(PackManifest)).Serialize(stream, this);
        }

        public static PackManifest Load(string path)
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Pack manifest is too large.");
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                var manifest = (PackManifest)new XmlSerializer(typeof(PackManifest)).Deserialize(reader);
                if (manifest.Schema != 1) throw new InvalidDataException("Unsupported pack manifest version.");
                return manifest;
            }
        }

        // The supplied manifest describes the host; messages describe what the joining player should change.
        public List<string> Compare(PackManifest local)
        {
            var messages = new List<string>();
            if (GameVersion != local.GameVersion) messages.Add("KSP build differs. Install the host's KSP version and DLC updates.");
            var ours = local.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            var required = Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in required.Values)
            {
                if (!ours.TryGetValue(entry.Path, out var found)) messages.Add("MISSING: " + entry.Path + " — install the host's mod/DLC.");
                else if (!string.Equals(entry.Hash, found.Hash, StringComparison.OrdinalIgnoreCase))
                    messages.Add("DIFFERENT: " + entry.Path + " — use the same release and configuration as the host.");
            }
            foreach (var entry in ours.Values.Where(f => !required.ContainsKey(f.Path)))
                messages.Add("EXTRA: " + entry.Path + " — absent on host; review its gameplay effects.");
            var localMods = local.Mods.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var mod in Mods)
                if (localMods.TryGetValue(mod.Id, out var found) && mod.Version != found.Version)
                    messages.Add("VERSION: " + mod.Id + " — host " + mod.Version + ", yours " + found.Version);
            return messages;
        }
    }
}
