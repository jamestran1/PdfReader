# Đánh giá & Thiết kế: Áp dụng iText cho xử lý PDF (Hybrid PDFium + iText)

**Ngày:** 2026-06-19
**Trạng thái:** Đã duyệt design, chờ implementation plan
**Động cơ:** Code "tự xử lý PDF" trên API cấp thấp của PDFium hiện **nặng về maintenance** (viết tay, một phần còn là stub). Mục tiêu là chuyển các mảng manipulation sang iText để giảm gánh nặng bảo trì.

## 1. Bối cảnh & ràng buộc

### 1.1 Điểm cốt lõi: iText KHÔNG render
iText là thư viện *thao tác cấu trúc* PDF (đọc/ghi/sửa/trích xuất/tạo), **không vẽ trang ra màn hình**. Do đó:
- **Render** (`RenderEngine` PDFium→Skia, `PdfViewerControl`, Skia virtual canvas) → **giữ nguyên**, iText không thay được.
- **Manipulation** (trích cấu trúc, annotation, page-ops, in-place edit, save) → iText gánh.

Kiến trúc đích là **hybrid**: PDFium render + iText manipulation.

### 1.2 License — AGPL v3 (đã quyết)
iText Core dual-license: AGPL v3 hoặc commercial. Dự án chọn **phát hành ra ngoài, chấp nhận AGPL**.

