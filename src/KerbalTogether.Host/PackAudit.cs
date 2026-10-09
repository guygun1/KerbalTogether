using System.Text.Json;
using KerbalTogether.Core;

namespace KerbalTogether.Host;

public static class PackAudit
{
    public static PackManifest Scan(string root)
    {
        var result = PackManifest.Scan(root);
        var registry = Path.Combine(root, "CKAN", "registry.json");
        if (File.Exists(registry))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(registry));
            foreach (var module in doc.RootElement.GetProperty("installed_modules").EnumerateObject())
            {
                var source = module.Value.GetProperty("source_module");
                result.Mods.Add(new PackMod { Id = module.Name, Version = source.GetProperty("version").GetString()! });
            }
        }
        return result;
    }

    public static List<string> Notes(IEnumerable<string> modules)
    {
        var result = new List<string>();
        foreach (var id in modules.OrderBy(x => x))
        {
            var name = id.ToLowerInvariant();
            string? reason = null;
            if (name.Contains("kerbalism")) reason = "UNVERIFIED: global science, kerbal health, radiation and background vessel simulation need an authoritative multiplayer adapter. Matching files is insufficient.";
            else if (name.Contains("kerbalcolonies") || name == "uks" || name.Contains("extraplanetary") || name.Contains("extraplanetarylaunchpads"))
                reason = "UNVERIFIED: colony/construction state and background production need ownership and simultaneous-build testing.";
            else if (name.Contains("bonvoyage") || name.Contains("backgroundresource") || name.Contains("systemheat") || name.Contains("cryotanks"))
                reason = "UNVERIFIED: background simulation can run independently on each player; test unloaded vessels and time advancement.";
            else if (name.Contains("bettertimewarp") || name.Contains("fmrs") || name.Contains("quickstart"))
                reason = "CONFLICT RISK: overrides time, save/revert or startup flow used by multiplayer. Use Luna's warp and join flow.";
            else if (name == "kas" || name == "kis" || name.Contains("infernalrobotics"))
                reason = "UNVERIFIED: inventory transfers, attachment and robotics require two-client mutation/physics testing.";
            else if (name.Contains("kopernicus") || name.Contains("outerplanets") || name.Contains("promisedworlds") || name.Contains("betterkerbol") || name.Contains("betterdres") || name.Contains("parallax"))
                reason = "MATCH REQUIRED: same bodies, terrain configuration and terrain quality on both clients; test landing and surface contacts.";
            else if (name.Contains("scansat") || name.Contains("testflight") || name.Contains("finalfrontier"))
                reason = "UNVERIFIED: persistent scenario data requires simultaneous-update and reconnect testing.";
            if (reason != null) result.Add(id + ": " + reason);
        }
        result.Add("Other mods: compatibility unverified. Luna synchronizes persistent part fields and scenario snapshots, but this is not proof of correct synchronization for arbitrary mods.");
        result.Add("Pack identity check covers DLL/CFG/XML/version files and CKAN versions. Textures/models, PluginData and ModuleManager's generated cache are not compared.");
        return result;
    }
}
