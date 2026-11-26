using System.Security.AccessControl;
using System.Security.Principal;

namespace ActiveRolesDashboard.Services.PermissionTracker;

/// <summary>One access control entry from an Active Directory object's DACL, in readable form.</summary>
public sealed record AdPermissionEntry(
    string TrusteeSid,
    bool IsAllow,
    bool IsInherited,
    int AccessMask,
    string Rights,
    Guid? ObjectType,
    string? ObjectTypeName,
    Guid? InheritedObjectType,
    string? InheritedObjectTypeName,
    string AppliesTo,
    bool IsWellKnownTrustee,
    bool IsHighRisk,
    bool IsDefault = false);

/// <summary>
/// Turns a binary/SDDL nTSecurityDescriptor (as returned by the Active Roles REST API) into
/// <see cref="AdPermissionEntry"/> rows. Trustee display names are resolved by the caller.
/// </summary>
public static class AdAceParser
{
    // ADS_RIGHT_* bits.
    private const int CreateChild = 0x1, DeleteChild = 0x2, ListChildren = 0x4, Self = 0x8,
        ReadProperty = 0x10, WriteProperty = 0x20, DeleteTree = 0x40, ListObject = 0x80,
        ControlAccess = 0x100, Delete = 0x10000, ReadControl = 0x20000, WriteDacl = 0x40000,
        WriteOwner = 0x80000, GenericAll = 0x10000000, GenericExecute = 0x20000000,
        GenericWrite = 0x40000000, GenericRead = unchecked((int)0x80000000);

    private const int FullControlMask = 0xF01FF;

    private static readonly (int Bit, string Name)[] RightNames =
    [
        (CreateChild, "Create child"), (DeleteChild, "Delete child"), (ListChildren, "List contents"),
        (Self, "Validated write"), (ReadProperty, "Read property"), (WriteProperty, "Write property"),
        (DeleteTree, "Delete tree"), (ListObject, "List object"), (ControlAccess, "Extended right"),
        (Delete, "Delete"), (ReadControl, "Read permissions"), (WriteDacl, "Modify permissions"),
        (WriteOwner, "Modify owner"), (GenericAll, "Generic all"), (GenericExecute, "Generic execute"),
        (GenericWrite, "Generic write"), (GenericRead, "Generic read")
    ];