Hệ quả AGPL đã thống nhất:
- Link iText ⇒ toàn bộ app là tác phẩm phái sinh ⇒ **toàn bộ app phải phát hành dưới AGPL v3**, kèm source đầy đủ cho người nhận.
- Section 13 (network clause): chỉ kích hoạt nếu có thành phần phục vụ qua mạng; app desktop thuần ít chạm tới.
- Không thể quay về đóng nguồn/dual-license app (vì không sở hữu bản quyền iText) trừ khi mua commercial license iText hoặc gỡ iText.
- Dependency hiện tại (MaterialDesign/CommunityToolkit/SkiaSharp = MIT, PDFium = BSD) đều tương thích AGPL.
- **Tính phí được** (kể cả AGPL), nhưng mô hình "bán binary" bị rò vì người mua có quyền phân phối lại; kiếm tiền bền vững qua tiện lợi (store)/dịch vụ/donations/thương hiệu.
- **Microsoft Store:** hợp lệ nếu đính kèm AGPL làm EULA của app + cung cấp link source công khai (Microsoft cho phép app's-own-license thắng terms mặc định). Apple App Store thì không tương thích GPL/AGPL.

> ⚠️ Không phải tư vấn pháp lý. Nếu app gắn Bliksund hoặc thương mại hóa, xác nhận nghĩa vụ AGPL với Legal/DPO trước khi phát hành.

### 1.3 Hai thách thức kỹ thuật xuyên suốt
1. **Hai hệ toạ độ:** PDFium render gốc trên-trái (pixel); iText/PDF native gốc dưới-trái (point, 1/72 inch). Cần `PdfCoordinateMapper` dịch qua lại.
2. **Vòng đời edit:** sau khi iText ghi thay đổi, PDFium phải reload trang để render lại.

## 2. Kiến trúc (target = service boundary, rollout = strangler tăng dần)

```
┌─────────────── UI (WPF / MVVM) ───────────────┐
│  PdfViewerControl (Skia canvas)  MainViewModel │
└───────┬───────────────────────────────┬───────┘
        │ render                         │ manipulate (commands)
┌───────▼────────┐            ┌──────────▼─────────────┐
│  RenderEngine  │            │   IPdfDocumentService  │  ← facade manipulation
│  (PDFium→Skia) │            │   (impl: iText)        │
└───────┬────────┘            └──────────┬─────────────┘
        │                                │
        │      ┌─────────────────────────▼──────────┐
        └──────┤  PdfCoordinateMapper (toạ độ chung) │
               └─────────────────────────────────────┘
        reload page ◄──── PageChanged event ◄──── sau khi iText save
```

### 2.1 Giữ nguyên
`RenderEngine`, `PdfViewerControl`, Skia virtual canvas. PDFium thuần render backend.

### 2.2 Thêm mới
- **`IPdfDocumentService`** — facade duy nhất cho manipulation iText: `ExtractStructure()`, `AddAnnotation()`, page-ops (merge/split/rotate), `EditText()`, `Save()`. Impl `ITextPdfDocumentService` ẩn iText sau interface ⇒ mock test được; đổi sang PdfPig/khác sau này chỉ cần thay impl.
- **`PdfCoordinateMapper`** — hàm thuần dịch PDF user-space ↔ render-space (cần page height + scale + dpi). Phục vụ cả vẽ annotation lẫn hit-test.
- **`ITextEditStrategy`** — ẩn kỹ thuật in-place edit. Phase 4 dùng impl **redact + vẽ lại**.

### 2.3 Refactor dần (giữ interface công khai để UI không vỡ)
- `PdfStructureAnalyzer` → nguồn dữ liệu chuyển từ `page.GetText()` heuristic sang extraction iText giàu thông tin.
- `PdfObjectManager` → toạ độ ghost lấy từ model extraction iText thay cho char-bounds PDFium (chuyển dần, không big-bang).

### 2.4 Nguyên tắc file
iText và PDFium không ghi/đọc đè cùng file cùng lúc (tránh lock). Thao tác trên **working copy** (temp); save xong PDFium reload.

## 3. Data flow & vòng đời tài liệu

**Mở file:** Open → tạo working copy từ gốc → PDFium mở working copy render; iText mở lazy khi cần thao tác đầu. Gốc không bị đụng đến tới khi user Save chủ động.

**Trích xuất (chỉ đọc — Phase 1):** `ExtractStructure()` dùng iText extraction → `List<TextBlock>`; RAG/AI tiêu thụ. Không reload.

**Sửa (ghi) — vòng đời trung tâm, chiến lược *ghi-rồi-reload ngay mỗi thao tác*:**
```
UI thao tác → tạo IUndoCommand → command gọi IPdfDocumentService
   → iText áp thay đổi + ghi working copy (atomic temp→replace)
   → service raise PageChanged(pageIndex)
   → RenderEngine invalidate + PDFium reload trang → vẽ lại Skia
```
Chọn ghi-rồi-reload ngay (thay vì gom pending) vì **đơn giản và luôn nhất quán** (UI thấy đúng kết quả iText render). Đánh đổi: mỗi thao tác tốn 1 lần ghi + reload — chấp nhận được với thao tác thủ công.

**Undo/Redo:** stack `IUndoCommand`; mỗi command tự `Execute()`/`Undo()` qua service (vd annotation: execute=add theo id, undo=remove theo id). Undo/redo cũng raise `PageChanged`.

**Save / Save As:** thao tác chỉ chạm working copy; Save mới copy working copy đè đường dẫn user chọn — gốc an toàn tới lúc đó.

## 4. Thiết kế từng capability

### (1) Trích text + cấu trúc — Phase 1
- iText `LocationTextExtractionStrategy` / custom `IEventListener` → text + toạ độ + font/size theo đoạn.
- Trả `TextBlock { Text, PdfRect, FontSize, PageIndex, StructureType }`. Heuristic block (heading/list) chạy trên dữ liệu giàu hơn ⇒ AI/RAG chính xác hơn `Split('\n')` hiện tại.

### (2) Annotation (highlight/underline/note) — Phase 2
- iText `PdfAnnotation` ghi chuẩn vào file (tương thích Acrobat/Foxit). Mỗi annotation có id để Undo gỡ.
- Toạ độ chọn trên Skia → `PdfCoordinateMapper` → PDF point.

### (3) Merge/split/rotate + lưu — Phase 3
- iText `PdfMerger`, copy pages, `page.SetRotation()`. Save atomic.

### (4) In-place text edit — Phase 4 (khó nhất)
- Kỹ thuật: **redact + vẽ lại** sau `ITextEditStrategy`. Gỡ text object cũ khỏi content stream + iText vẽ text mới đúng vị trí với font/size đã trích.
- **Lưu ý kỹ thuật:** redaction "thật" của iText là add-on **pdfSweep** (tính phí riêng kể cả dưới AGPL). Với iText core ta làm "gỡ text khỏi content stream + vẽ lại" — làm được nhưng thủ công hơn; cần spike để xác nhận độ phức tạp.
- Phạm vi thực tế: hợp sửa ngắn/từng dòng. **Không reflow** đoạn dài (không lib nào làm sẵn). Vùng không sửa được (đoạn dài/font nhúng thiếu) thì strategy báo "không sửa được" thay vì làm hỏng layout.

## 5. Xử lý lỗi
- **PDF lỗi/hỏng:** iText parse fail → vô hiệu manipulation, **vẫn render** (PDFium khoan dung hơn); báo "chỉ xem được", không crash.
- **File khóa / hai handle:** luôn working copy; bắt `IOException`, retry hoặc báo rõ.
- **Save thất bại:** ghi temp + atomic replace; lỗi giữa chừng thì gốc + working copy cũ nguyên vẹn.
- **PDF mã hóa:** iText cần password → prompt; sai pass → degrade chỉ-đọc.
- **Edit ngoài khả năng:** strategy trả "không sửa được vùng này".
- **AGPL compliance (checklist phát hành):** kèm link source công khai + văn bản AGPL trong About.

## 6. Testing (xUnit, project `PdfReaderApp.Tests`)
- `PdfCoordinateMapper` — test toán toạ độ qua lại (giá trị cao, dễ nhất).
- Extraction — iText trên PDF mẫu, assert text + toạ độ khớp golden values.
- Command apply/undo — round-trip trên PDF temp: execute → reload → assert thay đổi → undo → assert trở lại.
- Integration smoke — edit → save → reload → re-render không lỗi.

## 7. Lộ trình triển khai (strangler)
| Phase | Nội dung | Rủi ro |
|---|---|---|
| 1 | iText dep + `IPdfDocumentService` skeleton + `PdfCoordinateMapper` + **extraction** → nối RAG/AI | Thấp (chỉ đọc) |
| 2 | **Annotation** + pipeline Save + vòng reload PDFium | Trung bình (đường ghi đầu) |
| 3 | **Merge/split/rotate** + save | Trung bình |
| 4 | **In-place edit** (redact+vẽ lại, sau `ITextEditStrategy`) | Cao (để cuối) |

**Phạm vi:** Spec này ghi toàn bộ đánh giá + kiến trúc đích + cả 4 phase. **Implementation plan chỉ làm Phase 1 trước** (ra giá trị AI sớm, rủi ro thấp), sau đó plan tiếp từng phase. Tránh một plan khổng lồ ôm cả 4 phase.

## 8. Quyết định đã chốt
- License: AGPL v3, phát hành mã nguồn mở.
- Adoption: kiến trúc đích A (service boundary) + triển khai C (strangler tăng dần). Bỏ B (iText-first rewrite ngay) vì rủi ro thừa.
- Edit flow: ghi-rồi-reload ngay mỗi thao tác.
- In-place edit: redact + vẽ lại (sau interface `ITextEditStrategy`).
- Plan đầu tiên: chỉ Phase 1.

## 9. Rủi ro & việc cần spike
- **Phase 4 redact bằng iText core** (không pdfSweep): cần spike xác nhận độ phức tạp content-stream editing.
- **iText version:** dùng iText Core 8.x/9.x (NuGet `itext`); xác nhận tương thích `net10.0-windows`.
- **Bộ nhớ:** hai parser (PDFium + iText) cùng mở tài liệu lớn — theo dõi RAM, cân nhắc đóng iText doc khi rảnh.
