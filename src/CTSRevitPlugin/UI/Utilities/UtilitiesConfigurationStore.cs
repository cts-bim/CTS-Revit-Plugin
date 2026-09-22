using System;
using System.Collections.Generic;
using System.Linq;
using CTSRevitPlugin.UI.ElementTools;

namespace CTSRevitPlugin.UI.Utilities
{
    public static class UtilitiesConfigurationStore
    {
        private static ElementToolsConfiguration _current;

        public static ElementToolsConfiguration Current
        {
            get
            {
                if (_current == null)
                {
                    _current = ElementToolsConfiguration.Load();
                    ApplyDefaults(_current);
                    _current.Save();
                }
                return _current;
            }
        }

        public static void Apply(ElementToolsConfiguration configuration)
        {
            if (configuration == null) return;
            _current = Clone(configuration);
            ApplyDefaults(_current);
            _current.Save();
        }

        public static void Save()
        {
            Current.Save();
        }

        public static ElementToolsConfiguration Clone(ElementToolsConfiguration source)
        {
            if (source == null) return CreateDefault();

            return new ElementToolsConfiguration
            {
                ShowParameters = source.ShowParameters,
                ShowShortcuts = source.ShowShortcuts,
                ShowHangers = source.ShowHangers,
                PinnedParameters = new List<string>(source.PinnedParameters ?? new List<string>()),
                FavoriteMechanicalProperties = new List<string>(source.FavoriteMechanicalProperties ?? new List<string>()),
                ShortcutTools = new List<string>(source.ShortcutTools ?? new List<string>()),
                HangerTools = new List<string>(source.HangerTools ?? new List<string>()),
                ShowHangerRodAdjustment = source.ShowHangerRodAdjustment,
                HangerRodAdjustmentDefault = source.HangerRodAdjustmentDefault ?? "2\"",
                MechanicalPropertiesElementLimit = source.MechanicalPropertiesElementLimit,
                ShowElementId = true,
                ShowMark = true,
                ShowCategory = true,
                ShowFamily = true,
                ShowType = true,
                EnabledTools = new List<string>(source.EnabledTools ?? new List<string>())
            };
        }

        private static void ApplyDefaults(ElementToolsConfiguration configuration)
        {
            if (configuration.ShortcutTools == null)
                configuration.ShortcutTools = new List<string>();
            if (configuration.HangerTools == null)
                configuration.HangerTools = new List<string>();
            if (configuration.PinnedParameters == null)
                configuration.PinnedParameters = new List<string>();
            if (configuration.FavoriteMechanicalProperties == null)
                configuration.FavoriteMechanicalProperties = new List<string>();

            if (configuration.ShortcutTools.Count == 0)
            {
                configuration.ShortcutTools = ElementToolsRegistry.All
                    .Where(x => !string.Equals(x.Category, "Hanger", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Id)
                    .ToList();
            }
            else if (!configuration.ShortcutTools.Contains("FormatPainter"))
            {
                configuration.ShortcutTools.Add("FormatPainter");
            }

            if (configuration.HangerTools.Count == 0)
            {
                configuration.HangerTools = ElementToolsRegistry.All
                    .Where(x => string.Equals(x.Category, "Hanger", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Id)
                    .ToList();
            }
            else if (!configuration.HangerTools.Contains("RoundStrutChannel"))
            {
                configuration.HangerTools.Add("RoundStrutChannel");
            }

            if (string.IsNullOrWhiteSpace(configuration.HangerRodAdjustmentDefault))
                configuration.HangerRodAdjustmentDefault = "2\"";

            // Settings files written before the limit existed deserialize as 0,
            // which would mean "read nothing". Treat that as the default.
            if (configuration.MechanicalPropertiesElementLimit <= 0)
                configuration.MechanicalPropertiesElementLimit = 100;
        }

        private static ElementToolsConfiguration CreateDefault()
        {
            var configuration = new ElementToolsConfiguration();
            ApplyDefaults(configuration);
            return configuration;
        }
    }
}