    /// <summary>Well-known schema classes, attributes, property sets and extended rights.</summary>
    public static readonly IReadOnlyDictionary<Guid, string> KnownGuids = new Dictionary<Guid, string>
    {
        [new("bf967aba-0de6-11d0-a285-00aa003049e2")] = "user",
        [new("bf967a9c-0de6-11d0-a285-00aa003049e2")] = "group",
        [new("bf967a86-0de6-11d0-a285-00aa003049e2")] = "computer",
        [new("bf967aa5-0de6-11d0-a285-00aa003049e2")] = "organizationalUnit",
        [new("4828cc14-1437-45bc-9b07-ad6f015e5f28")] = "inetOrgPerson",
        [new("bf967a8b-0de6-11d0-a285-00aa003049e2")] = "container",
        [new("5cb41ed0-0e4c-11d0-a286-00aa003049e2")] = "contact",
        [new("f30e3bc2-9ff0-11d1-b603-0000f80367c1")] = "groupPolicyContainer",
        [new("ce206244-5827-4a86-ba1c-1c0c386c1b64")] = "msDS-ManagedServiceAccount",
        [new("7b8b558a-93a5-4af7-adca-c017e67f1057")] = "msDS-GroupManagedServiceAccount",
        [new("bf9679c0-0de6-11d0-a285-00aa003049e2")] = "member",
        [new("bf967950-0de6-11d0-a285-00aa003049e2")] = "description",
        [new("bf967a0a-0de6-11d0-a285-00aa003049e2")] = "pwdLastSet",
        [new("28630ebf-41d5-11d1-a9c1-0000f80367c1")] = "lockoutTime",
        [new("bf967a68-0de6-11d0-a285-00aa003049e2")] = "userAccountControl",
        [new("f3a64788-5306-11d1-a9c5-0000f80367c1")] = "servicePrincipalName",
        [new("5b47d60f-6090-40b2-9f37-2a4de88f3063")] = "msDS-KeyCredentialLink",
        [new("3f78c3e5-f79a-46bd-a0b8-9d18116ddc79")] = "msDS-AllowedToActOnBehalfOfOtherIdentity",
        [new("800d94d7-b7a1-42a1-b14d-7cae1423d07f")] = "msDS-AllowedToDelegateTo",
        [new("bf967953-0de6-11d0-a285-00aa003049e2")] = "displayName",
        [new("f0f8ff9a-1191-11d0-a060-00aa006c33ed")] = "scriptPath",
        [new("bf9679a8-0de6-11d0-a285-00aa003049e2")] = "gPLink",
        [new("bf96793f-0de6-11d0-a285-00aa003049e2")] = "cn",
        [new("4c164200-20c0-11d0-a768-00aa006e0529")] = "User Account Restrictions",
        [new("5f202010-79a5-11d0-9020-00c04fc2d4cf")] = "Logon Information",
        [new("bc0ac240-79a9-11d0-9020-00c04fc2d4cf")] = "Group Membership",
        [new("59ba2f42-79a2-11d0-9020-00c04fc2d3cf")] = "General Information",
        [new("e45795b2-9455-11d1-aebd-0000f80367c1")] = "Phone and Mail Options",
        [new("e45795b3-9455-11d1-aebd-0000f80367c1")] = "Web Information",
        [new("77b5b886-944a-11d1-aebd-0000f80367c1")] = "Personal Information",
        [new("e48d0154-bcf8-11d1-8702-00c04fb96050")] = "Public Information",
        [new("037088f8-0ae1-11d2-b422-00a0c968f939")] = "RAS Information",
        [new("91e647de-d96f-4b70-9557-d63ff4f3ccd8")] = "Private Information",
        [new("72e39547-7b18-11d1-adef-00c04fd8d5cd")] = "DNS Host Name Attributes",
        [new("b8119fd0-04f6-4762-ab7a-4986c76b3f9a")] = "Other Domain Parameters",
        [new("c7407360-20bf-11d0-a768-00aa006e0529")] = "Domain Password & Lockout Policies",
        [new("e2a36dc9-ae17-47c3-b58b-be34c55ba633")] = "Create Inbound Forest Trust",
        [new("00299570-246d-11d0-a768-00aa006e0529")] = "Reset Password",
        [new("ab721a53-1e2f-11d0-9819-00aa0040529b")] = "Change Password",
        [new("ab721a54-1e2f-11d0-9819-00aa0040529b")] = "Send As",
        [new("ab721a56-1e2f-11d0-9819-00aa0040529b")] = "Receive As",
        [new("1131f6aa-9c07-11d1-f79f-00c04fc2dcd2")] = "DS-Replication-Get-Changes",
        [new("1131f6ad-9c07-11d1-f79f-00c04fc2dcd2")] = "DS-Replication-Get-Changes-All",
        [new("89e95b76-444d-4c62-991a-0facbeda640c")] = "DS-Replication-Get-Changes-In-Filtered-Set",
        [new("1131f6ab-9c07-11d1-f79f-00c04fc2dcd2")] = "DS-Replication-Synchronize",
        [new("1131f6ac-9c07-11d1-f79f-00c04fc2dcd2")] = "DS-Replication-Manage-Topology",
        [new("1131f6ae-9c07-11d1-f79f-00c04fc2dcd2")] = "Read-Only-Replication-Secret-Synchronization",
        [new("edacfd8f-ffb3-11d1-b41d-00a0c968f939")] = "Apply Group Policy",
        [new("9923a32a-3607-11d2-b9be-0000f87a36b2")] = "DS-Install-Replica",
        [new("280f369c-67c7-438e-ae98-1d46f3c6f541")] = "Update-Password-Not-Required-Bit",
        [new("ccc2dc7d-a6ad-4a7a-8846-c04e3cc53501")] = "Unexpire-Password",
        [new("05c74c5e-4deb-43b4-bd9f-86664c2a7fd5")] = "Enable-Per-User-Reversibly-Encrypted-Password",
        [new("ba33815a-4f93-4c76-87f3-57574bff8109")] = "Migrate-SID-History",
        [new("0e10c968-78fb-11d2-90d4-00c04f79dc55")] = "Certificate-Enrollment",
        [new("a05b8cc2-17bc-4802-a710-e7c15ab866a2")] = "Certificate-AutoEnrollment",
        [new("b7b1b3dd-ab09-4242-9e30-9980e5d322f7")] = "Generate Resultant Set of Policy (Planning)",
        [new("b7b1b3de-ab09-4242-9e30-9980e5d322f7")] = "Generate Resultant Set of Policy (Logging)",
        [new("f98340fb-7c5b-4cdb-a00b-2ebdfa115a96")] = "DS-Validated-Write-Computer",
        [new("bf967a7f-0de6-11d0-a285-00aa003049e2")] = "userCertificate",
        [new("6db69a1c-9422-11d1-aebd-0000f80367c1")] = "terminalServer",
        [new("5805bc62-bdc9-4428-a5e2-856a0f4c185e")] = "Terminal Server License Server",
        [new("b7c69e6d-2cc7-11d2-854e-00a0c983f608")] = "tokenGroups",
        [new("46a9b11d-60ae-405a-b7e8-ff8a58d456d2")] = "tokenGroupsGlobalAndUniversal"
    };

