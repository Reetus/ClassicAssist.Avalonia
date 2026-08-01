using System;

namespace ClassicAssist.Launcher.Models
{
    public class ClassicOptionAttribute : Attribute
    {
        public ClassicOptionAttribute( string argument )
        {
            Argument = argument;
        }

        public string Argument { get; set; }
        public string CanIncludeProperty { get; set; }
        public object DefaultValue { get; set; }
        public bool IncludeIfFalse { get; set; } = true;
    }
}
