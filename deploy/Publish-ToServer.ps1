<#
.SYNOPSIS
	Publishes the Active Roles Dashboard and deploys it to a target IIS server,
	preserving machine-specific configuration and keys.

.DESCRIPTION
	Repeatable deploy for multi-user testing. Steps:
	  1. dotnet publish (Release, framework-dependent — target needs the
		 ASP.NET Core 9 Hosting Bundle).
	  2. Stop the IIS app pool on the target to release locked DLLs.
	  3. Robocopy the publish output to the target, EXCLUDING:
		   - appsettings.json  (holds the target's service-account config +
			 machine-encrypted ProtectedPassword)
		   - App_Data\         (holds the Data Protection key ring; these keys
			 decrypt ProtectedPassword and must NOT be overwritten)
	  4. Restart the app pool.

	On the FIRST deploy to a new machine, appsettings.json will not yet exist on
	the target. Pass -IncludeAppSettings to copy it once, then set the
	ServiceAccount Username/ProtectedPassword on the target (run the app with
	--protect-secret ON THE TARGET to generate a value valid for its keys).

.PARAMETER Target
	Target machine name (mandatory), e.g. winemeaapp04.

.PARAMETER RemotePath
	UNC path to the site folder on the target.
	Default: \\<Target>\C$\inetpub\wwwroot\ActiveRolesDashboard

.PARAMETER AppPool
	IIS application pool name to stop/start on the target (mandatory).

.PARAMETER IncludeAppSettings
	Also copy appsettings.json (use only on first deploy / when intentionally
	updating the target's config).

.EXAMPLE
	# Normal repeat deploy (preserves target config + keys):
	.\deploy\Publish-ToServer.ps1 -Target winemeaapp04 -AppPool ActiveRolesDashboard

.EXAMPLE
	# First-time deploy, seeding appsettings.json:
	.\deploy\Publish-ToServer.ps1 -Target winemeaapp04 -AppPool ActiveRolesDashboard -IncludeAppSettings
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[string]$Target,

	[string]$RemotePath,

	[Parameter(Mandatory = $true)]
	[string]$AppPool,

	[switch]$IncludeAppSettings
)

$ErrorActionPreference = "Stop"

# Resolve paths relative to this script (repo\deploy\ -> repo root).
$repoRoot   = Split-Path -Parent $PSScriptRoot
$project    = Join-Path $repoRoot "ActiveRolesDashboard.csproj"
$publishDir = Join-Path $repoRoot "publish\$Target"

if (-not $RemotePath) {
	$RemotePath = "\\$Target\C$\inetpub\wwwroot\ActiveRolesDashboard"
}

Write-Host "==> Publishing (Release, framework-dependent)..." -ForegroundColor Cyan
dotnet publish $project -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Write-Host "==> Stopping app pool '$AppPool' on $Target..." -ForegroundColor Cyan
try {
	Invoke-Command -ComputerName $Target -ScriptBlock {
		param($pool)
		Import-Module WebAdministration -ErrorAction SilentlyContinue
		if ((Get-WebAppPoolState -Name $pool -ErrorAction SilentlyContinue).Value -eq "Started") {
			Stop-WebAppPool -Name $pool
			Start-Sleep -Seconds 2
		}
	} -ArgumentList $AppPool
} catch {
	Write-Warning "Could not stop app pool remotely ($($_.Exception.Message)). Continuing; files may be locked if the app is running."
}

# Ensure the target directory exists.
if (-not (Test-Path $RemotePath)) {
	New-Item -ItemType Directory -Path $RemotePath -Force | Out-Null
}

Write-Host "==> Copying to $RemotePath (preserving target config + keys)..." -ForegroundColor Cyan
# /MIR mirrors the tree but we ALWAYS exclude machine-specific state from the mirror so
# redeploys don't clobber the target's protected secret or Data Protection key ring:
#   - App_Data\        (Data Protection key ring)
#   - appsettings.json (target's service-account config + machine-encrypted password)
# When -IncludeAppSettings is set we copy appsettings.json explicitly AFTERWARDS, so the
# mirror's /XF exclusion and the intentional seed can't conflict.
$robocopyArgs = @($publishDir, $RemotePath, "/MIR", "/R:2", "/W:2", "/NFL", "/NDL", "/NP",
                  "/XD", "App_Data", "/XF", "appsettings.json")

robocopy @robocopyArgs
# Robocopy exit codes 0-7 are success (8+ are failures).
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE." }

if ($IncludeAppSettings) {
    $srcAppSettings = Join-Path $publishDir "appsettings.json"
    $dstAppSettings = Join-Path $RemotePath "appsettings.json"
    Copy-Item -Path $srcAppSettings -Destination $dstAppSettings -Force
    Write-Host "==> Seeded appsettings.json on the target." -ForegroundColor Cyan
    Write-Host "    IMPORTANT: the ProtectedPassword in it (if any) was encrypted on THIS machine" -ForegroundColor Yellow
    Write-Host "    and will NOT decrypt on $Target. Regenerate it there:" -ForegroundColor Yellow
    Write-Host "        dotnet ActiveRolesDashboard.dll --protect-secret `"<password>`"" -ForegroundColor Yellow
}
else {
    Write-Host "==> Preserved the target's existing appsettings.json (not overwritten)." -ForegroundColor DarkGray
    Write-Host "    Re-run with -IncludeAppSettings to seed it on a fresh machine." -ForegroundColor DarkGray
}

Write-Host "==> Starting app pool '$AppPool' on $Target..." -ForegroundColor Cyan
try {
	Invoke-Command -ComputerName $Target -ScriptBlock {
		param($pool)
		Import-Module WebAdministration -ErrorAction SilentlyContinue
		Start-WebAppPool -Name $pool
	} -ArgumentList $AppPool
} catch {
	Write-Warning "Could not start app pool remotely ($($_.Exception.Message)). Start it manually on $Target."
}

Write-Host "==> Deploy complete." -ForegroundColor Green
if ($IncludeAppSettings) {
	Write-Host "    NOTE: appsettings.json was copied. On the target, set ServiceAccount:Username and" -ForegroundColor Yellow
	Write-Host "    a ProtectedPassword generated ON THE TARGET (pass the password as an argument):" -ForegroundColor Yellow
	Write-Host "        dotnet ActiveRolesDashboard.dll --protect-secret `"<password>`"" -ForegroundColor Yellow
}
