# Research: Claude hay Gemini cho AI review trên PR

Ticket: #115 (map #114 "Review code tự động"). Ngày tra cứu: 2026-10-24.
Phạm vi: chỉ thu thập sự thật cho quyết định, KHÔNG đưa ra lựa chọn.
Bối cảnh: repo public, chủ repo một mình (`jamestran1`), lớp AI chỉ góp ý (không chặn merge), tự sửa chỉ khi chủ repo comment đồng ý.

Mọi khẳng định có nguồn ở cuối mỗi mục ([S1], [S2]...). Danh sách nguồn ở cuối file.

---

## 1. Claude

### 1.1 Các lựa chọn chính thức

| Lựa chọn | Mô tả | Dùng được cho tài khoản cá nhân? |
|---|---|---|
| `anthropics/claude-code-action` (Claude Code GitHub Actions) | GitHub Action chạy Claude Code trên runner của mình; mention `@claude` hoặc chạy tự động với `prompt` | Có (API key hoặc token subscription) [S1][S2] |
| Code Review (managed) | Review tự động chạy trên hạ tầng Anthropic, inline comment + check run | Không: chỉ gói Team và Enterprise [S3] |

Phiên bản action: `v1` (tag di động), bản mới nhất `v1.0.261` phát hành 2026-10-23 [S4]. Action cập nhật rất thường xuyên.

### 1.2 Xác thực cho tài khoản cá nhân

Hai cách, lưu dưới dạng repo secret [S1]:

- `ANTHROPIC_API_KEY`: API key từ Claude Console (`platform.claude.com`), tính tiền theo token.
- `CLAUDE_CODE_OAUTH_TOKEN`: token OAuth gắn với gói subscription, "available on Pro, Max, Team, and Enterprise plans". Tạo bằng `claude setup-token` (token có hạn **một năm**) [S1][S5]. Khi dùng token này, "runs use your Claude subscription instead of API billing" [S1].
- Ngoài ra: workload identity federation (OIDC, không lưu secret dài hạn) qua Claude Console service account, hoặc Bedrock / Google Agent Platform / Microsoft Foundry [S1].

Cài đặt: `/install-github-app` trong Claude Code (cần quyền admin repo, cần `gh`), hoặc thủ công: cài Claude GitHub App + thêm secret + copy `examples/claude.yml` [S1]. Lưu ý: GitHub App chính thức xin bộ quyền rộng (Actions RW, Workflows RW, Repository hooks RW, Administration R...) và không cho chấp nhận một phần; muốn tối thiểu quyền thì tạo custom GitHub App chỉ với Contents, Issues, Pull requests [S1].

### 1.3 Chi phí ước tính mỗi PR

- Tài liệu chính thức **không đưa con số cho mỗi PR**. Chỉ nói chi phí gồm (a) phút GitHub Actions và (b) token API, phụ thuộc độ dài prompt, độ phức tạp, kích thước codebase; khuyên dùng `--max-turns`, timeout workflow, concurrency [S1].
- Phút GitHub Actions: runner chuẩn GitHub-hosted **miễn phí cho repo public** [S6].
- Giá token (Claude API, per MTok, input / output): Sonnet 5 $2 / $10, Sonnet 5.5 $2 / $10, Sonnet 4.6 $3 / $15, Opus 5.5 $4 / $20; cache hit rẻ hơn nhiều (vd Sonnet 5 $0.20) [S7].
- Mốc tham khảo duy nhất từ docs: một phiên ví dụ trên `claude-sonnet-4-6` với 1.2k input, 5.3k output, 940k cache read, 50k cache write có tổng **$0.55** [S8]. Trung bình doanh nghiệp khoảng $13 / dev / ngày hoạt động [S8] (không phải số per-PR).
- Ước lượng thô (KHÔNG phải số chính thức, tự suy từ bảng giá): một lượt review PR cỡ vừa với Sonnet, vài trăm nghìn token chủ yếu là cache read cộng 10-20k output, rơi vào khoảng **$0.3 đến $1.5 / lượt**. Một vòng "review + tự sửa" có thể gấp 2-3 lần. Cần đo thực tế bằng vài PR thử.
- Nếu dùng `CLAUDE_CODE_OAUTH_TOKEN`: không phát sinh tiền API, nhưng tiêu vào hạn mức của gói subscription (chung với việc dùng Claude Code hằng ngày).

