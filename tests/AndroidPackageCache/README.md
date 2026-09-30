# Android NuGet offline cache

Offline restore and APK build probe for the planned Android application. It includes phone and emulator architectures and a minimal Avalonia activity; it is not the Android report application. Check Release as well as Debug: Release requires `Microsoft.NET.ILLink.Tasks`.

From the repository root, verify using only the local package source:

```powershell
dotnet restore tests/AndroidPackageCache/AndroidPackageCache.csproj --configfile NuGet.Offline.Config --packages offline-cache/android-verified -p:NuGetAudit=false -p:Configuration=Release --force --no-http-cache
```

The restore command checks NuGet availability. To build a signed test APK using the local SDK/JDK, run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AndroidOffline.ps1 -Configuration Debug
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AndroidOffline.ps1 -Configuration Release
```

The script uses only the local NuGet feed, turns off Avalonia build telemetry, selects one ABI for the APK (ARM64 by default), and places AOT temporary files outside the Cyrillic Windows profile path. Both configurations use a test signing key, not a production signing identity. The .NET Android workload must already be installed on the computer. See `offline-cache/ANDROID-OFFLINE.md` for tool locations. Do not run workload or SDK installers just to restore NuGet packages.
