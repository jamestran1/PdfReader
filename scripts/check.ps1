#requires -Version 7
<#
.SYNOPSIS
    Shared check for CI and the Claude Code hook: report analyzer warnings on lines added or changed since the base.

.DESCRIPTION
    Builds the repo (analyzers write SARIF per project, see Directory.Build.props), then keeps only
    warnings that sit on lines added or changed relative to `git merge-base <Base> HEAD`, including
    uncommitted and untracked files. Warnings on untouched lines are ignored (ADR 0007).

    Exit codes:
      0  pass
      1  blocking violations found (one line each on stdout: <file>:<line>: <level> <rule>: <message>)
      2  could not run (build failed, git error, no SARIF); never reported as a pass

.PARAMETER Base
    Git ref to compare against. Default origin/main.

.PARAMETER Target
    Solution or project to build. Default: the single *.slnx / *.sln at the repo root.
#>
[CmdletBinding()]
param(
    [string]$Base = 'origin/main',
    [string]$Target
)

$ErrorActionPreference = 'Stop'
# Vietnamese messages must survive the pipe to CI logs and the hook.
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$ExitPass = 0
$ExitViolations = 1
$ExitCannotRun = 2

function Stop-CannotRun([string]$Message) {
    Write-Host "check: không chạy được: $Message"
    exit $ExitCannotRun
}

function Invoke-Git {
    $output = & git -c core.quotepath=off @args 2>&1
    if ($LASTEXITCODE -ne 0) { Stop-CannotRun "git $($args -join ' '): $output" }
    # Drop stderr chatter (e.g. CRLF notices) so callers only see stdout.
    $output | Where-Object { $_ -isnot [Management.Automation.ErrorRecord] }
}

# Returns a map: repo-relative path (forward slashes, lower case) -> HashSet[int] of added/changed lines,
# or $null for files that are new and untracked (every line counts).
function Get-ChangedLines([string]$MergeBase) {
    $changed = @{}
    $file = $null
    foreach ($line in Invoke-Git diff --unified=0 --no-color --no-ext-diff --no-renames $MergeBase) {
        if ($line -match '^\+\+\+ (?:b/)?(.+)$') {
            $file = if ($Matches[1] -eq '/dev/null') { $null } else { $Matches[1].ToLowerInvariant() }
            if ($file -and -not $changed.ContainsKey($file)) { $changed[$file] = [Collections.Generic.HashSet[int]]::new() }
        }
        elseif ($file -and $line -match '^@@ -\S+ \+(\d+)(?:,(\d+))? @@') {
            $start = [int]$Matches[1]
            $count = if ($null -ne $Matches[2]) { [int]$Matches[2] } else { 1 }
            for ($i = $start; $i -lt $start + $count; $i++) { [void]$changed[$file].Add($i) }
        }
    }
    foreach ($untracked in Invoke-Git ls-files --others --exclude-standard) {
        $changed[$untracked.ToLowerInvariant()] = $null
    }
    $changed
}

function Get-SarifResults([string]$RepoRoot) {
    $sarifFiles = Get-ChildItem -Path $RepoRoot -Recurse -File -Filter '*.sarif' |
        Where-Object { $_.Directory.Name -eq 'obj' -and $_.Name -notlike '*_wpftmp*' }
    if (-not $sarifFiles) { Stop-CannotRun 'không tìm thấy file SARIF trong obj/ (thiếu ErrorLog trong Directory.Build.props?)' }

    foreach ($sarif in $sarifFiles) {
        $log = Get-Content -LiteralPath $sarif.FullName -Raw | ConvertFrom-Json
        foreach ($run in $log.runs) {
            foreach ($result in $run.results) {
                if ($result.suppressions) { continue }
                $location = $result.locations | Select-Object -First 1
                $uri = $location.physicalLocation.artifactLocation.uri
                if (-not $uri) { continue }
                $path = [IO.Path]::GetRelativePath($RepoRoot, ([Uri]$uri).LocalPath) -replace '\\', '/'
                [pscustomobject]@{
                    File    = $path
                    Line    = [int]$location.physicalLocation.region.startLine
                    Level   = $result.level
                    Rule    = $result.ruleId
                    Message = ($result.message.text -replace '\s+', ' ').Trim()
                }
            }
        }
    }
}

$repoRoot = (Invoke-Git rev-parse --show-toplevel | Select-Object -First 1).Trim()
Push-Location $repoRoot
try {
    if (-not $Target) {
        $solutions = @(Get-ChildItem -File -Path $repoRoot | Where-Object Extension -in '.slnx', '.sln')
        if ($solutions.Count -ne 1) { Stop-CannotRun "cần đúng một file .slnx/.sln ở gốc repo, hoặc truyền -Target" }
        $Target = $solutions[0].FullName
    }

    $mergeBase = (Invoke-Git merge-base $Base HEAD | Select-Object -First 1).Trim()
    $changed = Get-ChangedLines $mergeBase

    $buildOutput = & dotnet build $Target -nologo -v:q 2>&1
    if ($LASTEXITCODE -ne 0) {
        $errors = $buildOutput | Where-Object { $_ -match ': error ' } | Select-Object -Unique
        if (-not $errors) { $errors = $buildOutput | Select-Object -Last 20 }
        Stop-CannotRun ("build lỗi`n" + ($errors -join "`n"))
    }

    $violations = Get-SarifResults $repoRoot |
        Where-Object { $_.Level -in 'warning', 'error' } |
        Where-Object {
            $key = $_.File.ToLowerInvariant()
            $changed.ContainsKey($key) -and ($null -eq $changed[$key] -or $changed[$key].Contains($_.Line))
        } |
        Sort-Object File, Line, Rule, Message -Unique

    foreach ($v in $violations) {
        Write-Output ('{0}:{1}: {2} {3}: {4}' -f $v.File, $v.Line, $v.Level, $v.Rule, $v.Message)
    }

    if ($violations) {
        Write-Host "check: $(@($violations).Count) vi phạm trên dòng thêm/sửa so với $Base"
        exit $ExitViolations
    }
    Write-Host "check: đạt (không có warning trên dòng thêm/sửa so với $Base)"
    exit $ExitPass
}
finally {
    Pop-Location
}
