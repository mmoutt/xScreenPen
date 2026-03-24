using System;
using System.IO;
using System.Runtime.Serialization.Json;
using xScreenPen.Models;

namespace xScreenPen.Services
{
    internal sealed class SettingsService
    {
        private static readonly string[] AllowedColors =
        {
            "#FFFFFFFF",
            "#FF000000",
            "#FFFF0000",
            "#FF0066FF",
            "#FF00CC00",
            "#FFFFCC00"
        };

        private static readonly double[] AllowedPenSizes = { 2, 4, 6 };

        public string SettingsPath { get; }

        public SettingsService(string settingsPath = null)
        {
            SettingsPath = settingsPath ?? BuildDefaultSettingsPath();
        }

        public PenSettings Load()
        {
            if (!File.Exists(SettingsPath))
            {
                var defaults = GetDefault();
                Save(defaults);
                return defaults;
            }

            try
            {
                using (var stream = File.OpenRead(SettingsPath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(PenSettings));
                    var loaded = serializer.ReadObject(stream) as PenSettings;
                    var sanitized = Sanitize(loaded);
                    Save(sanitized);
                    return sanitized;
                }
            }
            catch
            {
                var defaults = GetDefault();
                Save(defaults);
                return defaults;
            }
        }

        public void Save(PenSettings settings)
        {
            var sanitized = Sanitize(settings);
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (var stream = File.Create(SettingsPath))
            {
                var serializer = new DataContractJsonSerializer(typeof(PenSettings));
                serializer.WriteObject(stream, sanitized);
            }
        }

        public PenSettings GetDefault()
        {
            return new PenSettings();
        }

        public PenSettings Sanitize(PenSettings settings)
        {
            var sanitized = settings?.Clone() ?? GetDefault();

            sanitized.SchemaVersion = PenSettings.CurrentSchemaVersion;
            sanitized.DefaultColorHex = NormalizeColor(sanitized.DefaultColorHex);
            sanitized.DefaultPenSize = NormalizePenSize(sanitized.DefaultPenSize);

            if (!Enum.IsDefined(typeof(ToolMode), sanitized.StartupToolMode))
            {
                sanitized.StartupToolMode = ToolMode.Mouse;
            }

            return sanitized;
        }

        private static string BuildDefaultSettingsPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "xScreenPen", "settings.json");
        }

        private static string NormalizeColor(string colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return AllowedColors[0];
            }

            var normalized = colorHex.Trim().ToUpperInvariant();
            foreach (var allowedColor in AllowedColors)
            {
                if (string.Equals(allowedColor, normalized, StringComparison.Ordinal))
                {
                    return normalized;
                }
            }

            return AllowedColors[0];
        }

        private static double NormalizePenSize(double size)
        {
            if (double.IsNaN(size) || double.IsInfinity(size))
            {
                return 4;
            }

            double nearest = AllowedPenSizes[0];
            double minDistance = Math.Abs(AllowedPenSizes[0] - size);

            for (var i = 1; i < AllowedPenSizes.Length; i++)
            {
                var distance = Math.Abs(AllowedPenSizes[i] - size);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = AllowedPenSizes[i];
                }
            }

            return nearest;
        }
    }
}