    /// <summary>Extended rights that enable credential theft, impersonation or domain takeover.</summary>
    private static readonly HashSet<Guid> DangerousExtendedRights =
    [
        new("00299570-246d-11d0-a768-00aa006e0529"),
        new("1131f6ad-9c07-11d1-f79f-00c04fc2dcd2"),
        new("1131f6aa-9c07-11d1-f79f-00c04fc2dcd2"),
        new("89e95b76-444d-4c62-991a-0facbeda640c"),
        new("ba33815a-4f93-4c76-87f3-57574bff8109")
    ];

    /// <summary>Attributes/property sets whose write access enables takeover of the target object.</summary>
    private static readonly HashSet<Guid> DangerousWriteProperties =
    [
        new("bf9679c0-0de6-11d0-a285-00aa003049e2"),
        new("5b47d60f-6090-40b2-9f37-2a4de88f3063"),
        new("3f78c3e5-f79a-46bd-a0b8-9d18116ddc79"),
        new("f3a64788-5306-11d1-a9c5-0000f80367c1"),
        new("bf9679a8-0de6-11d0-a285-00aa003049e2"),
        new("f0f8ff9a-1191-11d0-a060-00aa006c33ed"),
        new("800d94d7-b7a1-42a1-b14d-7cae1423d07f"),
        new("bc0ac240-79a9-11d0-9020-00c04fc2d4cf")
    ];

    /// <summary>Parses the DACL of <paramref name="sd"/> into readable entries; returns an empty list when there is no DACL.</summary>
    public static IReadOnlyList<AdPermissionEntry> Parse(RawSecurityDescriptor? sd, IReadOnlyDictionary<Guid, string>? extraGuidNames = null)
    {
        var result = new List<AdPermissionEntry>();
        if (sd?.DiscretionaryAcl == null) return result;

        foreach (var ace in sd.DiscretionaryAcl)
        {
            if (ace is not KnownAce known) continue;

            bool isAllow;
            Guid? objectType = null, inheritedType = null;
            switch (ace)
            {
                case ObjectAce oa:
                    isAllow = oa.AceQualifier == AceQualifier.AccessAllowed;
                    if (oa.AceQualifier is not (AceQualifier.AccessAllowed or AceQualifier.AccessDenied)) continue;
                    if ((oa.ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent) != 0) objectType = oa.ObjectAceType;
                    if ((oa.ObjectAceFlags & ObjectAceFlags.InheritedObjectAceTypePresent) != 0) inheritedType = oa.InheritedObjectAceType;
                    break;
                case CommonAce ca:
                    if (ca.AceQualifier is not (AceQualifier.AccessAllowed or AceQualifier.AccessDenied)) continue;
                    isAllow = ca.AceQualifier == AceQualifier.AccessAllowed;
                    break;
                default:
                    continue;
            }

            var sid = known.SecurityIdentifier.Value;
            var wellKnown = IsWellKnownTrustee(known.SecurityIdentifier);
            result.Add(new AdPermissionEntry(
                sid,
                isAllow,
                (ace.AceFlags & AceFlags.Inherited) != 0,
                known.AccessMask,
                DescribeRights(known.AccessMask),
                objectType,
                NameOf(objectType, extraGuidNames),
                inheritedType,
                NameOf(inheritedType, extraGuidNames),
                DescribeAppliesTo(ace.AceFlags, inheritedType, extraGuidNames),
                wellKnown,
                isAllow && !wellKnown && IsDangerous(known.AccessMask, objectType)));
        }

        return result;
    }

