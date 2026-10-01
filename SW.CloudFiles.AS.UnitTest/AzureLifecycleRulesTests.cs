using System.Collections.Generic;
using System.Linq;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.CloudFiles.AS;

namespace SW.CloudFiles.AS.UnitTest;

/// <summary>Pure policy logic, no storage account needed — unlike the other tests in this project.</summary>
[TestClass]
public class AzureLifecycleRulesTests
{
    private static ManagementPolicyRule DeleteRule(string name, float days, params string[] prefixes)
    {
        var filter = new ManagementPolicyFilter(["blockBlob"]);
        foreach (var prefix in prefixes) filter.PrefixMatch.Add(prefix);
        var actions = new ManagementPolicyAction
        {
            BaseBlob = new ManagementPolicyBaseBlob { Delete = new DateAfterModification { DaysAfterModificationGreaterThan = days } }
        };
        return new ManagementPolicyRule(name, ManagementPolicyRuleType.Lifecycle,
            new ManagementPolicyDefinition(actions) { Filters = filter });
    }

    [TestMethod]
    public void Adds_this_containers_temp_rules_and_keeps_the_rest_of_the_policy()
    {
        var data = new StorageAccountManagementPolicyData();
        data.Rules.Add(DeleteRule("someone-elses", 14, "logs/"));
        data.Rules.Add(DeleteRule("other-temp30", 30, "other/temp30/"));

        Assert.IsTrue(AzureLifecycleRules.AddTempRules(data, "docs"));

        CollectionAssert.AreEquivalent(
            new[] { "someone-elses", "other-temp30", "docs-temp1", "docs-temp7", "docs-temp30", "docs-temp365" },
            data.Rules.Select(r => r.Name).ToArray());
        Assert.AreEqual("docs/temp30/", data.Rules.Single(r => r.Name == "docs-temp30").Definition.Filters.PrefixMatch.Single());
    }

    [TestMethod]
    public void Nothing_to_write_when_the_rules_are_already_there()
    {
        var data = new StorageAccountManagementPolicyData();
        AzureLifecycleRules.AddTempRules(data, "docs");

        Assert.IsFalse(AzureLifecycleRules.AddTempRules(data, "docs"));
    }

    [TestMethod]
    public void A_disabled_temp_rule_is_replaced_not_duplicated()
    {
        var data = new StorageAccountManagementPolicyData();
        var disabled = DeleteRule("docs-temp30", 30, "docs/temp30/");
        disabled.IsEnabled = false;
        data.Rules.Add(disabled);

        AzureLifecycleRules.AddTempRules(data, "docs");

        var temp30 = data.Rules.Where(r => r.Name == "docs-temp30").ToList();
        Assert.AreEqual(1, temp30.Count);
        Assert.IsTrue(temp30[0].IsEnabled);
    }

    [TestMethod]
    public void Reads_only_this_containers_prefixes_relative_to_the_container()
    {
        var rules = AzureLifecycleRules.ToDeletionRules([
            DeleteRule("docs-temp30", 30, "docs/temp30/"),
            DeleteRule("other-temp30", 30, "other/temp30/")
        ], "docs");

        var rule = rules.Single();
        Assert.AreEqual("temp30/", rule.Prefix);
        Assert.AreEqual(30, rule.Days);
        Assert.IsTrue(rule.Enabled);
    }

    [TestMethod]
    public void A_rule_without_prefixes_covers_every_container()
    {
        Assert.AreEqual(string.Empty, AzureLifecycleRules.ToDeletionRules([DeleteRule("all", 90)], "docs").Single().Prefix);
    }

    [TestMethod]
    public void Tag_filtered_rules_and_rules_without_a_delete_are_left_out()
    {
        var tagged = DeleteRule("tagged", 7, "docs/temp7/");
        tagged.Definition.Filters.BlobIndexMatch.Add(new ManagementPolicyTagFilter("kind", "==", "scratch"));
        var tiering = new ManagementPolicyRule("tiering", ManagementPolicyRuleType.Lifecycle,
            new ManagementPolicyDefinition(new ManagementPolicyAction
            {
                BaseBlob = new ManagementPolicyBaseBlob
                {
                    TierToCool = new DateAfterModification { DaysAfterModificationGreaterThan = 30 }
                }
            }));

        Assert.AreEqual(0, AzureLifecycleRules.ToDeletionRules([tagged, tiering], "docs").Count);
    }
}
