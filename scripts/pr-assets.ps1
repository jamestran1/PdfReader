#requires -Version 7
<#
.SYNOPSIS
    Đưa ảnh lên nhánh orphan `pr-assets` (không merge vào main) và in link raw để nhúng vào PR.

.EXAMPLE
    pwsh scripts/pr-assets.ps1 -Pr 160 -Files artifacts/main.png, artifacts/notes.png
    # in ra: ![main.png](https://raw.githubusercontent.com/<owner>/<repo>/pr-assets/pr-160/main.png)
#>
param(
    [Parameter(Mandatory)][int]$Pr,
    [Parameter(Mandatory)][string[]]$Files
)

$ErrorActionPreference = 'Stop'
$branch = 'pr-assets'
$repo = (gh repo view --json nameWithOwner -q .nameWithOwner).Trim()
$sources = $Files | ForEach-Object { (Resolve-Path $_).Path }
$worktree = Join-Path ([IO.Path]::GetTempPath()) "pr-assets-$([guid]::NewGuid().ToString('N').Substring(0, 8))"

git fetch -q origin $branch 2>$null
$exists = $LASTEXITCODE -eq 0
try {
    if ($exists) {
        git worktree add -q $worktree "origin/$branch" 2>&1 | Out-Null
        git -C $worktree checkout -q -B $branch "origin/$branch"
    }
    else {
        git worktree add -q --detach $worktree 2>&1 | Out-Null
        git -C $worktree checkout -q --orphan $branch
        git -C $worktree rm -rq --cached . 2>$null
        Get-ChildItem -Force $worktree | Where-Object Name -ne '.git' | Remove-Item -Recurse -Force
        Set-Content -LiteralPath (Join-Path $worktree 'README.md') -Value "Ảnh đính kèm PR (chụp bằng scripts/snapshot.ps1). Nhánh này không merge vào main."
    }

    $dir = Join-Path $worktree "pr-$Pr"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    foreach ($source in $sources) { Copy-Item -LiteralPath $source -Destination $dir -Force }

    git -C $worktree add -A
    git -C $worktree commit -q -m "assets: PR #$Pr" 2>&1 | Out-Null
    git -C $worktree push -q origin "HEAD:refs/heads/$branch"
    if ($LASTEXITCODE -ne 0) { throw 'push pr-assets thất bại' }

    foreach ($source in $sources) {
        $name = Split-Path $source -Leaf
        Write-Output "![$name](https://raw.githubusercontent.com/$repo/$branch/pr-$Pr/$name)"
    }
}
finally {
    git worktree remove --force $worktree 2>$null
}
