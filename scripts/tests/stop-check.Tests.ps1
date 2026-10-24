#requires -Version 7
#requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0' }
# Runs .claude/hooks/stop-check.ps1 against a throwaway git repo with a fake check script.
# Run: Invoke-Pester scripts/tests

BeforeAll {
    $script:RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
    $script:Hook = Join-Path $RepoRoot '.claude/hooks/stop-check.ps1'
    $script:Sandbox = Join-Path ([IO.Path]::GetTempPath()) "stop-check-tests-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $script:Work = Join-Path $Sandbox 'repo'
    $script:FakeCheck = Join-Path $Sandbox 'fake-check.ps1'
    $script:CallLog = Join-Path $Sandbox 'calls.log'
    $script:OriginalEncoding = [Console]::OutputEncoding
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

    # Fake check: logs each call, then prints FAKE_OUTPUT, sleeps FAKE_SLEEP seconds and exits FAKE_EXIT.
    New-Item -ItemType Directory -Path $Work -Force | Out-Null
    Set-Content -LiteralPath $FakeCheck -Value @'
param([string]$Base, [switch]$Fix)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Content -LiteralPath $env:FAKE_LOG -Value "start $Base fix=$Fix"
if ($env:FAKE_SLEEP) { Start-Sleep -Seconds ([int]$env:FAKE_SLEEP) }
if ($env:FAKE_OUTPUT) { $env:FAKE_OUTPUT -split '\|' | ForEach-Object { Write-Output $_ } }
Add-Content -LiteralPath $env:FAKE_LOG -Value 'end'
exit ([int]$env:FAKE_EXIT)
'@

    function Invoke-SandboxGit {
        & git -C $Work @args 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "git $args failed" }
    }

    function Write-WorkFile([string]$Path, [string]$Content) {
        $full = Join-Path $Work $Path
        New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
        Set-Content -LiteralPath $full -Value $Content -NoNewline
    }

    # Starts the hook as Claude Code would: JSON on stdin, cwd in the payload.
    function Start-Hook([string]$Event = 'Stop', [bool]$StopHookActive = $false, [string]$Transcript = 'main.jsonl') {
        $payload = @{
            session_id       = 'test-session'
            transcript_path  = (Join-Path $Sandbox $Transcript)
            cwd              = $Work
            hook_event_name  = $Event
            stop_hook_active = $StopHookActive
        } | ConvertTo-Json -Compress
        $info = [Diagnostics.ProcessStartInfo]::new('pwsh', @('-NoProfile', '-File', $Hook))
        $info.RedirectStandardInput = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $info.StandardOutputEncoding = [Text.Encoding]::UTF8
        $info.StandardErrorEncoding = [Text.Encoding]::UTF8
        $info.WorkingDirectory = $Work
        $process = [Diagnostics.Process]::Start($info)
        $process.StandardInput.Write($payload)
        $process.StandardInput.Close()
        [pscustomobject]@{ Process = $process; Stdout = $process.StandardOutput.ReadToEndAsync(); Stderr = $process.StandardError.ReadToEndAsync() }
    }

    function Wait-Hook($Started) {
        $Started.Process.WaitForExit()
        [pscustomobject]@{ ExitCode = $Started.Process.ExitCode; Stdout = $Started.Stdout.Result; Stderr = $Started.Stderr.Result }
    }

    function Invoke-Hook([string]$Event = 'Stop', [bool]$StopHookActive = $false) {
        Wait-Hook (Start-Hook -Event $Event -StopHookActive $StopHookActive)
    }

    function Get-CallCount {
        if (-not (Test-Path $CallLog)) { return 0 }
        @(Get-Content -LiteralPath $CallLog | Where-Object { $_ -like 'start*' }).Count
    }

    Invoke-SandboxGit init -q -b main
    Invoke-SandboxGit config user.email 'hook@test'
    Invoke-SandboxGit config user.name 'hook'
    Write-WorkFile 'src/Calc.cs' "namespace Lib;`n"
    Write-WorkFile 'README.md' "# Lib`n"
    Invoke-SandboxGit add -A
    Invoke-SandboxGit commit -q -m 'baseline'
}

