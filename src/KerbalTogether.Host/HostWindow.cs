using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Xml.Linq;
using KerbalTogether.Core;

namespace KerbalTogether.Host;

public sealed class HostWindow : Form
{
    private readonly TextBox ksp = new() { Width = 620 };
    private readonly TextBox name = new() { Text = "Kerbal Together", Width = 200, MaxLength = 30 };
    private readonly TextBox password = new() { Width = 180, UseSystemPasswordChar = true, MaxLength = 30 };
    private readonly NumericUpDown port = new() { Minimum = 1024, Maximum = 65535, Value = 8800 };
    private readonly CheckBox external = new() { Text = "Use public Luna rendezvous if router mapping fails", Checked = true, AutoSize = true };
    private readonly CheckBox genericScenarios = new() { Text = "Experimental generic scenario single-writer sync", Checked = false, AutoSize = true };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false };
    private readonly Button start = new() { Text = "Host game", AutoSize = true };
    private readonly Button stop = new() { Text = "Save & stop", Enabled = false, AutoSize = true };
    private readonly RouterMapping router = new();
    private Process? server;
    private CancellationTokenSource? hosting;
    private bool closing;
    private string? activeRoot;
    private FileStream? sessionLock;
    private Task? startup;

    public HostWindow(string root)
    {
        Text = "Kerbal Together — local hosting & pack diagnostics";
        Width = 950; Height = 650;
        ksp.Text = string.IsNullOrEmpty(root) ? @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program" : root;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(10) };
        top.Controls.Add(new Label { Text = "KSP folder", AutoSize = true }); top.Controls.Add(ksp);
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog(); if (dialog.ShowDialog() == DialogResult.OK) ksp.Text = dialog.SelectedPath; };
        top.Controls.Add(browse); top.SetFlowBreak(browse, true);
        top.Controls.Add(new Label { Text = "Session", AutoSize = true }); top.Controls.Add(name);
        top.Controls.Add(new Label { Text = "Password", AutoSize = true }); top.Controls.Add(password);
        top.Controls.Add(new Label { Text = "UDP port", AutoSize = true }); top.Controls.Add(port); top.SetFlowBreak(port, true);
        top.Controls.Add(external); top.SetFlowBreak(external, true);
        top.Controls.Add(genericScenarios); top.SetFlowBreak(genericScenarios, true);
        top.Controls.Add(start); top.Controls.Add(stop);
        var export = new Button { Text = "Export pack & report", AutoSize = true };
        var compare = new Button { Text = "Compare friend's pack", AutoSize = true };
        top.Controls.Add(export); top.Controls.Add(compare);
        Controls.Add(log); Controls.Add(top);
        start.Click += async (_, _) => { startup = Start(); await startup; };
        stop.Click += async (_, _) => await Stop();
        export.Click += async (_, _) => await Guard(async () =>
        {
            using var dialog = new SaveFileDialog { Filter = "Pack manifest|*.xml", FileName = "kerbal-together-pack.xml" };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            var rootPath = ksp.Text;
            var pack = await Task.Run(() => PackAudit.Scan(rootPath));
            pack.Save(dialog.FileName);
            var notes = PackAudit.Notes(pack.Mods.Select(m => m.Id));
            File.WriteAllLines(Path.ChangeExtension(dialog.FileName, ".txt"), notes);
            foreach (var note in notes) Log(note);
            Log("Share " + dialog.FileName + " with your friends. No mod binaries are included.");
        });
        compare.Click += async (_, _) => await Guard(async () =>
        {
            using var dialog = new OpenFileDialog { Filter = "Pack manifest|*.xml" };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            var remote = PackManifest.Load(dialog.FileName);
            var rootPath = ksp.Text;
            var local = await Task.Run(() => PackAudit.Scan(rootPath));
            var differences = remote.Compare(local);
            Log(differences.Count == 0 ? "Pack identity matches. Gameplay synchronization remains unverified." : $"{differences.Count} pack differences:");
            foreach (var difference in differences) Log(difference);
            File.WriteAllLines(Path.Combine(rootPath, "kerbal-together-differences.txt"), differences);
        });
        FormClosing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            hosting?.Cancel();
            if (startup != null) await startup;
            await Stop();
            closing = true; Close();
        };
        Log("Install Luna Multiplayer 0.29.2 + Harmony through CKAN, then open KSP and use Kerbal Together → Prepare mod pack.");
        Log("A local server is managed here. Internet fallback uses existing Luna rendezvous; gameplay is peer-to-host.");
    }

    private async Task Guard(Func<Task> action)
    {
        try { await action(); } catch (Exception e) { Log("ERROR: " + e.Message); }
    }

    private async Task Start()
    {
        if (hosting != null) return;
        start.Enabled = false; ksp.Enabled = false;
        name.Enabled = password.Enabled = port.Enabled = external.Enabled = genericScenarios.Enabled = false;
        hosting = new CancellationTokenSource();
        try
        {
            activeRoot = Path.GetFullPath(ksp.Text);
            var modFile = Path.Combine(activeRoot, "LMPModControl.xml");
            if (!File.Exists(modFile)) throw new FileNotFoundException("In the KSP main menu, click Kerbal Together → Prepare mod pack first.");
            if (password.Text.Length == 0) throw new InvalidOperationException("Set a session password, particularly for public rendezvous fallback.");
            var serverDir = Path.Combine(AppContext.BaseDirectory, "KerbalTogetherData");
            var exe = Path.Combine(AppContext.BaseDirectory, "KerbalTogether.Server.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("Bundled Luna Server.exe is missing. Run scripts/Build.ps1 to assemble the distribution.");
            Directory.CreateDirectory(serverDir);
            sessionLock = new FileStream(Path.Combine(serverDir, "hosting.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var rootPath = activeRoot;
            var pack = await Task.Run(() => PackAudit.Scan(rootPath));
            hosting.Token.ThrowIfCancellationRequested();
            var notes = PackAudit.Notes(pack.Mods.Select(m => m.Id));
            foreach (var note in notes) Log(note);
            pack.Save(Path.Combine(activeRoot, "kerbal-together-pack.xml"));
            if (MessageBox.Show(this, "This pack has unverified gameplay synchronization. Matching mods does not ensure background simulation/colony state is correct.\n\nSee the report above. Continue hosting?", "Mod compatibility", MessageBoxButtons.YesNo) != DialogResult.Yes)
                throw new OperationCanceledException("Hosting cancelled after pack assessment.");
            var gamePort = (int)port.Value;
            // Fail before opening the router if a local process already owns this port.
            using (var probe = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp))
            { probe.DualMode = true; probe.ExclusiveAddressUse = true; probe.Bind(new IPEndPoint(IPAddress.IPv6Any, gamePort)); }
            var endpoint = await router.TryOpen(gamePort, Log, hosting.Token);
            var useRendezvous = external.Checked && (endpoint == null || !RouterMapping.IsPublicIPv4(IPAddress.Parse(endpoint.Split(':')[0])));
            ServerConfig.Prepare(serverDir, gamePort, password.Text, name.Text, useRendezvous, modFile, genericScenarios.Checked);
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            server = new Process { StartInfo = new ProcessStartInfo(exe) { WorkingDirectory = serverDir, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true }, EnableRaisingEvents = true };
            server.StartInfo.Environment["KERBAL_TOGETHER_DATA"] = serverDir;
            server.OutputDataReceived += (_, e) => { if (e.Data != null) { Log(e.Data); if (e.Data.Contains("All systems up and running")) ready.TrySetResult(true); } };
            server.ErrorDataReceived += (_, e) => { if (e.Data != null) Log(e.Data); };
            server.Exited += (_, _) =>
            {
                ready.TrySetException(new IOException("Luna exited before hosting became ready. See the server log above."));
                Log("Local server exited.");
                if (!IsDisposed) BeginInvoke(new Action(async () => { if (stop.Enabled) await Stop(); }));
            };
            server.Start(); server.BeginOutputReadLine(); server.BeginErrorReadLine();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), hosting.Token);
            WriteSession(true, gamePort);
            Log("HOST READY locally. In KSP click Join my hosted game. Reachability from the internet is not yet verified.");
            if (endpoint != null) Log("Direct address to share: " + endpoint);
            foreach (var address in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address))
            {
                if (IPAddress.IsLoopback(address)) continue;
                if (address.AddressFamily == AddressFamily.InterNetwork) Log("LAN address: " + address + ":" + gamePort);
                if (address.AddressFamily == AddressFamily.InterNetworkV6 && (address.GetAddressBytes()[0] & 0xe0) == 0x20)
                    Log("IPv6 candidate: [" + address + "]:" + gamePort + " (both players need IPv6 and an inbound firewall rule)");
            }
            Log(useRendezvous ? "NAT fallback active: friends open Luna's server browser, find '" + name.Text + "', and enter your password. Public rendezvous availability and NAT type affect success." : "Public rendezvous disabled. Friends use Direct Connect with your IPv4/IPv6/LAN address.");
            Log("If joining times out: allow the bundled Server.exe through the firewall (UDP), check CGNAT/double NAT, and try the IPv6 candidate. Symmetric NAT may need manual forwarding or a VPN.");
            stop.Enabled = true;
        }
        catch (Exception e) { Log("ERROR: " + e.Message); await Stop(); }
    }

    private void WriteSession(bool ready, int gamePort = 0)
    {
        if (activeRoot == null) return;
        var dir = Path.Combine(activeRoot, "GameData", "KerbalTogether", "PluginData");
        Directory.CreateDirectory(dir);
        // Do not store the password; the user supplies it in KSP for the loopback connection too.
        new XDocument(new XElement("Session", new XElement("Ready", ready), new XElement("Port", gamePort), new XElement("ProcessId", ready ? server!.Id : 0))).Save(Path.Combine(dir, "session.xml"));
    }

    private async Task Stop()
    {
        stop.Enabled = false;
        hosting?.Cancel();
        try
        {
            if (server != null && !server.HasExited)
            {
                Log("Saving and stopping local server…");
                await server.StandardInput.WriteLineAsync("/shutdown");
                await server.StandardInput.FlushAsync();
                try { await server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (TimeoutException) { Log("Graceful shutdown timed out; stopping the process. Check the server's Universe/Backups before resuming."); server.Kill(true); await server.WaitForExitAsync(); }
            }
        }
        catch (Exception e) { Log("Shutdown: " + e.Message); }
        finally
        {
            server?.Dispose(); server = null;
            await router.DisposeAsync();
            try { if (sessionLock != null) WriteSession(false); } catch (Exception e) { Log("Could not clear session status: " + e.Message); }
            sessionLock?.Dispose(); sessionLock = null;
            hosting?.Dispose(); hosting = null;
            start.Enabled = true; ksp.Enabled = true;
            name.Enabled = password.Enabled = port.Enabled = external.Enabled = genericScenarios.Enabled = true;
        }
    }

    private void Log(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => Log(message))); return; }
        log.AppendText(message + Environment.NewLine);
    }
}
