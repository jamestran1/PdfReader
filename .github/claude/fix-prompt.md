# AI sửa góp ý review trên PR của Trí Thư

Chủ repo vừa ra lệnh `@claude fix` trên một PR. Nhánh PR đã được checkout; bạn sửa trên chính nhánh đó.

Đầu prompt có `REPO`, `PR`, `BASE`, `MODE`, `IDS`, `INSTRUCTION` và `COMMIT` (commit message bắt buộc).
Góp ý của lượt review gần nhất nằm trong `.ai-review/findings.md`.

## Sửa gì

- `MODE: ids`: chỉ sửa các góp ý có id trong `IDS`. Không đụng góp ý khác.
- `MODE: blockers`: sửa mọi góp ý 🔴, không đụng 🟡 và 🔵.
- `MODE: free`: làm theo `INSTRUCTION`, giữ thay đổi nhỏ nhất có thể.
- `INSTRUCTION` (nếu có, ở mode ids/blockers) là ghi chú thêm của chủ repo, làm theo.
- Id không có trong `.ai-review/findings.md`: không sửa gì cho id đó và nói rõ trong comment trả lời.
- Thấy góp ý sai hoặc không nên sửa: không sửa, giải thích trong comment trả lời.

## Quy tắc

- Theo `CODING_STANDARDS.md` và `CLAUDE.md` của repo.
- Giữ nguyên dấu tiếng Việt trong mọi chuỗi, comment và tài liệu (UI-7). Không bao giờ viết tiếng Việt thành ASCII.
- Sửa bug thì thêm test tái hiện (TEST-1).
- Chỉ sửa trong phạm vi góp ý; không refactor, không đổi format phần khác.

## Kiểm tra và commit

1. Chạy `pwsh scripts/check.ps1 -Base origin/$BASE -Fix` và sửa đến khi đạt. Khi bạn dừng, hook của repo chạy lại đúng kiểm tra này và sẽ giữ bạn lại nếu còn lỗi.
2. Tạo đúng **một** commit với message là giá trị `COMMIT`, rồi `git push` lên nhánh PR. Không force push, không rebase.
3. Trả lời bằng `gh pr comment $PR --body "..."`: id nào đã sửa và sửa thế nào (mỗi id một dòng), id nào không sửa và vì sao.

Không có gì để sửa thì không commit, chỉ trả lời giải thích.
