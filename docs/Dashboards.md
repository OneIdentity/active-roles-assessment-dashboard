# Active Roles Dashboard — Dashboards, Categories & KPIs

[← Back to README](../README.md)

This document lists the dashboards, their categories, and the KPIs each category
presents. The dashboard is split into a **main dashboard** plus four category
dashboards. The main dashboard shows a summary of key data across all categories,
while each category dashboard presents KPI cards, native charts, and
searchable/sortable drill-down tables.

Each KPI supports a drill-down panel backed by configurable LDAP filters and
attributes, and every category and KPI can be individually enabled or disabled per
user (see [ConfigSettings.md](ConfigSettings.md#visibility)).

## Contents

- [Active Roles](#active-roles)
- [Active Directory](#active-directory)
- [Entra ID](#entra-id)
- [Licensing](#licensing)

## Active Roles

Metrics on key Active Roles configuration objects.

- **Active Roles Configuration** — Information on Active Roles configuration objects (Active Roles admins only)
	- Active Roles administrators
	- Active Roles servers
	- Managed Domains
	- Access Template Links
	- Access Templates
	- Dynamic Groups
	- Group Families
	- Managed Units
	- Policy Object Links
	- Policy Objects
	- Virtual Attributes
	- Workflows

> Note: The Active Roles Configuration dashboard is only visible to users with the Active Roles administrator role.

## Active Directory

Metrics on key Active Directory objects managed by Active Roles.

- **Computers** — Computer objects in Active Directory excluding Domain Controllers
	- Clients
	- Clients (other)
	- Servers
	- Servers (other)
	- Windows 10 22H2
	- Windows 11 22H2
	- Windows 11 23H2
	- Windows 11 Enterprise
	- Windows 11 Pro
	- Windows 7
	- Windows 8.1
	- Windows Server 2008 R2
	- Windows Server 2012 R2
	- Windows Server 2016
	- Windows Server 2019
	- Windows Server 2022
	- Windows Server 2025
- **Governance and Risk** — A view of KPIs that are interesting from a governance and risk perspective
	- Empty Groups
	- Expired Users
	- Never Logged In
	- No Group Owner
	- No Manager (Service Account)
	- No Manager (User)
	- Reversible Encryption
	- User Account Locked Out
- **Groups** — Group objects in Active Directory excluding dynamic groups
	- Distribution Groups
	- Domain Local Groups
	- Empty Groups
	- Global Groups
	- Mail-Enabled Security Groups
	- No Group Owner
	- Security Groups
	- Universal Groups
- **Infrastructure** — Key Active Directory infrastructure objects
	- Domain Controllers
	- OUs
	- Site Links
	- Sites
	- Subnets
- **Privileged Groups** — Membership (direct & indirect) of administrative groups
	- Account Operators
	- Administrators
	- Backup Operators
	- Domain Admins
	- Enterprise Admins
	- Schema Admins
	- Server Operators
- **Privileged Users** — User objects that have the adminCount attribute set to 1
	- Admin Count
- **User Accounts**
	- Cannot Change Password
	- Disabled
	- Do Not Require Kerberos Pre-Authentication
	- Enabled Users
	- Expired Users
	- Expiring Users
	- Must Change Password
	- Never Logged In
	- No Manager (Service Account)
	- No Manager (User)
	- Password Never Expires
	- Password Not Required
	- Reversible Encryption
	- Sensitive - Cannot be Delegated
	- Smart Card Required
	- Trusted For Delegation
	- Use DES Encryption
	- User Account Locked Out

## Entra ID

Metrics on key Entra ID objects managed by Active Roles.

- **Entra Groups** — Microsoft Entra (Azure AD) groups managed by Active Roles
- **Entra Identity Governance** — governance KPIs derived from Entra group membership
  (empty groups, groups with no owner, single-owner groups, guest-containing groups,
  and large groups). Membership/owner data is loaded lazily in batches; KPIs that
  depend on it are marked as pending until loading completes.

## Licensing

License usage data.

- **Managed Objects** — licensing-related KPIs
