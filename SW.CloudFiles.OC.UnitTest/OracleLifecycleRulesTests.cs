using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Oci.ObjectstorageService.Models;
using SW.CloudFiles.OC;

namespace SW.CloudFiles.OC.UnitTest
{
    /// <summary>Pure rule logic, no bucket needed — unlike the other tests in this project.</summary>
    [TestClass]
    public class OracleLifecycleRulesTests
    {
        private static ObjectLifecycleRule Delete(string name, long amount, ObjectLifecycleRule.TimeUnitEnum unit,
            params string[] prefixes) => new ObjectLifecycleRule
        {
            Name = name,
            Action = "DELETE",
            TimeAmount = amount,
            TimeUnit = unit,
            IsEnabled = true,
            Target = "objects",
            ObjectNameFilter = new ObjectNameFilter { InclusionPrefixes = prefixes.ToList() }
        };

        [TestMethod]
        public void Reads_one_rule_per_included_prefix()
        {
            var rules = OracleLifecycleRules.ToDeletionRules(new[]
            {
                Delete("delete-temp", 30, ObjectLifecycleRule.TimeUnitEnum.Days, "temp30/", "scratch/")
            });

            CollectionAssert.AreEquivalent(new[] { "temp30/", "scratch/" }, rules.Select(r => r.Prefix).ToArray());
            Assert.IsTrue(rules.All(r => r.Days == 30 && r.Enabled));
        }

        [TestMethod]
        public void Years_count_as_365_days()
        {
            var rule = OracleLifecycleRules.ToDeletionRules(new[]
            {
                Delete("delete-temp365", 1, ObjectLifecycleRule.TimeUnitEnum.Years, "temp365/")
            }).Single();

            Assert.AreEqual(365, rule.Days);
        }

        [TestMethod]
        public void A_rule_with_no_prefixes_covers_the_whole_bucket()
        {
            var rule = OracleLifecycleRules.ToDeletionRules(new[]
            {
                Delete("delete-all", 7, ObjectLifecycleRule.TimeUnitEnum.Days)
            }).Single();

            Assert.AreEqual(string.Empty, rule.Prefix);
        }

        [TestMethod]
        public void Archive_actions_and_pattern_filtered_rules_are_left_out()
        {
            var archive = Delete("archive", 30, ObjectLifecycleRule.TimeUnitEnum.Days, "cold/");
            archive.Action = "ARCHIVE";
            var patterned = Delete("patterned", 30, ObjectLifecycleRule.TimeUnitEnum.Days, "temp30/");
            patterned.ObjectNameFilter.InclusionPatterns = new List<string> { "*.log" };

            Assert.AreEqual(0, OracleLifecycleRules.ToDeletionRules(new[] { archive, patterned }).Count);
        }
    }
}
