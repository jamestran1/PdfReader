#requires -Version 7
<#
.SYNOPSIS
    Shared check for CI and the Claude Code hook: format, build, analyzer warnings on changed lines, forbidden XAML, tests.

.DESCRIPTION
    Compares the working tree (including uncommitted and untracked files) with `git merge-base <Base> HEAD`
    and runs, in order:
      1. format    whitespace + style on changed .cs files; only issues on changed lines count.
                   -Fix rewrites those files instead and reports them on one line.
      2. build     analyzers write SARIF per project (see Directory.Build.props).
      3. filter    warnings on changed lines; rules listed in analyzers/clean-rules.txt count everywhere (ADR 0007).
      4. XAML      PrimaryHueMidBrush / CornerRadius="100" on changed lines (CODING_STANDARDS.md UI-3, UI-5).
      5. test      only when .cs / .xaml / .csproj (or .props / .targets / global.json) files changed.
    When nothing that affects the build changed (e.g. only markdown), build and test are skipped.

    Exit codes:
      0  pass
      1  blocking violations; one line each on stdout:
           <file>:<line>: <level> <rule>: <message>
           TEST: <fully qualified test name>
      2  could not run (build failed, git error, no SARIF...); never reported as a pass

.PARAMETER Base
    Git ref to compare against. Default origin/main.

.PARAMETER Target
    Solution or project to build. Default: the single *.slnx / *.sln at the repo root.

.PARAMETER Fix
    Rewrite badly formatted files instead of reporting them (for the agent hook). CI runs without it.

.PARAMETER Summary
    Build only and print a markdown table of all remaining warnings per rule, ignoring the diff
    (cleanup progress on main, ADR 0007). Exit 0 unless the build cannot run.
#>
[CmdletBinding()]
param(
    [string]$Base = 'origin/main',
    [string]$Target,
    [switch]$Fix,
    [switch]$Summary
)

$ErrorActionPreference = 'Stop'
# Vietnamese messages must survive the pipe to CI logs and the hook.
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$ExitPass = 0
$ExitViolations = 1
$ExitCannotRun = 2

$CleanRulesFile = 'analyzers/clean-rules.txt'
# Package/SDK changes (.props, .targets, global.json) can break tests too, so they also trigger them.
$TestExtensions = '.cs', '.xaml', '.csproj', '.props', '.targets'
$TestFiles = @('global.json')
$BuildExtensions = $TestExtensions + '.editorconfig', '.slnx', '.sln'
$BuildFiles = $TestFiles + 'codemetricsconfig.txt', $CleanRulesFile
$ForbiddenXaml = @(
    @{ Rule = 'UI-3'; Pattern = 'PrimaryHueMid(Foreground)?Brush'; Message = 'brush chết dưới CustomColorTheme; dùng MaterialDesign.Brush.Primary hoặc TriThu.Brush.*' }
    @{ Rule = 'UI-5'; Pattern = 'CornerRadius\s*=\s*"100"'; Message = 'CornerRadius="100" vẽ thành elip; nút dài dùng CornerRadius="12", cần bo tròn hai đầu thì dùng đúng nửa chiều cao' }
)

function Stop-CannotRun([string]$Message) {
    Write-Host "check: không chạy được: $Message"
    exit $ExitCannotRun
}

# Any unexpected failure (dotnet missing, bad SARIF...) must not look like exit 1 = violations.
trap { Stop-CannotRun "$_" }

function Invoke-Git {
    $output = & git -c core.quotepath=off @args 2>&1
    if ($LASTEXITCODE -ne 0) { Stop-CannotRun "git $($args -join ' '): $output" }
    # Drop stderr chatter (e.g. CRLF notices) so callers only see stdout.
    $output | Where-Object { $_ -isnot [Management.Automation.ErrorRecord] }
}

function ConvertTo-RepoPath([string]$Path) {
    ([IO.Path]::GetRelativePath($repoRoot, $Path) -replace '\\', '/')
}

# Returns a case-insensitive map: repo-relative path (forward slashes) -> HashSet[int] of added/changed lines,
# or $null for files that are new and untracked (every line counts).
function Get-ChangedLines([string]$MergeBase) {
    $changed = @{}
    $file = $null
    foreach ($line in Invoke-Git diff --unified=0 --no-color --no-ext-diff --no-renames $MergeBase) {
        if ($line -match '^\+\+\+ (?:b/)?(.+)$') {
            # git appends a tab after paths that contain spaces
            $path = $Matches[1].TrimEnd("`t")
            $file = if ($path -eq '/dev/null') { $null } else { $path }
            if ($file -and -not $changed.ContainsKey($file)) { $changed[$file] = [Collections.Generic.HashSet[int]]::new() }
        }
        elseif ($file -and $line -match '^@@ -\S+ \+(\d+)(?:,(\d+))? @@') {
            $start = [int]$Matches[1]
            $count = if ($null -ne $Matches[2]) { [int]$Matches[2] } else { 1 }
            for ($i = $start; $i -lt $start + $count; $i++) { [void]$changed[$file].Add($i) }
        }
    }
    foreach ($untracked in Invoke-Git ls-files --others --exclude-standard) {
        $changed[$untracked] = $null
    }
    $changed
}