### 1.4 Kích hoạt khi mở / cập nhật PR

- Automation mode: có input `prompt` thì chạy không cần mention [S1].
- Ví dụ chính thức: `on: pull_request: types: [opened, synchronize, ready_for_review, reopened]` + plugin `code-review@claude-code-plugins` + `prompt: "/code-review:code-review --comment <repo>/pull/<n>"` + `claude_args: '--allowedTools "mcp__github_inline_comment__create_inline_comment"'`. Với `--comment`, Claude post inline comment cho từng vấn đề hoặc một comment tóm tắt nếu không có gì [S1].
- Skill này bỏ qua PR draft, PR đã đóng, PR "không cần review" (tự động / tầm thường) và PR **đã có comment từ Claude** [S1]. Điểm cuối này quan trọng cho trigger `synchronize`: lần push sau có thể bị bỏ qua nếu không chỉnh prompt.

### 1.5 Kích hoạt bằng comment để tự sửa và push

- Interactive mode (không có `prompt`): chờ `@claude` trong comment issue / PR, review comment, hoặc body/title issue mới [S1]. `@claude` phải là nguyên từ; đổi được bằng `trigger_phrase` [S1][S9].
- Claude "analyze code, implement changes, and push commits" [S1]. Khi chạy trên PR, branch của PR đã được checkout [S10]. Khi từ issue, Claude commit lên branch mới (tiền tố `claude/`) và chỉ đưa link tạo PR, **không tự tạo PR** [S11][S12].
- Commit của Claude mặc định không ký; bật `use_commit_signing: true` hoặc `ssh_signing_key` [S11].
- Lưu ý CI: commit đẩy bằng `GITHUB_TOKEN` mặc định không kích hoạt workflow khác; để CI chạy trên commit của Claude thì đừng truyền `github_token` (dùng Claude App) hoặc dùng token app riêng [S1][S9].

### 1.6 Đọc issue liên kết và tài liệu trong repo

- Tài liệu repo: có `actions/checkout` nên đọc được toàn bộ file; docs khuyên đặt quy ước review trong `CLAUDE.md` gốc repo, Claude đọc nó mỗi lần chạy [S1]. Repo này đã có `CLAUDE.md` và `CONTEXT.md`, `docs/adr/`.
- Trên PR, `CLAUDE.md`, `.claude/`, `.mcp.json`... được **khôi phục từ base branch** (không lấy bản PR sửa) để chống PR tự đổi cấu hình [S11].
- Issue liên kết: docs **không nói Claude tự động nạp issue liên kết**. Interactive mode giữ ngữ cảnh PR / issue kích hoạt; muốn đọc issue khác (vd `Closes #N`) phải cho phép tool (vd `gh issue view` hoặc GitHub MCP `issue_read`) qua `--allowedTools` và nhắc trong prompt [S1][S13]. Đây là suy luận từ cơ chế tool, chưa có câu khẳng định trực tiếp.

### 1.7 Ai được kích hoạt trên repo public

- Mặc định chỉ người có **quyền write** kích hoạt được (kiểm tra trên issue, PR, comment, review, `workflow_run`) [S11][S1].
- Bot / GitHub App bị chặn mặc định; `allowed_bots` mở ra nhưng bot trong danh sách **không bị kiểm quyền**, cảnh báo đặc biệt cho repo public [S11].
- `allowed_non_write_users` bỏ qua kiểm quyền: docs gọi là "significant security risk" [S11].
- PR từ fork: GitHub không cấp secret cho run từ fork PR trên repo public, nên review tự động chỉ chạy với PR từ branch cùng repo [S1]. Với `pull_request_target` / `workflow_run`: không checkout ref không tin cậy vào workspace gốc [S11].
- Prompt injection: action lọc HTML comment, ký tự ẩn, alt text..., có `include_comments_by_actor` để chỉ nạp comment của người được phép (vd chỉ `jamestran1`) [S11].
- Log: `show_full_output` tắt mặc định vì log Actions **public** trên repo public; tự bật nếu đặt `ACTIONS_STEP_DEBUG` [S11].

