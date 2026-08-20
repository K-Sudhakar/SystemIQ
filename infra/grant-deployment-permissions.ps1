[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ResourceGroupName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $FunctionAppName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $KeyVaultName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ApiApplicationClientId,

    [ValidateSet('User', 'Group')]
    [string] $EditorPrincipalType = 'User',

    [string[]] $EditorPrincipalObjectId = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-AzCliJson {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $raw = & az @Arguments --output json
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }

    if ([string]::IsNullOrWhiteSpace(($raw -join [Environment]::NewLine))) {
        return $null
    }

    return (($raw -join [Environment]::NewLine) | ConvertFrom-Json)
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required. Install it and authenticate with az login before running this script.'
}

$account = Invoke-AzCliJson -Arguments @('account', 'show')
Write-Verbose "Using subscription '$($account.name)' and tenant '$($account.tenantId)'."

$functionPrincipalId = Invoke-AzCliJson -Arguments @(
    'functionapp', 'identity', 'show',
    '--resource-group', $ResourceGroupName,
    '--name', $FunctionAppName,
    '--query', 'principalId'
)
$functionPrincipalId = [string] $functionPrincipalId

$keyVaultId = Invoke-AzCliJson -Arguments @(
    'keyvault', 'show',
    '--resource-group', $ResourceGroupName,
    '--name', $KeyVaultName,
    '--query', 'id'
)
$keyVaultId = [string] $keyVaultId

if ($PSCmdlet.ShouldProcess($FunctionAppName, "Grant Key Vault Secrets User on $KeyVaultName")) {
    $existingKvAssignment = Invoke-AzCliJson -Arguments @(
        'role', 'assignment', 'list',
        '--assignee-object-id', $functionPrincipalId,
        '--scope', $keyVaultId,
        '--role', 'Key Vault Secrets User',
        '--query', '[0]'
    )

    if ($null -eq $existingKvAssignment) {
        Invoke-AzCliJson -Arguments @(
            'role', 'assignment', 'create',
            '--assignee-object-id', $functionPrincipalId,
            '--assignee-principal-type', 'ServicePrincipal',
            '--scope', $keyVaultId,
            '--role', 'Key Vault Secrets User'
        ) | Out-Null
    }
}

$apiApplication = Invoke-AzCliJson -Arguments @(
    'ad', 'app', 'show',
    '--id', $ApiApplicationClientId
)

$roleValue = 'DataIqGlossaryEditor'
$role = @($apiApplication.appRoles) | Where-Object { $_.value -eq $roleValue } | Select-Object -First 1

if ($null -eq $role) {
    $newRoleId = [Guid]::NewGuid().ToString()
    $newRole = [ordered]@{
        allowedMemberTypes = @('User')
        description        = 'Allows authorized curators to manage the DataIQ business glossary and feedback inbox.'
        displayName        = 'DataIQ Glossary Editor'
        id                 = $newRoleId
        isEnabled          = $true
        origin             = 'Application'
        value              = $roleValue
    }
    $roles = @($apiApplication.appRoles) + @($newRole)
    $rolesJson = $roles | ConvertTo-Json -Depth 10 -Compress

    if ($PSCmdlet.ShouldProcess($ApiApplicationClientId, "Create application role $roleValue")) {
        & az ad app update --id $ApiApplicationClientId --app-roles $rolesJson --only-show-errors
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create the $roleValue application role."
        }
    }

    $roleId = $newRoleId
}
else {
    $roleId = [string] $role.id
}

if ($EditorPrincipalObjectId.Count -gt 0) {
    $apiServicePrincipal = Invoke-AzCliJson -Arguments @(
        'ad', 'sp', 'show',
        '--id', $ApiApplicationClientId
    )

    foreach ($principalObjectId in $EditorPrincipalObjectId) {
        if ($PSCmdlet.ShouldProcess($principalObjectId, "Assign $roleValue")) {
            $assignmentBody = @{
                principalId = $principalObjectId
                resourceId  = [string] $apiServicePrincipal.id
                appRoleId   = $roleId
            } | ConvertTo-Json -Compress

            $existingAssignments = Invoke-AzCliJson -Arguments @(
                'rest',
                '--method', 'GET',
                '--url', "https://graph.microsoft.com/v1.0/$($EditorPrincipalType.ToLowerInvariant())s/$principalObjectId/appRoleAssignments?`$filter=resourceId eq $($apiServicePrincipal.id)"
            )

            $alreadyAssigned = @($existingAssignments.value) |
                Where-Object { $_.appRoleId -eq $roleId } |
                Select-Object -First 1

            if ($null -eq $alreadyAssigned) {
                Invoke-AzCliJson -Arguments @(
                    'rest',
                    '--method', 'POST',
                    '--url', "https://graph.microsoft.com/v1.0/$($EditorPrincipalType.ToLowerInvariant())s/$principalObjectId/appRoleAssignments",
                    '--headers', 'Content-Type=application/json',
                    '--body', $assignmentBody
                ) | Out-Null
            }
        }
    }
}

Write-Host 'Deployment permissions are configured.'
Write-Host "Application role: $roleValue ($roleId)"
if ($EditorPrincipalObjectId.Count -eq 0) {
    Write-Warning 'No editor principal was supplied. The role exists but is not assigned to a curator.'
}