AfterAll {
    [Console]::OutputEncoding = $OriginalEncoding
    foreach ($name in 'TRITHU_CHECK_SCRIPT', 'TRITHU_CHECK_BASE', 'TRITHU_HOOK_TIMEOUT', 'TRITHU_HOOKS', 'FAKE_LOG', 'FAKE_EXIT', 'FAKE_OUTPUT', 'FAKE_SLEEP') {
        Remove-Item "env:$name" -ErrorAction SilentlyContinue
    }
    if ($Sandbox -and (Test-Path $Sandbox)) { Remove-Item -Recurse -Force $Sandbox }
}

Describe 'stop-check hook' {
    BeforeEach {
        Invoke-SandboxGit checkout -q -f main
        Invoke-SandboxGit clean -q -fdx
        Remove-Item -LiteralPath (Join-Path $Work '.git/trithu-hooks') -Recurse -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $CallLog -ErrorAction SilentlyContinue
        $env:TRITHU_CHECK_SCRIPT = $FakeCheck
        $env:TRITHU_CHECK_BASE = 'main'
        $env:TRITHU_HOOK_TIMEOUT = '60'
        $env:FAKE_LOG = $CallLog
        $env:FAKE_EXIT = '0'
        $env:FAKE_OUTPUT = 'check: đạt (so với main)'
        Remove-Item env:FAKE_SLEEP, env:TRITHU_HOOKS -ErrorAction SilentlyContinue
    }

    It 'does nothing when TRITHU_HOOKS=off' {
        Write-WorkFile 'src/Calc.cs' "namespace Lib;`n// changed`n"
        $env:TRITHU_HOOKS = 'off'

        $result = Invoke-Hook

        $result.ExitCode | Should -Be 0
        Get-CallCount | Should -Be 0
    }

    It 'lets the agent stop without a build when only markdown changed' {
        Write-WorkFile 'README.md' "# Lib`nmore`n"

        $result = Invoke-Hook

        $result.ExitCode | Should -Be 0
        Get-CallCount | Should -Be 0
    }

    It 'blocks the stop with file, line and rule when the check finds violations' {
        Write-WorkFile 'src/Calc.cs' "namespace Lib;`npublic class Bad_Name {}`n"
        $env:FAKE_EXIT = '1'
        $env:FAKE_OUTPUT = 'src/Calc.cs:2: warning CA1707: Remove the underscores|check: 1 vi phạm, 0 test đỏ (so với main)'

        $result = Invoke-Hook

        $result.ExitCode | Should -Be 2 -Because $result.Stderr
        $result.Stderr | Should -Match 'src/Calc\.cs:2: warning CA1707: Remove the underscores'
        Get-Content -LiteralPath $CallLog | Should -Contain 'start main fix=True'
    }

    It 'blocks a subagent stop and names the failing test' {
        Write-WorkFile 'src/Calc.cs' "namespace Lib;`n// changed`n"
        $env:FAKE_EXIT = '1'
        $env:FAKE_OUTPUT = 'TEST: Lib.Tests.CalcTests.Add_ReturnsSum|check: 0 vi phạm, 1 test đỏ (so với main)'

        $result = Invoke-Hook -Event 'SubagentStop'

        $result.ExitCode | Should -Be 2 -Because $result.Stderr
        $result.Stderr | Should -Match 'TEST: Lib\.Tests\.CalcTests\.Add_ReturnsSum'
    }

    It 'blocks on a build error' {
        Write-WorkFile 'src/Calc.cs' "namespace Lib;`nint x = ;`n"
        $env:FAKE_EXIT = '2'
        $env:FAKE_OUTPUT = 'check: không chạy được: build lỗi|src/Calc.cs(2,9): error CS1525: Invalid expression term'

        $result = Invoke-Hook

        $result.ExitCode | Should -Be 2 -Because $result.Stderr
        $result.Stderr | Should -Match 'error CS1525'
    }

    It 'lets the agent stop after an auto format and reports the fixed files on one line' {
        Write-WorkFile 'src/Calc.cs' "namespace Lib;`n  // changed`n"
        $env:FAKE_OUTPUT = 'check: format đã sửa: src/Calc.cs|check: đạt (so với main)'

        $result = Invoke-Hook

        $result.ExitCode | Should -Be 0 -Because $result.Stderr
        ($result.Stdout | ConvertFrom-Json).systemMessage | Should -Be 'check: format đã sửa: src/Calc.cs'
    }

    It 'skips the check when nothing changed since the last passing check' {
        Write-WorkFile 'src/Calc.cs' "namespace Lib;`n// changed`n"
        (Invoke-Hook).ExitCode | Should -Be 0

        $second = Invoke-Hook

        $second.ExitCode | Should -Be 0
        Get-CallCount | Should -Be 1
    }

    It 'lets the agent stop on the 4th consecutive block with a summary of what is left' {
        Write-WorkFile 'src/Calc.cs' "namespace Lib;`npublic class Bad_Name {}`n"
        $env:FAKE_EXIT = '1'
        $env:FAKE_OUTPUT = 'src/Calc.cs:2: warning CA1707: Remove the underscores'

        (Invoke-Hook -StopHookActive $false).ExitCode | Should -Be 2
        (Invoke-Hook -StopHookActive $true).ExitCode | Should -Be 2
        (Invoke-Hook -StopHookActive $true).ExitCode | Should -Be 2
        $fourth = Invoke-Hook -StopHookActive $true

        $fourth.ExitCode | Should -Be 0 -Because $fourth.Stderr
        ($fourth.Stdout | ConvertFrom-Json).systemMessage | Should -Match 'src/Calc\.cs:2: warning CA1707'
    }

    It 'starts counting again after a fresh stop' {
        Write-WorkFile 'src/Calc.cs' "namespace Lib;`npublic class Bad_Name {}`n"
        $env:FAKE_EXIT = '1'
        $env:FAKE_OUTPUT = 'src/Calc.cs:2: warning CA1707: Remove the underscores'
        1..3 | ForEach-Object { (Invoke-Hook -StopHookActive ($_ -gt 1)).ExitCode | Should -Be 2 }

        $fresh = Invoke-Hook -StopHookActive $false

        $fresh.ExitCode | Should -Be 2
    }

    It 'fails open with a warning when the check crashes' {
        Write-WorkFile 'src/Calc.cs' "namespace Lib;`n// changed`n"
        $env:FAKE_EXIT = '2'
        $env:FAKE_OUTPUT = 'check: không chạy được: git merge-base failed'

        $result = Invoke-Hook

        $result.ExitCode | Should -Be 0 -Because $result.Stderr
        ($result.Stdout | ConvertFrom-Json).systemMessage | Should -Match 'chưa kiểm tra được'
    }

    It 'fails open with a warning when the check runs past the timeout' {
        Write-WorkFile 'src/Calc.cs' "namespace Lib;`n// changed`n"
        $env:TRITHU_HOOK_TIMEOUT = '2'
        $env:FAKE_SLEEP = '30'

        $elapsed = Measure-Command { $script:result = Invoke-Hook }

        $result.ExitCode | Should -Be 0 -Because $result.Stderr
        ($result.Stdout | ConvertFrom-Json).systemMessage | Should -Match 'chưa kiểm tra được'
        $elapsed.TotalSeconds | Should -BeLessThan 20
    }

    It 'makes a parallel stop wait for the running one, and both get the result' {
        Write-WorkFile 'src/Calc.cs' "namespace Lib;`npublic class Bad_Name {}`n"
        $env:FAKE_EXIT = '1'
        $env:FAKE_SLEEP = '3'
        $env:FAKE_OUTPUT = 'src/Calc.cs:2: warning CA1707: Remove the underscores'

        $first = Start-Hook -Event 'SubagentStop' -Transcript 'agent-a.jsonl'
        $second = Start-Hook -Event 'SubagentStop' -Transcript 'agent-b.jsonl'
        $results = @(Wait-Hook $first), @(Wait-Hook $second)

        $results | ForEach-Object {
            $_.ExitCode | Should -Be 2 -Because $_.Stderr
            $_.Stderr | Should -Match 'CA1707'
        }
        # Runs never overlap: every start is followed by its end.
        (Get-Content -LiteralPath $CallLog) -join ',' | Should -Match '^(start [^,]+,end,?)+$'
    }
}
