param([string]$ScratchDirectory = (Join-Path $env:TEMP 'pludibase-acquisition-tests'))
$ErrorActionPreference = 'Stop'
$sdkRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'This offline runner requires the Windows .NET Framework C# compiler.' }
if ($ScratchDirectory -notmatch 'acquisition-tests') { throw 'ScratchDirectory must explicitly contain acquisition-tests.' }
New-Item -ItemType Directory -Path $ScratchDirectory -Force | Out-Null
$output = Join-Path ([System.IO.Path]::GetFullPath($ScratchDirectory)) 'AcquisitionTests.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Runtime/Acquisition') -Filter '*.cs' | ForEach-Object { $_.FullName })
$sources += Join-Path $PSScriptRoot 'UnityStubs.cs'
$sources += Join-Path $PSScriptRoot 'Program.cs'
& $compiler /nologo /warn:4 /target:exe /r:System.Web.Extensions.dll "/out:$output" @sources
if ($LASTEXITCODE -ne 0) { throw 'Offline acquisition compilation failed.' }
& $output ([System.IO.Path]::GetFullPath($ScratchDirectory))
if ($LASTEXITCODE -ne 0) { throw 'Offline acquisition tests failed.' }
