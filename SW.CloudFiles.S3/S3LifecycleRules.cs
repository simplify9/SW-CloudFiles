using System.Collections.Generic;
using System.Linq;
using Amazon.S3;
using Amazon.S3.Model;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.S3;

/// <summary>The temp-prefix rules this library keeps on a bucket, and how S3 rules read as deletion rules.</summary>
internal static class S3LifecycleRules
{
    /// <summary>Files under these prefixes are deleted after the given number of days.</summary>
    internal static readonly (string Id, string Prefix, int Days)[] Temp =
    [
        ("temp1", "temp1/", 1),
        ("temp7", "temp7/", 7),
        ("temp30", "temp30/", 30),
        ("temp365", "temp365/", 365)
    ];

    /// <summary>
    /// The bucket's whole rule set with every missing or disabled temp rule put back, or <c>null</c> when
    /// nothing needs changing. S3 replaces the entire configuration on every write, so the rules this
    /// library doesn't own have to be sent back too or they are deleted.
    /// </summary>
    internal static List<LifecycleRule> WithTempRules(IEnumerable<LifecycleRule> existing)
    {
        var rules = existing?.ToList() ?? [];
        var changed = false;

        foreach (var (id, prefix, days) in Temp)
        {
            if (rules.Any(r => r.Id == id && r.Status == LifecycleRuleStatus.Enabled)) continue;

            rules.RemoveAll(r => r.Id == id);
            rules.Add(new LifecycleRule
            {
                Id = id,
                Expiration = new LifecycleRuleExpiration { Days = days },
                Filter = new LifecycleFilter
                {
                    LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = prefix }
                },
                Status = LifecycleRuleStatus.Enabled
            });
            changed = true;
        }

        return changed ? rules : null;
    }

    /// <summary>
    /// The rules that delete files by age. A rule filtered on tags or object size only reaches some of
    /// the files under its prefix, so it is left out rather than reported as deleting all of them.
    /// </summary>
    internal static IReadOnlyList<CloudFilesLifecycleRule> ToDeletionRules(IEnumerable<LifecycleRule> rules)
    {
        var result = new List<CloudFilesLifecycleRule>();

        foreach (var rule in rules ?? [])
        {
            if (rule.Expiration == null || rule.Expiration.Days <= 0) continue;

            string prefix;
            switch (rule.Filter?.LifecycleFilterPredicate)
            {
                case LifecyclePrefixPredicate predicate:
                    prefix = predicate.Prefix ?? string.Empty;
                    break;
                case null:
                    // No filter predicate: the older rule-level prefix, or the whole bucket.
#pragma warning disable CS0618
                    prefix = rule.Prefix ?? string.Empty;
#pragma warning restore CS0618
                    break;
                default:
                    continue;
            }

            result.Add(new CloudFilesLifecycleRule
            {
                Id = rule.Id,
                Prefix = prefix,
                Days = rule.Expiration.Days,
                Enabled = rule.Status == LifecycleRuleStatus.Enabled
            });
        }

        return result;
    }
}
