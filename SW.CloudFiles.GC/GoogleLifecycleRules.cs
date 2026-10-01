using System.Collections.Generic;
using System.Linq;
using Google.Apis.Storage.v1.Data;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.GC;

/// <summary>How a Cloud Storage bucket's lifecycle rules read as deletion rules.</summary>
internal static class GoogleLifecycleRules
{
    /// <summary>
    /// The rules that delete objects by age. Any other condition (storage class, suffix, dates, versions)
    /// narrows a rule to some of the objects under its prefixes, so such rules are left out.
    /// </summary>
    internal static IReadOnlyList<CloudFilesLifecycleRule> ToDeletionRules(IEnumerable<Bucket.LifecycleData.RuleData> rules)
    {
        var result = new List<CloudFilesLifecycleRule>();

        foreach (var rule in rules ?? Enumerable.Empty<Bucket.LifecycleData.RuleData>())
        {
            var condition = rule.Condition;
            if (rule.Action?.Type != "Delete" || condition?.Age is not > 0) continue;
            if (condition.CreatedBefore != null || condition.CustomTimeBefore != null ||
                condition.DaysSinceCustomTime != null || condition.DaysSinceNoncurrentTime != null ||
                condition.IsLive != null || condition.NoncurrentTimeBefore != null ||
                condition.NumNewerVersions != null || condition.MatchesStorageClass?.Count > 0 ||
                condition.MatchesSuffix?.Count > 0)
                continue;

            var prefixes = condition.MatchesPrefix?.Count > 0 ? condition.MatchesPrefix : [string.Empty];
            result.AddRange(prefixes.Select(prefix => new CloudFilesLifecycleRule
            {
                // Cloud Storage rules have no names.
                Id = null,
                Prefix = prefix ?? string.Empty,
                Days = condition.Age.Value,
                // A rule is either present or absent; there is no switch to turn one off.
                Enabled = true
            }));
        }

        return result;
    }
}