---

## 2. Gemini

### 2.1 Thay đổi lớn năm 2026 (cần đọc trước)

- **Từ 2026-06-18**, Gemini Code Assist IDE extensions **và Gemini CLI ngừng phục vụ** các tier Gemini Code Assist for individuals, **Google AI Pro** và Google AI Ultra; không còn dùng được "Login with Google" cho Gemini CLI. Người dùng được hướng sang Antigravity / Antigravity CLI [S14][S15].
- **Từ 2026-10-09**, ngừng bán mới Gemini Code Assist Standard / Enterprise; gói hiện có gia hạn hết 2026, ngừng gia hạn năm 2027 [S15].
- Trang GitHub App "Gemini Code Assist" ghi: "This app is only available for Google Cloud customers" [S16]. Tài liệu hiện chỉ còn mô tả **enterprise version (Preview)** cài qua Google Cloud [S17][S18]. Bản consumer (cài miễn phí từ GitHub Marketplace) không còn trong docs; một bài viết bên thứ ba nói nó đã tắt ngày 2026-07-17 nhưng **chưa xác minh được từ nguồn Google**.

### 2.2 Các lựa chọn chính thức còn lại

| Lựa chọn | Mô tả | Trạng thái |
|---|---|---|
| Gemini Code Assist on GitHub (enterprise version) | GitHub App, review tự động khi mở PR, lệnh `/gemini` | Preview, cần Google Cloud project + billing account; "no charges ... during Preview" [S17][S18][S19] |
| `google-github-actions/run-gemini-cli` | GitHub Action chạy Gemini CLI, workflow mẫu review / triage / assistant | Còn hoạt động với Gemini API key hoặc Vertex AI; bản mới nhất `v0.1.22` (2026-04-24), action dùng `@v0` [S20][S21][S4b] |
| Antigravity SDK trong workflow tự viết | Codelab chính thức của Google dùng `google-antigravity` (Python) + `GEMINI_API_KEY` trong GitHub Actions, post kết quả bằng `actions/github-script` | Không phải action đóng gói; là ví dụ codelab [S22] |

### 2.3 Tài khoản Gemini student có dùng cho CI / GitHub được không

- Gói Gemini cho sinh viên là một gói tiêu dùng (thường là Google AI Pro). Gói tiêu dùng Google AI Pro **không còn dùng được với Gemini CLI** từ 2026-06-18 [S14]. Trước đó tài liệu Gemini CLI cũng ghi các gói Gemini web/Workspace "do not apply to the API usage which powers the Gemini CLI" [S23].
- GitHub App Gemini Code Assist cần Google Cloud customer (project + billing), không gắn với gói Google AI Pro [S16][S18].
- Kết luận dựa trên nguồn: **tài khoản student không cấp quyền trực tiếp cho CI / GitHub**. Đường dùng được cho CI là **Gemini API key** từ Google AI Studio (free tier hoặc trả phí) hoặc Vertex AI, tạo bằng chính tài khoản Google đó nhưng tính hạn mức theo API, không theo gói student.
- Chưa xác minh: gói student có kèm credit Google Cloud / Developer Program dùng được cho API hay không (không tìm thấy nguồn chính thức).

### 2.4 Hạn mức và chi phí

