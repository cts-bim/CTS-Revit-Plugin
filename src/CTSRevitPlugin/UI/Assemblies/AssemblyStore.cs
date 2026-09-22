using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using System.Xml.Serialization;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.UI.Assemblies
{
    /// <summary>
    /// Loads and saves the user's assemblies. Same location and format family as the
    /// other CTS settings: %APPDATA%\CTSRevitPlugin\ (XML).
    /// The file is read the first time it is needed, so every assembly is ready as
    /// soon as Revit starts.
    /// </summary>
    public static class AssemblyStore
    {
        private const int CurrentVersion = 2;

        private static AssemblyLibrary _library;

        public static string RootDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CTSRevitPlugin");
            }
        }

        /// <summary>
        /// Assemblies are stored per Revit version, so the order of the items a user
        /// set up in 2024 is independent from the one they use in 2025. The
        /// fabrication database itself differs between releases, which is exactly why
        /// a single shared file was the wrong place for these presets.
        ///
        /// %APPDATA%\CTSRevitPlugin\&lt;version&gt;\Assemblies.xml
        /// </summary>
        public static string VersionDirectory
        {
            get { return Path.Combine(RootDirectory, CtsRevitVersion.Current); }
        }

        public static string FilePath
        {
            get { return Path.Combine(VersionDirectory, "Assemblies.xml"); }
        }

        public static string IconDirectory
        {
            get { return Path.Combine(VersionDirectory, "AssemblyIcons"); }
        }

        public static AssemblyLibrary Library
        {
            get
            {
                if (_library == null) _library = Load();
                return _library;
            }
        }

        public static IList<AssemblyDefinition> All
        {
            get { return Library.Assemblies; }
        }

        public static AssemblyDefinition Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return Library.Assemblies.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Adds a new assembly or replaces the one with the same Id.</summary>
        public static void Upsert(AssemblyDefinition assembly)
        {
            if (assembly == null) return;
            int index = Library.Assemblies.FindIndex(a => string.Equals(a.Id, assembly.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) Library.Assemblies[index] = assembly;
            else Library.Assemblies.Add(assembly);
            Save();
        }

        public static void Remove(string id)
        {
            Library.Assemblies.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(Library.SelectedId, id, StringComparison.OrdinalIgnoreCase))
                Library.SelectedId = Library.Assemblies.Count > 0 ? Library.Assemblies[0].Id : null;
            Save();
        }

        // ============================================================
        // SHARING (export / import)
        //
        // Presets belong to a project, not to a workstation: the lead builds the
        // sequences once, exports one .xml, and everybody on the job imports it.
        // The file is the same AssemblyLibrary format that is stored in APPDATA,
        // so an exported file can simply be dropped in place if preferred.
        // ============================================================

        /// <summary>Default file name offered when exporting.</summary>
        public static string SuggestedExportFileName
        {
            get { return "CTS Assemblies " + CtsRevitVersion.Current + ".xml"; }
        }

        /// <summary>Writes the current library to <paramref name="path"/>. Throws on failure.</summary>
        public static void ExportTo(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("No export path was given.", "path");

            Library.Version = CurrentVersion;

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            XmlSerializer serializer = new XmlSerializer(typeof(AssemblyLibrary));
            using (FileStream stream = File.Create(path))
                serializer.Serialize(stream, Library);
        }

        /// <summary>What an import did, for the pane status line.</summary>
        public sealed class ImportSummary
        {
            public int Added { get; set; }
            public int Replaced { get; set; }

            public int Total
            {
                get { return Added + Replaced; }
            }

            public override string ToString()
            {
                if (Total == 0) return "The file had no assemblies to import.";

                string text = "Imported " + Total + " assembly(ies): " + Added + " added";
                if (Replaced > 0) text += ", " + Replaced + " updated";
                return text + ".";
            }
        }

        /// <summary>
        /// Reads an exported file and brings its assemblies in.
        ///
        /// <paramref name="replaceAll"/> false merges: an incoming assembly updates the
        /// one with the same Id, or failing that the one with the same Name, and is
        /// otherwise added. True discards the local library first.
        ///
        /// Throws when the file cannot be read, so the caller can show the reason.
        /// </summary>
        public static ImportSummary Import(string path, bool replaceAll)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("The assemblies file was not found.", path);

            AssemblyLibrary incoming;
            XmlSerializer serializer = new XmlSerializer(typeof(AssemblyLibrary));
            using (FileStream stream = File.OpenRead(path))
                incoming = serializer.Deserialize(stream) as AssemblyLibrary;

            if (incoming == null)
                throw new InvalidDataException("The file is not a CTS assemblies file.");

            Normalize(incoming);
            if (incoming.Version < CurrentVersion) MigrateImported(incoming);

            ImportSummary summary = new ImportSummary();

            if (replaceAll)
            {
                Library.Assemblies.Clear();
                Library.SelectedId = null;
            }

            foreach (AssemblyDefinition assembly in incoming.Assemblies)
            {
                if (assembly == null) continue;

                int index = Library.Assemblies.FindIndex(
                    a => string.Equals(a.Id, assembly.Id, StringComparison.OrdinalIgnoreCase));

                if (index < 0 && !string.IsNullOrWhiteSpace(assembly.Name))
                {
                    index = Library.Assemblies.FindIndex(
                        a => string.Equals(a.Name, assembly.Name, StringComparison.OrdinalIgnoreCase));
                }

                if (index >= 0)
                {
                    Library.Assemblies[index] = assembly;
                    summary.Replaced++;
                }
                else
                {
                    Library.Assemblies.Add(assembly);
                    summary.Added++;
                }
            }

            if (string.IsNullOrWhiteSpace(Library.SelectedId) && Library.Assemblies.Count > 0)
                Library.SelectedId = Library.Assemblies[0].Id;

            Save();
            return summary;
        }

        /// <summary>
        /// Same step-1 drop as MigrateToCurrent, minus the .bak of the local file:
        /// an imported library is not the file on disk.
        /// </summary>
        private static void MigrateImported(AssemblyLibrary library)
        {
            foreach (AssemblyDefinition assembly in library.Assemblies)
            {
                if (assembly.Steps.Count > 0) assembly.Steps.RemoveAt(0);
                foreach (AssemblyStep step in assembly.Steps)
                {
                    if (step.SizeSource == StepSizeSource.Assembly) step.SizeSource = StepSizeSource.Previous;
                }
            }

            library.Version = CurrentVersion;
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(VersionDirectory);

                // Write to a temp file first so a crash never leaves a half-written XML.
                Library.Version = CurrentVersion;
                string temp = FilePath + ".tmp";
                XmlSerializer serializer = new XmlSerializer(typeof(AssemblyLibrary));
                using (FileStream stream = File.Create(temp))
                    serializer.Serialize(stream, Library);

                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception ex)
            {
                CtsMessageWindow.Show(
                    "CTS Revit Plugin - Settings Error",
                    "Could not save the assemblies.\n\nPath:\n" + FilePath + "\n\nError:\n" + ex.Message);
            }
        }

        private static AssemblyLibrary Load()
        {
            AssemblyLibrary library = null;

            // Nothing is inherited from another Revit version or from the old shared
            // file: each version owns its own library and starts from the shipped
            // defaults. Sharing between people and versions is done on purpose,
            // through Export / Import below.
            if (File.Exists(FilePath))
            {
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(AssemblyLibrary));
                    using (FileStream stream = File.OpenRead(FilePath))
                        library = serializer.Deserialize(stream) as AssemblyLibrary;
                }
                catch (Exception ex)
                {
                    string backup = FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    try { File.Copy(FilePath, backup, true); } catch { }

                    CtsMessageWindow.Show(
                        "CTS Revit Plugin - Settings Error",
                        "Could not read the assemblies file, so the default assemblies were loaded.\n\n" +
                        "A copy of the unreadable file was kept at:\n" + backup + "\n\nError:\n" + ex.Message);
                    library = null;
                }
            }

            bool firstRun = library == null;
            if (library == null) library = new AssemblyLibrary();
            Normalize(library);

            if (firstRun)
            {
                library.Version = CurrentVersion;
                library.Assemblies.AddRange(CreateDefaults());
                library.SelectedId = library.Assemblies[0].Id;
                _library = library;
                Save();
            }
            else if (library.Version < CurrentVersion)
            {
                MigrateToCurrent(library);
                _library = library;
                Save();
            }

            return library;
        }

        /// <summary>
        /// Version 1 assemblies started with the olet as step 1. Now the olet is created by the
        /// user and selected as input, so that first step is dropped. Everything else is kept.
        /// A copy of the old file is kept next to it.
        /// </summary>
        private static void MigrateToCurrent(AssemblyLibrary library)
        {
            try { File.Copy(FilePath, FilePath + ".v1.bak", true); } catch { }

            foreach (AssemblyDefinition assembly in library.Assemblies)
            {
                if (assembly.Steps.Count > 0) assembly.Steps.RemoveAt(0);
                foreach (AssemblyStep step in assembly.Steps)
                {
                    if (step.SizeSource == StepSizeSource.Assembly) step.SizeSource = StepSizeSource.Previous;
                }
            }

            library.Version = CurrentVersion;
        }

        private static void Normalize(AssemblyLibrary library)
        {
            if (library.Assemblies == null) library.Assemblies = new List<AssemblyDefinition>();
            library.Assemblies.RemoveAll(a => a == null);

            foreach (AssemblyDefinition assembly in library.Assemblies)
            {
                if (string.IsNullOrWhiteSpace(assembly.Id)) assembly.Id = Guid.NewGuid().ToString("N");
                if (assembly.Steps == null) assembly.Steps = new List<AssemblyStep>();
                assembly.Steps.RemoveAll(s => s == null);
                foreach (AssemblyStep step in assembly.Steps)
                    if (step.Item == null) step.Item = new CatalogItemRef();
            }
        }

        /// <summary>
        /// High Point and Low Point come pre-shaped (item order) but with no database items,
        /// because every project can name its buttons differently. The olet is not a step: the
        /// user creates it on the pipe and selects it. Open EDIT once and pick each item.
        /// </summary>
        private static IEnumerable<AssemblyDefinition> CreateDefaults()
        {
            yield return CreateDefaultPoint("High Point", "HP");
            yield return CreateDefaultPoint("Low Point", "LP");
        }

        private static AssemblyDefinition CreateDefaultPoint(string name, string shortName)
        {
            AssemblyDefinition assembly = new AssemblyDefinition
            {
                Name = name,
                ShortName = shortName
            };

            assembly.Steps.Add(new AssemblyStep { Label = "Nipple (x3)" });
            assembly.Steps.Add(new AssemblyStep { Label = "Valve" });
            assembly.Steps.Add(new AssemblyStep { Label = "Adapter" });
            assembly.Steps.Add(new AssemblyStep { Label = "Cap" });
            return assembly;
        }
    }

    /// <summary>
    /// Thumbnails of database buttons. They are cached on disk so the tiles keep their
    /// pictures even when no project is open (the Revit API cannot be read then).
    /// </summary>
    public static class AssemblyIconCache
    {
        private static readonly Dictionary<string, BitmapSource> _memory =
            new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Saves the PNG bytes and returns the file name (null when there is no image).</summary>
        public static string SaveIcon(string service, string palette, string button, byte[] png)
        {
            if (png == null || png.Length == 0) return null;
            try
            {
                Directory.CreateDirectory(AssemblyStore.IconDirectory);
                string fileName = Hash(service + "|" + palette + "|" + button) + ".png";
                string path = Path.Combine(AssemblyStore.IconDirectory, fileName);
                File.WriteAllBytes(path, png);
                _memory.Remove(fileName);
                return fileName;
            }
            catch
            {
                return null;
            }
        }

        public static BitmapSource Load(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            BitmapSource cached;
            if (_memory.TryGetValue(fileName, out cached)) return cached;

            try
            {
                string path = Path.Combine(AssemblyStore.IconDirectory, fileName);
                if (!File.Exists(path)) return null;
                BitmapSource image = FromPng(File.ReadAllBytes(path));
                _memory[fileName] = image;
                return image;
            }
            catch
            {
                return null;
            }
        }

        public static BitmapSource FromPng(byte[] png)
        {
            if (png == null || png.Length == 0) return null;
            BitmapImage bitmap = new BitmapImage();
            using (MemoryStream stream = new MemoryStream(png))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();
            return bitmap;
        }

        public static byte[] ToPng(System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null) return null;
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static string Hash(string text)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
