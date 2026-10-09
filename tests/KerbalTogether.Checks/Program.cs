using System.Net;
using System.Xml.Linq;
using KerbalTogether.Core;
using KerbalTogether.Host;
using System.Diagnostics;

var temp = Path.Combine(Path.GetTempPath(), "opencode", "kerbal-together-check-" + Guid.NewGuid());
Directory.CreateDirectory(temp);
var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
try
{
    var data = Path.Combine(temp, "GameData");
    Directory.CreateDirectory(Path.Combine(data, "UnknownMod"));
    File.WriteAllText(Path.Combine(data, "UnknownMod", "Plugin.dll"), "fake plugin content");
    File.WriteAllText(Path.Combine(data, "UnknownMod", "patch.cfg"), "@PART[*] { mass = 1 }");
    File.WriteAllText(Path.Combine(data, "ModuleManager.dll"), "root-level dll");
    File.WriteAllText(Path.Combine(temp, "buildID64.txt"), "version = 1.12.5");
    var host = PackManifest.Scan(temp);
    Check(host.Files.Count == 3, "Includes unknown mods, configurations and root-level DLLs");
    var manifestFile = Path.Combine(temp, "pack.xml");
    host.Save(manifestFile);
    Check(PackManifest.Load(manifestFile).Compare(host).Count == 0, "Manifest round trip and matching pack");
    File.WriteAllText(Path.Combine(data, "UnknownMod", "patch.cfg"), "@PART[*] { mass = 2 }");
    File.Delete(Path.Combine(data, "UnknownMod", "Plugin.dll"));
    File.WriteAllText(Path.Combine(data, "UnknownMod", "extra.cfg"), "RESOURCE_DEFINITION {}");
    var differences = host.Compare(PackManifest.Scan(temp));
    Check(differences.Any(x => x.StartsWith("MISSING:")) && differences.Any(x => x.StartsWith("DIFFERENT:")) && differences.Any(x => x.StartsWith("EXTRA:")), "Missing DLL, changed gameplay patch and extra patch are distinguished");
    Check(!PackManifest.Include("Unknown/PluginData/settings.cfg") && !PackManifest.Include("texture.dds") && PackManifest.Include("Kerbalism/Settings.cfg"), "Exclude user data/textures without excluding gameplay settings");
    Check(PackAudit.Notes(new[] { "Kerbalism", "SomethingRare" }).Any(x => x.StartsWith("Kerbalism:") && x.Contains("authoritative")), "Kerbalism database/simulation concern is surfaced");
    Check(PackAudit.Notes(new[] { "SomethingRare" }).Any(x => x.StartsWith("Other mods:") && x.Contains("unverified")), "Unknown mods are not falsely certified");
    Check(!RouterMapping.IsPublicIPv4(IPAddress.Parse("100.64.1.2")) && !RouterMapping.IsPublicIPv4(IPAddress.Parse("192.168.0.1")) && RouterMapping.IsPublicIPv4(IPAddress.Parse("8.8.8.8")), "CGNAT and private WAN addresses are distinguished");
    var modFile = Path.Combine(temp, "LMPModControl.xml");
    File.WriteAllText(modFile, "<ModControlStructure><AllowedParts><string>modded.part</string></AllowedParts><AllowedResources><string>ExoticResource</string></AllowedResources><MandatoryPlugins/><OptionalPlugins><DllFile><FilePath>mod/plugin.dll</FilePath><Sha>AA-BB</Sha></DllFile></OptionalPlugins></ModControlStructure>");
    ServerConfig.Prepare(temp, 18800, "friends&amp;<", "Test session", true, modFile);
    var config = Path.Combine(temp, "Config");
    var strict = XDocument.Load(Path.Combine(config, "LMPModControl.xml")).Root!;
    Check(strict.Element("MandatoryPlugins")!.Elements().Count() == 1 && !strict.Element("OptionalPlugins")!.Elements().Any() && strict.Element("AllowNonListedPlugins")!.Value == "false", "Modded plugins become mandatory with preserved Luna hash format");
    Check(strict.Element("AllowedResources")!.Value == "ExoticResource", "Loaded modded resources are retained");
    Check(strict.Element("MandatoryParts")!.Elements().Single().Element("PartName")!.Value == "modded.part", "Missing loaded modded parts are required at Luna handshake");
    Check(XDocument.Load(Path.Combine(config, "GeneralSettings.xml")).Root!.Element("Password")!.Value == "friends&amp;<", "Password XML escaping round trip");
    Check(XDocument.Load(Path.Combine(config, "MasterServerSettings.xml")).Root!.Element("RegisterWithMasterServer")!.Value == "true", "Rendezvous enabled for mapping-failed configuration");
    ServerConfig.Prepare(temp, 18800, "friends", "Test session", false, modFile);
    Check(XDocument.Load(Path.Combine(config, "MasterServerSettings.xml")).Root!.Element("RegisterWithMasterServer")!.Value == "false", "Direct-host configuration disables rendezvous");
    Check(XDocument.Load(Path.Combine(config, "WarpSettings.xml")).Root!.Element("WarpMode")!.Value == "None", "Separate simulation clocks disabled");
    var statePacket = new CompatibilityPacket { Kind = CompatibilityKind.Scenario, ModuleType = "UnknownMod:GlobalState", Sequence = 1, Epoch = 1, State = new SavedNode { Name = "GlobalState", Values = new List<SavedValue> { new() { Name = "json", Value = "{\"science\":12}" }, new() { Name = "duplicate", Value = "a" }, new() { Name = "duplicate", Value = "b" } }, Children = new List<SavedNode> { new() { Name = "vessel", Values = new List<SavedValue> { new() { Name = "id", Value = "test" } } } } } };
    var encoded = statePacket.Encode();
    var restored = CompatibilityPacket.Decode(encoded, encoded.Length);
    Check(restored.State.Values[0].Value == "{\"science\":12}" && restored.State.Values.Count == 3 && restored.State.Children[0].Values[0].Value == "test", "Generic node transport preserves JSON braces, duplicate keys and nested state");
    var rejected = false;
    try { CompatibilityPacket.Decode(encoded, encoded.Length - 1); } catch (Exception) { rejected = true; }
    Check(rejected, "Truncated compatibility snapshots are rejected");
    rejected = false;
    try { CompatibilityPacket.Decode(new byte[CompatibilityPacket.MaxBytes + 1], CompatibilityPacket.MaxBytes + 1); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Oversized compatibility snapshots are rejected");
    var authority = new CompatibilityAuthority();
    authority.Join("host"); authority.Join("friend");
    Check(authority.Leader == "host" && authority.IsParticipant("friend"), "Every client must negotiate and first participant becomes global writer");
    var partPacket = new CompatibilityPacket { Kind = CompatibilityKind.PartFields, Vessel = Guid.NewGuid(), Part = 4, ModuleIndex = 1, ModuleType = "RareMod:PartState", Sequence = 1, Owner = "forged" };
    Check(!authority.Accept(partPacket, "friend", "host", false), "Server rejects part writes by non-owning clients");
    Check(authority.Accept(partPacket, "host", "host", false) && partPacket.Owner == "host", "Owning client snapshot accepted and supplied owner replaced");
    Check(!authority.Accept(partPacket, "host", "host", false), "Repeated or stale module snapshots rejected");
    partPacket.ModuleIndex = 2;
    Check(authority.Accept(partPacket, "host", "host", false), "Repeated module instances are distinct streams by module index");
    Check(!authority.Accept(statePacket, "friend", "", true) && !authority.Accept(statePacket, "host", "", false), "Global-state writes require elected leader and enabled scenario mode");
    Check(authority.Accept(statePacket, "host", "", true), "Generic global state accepts the elected writer");
    authority.Leave("host");
    Check(authority.Leader == "friend" && authority.Epoch == 2 && !authority.Accept(statePacket, "friend", "", true), "Leader handover advances epoch and rejects pre-handover state");
    statePacket.Epoch = authority.Epoch; statePacket.Sequence = 2;
    Check(authority.Accept(statePacket, "friend", "", true), "New elected writer can publish state after handover");
    var oldCulture = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        Check(PersistentScalar.Format(1.125d, typeof(double)) == "1.125" && (double)PersistentScalar.Parse("1.125", typeof(double)) == 1.125d, "Generic numeric field transport is culture-independent");
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = oldCulture; }
    Check(!PersistentScalar.Supports(typeof(Dictionary<string, int>)) && PersistentScalar.Supports(typeof(DayOfWeek)), "Unsupported object graphs are not blindly serialized; enums are supported");
    if (args.Length == 2 && args[0] == "--server")
    {
        var smokeDir = Path.Combine(temp, "server-smoke");
        Directory.CreateDirectory(smokeDir);
        var serverSource = Path.Combine(Path.GetFullPath(args[1]), "KerbalTogether.Server.exe");
        File.Copy(serverSource, Path.Combine(smokeDir, "KerbalTogether.Server.exe"));
        ServerConfig.Prepare(smokeDir, 18809, "test-session", "Lifecycle smoke test", false, modFile);
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var process = new Process { StartInfo = new ProcessStartInfo(Path.Combine(smokeDir, "KerbalTogether.Server.exe")) { WorkingDirectory = smokeDir, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.Environment["KERBAL_TOGETHER_DATA"] = smokeDir;
        process.OutputDataReceived += (_, e) => { if (e.Data != null) { Console.WriteLine(e.Data); if (e.Data.Contains("All systems up and running")) ready.TrySetResult(true); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Check(!process.HasExited, "Bundled server starts with generated modded config, no router/public rendezvous");
            await process.StandardInput.WriteLineAsync("/shutdown");
            await process.StandardInput.FlushAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Check(process.ExitCode == 0, "Graceful shutdown exits successfully");
            Check(Directory.Exists(Path.Combine(smokeDir, "Universe")), "Universe storage initialized");
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
    Console.WriteLine($"{checks} checks passed.");
}
finally { Directory.Delete(temp, true); }