- Gemini API **free tier**: Gemini CLI docs ghi 250 request / user / ngày, chỉ model Flash [S23] (số này nằm trong trang có phần đã lỗi thời, cần kiểm lại trong AI Studio). Bảng giá: các model Flash / Flash-Lite có free tier ("Free of charge"); Gemini 4 Argon và Gemini 3.1 Pro Preview **không có free tier** [S24]. Ở free tier, nội dung "Used to improve our products: Yes" [S24] (code repo vốn public).
- Gemini API trả phí (per 1M token, input / output): Gemini 3.8 Flash $0.75 / $3.75 (đến 2026-12-31, sau đó gấp đôi), Gemini 4 Argon $2 / $10 (đến 2026-11-30, sau đó $4 / $20), Gemini 2.5 Pro $1.25 / $10 [S24].
- Gemini Code Assist on GitHub (enterprise): hạn mức "at least 100 pull request reviews per day" mỗi installation, không tính vào quota Code Assist chung; không tính phí trong Preview nhưng cần billing account hợp lệ [S19][S25].
- Phút GitHub Actions (cho `run-gemini-cli`): miễn phí trên repo public [S6].

### 2.5 Kích hoạt khi mở / cập nhật PR

- Code Assist on GitHub: tự review khi **mở** PR (`pull_request_opened.code_review: true` mặc định); không thấy cấu hình tự review lại sau mỗi push trong schema `config.yaml`; review lại bằng `/gemini review` [S18][S26].
- `run-gemini-cli`: workflow mẫu `gemini-dispatch.yml` chỉ bắt `pull_request: opened` (không có `synchronize`); review lại bằng comment `@gemini-cli /review`. Muốn review sau mỗi push thì tự thêm `synchronize` [S27].

### 2.6 Kích hoạt bằng comment để tự sửa và push

- Code Assist on GitHub: lệnh `/gemini summary`, `/gemini review`, `/gemini <câu hỏi>`, `/gemini help`. Nó đưa **code suggestion để người dùng bấm commit trên GitHub**, docs không mô tả việc nó tự push commit [S18]. Không đụng file trong `.github/workflows` [S17].
- `run-gemini-cli`: `@gemini-cli <yêu cầu>` chạy workflow `gemini-invoke` với `contents: read` và chỉ các tool GitHub đọc + `add_issue_comment` (tức là trả lời / lập kế hoạch, không push) [S28]. Lệnh `@gemini-cli /approve` chạy `gemini-plan-execute` với `contents: write` và tool `create_branch`, `create_or_update_file`, `push_files`, `create_pull_request` [S29][S27]. Nghĩa là có sẵn luồng "đề xuất kế hoạch, chủ repo approve, AI thực thi". Chưa rõ bản mẫu push vào branch PR hiện tại hay tạo branch / PR mới (prompt `/gemini-plan-execute` chưa đọc).

### 2.7 Đọc issue liên kết và tài liệu trong repo

- Code Assist on GitHub: "automatically retrieve helpful information from the repository and pull request"; tuỳ biến bằng `.gemini/config.yaml` và `.gemini/styleguide.md` [S17][S26]. Có "persistent memory" (Preview) [S15]. Docs không nói về issue liên kết.
- `run-gemini-cli`: checkout repo, đọc `GEMINI.md` gốc repo làm ngữ cảnh dự án [S20]. Workflow invoke có tool `issue_read`, `search_issues`, `get_file_contents` nên đọc được issue khác nếu prompt yêu cầu [S28]. Lưu ý repo này dùng `CLAUDE.md`, sẽ cần `GEMINI.md` (hoặc trỏ tới) nếu chọn Gemini.

### 2.8 Ai được kích hoạt trên repo public

- Code Assist on GitHub: "listens to comments from **any pull request contributor**, and decides whether it should respond" [S18]. Không có kiểm tra quyền write được mô tả; vì app chỉ comment / gợi ý (không push) nên rủi ro chủ yếu là tốn quota.
- `run-gemini-cli` (mẫu dispatch): PR chỉ chạy nếu `head.repo.fork == false`; comment chỉ chạy nếu bắt đầu bằng `@gemini-cli`, `sender.type == 'User'` và `author_association` thuộc `OWNER`, `MEMBER`, `COLLABORATOR` [S27]. Kiểm tra này nằm **trong YAML mẫu**, không phải trong action, nên sửa workflow là có thể vô tình bỏ mất.
- Secret: khuyên dùng custom GitHub App + Workload Identity Federation, quy tắc trust guidance cho dữ liệu không tin cậy [S21][S30]. GitHub không cấp secret cho run từ fork PR (chung cho mọi action) [S1].

