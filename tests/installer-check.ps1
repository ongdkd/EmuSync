$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $projectRoot 'src\EmuSync.App\EmuSync.App.csproj'))).SelectSingleNode('//ApplicationDisplayVersion').InnerText
$installer = Join-Path $projectRoot "dist\EmuSync-Setup-$version-x64.exe"
$source = Join-Path $projectRoot 'dist\Windows'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B57009C6-6921-420A-A67A-B6C9844129CA}_is1'
$shortcutFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'EmuSync'
if((Test-Path -LiteralPath $uninstallKey) -or (Test-Path -LiteralPath $shortcutFolder)) {
  throw 'Existing EmuSync installation or Start menu folder found; leaving it untouched.'
}
$testRoot = Join-Path $projectRoot ('dist\installer-check-' + [Guid]::NewGuid().ToString('N'))
$installDir = Join-Path $testRoot 'app'
New-Item -ItemType Directory -Path $testRoot | Out-Null
function Run-Setup([string]$Executable, [string[]]$Arguments) {
  $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
  if($process.ExitCode -ne 0) { throw "Setup failed with exit code $($process.ExitCode). Logs: $testRoot" }
}
try {
  Run-Setup $installer @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $installDir + '"'),('/LOG="' + $testRoot + '\install.log"'))
  $registration = Get-ItemProperty -LiteralPath $uninstallKey
  if($registration.InstallLocation.TrimEnd('\') -ne $installDir) { throw 'Incorrect uninstall registration.' }
  if(-not (Test-Path -LiteralPath (Join-Path $shortcutFolder 'EmuSync.lnk'))) { throw 'Start menu shortcut missing.' }
  $files = @(Get-ChildItem -LiteralPath $source -File -Recurse)
  foreach($file in $files) {
    $relative = $file.FullName.Substring($source.Length).TrimStart('\')
    $installedFile = Join-Path $installDir $relative
    if((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $installedFile).Hash) { throw "Installed file differs: $relative" }
  }
  Write-Output "PASS: $($files.Count) installed files match; Start menu shortcut and per-user uninstall entry exist."
} finally {
  $uninstaller = Join-Path $installDir 'unins000.exe'
  if(Test-Path -LiteralPath $uninstaller) {
    Run-Setup $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + $testRoot + '\uninstall.log"'))
  }
}
if((Test-Path -LiteralPath $installDir) -or (Test-Path -LiteralPath $uninstallKey) -or (Test-Path -LiteralPath $shortcutFolder)) {
  throw "Uninstall left installed files or entries. Logs: $testRoot"
}
Write-Output "PASS: uninstall removed installed files and entries. Logs: $testRoot"
