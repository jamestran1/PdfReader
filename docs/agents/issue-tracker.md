# Issue tracker: GitHub

PRD, task và map Wayfinder sống trên GitHub issues của `jamestran1/PdfReader`, thao tác qua `gh`. Viết body bằng file UTF-8 và `--body-file`, không pipe qua stdin (chữ tiếng Việt thành `?`).

## Đọc ticket

Số `#n` có thể là PR: `gh issue view <n>` vẫn chạy với PR nên không lộ ra. Kiểm trước bằng `gh pr view <n> --json state,headRefName` (lỗi nghĩa là issue). Với PR, brief triage (Agent Brief, checklist "Còn thiếu") nằm ở comment mới nhất của chủ repo, còn góp ý AI review nằm ở review comment:

| Việc | Lệnh |
|---|---|
| Body và comment | `gh pr view <n> --json title,body,comments --jq '.title, .body, (.comments[] \| "\(.author.login): \(.body)")'` |
| Review comment theo dòng | `gh api repos/jamestran1/PdfReader/pulls/<n>/comments --jq '.[] \| "\(.id) \(.path):\(.line) \(.body)"'` |
| Trả lời review comment | `gh api repos/jamestran1/PdfReader/pulls/<n>/comments/<id>/replies -F body=@<file>` |

Nhánh của PR thường đã có worktree: chạy `git worktree list` trước khi tạo mới.

## Wayfinding operations

Map là issue gắn nhãn `wayfinder:map`; ticket là sub-issue của map, gắn một nhãn `wayfinder:<type>`.

| Việc | Lệnh |
|---|---|
| Liệt kê ticket của map | `gh api repos/jamestran1/PdfReader/issues/<map>/sub_issues --jq '.[] \| {number,title,state,labels:[.labels[].name],assignees:[.assignees[].login]}'` |
| Ticket bị chặn bởi | `gh api repos/jamestran1/PdfReader/issues/<n>/dependencies/blocked_by --jq '[.[] \| {number,state}]'` |
| Gắn ticket vào map | `gh api -X POST repos/jamestran1/PdfReader/issues/<map>/sub_issues -F sub_issue_id=$(gh api repos/jamestran1/PdfReader/issues/<n> --jq .id)` |
| Nối chặn (`<n>` bị `<b>` chặn) | `gh api -X POST repos/jamestran1/PdfReader/issues/<n>/dependencies/blocked_by -F issue_id=$(gh api repos/jamestran1/PdfReader/issues/<b> --jq .id)` |
| Nhận ticket | `gh issue edit <n> --add-assignee jamestran1` |
| Ghi kết luận | `gh issue comment <n> --body-file <file>`, rồi `gh issue close <n> --reason completed` |
| Sửa body map | `gh issue view <map> --json body --jq .body > map.md`, sửa file, `gh issue edit <map> --body-file map.md` |

Cả hai API POST nhận `id` nội bộ của issue (số nguyên lớn), không phải số issue `#n`.

**Frontier** = sub-issue đang mở, chưa có assignee, và mọi issue trong `blocked_by` đã đóng. Lấy ticket đầu tiên theo thứ tự `sub_issues` trả về.
