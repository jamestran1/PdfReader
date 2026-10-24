#requires -Version 7
<#
.SYNOPSIS
    Parse an "@claude fix ..." PR comment for the AI fix job (#132).

.DESCRIPTION
    Returns an object with:
      Mode           ids | blockers | free | ask | none
                       ids       "@claude fix R3 R5" (commas, case and a trailing note allowed)
                       blockers  "@claude fix blockers": every 🔴 finding
                       free      "@claude fix <sentence>"
                       ask       "@claude fix" alone: ask which finding, change nothing
                       none      not a fix command
      Ids            finding ids in upper case (Mode ids)
      Instruction    text after the ids, or the free sentence
      CommitMessage  "fix(review): R3 R5" / "fix(review): blockers" / "fix(review): theo yêu cầu"
#>
param([Parameter(Mandatory)][AllowEmptyString()][string]$Body)

$text = $Body.Trim()
$command = [pscustomobject]@{ Mode = 'none'; Ids = @(); Instruction = ''; CommitMessage = '' }
if ($text -notmatch '^(?i)@claude\s+fix(?=\s|$)(?<rest>[\s\S]*)$') { return $command }
$rest = $Matches.rest.Trim()

if (-not $rest) {
    $command.Mode = 'ask'
}
elseif ($rest -match '^(?i)blockers\b(?<note>[\s\S]*)$') {
    $command.Mode = 'blockers'
    $command.Instruction = $Matches.note.Trim()
    $command.CommitMessage = 'fix(review): blockers'
}
elseif ($rest -match '^(?i)(?<ids>R\d+(?:[\s,]+R\d+)*)\b[\s,]*(?<note>[\s\S]*)$') {
    $command.Mode = 'ids'
    $command.Ids = @([regex]::Matches($Matches.ids, '\d+') | ForEach-Object { "R$($_.Value)" } | Select-Object -Unique)
    $command.Instruction = $Matches.note.Trim()
    $command.CommitMessage = "fix(review): $($command.Ids -join ' ')"
}
else {
    $command.Mode = 'free'
    $command.Instruction = $rest
    $command.CommitMessage = 'fix(review): theo yêu cầu'
}
$command
