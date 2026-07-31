[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,

    [Parameter(Mandatory)]
    [string]$Thumbprint
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$expectedThumbprint = $Thumbprint.Replace(' ', '').ToUpperInvariant()
$requiredFiles = @(
    'MyBudget.Core.dll',
    'MyBudget.Infrastructure.dll',
    'MyBudget.App.dll',
    'MyBudget.App.exe'
)

$resourceIndex = Join-Path $resolvedPublishDirectory 'MyBudget.App.pri'
if (-not (Test-Path -LiteralPath $resourceIndex -PathType Leaf)) {
    throw "The release is incomplete because MyBudget.App.pri is missing from $resolvedPublishDirectory."
}

$results = foreach ($fileName in $requiredFiles) {
    $filePath = Join-Path $resolvedPublishDirectory $fileName
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "The required first-party file is missing: $filePath"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $filePath
    if ($null -eq $signature.SignerCertificate) {
        throw "$fileName does not contain an Authenticode signature."
    }

    $actualThumbprint = $signature.SignerCertificate.Thumbprint.Replace(' ', '').ToUpperInvariant()
    if ($actualThumbprint -ne $expectedThumbprint) {
        throw "$fileName was signed by an unexpected certificate: $actualThumbprint"
    }

    if ($signature.Status.ToString() -notin @('Valid', 'NotTrusted', 'UnknownError')) {
        throw "$fileName failed signature verification: $($signature.Status) - $($signature.StatusMessage)"
    }

    [pscustomobject]@{
        File = $fileName
        SignatureStatus = $signature.Status
        Signer = $signature.SignerCertificate.Subject
        Thumbprint = $actualThumbprint
    }
}

$results