---

## 3. Bảng so sánh ngắn

| Tiêu chí | Claude (`claude-code-action`) | Gemini Code Assist on GitHub | Gemini (`run-gemini-cli`) |
|---|---|---|---|
| Dùng với tài khoản cá nhân | API key hoặc token Pro/Max (`claude setup-token`) | Cần Google Cloud project + billing (Preview) | Gemini API key (AI Studio) hoặc Vertex |
| Gói student / subscription có dùng được | Gói Claude Pro/Max: có | Gói student (AI Pro): không | Gói student (AI Pro): không, từ 2026-06-18 |
| Chi phí | Token API (ước ~$0.3-1.5/lượt, chưa đo) hoặc hạn mức subscription; phút Actions free (public) | Không tính phí trong Preview; >=100 review/ngày | Free tier Flash (giới hạn, dữ liệu dùng để cải thiện) hoặc trả phí theo token; phút Actions free |
| Tự review khi mở PR | Có | Có | Có (mẫu) |
| Tự review mỗi push | Có (`synchronize`), nhưng skill bỏ qua PR đã có comment Claude | Không thấy; gọi `/gemini review` | Phải tự thêm `synchronize` |
| Comment để tự sửa + push | `@claude ...` push commit lên branch PR | Không push; chỉ gợi ý commit bằng tay | `@gemini-cli /approve` thực thi kế hoạch (`push_files`, `create_pull_request`) |
| Đọc tài liệu repo | `CLAUDE.md` (lấy từ base branch) | `.gemini/styleguide.md`, `config.yaml` | `GEMINI.md` |
| Đọc issue liên kết | Qua tool nếu cho phép; không tự động | Không nói | Có tool `issue_read` |
| Chặn người lạ | Kiểm write access trong action, chặn bot | Nghe mọi contributor | Kiểm `author_association` trong YAML mẫu |
| Độ ổn định sản phẩm | `v1`, phát hành liên tục | Preview, cả dòng Code Assist đang bị thu hẹp | `v0`, Gemini CLI đã chuyển hướng sang Antigravity |

## 4. Các điểm chưa chắc chắn

1. **Chi phí Claude mỗi PR**: không có số chính thức; khoảng $0.3-1.5 là tự ước tính từ bảng giá. Cần chạy thử vài PR và đọc usage trong Console.
2. **Hạn mức subscription khi dùng `CLAUDE_CODE_OAUTH_TOKEN`**: docs xác nhận được hỗ trợ cho Pro/Max nhưng không nêu mỗi lượt CI tiêu bao nhiêu hạn mức.
3. **Bản consumer của Gemini Code Assist GitHub App**: docs Google không còn nhắc; ngày tắt 2026-07-17 chỉ thấy ở nguồn thứ ba.
4. **Free tier Gemini API**: con số 250 request/ngày lấy từ trang Gemini CLI có phần đã lỗi thời; trang rate-limits không hiển thị số cụ thể khi tải về. Cần xem trong AI Studio.
5. **Gói student**: chưa xác minh có kèm credit Cloud / API không; nguồn chính thức chỉ cho thấy gói AI Pro không dùng được cho Gemini CLI.
6. **Tương lai `run-gemini-cli`**: action vẫn chạy với API key, nhưng Gemini CLI đã bị thay bằng Antigravity CLI cho người dùng consumer; chưa thấy thông báo deprecate chính action. Chưa có action Antigravity chính thức (chỉ codelab).
7. **Claude đọc issue liên kết**: suy từ cơ chế tool, chưa có câu khẳng định trong docs.
8. **`gemini-plan-execute`**: chưa xác minh nó push vào branch PR hiện tại hay tạo branch / PR mới.

