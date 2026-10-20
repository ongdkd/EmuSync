param([ValidateSet('All','Windows','Android','Checks','Installer')][string]$Target='All')
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$dotnetPath=Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if(Test-Path -LiteralPath $dotnetPath) { $env:DOTNET_ROOT=Split-Path $dotnetPath; $env:NUGET_PACKAGES=Join-Path $PSScriptRoot '.tools\nuget' } else { $dotnetPath='dotnet' }
$tools=Join-Path $PSScriptRoot '.tools'
if(Test-Path -LiteralPath "$tools\jdk") { $env:JAVA_HOME="$tools\jdk" }
if(Test-Path -LiteralPath "$tools\home\android-sdk") { $env:ANDROID_HOME="$tools\home\android-sdk"; $env:ANDROID_SDK_ROOT=$env:ANDROID_HOME; $env:ANDROID_USER_HOME="$tools\home\android-user" }
elseif(Test-Path -LiteralPath "$tools\android") { $env:ANDROID_HOME="$tools\android"; $env:ANDROID_SDK_ROOT=$env:ANDROID_HOME }
if(Test-Path -LiteralPath "$tools\tmp") { $env:TEMP="$tools\tmp"; $env:TMP=$env:TEMP }
function Run-Dotnet { & $dotnetPath @args; if($LASTEXITCODE -ne 0) { throw "Build failed: dotnet $args" } }
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'dist') | Out-Null
Run-Dotnet run --project tests/EmuSync.Checks
if($env:OS -eq 'Windows_NT') { Run-Dotnet run --project tests/EmuSync.Checks -- --redirection-guard }
Run-Dotnet run --project tests/EmuSync.AuthChecks
if($Target -in 'All','Windows') {
  Run-Dotnet publish src/EmuSync.App/EmuSync.App.csproj -f net10.0-windows10.0.19041.0 -c Release '-p:WindowsPackageType=None' '-p:WindowsAppSDKSelfContained=true' '-p:SelfContained=true' -o dist/Windows
}
if($Target -in 'All','Android') {
  $keyStore=Join-Path $tools 'signing\emusync.keystore'
  if(-not (Test-Path -LiteralPath $keyStore)) { throw 'Restore the original EmuSync signing key to .tools/signing/emusync.keystore before building a release. Do not generate a replacement.' }
  Run-Dotnet publish src/EmuSync.App/EmuSync.App.csproj -f net10.0-android -c Release '-p:AndroidPackageFormats=apk' '-p:EmbedAssembliesIntoApk=true' '-p:RunAOTCompilation=false' "-p:AndroidSdkDirectory=$env:ANDROID_HOME" "-p:JavaSdkDirectory=$env:JAVA_HOME" '-p:InstallAndroidDependencies=true' '-p:AcceptAndroidSDKLicenses=true' '-p:AndroidKeyStore=true' "-p:AndroidSigningKeyStore=$keyStore" '-p:AndroidSigningKeyAlias=androiddebugkey' '-p:AndroidSigningStorePass=android' '-p:AndroidSigningKeyPass=android'
  $apk=Get-ChildItem -LiteralPath 'src/EmuSync.App/bin/Release/net10.0-android/publish' -Filter '*-Signed.apk' | Select-Object -First 1
  if(-not $apk) { throw 'Signed APK not found.' }
  & "$PSScriptRoot\tests\android-package-check.ps1" -Apk $apk.FullName
  Copy-Item -LiteralPath $apk.FullName -Destination 'dist/EmuSync-Android.apk' -Force
}
if($Target -eq 'Installer') {
  $compiler = @(
    (Join-Path $PSScriptRoot '.tools\inno\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
  ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
  if(-not $compiler) { throw 'Install Inno Setup 7, or place its compiler in .tools\inno, then retry.' }
  if(-not (Test-Path -LiteralPath 'dist\Windows\EmuSync.App.exe')) { throw 'Run .\build.ps1 -Target Windows first.' }
  & $compiler /Qp installer\EmuSync.iss
  if($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
}
