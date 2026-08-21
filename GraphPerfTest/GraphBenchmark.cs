using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace GraphPerfTest;

/// <summary>
/// Runs a set of timed Microsoft Graph retrievals against a single tenant and prints the
/// elapsed time and object counts for each, mirroring the object types the dashboard
/// collects from Active Roles so the two data paths can be compared directly.
/// </summary>
public sealed class GraphBenchmark
{
    private readonly TenantSettings _tenant;
    private readonly AppSettings _settings;
    private readonly GraphServiceClient _graph;

    public GraphBenchmark(TenantSettings tenant, AppSettings settings)
    {
        _tenant = tenant;
        _settings = settings;

        // App-only (client credentials) flow - no interactive sign-in required. Uses a
        // certificate if one is configured (so the same cert Active Roles uses can be
        // reused), otherwise falls back to the client secret.
        TokenCredential credential = tenant.UsesCertificate
            ? new ClientCertificateCredential(tenant.TenantId, tenant.ClientId, LoadCertificate(tenant))
            : new ClientSecretCredential(tenant.TenantId, tenant.ClientId, tenant.ClientSecret);

        _graph = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
    }

    /// <summary>
    /// Resolves the configured certificate, preferring a PFX/PEM file path and otherwise
    /// looking the thumbprint up in the CurrentUser then LocalMachine "My" stores.
    /// </summary>
    private static X509Certificate2 LoadCertificate(TenantSettings tenant)
    {
        if (!string.IsNullOrWhiteSpace(tenant.CertificatePath))
        {
            var fullPath = Path.IsPathRooted(tenant.CertificatePath)
                ? tenant.CertificatePath
                : Path.Combine(AppContext.BaseDirectory, tenant.CertificatePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Certificate file not found at '{fullPath}'.", fullPath);

            var password = string.IsNullOrEmpty(tenant.CertificatePassword) ? null : tenant.CertificatePassword;
            try
            {
                var cert = X509CertificateLoader.LoadPkcs12FromFile(
                    fullPath,
                    password,
                    X509KeyStorageFlags.EphemeralKeySet);

                if (!cert.HasPrivateKey)
                    throw new InvalidOperationException(
                        $"Certificate '{fullPath}' has no private key. Re-export the PFX including the private key.");

                return cert;
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load PFX '{fullPath}'. This usually means the CertificatePassword is wrong, " +
                    $"or the file is not a valid PKCS#12/PFX containing the private key. Underlying error: {ex.Message}",
                    ex);
            }
        }

        var thumbprint = new string(tenant.CertificateThumbprint
            .Where(Uri.IsHexDigit)
            .ToArray())
            .ToUpperInvariant();

        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var match = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            if (match.Count > 0)
                return match[0];
        }

