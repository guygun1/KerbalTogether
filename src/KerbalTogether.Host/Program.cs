using System.Text;
using KerbalTogether.Core;

namespace KerbalTogether.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Command-line audit is useful without launching KSP or opening a window.
        if (args.Length >= 3 && args[0] == "--audit")
        {
            var pack = PackAudit.Scan(args[1]);
            pack.Save(args[2] + ".xml");
            File.WriteAllLines(args[2] + ".txt", new[] { $"Installed CKAN modules: {pack.Mods.Count}", $"Gameplay files checked: {pack.Files.Count}", "" }
                .Concat(pack.Mods.Select(m => m.Id + " " + m.Version)).Concat(new[] { "", "Synchronization assessment:" }).Concat(PackAudit.Notes(pack.Mods.Select(m => m.Id))), Encoding.UTF8);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new HostWindow(args.Length > 0 ? args[0] : ""));
    }
}
