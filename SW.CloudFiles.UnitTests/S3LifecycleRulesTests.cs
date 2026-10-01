using System.Collections.Generic;
using System.Linq;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.CloudFiles.S3;

namespace SW.CloudFiles.UnitTests;

/// <summary>Pure rule logic, no bucket needed — unlike the other tests in this project.</summary>
[TestClass]
public class S3LifecycleRulesTests
{
    private static LifecycleRule PrefixRule(string id, string prefix, int days, bool enabled = true) => new()
    {
        Id = id,
        Expiration = new LifecycleRuleExpiration { Days = days },
        Filter = new LifecycleFilter { LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = prefix } },
        Status = enabled ? LifecycleRuleStatus.Enabled : LifecycleRuleStatus.Disabled
    };

    [TestMethod]
    public void Adding_the_temp_rules_keeps_every_rule_already_on_the_bucket()
    {
        var existing = new List<LifecycleRule> { PrefixRule("logs", "logs/", 14), PrefixRule("temp1", "temp1/", 1) };

        var rules = S3LifecycleRules.WithTempRules(existing);

        CollectionAssert.AreEquivalent(
            new[] { "logs", "temp1", "temp7", "temp30", "temp365" },
            rules.Select(r => r.Id).ToArray());
        Assert.AreEqual(14, rules.Single(r => r.Id == "logs").Expiration.Days);
    }

    [TestMethod]
    public void Nothing_to_write_when_every_temp_rule_is_there_and_enabled()
    {
        var existing = S3LifecycleRules.Temp.Select(t => PrefixRule(t.Id, t.Prefix, t.Days)).ToList();

        Assert.IsNull(S3LifecycleRules.WithTempRules(existing));
    }

    [TestMethod]
    public void A_disabled_temp_rule_is_replaced_not_duplicated()
    {
        var existing = new List<LifecycleRule> { PrefixRule("temp30", "temp30/", 30, enabled: false) };

        var rules = S3LifecycleRules.WithTempRules(existing);

        var temp30 = rules.Where(r => r.Id == "temp30").ToList();
        Assert.AreEqual(1, temp30.Count);
        Assert.AreEqual(LifecycleRuleStatus.Enabled, temp30[0].Status);
    }

    [TestMethod]
    public void Reads_prefix_filtered_expirations_as_deletion_rules()
    {
        var rules = S3LifecycleRules.ToDeletionRules([PrefixRule("temp30", "temp30/", 30)]);

        var rule = rules.Single();
        Assert.AreEqual("temp30/", rule.Prefix);
        Assert.AreEqual(30, rule.Days);
        Assert.IsTrue(rule.Enabled);
    }

    [TestMethod]
    public void A_rule_without_a_filter_covers_the_whole_bucket()
    {
        var rules = S3LifecycleRules.ToDeletionRules([new LifecycleRule
        {
            Id = "everything",
            Expiration = new LifecycleRuleExpiration { Days = 90 },
            Status = LifecycleRuleStatus.Enabled
        }]);

        Assert.AreEqual(string.Empty, rules.Single().Prefix);
    }

    [TestMethod]
    public void Tag_filtered_and_non_expiring_rules_are_left_out()
    {
        var rules = S3LifecycleRules.ToDeletionRules([
            new LifecycleRule
            {
                Id = "tagged",
                Expiration = new LifecycleRuleExpiration { Days = 7 },
                Filter = new LifecycleFilter
                {
                    LifecycleFilterPredicate = new LifecycleTagPredicate { Tag = new Tag { Key = "k", Value = "v" } }
                },
                Status = LifecycleRuleStatus.Enabled
            },
            new LifecycleRule
            {
                Id = "transition-only",
                Filter = new LifecycleFilter { LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = "cold/" } },
                Status = LifecycleRuleStatus.Enabled
            }
        ]);

        Assert.AreEqual(0, rules.Count);
    }
}
