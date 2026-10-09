using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KerbalTogether.Core;
using UnityEngine;

namespace KerbalTogether
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class GenericCompatibilityLayer : MonoBehaviour
    {
        private static GenericCompatibilityLayer instance;
        private readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();
        private readonly HashSet<Type> scenarioTypes = new HashSet<Type>();
        private readonly ConcurrentQueue<CompatibilityPacket> incoming = new ConcurrentQueue<CompatibilityPacket>();
        private readonly Dictionary<string, CompatibilityPacket> pending = new Dictionary<string, CompatibilityPacket>();
        private readonly Dictionary<string, string> lastSent = new Dictionary<string, string>();
        private readonly HashSet<MethodBase> patched = new HashSet<MethodBase>();
        private readonly HashSet<Type> failedScenarioLoads = new HashSet<Type>();
        private LunaBridge bridge;
        private Harmony harmony;
        private bool negotiated;
        private bool scenarioAuthority;
        private string leader = "";
        private long epoch;
        private long sequence;
        private float nextScan;
        private float nextHello;
        private float nextFull;
        private bool wasRunning;
        private bool applying;
        private bool running;
        private string player = "";
        private readonly Dictionary<Guid, string> owners = new Dictionary<Guid, string>();
        public static string Status { get; private set; } = "Generic layer: waiting for Luna.";

        private void Awake()
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            harmony = new Harmony("KerbalTogether.GenericCompatibility.v1");
            Discover();
        }

        private void Discover()
        {
            var report = new List<string> { "Generic runtime compatibility catalog", "Persistent scalar fields are auto-synced; module callbacks obey vessel ownership.", "Scenario authority is experimental and selected by the host.", "" };
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == typeof(PartModule).Assembly || assembly == typeof(GenericCompatibilityLayer).Assembly || assembly.GetName().Name.StartsWith("Lmp")) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }
                foreach (var type in types.Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters))
                {
                    if (typeof(PartModule).IsAssignableFrom(type))
                    {
                        var persistent = AllFields(type).Where(f => !f.IsStatic && !f.IsInitOnly && f.GetCustomAttributes(typeof(KSPField), true).Cast<KSPField>().Any(a => a.isPersistant)).ToArray();
                        var supported = persistent.Where(f => PersistentScalar.Supports(f.FieldType)).GroupBy(f => f.Name).Select(g => g.First()).ToArray();
                        if (supported.Length > 0) { fields[type] = supported; PatchCallbacks(type, false); }
                        report.Add(type.FullName + ": " + supported.Length + " scalar fields; " + (persistent.Length - supported.Length) + " fields need non-scalar handling.");
                        foreach (var unsupported in persistent.Except(supported)) report.Add("  Uncovered: " + unsupported.Name + " (" + unsupported.FieldType.FullName + ")");
                        if (type.GetMethod("OnSave", new[] { typeof(ConfigNode) })?.DeclaringType != typeof(PartModule)) report.Add("  Custom OnSave state: retained by Luna vessel snapshots; not live-synced by scalar field layer.");
                    }
                    else if (typeof(ScenarioModule).IsAssignableFrom(type))
                    {
                        scenarioTypes.Add(type); PatchCallbacks(type, true);
                        report.Add(type.FullName + ": generic saved-node scenario snapshots + single-writer callbacks (experimental).");
                    }
                    else if (typeof(MonoBehaviour).IsAssignableFrom(type))
                        report.Add(type.FullName + ": standalone controller — simulation/state hooks not covered automatically.");
                }
            }
            var dir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KerbalTogether", "PluginData");
            Directory.CreateDirectory(dir); File.WriteAllLines(Path.Combine(dir, "runtime-compatibility.txt"), report);
            Status = "Generic layer: " + fields.Count + " field schemas, " + scenarioTypes.Count + " scenario schemas discovered.";
        }

        private static IEnumerable<FieldInfo> AllFields(Type type)
        {
            for (var t = type; t != null && t != typeof(PartModule); t = t.BaseType)
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) yield return field;
        }

        private void PatchCallbacks(Type type, bool scenario)
        {
            // Patch actual declaring methods, once. UI/render/lifecycle callbacks are left intact.
            foreach (var name in new[] { "Update", "FixedUpdate", "LateUpdate", "OnUpdate", "OnFixedUpdate" })
            {
                var method = AccessTools.Method(type, name, Type.EmptyTypes);
                if (method == null || method.IsStatic || method.ReturnType != typeof(void) || method.DeclaringType.Assembly == typeof(PartModule).Assembly || !patched.Add(method)) continue;
                try { harmony.Patch(method, prefix: new HarmonyMethod(typeof(GenericCompatibilityLayer), scenario ? nameof(ScenarioTick) : nameof(PartTick))); }
                catch (Exception e) { Debug.LogWarning("[KerbalTogether] Cannot guard " + type.FullName + "." + name + ": " + e.Message); }
            }
        }

        public static bool PartTick(PartModule __instance)
        {
            var layer = instance;
            if (layer == null || !layer.negotiated || !layer.running || layer.applying || !layer.fields.ContainsKey(__instance.GetType())) return true;
            var vessel = __instance.vessel;
            if (vessel == null || !vessel.loaded || HighLogic.LoadedSceneIsEditor) return true;
            return layer.owners.TryGetValue(vessel.id, out var owner) && owner == layer.player;
        }

        public static bool ScenarioTick(ScenarioModule __instance)
        {
            var layer = instance;
            if (layer == null || !layer.negotiated || !layer.running || !layer.scenarioAuthority || layer.applying || !layer.scenarioTypes.Contains(__instance.GetType())) return true;
            return layer.leader == layer.player && !layer.failedScenarioLoads.Contains(__instance.GetType());
        }

        private void Receive(string channel, byte[] bytes)
        {
            if (channel != CompatibilityPacket.Channel || incoming.Count >= 512) return;
            try { incoming.Enqueue(CompatibilityPacket.Decode(bytes, bytes.Length)); }
            catch (Exception e) { Debug.LogWarning("[KerbalTogether] Dropped compatibility snapshot: " + e.Message); }
        }

        private void Update()
        {
            if (bridge == null)
            {
                if (Time.realtimeSinceStartup < nextHello || LunaBridge.Find("LmpClient.MainSystem") == null) return;
                nextHello = Time.realtimeSinceStartup + 2;
                try
                {
                    bridge = new LunaBridge(Receive);
                    harmony.Patch(LunaBridge.Find("LmpClient.Systems.ModApi.ModApiMessageSender").GetMethod("SendMessage"),
                        prefix: new HarmonyMethod(typeof(GenericCompatibilityLayer), nameof(ReliableSnapshots)));
                }
                catch (Exception e) { Status = "Generic layer bind: " + e.Message; return; }
            }
            running = bridge.Running;
            player = bridge.Player;
            if (!running)
            {
                if (wasRunning) ResetSession();
                wasRunning = false; return;
            }
            if (!wasRunning) { ResetSession(); wasRunning = true; nextHello = 0; }
            owners.Clear();
            foreach (var vessel in FlightGlobals.Vessels.Where(v => v != null)) owners[vessel.id] = bridge.Owner(vessel.id);
            if (!negotiated && Time.realtimeSinceStartup >= nextHello)
            {
                var hello = new CompatibilityPacket { Kind = CompatibilityKind.Hello };
                foreach (var type in scenarioTypes) hello.State.Values.Add(new SavedValue { Name = type.Name });
                bridge.Send(hello); nextHello = Time.realtimeSinceStartup + 5;
                Status = "Generic layer: negotiating with host (requires bundled Kerbal Together server).";
            }
            for (var i = 0; i < 128 && incoming.TryDequeue(out var packet); i++)
            {
                if (packet.Kind == CompatibilityKind.Authority)
                {
                    if (packet.Epoch < epoch) continue;
                    negotiated = true; leader = packet.Owner; scenarioAuthority = packet.ScenarioAuthority; epoch = packet.Epoch;
                    lastSent.Clear(); pending.Clear(); failedScenarioLoads.Clear(); nextScan = 0;
                    Status = "Generic layer active: " + fields.Count + " field schemas; scenario mode " + (scenarioAuthority ? "single writer " + leader : "off") + ".";
                }
                else if (pending.Count < 2048 || pending.ContainsKey(packet.Key))
                {
                    if (pending.TryGetValue(packet.Key, out var previous) && previous.Owner == packet.Owner && previous.Epoch == packet.Epoch && previous.Sequence >= packet.Sequence) continue;
                    pending[packet.Key] = packet;
                }
            }
            if (!negotiated) return;
            ApplyPending();
            if (Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + 2;
            var full = Time.realtimeSinceStartup >= nextFull;
            if (full) nextFull = Time.realtimeSinceStartup + 10;
            var budget = 128;
            foreach (var vessel in FlightGlobals.Vessels.Where(v => v != null && v.loaded && owners.TryGetValue(v.id, out var owner) && owner == player))
                foreach (var part in vessel.parts)
                    for (var index = 0; index < part.Modules.Count; index++)
                    {
                        var module = part.Modules[index];
                        if (!fields.TryGetValue(module.GetType(), out var schema) || budget <= 0) continue;
                        var state = new SavedNode();
                        foreach (var field in schema) state.Values.Add(new SavedValue { Name = field.Name, Value = PersistentScalar.Format(field.GetValue(module), field.FieldType) });
                        if (SendChanged(new CompatibilityPacket { Kind = CompatibilityKind.PartFields, Vessel = vessel.id, Part = part.flightID, ModuleIndex = index, ModuleType = TypeId(module.GetType()), State = state }, full)) budget--;
                    }
            if (scenarioAuthority && leader == player)
                foreach (var scenario in ScenarioRunner.GetLoadedModules().Where(s => s != null && scenarioTypes.Contains(s.GetType()) && !failedScenarioLoads.Contains(s.GetType())))
                {
                    try
                    {
                        var node = new ConfigNode(scenario.GetType().Name);
                        scenario.Save(node);
                        SendChanged(new CompatibilityPacket { Kind = CompatibilityKind.Scenario, ModuleType = TypeId(scenario.GetType()), Epoch = epoch, State = Capture(node) }, full);
                    }
                    catch (Exception e) { Debug.LogWarning("[KerbalTogether] Scenario snapshot failed for " + scenario.GetType().FullName + ": " + e.Message); }
                }
        }

        private bool SendChanged(CompatibilityPacket packet, bool full)
        {
            var content = Convert.ToBase64String(packet.Encode());
            if (!full && lastSent.TryGetValue(packet.Key, out var previous) && previous == content) return false;
            packet.Sequence = ++sequence;
            try { bridge.Send(packet); lastSent[packet.Key] = content; return true; }
            catch (Exception e) { Debug.LogWarning("[KerbalTogether] Snapshot not sent: " + e.Message); return false; }
        }

        public static void ReliableSnapshots(object __0)
        {
            if (__0 == null) return;
            var type = __0.GetType();
            if (type.GetField("ModName")?.GetValue(__0) as string == CompatibilityPacket.Channel)
                type.GetField("Reliable")?.SetValue(__0, true);
        }

        private void ApplyPending()
        {
            foreach (var item in pending.ToArray())
            {
                var packet = item.Value;
                try
                {
                    if (packet.Kind == CompatibilityKind.PartFields)
                    {
                        if (!owners.TryGetValue(packet.Vessel, out var owner) || owner != packet.Owner || owner == player) { pending.Remove(item.Key); continue; }
                        var vessel = FlightGlobals.FindVessel(packet.Vessel);
                        if (vessel == null || !vessel.loaded) continue;
                        var part = vessel.parts.FirstOrDefault(p => p.flightID == packet.Part);
                        if (part == null || packet.ModuleIndex >= part.Modules.Count || packet.ModuleIndex < 0) { pending.Remove(item.Key); continue; }
                        var module = part.Modules[packet.ModuleIndex];
                        if (TypeId(module.GetType()) != packet.ModuleType || !fields.TryGetValue(module.GetType(), out var schema)) { pending.Remove(item.Key); continue; }
                        // Decode all values before mutating: malformed snapshots cannot partially apply.
                        var values = packet.State.Values.Select(v => new { Field = schema.FirstOrDefault(f => f.Name == v.Name), v.Value }).Where(v => v.Field != null).Select(v => new { v.Field, Value = PersistentScalar.Parse(v.Value, v.Field.FieldType) }).ToArray();
                        applying = true;
                        foreach (var value in values) value.Field.SetValue(module, value.Value);
                    }
                    else if (packet.Kind == CompatibilityKind.Scenario)
                    {
                        if (!scenarioAuthority || packet.Epoch != epoch || packet.Owner != leader) { pending.Remove(item.Key); continue; }
                        var scenario = ScenarioRunner.GetLoadedModules().FirstOrDefault(s => s != null && TypeId(s.GetType()) == packet.ModuleType && scenarioTypes.Contains(s.GetType()));
                        if (scenario == null) continue;
                        applying = true;
                        try { scenario.Load(Restore(packet.State)); }
                        catch { failedScenarioLoads.Add(scenario.GetType()); throw; }
                    }
                    pending.Remove(item.Key);
                }
                catch (Exception e) { pending.Remove(item.Key); Debug.LogWarning("[KerbalTogether] Snapshot apply failed: " + e.Message); }
                finally { applying = false; }
            }
        }

        public static SavedNode Capture(ConfigNode node)
        {
            var state = new SavedNode { Name = node.name };
            foreach (ConfigNode.Value value in node.values) state.Values.Add(new SavedValue { Name = value.name, Value = value.value });
            foreach (ConfigNode child in node.nodes) state.Children.Add(Capture(child));
            return state;
        }

        public static ConfigNode Restore(SavedNode state)
        {
            var node = new ConfigNode(state.Name);
            foreach (var value in state.Values) node.AddValue(value.Name, value.Value);
            foreach (var child in state.Children) node.AddNode(Restore(child));
            return node;
        }

        private static string TypeId(Type type) => type.Assembly.GetName().Name + ":" + type.FullName;
        private void ResetSession()
        {
            negotiated = false; scenarioAuthority = false; leader = ""; epoch = 0; sequence = 0;
            pending.Clear(); lastSent.Clear(); owners.Clear(); failedScenarioLoads.Clear();
            while (incoming.TryDequeue(out _)) { }
        }

        private void OnDestroy()
        {
            bridge?.Dispose(); harmony?.UnpatchAll("KerbalTogether.GenericCompatibility.v1");
            if (instance == this) instance = null;
        }
    }
}
