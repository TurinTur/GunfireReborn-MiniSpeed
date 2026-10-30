namespace MiniSpeed;

internal sealed record UpgradePricing(bool Enabled, int[] EarlyCosts, int Level7Cost, int PerLevelIncrease)
{
    internal static readonly UpgradePricing Default = new(true, new[] { 40, 80, 150, 200, 300, 400, 500 }, 600, 40);

    // Level means the current +N, before purchasing the upgrade to +(N+1).
    internal int Cost(int level)
    {
        if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));
        return level < 7 ? EarlyCosts[level] : (int)Math.Min(int.MaxValue, Level7Cost + (long)(level - 7) * PerLevelIncrease);
    }
}
