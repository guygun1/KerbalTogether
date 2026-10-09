using System.Xml.Linq;

namespace KerbalTogether.Host;

public static class ServerConfig
{
    public static void Prepare(string serverDir, int port, string password, string name, bool rendezvous, string modFile, bool genericScenarios = false)
    {
        var config = Path.Combine(serverDir, "Config");
        Directory.CreateDirectory(config);
        var scenarioFlag = Path.Combine(config, "GenericScenarioAuthority.enabled");
        if (genericScenarios) File.WriteAllText(scenarioFlag, "Experimental generic scenario single-writer mode");
        else if (File.Exists(scenarioFlag)) File.Delete(scenarioFlag);
        Set(config, "Connection", ("ListenAddress", "::"), ("Port", port.ToString()), ("Upnp", "false"));
        Set(config, "MasterServer", ("RegisterWithMasterServer", rendezvous.ToString().ToLowerInvariant()));
        Set(config, "Website", ("EnableWebsite", "false"));
        Set(config, "General", ("ServerName", name), ("Password", password), ("ModControl", "true"), ("AutoDekessler", "0"));
        // No independent subspace clocks for background-simulation-heavy packs.
        Set(config, "Warp", ("WarpMode", "None"));
        var mods = XDocument.Load(modFile);
        var root = mods.Root ?? throw new InvalidDataException("Empty LMP mod-control file.");
        if (root.Name.LocalName != "ModControlStructure" || root.Element("AllowedParts")?.Elements().Any() != true)
            throw new InvalidDataException("Generate the mod-control file in KSP after parts have loaded.");
        var mandatory = root.Element("MandatoryPlugins") ?? new XElement("MandatoryPlugins");
        if (mandatory.Parent == null) root.Add(mandatory);
        var optional = root.Element("OptionalPlugins");
        if (optional != null) foreach (var dll in optional.Elements().ToList()) { dll.Remove(); mandatory.Add(dll); }
        root.SetElementValue("AllowNonListedPlugins", "false");
        // A DLL match does not prove the client's ModuleManager patches produced
        // the same parts. Require the host's actual loaded part names at handshake.
        var requiredParts = root.Element("MandatoryParts") ?? new XElement("MandatoryParts");
        if (requiredParts.Parent == null) root.Add(requiredParts);
        var listed = requiredParts.Elements().Select(p => p.Element("PartName")?.Value).ToHashSet();
        foreach (var part in root.Element("AllowedParts")!.Elements().Select(p => p.Value).Distinct())
            if (listed.Add(part)) requiredParts.Add(new XElement("MandatoryPart", new XElement("PartName", part), new XElement("Text", "Required loaded part: " + part)));
        mods.Save(Path.Combine(config, "LMPModControl.xml"));
    }

    private static void Set(string dir, string kind, params (string Key, string Value)[] entries)
    {
        var file = Path.Combine(dir, kind + "Settings.xml");
        var doc = File.Exists(file) ? XDocument.Load(file) : new XDocument(new XElement(kind + "SettingsDefinition"));
        foreach (var item in entries) doc.Root!.SetElementValue(item.Key, item.Value);
        doc.Save(file);
    }
}
