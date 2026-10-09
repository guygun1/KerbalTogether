using KerbalTogether.Core;
using LmpCommon.Message.Data;
using LmpCommon.Message.Server;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Plugin;
using Server.Server;
using Server.System;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Server.Compatibility
{
    public static class CompatibilityRelay
    {
        private static readonly object Gate = new object();
        private static readonly CompatibilityAuthority Authority = new CompatibilityAuthority();
        private static readonly HashSet<string> ScenarioNames = new HashSet<string>();
        private static readonly Dictionary<string, byte[]> ScenarioSnapshots = new Dictionary<string, byte[]>();
        private static bool Scenarios;
        private static string SnapshotDirectory => Path.Combine(ServerContext.UniverseDirectory, "KerbalTogetherScenarios");

        public static void Initialize()
        {
            // Experimental mode is enabled by the managed host, without mod-specific rules.
            Scenarios = File.Exists(Path.Combine(ServerContext.ConfigDirectory, "GenericScenarioAuthority.enabled"));
            if (Scenarios)
            {
                Directory.CreateDirectory(SnapshotDirectory);
                foreach (var file in Directory.EnumerateFiles(SnapshotDirectory, "*.state").Take(512))
                    try
                    {
                        if (new FileInfo(file).Length > CompatibilityPacket.MaxBytes) continue;
                        var bytes = File.ReadAllBytes(file);
                        var packet = CompatibilityPacket.Decode(bytes, bytes.Length);
                        if (packet.Kind != CompatibilityKind.Scenario) continue;
                        ScenarioSnapshots[packet.ModuleType] = bytes;
                        ScenarioNames.Add(packet.State.Name);
                    }
                    catch (Exception e) { LunaLog.Warning("Could not restore generic scenario snapshot: " + e.Message); }
            }
            LmpModInterface.RegisterModHandler(CompatibilityPacket.Channel, Receive);
            LunaLog.Info("Generic compatibility relay enabled; scenario authority: " + Scenarios);
        }

        private static void Send(ClientStructure client, CompatibilityPacket packet)
        {
            var bytes = packet.Encode();
            var data = ServerContext.ServerMessageFactory.CreateNewMessageData<ModMsgData>();
            data.ModName = CompatibilityPacket.Channel; data.Data = bytes; data.NumBytes = bytes.Length; data.Reliable = true; data.Relay = false;
            MessageQueuer.SendToClient<ModSrvMsg>(client, data);
        }

        private static void Announce(ClientStructure client)
        {
            Send(client, new CompatibilityPacket { Kind = CompatibilityKind.Authority, Owner = Authority.Leader, Epoch = Authority.Epoch, ScenarioAuthority = Scenarios });
        }

        private static void Receive(ClientStructure client, byte[] bytes, int count)
        {
            if (!client.Authenticated) return;
            try
            {
                var packet = CompatibilityPacket.Decode(bytes, count);
                lock (Gate)
                {
                    if (packet.Kind == CompatibilityKind.Hello)
                    {
                        var changed = Authority.Join(client.PlayerName);
                        // Names are reported by the loaded client catalog; this also prevents
                        // Luna's legacy all-writers scenario stream overwriting the same state.
                        if (Scenarios)
                            foreach (var item in packet.State.Values.Take(512)) ScenarioNames.Add(item.Name);
                        if (changed) foreach (var participant in ClientRetriever.GetAuthenticatedClients().Where(c => Authority.IsParticipant(c.PlayerName))) Announce(participant);
                        else Announce(client);
                        foreach (var snapshot in ScenarioSnapshots.Values)
                        {
                            var saved = CompatibilityPacket.Decode(snapshot, snapshot.Length);
                            saved.Epoch = Authority.Epoch; saved.Owner = Authority.Leader;
                            Send(client, saved);
                        }
                        return;
                    }
                    var owner = LockSystem.LockQuery.GetUpdateLockOwner(packet.Vessel) ?? LockSystem.LockQuery.GetUnloadedUpdateLockOwner(packet.Vessel);
                    if (!Authority.Accept(packet, client.PlayerName, owner, Scenarios)) return;
                    if (packet.Kind == CompatibilityKind.Scenario)
                    {
                        if (!ScenarioNames.Contains(packet.State.Name)) return;
                        if (ScenarioSnapshots.Count >= 512 && !ScenarioSnapshots.ContainsKey(packet.ModuleType)) return;
                        ScenarioSnapshots[packet.ModuleType] = packet.Encode();
                        using (var sha = SHA256.Create())
                        {
                            var filename = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(packet.ModuleType))).Replace("-", "") + ".state";
                            var path = Path.Combine(SnapshotDirectory, filename);
                            File.WriteAllBytes(path + ".tmp", ScenarioSnapshots[packet.ModuleType]);
                            File.Move(path + ".tmp", path, true);
                        }
                    }
                    foreach (var target in ClientRetriever.GetAuthenticatedClients().Where(c => c != client)) Send(target, packet);
                }
            }
            catch (Exception e) { LunaLog.Warning("Compatibility packet rejected: " + e.Message); }
        }

        public static bool OwnsScenarioStream(string module)
        {
            lock (Gate) return Scenarios && ScenarioNames.Contains(module);
        }

        public static void Disconnect(ClientStructure client)
        {
            lock (Gate)
            {
                if (!Authority.Leave(client.PlayerName)) return;
                foreach (var target in ClientRetriever.GetAuthenticatedClients().Where(c => Authority.IsParticipant(c.PlayerName)))
                {
                    Announce(target);
                    foreach (var snapshot in ScenarioSnapshots.Values)
                    {
                        var saved = CompatibilityPacket.Decode(snapshot, snapshot.Length);
                        saved.Epoch = Authority.Epoch; saved.Owner = Authority.Leader;
                        Send(target, saved);
                    }
                }
            }
        }
    }
}