## Nguồn

- [S1] Claude Code GitHub Actions: https://code.claude.com/docs/en/github-actions
- [S2] `anthropics/claude-code-action` README: https://github.com/anthropics/claude-code-action
- [S3] Claude Code Review: https://code.claude.com/docs/en/code-review
- [S4] Releases `claude-code-action`: https://github.com/anthropics/claude-code-action/releases ; [S4b] Releases `run-gemini-cli`: https://github.com/google-github-actions/run-gemini-cli/releases
- [S5] Claude Code authentication (long-lived token): https://code.claude.com/docs/en/authentication#generate-a-long-lived-token
- [S6] GitHub Actions billing: https://docs.github.com/en/billing/concepts/product-billing/github-actions
- [S7] Claude pricing: https://docs.claude.com/en/docs/about-claude/pricing
- [S8] Claude Code costs: https://code.claude.com/docs/en/costs
- [S9] `claude-code-action` FAQ: https://github.com/anthropics/claude-code-action/blob/main/docs/faq.md
- [S10] `claude-code-action` solutions: https://github.com/anthropics/claude-code-action/blob/main/docs/solutions.md
- [S11] `claude-code-action` security: https://github.com/anthropics/claude-code-action/blob/main/docs/security.md
- [S12] `claude-code-action` usage (inputs `branch_prefix`, `include_comments_by_actor`): https://github.com/anthropics/claude-code-action/blob/main/docs/usage.md
- [S13] `claude-code-action` configuration: https://github.com/anthropics/claude-code-action/blob/main/docs/configuration.md
- [S14] Deprecation Gemini Code Assist for individuals: https://developers.google.com/gemini-code-assist/docs/deprecations/code-assist-individuals
- [S15] Gemini Code Assist release notes: https://docs.cloud.google.com/gemini/docs/codeassist/release-notes
- [S16] GitHub App Gemini Code Assist: https://github.com/apps/gemini-code-assist
- [S17] Review GitHub code using Gemini Code Assist: https://developers.google.com/gemini-code-assist/docs/review-github-code
- [S18] Use Gemini Code Assist on GitHub: https://docs.cloud.google.com/gemini/docs/code-review/use-code-assist-github
- [S19] Set up Gemini Code Assist on GitHub: https://docs.cloud.google.com/gemini/docs/code-review/set-up-code-assist-github
- [S20] `run-gemini-cli` README: https://github.com/google-github-actions/run-gemini-cli
- [S21] `run-gemini-cli` authentication: https://github.com/google-github-actions/run-gemini-cli/blob/main/docs/authentication.md
- [S22] Codelab Antigravity CLI / SDK code review: https://codelabs.developers.google.com/agy-cli-sdk-code-review
- [S23] Gemini CLI quota and pricing: https://github.com/google-gemini/gemini-cli/blob/main/docs/resources/quota-and-pricing.md
- [S24] Gemini API pricing: https://ai.google.dev/gemini-api/docs/pricing
- [S25] Gemini for Google Cloud quotas: https://developers.google.com/gemini-code-assist/resources/quotas
- [S26] Customize Gemini Code Assist behavior in GitHub: https://developers.google.com/gemini-code-assist/docs/customize-gemini-behavior-github
- [S27] `gemini-dispatch.yml`: https://github.com/google-github-actions/run-gemini-cli/blob/main/examples/workflows/gemini-dispatch/gemini-dispatch.yml
- [S28] `gemini-invoke.yml`: https://github.com/google-github-actions/run-gemini-cli/blob/main/examples/workflows/gemini-assistant/gemini-invoke.yml
- [S29] `gemini-plan-execute.yml`: https://github.com/google-github-actions/run-gemini-cli/blob/main/examples/workflows/gemini-assistant/gemini-plan-execute.yml
- [S30] `run-gemini-cli` best practices: https://github.com/google-github-actions/run-gemini-cli/blob/main/docs/best-practices.md
