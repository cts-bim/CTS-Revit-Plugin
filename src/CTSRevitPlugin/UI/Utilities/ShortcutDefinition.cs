using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace CTSRevitPlugin.UI.Utilities
{
    public sealed class ShortcutDefinition
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Group { get; set; }
        public ImageSource Icon { get; set; }
        public List<string> CommandCandidates { get; set; } = new List<string>();
        public string NativeCommandName { get; set; }
        public string CtsToolId { get; set; }
        public string Description { get; set; }

        public bool IsNative { get { return !string.IsNullOrWhiteSpace(NativeCommandName); } }
        public bool IsCts { get { return !string.IsNullOrWhiteSpace(CtsToolId); } }
    }
}
