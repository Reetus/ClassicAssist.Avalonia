using System.IO;

namespace ClassicAssist.Launcher.Models
{
    public class PluginEntry
    {
        public string FullPath { get; set; }
        public bool IsValid => File.Exists( FullPath );
        public string Name { get; set; }
    }
}
