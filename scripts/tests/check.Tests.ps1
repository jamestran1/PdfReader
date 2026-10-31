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
        # CRLF like a Windows checkout: here-strings follow this file's line endings, inserted lines use `n.
        Set-Content -LiteralPath $full -Value ($Content -replace "`r?`n", "`r`n") -NoNewline
    }

    function Invoke-SandboxGit {
        & git -C $Sandbox @args 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "git $args failed" }
    }

    # Runs the check on the current sandbox working tree; returns exit code + output lines.
    function Invoke-Check([string]$Base = 'main', [switch]$Fix, [switch]$Summary) {
        Push-Location $Sandbox
        try {
            $arguments = @('-NoProfile', '-File', $CheckScript, '-Base', $Base)
            if ($Fix) { $arguments += '-Fix' }
            if ($Summary) { $arguments += '-Summary' }
            $output = & pwsh @arguments 2>&1 | ForEach-Object { "$_" }
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
        }
        finally { Pop-Location }
    }

    $script:CalcOriginal = @'
namespace Sample;

public class Calc
{
    public static int Add(int a, int b) => a + b;
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
    # Same test packages as tests/PdfReaderApp.Tests, so restore hits the local cache.
    Write-SandboxFile 'tests/Lib.Tests/Lib.Tests.csproj' @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Lib\Lib.csproj" />
  </ItemGroup>
</Project>
'@
    Write-SandboxFile 'src/Lib/Calc.cs' $CalcOriginal
    $script:CalcTestsOriginal = @'
namespace Sample.Tests;

public class CalcTests
{
    [Fact]
    public void Add_ReturnsSum() => Assert.Equal(3, Calc.Add(1, 2));
}
'@
    Write-SandboxFile 'tests/Lib.Tests/CalcTests.cs' $CalcTestsOriginal
    # Old XAML already uses the dead brush on a line nobody touches.
    $script:XamlOriginal = @'
<UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <Border Background="{DynamicResource PrimaryHueMidBrush}" />
    <Border CornerRadius="12" />
</UserControl>
'@
    Write-SandboxFile 'src/Lib/Views/Panel.xaml' $XamlOriginal
    # Old Markdown already has an em dash on a line nobody touches.
    $script:EmDash = [string][char]0x2014
    $script:MdOriginal = "# Notes`n`nCũ $EmDash giữ nguyên.`nDòng thứ hai.`n"
    Write-SandboxFile 'docs/Notes.md' $MdOriginal

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
        # Reset the dirty tree first: checkout refuses to leave uncommitted edits behind.
        Invoke-SandboxGit reset -q --hard
        Invoke-SandboxGit clean -q -fd -e bin -e obj
        Invoke-SandboxGit checkout -q -B work main
    }

    It 'passes when nothing changed' {
        $result = Invoke-Check

        $result.ExitCode | Should -Be 0 -Because ($result.Output -join "`n")
    }

    It 'reports a warning on a newly added line with file, line and rule' {
        Write-SandboxFile 'src/Lib/Calc.cs' ($CalcOriginal -replace '(public static int Add\(int a, int b\) => a \+ b;)', "`$1`n`n    public static int New_Name() => 2;")
        Invoke-SandboxGit commit -q -am 'add New_Name'

        $result = Invoke-Check

        $result.ExitCode | Should -Be 1 -Because ($result.Output -join "`n")
        $result.Output | Where-Object { $_ -match '^src/Lib/Calc\.cs:\d+: warning CA1707: ' } | Should -HaveCount 1
        $result.Output | Where-Object { $_ -match '^src/Lib/Calc\.cs:7: warning CA1707: .*New_Name' } | Should -HaveCount 1
    }

    It 'reports warnings in uncommitted and untracked files too' {
        Write-SandboxFile 'src/Lib/Extra.cs' "namespace Sample;`n`npublic class Extra`n{`n    public static int Bad_Name() => 3;`n}`n"

        $result = Invoke-Check

        $result.ExitCode | Should -Be 1 -Because ($result.Output -join "`n")
        $result.Output | Where-Object { $_ -match '^src/Lib/Extra\.cs:5: warning CA1707: ' } | Should -HaveCount 1
    }

    It 'blocks an old warning in an untouched file once other code changes (no diff filter, ADR 0007)' {
        Write-SandboxFile 'src/Lib/Calc.cs' ($CalcOriginal -replace '(public static int Add\(int a, int b\) => a \+ b;)', "`$1`n`n    public static int Old_Name() => 1;")
        Invoke-SandboxGit commit -q -am 'old warning'
        Write-SandboxFile 'src/Lib/Clean.cs' "namespace Sample;`n`npublic static class Clean`n{`n    public static int One() => 1;`n}`n"

        $result = Invoke-Check -Base 'HEAD'

        $result.ExitCode | Should -Be 1 -Because ($result.Output -join "`n")
        $result.Output | Where-Object { $_ -match '^src/Lib/Calc\.cs:7: warning CA1707: .*Old_Name' } | Should -HaveCount 1
    }

    It 'does not block on a suggestion-level rule (MA0003)' {
        Write-SandboxFile 'src/Lib/Flags.cs' @'
namespace Sample;

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
namespace Sample.Tests;

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
namespace Sample;

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
        Write-SandboxFile 'src/Lib/Broken.cs' "namespace Sample;`n`npublic class Broken { int x = ; }`n"

        $result = Invoke-Check

        $result.ExitCode | Should -Be 2 -Because ($result.Output -join "`n")
        ($result.Output -join "`n") | Should -Match 'không chạy được'
        ($result.Output -join "`n") | Should -Match 'Broken\.cs.*error CS'
    }

    It 'exits with the could-not-run code when the base ref does not exist' {
        $result = Invoke-Check -Base 'no-such-branch'

        $result.ExitCode | Should -Be 2 -Because ($result.Output -join "`n")
    }

    Context 'format' {
        BeforeEach {
            $script:BadIndent = "namespace Sample;`n`npublic static class Indent`n{`n  public static int One() => 1;`n}`n"
            Write-SandboxFile 'src/Lib/Indent.cs' $BadIndent
        }

        It 'blocks a badly indented file in check mode and names it' {
            $result = Invoke-Check

            $result.ExitCode | Should -Be 1 -Because ($result.Output -join "`n")
            $result.Output | Where-Object { $_ -match '^src/Lib/Indent\.cs:\d+: warning WHITESPACE: ' } | Should -Not -BeNullOrEmpty
            (Get-Content (Join-Path $Sandbox 'src/Lib/Indent.cs') -Raw) -replace "`r`n", "`n" | Should -Be $BadIndent
        }

        It 'fixes the file in fix mode, names it and does not count it as a violation' {
            $result = Invoke-Check -Fix

            $result.ExitCode | Should -Be 0 -Because ($result.Output -join "`n")
            $result.Output | Where-Object { $_ -match '^check: format đã sửa: .*src/Lib/Indent\.cs' } | Should -HaveCount 1
            Get-Content (Join-Path $Sandbox 'src/Lib/Indent.cs') -Raw | Should -Match '\n    public static int One'
        }
    }

    Context 'format on an existing file' {
        # The badly indented file is committed on the work branch; -Base HEAD then compares only uncommitted edits.
        BeforeEach {
            Write-SandboxFile 'src/Lib/Indent.cs' "namespace Sample;`n`npublic static class Indent`n{`n  public static int One() => 1;`n`n    public static int Two() => 2;`n}`n"
            Invoke-SandboxGit add -A
            Invoke-SandboxGit commit -q -m 'old bad indentation'
        }

        It 'ignores bad indentation on an unchanged line' {
            Write-SandboxFile 'src/Lib/Indent.cs' "namespace Sample;`n`npublic static class Indent`n{`n  public static int One() => 1;`n`n    public static int Two() => 3;`n}`n"

            $result = Invoke-Check -Base 'HEAD'

            $result.ExitCode | Should -Be 0 -Because ($result.Output -join "`n")
        }

        It 'reports bad indentation on a changed line at that line' {
            Write-SandboxFile 'src/Lib/Indent.cs' "namespace Sample;`n`npublic static class Indent`n{`n  public static int One() => 1;`n`n  public static int Two() => 2;`n}`n"

            $result = Invoke-Check -Base 'HEAD'

            $result.ExitCode | Should -Be 1 -Because ($result.Output -join "`n")
            $result.Output | Where-Object { $_ -match '^src/Lib/Indent\.cs:\d+: ' } | Should -HaveCount 1
            $result.Output | Where-Object { $_ -match '^src/Lib/Indent\.cs:7: warning WHITESPACE: ' } | Should -HaveCount 1
        }
    }

    Context 'forbidden XAML' {
        It 'blocks PrimaryHueMidBrush on an added line with file and line' {
            Write-SandboxFile 'src/Lib/Views/Panel.xaml' ($XamlOriginal -replace '(<Border CornerRadius="12" />)', "`$1`n    <TextBlock Foreground=`"{DynamicResource PrimaryHueMidBrush}`" />")
            Invoke-SandboxGit commit -q -am 'new brush use'

            $result = Invoke-Check

            $result.ExitCode | Should -Be 1 -Because ($result.Output -join "`n")
            $result.Output | Where-Object { $_ -match '^src/Lib/Views/Panel\.xaml:\d+: ' } | Should -HaveCount 1
            $result.Output | Where-Object { $_ -match '^src/Lib/Views/Panel\.xaml:4: warning UI-3: ' } | Should -HaveCount 1
        }

        It 'blocks CornerRadius="100" on an added line with file and line' {
            Write-SandboxFile 'src/Lib/Views/Pill.xaml' "<UserControl xmlns=`"http://schemas.microsoft.com/winfx/2006/xaml/presentation`">`n    <Border CornerRadius=`"100`" />`n</UserControl>`n"

            $result = Invoke-Check

            $result.ExitCode | Should -Be 1 -Because ($result.Output -join "`n")
            $result.Output | Where-Object { $_ -match '^src/Lib/Views/Pill\.xaml:2: warning UI-5: ' } | Should -HaveCount 1
        }

        It 'does not report a forbidden value that sits on an unchanged line' {
            Write-SandboxFile 'src/Lib/Views/Panel.xaml' ($XamlOriginal -replace 'CornerRadius="12"', 'CornerRadius="8"')
            Invoke-SandboxGit commit -q -am 'touch another line'

            $result = Invoke-Check

            $result.ExitCode | Should -Be 0 -Because ($result.Output -join "`n")
            $result.Output | Should -Not -Match 'UI-3'
        }
    }

    Context 'forbidden Markdown' {
        It 'blocks an em dash on an added Markdown line with file and line' {
            Write-SandboxFile 'docs/Notes.md' ($MdOriginal + "Mới $EmDash thêm vào.`n")
            Invoke-SandboxGit commit -q -am 'add em dash'

            $result = Invoke-Check

            $result.ExitCode | Should -Be 1 -Because ($result.Output -join "`n")
            $result.Output | Where-Object { $_ -match '^docs/Notes\.md:\d+: ' } | Should -HaveCount 1
            $result.Output | Where-Object { $_ -match '^docs/Notes\.md:5: warning REPO-3: ' } | Should -HaveCount 1
        }

        It 'does not report an em dash that sits on an unchanged line' {
            Write-SandboxFile 'docs/Notes.md' ($MdOriginal -replace 'Dòng thứ hai', 'Dòng thứ hai đã sửa')
            Invoke-SandboxGit commit -q -am 'touch another line'

            $result = Invoke-Check

            $result.ExitCode | Should -Be 0 -Because ($result.Output -join "`n")
            $result.Output | Should -Not -Match 'REPO-3'
        }
    }

    It 'ignores SARIF from nested worktrees under .claude/worktrees' {
        # A subagent worktree inside the repo leaves its own obj/*.sarif behind; it is not this checkout's code.
        Write-SandboxFile 'src/Lib/Extra.cs' "namespace Sample;`n`npublic class Extra`n{`n    public static int Bad_Name() => 3;`n}`n"
        & dotnet build (Join-Path $Sandbox 'Sample.slnx') -nologo -v:q | Out-Null
        $sarif = Get-Content (Join-Path $Sandbox 'src/Lib/obj/Lib.sarif') -Raw
        $worktree = Join-Path $Sandbox '.claude/worktrees/agent-x'
        $sandboxUri = ([Uri](Join-Path $Sandbox 'src')).AbsoluteUri
        $worktreeUri = ([Uri](Join-Path $worktree 'src')).AbsoluteUri
        New-Item -ItemType Directory -Force (Join-Path $worktree 'src/Lib/obj') | Out-Null
        Set-Content -LiteralPath (Join-Path $worktree 'src/Lib/obj/Lib.sarif') -Value $sarif.Replace($sandboxUri, $worktreeUri)
        Add-Content -LiteralPath (Join-Path $Sandbox '.gitignore') -Value '.claude/'

        $result = Invoke-Check

        $result.Output | Should -Not -Match '\.claude/' -Because ($result.Output -join "`n")
        $result.Output | Where-Object { $_ -match '^src/Lib/Extra\.cs:5: warning CA1707: ' } | Should -HaveCount 1
    }

    It 'still checks its own SARIF when it runs inside a worktree under .claude/worktrees' {
        # The SubagentStop hook runs check.ps1 inside the subagent's worktree; that checkout must still be checked.
        Add-Content -LiteralPath (Join-Path $Sandbox '.gitignore') -Value '.claude/'
        Invoke-SandboxGit commit -q -am 'ignore .claude'
        $worktree = Join-Path $Sandbox '.claude/worktrees/agent-y'
        Invoke-SandboxGit worktree add -q -b agent-y $worktree
        try {
            Set-Content -LiteralPath (Join-Path $worktree 'src/Lib/Extra.cs') -Value "namespace Sample;`r`n`r`npublic static class Extra`r`n{`r`n    public static int Bad_Name() => 3;`r`n}`r`n"
            Push-Location $worktree
            try { $output = & pwsh -NoProfile -File $CheckScript -Base main 2>&1 | ForEach-Object { "$_" }; $exit = $LASTEXITCODE }
            finally { Pop-Location }

            $exit | Should -Be 1 -Because ($output -join "`n")
            $output | Where-Object { $_ -match '^src/Lib/Extra\.cs:5: warning CA1707: ' } | Should -HaveCount 1
        }
        finally {
            Invoke-SandboxGit worktree remove --force $worktree
        }
    }

    It 'blocks a failing test and prints its name' {
        Write-SandboxFile 'tests/Lib.Tests/CalcTests.cs' ($CalcTestsOriginal -replace 'Assert.Equal\(3', 'Assert.Equal(4')
        Invoke-SandboxGit commit -q -am 'break the test'

        $result = Invoke-Check

        $result.ExitCode | Should -Be 1 -Because ($result.Output -join "`n")
        $result.Output | Where-Object { $_ -match '^TEST: Sample\.Tests\.CalcTests\.Add_ReturnsSum$' } | Should -HaveCount 1
    }

    It 'prints a markdown table of remaining warnings per rule in summary mode, ignoring the diff' {
        Write-SandboxFile 'src/Lib/Extra.cs' "namespace Sample;`n`npublic class Extra`n{`n    public static int Bad_Name() => 3;`n}`n"

        $result = Invoke-Check -Summary

        $result.ExitCode | Should -Be 0 -Because ($result.Output -join "`n")
        $result.Output | Should -Contain '| Rule | Warning |'
        $result.Output | Should -Contain '| CA1707 | 1 |'
        $result.Output | Where-Object { $_ -match '^\| \*\*Tổng\*\* \| \*\*\d+\*\* \|$' } | Should -HaveCount 1
    }

    It 'skips build and tests when only markdown changed' {
        $sarif = Join-Path $Sandbox 'src/Lib/obj/Lib.sarif'
        Remove-Item -LiteralPath $sarif -ErrorAction SilentlyContinue
        Write-SandboxFile 'README.md' "# Lib`n"
        Invoke-SandboxGit add -A
        Invoke-SandboxGit commit -q -m 'docs only'

        $result = Invoke-Check

        $result.ExitCode | Should -Be 0 -Because ($result.Output -join "`n")
        ($result.Output -join "`n") | Should -Match 'bỏ qua build và test'
        Test-Path $sarif | Should -BeFalse
    }
}
