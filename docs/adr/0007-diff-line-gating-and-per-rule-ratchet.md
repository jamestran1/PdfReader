---
status: accepted
---

# Chặn warning theo dòng trong diff, siết dần theo từng rule

Bật analyzer (SDK `10.0-recommended` + Meziantou) trên code hiện tại cho ra khoảng 700 vi phạm và khoảng 12 method quá phức tạp. Bật chặn một lần thì mọi PR đỏ; không chặn thì chuẩn không bao giờ được áp. Quyết định:

- **Chặn theo dòng:** chỉ warning nằm trên dòng PR thêm hoặc sửa (so với `merge-base` với nhánh gốc) mới chặn. Warning trên dòng cũ, kể cả trong file PR có đụng tới, không chặn. Việc lọc nằm trong một script dùng chung (`scripts/check.ps1`) cho CI và hook của agent.
- **Siết dần theo rule:** dọn từng rule trong PR riêng. Rule đã về 0 vi phạm được ghi vào `analyzers/clean-rules.txt` và từ đó bị chặn trên toàn repo, không lọc diff. Khi mọi rule đều sạch thì bỏ lọc diff.
- Rule độ phức tạp (CA1502, MA0051) báo ở dòng khai báo method, nên chỉ chặn khi PR thêm method mới hoặc sửa chữ ký. Method cũ do đợt dọn riêng xử lý.
- Hai mức: warning chặn (có lọc), suggestion chỉ hiện trong IDE. Không bật `TreatWarningsAsErrors`.

## Considered options

- **File baseline (danh sách vi phạm cũ được bỏ qua):** chặn chính xác cả trên dòng cũ bị sửa, nhưng file baseline lệch ngay khi dòng dịch chuyển, phải sinh lại thường xuyên và tạo diff nhiễu; loại.
- **Mức chặn theo thư mục (thư mục sạch thì chặn, thư mục cũ thì chỉ cảnh báo):** đơn giản, nhưng code mới viết vào thư mục cũ (phần lớn code nằm trong vài thư mục lớn) không bị chặn gì; loại.
- **Chặn theo cả file trong diff:** PR tính năng đụng một dòng của `MainViewModel.cs` sẽ phải dọn cả file; loại.

## Consequences

- Code mới bị chặn ngay từ PR cấu hình analyzer; build và test không bao giờ đỏ vì code cũ.
- Dòng cũ bị sửa nhẹ (vd đổi một tham số) cũng phải sạch mọi warning trên dòng đó.
- Method cũ quá phức tạp vẫn được thêm logic mà không bị chặn, chừng nào chữ ký không đổi; rủi ro này chấp nhận cho tới đợt dọn độ phức tạp.
- Kết quả phụ thuộc SARIF trong `obj/` của build gần nhất: script luôn build trước khi lọc.