function Test-ChangedLine([string]$File, [int]$Line) {
    $changed.ContainsKey($File) -and ($null -eq $changed[$File] -or $changed[$File].Contains($Line))
}

# Existing changed files whose extension is in $Extensions (repo-relative, original casing).
function Get-ChangedFiles([string[]]$Extensions) {
    foreach ($key in $changed.Keys) {
        if ([IO.Path]::GetExtension($key) -notin $Extensions) { continue }
        $item = Get-Item -LiteralPath (Join-Path $repoRoot $key) -ErrorAction SilentlyContinue
        if ($item) { ConvertTo-RepoPath $item.FullName }
    }
}

function New-Violation([string]$File, [int]$Line, [string]$Level, [string]$Rule, [string]$Message) {
    [pscustomobject]@{ File = $File; Line = $Line; Level = $Level; Rule = $Rule; Message = ($Message -replace '\s+', ' ').Trim() }
}

# Runs `dotnet format <whitespace|style> --verify-no-changes` and returns its diagnostics.
function Get-FormatIssues([string]$Kind, [string[]]$Files) {
    $output = & dotnet format $Kind $Target --verify-no-changes --include @Files 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -notin 0, 2) { Stop-CannotRun ("dotnet format $Kind lỗi`n" + ($output -join "`n")) }
    foreach ($line in $output) {
        if ($line -notmatch '^(.+?)\((\d+),\d+\): (?:error|warning|info) (\S+): (.+?) \[[^\]]+\]$') { continue }
        $file = ConvertTo-RepoPath $Matches[1]
        $at = [int]$Matches[2]
        # A whitespace fix starts at the end of the previous line (e.g. bad indentation of line N is reported at N-1).
        $hit = @($at, ($at + 1)) | Where-Object { Test-ChangedLine $file $_ } | Select-Object -First 1
        if ($null -ne $hit) { New-Violation $file $hit 'warning' $Matches[3] $Matches[4] }
    }
}

function Invoke-FormatStep {
    $files = @(Get-ChangedFiles '.cs')
    if (-not $files) { return }

    $issues = @(Get-FormatIssues 'whitespace' $files) + @(Get-FormatIssues 'style' $files)
    if (-not $Fix -or -not $issues) { return $issues }

    $toFix = @($issues.File | Sort-Object -Unique)
    if ($issues | Where-Object Rule -eq 'WHITESPACE') {
        & dotnet format whitespace $Target --include @toFix 2>&1 | Out-Null
    }
    $styleRules = @($issues.Rule | Where-Object { $_ -ne 'WHITESPACE' } | Sort-Object -Unique)
    if ($styleRules) {
        & dotnet format style $Target --include @toFix --diagnostics @styleRules 2>&1 | Out-Null
    }
    Write-Host "check: format đã sửa: $($toFix -join ', ')"

    # Some style issues have no code fix; whatever is left still blocks.
    @(Get-FormatIssues 'whitespace' $toFix) + @(Get-FormatIssues 'style' $toFix)
}

