using System;

namespace CTSRevitPlugin.Utilities
{
    [AttributeUsage(
        AttributeTargets.Class,
        AllowMultiple = false,
        Inherited = false)]
    public sealed class ToolDocumentationAttribute : Attribute
    {
        public string Description { get; }

        public string Usage { get; }

        public string Author { get; }

        public string Version { get; }

        public string Notes { get; }

        public ToolDocumentationAttribute(
            string description,
            string usage,
            string author,
            string version = "1.0",
            string notes = "")
        {
            Description = description;
            Usage = usage;
            Author = author;
            Version = version;
            Notes = notes;
        }
    }
}