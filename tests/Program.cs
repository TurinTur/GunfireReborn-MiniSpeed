using MiniSpeed;

var pricing = UpgradePricing.Default;
int[] expected = { 40, 80, 150, 200, 300, 400, 500, 600, 640, 680, 720 };
for (int level = 0; level < expected.Length; level++)
    if (pricing.Cost(level) != expected[level]) throw new Exception($"Wrong cost at +{level}");
if (pricing.Cost(35) != 1720 || pricing.Cost(100) != 4320) throw new Exception("High-level scaling is incorrect.");
for (int level = 8; level <= 10000; level++)
    if (pricing.Cost(level) - pricing.Cost(level - 1) != 40) throw new Exception($"Incorrect increment at +{level}");
if (pricing.Cost(int.MaxValue) != int.MaxValue) throw new Exception("Overflow protection failed.");
try { pricing.Cost(-1); throw new Exception("Negative level accepted."); }
catch (ArgumentOutOfRangeException) { }
var custom = new UpgradePricing(true, new[] { 1, 2, 3, 4, 5, 6, 7 }, 10, 2);
if (custom.Cost(6) != 7 || custom.Cost(7) != 10 || custom.Cost(10) != 16) throw new Exception("Custom pricing failed.");
Console.WriteLine("Pricing checks passed: requested table, level boundaries, +40 scaling, custom settings, negative levels, and overflow.");
