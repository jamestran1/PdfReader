#requires -Version 7
#requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0' }
# Runs scripts/check.ps1 against a throwaway git repo that uses this repo's real analyzer config.
# Run: Invoke-Pester scripts/tests

BeforeAll {
    $script:RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
    $script:CheckScript = Join-Path $RepoRoot 'scripts/check.ps1'
    $script:Sandbox = Join-Path ([IO.Path]::GetTempPath()) "check-tests-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    # check.ps1 writes UTF-8; decode child output the same way.
    $script:OriginalEncoding = [Console]::OutputEncoding
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

    function Write-SandboxFile([string]$Path, [string]$Content) {
        $full = Join-Path $Sandbox $Path
        New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
        Set-Content -LiteralPath $full -Value $Content -NoNewline
    }

    function Invoke-SandboxGit {
        & git -C $Sandbox @args 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "git $args failed" }
    }

    # Runs the check on the current sandbox working tree; returns exit code + output lines.
    function Invoke-Check {
        Push-Location $Sandbox
        try {
            $output = & pwsh -NoProfile -File $CheckScript -Base main 2>&1 | ForEach-Object { "$_" }
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
        }
        finally { Pop-Location }
    }

    $script:CalcOriginal = @'
namespace Lib;

public class Calc
{
    public static int Add(int a, int b) => a + b;

    public static int Old_Name() => 1;
}
'@

    New-Item -ItemType Directory -Path $Sandbox | Out-Null
    foreach ($config in 'Directory.Build.props', '.editorconfig', 'CodeMetricsConfig.txt') {
        Copy-Item (Join-Path $RepoRoot $config) (Join-Path $Sandbox $config)
    }
    Write-SandboxFile '.gitignore' "bin/`nobj/`n"
    Write-SandboxFile 'Sample.slnx' @'
<Solution>
  <Project Path="src/Lib/Lib.csproj" />
  <Project Path="tests/Lib.Tests/Lib.Tests.csproj" />
</Solution>
'@
    $csproj = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
'@
    Write-SandboxFile 'src/Lib/Lib.csproj' $csproj
    Write-SandboxFile 'tests/Lib.Tests/Lib.Tests.csproj' $csproj
    Write-SandboxFile 'src/Lib/Calc.cs' $CalcOriginal
    Write-SandboxFile 'tests/Lib.Tests/CalcTests.cs' @'
namespace Lib.Tests;

public class CalcTests
{
    public static void Add_ReturnsSum() { }
}
'@

    Invoke-SandboxGit init -q -b main
    Invoke-SandboxGit config user.email 'check@test'
    Invoke-SandboxGit config user.name 'check'
    Invoke-SandboxGit config core.autocrlf false
    Invoke-SandboxGit add -A
    Invoke-SandboxGit commit -q -m 'baseline'

    & dotnet restore (Join-Path $Sandbox 'Sample.slnx') -nologo -v:q | Out-Null
}

AfterAll {
    [Console]::OutputEncoding = $OriginalEncoding
    if ($Sandbox -and (Test-Path $Sandbox)) { Remove-Item -Recurse -Force $Sandbox }
}

