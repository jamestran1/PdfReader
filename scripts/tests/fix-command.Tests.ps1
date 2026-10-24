#requires -Version 7
#requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0' }
# scripts/ai/Get-FixCommand.ps1: turns an "@claude fix ..." PR comment into what the fix job does.
# Run: Invoke-Pester scripts/tests

BeforeAll {
    $script:Parser = Join-Path $PSScriptRoot "../ai/Get-FixCommand.ps1"
    function Get-FixCommand([string]$Body) { & $Parser -Body $Body }
}

Describe 'Get-FixCommand' {
    It 'takes the listed finding ids' {
        $command = Get-FixCommand '@claude fix R3 R5'

        $command.Mode | Should -Be 'ids'
        $command.Ids | Should -Be @('R3', 'R5')
        $command.CommitMessage | Should -Be 'fix(review): R3 R5'
    }

    It 'accepts commas, lower case and a trailing note after the ids' {
        $command = Get-FixCommand "@Claude fix r1, R2 nhưng giữ tên biến cũ"

        $command.Mode | Should -Be 'ids'
        $command.Ids | Should -Be @('R1', 'R2')
        $command.Instruction | Should -Be 'nhưng giữ tên biến cũ'
    }

    It 'takes every blocker' {
        $command = Get-FixCommand '@claude fix blockers'

        $command.Mode | Should -Be 'blockers'
        $command.CommitMessage | Should -Be 'fix(review): blockers'
    }

    It 'asks back when no finding is named' {
        (Get-FixCommand '@claude fix').Mode | Should -Be 'ask'
        (Get-FixCommand "  @claude fix  `n").Mode | Should -Be 'ask'
    }

    It 'accepts a free sentence' {
        $command = Get-FixCommand '@claude fix đổi tên biến cho rõ nghĩa'

        $command.Mode | Should -Be 'free'
        $command.Instruction | Should -Be 'đổi tên biến cho rõ nghĩa'
        $command.CommitMessage | Should -Be 'fix(review): theo yêu cầu'
    }

    It 'ignores comments that are not a fix command' {
        (Get-FixCommand '@claude review').Mode | Should -Be 'none'
        (Get-FixCommand 'nhờ @claude fix R1 giúp').Mode | Should -Be 'none'
        (Get-FixCommand '@claude fixes').Mode | Should -Be 'none'
    }
}
