using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

public class ActiveRolesService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<ActiveRolesConfig> _configMonitor;
    private readonly ILogger<ActiveRolesService> _logger;

    public ActiveRolesService(IHttpClientFactory httpClientFactory, IOptionsMonitor<ActiveRolesConfig> configMonitor, ILogger<ActiveRolesService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configMonitor = configMonitor;
        _logger = logger;
    }

    private string BaseUrl => _configMonitor.CurrentValue.ApiBaseUrl.TrimEnd('/');

    private HttpClient CreateClient(string token)
    {
        var client = _httpClientFactory.CreateClient("ActiveRolesApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>
    /// Lightweight preflight that verifies the Active Roles REST API is reachable before running
    /// the full (expensive) superset collection. Issues a single short-timeout GET against the API
    /// root. Any HTTP response (including 401/404) means the service is up and answering; only a
    /// transport-level failure (connection refused, DNS, TLS, timeout) is treated as unreachable.
    /// Returns null on success, or a short human-readable reason on failure.
    /// </summary>
    public async Task<string?> TestConnectionAsync(string token, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var client = CreateClient(token);
            using var response = await client
                .GetAsync(BaseUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            // Reaching here means the server responded at the HTTP layer -> it is up.
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Active Roles connectivity preflight timed out against {BaseUrl}.", BaseUrl);
            return $"Active Roles REST API did not respond within the timeout ({BaseUrl}).";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Active Roles connectivity preflight failed against {BaseUrl}.", BaseUrl);
            return $"Active Roles REST API is unreachable ({BaseUrl}): {ex.Message}";
        }
    }


    public async Task<DashboardSummary> GetDashboardSummaryAsync(string token, KpiSettings? kpiSettings = null, UserSettings? userSettings = null, bool skipOverviewTotals = false, OverviewTotalsCache? cachedTotals = null)
    {
        var settings = kpiSettings ?? new KpiSettings();
        var config = _configMonitor.CurrentValue;
        var summary = new DashboardSummary();
        summary.EntraLargeGroupMemberThreshold = config.EntraLargeGroupMemberThreshold;

        var tasks = new List<(string Key, Task Task)>();

        // Fetch AD User Accounts first - needed by derived KPIs like NoManagerUser, EnabledUsers, DisabledUsers, ExpiringUsers
        var needsADUserAccounts = settings.IsKpiEnabled("Overview", "ADUserAccounts")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "NoManagerUser") || settings.IsKpiEnabled("ADGovernance", "NoManagerUser")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "NeverLoggedIn") || settings.IsKpiEnabled("ADGovernance", "NeverLoggedIn")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "EnabledUsers")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "DisabledUsers")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "ExpiringUsers")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "ExpiredUsers") || settings.IsKpiEnabled("ADGovernance", "ExpiredUsers")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "PasswordNeverExpires")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "MustChangePassword")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "PasswordNotRequired")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "SmartCardRequired")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "CannotChangePassword")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "NoKerberosPreauth")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "UserReversibleEncryption")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "SensitiveCannotDelegate")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "TrustedForDelegation")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "UseDesEncryption")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "DeprovisionedUsers")
            || settings.IsKpiEnabled("NHIs", "SpnUserAccounts")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "StaleUsers")
            || settings.IsKpiEnabled("ADUserAccountsCategory", "ReversibleEncryption") || settings.IsKpiEnabled("ADGovernance", "ReversibleEncryption")
            || settings.IsKpiEnabled("PrivilegedUsers", "AdminCount");

        if (needsADUserAccounts)
        {
            if (skipOverviewTotals && cachedTotals != null)
                summary.ADUserAccounts = cachedTotals.ADUserAccounts;
            else if (!skipOverviewTotals)
                summary.ADUserAccounts = await GetADUserAccountsCountAsync(token);
        }

        // Fetch AD Groups first - needed by derived KPIs
        var needsADGroups = settings.IsKpiEnabled("Overview", "ADGroups")
            || settings.IsKpiEnabled("ADGroupsCategory", "SecurityGroups")
            || settings.IsKpiEnabled("ADGroupsCategory", "DistributionGroups")
            || settings.IsKpiEnabled("ADGroupsCategory", "GlobalGroups")
            || settings.IsKpiEnabled("ADGroupsCategory", "UniversalGroups")
            || settings.IsKpiEnabled("ADGroupsCategory", "MailEnabledSecurityGroups")
            || settings.IsKpiEnabled("ADGroupsCategory", "CircularGroupNesting")
            || settings.IsKpiEnabled("ARConfiguration", "GroupFamilies")
            || settings.IsKpiEnabled("ARConfiguration", "DynamicGroups");

        if (needsADGroups)
        {
            if (skipOverviewTotals && cachedTotals != null)
                summary.ADGroups = cachedTotals.ADGroups;
            else if (!skipOverviewTotals)
                summary.ADGroups = await GetADGroupsAsync(token);
        }

        // Fetch Computers first - needed by derived KPIs like DomainControllers
        var needsComputers = settings.IsKpiEnabled("Overview", "Computers")
            || settings.IsKpiEnabled("Infrastructure", "DomainControllers")
            || settings.IsCategoryEnabled("ComputersCategory");

        if (needsComputers)
        {
            if (skipOverviewTotals && cachedTotals != null)
                summary.Computers = cachedTotals.Computers;
            else if (!skipOverviewTotals)
                summary.Computers = await GetComputersAsync(token);
        }

        // Restore Entra totals from cache when skipping overview loads
        if (skipOverviewTotals && cachedTotals != null)
        {
            summary.EntraTotals = cachedTotals.EntraTotals;
        }
        else if (!skipOverviewTotals)
        {
            summary.EntraTotals = await GetEntraTotalsAsync(token);
        }

        // Derive DomainControllers from Computers data (SERVER_TRUST_ACCOUNT = 0x2000)
        if (settings.IsKpiEnabled("Infrastructure", "DomainControllers") && summary.Computers.Error == null)
        {
            var dcs = summary.Computers.Items
                .Where(i =>
                {
                    var uac = GetAttr(i, "userAccountControl");
                    return int.TryParse(uac, out var v) && (v & 0x2000) != 0;
                })
                .ToList();
            summary.DomainControllers = new DomainControllersSummary
            {
                TotalCount = dcs.Count,
                Items = dcs.Select(i => new DomainControllerInfo
                {
                    Name = GetAttr(i, "name"),
                    Domain = GetAttr(i, "edsaDomainNetbiosName"),
                    Dn = GetAttr(i, "distinguishedName"),
                    SiteName = GetAttr(i, "msDS-SiteName")
                }).ToList()
            };
        }

        // Derive StaleComputers (inactive computer accounts) from Computers data.
        // Stale = enabled computer account whose last logon (lastLogonTimestamp) is older
        // than the inactivity threshold. Domain controllers are excluded. Computers that
        // never authenticated (no lastLogonTimestamp) are ignored here.
        if (settings.IsKpiEnabled("Infrastructure", "DomainControllers") && summary.Computers.Error == null)
        {
            var staleThresholdDays = _configMonitor.CurrentValue.StaleAccountThresholdDays;
            if (staleThresholdDays <= 0) staleThresholdDays = 90;
            var staleCutoff = DateTime.UtcNow.AddDays(-staleThresholdDays);

            bool IsComputerStale(string raw)
            {
                if (string.IsNullOrEmpty(raw) || !long.TryParse(raw, out var val) || val <= 0)
                    return false;
                try { return DateTime.FromFileTimeUtc(val) < staleCutoff; }
                catch (ArgumentOutOfRangeException) { return false; }
            }

            var staleComputers = summary.Computers.Items
                .Where(i =>
                {
                    var uac = GetAttr(i, "userAccountControl");
                    var isDc = int.TryParse(uac, out var v) && (v & 0x2000) != 0;
                    return !isDc
                        && !IsAccountDisabled(uac)
                        && IsComputerStale(GetAttr(i, "lastLogonTimestamp"));
                })
                .ToList();

            summary.StaleComputers = new ComputerBreakdownSummary
            {
                TotalCount = staleComputers.Count,
                Items = staleComputers.Select(i => new ComputerBreakdownInfo
                {
                    Name = GetAttr(i, "name"),
                    Domain = GetAttr(i, "edsaDomainNetbiosName"),
                    Dn = GetAttr(i, "distinguishedName")
                }).ToList()
            };
        }

        // Derive Computers category breakdown from Computers data
        if (settings.IsCategoryEnabled("ComputersCategory") && summary.Computers.Error == null)
        {
            var clientsList = new List<ComputerBreakdownInfo>();
            var serversList = new List<ComputerBreakdownInfo>();
            var srv2008R2List = new List<ComputerBreakdownInfo>();
            var srv2012R2List = new List<ComputerBreakdownInfo>();
            var srv2016List = new List<ComputerBreakdownInfo>();
            var srv2019List = new List<ComputerBreakdownInfo>();
            var srv2022List = new List<ComputerBreakdownInfo>();
            var srv2025List = new List<ComputerBreakdownInfo>();
            var srvOtherList = new List<ComputerBreakdownInfo>();
            var win7List = new List<ComputerBreakdownInfo>();
            var win81List = new List<ComputerBreakdownInfo>();
            var win10_22h2List = new List<ComputerBreakdownInfo>();
            var win11_22h2List = new List<ComputerBreakdownInfo>();
            var win11_23h2List = new List<ComputerBreakdownInfo>();
            var win11EntList = new List<ComputerBreakdownInfo>();
            var win11ProList = new List<ComputerBreakdownInfo>();
            var cliOtherList = new List<ComputerBreakdownInfo>();

            foreach (var item in summary.Computers.Items)
            {
                var os = GetAttr(item, "operatingSystem");
                var osVer = GetAttr(item, "operatingSystemVersion");
                var uac = GetAttr(item, "userAccountControl");
                bool isDC = int.TryParse(uac, out var uacVal) && (uacVal & 0x2000) != 0;
                bool isServer = os.Contains("server", StringComparison.OrdinalIgnoreCase);

                var info = new ComputerBreakdownInfo
                {
                    Name = GetAttr(item, "name"),
                    Domain = GetAttr(item, "edsaDomainNetbiosName"),
                    OperatingSystem = os,
                    OperatingSystemVersion = osVer,
                    FriendlyOSName = GetFriendlyOSName(os, osVer),
                    Dn = GetAttr(item, "distinguishedName")
                };

                if (isServer)
                {
                    if (!isDC) serversList.Add(info);
                    switch (osVer)
                    {
                        case "6.1": srv2008R2List.Add(info); break;
                        case "6.3": srv2012R2List.Add(info); break;
                        case "10.0 (14393)": srv2016List.Add(info); break;
                        case "10.0 (17763)": srv2019List.Add(info); break;
                        case "10.0 (20348)": srv2022List.Add(info); break;
                        case "10.0 (26100)": srv2025List.Add(info); break;
                        default: srvOtherList.Add(info); break;
                    }
                }
                else
                {
                    clientsList.Add(info);
                    switch (osVer)
                    {
                        case "6.1": win7List.Add(info); break;
                        case "6.3": win81List.Add(info); break;
                        case "10.0 (19045)": win10_22h2List.Add(info); break;
                        case "10.0 (22621)": win11_22h2List.Add(info); break;
                        case "10.0 (22631)": win11_23h2List.Add(info); break;
                        case "10.0 (26200)": win11EntList.Add(info); break;
                        case "10.0 (26100)": win11ProList.Add(info); break;
                        default: cliOtherList.Add(info); break;
                    }
                }
            }

            summary.ComputerClients = new ComputerBreakdownSummary { TotalCount = clientsList.Count, Items = clientsList };
            summary.ComputerServers = new ComputerBreakdownSummary { TotalCount = serversList.Count, Items = serversList };
            summary.WinServer2008R2 = new ComputerBreakdownSummary { TotalCount = srv2008R2List.Count, Items = srv2008R2List };
            summary.WinServer2012R2 = new ComputerBreakdownSummary { TotalCount = srv2012R2List.Count, Items = srv2012R2List };
            summary.WinServer2016 = new ComputerBreakdownSummary { TotalCount = srv2016List.Count, Items = srv2016List };
            summary.WinServer2019 = new ComputerBreakdownSummary { TotalCount = srv2019List.Count, Items = srv2019List };
            summary.WinServer2022 = new ComputerBreakdownSummary { TotalCount = srv2022List.Count, Items = srv2022List };
            summary.WinServer2025 = new ComputerBreakdownSummary { TotalCount = srv2025List.Count, Items = srv2025List };
            summary.ServerOther = new ComputerBreakdownSummary { TotalCount = srvOtherList.Count, Items = srvOtherList };
            summary.Win7 = new ComputerBreakdownSummary { TotalCount = win7List.Count, Items = win7List };
            summary.Win81 = new ComputerBreakdownSummary { TotalCount = win81List.Count, Items = win81List };
            summary.Win10_22H2 = new ComputerBreakdownSummary { TotalCount = win10_22h2List.Count, Items = win10_22h2List };
            summary.Win11_22H2 = new ComputerBreakdownSummary { TotalCount = win11_22h2List.Count, Items = win11_22h2List };
            summary.Win11_23H2 = new ComputerBreakdownSummary { TotalCount = win11_23h2List.Count, Items = win11_23h2List };
            summary.Win11Enterprise = new ComputerBreakdownSummary { TotalCount = win11EntList.Count, Items = win11EntList };
            summary.Win11Pro = new ComputerBreakdownSummary { TotalCount = win11ProList.Count, Items = win11ProList };
            summary.ClientsOther = new ComputerBreakdownSummary { TotalCount = cliOtherList.Count, Items = cliOtherList };

            // Derive UnconstrainedComputers (TRUSTED_FOR_DELEGATION = 0x80000) from Computers data.
            if (settings.IsKpiEnabled("ComputersCategory", "UnconstrainedComputers"))
            {
                const int trustedForDelegationFlag = 0x80000;
                var unconstrained = summary.Computers.Items
                    .Where(i => int.TryParse(GetAttr(i, "userAccountControl"), out var uac) && (uac & trustedForDelegationFlag) != 0)
                    .Select(i =>
                    {
                        var os = GetAttr(i, "operatingSystem");
                        var osVer = GetAttr(i, "operatingSystemVersion");
                        return new ComputerBreakdownInfo
                        {
                            Name = GetAttr(i, "name"),
                            Domain = GetAttr(i, "edsaDomainNetbiosName"),
                            OperatingSystem = os,
                            OperatingSystemVersion = osVer,
                            FriendlyOSName = GetFriendlyOSName(os, osVer),
                            Dn = GetAttr(i, "distinguishedName")
                        };
                    })
                    .ToList();
                summary.UnconstrainedComputers = new ComputerBreakdownSummary { TotalCount = unconstrained.Count, Items = unconstrained };
            }
        }

        // Derive NoManagerUser from AD User Accounts data
        if ((settings.IsKpiEnabled("ADUserAccountsCategory", "NoManagerUser") || settings.IsKpiEnabled("ADGovernance", "NoManagerUser")) && summary.ADUserAccounts.Error == null)
        {
            var noManagerUsers = summary.ADUserAccounts.Items
                .Where(i => string.IsNullOrEmpty(GetAttr(i, "manager")))
                .ToList();
            summary.NoManagerUser = new GovernanceKpiSummary
            {
                TotalCount = noManagerUsers.Count,
                Items = noManagerUsers.Select(i => new GovernanceKpiInfo
                {
                    Name = GetAttr(i, "name"),
                    Dn = GetAttr(i, "distinguishedName"),
                    Guid = GetAttr(i, "objectGuid")
                }).ToList()
            };
        }

        // Derive NeverLoggedIn from AD User Accounts data
        if ((settings.IsKpiEnabled("ADUserAccountsCategory", "NeverLoggedIn") || settings.IsKpiEnabled("ADGovernance", "NeverLoggedIn")) && summary.ADUserAccounts.Error == null)
        {
            var neverLoggedIn = summary.ADUserAccounts.Items
                .Where(i => string.IsNullOrEmpty(GetAttr(i, "lastLogonTimestamp")))
                .ToList();
            summary.NeverLoggedIn = new ADUserAccountDetailSummary
            {
                TotalCount = neverLoggedIn.Count,
                Items = neverLoggedIn.Select(i => new ADUserAccountDetailInfo
                {
                    Name = GetAttr(i, "name"),
                    Domain = GetAttr(i, "edsaDomainNetbiosName"),
                    Dn = GetAttr(i, "distinguishedName"),
                    Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                }).ToList()
            };
        }

        // Derive StaleUsers (inactive accounts) from AD User Accounts data.
        // Stale = enabled account whose last interactive logon (lastLogonTimestamp) is
        // older than the inactivity threshold. Accounts that never logged on are excluded
        // here (they are surfaced by the NeverLoggedIn KPI).
        if (settings.IsKpiEnabled("ADUserAccountsCategory", "StaleUsers") && summary.ADUserAccounts.Error == null)
        {
            var staleThresholdDays = _configMonitor.CurrentValue.StaleAccountThresholdDays;
            if (staleThresholdDays <= 0) staleThresholdDays = 90;
            var staleCutoff = DateTime.UtcNow.AddDays(-staleThresholdDays);

            bool IsStale(string raw, out DateTime lastLogon)
            {
                lastLogon = default;
                if (string.IsNullOrEmpty(raw) || !long.TryParse(raw, out var val) || val <= 0)
                    return false;
                try
                {
                    lastLogon = DateTime.FromFileTimeUtc(val);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
                return lastLogon < staleCutoff;
            }

            var staleUsers = summary.ADUserAccounts.Items
                .Where(i => !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                            && IsStale(GetAttr(i, "lastLogonTimestamp"), out _))
                .ToList();

            summary.StaleUsers = new ADUserAccountDetailSummary
            {
                TotalCount = staleUsers.Count,
                Items = staleUsers.Select(i =>
                {
                    IsStale(GetAttr(i, "lastLogonTimestamp"), out var lastLogon);
                    return new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Description = $"Last logon {lastLogon:yyyy-MM-dd}",
                        Enabled = true
                    };
                }).ToList()
            };
        }

        // Derive AdminCount from AD User Accounts data
        if (settings.IsKpiEnabled("PrivilegedUsers", "AdminCount") && summary.ADUserAccounts.Error == null)
        {
            var adminCountUsers = summary.ADUserAccounts.Items
                .Where(i => GetAttr(i, "adminCount") == "1")
                .ToList();
            summary.AdminCount = new ADUserAccountDetailSummary
            {
                TotalCount = adminCountUsers.Count,
                Items = adminCountUsers.Select(i => new ADUserAccountDetailInfo
                {
                    Name = GetAttr(i, "name"),
                    Domain = GetAttr(i, "edsaDomainNetbiosName"),
                    Dn = GetAttr(i, "distinguishedName"),
                    Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                }).ToList()
            };
        }

        // Derive Enabled/Disabled Users from AD User Accounts data
        if (summary.ADUserAccounts.Error == null)
        {
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "EnabledUsers"))
            {
                var enabledUsers = summary.ADUserAccounts.Items
                    .Where(i => !IsAccountDisabled(GetAttr(i, "userAccountControl")))
                    .ToList();
                summary.EnabledUsers = new ADUserAccountDetailSummary
                {
                    TotalCount = enabledUsers.Count,
                    Items = enabledUsers.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = true
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "DisabledUsers"))
            {
                var disabledUsers = summary.ADUserAccounts.Items
                    .Where(i => IsAccountDisabled(GetAttr(i, "userAccountControl")))
                    .ToList();
                summary.DisabledUsers = new ADUserAccountDetailSummary
                {
                    TotalCount = disabledUsers.Count,
                    Items = disabledUsers.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = false
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "ExpiredUsers") || settings.IsKpiEnabled("ADGovernance", "ExpiredUsers"))
            {
                const long neverExpires = 9223372036854775807;
                var now = DateTime.UtcNow;
                var expiredUsers = summary.ADUserAccounts.Items
                    .Where(i =>
                    {
                        var raw = GetAttr(i, "accountExpires");
                        if (!long.TryParse(raw, out var val) || val == 0 || val == neverExpires)
                            return false;
                        var expiryDate = DateTime.FromFileTimeUtc(val);
                        return expiryDate <= now;
                    })
                    .ToList();
                summary.ExpiredUsers = new ADUserAccountDetailSummary
                {
                    TotalCount = expiredUsers.Count,
                    Items = expiredUsers.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "ReversibleEncryption") || settings.IsKpiEnabled("ADGovernance", "ReversibleEncryption"))
            {
                // userAccountControl bit 0x0080 (128) = ENCRYPTED_TEXT_PWD_ALLOWED (reversible encryption).
                const int reversibleEncryptionFlag = 0x0080;
                var reversibleUsers = summary.ADUserAccounts.Items
                    .Where(i => int.TryParse(GetAttr(i, "userAccountControl"), out var uac) && (uac & reversibleEncryptionFlag) != 0)
                    .ToList();
                summary.ReversibleEncryption = new GovernanceKpiSummary
                {
                    TotalCount = reversibleUsers.Count,
                    Items = reversibleUsers.Select(i => new GovernanceKpiInfo
                    {
                        Name = GetAttr(i, "name"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Guid = GetAttr(i, "objectGuid")
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "ExpiringUsers"))
            {
                const long neverExpires = 9223372036854775807;
                var now = DateTime.UtcNow;
                var expiringUsers = summary.ADUserAccounts.Items
                    .Where(i =>
                    {
                        var raw = GetAttr(i, "accountExpires");
                        if (!long.TryParse(raw, out var val) || val == 0 || val == neverExpires)
                            return false;
                        var expiryDate = DateTime.FromFileTimeUtc(val);
                        return expiryDate > now;
                    })
                    .ToList();
                summary.ExpiringUsers = new ExpiringUsersSummary
                {
                    TotalCount = expiringUsers.Count,
                    Items = expiringUsers.Select(i =>
                    {
                        var raw = GetAttr(i, "accountExpires");
                        long.TryParse(raw, out var ticks);
                        var expiryDate = DateTime.FromFileTimeUtc(ticks);
                        var daysUntil = (expiryDate.Date - DateTime.UtcNow.Date).Days;
                        return new ExpiringUserInfo
                        {
                            Name = GetAttr(i, "name"),
                            Domain = GetAttr(i, "edsaDomainNetbiosName"),
                            Dn = GetAttr(i, "distinguishedName"),
                            ExpiryDate = expiryDate,
                            DaysUntilExpiry = daysUntil
                        };
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "PasswordNeverExpires"))
            {
                var passwordNeverExpires = summary.ADUserAccounts.Items
                    .Where(i =>
                    {
                        var raw = GetAttr(i, "accountExpires");
                        return long.TryParse(raw, out var val) && val == 0;
                    })
                    .ToList();
                summary.PasswordNeverExpires = new ADUserAccountDetailSummary
                {
                    TotalCount = passwordNeverExpires.Count,
                    Items = passwordNeverExpires.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "DeprovisionedUsers"))
            {
                var deprovisionedUsers = summary.ADUserAccounts.Items
                    .Where(i => GetAttr(i, "edsvaDeprovisionStatus") == "1")
                    .ToList();
                summary.DeprovisionedUsers = new ADUserAccountDetailSummary
                {
                    TotalCount = deprovisionedUsers.Count,
                    Items = deprovisionedUsers.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl")),
                        Description = GetAttr(i, "description")
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "MustChangePassword"))
            {
                // pwdLastSet == 0 means the user must change their password at next logon.
                var mustChange = summary.ADUserAccounts.Items
                    .Where(i => long.TryParse(GetAttr(i, "pwdLastSet"), out var val) && val == 0)
                    .ToList();
                summary.MustChangePassword = new ADUserAccountDetailSummary
                {
                    TotalCount = mustChange.Count,
                    Items = mustChange.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "PasswordNotRequired"))
            {
                // userAccountControl bit 0x0020 (32) = PASSWD_NOTREQD.
                const int passwordNotRequiredFlag = 0x0020;
                var passwordNotRequired = summary.ADUserAccounts.Items
                    .Where(i => int.TryParse(GetAttr(i, "userAccountControl"), out var uac) && (uac & passwordNotRequiredFlag) != 0)
                    .ToList();
                summary.PasswordNotRequired = new ADUserAccountDetailSummary
                {
                    TotalCount = passwordNotRequired.Count,
                    Items = passwordNotRequired.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "SmartCardRequired"))
            {
                // userAccountControl bit 0x40000 (262144) = SMARTCARD_REQUIRED.
                const int smartCardRequiredFlag = 0x40000;
                var smartCardRequired = summary.ADUserAccounts.Items
                    .Where(i => int.TryParse(GetAttr(i, "userAccountControl"), out var uac) && (uac & smartCardRequiredFlag) != 0)
                    .ToList();
                summary.SmartCardRequired = new ADUserAccountDetailSummary
                {
                    TotalCount = smartCardRequired.Count,
                    Items = smartCardRequired.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "CannotChangePassword"))
            {
                // userAccountControl bit 0x0040 (64) = PASSWD_CANT_CHANGE.
                const int cannotChangePasswordFlag = 0x0040;
                var cannotChangePassword = summary.ADUserAccounts.Items
                    .Where(i => int.TryParse(GetAttr(i, "userAccountControl"), out var uac) && (uac & cannotChangePasswordFlag) != 0)
                    .ToList();
                summary.CannotChangePassword = new ADUserAccountDetailSummary
                {
                    TotalCount = cannotChangePassword.Count,
                    Items = cannotChangePassword.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "UserReversibleEncryption"))
            {
                // userAccountControl bit 0x0080 (128) = ENCRYPTED_TEXT_PWD_ALLOWED (reversible encryption).
                const int reversibleEncryptionFlag = 0x0080;
                var reversible = summary.ADUserAccounts.Items
                    .Where(i => int.TryParse(GetAttr(i, "userAccountControl"), out var uac) && (uac & reversibleEncryptionFlag) != 0)
                    .ToList();
                summary.UserReversibleEncryption = new ADUserAccountDetailSummary
                {
                    TotalCount = reversible.Count,
                    Items = reversible.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "TrustedForDelegation"))
            {
                // userAccountControl bit 0x80000 (524288) = TRUSTED_FOR_DELEGATION.
                const int trustedForDelegationFlag = 0x80000;
                var trusted = summary.ADUserAccounts.Items
                    .Where(i => int.TryParse(GetAttr(i, "userAccountControl"), out var uac) && (uac & trustedForDelegationFlag) != 0)
                    .ToList();
                summary.TrustedForDelegation = new ADUserAccountDetailSummary
                {
                    TotalCount = trusted.Count,
                    Items = trusted.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("NHIs", "SpnUserAccounts"))
            {
                // User accounts that expose a servicePrincipalName are Kerberoastable service accounts.
                var spnUsers = summary.ADUserAccounts.Items
                    .Where(i => !string.IsNullOrEmpty(GetAttr(i, "servicePrincipalName")))
                    .ToList();
                summary.SpnUserAccounts = new ADUserAccountDetailSummary
                {
                    TotalCount = spnUsers.Count,
                    Items = spnUsers.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "SensitiveCannotDelegate"))
            {
                // userAccountControl bit 0x100000 (1048576) = NOT_DELEGATED (account is sensitive).
                const int sensitiveCannotDelegateFlag = 0x100000;
                var sensitive = summary.ADUserAccounts.Items
                    .Where(i => int.TryParse(GetAttr(i, "userAccountControl"), out var uac) && (uac & sensitiveCannotDelegateFlag) != 0)
                    .ToList();
                summary.SensitiveCannotDelegate = new ADUserAccountDetailSummary
                {
                    TotalCount = sensitive.Count,
                    Items = sensitive.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "UseDesEncryption"))
            {
                // userAccountControl bit 0x200000 (2097152) = USE_DES_KEY_ONLY.
                const int useDesEncryptionFlag = 0x200000;
                var useDes = summary.ADUserAccounts.Items
                    .Where(i => int.TryParse(GetAttr(i, "userAccountControl"), out var uac) && (uac & useDesEncryptionFlag) != 0)
                    .ToList();
                summary.UseDesEncryption = new ADUserAccountDetailSummary
                {
                    TotalCount = useDes.Count,
                    Items = useDes.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADUserAccountsCategory", "NoKerberosPreauth"))
            {
                // userAccountControl bit 0x400000 (4194304) = DONT_REQ_PREAUTH.
                const int noKerberosPreauthFlag = 0x400000;
                var noPreauth = summary.ADUserAccounts.Items
                    .Where(i => int.TryParse(GetAttr(i, "userAccountControl"), out var uac) && (uac & noKerberosPreauthFlag) != 0)
                    .ToList();
                summary.NoKerberosPreauth = new ADUserAccountDetailSummary
                {
                    TotalCount = noPreauth.Count,
                    Items = noPreauth.Select(i => new ADUserAccountDetailInfo
                    {
                        Name = GetAttr(i, "name"),
                        Domain = GetAttr(i, "edsaDomainNetbiosName"),
                        Dn = GetAttr(i, "distinguishedName"),
                        Enabled = !IsAccountDisabled(GetAttr(i, "userAccountControl"))
                    }).ToList()
                };
            }
        }

        // Derive AD Group KPIs from AD Groups data (exclude Group Families)
        if (summary.ADGroups.Error == null && summary.ADGroups.Items.Any())
        {
            var nonGroupFamilyItems = summary.ADGroups.Items
                .Where(i => !string.Equals(GetAttr(i, "edsvaGFIsGroupFamily"), "TRUE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (settings.IsKpiEnabled("ADGroupsCategory", "DistributionGroups"))
            {
                var items = nonGroupFamilyItems
                    .Where(i => { var gt = GetAttr(i, "groupType"); return int.TryParse(gt, out var v) && (v & unchecked((int)0x80000000)) == 0; })
                    .ToList();
                summary.DistributionGroups = new ADGroupDetailSummary
                {
                    TotalCount = items.Count,
                    Items = items.Select(i => ToGroupDetail(i)).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADGroupsCategory", "DomainLocalGroups"))
            {
                var items = nonGroupFamilyItems
                    .Where(i => { var gt = GetAttr(i, "groupType"); return int.TryParse(gt, out var v) && (v & 0x4) != 0; })
                    .ToList();
                summary.DomainLocalGroups = new ADGroupDetailSummary
                {
                    TotalCount = items.Count,
                    Items = items.Select(i => ToGroupDetail(i)).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADGroupsCategory", "GlobalGroups"))
            {
                var items = nonGroupFamilyItems
                    .Where(i => { var gt = GetAttr(i, "groupType"); return int.TryParse(gt, out var v) && (v & 0x2) != 0; })
                    .ToList();
                summary.GlobalGroups = new ADGroupDetailSummary
                {
                    TotalCount = items.Count,
                    Items = items.Select(i => ToGroupDetail(i)).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADGroupsCategory", "MailEnabledSecurityGroups"))
            {
                var items = nonGroupFamilyItems
                    .Where(i =>
                    {
                        var gt = GetAttr(i, "groupType");
                        if (!int.TryParse(gt, out var v)) return false;
                        bool isSecurity = (v & unchecked((int)0x80000000)) != 0;
                        bool hasMail = !string.IsNullOrEmpty(GetAttr(i, "mail"));
                        return isSecurity && hasMail;
                    })
                    .ToList();
                summary.MailEnabledSecurityGroups = new ADGroupDetailSummary
                {
                    TotalCount = items.Count,
                    Items = items.Select(i => ToGroupDetail(i)).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADGroupsCategory", "SecurityGroups"))
            {
                var items = nonGroupFamilyItems
                    .Where(i => { var gt = GetAttr(i, "groupType"); return int.TryParse(gt, out var v) && v < 0; })
                    .ToList();
                summary.SecurityGroups = new ADGroupDetailSummary
                {
                    TotalCount = items.Count,
                    Items = items.Select(i => ToGroupDetail(i)).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADGroupsCategory", "UniversalGroups"))
            {
                var items = nonGroupFamilyItems
                    .Where(i => { var gt = GetAttr(i, "groupType"); return int.TryParse(gt, out var v) && (v & 0x8) != 0; })
                    .ToList();
                summary.UniversalGroups = new ADGroupDetailSummary
                {
                    TotalCount = items.Count,
                    Items = items.Select(i => ToGroupDetail(i)).ToList()
                };
            }
            if (settings.IsKpiEnabled("ADGroupsCategory", "CircularGroupNesting"))
            {
                summary.CircularGroupNesting = DetectCircularGroupNesting(summary.ADGroups.Items);
            }
            if (settings.IsKpiEnabled("ARConfiguration", "GroupFamilies"))
            {
                var items = summary.ADGroups.Items
                    .Where(i => string.Equals(GetAttr(i, "edsvaGFIsGroupFamily"), "TRUE", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                summary.GroupFamilies = new GroupFamilySummary
                {
                    TotalCount = items.Count,
                    Items = items.Select(i => new GroupFamilyInfo
                    {
                        Name = GetAttr(i, "name"),
                        Dn = GetAttr(i, "distinguishedName")
                    }).ToList()
                };
            }
            if (settings.IsKpiEnabled("ARConfiguration", "DynamicGroups"))
            {
                var items = summary.ADGroups.Items
                    .Where(i => string.Equals(GetAttr(i, "edsaIsDynamicGroup"), "TRUE", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                summary.DynamicGroups = new DynamicGroupSummary
                {
                    TotalCount = items.Count,
                    Items = items.Select(i => new DynamicGroupInfo
                    {
                        Name = GetAttr(i, "name"),
                        Dn = GetAttr(i, "distinguishedName")
                    }).ToList()
                };
            }
        }

        if (settings.IsKpiEnabled("ARConfiguration", "ActiveRolesAdmins"))
        {
            var baseDn = ResolveValue(config.CustomActiveRolesAdminsBaseDn, config.DefaultActiveDirectoryDN);
            var filter = ResolveValue(config.CustomActiveRolesAdminsFilter, config.DefaultActiveRolesAdminsFilter);
            var t = GetActiveRolesAdminsAsync(token, baseDn, filter);
            tasks.Add(("ActiveRolesAdmins", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.ActiveRolesAdmins = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ARConfiguration", "Domains"))
        {
            var t = GetDomainsAsync(token);
            tasks.Add(("Domains", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.Domains = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ARConfiguration", "Servers"))
        {
            var t = GetServersAsync(token);
            tasks.Add(("Servers", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.Servers = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ARConfiguration", "ManagedUnits"))
        {
            var t = GetManagedUnitsAsync(token);
            tasks.Add(("ManagedUnits", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.ManagedUnits = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ARConfiguration", "Workflows"))
        {
            var t = GetWorkflowsAsync(token);
            tasks.Add(("Workflows", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.Workflows = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ARConfiguration", "VirtualAttributes"))
        {
            var t = GetVirtualAttributesAsync(token);
            tasks.Add(("VirtualAttributes", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.VirtualAttributes = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ARConfiguration", "ConfigDatabases"))
        {
            var t = GetConfigDatabasesAsync(token);
            tasks.Add(("ConfigDatabases", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.ConfigDatabases = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ARConfiguration", "HistoryDatabases"))
        {
            var t = GetHistoryDatabasesAsync(token);
            tasks.Add(("HistoryDatabases", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.HistoryDatabases = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ARConfiguration", "PolicyObjects"))
        {
            var t = GetPolicyObjectsAsync(token);
            tasks.Add(("PolicyObjects", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.PolicyObjects = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ARConfiguration", "AccessTemplates"))
        {
            var t = GetAccessTemplatesAsync(token);
            tasks.Add(("AccessTemplates", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.AccessTemplates = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ARConfiguration", "AccessTemplateLinks"))
        {
            var t = GetAccessTemplateLinksAsync(token);
            tasks.Add(("AccessTemplateLinks", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.AccessTemplateLinks = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ARConfiguration", "PolicyObjectLinks"))
        {
            var t = GetPolicyObjectLinksAsync(token);
            tasks.Add(("PolicyObjectLinks", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.PolicyObjectLinks = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("Licensing", "ManagedObjects"))
        {
            var t = GetManagedObjectsAsync(token);
            tasks.Add(("ManagedObjects", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.ManagedObjects = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ADGroupsCategory", "NoGroupOwner") || settings.IsKpiEnabled("ADGovernance", "NoGroupOwner"))
        {
            var baseDn = ResolveValue(config.CustomNoGroupOwnerBaseDn, config.DefaultActiveDirectoryDN);
            var filter = config.DefaultNoGroupOwnerFilter;
            var t = GetNoGroupOwnerAsync(token, baseDn, filter);
            tasks.Add(("NoGroupOwner", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.NoGroupOwner = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("NHIs", "NoManagerServiceAccount") || settings.IsKpiEnabled("ADGovernance", "NoManagerServiceAccount"))
        {
            var baseDn = ResolveValue(config.CustomNoManagerServiceAccountBaseDn, config.DefaultActiveDirectoryDN);
            var filter = ResolveValue(config.CustomNoManagerServiceAccountFilter, config.DefaultNoManagerServiceAccountFilter);
            var t = GetNoManagerServiceAccountAsync(token, baseDn, filter);
            tasks.Add(("NoManagerServiceAccount", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.NoManagerServiceAccount = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("NHIs", "ServiceAccounts"))
        {
            var baseDn = config.DefaultActiveDirectoryDN;
            var filter = config.DefaultServiceAccountsFilter;
            var t = GetServiceAccountsAsync(token, baseDn, filter);
            tasks.Add(("ServiceAccounts", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.ServiceAccounts = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("NHIs", "GmsaServiceAccounts"))
        {
            var baseDn = config.DefaultActiveDirectoryDN;
            var filter = config.DefaultGmsaServiceAccountsFilter;
            var t = GetGmsaServiceAccountsAsync(token, baseDn, filter);
            tasks.Add(("GmsaServiceAccounts", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.GmsaServiceAccounts = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("NHIs", "SmsaServiceAccounts"))
        {
            var baseDn = config.DefaultActiveDirectoryDN;
            var filter = config.DefaultSmsaServiceAccountsFilter;
            var t = GetSmsaServiceAccountsAsync(token, baseDn, filter);
            tasks.Add(("SmsaServiceAccounts", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.SmsaServiceAccounts = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }

        if (settings.IsKpiEnabled("ADUserAccountsCategory", "UserAccountLockedOut") || settings.IsKpiEnabled("ADGovernance", "UserAccountLockedOut"))
        {
            var baseDn = ResolveValue(config.CustomUserAccountLockedOutBaseDn, config.DefaultActiveDirectoryDN);
            var filter = config.DefaultUserAccountLockedOutFilter;
            var t = GetUserAccountLockedOutAsync(token, baseDn, filter);
            tasks.Add(("UserAccountLockedOut", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.UserAccountLockedOut = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("ADGroupsCategory", "EmptyGroups") || settings.IsKpiEnabled("ADGovernance", "EmptyGroups"))
        {
            var baseDn = ResolveValue(config.CustomEmptyGroupsBaseDn, config.DefaultActiveDirectoryDN);
            var filter = config.DefaultEmptyGroupsFilter;
            var t = GetEmptyGroupsAsync(token, baseDn, filter);
            tasks.Add(("EmptyGroups", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.EmptyGroups = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("PrivilegedGroups", "AccountOperators"))
        {
            var t = GetAccountOperatorsAsync(token);
            tasks.Add(("AccountOperators", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.AccountOperators = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("PrivilegedGroups", "Administrators"))
        {
            var t = GetAdministratorsAsync(token);
            tasks.Add(("Administrators", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.Administrators = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("PrivilegedGroups", "BackupOperators"))
        {
            var t = GetBackupOperatorsAsync(token);
            tasks.Add(("BackupOperators", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.BackupOperators = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("PrivilegedGroups", "DomainAdmins"))
        {
            var t = GetDomainAdminsAsync(token);
            tasks.Add(("DomainAdmins", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.DomainAdmins = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("PrivilegedGroups", "ServerOperators"))
        {
            var t = GetServerOperatorsAsync(token);
            tasks.Add(("ServerOperators", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.ServerOperators = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("PrivilegedGroups", "EnterpriseAdmins"))
        {
            var t = GetEnterpriseAdminsAsync(token);
            tasks.Add(("EnterpriseAdmins", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.EnterpriseAdmins = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("PrivilegedGroups", "SchemaAdmins"))
        {
            var t = GetSchemaAdminsAsync(token);
            tasks.Add(("SchemaAdmins", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.SchemaAdmins = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("Infrastructure", "Sites"))
        {
            var t = GetInfrastructureKpiAsync(token, KpiInfo.Sites);
            tasks.Add(("Sites", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.Sites = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("Infrastructure", "SiteLinks"))
        {
            var t = GetInfrastructureKpiAsync(token, KpiInfo.SiteLinks);
            tasks.Add(("SiteLinks", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.SiteLinks = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("Infrastructure", "Subnets"))
        {
            var t = GetInfrastructureKpiAsync(token, KpiInfo.Subnets);
            tasks.Add(("Subnets", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.Subnets = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }
        if (settings.IsKpiEnabled("Infrastructure", "OUs"))
        {
            var t = GetInfrastructureKpiAsync(token, KpiInfo.OUs);
            tasks.Add(("OUs", t));
            _ = t.ContinueWith(r => { if (r.IsCompletedSuccessfully) summary.OUs = r.Result; }, TaskContinuationOptions.ExecuteSynchronously);
        }

        await Task.WhenAll(tasks.Select(t => t.Task));

        // Tier 2 security-health signals (krbtgt password age, weak domain password policy,
        // orphaned adminCount accounts). These are lightweight targeted reads against the
        // default AD naming context and are best-effort: failures are captured per-summary
        // and never abort the overall risk summary.
        await PopulateSecurityHealthAsync(token, summary);

        // Stamp per-object permission-scope (effective AT-Link GUIDs + object class) onto the typed
        // user drilldown lists so the shared superset can be filtered per viewer. The typed items are
        // Stamp per-object permission-scope (effective AT-Link GUIDs + object class) onto the typed
        // drilldown lists so the shared superset can be filtered per viewer. Typed items are derived
        // from raw JsonElement sources that carry edsvaATLinksEffective/objectClass (requested
        // centrally in BuildAttributesQuery); we build a global DN->scope map and copy it across.
        StampObjectScopes(summary);

        return summary;
    }

    /// <summary>
    /// Copies each raw directory object's effective Access Template Link GUIDs and object class onto
    /// every typed <see cref="IPermissionScoped"/> drilldown derived from it, keyed by DN, across all
    /// KPI families. Also records the visible-domain context for scalar security-health signals.
    /// Enables per-user visibility filtering without re-querying Active Roles.
    /// </summary>
    private static void StampObjectScopes(DashboardSummary summary)
    {
        var scopeByDn = new Dictionary<string, (IReadOnlyCollection<string> Links, string Class)>(StringComparer.OrdinalIgnoreCase);

        void Index(IEnumerable<JsonElement>? rawItems)
        {
            if (rawItems == null) return;
            foreach (var raw in rawItems)
            {
                var dn = GetAttr(raw, "distinguishedName");
                if (string.IsNullOrEmpty(dn) || scopeByDn.ContainsKey(dn))
                    continue;
                scopeByDn[dn] = (SegmentAttributes.EffectiveLinksOf(raw).ToArray(), SegmentAttributes.ClassOf(raw));
            }
        }

        // Raw JsonElement sources carry the authoritative scope attributes.
        Index(summary.ADUserAccounts?.Items);
        Index(summary.ADGroups?.Items);
        Index(summary.Computers?.Items);

        void Stamp(IEnumerable<IPermissionScoped>? items)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item == null) continue;
                var dn = DnOf(item);
                if (!string.IsNullOrEmpty(dn) && scopeByDn.TryGetValue(dn, out var s))
                    SetScope(item, s.Links, s.Class);
            }
        }

        // User drilldowns (all derived from ADUserAccounts / ExpiringUsers).
        Stamp(summary.StaleUsers?.Items);
        Stamp(summary.NeverLoggedIn?.Items);
        Stamp(summary.AdminCount?.Items);
        Stamp(summary.EnabledUsers?.Items);
        Stamp(summary.DisabledUsers?.Items);
        Stamp(summary.ExpiredUsers?.Items);
        Stamp(summary.PasswordNeverExpires?.Items);
        Stamp(summary.DeprovisionedUsers?.Items);
        Stamp(summary.MustChangePassword?.Items);
        Stamp(summary.PasswordNotRequired?.Items);
        Stamp(summary.SmartCardRequired?.Items);
        Stamp(summary.CannotChangePassword?.Items);
        Stamp(summary.UserReversibleEncryption?.Items);
        Stamp(summary.TrustedForDelegation?.Items);
        Stamp(summary.SpnUserAccounts?.Items);
        Stamp(summary.SensitiveCannotDelegate?.Items);
        Stamp(summary.UseDesEncryption?.Items);
        Stamp(summary.NoKerberosPreauth?.Items);
        Stamp(summary.ExpiringUsers?.Items);

        // Group detail drilldowns (derived from ADGroups).
        Stamp(summary.DistributionGroups?.Items);
        Stamp(summary.DomainLocalGroups?.Items);
        Stamp(summary.GlobalGroups?.Items);
        Stamp(summary.MailEnabledSecurityGroups?.Items);
        Stamp(summary.SecurityGroups?.Items);
        Stamp(summary.UniversalGroups?.Items);

        // Computer breakdown drilldowns (derived from Computers).
        Stamp(summary.ComputerClients?.Items);
        Stamp(summary.ComputerServers?.Items);
        Stamp(summary.UnconstrainedComputers?.Items);
        Stamp(summary.WinServer2008R2?.Items);
        Stamp(summary.WinServer2012R2?.Items);
        Stamp(summary.WinServer2016?.Items);
        Stamp(summary.WinServer2019?.Items);
        Stamp(summary.WinServer2022?.Items);
        Stamp(summary.WinServer2025?.Items);
        Stamp(summary.ServerOther?.Items);
        Stamp(summary.Win7?.Items);
        Stamp(summary.Win81?.Items);
        Stamp(summary.Win10_22H2?.Items);
        Stamp(summary.Win11_22H2?.Items);
        Stamp(summary.Win11_23H2?.Items);
        Stamp(summary.Win11Enterprise?.Items);
        Stamp(summary.Win11Pro?.Items);
        Stamp(summary.ClientsOther?.Items);
        Stamp(summary.StaleComputers?.Items);
        Stamp(summary.DomainControllers?.Items);

        // Privileged group members.
        Stamp(summary.AccountOperators?.Items);
        Stamp(summary.Administrators?.Items);
        Stamp(summary.BackupOperators?.Items);
        Stamp(summary.DomainAdmins?.Items);
        Stamp(summary.ServerOperators?.Items);
        Stamp(summary.EnterpriseAdmins?.Items);
        Stamp(summary.SchemaAdmins?.Items);
        Stamp(summary.ActiveRolesAdmins?.Items);

        // Governance KPI drilldowns.
        Stamp(summary.NoManagerUser?.Items);
        Stamp(summary.NoManagerServiceAccount?.Items);
        Stamp(summary.ServiceAccounts?.Items);
        Stamp(summary.GmsaServiceAccounts?.Items);
        Stamp(summary.SmsaServiceAccounts?.Items);
        Stamp(summary.UserAccountLockedOut?.Items);
        Stamp(summary.ReversibleEncryption?.Items);
        Stamp(summary.EmptyGroups?.Items);
        Stamp(summary.CircularGroupNesting?.Items);
        Stamp(summary.Sites?.Items);
        Stamp(summary.SiteLinks?.Items);
        Stamp(summary.Subnets?.Items);
        Stamp(summary.OUs?.Items);
        Stamp(summary.NoGroupOwner?.Items);

        // Domain / server infra objects.
        Stamp(summary.Domains?.Items);
        Stamp(summary.Servers?.Items);
    }

    /// <summary>Reads the DN of a typed permission-scoped item (property names vary by type).</summary>
    private static string DnOf(IPermissionScoped item) => item switch
    {
        ADUserAccountDetailInfo u => u.Dn,
        ExpiringUserInfo e => e.Dn,
        ADGroupDetailInfo g => g.Dn,
        ComputerBreakdownInfo c => c.Dn,
        PrivilegedGroupMemberInfo p => p.Dn,
        GovernanceKpiInfo k => k.Dn,
        NoGroupOwnerInfo n => n.Dn,
        DomainInfo d => d.Dn,
        ServerInfo => string.Empty, // ServerInfo has no DN; scope stays empty (admin-only visibility)
        DomainControllerInfo dc => dc.Dn,
        _ => string.Empty
    };

    /// <summary>Writes scope onto a typed permission-scoped item (setters are type-specific).</summary>
    private static void SetScope(IPermissionScoped item, IReadOnlyCollection<string> links, string objectClass)
    {
        switch (item)
        {
            case ADUserAccountDetailInfo u: u.EffectiveLinkGuids = links; u.ObjectClass = objectClass; break;
            case ExpiringUserInfo e: e.EffectiveLinkGuids = links; e.ObjectClass = objectClass; break;
            case ADGroupDetailInfo g: g.EffectiveLinkGuids = links; g.ObjectClass = objectClass; break;
            case ComputerBreakdownInfo c: c.EffectiveLinkGuids = links; c.ObjectClass = objectClass; break;
            case PrivilegedGroupMemberInfo p: p.EffectiveLinkGuids = links; p.ObjectClass = objectClass; break;
            case GovernanceKpiInfo k: k.EffectiveLinkGuids = links; k.ObjectClass = objectClass; break;
            case NoGroupOwnerInfo n: n.EffectiveLinkGuids = links; n.ObjectClass = objectClass; break;
            case DomainInfo d: d.EffectiveLinkGuids = links; d.ObjectClass = objectClass; break;
            case ServerInfo s: s.EffectiveLinkGuids = links; s.ObjectClass = objectClass; break;
            case DomainControllerInfo dc: dc.EffectiveLinkGuids = links; dc.ObjectClass = objectClass; break;
        }
    }

    private static string ResolveValue(string userValue, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(userValue) ? defaultValue : userValue;
    }

    /// <summary>
    /// Populates Tier 2 security-health signals on the risk summary. Each signal is
    /// collected best-effort so a failure in one does not affect the others or the
    /// overall summary. Reads target the default Active Directory naming context.
    /// </summary>
    private async Task PopulateSecurityHealthAsync(string token, DashboardSummary summary)
    {
        var config = _configMonitor.CurrentValue;
        var baseDn = config.DefaultActiveDirectoryDN;

        // --- krbtgt password age ---------------------------------------------
        // A rarely-rotated krbtgt password increases exposure to Golden Ticket attacks.
        try
        {
            var items = await SearchObjectsAsync(token, baseDn, "(sAMAccountName=krbtgt)", "sub", "pwdLastSet,distinguishedName,edsaDomainNetbiosName");
            var krbtgtItem = items.FirstOrDefault(i => !string.IsNullOrEmpty(GetAttr(i, "pwdLastSet")) && GetAttr(i, "pwdLastSet") != "0");
            var krbtgtDomain = items.Select(i => GetAttr(i, "edsaDomainNetbiosName")).FirstOrDefault(d => !string.IsNullOrEmpty(d)) ?? string.Empty;
            var raw = krbtgtItem.ValueKind == JsonValueKind.Object ? GetAttr(krbtgtItem, "pwdLastSet") : null;
            if (raw != null && long.TryParse(raw, out var val) && val > 0)
            {
                var lastSet = DateTime.FromFileTimeUtc(val);
                var ageDays = (int)Math.Max(0, (DateTime.UtcNow - lastSet).TotalDays);
                summary.KrbtgtPasswordAge = new SecurityHealthSummary { Value = ageDays, Domain = krbtgtDomain };
            }
            else
            {
                summary.KrbtgtPasswordAge = new SecurityHealthSummary { Error = "krbtgt pwdLastSet unavailable" };
            }
        }
        catch (Exception ex)
        {
            summary.KrbtgtPasswordAge = new SecurityHealthSummary { Error = $"No data ({ex.GetType().Name}: {ex.Message})" };
        }

        // --- Domain password policy ------------------------------------------
        // Reads the default domain policy from the domain root object. Weaknesses are
        // encoded as small counts so the assessment engine's threshold comparison works:
        // a value of 1 means "weak" (fails a warn/fail threshold of 1), 0 means "adequate".
        try
        {
            var items = await SearchObjectsAsync(token, baseDn,
                "(objectClass=domainDNS)", "base",
                "minPwdLength,maxPwdAge,pwdProperties,lockoutThreshold,edsaDomainNetbiosName");
            if (items.Count > 0)
            {
                var domain = items[0];
                var domainName = GetAttr(domain, "edsaDomainNetbiosName");
                var minLen = int.TryParse(GetAttr(domain, "minPwdLength"), out var ml) ? ml : -1;
                var pwdProps = long.TryParse(GetAttr(domain, "pwdProperties"), out var pp) ? pp : 0;
                var lockoutThreshold = int.TryParse(GetAttr(domain, "lockoutThreshold"), out var lt) ? lt : -1;

                // DOMAIN_PASSWORD_COMPLEX = 0x1
                bool complexityEnabled = (pwdProps & 0x1) != 0;

                // maxPwdAge is a negative 100-ns interval; 0 or the "never" sentinel means no expiry.
                int maxAgeDays = 0;
                if (long.TryParse(GetAttr(domain, "maxPwdAge"), out var maxPwdAge) && maxPwdAge != 0
                    && maxPwdAge != long.MinValue)
                {
                    maxAgeDays = (int)Math.Round(TimeSpan.FromTicks(Math.Abs(maxPwdAge)).TotalDays);
                }

                summary.WeakPasswordLength = new SecurityHealthSummary { Value = (minLen >= 0 && minLen < 12) ? 1 : 0, Domain = domainName };
                summary.PasswordComplexityDisabled = new SecurityHealthSummary { Value = complexityEnabled ? 0 : 1, Domain = domainName };
                summary.NoAccountLockout = new SecurityHealthSummary { Value = (lockoutThreshold == 0) ? 1 : 0, Domain = domainName };
                summary.PasswordMaxAgeDays = new SecurityHealthSummary { Value = maxAgeDays, Domain = domainName };
            }
            else
            {
                var err = "Domain password policy unavailable";
                summary.WeakPasswordLength = new SecurityHealthSummary { Error = err };
                summary.PasswordComplexityDisabled = new SecurityHealthSummary { Error = err };
                summary.NoAccountLockout = new SecurityHealthSummary { Error = err };
                summary.PasswordMaxAgeDays = new SecurityHealthSummary { Error = err };
            }
        }
        catch (Exception ex)
        {
            var err = $"No data ({ex.GetType().Name}: {ex.Message})";
            summary.WeakPasswordLength = new SecurityHealthSummary { Error = err };
            summary.PasswordComplexityDisabled = new SecurityHealthSummary { Error = err };
            summary.NoAccountLockout = new SecurityHealthSummary { Error = err };
            summary.PasswordMaxAgeDays = new SecurityHealthSummary { Error = err };
        }
    }

    /// <summary>
    /// Executes the search(es) defined on a KpiInfo and returns the raw results.
    /// Resolves tokens in BaseDn, Filter, and Attributes from the current config.
    /// </summary>
    public async Task<List<JsonElement>> ExecuteKpiSearchAsync(string token, KpiSearchDefinition search)
    {
        var config = _configMonitor.CurrentValue;
        var baseDn = search.ResolveBaseDn(config);
        var filter = search.ResolveFilter(config);
        var attributes = search.ResolveAttributes(config);
        return await SearchObjectsAsync(token, baseDn, filter, search.Scope, attributes);
    }

    /// <summary>
    /// Executes a KPI search with optional overrides for baseDn and filter (for custom config overrides).
    /// </summary>
    public async Task<List<JsonElement>> ExecuteKpiSearchAsync(string token, KpiSearchDefinition search, string? baseDnOverride, string? filterOverride)
    {
        var config = _configMonitor.CurrentValue;
        var baseDn = string.IsNullOrWhiteSpace(baseDnOverride) ? search.ResolveBaseDn(config) : baseDnOverride;
        var filter = string.IsNullOrWhiteSpace(filterOverride) ? search.ResolveFilter(config) : filterOverride;
        var attributes = search.ResolveAttributes(config);
        return await SearchObjectsAsync(token, baseDn, filter, search.Scope, attributes);
    }

    public async Task<ADUserAccountsSummary> GetADUserAccountsCountAsync(string token)
    {
        var result = new ADUserAccountsSummary();
        try
        {
            var config = _configMonitor.CurrentValue;
            var baseDn = config.DefaultActiveDirectoryDN;
            var filter = config.DefaultADUserAccountsFilter;
            var attributes = string.Join(",", config.DefaultADUserAccountAttributes.Concat(config.CustomADUserAccountAttributes).Distinct());
            var items = await SearchObjectsAsync(token, baseDn, filter, "sub", attributes);
            result.TotalCount = items.Count;
            result.Items = items;
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<ADGroupsSummary> GetADGroupsAsync(string token)
    {
        var result = new ADGroupsSummary();
        try
        {
            var config = _configMonitor.CurrentValue;
            var baseDn = config.DefaultActiveDirectoryDN;
            var filter = config.DefaultADGroupsFilter;
            var attributes = "name,distinguishedName,groupType,edsaIsDynamicGroup,edsaMember,edsaMemberIndirect,mail,edsvaGFIsGroupFamily,edsaDomainNetbiosName";
            var items = await SearchObjectsAsync(token, baseDn, filter, "sub", attributes);
            result.Items = items;
            result.TotalCount = items.Count(i => !string.Equals(GetAttr(i, "edsvaGFIsGroupFamily"), "TRUE", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<ComputersSummary> GetComputersAsync(string token)
    {
        var result = new ComputersSummary();
        try
        {
            var config = _configMonitor.CurrentValue;
            var baseDn = config.DefaultActiveDirectoryDN;
            var filter = "(objectClass=computer)";
            var attributes = "name,distinguishedName,userAccountControl,edsaDomainNetbiosName,operatingSystem,operatingSystemVersion,msDS-SiteName";
            var items = await SearchObjectsAsync(token, baseDn, filter, "sub", attributes);
            result.Items = items;
            // Exclude domain controllers (SERVER_TRUST_ACCOUNT = 0x2000)
            result.TotalCount = items.Count(i =>
            {
                var uac = GetAttr(i, "userAccountControl");
                return !int.TryParse(uac, out var v) || (v & 0x2000) == 0;
            });
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    /// <summary>
    /// Discovers the connected Azure/Entra tenants
    /// containers under the Azure configuration base (e.g. CN=&lt;tenant&gt;,CN=Azure,CN=Configuration).
    /// Returns the tenant names (the RDN value of each child container).
    /// </summary>
    public async Task<List<string>> GetEntraTenantsAsync(string token)
    {
        var config = _configMonitor.CurrentValue;
        var baseDn = config.DefaultAzureConfigurationDN;
        // Tenant nodes under the Azure configuration container are of the Active Roles
        // class 'edsAzureTenantcontainer'. Filtering on this class picks up the tenants
        // while excluding the 'Azure Configuration' node itself.
        var items = await SearchObjectsAsync(token, baseDn, "(objectClass=edsAzureTenantcontainer)", "one", "name,distinguishedName");
        return items
            .Select(i => GetAttr(i, "name"))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Identifies the Entra object types that are groups, so group-specific attributes
    /// (such as edsaMember for the Empty Groups KPI) are only requested for those types.
    /// </summary>
    public static bool IsEntraGroupType(EntraObjectType type) =>
        type is EntraObjectType.DistributionGroup
            or EntraObjectType.DynamicDistributionGroup
            or EntraObjectType.Microsoft365Group
            or EntraObjectType.SecurityGroup;

    /// <summary>
    /// Collects totals for all nine Entra object types across every connected tenant.
    /// Runs one subtree search per object type over the whole Azure base (the most
    /// performant approach for large environments: 9 round-trips regardless of tenant
    /// count), then tags each result with its source tenant (derived from the DN) and
    /// aggregates the counts.
    /// </summary>
    public async Task<EntraTotalsSummary> GetEntraTotalsAsync(string token)
    {
        var result = new EntraTotalsSummary();
        var config = _configMonitor.CurrentValue;
        var baseDn = config.DefaultAzureConfigurationDN;

        try
        {
            result.Tenants = await GetEntraTenantsAsync(token);
        }
        catch (Exception ex)
        {
            // Tenant discovery is best-effort; totals can still be collected and the
            // source tenant left blank if discovery fails.
            result.Error = $"Tenant discovery failed ({ex.GetType().Name}: {ex.Message})";
        }

        var tenants = result.Tenants;

        // Fetch every object type in parallel.
        var typeTasks = EntraObjectTypeInfo.All.Select(async type =>
        {
            var objectClass = EntraObjectTypeInfo.ObjectClass(type);
            var count = new EntraObjectTypeCount { ObjectType = type };
            var items = new List<EntraObjectInfo>();
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                // Users need the account-enabled state and manager for the User Accounts
                // category KPIs/drilldowns; guest users need the UPN (and enabled state) so the
                // internal/external classification (#EXT# marker) works regardless of object
                // class. Group membership ('member') and owner ('edsaAzureGroupManagedBy') are
                // deliberately NOT requested here: fetching membership inline for every group is
                // the dominant login cost. Those attributes are loaded lazily and in parallel by
                // LoadEntraGroupMembershipAsync after the initial page render. Group types (and
                // every other type) therefore only need name + DN in this eager pass, plus the
                // cheap single-valued 'visibility' and 'edsvaOnPremisesSyncEnabled' attributes
                // that back the Public M365 Groups and On-Premises Synced Groups KPIs.
                var attributes = type == EntraObjectType.User
                    ? "name,distinguishedName,edsaAzureUserAccountEnabled,manager,edsaAzureUserPrincipalName"
                    : type == EntraObjectType.GuestUser
                        ? "name,distinguishedName,edsaAzureUserAccountEnabled,edsaAzureUserPrincipalName"
                        : "name,distinguishedName,visibility,edsvaOnPremisesSyncEnabled";
                var raw = await SearchObjectsAsync(
                    token, baseDn, $"(objectClass={objectClass})", "sub", attributes);
                sw.Stop();
                _logger.LogInformation(
                    "Entra collection: {ObjectType} took {ElapsedMs}ms for {Count} objects (attributes: {Attributes})",
                    type, sw.ElapsedMilliseconds, raw.Count, attributes);
                foreach (var el in raw)
                {
                    var dn = GetAttr(el, "distinguishedName");
                    items.Add(new EntraObjectInfo
                    {
                        Name = GetAttr(el, "name"),
                        Dn = dn,
                        Tenant = ExtractTenantFromDn(dn, tenants),
                        ObjectType = type,
                        Raw = el
                    });
                }
                count.TotalCount = items.Count;
            }
            catch (Exception ex)
            {
                count.Error = $"No data ({ex.GetType().Name}: {ex.Message})";
            }
            return (count, items);
        }).ToList();

        var results = await Task.WhenAll(typeTasks);

        foreach (var (count, items) in results)
        {
            result.ByObjectType.Add(count);
            result.Items.AddRange(items);
        }

        result.ByObjectType = result.ByObjectType
            .OrderBy(c => EntraObjectTypeInfo.All.ToList().IndexOf(c.ObjectType))
            .ToList();
        result.TotalCount = result.ByObjectType.Sum(c => c.TotalCount);
        return result;
    }

    /// <summary>
    /// Lazily loads group membership for the Entra Groups hygiene KPIs. For every group
    /// object already collected in <paramref name="summary"/>, this fetches the group's
    /// <c>member</c> (direct membership) and <c>edsaAzureGroupManagedBy</c> (owner)
    /// attributes with a per-group base-scope search, and merges those values back into
    /// each <see cref="EntraObjectInfo.Raw"/> so that <c>EntraEmptyGroups()</c>,
    /// <c>EntraNoGroupOwnerGroups()</c>, and <c>EntraGuestContainingGroups()</c> can be
    /// recomputed. The per-group fetches run in parallel, bounded by the configurable
    /// <see cref="ActiveRolesConfig.EntraMembershipFetchConcurrency"/> (default 8), because
    /// fetching membership inline for every group is the dominant login-time cost. On
    /// success <see cref="EntraTotalsSummary.MembershipLoaded"/> is set to true.
    /// </summary>
    public Task LoadEntraGroupMembershipAsync(string token, EntraTotalsSummary summary) =>
        LoadEntraGroupMembershipAsync(token, summary, 0, int.MaxValue);

    /// <summary>
    /// Slice-aware overload of <see cref="LoadEntraGroupMembershipAsync(string, EntraTotalsSummary)"/>.
    /// Loads membership only for the group window <paramref name="skip"/>..<paramref name="skip"/>+
    /// <paramref name="take"/> (ordered as the groups appear in <paramref name="summary"/>). This
    /// supports the client-driven batched loading used to drive the header progress badge:
    /// each call enriches a slice of groups and returns the total group count so the caller can
    /// determine how many remain. <see cref="EntraTotalsSummary.MembershipLoaded"/> is only set
    /// to true once the final slice (reaching the end of the group list) has completed.
    /// </summary>
    /// <returns>The total number of Entra group objects in the summary.</returns>
    public async Task<int> LoadEntraGroupMembershipAsync(string token, EntraTotalsSummary summary, int skip, int take)
    {
        if (summary == null || summary.Items.Count == 0)
            return 0;

        var allGroups = summary.Items.Where(i => IsEntraGroupType(i.ObjectType)).ToList();
        var totalGroups = allGroups.Count;
        if (totalGroups == 0)
        {
            summary.MembershipLoaded = true;
            return 0;
        }

        if (skip < 0) skip = 0;
        if (take < 0) take = 0;
        var groups = allGroups.Skip(skip).Take(take).ToList();
        var reachedEnd = skip + groups.Count >= totalGroups;
        if (groups.Count == 0)
        {
            if (reachedEnd)
            {
                summary.MembershipLoadedCount = totalGroups;
                summary.MembershipLoaded = true;
            }
            return totalGroups;
        }

        var concurrency = Math.Max(1, _configMonitor.CurrentValue.EntraMembershipFetchConcurrency);
        using var gate = new SemaphoreSlim(concurrency);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Per-object-type timing: track the wall-clock span (first fetch start -> last fetch
        // finish) and the count for each group type, so we can report e.g. "loaded membership
        // for N security groups in X ms". Timestamps use the shared stopwatch's baseline.
        var typeStats = new System.Collections.Concurrent.ConcurrentDictionary<EntraObjectType, (long FirstStartMs, long LastEndMs, int Count)>();

        void RecordTiming(EntraObjectType type, long startMs, long endMs)
        {
            typeStats.AddOrUpdate(
                type,
                _ => (startMs, endMs, 1),
                (_, cur) => (Math.Min(cur.FirstStartMs, startMs), Math.Max(cur.LastEndMs, endMs), cur.Count + 1));
        }

        var tasks = groups.Select(async group =>
        {
            await gate.WaitAsync();
            var startMs = sw.ElapsedMilliseconds;
            try
            {
                if (string.IsNullOrWhiteSpace(group.Dn))
                    return;

                var raw = await SearchObjectsAsync(
                    token, group.Dn, "(objectClass=*)", "base",
                    "member,edsaAzureGroupManagedBy");
                var fetched = raw.FirstOrDefault();
                if (fetched.ValueKind == JsonValueKind.Object)
                    group.Raw = MergeAttributes(group.Raw, fetched,
                        "member", "edsaAzureGroupManagedBy");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Entra membership fetch failed for {Dn} ({Type}): {Message}",
                    group.Dn, ex.GetType().Name, ex.Message);
            }
            finally
            {
                RecordTiming(group.ObjectType, startMs, sw.ElapsedMilliseconds);
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        sw.Stop();

        // Per-type breakdown, e.g. "Entra membership: Security Groups - loaded 1234 groups in 5678ms".
        foreach (var type in EntraObjectTypeInfo.All.Where(IsEntraGroupType))
        {
            if (!typeStats.TryGetValue(type, out var stat))
                continue;
            var elapsedMs = Math.Max(0, stat.LastEndMs - stat.FirstStartMs);
            _logger.LogInformation(
                "Entra membership: {ObjectType} - loaded membership for {Count} groups in {ElapsedMs}ms",
                EntraObjectTypeInfo.DisplayName(type), stat.Count, elapsedMs);
        }

        _logger.LogInformation(
            "Entra membership: loaded 'member'/'edsaAzureGroupManagedBy' for {Count} groups in {ElapsedMs}ms (concurrency {Concurrency})",
            groups.Count, sw.ElapsedMilliseconds, concurrency);

        // Advance the persisted loaded-count high-water mark so navigating between pages
        // resumes from this offset rather than restarting at the full group count.
        summary.MembershipLoadedCount = Math.Max(summary.MembershipLoadedCount, skip + groups.Count);

        if (reachedEnd)
            summary.MembershipLoaded = true;

        return totalGroups;
    }

    /// <summary>
    /// Produces a new <see cref="JsonElement"/> that is a copy of <paramref name="original"/>
    /// with the named attributes overlaid from <paramref name="source"/>. Attribute lookup
    /// mirrors <c>GetAttr</c>: a value is taken from the direct property or from a nested
    /// <c>attributes</c> object. Copied attributes are written as direct top-level
    /// properties so <c>SegmentAttributes.AttrOf</c>/<c>MultiAttrOf</c> can read them.
    /// </summary>
    private static JsonElement MergeAttributes(JsonElement original, JsonElement source, params string[] attributeNames)
    {
        using var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            var overlaid = new HashSet<string>(attributeNames, StringComparer.OrdinalIgnoreCase);

            if (original.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in original.EnumerateObject())
                {
                    if (overlaid.Contains(prop.Name))
                        continue;
                    prop.WriteTo(writer);
                }
            }

            foreach (var name in attributeNames)
            {
                if (TryGetRawProperty(source, name, out var value))
                {
                    writer.WritePropertyName(name);
                    value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(buffer.ToArray());
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Reads an attribute value as a raw <see cref="JsonElement"/> from the direct property
    /// or a nested <c>attributes</c> object, matching the lookup used elsewhere in the service.
    /// </summary>
    private static bool TryGetRawProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out value))
                return true;
            if (element.TryGetProperty("attributes", out var attrs)
                && attrs.ValueKind == JsonValueKind.Object
                && attrs.TryGetProperty(name, out value))
                return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Resolves the owning tenant for an Entra object by matching a discovered tenant
    /// name against the components of the object's distinguished name. Returns an empty
    /// string when no tenant can be determined.
    /// </summary>
    private static string ExtractTenantFromDn(string dn, IReadOnlyList<string> tenants)
    {
        if (string.IsNullOrWhiteSpace(dn) || tenants.Count == 0)
            return string.Empty;

        // DN components look like: CN=obj,...,CN=<tenant>,CN=Azure,CN=Configuration
        // Match the most specific (last-matching) tenant RDN present in the DN.
        foreach (var component in dn.Split(','))
        {
            var trimmed = component.Trim();
            var eq = trimmed.IndexOf('=');
            if (eq < 0) continue;
            var value = trimmed[(eq + 1)..].Trim();
            var match = tenants.FirstOrDefault(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }
        return string.Empty;
    }

    private ADGroupDetailInfo ToGroupDetail(JsonElement i)
    {
        var directRaw = GetAttr(i, "edsaMember");
        var indirectRaw = GetAttr(i, "edsaMemberIndirect");
        return new ADGroupDetailInfo
        {
            Name = GetAttr(i, "name"),
            Dn = GetAttr(i, "distinguishedName"),
            DirectMembers = ParseMemberCount(directRaw),
            IndirectMembers = ParseMemberCount(indirectRaw)
        };
    }

    private static int ParseMemberCount(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return 0;
        if (int.TryParse(raw, out var count)) return count;
        // If multi-valued (DN list), count semicolons or entries
        return raw.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// Detects groups that participate in a circular nesting chain (e.g. C is a member of B,
    /// B is a member of A, and A is a member of C). Builds a directed graph of group-to-group
    /// membership edges (group -> the groups it is a member of) from the shared ADGroups dataset
    /// and returns every group that lies on at least one cycle.
    /// </summary>
    private static GovernanceKpiSummary DetectCircularGroupNesting(List<JsonElement> groups)
    {
        var result = new GovernanceKpiSummary();
        try
        {
            // Map every group DN (case-insensitive) to its element and its display info.
            var byDn = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                var dn = GetAttr(g, "distinguishedName");
                if (!string.IsNullOrEmpty(dn) && !byDn.ContainsKey(dn))
                    byDn[dn] = g;
            }

            // Build adjacency: for each group, the edge points to member DNs that are themselves
            // groups in the dataset (i.e. child group -> parent group relationships are captured by
            // walking each group's direct members and keeping only group members).
            var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in byDn)
            {
                var memberDns = GetMultiValuedAttr(kvp.Value, "edsaMember");
                var groupMembers = memberDns
                    .Where(m => !string.IsNullOrEmpty(m) && byDn.ContainsKey(m))
                    .ToList();
                adjacency[kvp.Key] = groupMembers;
            }

            // Iterative DFS with coloring (white/gray/black) to find all nodes that lie on a cycle.
            const int White = 0, Gray = 1, Black = 2;
            var color = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var dn in adjacency.Keys) color[dn] = White;
            var onCycle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var start in adjacency.Keys)
            {
                if (color[start] != White) continue;

                // stack holds (node, path-index of the node within the current DFS path)
                var stack = new Stack<string>();
                var path = new List<string>();
                var pathIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                stack.Push(start);

                while (stack.Count > 0)
                {
                    var node = stack.Peek();
                    if (color[node] == White)
                    {
                        color[node] = Gray;
                        pathIndex[node] = path.Count;
                        path.Add(node);
                    }

                    bool advanced = false;
                    foreach (var next in adjacency[node])
                    {
                        if (color[next] == White)
                        {
                            stack.Push(next);
                            advanced = true;
                            break;
                        }
                        if (color[next] == Gray)
                        {
                            // Back edge: everything from 'next' to 'node' on the current path is on a cycle.
                            if (pathIndex.TryGetValue(next, out var startIdx))
                            {
                                for (int k = startIdx; k < path.Count; k++)
                                    onCycle.Add(path[k]);
                            }
                        }
                    }

                    if (!advanced)
                    {
                        color[node] = Black;
                        if (path.Count > 0 && string.Equals(path[^1], node, StringComparison.OrdinalIgnoreCase))
                        {
                            pathIndex.Remove(node);
                            path.RemoveAt(path.Count - 1);
                        }
                        stack.Pop();
                    }
                }
            }

            var items = onCycle
                .Select(dn => byDn[dn])
                .Select(g => new GovernanceKpiInfo
                {
                    Name = GetAttr(g, "name"),
                    Domain = GetAttr(g, "edsaDomainNetbiosName"),
                    Dn = GetAttr(g, "distinguishedName"),
                    Guid = GetAttr(g, "objectGuid")
                })
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.TotalCount = items.Count;
            result.Items = items;
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<DomainSummary> GetDomainsAsync(string token)
    {
        var result = new DomainSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.Domains.Searches[0]);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new DomainInfo { Name = GetAttr(i, "name"), 
                                                              Dn = GetAttr(i, "edsvaDomainDNS"),
                                                              DnsName = GetAttr(i, "edsaSavedDnsName"), 
                                                              UseOverride = bool.Parse(GetAttr(i, "edsaUseOverrideAccount")), 
                                                              Guid = GetAttr(i, "objectGuid") }).ToList();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden || ex.StatusCode == System.Net.HttpStatusCode.Unauthorized) { result.Error = "No data"; }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<ServerSummary> GetServersAsync(string token)
    {
        var result = new ServerSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.Servers.Searches[0]);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new ServerInfo { ServerName = GetAttr(i, "edsaEdmServiceComputerName"), 
                                                              Version = GetAttr(i, "edsvaPublicProductVersion"),
                                                              Guid = GetAttr(i, "objectGuid") }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<DynamicGroupSummary> GetDynamicGroupsAsync(string token)
    {
        var result = new DynamicGroupSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.DynamicGroups.Searches[0]);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new DynamicGroupInfo { Name = GetAttr(i, "name"), 
                                                                    Dn = GetAttr(i, "distinguishedName"), 
                                                                    Guid = GetAttr(i, "objectGuid") }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<ManagedUnitSummary> GetManagedUnitsAsync(string token)
    {
        var result = new ManagedUnitSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.ManagedUnits.Searches[0]);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new ManagedUnitInfo { Name = GetAttr(i, "name"), 
                                                                   Dn = GetAttr(i, "distinguishedName"), 
                                                                   Guid = GetAttr(i, "objectGuid"),
                                                                   RuleCount = CountMembershipRules(GetAttr(i, "edsaMUConditionsList")) }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    private static int CountMembershipRules(string conditionsList)
    {
        if (string.IsNullOrEmpty(conditionsList)) return 0;
        // Each rule is enclosed in [...], so count the opening brackets
        int count = 0;
        foreach (var c in conditionsList)
        {
            if (c == '[') count++;
        }
        return count;
    }

    public async Task<WorkflowSummary> GetWorkflowsAsync(string token)
    {
        var result = new WorkflowSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.Workflows.Searches[0]);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new WorkflowInfo { Name = GetAttr(i, "name"), 
                                                                Dn = GetAttr(i, "distinguishedName"), 
                                                                IsEnabled = GetAttr(i, "edsaWorkflowIsDisabled") == "false", 
                                                                IsAutomationWorkflow = GetAttr(i, "objectClass") == "edsAutomationWorkflowDefinition", 
                                                                Guid = GetAttr(i, "objectGuid") }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<VirtualAttributeSummary> GetVirtualAttributesAsync(string token)
    {
        var result = new VirtualAttributeSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.VirtualAttributes.Searches[0]);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new VirtualAttributeInfo { Name = GetAttr(i, "name"), 
                                                                        LdapDisplayName = GetAttr(i, "lDAPDisplayName"), 
                                                                        IsMultivalued = GetAttr(i, "isSingleValued") == "false", 
                                                                        Guid = GetAttr(i, "objectGuid") }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<ConfigDatabaseSummary> GetConfigDatabasesAsync(string token)
    {
        var result = new ConfigDatabaseSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.ConfigDatabases.Searches[0]);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new DatabaseInfo { SqlAlias = GetAttr(i, "edsaSQLAlias"),
                                                                DatabaseName = GetAttr(i, "edsaDatabaseName"),
                                                                DatabaseType = GetAttr(i, "edsaDatabaseType"),
                                                                ReplicationSupport = GetAttr(i, "edsaReplicationSupport"),
                                                                ReplicationRole = ParseReplicationRole(GetAttr(i, "edsaReplicationRole")) }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<HistoryDatabaseSummary> GetHistoryDatabasesAsync(string token)
    {
        var result = new HistoryDatabaseSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.HistoryDatabases.Searches[0]);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new DatabaseInfo { SqlAlias = GetAttr(i, "edsaSQLAlias"),
                                                                DatabaseName = GetAttr(i, "edsaDatabaseName"),
                                                                DatabaseType = GetAttr(i, "edsaDatabaseType"),
                                                                ReplicationRole = ParseReplicationRole(GetAttr(i, "edsaReplicationRole")) }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    private static ReplicationRole ParseReplicationRole(string? value) =>
        int.TryParse(value, out var n) && Enum.IsDefined(typeof(ReplicationRole), n)
            ? (ReplicationRole)n
            : ReplicationRole.Undefined;

    public async Task<PolicyObjectSummary> GetPolicyObjectsAsync(string token)
    {
        var result = new PolicyObjectSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.PolicyObjects.Searches[0]);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new PolicyObjectInfo { Name = GetAttr(i, "name"), 
                                                                    Dn = GetAttr(i, "distinguishedName"), 
                                                                    Guid = GetAttr(i, "objectGuid"),
                                                                    RuleCount = CountApeRules(GetAttr(i, "edsaAPEListXML")) }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<AccessTemplateSummary> GetAccessTemplatesAsync(string token)
    {
        var result = new AccessTemplateSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.AccessTemplates.Searches[0]);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new AccessTemplateInfo { Name = GetAttr(i, "name"), 
                                                                      Dn = GetAttr(i, "distinguishedName"), 
                                                                      Parent = GetAttr(i, "edsvaParentCanonicalName"), 
                                                                      Guid = GetAttr(i, "objectGuid") }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<AccessTemplateLinkSummary> GetAccessTemplateLinksAsync(string token)
    {
        var result = new AccessTemplateLinkSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.AccessTemplateLinks.Searches[0]);
            result.TotalCount = items.Count;
            result.Items = new List<AccessTemplateLinkInfo>();
            foreach (var i in items)
            {
                var link = new AccessTemplateLinkInfo
                {
                    Name = GetAttr(i, "name"),
                    Dn = GetAttr(i, "distinguishedName"),
                    IsPredefined = string.Equals(GetAttr(i, "edsaIsPredefined"), "TRUE", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(GetAttr(i, "edsaSystemObject"), "TRUE", StringComparison.OrdinalIgnoreCase)
                };

                // Resolve Trustee SID to name
                var sid = GetAttr(i, "edsaTrusteeSID");
                if (!string.IsNullOrEmpty(sid))
                {
                    link.Trustee = await ResolveSidToNameAsync(token, sid);
                }

                // Resolve Directory Object GUID to name
                var secObjGuid = GetAttr(i, "edsaSecObjectGUID");
                if (!string.IsNullOrEmpty(secObjGuid))
                {
                    link.DirectoryObject = await ResolveGuidToNameAsync(token, secObjGuid);
                }

                // Resolve Access Template GUID to name
                var atGuid = GetAttr(i, "edsaAccessTemplateGUID");
                if (!string.IsNullOrEmpty(atGuid))
                {
                    link.AccessTemplate = await ResolveAccessTemplateGuidAsync(token, atGuid);
                }

                result.Items.Add(link);
            }
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<PolicyObjectLinkSummary> GetPolicyObjectLinksAsync(string token)
    {
        var result = new PolicyObjectLinkSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.PolicyObjectLinks.Searches[0]);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new PolicyObjectLinkInfo
            {
                Name = GetAttr(i, "name"),
                Dn = GetAttr(i, "distinguishedName")
            }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    private async Task<string> ResolveSidToNameAsync(string token, string sid)
    {
        try
        {
            // The API returns the SID as Base64-encoded bytes - decode to SID string
            string sidString;
            byte[]? sidBytes = null;
            try
            {
                sidBytes = Convert.FromBase64String(sid);
                var securityIdentifier = new System.Security.Principal.SecurityIdentifier(sidBytes, 0);
                sidString = securityIdentifier.Value;
            }
            catch
            {
                // If not valid Base64, assume it's already a SID string
                sidString = sid;
            }

            // Try to resolve well-known SIDs locally first
            try
            {
                var secId = new System.Security.Principal.SecurityIdentifier(sidString);
                var account = secId.Translate(typeof(System.Security.Principal.NTAccount));
                if (account != null)
                    return account.ToString();
            }
            catch { }

            // Search Active Directory
            var config = _configMonitor.CurrentValue;
            var items = await SearchObjectsAsync(token, config.DefaultActiveDirectoryDN, $"(objectSid={sidString})", "sub", "name");
            if (items.Count > 0)
                return GetAttr(items[0], "name");

            // Build the hex-authority SID variant for Active Roles foreignSecurityPrincipals
            // The REST API decodes authority as decimal, but AR stores it as hex (e.g. S-1-0x617273737663-1-1)
            var hexSid = ToHexAuthoritySid(sidString, sidBytes);

            // Search Active Roles well-known accounts
            // These objects have human-readable names (e.g., "Primary Owner (Managed By)") as their cn/name
            // and store the SID in objectSid. We need to enumerate and match.
            try
            {
                var arItems = await SearchObjectsAsync(token, "CN=Well-Known Accounts by ActiveRoles Server,CN=Application Configuration,CN=Configuration", "(objectClass=foreignSecurityPrincipal)", "sub", "name,displayName,objectSid");
                foreach (var item in arItems)
                {
                    var itemSid = GetAttr(item, "objectSid");
                    if (string.IsNullOrEmpty(itemSid)) continue;

                    // Compare raw value, decoded SID, or hex SID
                    if (string.Equals(itemSid, sidString, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(itemSid, sid, StringComparison.OrdinalIgnoreCase)
                        || (hexSid != null && string.Equals(itemSid, hexSid, StringComparison.OrdinalIgnoreCase)))
                    {
                        return GetAttr(item, "displayName") is { Length: > 0 } dn ? dn : GetAttr(item, "name");
                    }

                    // The stored objectSid may also be Base64 — decode and compare
                    try
                    {
                        var itemSidBytes = Convert.FromBase64String(itemSid);
                        var itemSecId = new System.Security.Principal.SecurityIdentifier(itemSidBytes, 0);
                        if (string.Equals(itemSecId.Value, sidString, StringComparison.OrdinalIgnoreCase))
                        {
                            return GetAttr(item, "displayName") is { Length: > 0 } dn2 ? dn2 : GetAttr(item, "name");
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Also try AD with hex format
            if (hexSid != null && hexSid != sidString)
            {
                try
                {
                    items = await SearchObjectsAsync(token, config.DefaultActiveDirectoryDN, $"(objectSid={hexSid})", "sub", "name");
                    if (items.Count > 0)
                        return GetAttr(items[0], "name");
                }
                catch { }
            }

            return sidString;
        }
        catch { }
        return sid;
    }

    /// <summary>
    /// Converts a SID string to use hex authority format when the authority value exceeds standard Windows range.
    /// The AR REST API returns the 6-byte authority as decimal, but Active Roles stores it as hex for
    /// foreignSecurityPrincipals (e.g., S-1-107144191112803-1-1 → S-1-0x617273737663-1-1).
    /// </summary>
    private static string? ToHexAuthoritySid(string sidString, byte[]? sidBytes)
    {
        if (sidBytes == null || sidBytes.Length < 8) return null;

        // SID binary format: byte[0]=revision, byte[1]=sub-authority count, bytes[2-7]=authority (big-endian)
        long authority = 0;
        for (int i = 2; i < 8; i++)
            authority = (authority << 8) | sidBytes[i];

        // Standard Windows authorities (SECURITY_NT_AUTHORITY etc.) are <= 15
        // Active Roles custom authorities are large values that need hex representation
        if (authority <= 0xFFFFFFFF) return null;

        // Parse the SID parts and rebuild with hex authority
        var parts = sidString.Split('-');
        if (parts.Length < 4) return null;

        // parts: "S", revision, authority(decimal), sub1, sub2, ...
        parts[2] = "0x" + authority.ToString("x");
        return string.Join("-", parts);
    }

    private async Task<string> ResolveGuidToNameAsync(string token, string guid)
    {
        try
        {
            var doc = await GetObjectDetailsAsync(token, guid, "name");
            if (doc != null)
            {
                using (doc)
                {
                    var name = GetAttr(doc.RootElement, "name");
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
            }
        }
        catch { }
        return "Unknown";
    }

    private async Task<string> ResolveAccessTemplateGuidAsync(string token, string guid)
    {
        try
        {
            var doc = await GetObjectDetailsAsync(token, guid, "name");
            if (doc != null)
            {
                using (doc)
                {
                    var name = GetAttr(doc.RootElement, "name");
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
            }
        }
        catch { }
        return "Unknown";
    }

    public async Task<ManagedObjectSummary> GetManagedObjectsAsync(string token)
    {
        var result = new ManagedObjectSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.ManagedObjects.Searches[0]);
            result.TotalCount = items.Count;
            result.DataPoints = items
                .Select(i => ParseStatisticsRun(GetAttr(i, "edsaStatisticsCountXML")))
                .Where(dp => dp != null)
                .Select(dp => dp!)
                .OrderBy(dp => dp.RunTime)
                .ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<NoGroupOwnerSummary> GetNoGroupOwnerAsync(string token, string baseDn, string filter)
    {
        var result = new NoGroupOwnerSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.NoGroupOwner.Searches[0], baseDn, filter);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new NoGroupOwnerInfo
            {
                Name = GetAttr(i, "name"),
                Dn = GetAttr(i, "distinguishedName"),
                Guid = GetAttr(i, "objectGuid")
            }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<GovernanceKpiSummary> GetNoManagerUserAsync(string token, string baseDn, string filter)
    {
        var result = new GovernanceKpiSummary();
        try
        {
            var items = await SearchObjectsAsync(token, baseDn, filter, "sub", "name,distinguishedName");
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new GovernanceKpiInfo
            {
                Name = GetAttr(i, "name"),
                Dn = GetAttr(i, "distinguishedName"),
                Guid = GetAttr(i, "objectGuid")
            }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<GovernanceKpiSummary> GetNoManagerServiceAccountAsync(string token, string baseDn, string filter)
    {
        var result = new GovernanceKpiSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.NoManagerServiceAccount.Searches[0], baseDn, filter);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new GovernanceKpiInfo
            {
                Name = GetAttr(i, "name"),
                Dn = GetAttr(i, "distinguishedName"),
                Guid = GetAttr(i, "objectGuid")
            }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<GovernanceKpiSummary> GetServiceAccountsAsync(string token, string baseDn, string filter)
    {
        var result = new GovernanceKpiSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.ServiceAccounts.Searches[0], baseDn, filter);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new GovernanceKpiInfo
            {
                Name = GetAttr(i, "name"),
                Dn = GetAttr(i, "distinguishedName"),
                Guid = GetAttr(i, "objectGuid")
            }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<GovernanceKpiSummary> GetGmsaServiceAccountsAsync(string token, string baseDn, string filter)
    {
        var result = new GovernanceKpiSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.GmsaServiceAccounts.Searches[0], baseDn, filter);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new GovernanceKpiInfo
            {
                Name = GetAttr(i, "name"),
                Dn = GetAttr(i, "distinguishedName"),
                Guid = GetAttr(i, "objectGuid")
            }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<GovernanceKpiSummary> GetSmsaServiceAccountsAsync(string token, string baseDn, string filter)
    {
        var result = new GovernanceKpiSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.SmsaServiceAccounts.Searches[0], baseDn, filter);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new GovernanceKpiInfo
            {
                Name = GetAttr(i, "name"),
                Dn = GetAttr(i, "distinguishedName"),
                Guid = GetAttr(i, "objectGuid")
            }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<GovernanceKpiSummary> GetUserAccountExpiredAsync(string token, string baseDn, string filter)
    {
        var result = new GovernanceKpiSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.ExpiredUsers.Searches[0], baseDn, filter);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new GovernanceKpiInfo
            {
                Name = GetAttr(i, "name"),
                Dn = GetAttr(i, "distinguishedName"),
                Guid = GetAttr(i, "objectGuid")
            }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<GovernanceKpiSummary> GetUserAccountLockedOutAsync(string token, string baseDn, string filter)
    {        var result = new GovernanceKpiSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.UserAccountLockedOut.Searches[0], baseDn, filter);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new GovernanceKpiInfo
            {
                Name = GetAttr(i, "name"),
                Dn = GetAttr(i, "distinguishedName"),
                Guid = GetAttr(i, "objectGuid")
            }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<GovernanceKpiSummary> GetEmptyGroupsAsync(string token, string baseDn, string filter)
    {
        var result = new GovernanceKpiSummary();
        try
        {
            var items = await ExecuteKpiSearchAsync(token, KpiInfo.EmptyGroups.Searches[0], baseDn, filter);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new GovernanceKpiInfo
            {
                Name = GetAttr(i, "name"),
                Domain = GetAttr(i, "edsaDomainNetbiosName"),
                Dn = GetAttr(i, "distinguishedName"),
                Guid = GetAttr(i, "objectGuid")
            }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<PrivilegedGroupSummary> GetAccountOperatorsAsync(string token)
    {
        return await GetPrivilegedGroupMembersAsync(token, "Account Operators");
    }

    public async Task<PrivilegedGroupSummary> GetAdministratorsAsync(string token)
    {
        return await GetPrivilegedGroupMembersAsync(token, "Administrators");
    }

    public async Task<PrivilegedGroupSummary> GetBackupOperatorsAsync(string token)
    {
        return await GetPrivilegedGroupMembersAsync(token, "Backup Operators");
    }

    public async Task<PrivilegedGroupSummary> GetDomainAdminsAsync(string token)
    {
        return await GetPrivilegedGroupMembersAsync(token, "Domain Admins");
    }

    public async Task<PrivilegedGroupSummary> GetServerOperatorsAsync(string token)
    {
        return await GetPrivilegedGroupMembersAsync(token, "Server Operators");
    }

    public async Task<PrivilegedGroupSummary> GetEnterpriseAdminsAsync(string token)
    {
        return await GetPrivilegedGroupMembersAsync(token, "Enterprise Admins");
    }

    public async Task<PrivilegedGroupSummary> GetSchemaAdminsAsync(string token)
    {
        return await GetPrivilegedGroupMembersAsync(token, "Schema Admins");
    }

    public async Task<GovernanceKpiSummary> GetInfrastructureKpiAsync(string token, KpiInfo kpi)
    {
        var result = new GovernanceKpiSummary();
        try
        {
            var config = _configMonitor.CurrentValue;
            var search = kpi.Searches[0];
            var baseDn = search.ResolveBaseDn(config);
            var filter = search.ResolveFilter(config);
            var attributes = search.ResolveAttributes(config);
            var items = await SearchObjectsAsync(token, baseDn, filter, "sub", attributes);
            result.TotalCount = items.Count;
            result.Items = items.Select(i => new GovernanceKpiInfo
            {
                Name = GetAttr(i, "name"),
                Domain = GetAttr(i, "edsaDomainNetbiosName"),
                Dn = GetAttr(i, "distinguishedName")
            }).ToList();
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    public async Task<PrivilegedGroupSummary> GetActiveRolesAdminsAsync(string token, string baseDn, string filter)
    {
        return await GetPrivilegedGroupMembersAsync(token, baseDn, filter);
    }

    public async Task<bool> IsUserActiveRolesAdminAsync(string token, string username)
    {
        try
        {
            var config = _configMonitor.CurrentValue;
            var baseDn = config.DefaultActiveDirectoryDN;
            var filter = config.DefaultActiveRolesAdminsFilter;
            var admins = await GetPrivilegedGroupMembersAsync(token, baseDn, filter);

            _logger.LogInformation("IsUserActiveRolesAdminAsync: Admin group members ({Count}): {Members}",
                admins.Items.Count,
                string.Join("; ", admins.Items.Select(m => $"{m.Name} [{m.Dn}] ({m.MembershipType})")));

            if (admins.Error != null || admins.Items.Count == 0)
            {
                _logger.LogWarning("IsUserActiveRolesAdminAsync: No admins found or error: {Error}", admins.Error ?? "Empty list");
                return false;
            }

            // Extract the bare username (remove domain prefix if present)
            var name = username;
            var slashIndex = name.IndexOf('\\');
            if (slashIndex >= 0) name = name[(slashIndex + 1)..];
            var atIndex = name.IndexOf('@');
            if (atIndex >= 0) name = name[..atIndex];

            // Look up the user's DN by sAMAccountName and compare against member DNs
            var userSearch = await SearchObjectsAsync(token, baseDn,
                $"(&(objectClass=user)(sAMAccountName={name}))", "sub", "distinguishedName");
            if (userSearch.Count > 0)
            {
                var userDn = GetAttr(userSearch[0], "distinguishedName");
                _logger.LogInformation("IsUserActiveRolesAdminAsync: User '{Username}' resolved to DN: {UserDn}", name, userDn);

                if (!string.IsNullOrEmpty(userDn))
                {
                    var normalizedUserDn = NormalizeAdDn(userDn);
                    var isAdmin = admins.Items.Any(m =>
                        NormalizeAdDn(m.Dn).Equals(normalizedUserDn, StringComparison.OrdinalIgnoreCase));

                    _logger.LogInformation("IsUserActiveRolesAdminAsync: Normalized user DN: {NormalizedDn}, IsAdmin: {IsAdmin}",
                        normalizedUserDn, isAdmin);

                    return isAdmin;
                }
            }
            else
            {
                _logger.LogWarning("IsUserActiveRolesAdminAsync: No user found for sAMAccountName '{Username}'", name);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IsUserActiveRolesAdminAsync: Exception while checking admin status for '{Username}'", username);
            return false;
        }
    }

    /// <summary>
    /// Strips Active Roles virtual tree suffixes from a DN so that DNs from different
    /// API contexts can be compared reliably.
    /// E.g. "CN=user,OU=Users,DC=domain,DC=com,CN=domain.com,CN=Active Directory"
    /// becomes "CN=user,OU=Users,DC=domain,DC=com"
    /// </summary>
    private static string NormalizeAdDn(string dn)
    {
        if (string.IsNullOrEmpty(dn)) return dn;

        // Find the first ",CN=" segment after a "DC=" segment — that indicates
        // the start of the Active Roles virtual tree namespace suffix.
        var dcIndex = dn.IndexOf(",DC=", StringComparison.OrdinalIgnoreCase);
        if (dcIndex < 0) return dn;

        // Walk past all DC= components to find where the virtual tree suffix begins
        var pos = dcIndex;
        while (pos < dn.Length)
        {
            // Find the next comma-separated component
            var nextComma = dn.IndexOf(',', pos + 1);
            if (nextComma < 0) break;

            var nextSegment = dn[(nextComma + 1)..];
            if (nextSegment.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            {
                pos = nextComma;
            }
            else if (nextSegment.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                // Non-DC component after DCs — this is the virtual tree suffix
                return dn[..nextComma];
            }
            else
            {
                break;
            }
        }

        return dn;
    }

    private async Task<PrivilegedGroupSummary> GetPrivilegedGroupMembersAsync(string token, string groupName)
    {
        var config = _configMonitor.CurrentValue;
        return await GetPrivilegedGroupMembersAsync(token, config.DefaultActiveDirectoryDN, $"(&(objectClass=group)(name={groupName}))");
    }

    private async Task<PrivilegedGroupSummary> GetPrivilegedGroupMembersAsync(string token, string baseDn, string filter)
    {
        var result = new PrivilegedGroupSummary();
        try
        {
            // Search for the group using the provided base DN and filter
            var groups = await SearchObjectsAsync(token, baseDn,
                filter, "sub", "distinguishedName,edsaDomainNetbiosName,edsaMember,edsaMemberIndirect");

            var allMembers = new List<PrivilegedGroupMemberInfo>();

            foreach (var group in groups)
            {
                var domain = GetAttr(group, "edsaDomainNetbiosName");

                // Capture the privileged group's own identity for launching the nested membership tree.
                if (string.IsNullOrEmpty(result.GroupDn))
                {
                    var groupDn = GetAttr(group, "distinguishedName");
                    if (!string.IsNullOrEmpty(groupDn))
                    {
                        result.GroupDn = groupDn;
                        result.GroupName = ExtractCnFromDn(groupDn);
                    }
                }

                // Parse direct members from edsaMember
                var directMembers = GetMultiValuedAttr(group, "edsaMember");
                foreach (var dn in directMembers)
                {
                    allMembers.Add(new PrivilegedGroupMemberInfo
                    {
                        Name = ExtractCnFromDn(dn),
                        Domain = domain,
                        Dn = dn,
                        MembershipType = "Direct"
                    });
                }

                // Parse indirect members from edsaMemberIndirect
                var indirectMembers = GetMultiValuedAttr(group, "edsaMemberIndirect");
                foreach (var dn in indirectMembers)
                {
                    // Skip if already listed as direct member
                    if (!allMembers.Any(m => m.Dn.Equals(dn, StringComparison.OrdinalIgnoreCase)))
                    {
                        allMembers.Add(new PrivilegedGroupMemberInfo
                        {
                            Name = ExtractCnFromDn(dn),
                            Domain = domain,
                            Dn = dn,
                            MembershipType = "Indirect"
                        });
                    }
                }
            }

            result.Items = allMembers;
            result.TotalCount = allMembers.Count;
        }
        catch (Exception ex) { result.Error = $"No data ({ex.GetType().Name}: {ex.Message})"; }
        return result;
    }

    private static List<string> GetMultiValuedAttr(JsonElement element, string name)
    {
        var results = new List<string>();
        JsonElement val = default;

        if (element.TryGetProperty(name, out val) ||
            (element.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty(name, out val)))
        {
            if (val.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in val.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrEmpty(s))
                        results.Add(s);
                }
            }
            else if (val.ValueKind == JsonValueKind.String)
            {
                var s = val.GetString();
                if (!string.IsNullOrEmpty(s))
                    results.Add(s);
            }
        }

        return results;
    }

    private static string ExtractCnFromDn(string dn)
    {
        if (string.IsNullOrEmpty(dn)) return "";
        // Extract the first CN= value from a DN
        if (dn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            var end = dn.IndexOf(',');
            return end > 3 ? dn[3..end] : dn[3..];
        }
        return dn;
    }

    private static ManagedObjectDataPoint? ParseStatisticsRun(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            var ns = doc.Root?.GetDefaultNamespace() ?? System.Xml.Linq.XNamespace.None;
            var root = doc.Root;
            if (root == null) return null;

            var dataPoint = new ManagedObjectDataPoint();

            var runTimeAttr = root.Attribute("runTime");
            if (runTimeAttr != null && long.TryParse(runTimeAttr.Value, out var fileTime))
                dataPoint.RunTime = DateTime.FromFileTimeUtc(fileTime);

            dataPoint.ServiceName = root.Attribute("serviceName")?.Value ?? "";

            var allItems = new List<ManagedObjectItem>();

            foreach (var domain in root.Descendants(ns + "Domain"))
            {
                allItems.Add(new ManagedObjectItem
                {
                    DisplayName = domain.Element(ns + "DisplayName")?.Value ?? "",
                    Category = "Domain",
                    Count = int.TryParse(domain.Element(ns + "Count")?.Value, out var c) ? c : 0
                });
            }

            foreach (var partition in root.Descendants(ns + "Partition"))
            {
                allItems.Add(new ManagedObjectItem
                {
                    DisplayName = partition.Element(ns + "DisplayName")?.Value ?? "",
                    Category = "Partition",
                    Count = int.TryParse(partition.Element(ns + "Count")?.Value, out var c) ? c : 0
                });
            }

            foreach (var azure in root.Descendants(ns + "AzureObject"))
            {
                allItems.Add(new ManagedObjectItem
                {
                    DisplayName = azure.Element(ns + "DisplayName")?.Value ?? "",
                    Category = "Azure",
                    Count = int.TryParse(azure.Element(ns + "Count")?.Value, out var c) ? c : 0
                });
            }

            foreach (var saas in root.Descendants(ns + "SAASObject"))
            {
                allItems.Add(new ManagedObjectItem
                {
                    DisplayName = saas.Element(ns + "DisplayName")?.Value ?? "",
                    Category = "SAAS",
                    Count = int.TryParse(saas.Element(ns + "Count")?.Value, out var c) ? c : 0
                });
            }

            dataPoint.Items = allItems;
            return dataPoint;
        }
        catch
        {
            return null;
        }
    }

    public async Task<JsonDocument?> GetObjectDetailsAsync(string token, string objectGuid, string? attributes = null)
    {
        try
        {
            var client = CreateClient(token);
            var url = $"{BaseUrl}/objects/{objectGuid}";
            if (!string.IsNullOrEmpty(attributes)) url += "?" + BuildAttributesQuery(attributes);
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
        catch { return null; }
    }

    public async Task<JsonDocument?> GetChildrenAsync(string token, string objectGuid)
    {
        try
        {
            var client = CreateClient(token);
            var response = await client.GetAsync($"{BaseUrl}/children/{objectGuid}");
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
        catch { return null; }
    }

    /// <summary>
    /// Resolves a group by exact name (or by distinguished name) to its
    /// (Name, Dn). Returns null when no matching group is found.
    /// </summary>
    public async Task<(string Name, string Dn)?> ResolveGroupAsync(string token, string nameOrDn)
    {
        if (string.IsNullOrWhiteSpace(nameOrDn)) return null;
        var config = _configMonitor.CurrentValue;

        var trimmed = nameOrDn.Trim();
        var escaped = EscapeLdapFilterValue(trimmed);
        var filter = $"(&(objectClass=group)(|(name={escaped})(distinguishedName={escaped})))";

        var items = await SearchObjectsAsync(token, config.DefaultActiveDirectoryDN, filter, "sub", "name,distinguishedName");
        if (items.Count == 0) return null;

        return (GetAttr(items[0], "name"), GetAttr(items[0], "distinguishedName"));
    }

    private static string EscapeLdapFilterValue(string value)
    {
        return value
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }

    /// <summary>
    /// Lazily expands a single level of a group's nested membership. Returns the
    /// direct members of <paramref name="groupDn"/>, flagging child groups so the
    /// caller can expand them on demand. Cycles (a group already present in
    /// <paramref name="ancestorDns"/>) and the depth cap are marked as terminal
    /// leaves rather than being expanded further.
    /// </summary>
    public async Task<List<GroupMemberNode>> ExpandGroupChildrenAsync(
        string token, string groupDn, int currentDepth, int maxDepth, ISet<string> ancestorDns)
    {
        var children = new List<GroupMemberNode>();
        if (string.IsNullOrWhiteSpace(groupDn)) return children;

        // Read this group's member DNs (base scope on the group object itself).
        var groupItems = await SearchObjectsAsync(token, groupDn, "(objectClass=*)", "base", "member");
        if (groupItems.Count == 0) return children;

        var memberDns = GetMultiValuedAttr(groupItems[0], "member");
        if (memberDns.Count == 0) return children;

        // Resolve all member identities in batched OR-filter queries (chunked to
        // keep each LDAP filter bounded), instead of one round-trip per member.
        var config = _configMonitor.CurrentValue;
        var resolved = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        const int batchSize = 50;

        foreach (var chunk in memberDns.Chunk(batchSize))
        {
            var clauses = string.Concat(chunk.Select(dn => $"(distinguishedName={EscapeLdapFilterValue(dn)})"));
            var filter = $"(&(objectClass=*)(|{clauses}))";
            var items = await SearchObjectsAsync(
                token, config.DefaultActiveDirectoryDN, filter, "sub", "name,distinguishedName,objectClass");

            foreach (var item in items)
            {
                var dn = GetAttr(item, "distinguishedName");
                if (!string.IsNullOrEmpty(dn))
                    resolved[dn] = item;
            }
        }

        foreach (var memberDn in memberDns)
        {
            if (!resolved.TryGetValue(memberDn, out var m))
            {
                // Dangling / unresolvable member - surface it as a leaf.
                children.Add(new GroupMemberNode
                {
                    Name = DnToName(memberDn),
                    Dn = memberDn,
                    IsGroup = false,
                    Depth = currentDepth + 1
                });
                continue;
            }

            var classes = GetMultiValuedAttr(m, "objectClass");
            var isGroup = classes.Contains("group", StringComparer.OrdinalIgnoreCase);

            var node = new GroupMemberNode
            {
                Name = GetAttr(m, "name"),
                Dn = GetAttr(m, "distinguishedName"),
                IsGroup = isGroup,
                Depth = currentDepth + 1
            };

            if (isGroup)
            {
                if (ancestorDns.Contains(node.Dn))
                    node.CycleReference = true;
                else if (currentDepth + 1 >= maxDepth)
                    node.DepthLimitReached = true;
                // else: leave Children == null so the UI can lazy-load on click.
            }

            children.Add(node);
        }

        // Groups first, then alphabetical for readable tree ordering.
        return children
            .OrderByDescending(c => c.IsGroup)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string DnToName(string dn)
    {
        if (string.IsNullOrEmpty(dn)) return dn;
        var first = dn.Split(',', 2)[0];
        var eq = first.IndexOf('=');
        return eq >= 0 ? first[(eq + 1)..] : first;
    }

    private async Task<List<JsonElement>> SearchObjectsAsync(string token, string baseDn, string filter, string scope, string attributes)
    {
        var client = CreateClient(token);
        var url = $"{BaseUrl}/objects?base={EscapeAmpersand(baseDn)}&filter={EscapeAmpersand(filter)}&scope={scope}&{BuildAttributesQuery(attributes)}";
        var allItems = new List<JsonElement>();

        while (url != null)
        {
            _logger.LogInformation("ActiveRoles API request: {Url}", url);
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            // The full response body is verbose (whole membership lists, etc.) and clutters
            // the log at Information level. Emit it only at Debug so it's opt-in via the
            // ActiveRolesDashboard.Services.ActiveRolesService log level.
            _logger.LogDebug("ActiveRoles API response: {Body}", body);

            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                    allItems.Add(item.Clone());
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                    allItems.Add(item.Clone());
            }

            if (doc.RootElement.TryGetProperty("nextPage", out var nextPage) && nextPage.ValueKind == JsonValueKind.String)
            {
                var pageToken = nextPage.GetString();
                url = $"{BaseUrl}/objects?nextPage={pageToken}";
            }
            else
            {
                url = null;
            }
        }

        return allItems;
    }

    private static string BuildAttributesQuery(string attributes)
    {
        var attrs = attributes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Always request the per-object permission-scoping attributes so every collected item
        // carries its effective Access Template Link GUIDs and structural class for the per-user
        // visibility gate (PermissionScope.IsVisibleTo). Deduped case-insensitively so callers that
        // already ask for them don't produce duplicate query params.
        foreach (var scoped in new[] { SegmentAttributes.EffectiveLinksAttribute, SegmentAttributes.ClassAttribute })
        {
            if (!attrs.Any(a => string.Equals(a, scoped, StringComparison.OrdinalIgnoreCase)))
                attrs.Add(scoped);
        }

        return string.Join("&", attrs.Select(a => $"attributes={EscapeAmpersand(a)}"));
    }

    private static string EscapeAmpersand(string value)
    {
        return value.Replace("&", "%26");
    }

    private static string GetAttr(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var val))
            return val.ValueKind == JsonValueKind.String ? val.GetString() ?? "" : val.GetRawText();
        if (element.TryGetProperty("attributes", out var attrs) && attrs.TryGetProperty(name, out var attrVal))
            return attrVal.ValueKind == JsonValueKind.String ? attrVal.GetString() ?? "" : attrVal.GetRawText();
        return "";
    }

    private static bool IsAccountDisabled(string userAccountControl)
    {
        if (int.TryParse(userAccountControl, out var uac))
            return (uac & 0x0002) != 0;
        return false;
    }

    private static int CountApeRules(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return 0;
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            return doc.Descendants("APE").Count();
        }
        catch
        {
            return 0;
        }
    }

    private static string GetFriendlyOSName(string operatingSystem, string operatingSystemVersion)
    {
        bool isServer = operatingSystem.Contains("server", StringComparison.OrdinalIgnoreCase);

        if (isServer)
        {
            return operatingSystemVersion switch
            {
                "6.1" => "Windows Server 2008 R2",
                "6.3" => "Windows Server 2012 R2",
                "10.0 (14393)" => "Windows Server 2016",
                "10.0 (17763)" => "Windows Server 2019",
                "10.0 (20348)" => "Windows Server 2022",
                "10.0 (26100)" => "Windows Server 2025",
                _ => operatingSystem
            };
        }
        else
        {
            return operatingSystemVersion switch
            {
                "6.1" => "Windows 7",
                "6.3" => "Windows 8.1",
                "10.0 (19045)" => "Windows 10 22H2",
                "10.0 (22621)" => "Windows 11 22H2",
                "10.0 (22631)" => "Windows 11 23H2",
                "10.0 (26200)" => "Windows 11 Enterprise",
                "10.0 (26100)" => "Windows 11 Pro",
                _ => operatingSystem
            };
        }
    }
}