Describe 'check.ps1' {
    BeforeEach {
        Invoke-SandboxGit checkout -q -B work main
        Invoke-SandboxGit reset -q --hard main
        Invoke-SandboxGit clean -q -fd -e bin -e obj
    }

    It 'passes when nothing changed, even with old warnings in the code' {
        $result = Invoke-Check

        $result.ExitCode | Should -Be 0 -Because ($result.Output -join "`n")
    }

    It 'reports a warning on a newly added line with file, line and rule' {
        Write-SandboxFile 'src/Lib/Calc.cs' ($CalcOriginal -replace '(public static int Old_Name\(\) => 1;)', "`$1`n`n    public static int New_Name() => 2;")
        Invoke-SandboxGit commit -q -am 'add New_Name'

        $result = Invoke-Check

        $result.ExitCode | Should -Be 1 -Because ($result.Output -join "`n")
        $result.Output | Where-Object { $_ -match '^src/Lib/Calc\.cs:\d+: warning CA1707: ' } | Should -HaveCount 1
        $result.Output | Where-Object { $_ -match '^src/Lib/Calc\.cs:9: warning CA1707: .*New_Name' } | Should -HaveCount 1
    }

    It 'reports warnings in uncommitted and untracked files too' {
        Write-SandboxFile 'src/Lib/Extra.cs' "namespace Lib;`n`npublic class Extra`n{`n    public static int Bad_Name() => 3;`n}`n"

        $result = Invoke-Check

        $result.ExitCode | Should -Be 1 -Because ($result.Output -join "`n")
        $result.Output | Where-Object { $_ -match '^src/Lib/Extra\.cs:5: warning CA1707: ' } | Should -HaveCount 1
    }

    It 'ignores an old warning when the branch only changes another line of the same file' {
        Write-SandboxFile 'src/Lib/Calc.cs' ($CalcOriginal -replace 'a \+ b', 'b + a')
        Invoke-SandboxGit commit -q -am 'touch Add'

        $result = Invoke-Check

        $result.ExitCode | Should -Be 0 -Because ($result.Output -join "`n")
        $result.Output | Should -Not -Match 'Old_Name'
    }

    It 'does not block on a suggestion-level rule (MA0003)' {
        Write-SandboxFile 'src/Lib/Flags.cs' @'
namespace Lib;

public class Flags
{
    public static int Pick(bool value) => value ? 1 : 0;

    public static int Use() => Pick(true);
}
'@

        $result = Invoke-Check

        $result.ExitCode | Should -Be 0 -Because ($result.Output -join "`n")
        # Guard against a vacuous pass: the rule must actually have fired.
        Get-Content (Join-Path $Sandbox 'src/Lib/obj/Lib.sarif') -Raw | Should -Match '"MA0003"'
    }

    It 'does not report CA1707 for underscored test names under tests/' {
        Write-SandboxFile 'tests/Lib.Tests/MoreTests.cs' @'
namespace Lib.Tests;

public class MoreTests
{
    public static void Sub_ReturnsDifference() { }
}
'@

        $result = Invoke-Check

        $result.Output | Should -Not -Match 'CA1707'
        $result.ExitCode | Should -Be 0 -Because ($result.Output -join "`n")
    }

    It 'reports CA1502 at the declaration of a new method with cyclomatic complexity above 15' {
        $ifs = (1..16 | ForEach-Object { "        if (x == $_) { return $_; }" }) -join "`n"
        Write-SandboxFile 'src/Lib/Branchy.cs' @"
namespace Lib;

public static class Branchy
{
    public static int Pick(int x)
    {
$ifs
        return 0;
    }
}
"@

        $result = Invoke-Check

        $result.ExitCode | Should -Be 1 -Because ($result.Output -join "`n")
        $result.Output | Where-Object { $_ -match '^src/Lib/Branchy\.cs:5: warning CA1502: ' } | Should -HaveCount 1
    }

    It 'exits with the could-not-run code and the compiler error when the build fails' {
        Write-SandboxFile 'src/Lib/Broken.cs' "namespace Lib;`n`npublic class Broken { int x = ; }`n"

        $result = Invoke-Check

        $result.ExitCode | Should -Be 2 -Because ($result.Output -join "`n")
        ($result.Output -join "`n") | Should -Match 'không chạy được'
        ($result.Output -join "`n") | Should -Match 'Broken\.cs.*error CS'
    }

    It 'exits with the could-not-run code when the base ref does not exist' {
        Push-Location $Sandbox
        try {
            & pwsh -NoProfile -File $CheckScript -Base no-such-branch 2>&1 | Out-Null
            $LASTEXITCODE | Should -Be 2
        }
        finally { Pop-Location }
    }
}
