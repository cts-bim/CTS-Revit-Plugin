using Autodesk.Revit.UI;
using CTSRevitPlugin.UI.Utilities;
using CTSRevitPlugin.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CTSRevitPlugin.UI.Assemblies
{
    public partial class AssembliesPane : Page, IDockablePaneProvider
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("5B8E2D4A-9C31-4F7E-A6D2-1E8C3B07F5A9"));

        public static AssembliesPane Instance { get; private set; }

        private readonly AssembliesController _controller;
        private AssemblyBuilderWindow _builder;
        private string _selectedId;

        public AssembliesPane()
        {
            Instance = this;
            InitializeComponent();
            RevitThemeService.Register(this);
            LoadBranding();

            _controller = new AssembliesController();

            RebuildTiles();

            // Tiles are built in code with the current theme colors: rebuild them whenever the pane
            // is shown again so a Light/Dark switch in Revit is picked up.
            IsVisibleChanged += delegate
            {
                if (IsVisible) RebuildTiles();
            };
        }

        // ============================================================
        // BRANDING / DOCKABLE PANE
        // ============================================================

        private void LoadBranding()
        {
            string root = Path.Combine(Path.GetDirectoryName(typeof(AssembliesPane).Assembly.Location), "Resources", "Icons");
            SetImage(HeaderLogo, Path.Combine(root, "CTS Icon.png"));
            SetImage(FooterCtsLogo, Path.Combine(root, "CTS Icon.png"));
            SetImage(FooterMotto, Path.Combine(root, "No bull Just build.png"));
        }

        private static void SetImage(Image image, string path)
        {
            if (image == null || !File.Exists(path)) return;
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                image.Source = bitmap;
            }
            catch { }
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
            data.VisibleByDefault = false;
        }

        public void DisposeController()
        {
            try { if (_controller != null) _controller.Dispose(); } catch { }
        }

        // ============================================================
        // TILES
        // ============================================================

        /// <summary>
        /// Rebuilds the tile strip.
        ///
        /// Every caller is a WPF handler: IsVisibleChanged, which Revit fires
        /// while it destroys and recreates the pane's host window during an
        /// undock, and the tile clicks. An exception escaping a WPF handler
        /// inside Revit is not reported to anyone - it terminates the process
        /// (journal: ExceptionCode=0xe0434352). So the whole rebuild is fenced
        /// off and a failure becomes a status line instead of a crash.
        /// </summary>
        private void RebuildTiles()
        {
            try { RebuildTilesCore(); }
            catch (Exception ex)
            {
                try { SetStatus("Could not refresh assemblies: " + ex.Message); }
                catch { }
            }
        }

        private void RebuildTilesCore()
        {
            TilesPanel.Children.Clear();
            IList<AssemblyDefinition> all = AssemblyStore.All;

            EmptyText.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (AssemblyStore.Find(_selectedId) == null)
            {
                string remembered = AssemblyStore.Library.SelectedId;
                if (AssemblyStore.Find(remembered) != null) _selectedId = remembered;
                else _selectedId = all.Count > 0 ? all[0].Id : null;
            }

            foreach (AssemblyDefinition assembly in all)
                TilesPanel.Children.Add(CreateTile(assembly));

            ShowDetails();
        }

        private Button CreateTile(AssemblyDefinition assembly)
        {
            bool selected = string.Equals(assembly.Id, _selectedId, StringComparison.OrdinalIgnoreCase);
            bool incomplete = assembly.UnassignedCount > 0;

            Button button = new Button
            {
                Tag = assembly.Id,
                Style = RevitThemeService.StyleOrNull(this, "TileButtonStyle"),
                ToolTip = assembly.Name + (incomplete ? "\nSetup needed: press EDIT and pick the database items." : string.Empty)
            };

            if (selected)
            {
                button.BorderBrush = RevitThemeService.ThemeBrush(this, "PrimaryBrush");
                button.BorderThickness = new Thickness(2);
            }

            Border badge = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 2, 0, 4),
                Background = RevitThemeService.ThemeBrush(this, incomplete ? "SurfaceAltBrush" : "PrimaryBrush"),
                BorderBrush = RevitThemeService.ThemeBrush(this, "PrimaryBrush"),
                BorderThickness = new Thickness(incomplete ? 1 : 0)
            };
            badge.Child = new TextBlock
            {
                Text = ShortNameOf(assembly),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = RevitThemeService.ThemeBrush(this, incomplete ? "TextBrush" : "AccentTextBrush")
            };

            TextBlock caption = new TextBlock
            {
                Text = assembly.Name,
                FontSize = 9,
                MaxWidth = 74,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = RevitThemeService.ThemeBrush(this, "TextBrush")
            };

            StackPanel content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(badge);
            content.Children.Add(caption);
            button.Content = content;

            button.Click += Tile_Click;
            return button;
        }

        private static string ShortNameOf(AssemblyDefinition assembly)
        {
            string text = assembly.ShortName;
            if (string.IsNullOrWhiteSpace(text)) text = assembly.Name ?? "?";
            text = text.Trim();
            return text.Length > 3 ? text.Substring(0, 3).ToUpperInvariant() : text.ToUpperInvariant();
        }

        private void Tile_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;

            _selectedId = button.Tag as string;
            AssemblyStore.Library.SelectedId = _selectedId;
            AssemblyStore.Save();
            RebuildTiles();
        }

        // ============================================================
        // DETAILS
        // ============================================================

        private void ShowDetails()
        {
            AssemblyDefinition assembly = AssemblyStore.Find(_selectedId);
            if (assembly == null)
            {
                DetailsCard.Visibility = Visibility.Collapsed;
                return;
            }

            DetailsCard.Visibility = Visibility.Visible;
            DetailName.Text = assembly.Name;
            DetailCount.Text = assembly.Steps.Count + (assembly.Steps.Count == 1 ? " ITEM" : " ITEMS");

            BuildSequence(assembly);

            int missing = assembly.UnassignedCount;
            if (missing > 0)
            {
                DetailWarning.Text = missing + " step(s) still need a database item. Press EDIT and pick them from the database.";
                DetailWarning.Visibility = Visibility.Visible;
            }
            else
            {
                DetailWarning.Visibility = Visibility.Collapsed;
            }

            PlaceButton.IsEnabled = missing == 0 && assembly.Steps.Count > 0;
        }

        private void BuildSequence(AssemblyDefinition assembly)
        {
            SequencePanel.Children.Clear();

            for (int i = 0; i < assembly.Steps.Count; i++)
            {
                if (i > 0)
                {
                    SequencePanel.Children.Add(new TextBlock
                    {
                        Text = "\u203A",
                        FontSize = 16,
                        Margin = new Thickness(3, 4, 3, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = RevitThemeService.ThemeBrush(this, "TextSecondaryBrush")
                    });
                }

                SequencePanel.Children.Add(CreateStepThumb(assembly.Steps[i], i));
            }
        }

        private Border CreateStepThumb(AssemblyStep step, int index)
        {
            bool assigned = step.Item != null && step.Item.IsAssigned;

            Border border = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 4),
                Background = RevitThemeService.ThemeBrush(this, "InputBrush"),
                BorderBrush = RevitThemeService.ThemeBrush(this, assigned ? "BorderBrush" : "PrimaryBrush"),
                ToolTip = (index + 1) + ". " + step.DisplayName + "\n" + (step.Item == null ? "No item assigned" : step.Item.Describe())
            };

            BitmapSource icon = assigned ? AssemblyIconCache.Load(step.Item.IconFile) : null;
            if (icon != null)
            {
                border.Child = new Image { Source = icon, Stretch = Stretch.Uniform, Margin = new Thickness(3) };
            }
            else
            {
                string letter = "?";
                if (assigned && !string.IsNullOrEmpty(step.Item.ButtonName))
                    letter = step.Item.ButtonName.Substring(0, 1).ToUpperInvariant();

                border.Child = new TextBlock
                {
                    Text = letter,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = RevitThemeService.ThemeBrush(this, assigned ? "TextBrush" : "PrimaryBrush")
                };
            }

            return border;
        }

        // ============================================================
        // ACTIONS
        // ============================================================

        private void PlaceButton_Click(object sender, RoutedEventArgs e)
        {
            AssemblyDefinition assembly = AssemblyStore.Find(_selectedId);
            if (assembly == null) return;

            if (assembly.UnassignedCount > 0)
            {
                SetStatus("Assign the missing items first (EDIT).");
                return;
            }

            SetStatus("Building " + assembly.Name + "...");
            _controller.RequestApply(Dispatcher, assembly.Clone(), SetStatus);
        }

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            AssemblyDefinition fresh = new AssemblyDefinition
            {
                Name = "New Assembly",
                ShortName = "NEW"
            };
            OpenBuilder(fresh, true);
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            AssemblyDefinition assembly = AssemblyStore.Find(_selectedId);
            if (assembly == null) return;
            OpenBuilder(assembly.Clone(), false);
        }

        private void DuplicateButton_Click(object sender, RoutedEventArgs e)
        {
            AssemblyDefinition assembly = AssemblyStore.Find(_selectedId);
            if (assembly == null) return;

            AssemblyDefinition copy = assembly.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = assembly.Name + " Copy";

            AssemblyStore.Upsert(copy);
            _selectedId = copy.Id;
            AssemblyStore.Library.SelectedId = copy.Id;
            AssemblyStore.Save();
            RebuildTiles();
            SetStatus("Duplicated as '" + copy.Name + "'. Press EDIT to adapt it.");
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            AssemblyDefinition assembly = AssemblyStore.Find(_selectedId);
            if (assembly == null) return;

            if (!CtsConfirmWindow.Ask(
                    "Delete assembly",
                    "Delete '" + assembly.Name + "'?\n\nThis only removes the assembly from CTS Assemblies. " +
                    "Nothing in the model is changed.",
                    "DELETE"))
                return;

            string name = assembly.Name;
            AssemblyStore.Remove(assembly.Id);
            _selectedId = AssemblyStore.Library.SelectedId;
            RebuildTiles();
            SetStatus("'" + name + "' deleted.");
        }

        // ============================================================
        // PROJECT SHARING
        //
        // Each Revit version keeps its own library and starts from the shipped
        // defaults, so nothing is inherited automatically. Teams share on purpose:
        // the lead builds the sequences, exports one file, and everybody imports it.
        // ============================================================

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (AssemblyStore.All.Count == 0)
            {
                SetStatus("There is nothing to export yet.");
                return;
            }

            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export CTS Assemblies",
                Filter = "CTS Assemblies (*.xml)|*.xml|All files (*.*)|*.*",
                DefaultExt = ".xml",
                AddExtension = true,
                FileName = AssemblyStore.SuggestedExportFileName
            };

            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            try
            {
                AssemblyStore.ExportTo(dialog.FileName);
                SetStatus(AssemblyStore.All.Count + " assembly(ies) exported to " +
                          Path.GetFileName(dialog.FileName) + ".");
            }
            catch (Exception ex)
            {
                CtsMessageWindow.Show(
                    "CTS Assemblies - Export",
                    "Could not export the assemblies.\n\nPath:\n" + dialog.FileName +
                    "\n\nError:\n" + ex.Message);
                SetStatus("Export failed.");
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import CTS Assemblies",
                Filter = "CTS Assemblies (*.xml)|*.xml|All files (*.*)|*.*",
                DefaultExt = ".xml",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            // Merging is the safe default, so someone who already built their own
            // sequences does not lose them by importing the project file.
            bool replaceAll = false;

            if (AssemblyStore.All.Count > 0)
            {
                replaceAll = CtsConfirmWindow.Ask(
                    "Import assemblies",
                    "You already have " + AssemblyStore.All.Count + " assembly(ies).\n\n" +
                    "REPLACE ALL removes them and keeps only what is in the file.\n" +
                    "MERGE keeps yours and updates any assembly with the same name.",
                    "REPLACE ALL",
                    "MERGE");
            }

            try
            {
                AssemblyStore.ImportSummary summary = AssemblyStore.Import(dialog.FileName, replaceAll);
                _selectedId = AssemblyStore.Library.SelectedId;
                RebuildTiles();
                SetStatus(summary.ToString());
            }
            catch (Exception ex)
            {
                CtsMessageWindow.Show(
                    "CTS Assemblies - Import",
                    "Could not import the assemblies.\n\nFile:\n" + dialog.FileName +
                    "\n\nError:\n" + ex.Message);
                SetStatus("Import failed.");
            }
        }

        private void OpenBuilder(AssemblyDefinition definition, bool isNew)
        {
            if (_builder != null)
            {
                try { _builder.Activate(); } catch { }
                return;
            }

            _builder = new AssemblyBuilderWindow(_controller, definition, isNew, OnBuilderSaved);
            _builder.Closed += delegate { _builder = null; };

            try
            {
                // Keep the builder on top of Revit without blocking it (the database is read
                // through an ExternalEvent, so the window has to stay modeless).
                new WindowInteropHelper(_builder).Owner = Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { }

            _builder.Show();
        }

        private void OnBuilderSaved(AssemblyDefinition saved)
        {
            AssemblyStore.Upsert(saved);
            _selectedId = saved.Id;
            AssemblyStore.Library.SelectedId = saved.Id;
            AssemblyStore.Save();
            RebuildTiles();
            SetStatus("'" + saved.Name + "' saved.");
        }

        public void SetStatus(string message)
        {
            if (Dispatcher.CheckAccess()) StatusText.Text = message ?? string.Empty;
            else Dispatcher.BeginInvoke(new Action(delegate { StatusText.Text = message ?? string.Empty; }));
        }
    }
}