function Get-SarifResults {
    # Worktree của subagent nằm trong .claude/worktrees/ có obj/*.sarif riêng; đó không phải code của checkout này.
    $sarifFiles = Get-ChildItem -Path $repoRoot -Recurse -File -Filter '*.sarif' |
        Where-Object { $_.Directory.Name -eq 'obj' -and $_.Name -notlike '*_wpftmp*' -and $_.FullName -notmatch '[\\/]\.claude[\\/]worktrees[\\/]' }
    if (-not $sarifFiles) { Stop-CannotRun 'không tìm thấy file SARIF trong obj/ (thiếu ErrorLog trong Directory.Build.props?)' }

    foreach ($sarif in $sarifFiles) {
        $log = Get-Content -LiteralPath $sarif.FullName -Raw | ConvertFrom-Json
        foreach ($run in $log.runs) {
            foreach ($result in $run.results) {
                if ($result.suppressions) { continue }
                $location = $result.locations | Select-Object -First 1
                $uri = $location.physicalLocation.artifactLocation.uri
                if (-not $uri) { continue }
                New-Violation (ConvertTo-RepoPath ([Uri]$uri).LocalPath) $location.physicalLocation.region.startLine `
                    $result.level $result.ruleId $result.message.text
            }
        }
    }
}

function Get-CleanRules {
    $path = Join-Path $repoRoot $CleanRulesFile
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    @(Get-Content -LiteralPath $path | ForEach-Object { ($_ -replace '#.*$', '').Trim() } | Where-Object { $_ })
}

# Repo source, not generated code in obj/ or files outside the repo.
function Test-SourceFile([string]$File) {
    -not ($File.StartsWith('../') -or $File -match '(^|/)obj/')
}

function Get-AnalyzerViolations {
    $cleanRules = Get-CleanRules
    Get-SarifResults | Where-Object {
        $isSource = Test-SourceFile $_.File
        # Suggestions never block, not even for a clean rule.
        if ($_.Level -notin 'warning', 'error') { $false }
        elseif ($_.Rule -in $cleanRules) { $isSource }
        else { Test-ChangedLine $_.File $_.Line }
    }
}

function Get-XamlViolations {
    foreach ($file in Get-ChangedFiles '.xaml') {
        $lineNumber = 0
        foreach ($text in Get-Content -LiteralPath (Join-Path $repoRoot $file)) {
            $lineNumber++
            if (-not (Test-ChangedLine $file $lineNumber)) { continue }
            foreach ($rule in $ForbiddenXaml) {
                if ($text -match $rule.Pattern) { New-Violation $file $lineNumber 'warning' $rule.Rule $rule.Message }
            }
        }
    }
}

# Returns the names of failed tests; stops when the test run fails without any failed test.
function Get-FailedTests {
    $output = & dotnet test $Target --no-build -nologo 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -eq 0) { return }
    $failed = @($output | ForEach-Object { if ($_ -match '^\s*Failed (\S+) \[') { $Matches[1] } } | Sort-Object -Unique)
    if (-not $failed) { Stop-CannotRun ("dotnet test lỗi`n" + (($output | Select-Object -Last 20) -join "`n")) }
    $failed
}

function Invoke-Build {
    $buildOutput = & dotnet build $Target -nologo -v:q 2>&1
    if ($LASTEXITCODE -ne 0) {
        $errors = $buildOutput | Where-Object { $_ -match ': error ' } | Select-Object -Unique
        if (-not $errors) { $errors = $buildOutput | Select-Object -Last 20 }
        Stop-CannotRun ("build lỗi`n" + ($errors -join "`n"))
    }
}

function Write-WarningSummary {
    $warnings = @(Get-SarifResults |
        Where-Object { $_.Level -in 'warning', 'error' -and (Test-SourceFile $_.File) } |
        Sort-Object File, Line, Rule, Message -Unique)
    Write-Output '| Rule | Warning |'
    Write-Output '|---|---|'
    foreach ($group in $warnings | Group-Object Rule | Sort-Object @{ Expression = 'Count'; Descending = $true }, Name) {
        Write-Output "| $($group.Name) | $($group.Count) |"
    }
    Write-Output "| **Tổng** | **$($warnings.Count)** |"
}

$repoRoot = (Invoke-Git rev-parse --show-toplevel | Select-Object -First 1).Trim()
Push-Location $repoRoot
try {
    if (-not $Target) {
        $solutions = @(Get-ChildItem -File -Path $repoRoot | Where-Object Extension -in '.slnx', '.sln')
        if ($solutions.Count -ne 1) { Stop-CannotRun "cần đúng một file .slnx/.sln ở gốc repo, hoặc truyền -Target" }
        $Target = $solutions[0].FullName
    }

    if ($Summary) {
        Invoke-Build
        Write-WarningSummary
        exit $ExitPass
    }

    $mergeBase = (Invoke-Git merge-base $Base HEAD | Select-Object -First 1).Trim()
    $changed = Get-ChangedLines $mergeBase

    $violations = @(Get-XamlViolations)
    $failedTests = @()
    $needsBuild = @($changed.Keys | Where-Object { [IO.Path]::GetExtension($_) -in $BuildExtensions -or $_ -in $BuildFiles }).Count -gt 0
    if ($needsBuild) {
        $violations += @(Invoke-FormatStep)

        Invoke-Build
        $violations += @(Get-AnalyzerViolations)

        $needsTests = @(Get-ChangedFiles $TestExtensions).Count -gt 0 -or @($changed.Keys | Where-Object { $_ -in $TestFiles }).Count -gt 0
        if ($needsTests) { $failedTests = @(Get-FailedTests) }
    }
    else {
        Write-Host 'check: bỏ qua build và test (không có thay đổi ảnh hưởng tới code)'
    }

    $violations = @($violations | Sort-Object File, Line, Rule, Message -Unique)
    foreach ($v in $violations) {
        Write-Output ('{0}:{1}: {2} {3}: {4}' -f $v.File, $v.Line, $v.Level, $v.Rule, $v.Message)
    }
    foreach ($test in $failedTests) { Write-Output "TEST: $test" }

    if ($violations -or $failedTests) {
        Write-Host "check: $($violations.Count) vi phạm, $($failedTests.Count) test đỏ (so với $Base)"
        exit $ExitViolations
    }
    Write-Host "check: đạt (so với $Base)"
    exit $ExitPass
}
finally {
    Pop-Location
}
