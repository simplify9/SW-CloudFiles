using Google.Apis.Storage.v1.Data;
using SW.CloudFiles.GC;

namespace SW.CloudFiles.GC.UnitTests;

/// <summary>Pure rule logic, no bucket needed — unlike the other test in this project.</summary>
[TestClass]
public class GoogleLifecycleRulesTests
{
    private static Bucket.LifecycleData.RuleData Delete(int age, params string[] prefixes) => new()
    {
        Action = new Bucket.LifecycleData.RuleData.ActionData { Type = "Delete" },
        Condition = new Bucket.LifecycleData.RuleData.ConditionData
        {
            Age = age,
            MatchesPrefix = prefixes.Length > 0 ? prefixes.ToList() : null
        }
    };

    [TestMethod]
    public void Reads_one_rule_per_matched_prefix()
    {
        var rules = GoogleLifecycleRules.ToDeletionRules([Delete(30, "temp30/", "scratch/")]);

        CollectionAssert.AreEquivalent(new[] { "temp30/", "scratch/" }, rules.Select(r => r.Prefix).ToArray());
        Assert.IsTrue(rules.All(r => r.Days == 30 && r.Enabled));
    }

    [TestMethod]
    public void A_rule_with_no_prefix_covers_the_whole_bucket()
    {
        Assert.AreEqual(string.Empty, GoogleLifecycleRules.ToDeletionRules([Delete(7)]).Single().Prefix);
    }

    [TestMethod]
    public void Storage_class_changes_and_narrowed_rules_are_left_out()
    {
        var tiering = Delete(30, "cold/");
        tiering.Action.Type = "SetStorageClass";
        var suffixed = Delete(30, "temp30/");
        suffixed.Condition.MatchesSuffix = [".log"];

        Assert.AreEqual(0, GoogleLifecycleRules.ToDeletionRules([tiering, suffixed]).Count);
    }
}
