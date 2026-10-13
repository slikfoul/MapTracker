using System;
using BepInEx.Configuration;

namespace MapTracker
{
    internal static class RadiusSetting
    {
        internal const int Minimum = 0, LineOfSight = 0, Maximum = 300, Unlimited = 301;
        internal static float Distance(int selected) => selected == Unlimited || selected == LineOfSight ? 0 : selected;
        internal static int Convert(float previous, bool alreadyConverted)
        {
            if (float.IsNaN(previous) || float.IsInfinity(previous) || previous <= 0 || (alreadyConverted && previous == Unlimited)) return Unlimited;
            return Math.Max(1, Math.Min(Maximum, (int)Math.Round(previous, MidpointRounding.AwayFromZero)));
        }
        internal static ConfigEntry<int> Bind(ConfigFile config, Action<ConfigEntryBase> drawer)
        {
            bool autosave = config.SaveOnConfigSet; config.SaveOnConfigSet = false;
            try
            {
                var revision = config.Bind("Internal", "Radius slider revision", 0,
                    new ConfigDescription("Integer radius slider conversion revision.", null, new ConfigurationManagerAttributes { Browsable = false }));
                // Read the old floating value before changing the setting's type,
                // so fractional values are rounded rather than rejected by TOML.
                var previous = ConfigSections.Bind(config, "General", "Detection", "Detection radius", 0f, new ConfigDescription("Radius before integer conversion."));
                int initial = revision.Value >= 2 && previous.Value == LineOfSight ? LineOfSight : Convert(previous.Value, revision.Value > 0);
                config.Remove(previous.Definition);
                var result = config.Bind("Detection", "Detection radius", DefaultSettings.Value("Detection", "Detection radius", Unlimited), new ConfigDescription(
                    "Leftmost position: discover objects in the camera view with unobstructed sight from the character and camera, including mist checks. Other positions are whole meters; rightmost infinity uses all client data. Remembered local discoveries remain visible when Keep discovered is enabled.",
                    new AcceptableValueRange<int>(Minimum, Unlimited), new ConfigurationManagerAttributes
                    { DispName = "Discovery radius (m)", CustomDrawer = drawer }));
                result.Value = initial;
                revision.Value = 2;
                return result;
            }
            finally { config.SaveOnConfigSet = autosave; }
        }
    }
}