    public static string DescribeRights(int mask)
    {
        if ((mask & FullControlMask) == FullControlMask || (mask & GenericAll) != 0) return "Full control";
        var names = RightNames.Where(r => (mask & r.Bit) != 0).Select(r => r.Name).ToList();
        return names.Count == 0 ? $"0x{mask:X}" : string.Join(", ", names);
    }

    public static string? NameOf(Guid? guid, IReadOnlyDictionary<Guid, string>? extra = null)
    {
        if (guid is not { } g) return null;
        if (KnownGuids.TryGetValue(g, out var name)) return name;
        if (extra != null && extra.TryGetValue(g, out name)) return name;
        return g.ToString();
    }

    public static string DescribeAppliesTo(AceFlags flags, Guid? inheritedType, IReadOnlyDictionary<Guid, string>? extra = null)
    {
        var inherits = (flags & AceFlags.ContainerInherit) != 0;
        var inheritOnly = (flags & AceFlags.InheritOnly) != 0;
        if (inheritedType != null)
            return $"Descendant {NameOf(inheritedType, extra)} objects";
        if (!inherits) return "This object only";
        return inheritOnly ? "All descendant objects" : "This object and all descendant objects";
    }

    public static bool IsDangerous(int mask, Guid? objectType)
    {
        if ((mask & (GenericAll | GenericWrite | WriteDacl | WriteOwner)) != 0) return true;
        if ((mask & FullControlMask) == FullControlMask) return true;
        if ((mask & ControlAccess) != 0 && (objectType == null || DangerousExtendedRights.Contains(objectType.Value))) return true;
        if ((mask & WriteProperty) != 0 && (objectType == null || DangerousWriteProperties.Contains(objectType.Value))) return true;
        if ((mask & Self) != 0 && objectType == new Guid("bf9679c0-0de6-11d0-a285-00aa003049e2")) return true;
        return false;
    }

    /// <summary>
    /// Built-in / system principals that hold permissions by design (SYSTEM, Enterprise DCs, Domain
    /// Admins, Enterprise Admins, Administrators, SELF, Authenticated Users read, etc.).
    /// </summary>
    public static bool IsWellKnownTrustee(SecurityIdentifier sid)
    {
        if (sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
            || sid.IsWellKnown(WellKnownSidType.SelfSid)
            || sid.IsWellKnown(WellKnownSidType.CreatorOwnerSid)
            || sid.IsWellKnown(WellKnownSidType.EnterpriseControllersSid)
            || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
            || sid.IsWellKnown(WellKnownSidType.BuiltinAccountOperatorsSid)
            || sid.IsWellKnown(WellKnownSidType.BuiltinSystemOperatorsSid)
            || sid.IsWellKnown(WellKnownSidType.BuiltinPrintOperatorsSid))
            return true;

        // Domain-relative RIDs: Domain Admins 512, Domain Controllers 516, Schema Admins 518,
        // Enterprise Admins 519, Cert Publishers 517, Enterprise Key Admins 527, Key Admins 526,
        // Read-only Domain Controllers 521, Enterprise Read-only DCs 498.
        var value = sid.Value;
        if (value.StartsWith("S-1-5-21-", StringComparison.Ordinal))
        {
            var rid = value[(value.LastIndexOf('-') + 1)..];
            return rid is "512" or "516" or "517" or "518" or "519" or "521" or "526" or "527" or "498";
        }
        return value is "S-1-5-32-560" or "S-1-5-32-561";
    }
}
