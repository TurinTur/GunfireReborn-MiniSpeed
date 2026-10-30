using System.Globalization;
using Doorstop;

namespace MiniSpeed;

internal static class Settings
{
    internal static bool Enabled = true;
    internal static float Multiplier = 1.5f;

    internal static void Reload()
    {
        try
        {
            var path = Path.Combine(Entrypoint.Root, "MiniSpeed.cfg");
            if (!File.Exists(path))
                File.WriteAllText(path, "# F6 toggles the mod; F7 reloads this file.\n[Movement]\nEnabled = true\nSpeedMultiplier = 1.5\n");
            bool enabled = true;
            float multiplier = 1.5f;
            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2) continue;
                if (parts[0] == "Enabled")
                    enabled = bool.Parse(parts[1]);
                if (parts[0] == "SpeedMultiplier")
                {
                    multiplier = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    if (!float.IsFinite(multiplier) || multiplier < 0.25f || multiplier > 5f)
                        throw new FormatException("SpeedMultiplier must be between 0.25 and 5.");
                }
            }
            Enabled = enabled;
            Multiplier = multiplier;
            Entrypoint.Log($"Settings loaded: Enabled={Enabled}, SpeedMultiplier={Multiplier.ToString(CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            Entrypoint.Log($"Settings unchanged: {ex.Message}");
        }
    }
}
