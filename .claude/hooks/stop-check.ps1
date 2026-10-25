#requires -Version 7
<#
.SYNOPSIS
    Claude Code Stop / SubagentStop hook: run scripts/check.ps1 -Fix before the agent may stop.

.DESCRIPTION
    Thin shell around the shared check (#129). Reads the hook JSON from stdin, then:
      - TRITHU_HOOKS=off, no code changes vs the base, or same diff fingerprint as the last
        passing check: let the agent stop at once.
      - check passes (maybe after auto format): let it stop; fixed files go to systemMessage.
      - violations, build errors or failing tests: exit 2, the list goes to the agent on stderr.
        At most 3 blocks in a row; the 4th stop is let through with a summary for the user.
      - check crashes or runs past the timeout: fail open with "chưa kiểm tra được". CI is the last gate.
    A lock file makes parallel subagents in one worktree wait for each other.

    Environment (defaults in brackets):
      TRITHU_HOOKS          off = do nothing
      TRITHU_CHECK_SCRIPT   check script to run [<repo>/scripts/check.ps1]
      TRITHU_CHECK_BASE     base ref [origin/main]
      TRITHU_HOOK_TIMEOUT   seconds before the check is killed [180]
#>

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$MaxBlocks = 3
$LockWaitSeconds = 300
# Paths that can change what the check reports; anything else (markdown...) never needs a build.
$CodePathspecs = '*.cs', '*.xaml', '*.csproj', '*.props', '*.targets', '*.slnx', '*.sln', '.editorconfig', 'global.json',
    'CodeMetricsConfig.txt'

# Let the agent stop; the message is shown to the user, not the agent.
function Exit-Allow([string]$Message) {
    if ($Message) { @{ systemMessage = $Message } | ConvertTo-Json -Compress | Write-Output }
    exit 0
}

# Keep the agent working; stderr is fed back to it.
function Exit-Block([string]$Message) {
    [Console]::Error.WriteLine($Message)
    exit 2
}

# A broken hook must never trap the session.
trap { Exit-Allow "check: chưa kiểm tra được (hook lỗi: $_)" }

function Invoke-Git {
    $output = & git -C $repoRoot -c core.quotepath=off @args 2>$null
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') thất bại" }
    $output
}

# Hash of everything in the code diff vs the merge-base, including untracked files; $null when there is none.
function Get-DiffFingerprint([string]$Base) {
    $mergeBase = Invoke-Git merge-base $Base HEAD
    $diff = @(Invoke-Git diff --no-color --no-ext-diff $mergeBase '--' @CodePathspecs)
    $untracked = @(Invoke-Git ls-files --others --exclude-standard '--' @CodePathspecs)
    if (-not $diff -and -not $untracked) { return $null }
    $hashes = $untracked | ForEach-Object { "$_ $(Invoke-Git hash-object '--' $_)" }
    $text = (@($mergeBase) + $diff + $hashes) -join "`n"
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
}

# Runs the check with a timeout; returns @{ ExitCode; Output } or $null on timeout.
function Invoke-Check([string]$Script, [string]$Base, [int]$TimeoutSeconds) {
    $info = [Diagnostics.ProcessStartInfo]::new('pwsh', [string[]]@('-NoProfile', '-File', $Script, '-Base', $Base, '-Fix'))
    $info.WorkingDirectory = $repoRoot
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [Text.Encoding]::UTF8
    $process = [Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        return $null
    }
    $lines = ($stdout.Result + $stderr.Result) -split "`r?`n" | Where-Object { $_ }
    @{ ExitCode = $process.ExitCode; Output = @($lines) }
}

function Save-State($State) {
    $State | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath
}

function Read-State([string]$Path) {
    if (Test-Path -LiteralPath $Path) { Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable }
    else { @{ Fingerprint = $null; ExitCode = $null; Output = @(); Blocks = @{} } }
}

$hookInput = [Console]::In.ReadToEnd() | ConvertFrom-Json
if ($env:TRITHU_HOOKS -eq 'off') { exit 0 }

$repoRoot = (& git -C $hookInput.cwd rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0) { exit 0 }
$gitDir = (& git -C $repoRoot rev-parse --absolute-git-dir)
$stateDir = Join-Path $gitDir 'trithu-hooks'
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$statePath = Join-Path $stateDir 'state.json'

$checkScript = if ($env:TRITHU_CHECK_SCRIPT) { $env:TRITHU_CHECK_SCRIPT } else { Join-Path $repoRoot 'scripts/check.ps1' }
$base = if ($env:TRITHU_CHECK_BASE) { $env:TRITHU_CHECK_BASE } else { 'origin/main' }
$timeout = if ($env:TRITHU_HOOK_TIMEOUT) { [int]$env:TRITHU_HOOK_TIMEOUT } else { 180 }
# Each agent has its own block counter. Subagents share the session transcript_path, so prefer agent_id.
$agentId = if ($hookInput.agent_id) { $hookInput.agent_id } elseif ($hookInput.agent_transcript_path) { $hookInput.agent_transcript_path } else { $hookInput.transcript_path }
$agentKey = "$($hookInput.hook_event_name)|$agentId"

# Parallel subagents in one worktree: wait for the running check instead of building over it.
$lock = $null
$deadline = (Get-Date).AddSeconds($LockWaitSeconds)
while (-not $lock) {
    try { $lock = [IO.File]::Open((Join-Path $stateDir 'lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch [IO.IOException] {
        if ((Get-Date) -gt $deadline) { Exit-Allow 'check: chưa kiểm tra được (chờ khoá quá lâu)' }
        Start-Sleep -Milliseconds 250
    }
}

try {
    $state = Read-State $statePath
    $fingerprint = Get-DiffFingerprint $base
    if (-not $fingerprint) { exit 0 }

    $blocks = if ($hookInput.stop_hook_active) { [int]$state.Blocks[$agentKey] } else { 0 }

    if ($fingerprint -eq $state.Fingerprint) {
        # Nothing changed since the last check: pass again, or repeat its findings without rebuilding.
        if ($state.ExitCode -eq 0) { exit 0 }
        $result = @{ ExitCode = $state.ExitCode; Output = $state.Output }
    }
    else {
        $result = Invoke-Check $checkScript $base $timeout
        if (-not $result) { Exit-Allow "check: chưa kiểm tra được (quá $timeout giây)" }
        # -Fix may have rewritten files, so fingerprint the tree as it is now.
        $state.Fingerprint = Get-DiffFingerprint $base
        $state.ExitCode = $result.ExitCode
        $state.Output = $result.Output
    }

    # Exit codes and line formats are check.ps1's contract (see its help).
    $isBuildError = $result.ExitCode -eq 2 -and ($result.Output -match 'build lỗi')
    $hasFindings = $result.ExitCode -eq 1 -and ($result.Output -match '^(.+?):\d+: \w+ \S+: |^TEST: ')
    if ($result.ExitCode -eq 0) {
        $state.Blocks[$agentKey] = 0
        Save-State $state
        Exit-Allow (@($result.Output -match '^check: format đã sửa') -join "`n")
    }
    # Exit 1 without any finding means the script died early (e.g. a parse error), not a real violation.
    if (-not $hasFindings -and -not $isBuildError) {
        # Do not cache a broken run; the next stop retries.
        $state.Fingerprint = $null
        Save-State $state
        Exit-Allow ("check: chưa kiểm tra được (mã thoát $($result.ExitCode))`n" + ($result.Output -join "`n"))
    }

    $findings = $result.Output -join "`n"
    if ($blocks -ge $MaxBlocks) {
        $state.Blocks[$agentKey] = 0
        Save-State $state
        Exit-Allow "check: đã chặn $MaxBlocks lần liên tiếp, cho dừng. Lỗi còn lại cần người xử lý:`n$findings"
    }
    $state.Blocks[$agentKey] = $blocks + 1
    Save-State $state
    Exit-Block "check chặn dừng (lần $($blocks + 1)/$MaxBlocks). Sửa các lỗi sau rồi dừng lại:`n$findings"
}
finally {
    $lock.Dispose()
}
