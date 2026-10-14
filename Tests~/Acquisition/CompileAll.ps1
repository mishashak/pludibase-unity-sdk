param([string]$ScratchDirectory = (Join-Path $env:TEMP 'pludibase-acquisition-tests'))
$ErrorActionPreference = 'Stop'
if ($ScratchDirectory -notmatch 'acquisition-tests') { throw 'ScratchDirectory must explicitly contain acquisition-tests.' }
$roslyn = Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll'
$csharp = Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll'
if (-not (Test-Path -LiteralPath $roslyn) -or -not (Test-Path -LiteralPath $csharp)) { throw 'Run this compile-only check with PowerShell 7 containing Roslyn.' }
Add-Type -LiteralPath $roslyn
Add-Type -LiteralPath $csharp
New-Item -ItemType Directory -Path $ScratchDirectory -Force | Out-Null
$sdkRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sources = @(Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Runtime') -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName })
$sources += Join-Path $PSScriptRoot 'UnityStubs.cs'
$sources += Join-Path $PSScriptRoot 'TaloCompileStubs.cs'
$references = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($name in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Web.Extensions.dll')) {
    $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319' $name)))
}
foreach ($profile in @('UNITY_EDITOR', 'UNITY_ANDROID', 'UNITY_IOS')) {
    $parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp8).WithPreprocessorSymbols([string[]]@($profile))
    $trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
    foreach ($source in $sources) {
        $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($source), $parseOptions, $source, [Text.Encoding]::UTF8, [Threading.CancellationToken]::None))
    }
    $compileOptions = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
    $compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create("PludibaseStubCompile_$profile", $trees.ToArray(), $references.ToArray(), $compileOptions)
    $output = Join-Path ([IO.Path]::GetFullPath($ScratchDirectory)) "PludibaseStubCompile_$profile.dll"
    $stream = [IO.File]::Open($output, [IO.FileMode]::Create, [IO.FileAccess]::Write)
    try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
    foreach ($diagnostic in $result.Diagnostics) {
        if ($diagnostic.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error) { Write-Output $diagnostic.ToString() }
    }
    if (-not $result.Success) { throw "Compile-only SDK verification failed: $profile" }
    Write-Output "PASS: full Runtime compile against test-only Unity/Talo stubs ($profile). No source executed."
}
