param([string]$Message='Update EmuSync')
# Build, commit, push the current branch, then publish a GitHub prerelease with the Windows and Android builds.
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$gh=Join-Path $PSScriptRoot '.tools\gh\bin\gh.exe'
if(-not (Test-Path -LiteralPath $gh)) { $gh='gh' }
& $gh auth status *> $null
if($LASTEXITCODE -ne 0) { throw 'Not signed in to GitHub. Run: .tools\gh\bin\gh.exe auth login' }
function Run-Git { $ErrorActionPreference='Continue'; git @args 2>&1 | Out-Host; if($LASTEXITCODE -ne 0) { throw "git $args failed" } }

.\build.ps1 -Target All
$installer = Get-ChildItem dist -Filter 'EmuSync-Setup-*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
$assets = @('dist\EmuSync-Android.apk')
if($installer) { $assets += $installer.FullName } else {
  Compress-Archive -Path 'dist\Windows\*' -DestinationPath 'dist\EmuSync-Windows-portable.zip' -Force
  $assets += 'dist\EmuSync-Windows-portable.zip'
}

Run-Git add -A
if(git status --porcelain) { Run-Git commit -m "$Message`n`nCo-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>" }
$branch = git rev-parse --abbrev-ref HEAD
Run-Git push -u origin $branch

$version = ([xml](Get-Content src\EmuSync.App\EmuSync.App.csproj)).SelectSingleNode("//ApplicationDisplayVersion").InnerText
$n = @(git ls-remote --tags origin "v$version-preview.*").Count + 1
$tag = "v$version-preview.$n"
& $gh release create $tag @assets --target $branch --prerelease --title "EmuSync $version preview $n" --generate-notes
if($LASTEXITCODE -ne 0) { throw 'Release failed.' }
"Published $tag"
