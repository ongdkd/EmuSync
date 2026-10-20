param([Parameter(Mandatory)][string]$Apk)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$sdk=if($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $root '.tools\android' }
$java=if($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\java.exe' } else { Join-Path $root '.tools\jdk\bin\java.exe' }
$buildTools=Join-Path $sdk 'build-tools\36.0.0'
$signature=& $java -jar "$buildTools\lib\apksigner.jar" verify --print-certs $Apk
# The original preview's public certificate is the upgrade identity; never accept a new debug key.
if($LASTEXITCODE -ne 0 -or $signature -notcontains 'Signer #1 certificate SHA-256 digest: e8f62805802cc62dfd5ce84ccdf40d221b8e790cf722cbebedac580676a2d313') {
  throw 'APK is not signed with the original EmuSync key. Publishing it would break updates.'
}
$project=[xml](Get-Content (Join-Path $root 'src\EmuSync.App\EmuSync.App.csproj'))
$version=@($project.SelectNodes('//ApplicationVersion'))[-1].InnerText
$display=@($project.SelectNodes('//ApplicationDisplayVersion'))[-1].InnerText
$manifest=& "$buildTools\aapt2.exe" dump badging $Apk
if($LASTEXITCODE -ne 0 -or -not ($manifest | Where-Object { $_.StartsWith("package: name='app.emusync.local' versionCode='$version' versionName='$display' ") })) {
  throw 'APK package identity or version does not match the Android project.'
}
& "$buildTools\zipalign.exe" -c -P 16 4 $Apk
if($LASTEXITCODE -ne 0) { throw 'APK is not aligned for Android 16 KB pages.' }
"PASS: original signing certificate, package identity, Android version $display ($version), and 16 KB alignment."
