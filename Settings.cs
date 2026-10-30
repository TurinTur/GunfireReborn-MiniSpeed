using System.Globalization;
using Doorstop;

namespace MiniSpeed;

internal static class Settings
{
    internal static bool Enabled = true;
    internal static float Multiplier = 1.5f;
    internal static UpgradePricing Upgrades = UpgradePricing.Default;

    internal static void Reload()
    {
        try
        {
            var path = Path.Combine(Entrypoint.Root, "MiniSpeed.cfg");
            if (!File.Exists(path))
                File.WriteAllText(path, "# F6 toggles movement; F7 reloads this file.\n[Movement]\nEnabled = true\nSpeedMultiplier = 1.5\n\n[WeaponUpgrades]\nEnabled = true\nLevels0To6 = 40, 80, 150, 200, 300, 400, 500\nLevel7Cost = 600\nPerLevelIncrease = 40\n");
            bool enabled = true;
            float multiplier = 1.5f;
            bool upgradesEnabled = true;
            int[] earlyCosts = (int[])UpgradePricing.Default.EarlyCosts.Clone();
            int level7Cost = 600, perLevelIncrease = 40;
            string section = "Movement";
            foreach (var line in File.ReadLines(path))
            {
                string text = line.Trim();
                if (text.StartsWith('#') || text.StartsWith(';') || text.Length == 0) continue;
                if (text.StartsWith('[') && text.EndsWith(']')) { section = text[1..^1]; continue; }
                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2) continue;
                if (section == "WeaponUpgrades")
                {
                    switch (parts[0])
                    {
                        case "Enabled": upgradesEnabled = bool.Parse(parts[1]); break;
                        case "Levels0To6":
                            earlyCosts = parts[1].Split(',').Select(v => int.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray();
                            if (earlyCosts.Length != 7 || earlyCosts.Any(v => v < 0)) throw new FormatException("Levels0To6 requires seven nonnegative costs.");
                            break;
                        case "Level7Cost": level7Cost = int.Parse(parts[1], CultureInfo.InvariantCulture); break;
                        case "PerLevelIncrease": perLevelIncrease = int.Parse(parts[1], CultureInfo.InvariantCulture); break;
                    }
                    continue;
                }
                if (section != "Movement") continue;
                if (parts[0] == "Enabled")
                    enabled = bool.Parse(parts[1]);
                if (parts[0] == "SpeedMultiplier")
                {
                    multiplier = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    if (!float.IsFinite(multiplier) || multiplier < 0.25f || multiplier > 5f)
                        throw new FormatException("SpeedMultiplier must be between 0.25 and 5.");
                }
            }
            if (level7Cost < 0 || perLevelIncrease < 0) throw new FormatException("Upgrade costs and increases must be nonnegative.");
            Enabled = enabled;
            Multiplier = multiplier;
            Upgrades = new UpgradePricing(upgradesEnabled, earlyCosts, level7Cost, perLevelIncrease);
            Entrypoint.Log($"Settings loaded: Enabled={Enabled}, SpeedMultiplier={Multiplier.ToString(CultureInfo.InvariantCulture)}");
            Entrypoint.Log($"Weapon upgrades: Enabled={upgradesEnabled}, levels 0-6=[{string.Join(",", earlyCosts)}], level 7={level7Cost}, increase={perLevelIncrease}.");
        }
        catch (Exception ex) when (ex is IOException or FormatException or OverflowException or UnauthorizedAccessException)
        {
            Entrypoint.Log($"Settings unchanged: {ex.Message}");
        }
    }
}
