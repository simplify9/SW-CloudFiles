using System;
using System.Collections.Generic;
using System.Linq;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.AS;

/// <summary>
/// The storage account's lifecycle management policy: the temp-prefix rules this library keeps in it, and
/// how its rules read as deletion rules for one container.
/// </summary>
/// <remarks>
/// The policy belongs to the whole account and applies by <c>container/prefix</c>, so every rule this
/// library writes is named and filtered for its own container, and the rules of other containers — or
/// anyone else's — are kept as they are. The policy is always read and written whole.
/// </remarks>
internal static class AzureLifecycleRules
{
    internal static readonly (string Id, string Prefix, int Days)[] Temp =
    [
        ("temp1", "temp1/", 1),
        ("temp7", "temp7/", 7),
        ("temp30", "temp30/", 30),
        ("temp365", "temp365/", 365)
    ];

    /// <summary>Whether the account's policy can be reached: it lives on the Resource Manager plane, found by subscription and resource group.</summary>
    internal static bool CanManage(this AzureCloudFilesOptions options) =>
        !string.IsNullOrWhiteSpace(options.SubscriptionId) && !string.IsNullOrWhiteSpace(options.ResourceGroupName);

    internal static string AccountName(this AzureCloudFilesOptions options) =>
        !string.IsNullOrWhiteSpace(options.StorageAccountName) ? options.StorageAccountName
        : options.Managed ? new Uri(options.ServiceUrl).Host.Split('.')[0]
        : options.AccessKeyId;

    /// <summary>
    /// The account's management policy. The managed identity when one is configured, and otherwise
    /// whatever <see cref="DefaultAzureCredential"/> finds — a service principal in the AZURE_* variables.
    /// A shared key can't be used: it only opens the blob endpoint.
    /// </summary>
    internal static StorageAccountManagementPolicyResource ManagementPolicy(this AzureCloudFilesOptions options)
    {
        var credential = options.ManagedIdentityClientId is not null
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = options.ManagedIdentityClientId })
            : new DefaultAzureCredential();
        var account = StorageAccountResource.CreateResourceIdentifier(options.SubscriptionId, options.ResourceGroupName,
            options.AccountName());
        return new ArmClient(credential).GetStorageAccountResource(account).GetStorageAccountManagementPolicy();
    }

    internal static string RuleName(string container, string id) => $"{container}-{id}";

    /// <summary>
    /// Adds each missing or disabled temp rule for <paramref name="container"/> to <paramref name="data"/>.
    /// Returns whether anything changed, i.e. whether the policy needs writing back.
    /// </summary>
    internal static bool AddTempRules(StorageAccountManagementPolicyData data, string container)
    {
        var changed = false;
        foreach (var (id, prefix, days) in Temp)
        {
            var name = RuleName(container, id);
            if (data.Rules.Any(r => r.Name == name && r.IsEnabled != false)) continue;

            foreach (var stale in data.Rules.Where(r => r.Name == name).ToList()) data.Rules.Remove(stale);

            var filter = new ManagementPolicyFilter(["blockBlob"]);
            filter.PrefixMatch.Add($"{container}/{prefix}");
            var actions = new ManagementPolicyAction
            {
                BaseBlob = new ManagementPolicyBaseBlob
                {
                    Delete = new DateAfterModification { DaysAfterModificationGreaterThan = days }
                }
            };
            data.Rules.Add(new ManagementPolicyRule(name, ManagementPolicyRuleType.Lifecycle,
                new ManagementPolicyDefinition(actions) { Filters = filter }) { IsEnabled = true });
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// The rules that delete <paramref name="container"/>'s block blobs by age, with prefixes relative to the
    /// container. A rule filtered on blob index tags, or counting from last access, only reaches some of the
    /// blobs under its prefix, so it is left out rather than reported as deleting all of them.
    /// </summary>
    internal static IReadOnlyList<CloudFilesLifecycleRule> ToDeletionRules(IEnumerable<ManagementPolicyRule> rules,
        string container)
    {
        var result = new List<CloudFilesLifecycleRule>();

        foreach (var rule in rules ?? [])
        {
            var filters = rule.Definition?.Filters;
            if (filters?.BlobIndexMatch?.Count > 0) continue;
            if (filters?.BlobTypes?.Count > 0 && !filters.BlobTypes.Contains("blockBlob")) continue;

            var delete = rule.Definition?.Actions?.BaseBlob?.Delete;
            var days = delete?.DaysAfterModificationGreaterThan ?? delete?.DaysAfterCreationGreaterThan;
            if (days is not > 0) continue;

            // No prefix covers every container in the account; otherwise only this container's prefixes count.
            var prefixes = filters?.PrefixMatch?.Count > 0
                ? filters.PrefixMatch
                    .Where(p => p == container || p.StartsWith(container + "/", StringComparison.Ordinal))
                    .Select(p => p == container ? string.Empty : p[(container.Length + 1)..])
                    .ToList()
                : [string.Empty];

            result.AddRange(prefixes.Select(prefix => new CloudFilesLifecycleRule
            {
                Id = rule.Name,
                Prefix = prefix,
                Days = (int)Math.Ceiling(days.Value),
                // Absent means enabled.
                Enabled = rule.IsEnabled ?? true
            }));
        }

        return result;
    }
}
