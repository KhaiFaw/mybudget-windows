# Local development signature

MyBudget releases can carry a local Authenticode signature from a self-signed `KhaiFaw MyBudget Development` certificate. Its private key is created as non-exportable and stays in the current Windows user's certificate store. GitHub Actions remains keyless, and PFX/private-key files must never be committed.

Create the certificate once:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
$identity = .\scripts\New-DevelopmentSigningCertificate.ps1
$identity.Thumbprint
```

The execution-policy change applies only to that PowerShell window and does not alter the user's permanent policy.

Publish and sign a release:

```powershell
dotnet publish src/MyBudget.App/MyBudget.App.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/MyBudget-win-x64
.\scripts\Sign-DevelopmentRelease.ps1 -PublishDirectory .\artifacts\MyBudget-win-x64 -Thumbprint $identity.Thumbprint
.\scripts\Test-DevelopmentReleaseSignature.ps1 -PublishDirectory .\artifacts\MyBudget-win-x64 -Thumbprint $identity.Thumbprint
```

The signing script signs only the four first-party PE files: `MyBudget.Core.dll`, `MyBudget.Infrastructure.dll`, `MyBudget.App.dll`, and `MyBudget.App.exe`. It does not alter third-party dependencies. It also exports the public certificate and a human-readable `SIGNATURE.txt` into the release folder; neither contains the private key.

In Windows Explorer, open `MyBudget.App.exe` properties and look under **Digital Signatures**. A self-signed certificate may be reported as untrusted on a PC where its public certificate has not been explicitly trusted. Match its thumbprint to `SIGNATURE.txt` to confirm the expected local identity.

## What this proves—and what it does not

Changing a signed PE file after signing causes its Authenticode hash check to fail. Someone else can remove the visible KF mark, rebuild the app, or sign it with a different certificate, but they cannot reproduce the same KhaiFaw signature without access to its private key.

This setup is for local development and portfolio experimentation. It does not verify a legal identity, prevent copying or decompilation, cover the ZIP or every release file, build SmartScreen reputation, or replace a certificate from a publicly trusted code-signing provider. Losing the Windows profile also loses this non-exportable key, so a replacement certificate will have a different thumbprint.

Microsoft documents self-signed certificates as testing tools and describes Authenticode commands in [about Signing](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_signing), [Set-AuthenticodeSignature](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.security/set-authenticodesignature), and [Get-AuthenticodeSignature](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.security/get-authenticodesignature).