        throw new InvalidOperationException(
            $"Certificate with thumbprint '{thumbprint}' not found in CurrentUser or LocalMachine 'My' store.");
    }

    public async Task RunAsync()
    {
        Console.WriteLine();
        Console.WriteLine($"=== Tenant: {_tenant.Name} ({_tenant.TenantId}) ===");

        await MeasureUsersAsync();
        await MeasureGroupsAsync();
    }

    /// <summary>Times retrieval of all users (comparable to the Entra User collection).</summary>
    private async Task MeasureUsersAsync()
    {
        var sw = Stopwatch.StartNew();
        var count = 0;
        try
        {
            var page = await _graph.Users.GetAsync(rc =>
            {
                rc.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName", "accountEnabled", "userType" };
                rc.QueryParameters.Top = _settings.PageSize;
            });

            var iterator = PageIterator<User, UserCollectionResponse>
                .CreatePageIterator(_graph, page!, _ =>
                {
                    count++;
                    return _settings.MaxObjectsPerType == 0 || count < _settings.MaxObjectsPerType;
                });
            await iterator.IterateAsync();

            sw.Stop();
            Report("Users", sw, count);
        }
        catch (Exception ex)
        {
            sw.Stop();
            ReportError("Users", sw, ex);
        }
    }

    /// <summary>
    /// Times retrieval of all groups and, optionally, each group's owners and members.
    /// Member retrieval is the operation that is extremely slow via the Active Roles REST
    /// API (edsaMember expansion), so this is the key comparison.
    /// </summary>
    private async Task MeasureGroupsAsync()
    {
        var sw = Stopwatch.StartNew();
        var groups = new List<Group>();
        try
        {
            var page = await _graph.Groups.GetAsync(rc =>
            {
                rc.QueryParameters.Select = new[] { "id", "displayName", "groupTypes", "securityEnabled", "mailEnabled" };
                rc.QueryParameters.Top = _settings.PageSize;
            });

            var iterator = PageIterator<Group, GroupCollectionResponse>
                .CreatePageIterator(_graph, page!, g =>
                {
                    groups.Add(g);
                    return _settings.MaxObjectsPerType == 0 || groups.Count < _settings.MaxObjectsPerType;
                });
            await iterator.IterateAsync();

            sw.Stop();
            Report("Groups (objects only)", sw, groups.Count);
        }
        catch (Exception ex)
        {
            sw.Stop();
            ReportError("Groups", sw, ex);
            return;
        }

        if (!_settings.IncludeGroupMembers)
            return;

        await MeasureGroupsExpandMembersAsync();

        await MeasureGroupMembersAsync(groups);
    }

    /// <summary>
    /// Times full per-group member retrieval - the direct comparison to the AR edsaMember
    /// cost. Runs up to <see cref="AppSettings.MemberFetchConcurrency"/> group fetches in
    /// parallel to reduce the impact of per-request latency (1 = strictly sequential).
    /// </summary>
    private async Task MeasureGroupMembersAsync(IReadOnlyList<Group> groups)
    {
        var concurrency = Math.Max(1, _settings.MemberFetchConcurrency);
        Console.WriteLine(
            $"  Retrieving members for {groups.Count:N0} groups " +
            $"(concurrency: {concurrency}, this can take a while)...");

        var memberSw = Stopwatch.StartNew();
        long totalMembers = 0;
        var groupsWithGuests = 0;
        var emptyGroups = 0;
        var processed = 0;
        try
        {
            using var throttle = new SemaphoreSlim(concurrency);
            var tasks = new List<Task>(groups.Count);

            foreach (var g in groups)
            {
                if (g.Id is null)
                    continue;

                await throttle.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var (groupMemberCount, hasGuest) = await FetchGroupMembersAsync(g.Id);

                        Interlocked.Add(ref totalMembers, groupMemberCount);
                        if (groupMemberCount == 0) Interlocked.Increment(ref emptyGroups);
                        if (hasGuest) Interlocked.Increment(ref groupsWithGuests);

                        var done = Interlocked.Increment(ref processed);
                        if (done % 50 == 0)
                        {
                            Console.WriteLine(
                                $"    ...{done,6:N0}/{groups.Count:N0} groups  " +
                                $"{memberSw.ElapsedMilliseconds,10:N0} ms  " +
                                $"{Interlocked.Read(ref totalMembers),9:N0} members so far");
                        }
                    }
                    finally
                    {
                        throttle.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            memberSw.Stop();
            Report($"Group members (all {groups.Count} groups)", memberSw, (int)Interlocked.Read(ref totalMembers));
            Console.WriteLine($"    Empty groups: {emptyGroups}, Guest-containing groups: {groupsWithGuests}");
        }
        catch (Exception ex)
        {
            memberSw.Stop();
            ReportError("Group members", memberSw, ex);
        }
    }

    /// <summary>
    /// Retrieves all members of a single group (paging through every page) and returns the
    /// total member count and whether any member is a Guest user.
    /// </summary>
    private async Task<(int Count, bool HasGuest)> FetchGroupMembersAsync(string groupId)
    {
        var members = await _graph.Groups[groupId].Members.GetAsync(rc =>
        {
            rc.QueryParameters.Select = new[] { "id", "userType" };
            rc.QueryParameters.Top = _settings.PageSize;
        });

        var groupMemberCount = 0;
        var hasGuest = false;
        if (members?.Value is not null)
        {
            var iterator = PageIterator<DirectoryObject, DirectoryObjectCollectionResponse>
                .CreatePageIterator(_graph, members, m =>
                {
                    groupMemberCount++;
                    if (m is User u && string.Equals(u.UserType, "Guest", StringComparison.OrdinalIgnoreCase))
                        hasGuest = true;
                    return true;
                });
            await iterator.IterateAsync();
        }

        return (groupMemberCount, hasGuest);
    }

    /// <summary>
    /// Times retrieving all groups with their members in a single paged collection using
    /// GET /groups?$expand=members. This collapses the N+1 per-group calls into a handful of
    /// page requests. NOTE: Graph caps $expand=members at 20 members per group, so groups with
    /// more than 20 members are truncated here - this measures the single-collection retrieval
    /// cost, not a complete membership fetch for large groups.
    /// </summary>
    private async Task MeasureGroupsExpandMembersAsync()
    {
        Console.WriteLine("  Retrieving groups with $expand=members (single collection, max 20 members/group)...");
        var sw = Stopwatch.StartNew();
        var groupCount = 0;
        long totalMembers = 0;
        var emptyGroups = 0;
        var truncatedGroups = 0;
        try
        {
            var page = await _graph.Groups.GetAsync(rc =>
            {
                rc.QueryParameters.Select = new[] { "id", "displayName" };
                rc.QueryParameters.Expand = new[] { "members($select=id,userType)" };
                rc.QueryParameters.Top = _settings.PageSize;
            });

            var iterator = PageIterator<Group, GroupCollectionResponse>
                .CreatePageIterator(_graph, page!, g =>
                {
                    groupCount++;
                    var members = g.Members?.Count ?? 0;
                    totalMembers += members;
                    if (members == 0) emptyGroups++;
                    if (members == 20) truncatedGroups++;

                    if (groupCount % 200 == 0)
                    {
                        Console.WriteLine(
                            $"    ...{groupCount,6:N0} groups  " +
                            $"{sw.ElapsedMilliseconds,10:N0} ms  {totalMembers,9:N0} members so far");
                    }

                    return _settings.MaxObjectsPerType == 0 || groupCount < _settings.MaxObjectsPerType;
                });
            await iterator.IterateAsync();

            sw.Stop();
            Report($"Groups + members ($expand, all {groupCount})", sw, (int)totalMembers);
            Console.WriteLine(
                $"    Empty groups: {emptyGroups}, Groups at 20-member cap (possibly truncated): {truncatedGroups}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            ReportError("Groups + members ($expand)", sw, ex);
        }
    }

    private static void Report(string label, Stopwatch sw, int count) =>
        Console.WriteLine($"  {label,-32} {sw.ElapsedMilliseconds,10:N0} ms  for {count,7:N0} objects");

    private static void ReportError(string label, Stopwatch sw, Exception ex)
    {
        var messages = new List<string>();
        for (var cur = ex; cur is not null; cur = cur.InnerException)
            messages.Add($"{cur.GetType().Name}: {cur.Message}");

        var detail = string.Join(" | ", messages.Where(m => !m.EndsWith(": ")));
        Console.WriteLine($"  {label,-32} {sw.ElapsedMilliseconds,10:N0} ms  FAILED: {detail}");
    }
}
