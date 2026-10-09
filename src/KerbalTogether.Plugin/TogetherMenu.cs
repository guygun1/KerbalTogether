using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using UnityEngine;

namespace KerbalTogether
{
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public sealed class TogetherMenu : MonoBehaviour
    {
        private Rect rect = new Rect(30, 180, 440, 340);
        private string address = "127.0.0.1";
        private string port = "8800";
        private string password = "";
        private string status = "Prepare the pack after KSP finishes loading, then host.";
        private string root;
        private bool show = true;

        private void Start() { root = KSPUtil.ApplicationRootPath; }
        private void OnGUI()
        {
            if (GUI.Button(new Rect(30, 145, 150, 28), "Kerbal Together")) show = !show;
            if (show) rect = GUILayout.Window(19408800, rect, Draw, "Kerbal Together");
        }

        private void Draw(int id)
        {
            GUILayout.Label("Local hosting • Luna Multiplayer 0.29.2");
            if (GUILayout.Button("Prepare mod pack (loaded parts + resources)")) Run(Prepare);
            if (GUILayout.Button("Host / pack diagnostics")) Run(() =>
            {
                var exe = Path.Combine(root, "KerbalTogether.Host.exe");
                if (!File.Exists(exe)) throw new FileNotFoundException("Install KerbalTogether.Host.exe and KerbalTogether.Server.exe into your KSP root folder.");
                Process.Start(new ProcessStartInfo(exe, "\"" + root.TrimEnd('\\', '/') + "\"") { UseShellExecute = true });
                status = "Hosting window opened. Set a password and click Host game.";
            });
            GUILayout.Label("Address (IPv4, IPv6 or hostname)"); address = GUILayout.TextField(address);
            GUILayout.BeginHorizontal(); GUILayout.Label("Port"); port = GUILayout.TextField(port); GUILayout.EndHorizontal();
            GUILayout.Label("Session password"); password = GUILayout.PasswordField(password, '*');
            if (GUILayout.Button("Join my hosted game")) Run(() =>
            {
                var file = Path.Combine(root, "GameData", "KerbalTogether", "PluginData", "session.xml");
                var session = XDocument.Load(file).Root;
                if (!bool.Parse(session.Element("Ready").Value)) throw new InvalidOperationException("Start hosting first.");
                using (var process = Process.GetProcessById(int.Parse(session.Element("ProcessId").Value)))
                    if (process.HasExited) throw new InvalidOperationException("Hosting process has stopped.");
                Connect("127.0.0.1", int.Parse(session.Element("Port").Value));
            });
            if (GUILayout.Button("Join address")) Run(() =>
            {
                if (!int.TryParse(port, out var parsed) || parsed < 1 || parsed > 65535) throw new ArgumentException("Port must be 1–65535.");
                Connect(address.Trim().Trim('[', ']'), parsed);
            });
            GUILayout.Label("If direct connect fails, use Luna's server browser for NAT punch-through. Compare the host's pack in the hosting window first.");
            GUILayout.Label(status);
            GUILayout.Label(GenericCompatibilityLayer.Status);
            GUI.DragWindow();
        }

        private static Type LunaType(string name)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
            return type ?? throw new InvalidOperationException("Install Luna Multiplayer 0.29.2 and Harmony through CKAN, then restart KSP.");
        }

        private void Prepare()
        {
            var type = LunaType("LmpClient.Systems.Mod.ModSystem");
            var singleton = type.GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null, null);
            type.GetMethod("GenerateModControlFile").Invoke(singleton, new object[] { true });
            // Root-level DLLs are covered by our pack manifest. Stock Luna's DLL scanner
            // does not include them, so requiring them in its handshake would reject everyone.
            status = "Pack prepared using loaded parts/resources. Host will require exact plugin hashes. Compare CFG files in the hosting window.";
        }

        private void Connect(string host, int gamePort)
        {
            LunaType("LmpClient.Network.NetworkConnection").GetMethod("ConnectToServer", new[] { typeof(string), typeof(int), typeof(string) }).Invoke(null, new object[] { host, gamePort, password });
            status = "Connecting via Luna. If rejected, check its mod-control report and compare pack manifests.";
        }

        private void Run(Action action)
        {
            try { action(); }
            catch (Exception e) { status = (e is TargetInvocationException && e.InnerException != null ? e.InnerException : e).Message; UnityEngine.Debug.LogError("[KerbalTogether] " + e); }
        }
    }
}
