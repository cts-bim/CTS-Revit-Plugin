using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.UI.ElementFilter
{
    /// <summary>
    /// Loads and saves the element filters.
    ///
    /// Same shape as AssemblyStore: one file per Revit version under
    /// %APPDATA%\CTSRevitPlugin\&lt;version&gt;\, nothing inherited between versions,
    /// and sharing done on purpose through Export / Import so a project lead can
    /// hand the same rule set to the whole team.
    /// </summary>
    public static class ElementFilterStore
    {
        private const int CurrentVersion = 1;

        private static FilterLibrary _library;

        public static string VersionDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CTSRevitPlugin",
                    CtsRevitVersion.Current);
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(VersionDirectory, "ElementFilters.xml"); }
        }

        public static string SuggestedExportFileName
        {
            get { return "CTS Element Filters " + CtsRevitVersion.Current + ".xml"; }
        }

        public static FilterLibrary Library
        {
            get
            {
                if (_library == null) _library = Load();
                return _library;
            }
        }

        public static List<FilterDefinition> All
        {
            get { return Library.Filters; }
        }

        public static FilterDefinition Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return Library.Filters.FirstOrDefault(
                f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static void Upsert(FilterDefinition filter)
        {
            if (filter == null) return;

            int index = Library.Filters.FindIndex(
                f => string.Equals(f.Id, filter.Id, StringComparison.OrdinalIgnoreCase));

            if (index >= 0) Library.Filters[index] = filter;
            else Library.Filters.Add(filter);

            Save();
        }

        public static void Remove(string id)
        {
            Library.Filters.RemoveAll(
                f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

            if (string.Equals(Library.SelectedId, id, StringComparison.OrdinalIgnoreCase))
                Library.SelectedId = Library.Filters.Count > 0 ? Library.Filters[0].Id : null;

            Save();
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(VersionDirectory);

                Library.Version = CurrentVersion;

                // Temp file first, so a crash never leaves a half-written XML.
                string temp = FilePath + ".tmp";
                XmlSerializer serializer = new XmlSerializer(typeof(FilterLibrary));
                using (FileStream stream = File.Create(temp))
                    serializer.Serialize(stream, Library);

                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception ex)
            {
                CtsMessageWindow.Show(
                    "CTS Element Filter - Settings Error",
                    "Could not save the filters.\n\nPath:\n" + FilePath + "\n\nError:\n" + ex.Message);
            }
        }

        private static FilterLibrary Load()
        {
            FilterLibrary library = null;

            if (File.Exists(FilePath))
            {
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(FilterLibrary));
                    using (FileStream stream = File.OpenRead(FilePath))
                        library = serializer.Deserialize(stream) as FilterLibrary;
                }
                catch (Exception ex)
                {
                    string backup = FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    try { File.Copy(FilePath, backup, true); } catch { }

                    CtsMessageWindow.Show(
                        "CTS Element Filter - Settings Error",
                        "Could not read the filters file, so the list was started empty.\n\n" +
                        "A copy of the unreadable file was kept at:\n" + backup +
                        "\n\nError:\n" + ex.Message);

                    library = null;
                }
            }

            if (library == null) library = new FilterLibrary();
            Normalize(library);
            return library;
        }

        private static void Normalize(FilterLibrary library)
        {
            if (library.Filters == null) library.Filters = new List<FilterDefinition>();

            foreach (FilterDefinition filter in library.Filters)
            {
                if (filter.Conditions == null) filter.Conditions = new List<FilterCondition>();
                if (filter.CategoryIds == null) filter.CategoryIds = new List<long>();
                if (filter.CategoryNames == null) filter.CategoryNames = new List<string>();
                if (string.IsNullOrWhiteSpace(filter.Id)) filter.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(filter.Name)) filter.Name = "Filter";
                if (filter.Transparency < 0) filter.Transparency = 0;
                if (filter.Transparency > 100) filter.Transparency = 100;
            }
        }

        // ============================================================
        // SHARING
        // ============================================================

        public static void ExportTo(string path, IList<FilterDefinition> filters)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("No export path was given.", "path");

            FilterLibrary payload = new FilterLibrary { Version = CurrentVersion };
            payload.Filters.AddRange(filters ?? All);

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            XmlSerializer serializer = new XmlSerializer(typeof(FilterLibrary));
            using (FileStream stream = File.Create(path))
                serializer.Serialize(stream, payload);
        }

        public sealed class ImportSummary
        {
            public int Added { get; set; }
            public int Replaced { get; set; }

            public int Total { get { return Added + Replaced; } }

            public override string ToString()
            {
                if (Total == 0) return "The file had no filters to import.";

                string text = "Imported " + Total + " filter(s): " + Added + " added";
                if (Replaced > 0) text += ", " + Replaced + " updated";
                return text + ".";
            }
        }

        /// <summary>
        /// Merges by Id, then by Name, so re-importing a corrected project file
        /// updates in place instead of duplicating rows.
        /// </summary>
        public static ImportSummary Import(string path, bool replaceAll)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("The filters file was not found.", path);

            FilterLibrary incoming;
            XmlSerializer serializer = new XmlSerializer(typeof(FilterLibrary));
            using (FileStream stream = File.OpenRead(path))
                incoming = serializer.Deserialize(stream) as FilterLibrary;

            if (incoming == null)
                throw new InvalidDataException("The file is not a CTS element filters file.");

            Normalize(incoming);

            ImportSummary summary = new ImportSummary();

            if (replaceAll)
            {
                Library.Filters.Clear();
                Library.SelectedId = null;
            }

            foreach (FilterDefinition filter in incoming.Filters)
            {
                if (filter == null) continue;

                int index = Library.Filters.FindIndex(
                    f => string.Equals(f.Id, filter.Id, StringComparison.OrdinalIgnoreCase));

                if (index < 0 && !string.IsNullOrWhiteSpace(filter.Name))
                {
                    index = Library.Filters.FindIndex(
                        f => string.Equals(f.Name, filter.Name, StringComparison.OrdinalIgnoreCase));
                }

                if (index >= 0) { Library.Filters[index] = filter; summary.Replaced++; }
                else { Library.Filters.Add(filter); summary.Added++; }
            }

            if (string.IsNullOrWhiteSpace(Library.SelectedId) && Library.Filters.Count > 0)
                Library.SelectedId = Library.Filters[0].Id;

            Save();
            return summary;
        }
    }
}
