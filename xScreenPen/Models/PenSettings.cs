using System.Runtime.Serialization;

namespace xScreenPen.Models
{
    public enum ToolMode
    {
        Mouse = 0,
        Pen = 1,
        Eraser = 2
    }

    [DataContract]
    public class PenSettings
    {
        public const int CurrentSchemaVersion = 1;

        [DataMember(Order = 0)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [DataMember(Order = 1)]
        public string DefaultColorHex { get; set; } = "#FFFFFFFF";

        [DataMember(Order = 2)]
        public double DefaultPenSize { get; set; } = 4;

        [DataMember(Order = 3)]
        public ToolMode StartupToolMode { get; set; } = ToolMode.Mouse;

        [DataMember(Order = 4)]
        public bool IsToolbarVisible { get; set; } = true;

        [DataMember(Order = 5)]
        public bool IsToolbarExpanded { get; set; } = false;

        public PenSettings Clone()
        {
            return new PenSettings
            {
                SchemaVersion = SchemaVersion,
                DefaultColorHex = DefaultColorHex,
                DefaultPenSize = DefaultPenSize,
                StartupToolMode = StartupToolMode,
                IsToolbarVisible = IsToolbarVisible,
                IsToolbarExpanded = IsToolbarExpanded
            };
        }
    }
}
