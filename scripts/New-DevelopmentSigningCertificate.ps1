[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$ProtectPrivateKey
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw 'MyBudget development signing is only supported on Windows.'
}

$subject = 'CN=KhaiFaw MyBudget Development'
$now = Get-Date
$existingCertificate = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt $now } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($null -ne $existingCertificate) {
    Write-Host 'Reusing the existing KhaiFaw development signing certificate.'
    [pscustomobject]@{
        Subject = $existingCertificate.Subject
        Thumbprint = $existingCertificate.Thumbprint
        Expires = $existingCertificate.NotAfter
        PrivateKeyExportable = $false
        Created = $false
    }
    return
}

$certificateParameters = @{
    Type = 'CodeSigningCert'
    Subject = $subject
    FriendlyName = 'MyBudget self-signed development code signing'
    CertStoreLocation = 'Cert:\CurrentUser\My'
    Provider = 'Microsoft Software Key Storage Provider'
    KeyAlgorithm = 'RSA'
    KeyLength = 3072
    HashAlgorithm = 'SHA256'
    KeyUsage = 'DigitalSignature'
    KeyExportPolicy = 'NonExportable'
    NotAfter = $now.AddYears(1)
}

if ($ProtectPrivateKey) {
    $certificateParameters.KeyProtection = 'Protect'
}

if (-not $PSCmdlet.ShouldProcess($subject, 'Create a non-exportable CurrentUser code-signing certificate')) {
    return
}

$certificate = New-SelfSignedCertificate @certificateParameters

[pscustomobject]@{
    Subject = $certificate.Subject
    Thumbprint = $certificate.Thumbprint
    Expires = $certificate.NotAfter
    PrivateKeyExportable = $false
    Created = $true
}
