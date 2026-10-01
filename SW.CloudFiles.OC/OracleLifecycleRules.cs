using System.Collections.Generic;
using System.Linq;
using Oci.ObjectstorageService.Models;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.OC
{
    /// <summary>How an Object Storage lifecycle policy reads as deletion rules.</summary>
    internal static class OracleLifecycleRules
    {
        /// <summary>
        /// The rules that delete objects by age. Archive and tiering actions don't delete anything, and a
        /// rule narrowed by name patterns only reaches some of the objects under its prefixes, so both are
        /// left out.
        /// </summary>
        internal static IReadOnlyList<CloudFilesLifecycleRule> ToDeletionRules(IEnumerable<ObjectLifecycleRule> rules)
        {
            var result = new List<CloudFilesLifecycleRule>();

            foreach (var rule in rules ?? Enumerable.Empty<ObjectLifecycleRule>())
            {
                if (rule.Action != "DELETE" || rule.TimeAmount is not > 0) continue;
                if (rule.Target != null && rule.Target != "objects") continue;

                var filter = rule.ObjectNameFilter;
                if (filter?.InclusionPatterns?.Count > 0 || filter?.ExclusionPatterns?.Count > 0) continue;

                var days = (int)rule.TimeAmount.Value *
                           (rule.TimeUnit == ObjectLifecycleRule.TimeUnitEnum.Years ? 365 : 1);
                var prefixes = filter?.InclusionPrefixes?.Count > 0
                    ? filter.InclusionPrefixes
                    : new List<string> { string.Empty };

                result.AddRange(prefixes.Select(prefix => new CloudFilesLifecycleRule
                {
                    Id = rule.Name,
                    Prefix = prefix ?? string.Empty,
                    Days = days,
                    Enabled = rule.IsEnabled ?? false
                }));
            }

            return result;
        }
    }
}
