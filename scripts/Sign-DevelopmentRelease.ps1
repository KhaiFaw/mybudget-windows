[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,

    [string]$Thumbprint = $env:MYBUDGET_SIGNING_CERT_THUMBPRINT
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Thumbprint)) {
    throw 'Provide -Thumbprint or set MYBUDGET_SIGNING_CERT_THUMBPRINT.'
}

$resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$expectedThumbprint = $Thumbprint.Replace(' ', '').ToUpperInvariant()
$subject = 'CN=KhaiFaw MyBudget Development'
$now = Get-Date

$certificate = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Thumbprint -eq $expectedThumbprint } |
    Select-Object -First 1

if ($null -eq $certificate) {
    throw "No CurrentUser code-signing certificate with thumbprint $expectedThumbprint and a private key was found."
}

if ($certificate.Subject -ne $subject) {
    throw "The selected certificate has the unexpected subject '$($certificate.Subject)'."
}

if (-not $certificate.HasPrivateKey -or $certificate.NotBefore -gt $now -or $certificate.NotAfter -le $now) {
    throw 'The selected signing certificate is not currently usable.'
}

$filesToSign = @(
    'MyBudget.Core.dll',
    'MyBudget.Infrastructure.dll',
    'MyBudget.App.dll',
    'MyBudget.App.exe'
)

foreach ($fileName in $filesToSign) {
    $filePath = Join-Path $resolvedPublishDirectory $fileName
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "The required first-party file is missing: $filePath"
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $resolvedPublishDirectory 'MyBudget.App.pri') -PathType Leaf)) {
    throw 'MyBudget.App.pri is missing. Publish the complete WinUI release before signing.'
}

foreach ($fileName in $filesToSign) {
    $filePath = Join-Path $resolvedPublishDirectory $fileName
    if ($PSCmdlet.ShouldProcess($filePath, "Sign with $subject")) {
        $signature = Set-AuthenticodeSignature `
            -LiteralPath $filePath `
            -Certificate $certificate `
            -HashAlgorithm SHA256 `
            -IncludeChain Signer `
            -Force

        if ($null -eq $signature.SignerCertificate -or
            $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
            throw "Signing did not attach the expected certificate to $fileName."
        }
    }
}

$publicCertificatePath = Join-Path $resolvedPublishDirectory 'KhaiFaw-MyBudget-Development.cer'
Export-Certificate -Cert $certificate -FilePath $publicCertificatePath -Force | Out-Null

$signatureNotePath = Join-Path $resolvedPublishDirectory 'SIGNATURE.txt'
$signatureNote = @"
MYBUDGET LOCAL DEVELOPMENT SIGNATURE

Creator: KhaiFaw
Certificate subject: $($certificate.Subject)
Certificate thumbprint: $($certificate.Thumbprint)
Signed at: $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz'))

This is a self-signed development certificate. It makes changes to the signed
MyBudget files detectable, but it is not a publicly trusted publisher identity
and does not remove Microsoft Defender SmartScreen warnings on other PCs.

Verify from PowerShell with:
Get-AuthenticodeSignature .\MyBudget.App.exe | Format-List Status, StatusMessage, SignerCertificate
"@

$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($signatureNotePath, $signatureNote, $utf8WithoutBom)

& (Join-Path $PSScriptRoot 'Test-DevelopmentReleaseSignature.ps1') `
    -PublishDirectory $resolvedPublishDirectory `
    -Thumbprint $certificate.Thumbprint
