# Cách NotebookLM và app tương tự làm Studio

Nghiên cứu cho issue #249, phục vụ map #28 (chọn tập Note trong Workspace, AI sinh Bản soạn: Tóm tắt, Outline, Soạn thảo).

- Ngày tra: 2026-10-26.
- Nguồn: chỉ tài liệu chính thức (Google NotebookLM Help và blog Google; Microsoft Support; Notion Help). Trang help của Google hiện đặt tên sản phẩm là "Gemini Notebook", URL vẫn là `support.google.com/notebooklm`.
- Quy ước: **[Fact]** là điều nguồn viết ra (có link); **[Suy luận]** là nhận định của người viết, chưa được nguồn xác nhận.

## 1. Chọn nguồn

### NotebookLM

- **[Fact]** Mỗi notebook là một tập nguồn độc lập; NotebookLM không truy cập nhiều notebook cùng lúc. ([Create a notebook](https://support.google.com/notebooklm/answer/16206563?hl=en))
- **[Fact]** Trong panel Sources, người dùng thêm nguồn bằng "+ Add" hoặc bỏ chọn từng nguồn. ([Create a notebook](https://support.google.com/notebooklm/answer/16206563?hl=en))
- **[Fact]** Chat cho phép chọn nguồn nào được dùng để trả lời. ([Use chat](https://support.google.com/notebooklm/answer/16179559?hl=en))
- **[Fact]** FAQ: nguồn "luôn được dùng, hoặc cả tập hoặc tập con bạn chọn"; note "chỉ khi bạn chọn nó cụ thể". ([FAQ](https://support.google.com/notebooklm/answer/16269187?hl=en))
- **[Fact]** Mô hình hiện tại: note tạo ra tự hiện trong panel Sources cạnh nguồn upload, mỗi Note source có checkbox; bỏ tick thì note không dùng cho hỏi đáp và sinh artifact. Note cũ nằm ở Studio phải "Convert" (từng cái hoặc "Convert all notes to source") mới dùng được làm nguồn. ([Create & add notes](https://support.google.com/notebooklm/answer/16262519?hl=en))
- **[Fact]** Với Slide Deck, ngay trong bảng tuỳ chỉnh trước khi sinh có lựa chọn "format, output language, length, and sources", tức chọn nguồn riêng cho từng lần sinh. ([Slide Decks](https://support.google.com/notebooklm/answer/16757456?hl=en))
- **[Fact]** Khi có từ 5 nguồn trở lên, NotebookLM có thể tự gán nhãn, nhóm nguồn theo chủ đề; người dùng sửa được nhãn. ([Create a notebook](https://support.google.com/notebooklm/answer/16206563?hl=en))
- **[Suy luận]** Mặc định là tất cả nguồn được tick: help nói chỉ đến thao tác "unselecting" và FAQ nói "entire set or the subset you select". Help không viết thẳng "mặc định chọn tất cả". Tài liệu cũng không nói rõ Report hay Audio có hộp chọn nguồn riêng như Slide Deck hay dùng chung checkbox của panel Sources.

### App tương tự

- **[Fact] Microsoft Copilot Notebooks**: người dùng gom "References" (file Word/PowerPoint/PDF, trang OneNote, Copilot page, thư mục SharePoint) vào notebook; Copilot chỉ dùng các reference đó. Giới hạn grounding: tối đa 300 file (Copilot), 50 (Copilot Chat). ([How Copilot Notebooks works](https://support.microsoft.com/en-us/microsoft-365-copilot/how-microsoft-365-copilot-notebooks-works), [Add references](https://support.microsoft.com/en-us/microsoft-365-copilot/add-references-to-your-microsoft-365-copilot-notebook))
- **[Fact] Notion Agent**: mặc định lấy ngữ cảnh trang đang mở; nếu bôi chọn block thì tập trung vào các block đó; thêm ngữ cảnh bằng @-mention trang/người hoặc chọn "All sources" rồi tick một hay nhiều nguồn. ([Notion Agent](https://www.notion.com/help/notion-agent)) Enterprise Search mặc định tìm trên mọi nguồn, có thể thu hẹp về một trang hoặc teamspace. ([Enterprise Search](https://www.notion.com/help/enterprise-search))

## 2. Kết quả hiển thị, lưu ở đâu, có sửa, có lưu thành ghi chú

### NotebookLM

- **[Fact]** Panel Studio là nơi sinh và xem kết quả; trên cùng là các ô (tile) tạo từng loại, mọi kết quả đã tạo hiện thành danh sách bên dưới. Từ bản nâng cấp Studio, có thể tạo và lưu **nhiều** kết quả cùng loại trong một notebook (trước đó mỗi loại chỉ một). ([Blog: Video Overviews and an upgraded Studio](https://blog.google/innovation-and-ai/models-and-research/google-labs/notebooklm-video-overviews-studio-upgrades/))
- **[Fact]** Sinh chạy nền: có thể sinh nhiều artifact đồng thời hoặc chuyển màn hình. Khi xong có thông báo, artifact mới chưa mở có chấm xanh trong Studio. ([Audio Overview](https://support.google.com/notebooklm/answer/16212820?hl=en), [Create a notebook](https://support.google.com/notebooklm/answer/16206563?hl=en))
- **[Fact]** Menu ba chấm cạnh artifact: đổi tên, tải về, chia sẻ, xoá (Slide Deck); "View custom prompt" để xem prompt đã dùng; Report có "Export to Docs" (bảng dữ liệu thì "Export to Sheets"). Bản xuất ra Docs/Sheets không đồng bộ ngược lại. ([Create a notebook](https://support.google.com/notebooklm/answer/16206563?hl=en), [Slide Decks](https://support.google.com/notebooklm/answer/16757456?hl=en))
- **[Fact]** Sửa: Slide Deck có chế độ "Revise" (sửa text, layout, xoá slide), nhưng mỗi lần áp dụng sẽ sinh **một deck mới hoàn toàn**, và "sources are not taken into account during revisions". ([Slide Decks](https://support.google.com/notebooklm/answer/16757456?hl=en)) Tài liệu không mô tả việc sửa trực tiếp nội dung Report trong NotebookLM; đường sửa được nêu là Export to Docs.
- **[Fact]** Lưu thành ghi chú: câu trả lời Chat lưu bằng "Save to note", giữ nguyên định dạng gồm bảng và trích dẫn inline bấm được; note từ câu trả lời "chỉ sửa được khi đã bỏ trích dẫn". Note tự viết thì sửa tự do. Note xoá rồi không khôi phục được. ([Create & add notes](https://support.google.com/notebooklm/answer/16262519?hl=en), [Use chat](https://support.google.com/notebooklm/answer/16179559?hl=en), [FAQ](https://support.google.com/notebooklm/answer/16269187?hl=en))
- **[Fact]** Chat còn có thể "Modify artifacts as versions" và tạo file tải về (PDF, Word, Markdown...), đang ở mức thử nghiệm. ([Use chat](https://support.google.com/notebooklm/answer/16179559?hl=en))
- **[Suy luận]** Không thấy tài liệu nói Report trong Studio được lưu thành note bằng một nút riêng. Vòng lặp "kết quả AI quay lại làm nguồn" của NotebookLM đi qua Note (Save to note, rồi note là source).

### App tương tự

- **[Fact] Copilot Notebooks Study Guide**: mỗi phần (Summary, Topic pages, Flashcards, Quiz) thành **một trang** trong notebook, có placeholder và tiến độ khi đang sinh, đọc được từng trang khi xong. Trang sửa được "như một Copilot page bình thường". Thêm reference mới thì có thể regenerate, và việc này "tạo bản sao mới" chứ không ghi đè. ([Create a Copilot Notebook Study Guide](https://support.microsoft.com/en-us/microsoft-365-copilot/create-a-copilot-notebook-study-guide))
- **[Fact] Copilot Notebooks Overview Page**: trang tổng quan tự sinh từ References, mở mặc định khi vào notebook; đổi reference thì bấm refresh để sinh lại. ([Overview Page](https://support.microsoft.com/en-us/microsoft-365-copilot/view-an-overview-page-in-microsoft-365-copilot-notebooks))
- **[Fact] Notion AI**: viết trực tiếp lên trang (space trên dòng mới rồi gõ prompt); sửa đoạn chọn bằng "Edit with AI" rồi accept, discard hoặc thử lại. ([Notion AI for docs](https://www.notion.com/help/guides/notion-ai-for-docs))

## 3. Trích dẫn ngược về nguồn

- **[Fact] NotebookLM Chat**: dùng trích dẫn trực tiếp (text, ảnh) từ nguồn; rê chuột vào trích dẫn thấy đoạn trích đầy đủ, bấm thì nhảy tới đoạn đó trong ngữ cảnh nguồn. Nguồn quá ngắn thì có thể không có trích dẫn cụ thể (tham chiếu cả tài liệu). ([Use chat](https://support.google.com/notebooklm/answer/16179559?hl=en), [FAQ](https://support.google.com/notebooklm/answer/16269187?hl=en))
- **[Fact]** Note được dùng làm nguồn thì câu trả lời có "citation chips" dẫn về đúng đoạn trong note. ([Create & add notes](https://support.google.com/notebooklm/answer/16262519?hl=en))
- **[Fact]** Flashcard/Quiz có nút "explain" sinh giải thích "với trích dẫn trỏ về tài liệu nguồn". ([Blog: 6 ways to use NotebookLM](https://blog.google/innovation-and-ai/models-and-research/google-labs/notebooklm-student-features/))
- **[Fact]** Data Table xuất ra Sheets thì tab thứ hai chứa trích dẫn. ([Create a notebook](https://support.google.com/notebooklm/answer/16206563?hl=en))
- **[Fact] Notion**: khi trả lời bằng thông tin trong workspace hoặc app kết nối, "luôn trích nguồn" để quay lại nguồn. ([Enterprise Search](https://www.notion.com/help/enterprise-search))
- **[Suy luận]** Help không nói rõ Report (Briefing doc, Study guide...) có trích dẫn inline bấm được hay không. Tài liệu Copilot Study Guide cũng không nhắc trích dẫn trong trang sinh ra. Trích dẫn bấm-nhảy rõ ràng nhất là ở Chat và note lưu từ Chat.

## 4. Các kiểu kết quả và yêu cầu tự do

### NotebookLM

- **[Fact]** Studio có: Notes, Audio Overviews, Video Overviews, Mind maps, Reports, Data Tables, Flashcards/Quizzes, Slide Decks, Infographics. ([Create a notebook](https://support.google.com/notebooklm/answer/16206563?hl=en))
- **[Fact]** Reports: "Create your own", hoặc chọn FAQ, Study guide, Briefing document, hoặc một kiểu do AI gợi ý. ([Create a notebook](https://support.google.com/notebooklm/answer/16206563?hl=en)) Blog bổ sung kiểu Blog Post, gợi ý động theo chủ đề nguồn (vd bài kinh tế thì gợi ý glossary; truyện ngắn thì gợi ý phân tích nhân vật), và cho tạo "custom formats". ([Blog: 6 ways to use NotebookLM](https://blog.google/innovation-and-ai/models-and-research/google-labs/notebooklm-student-features/))
- **[Fact]** Yêu cầu tự do: hầu hết loại có nút bút chì mở bảng tuỳ chỉnh với ô prompt (Data Table: mô tả hàng/cột; Audio: tập trung chủ đề, trình độ; Video: chủ đề, mục tiêu học, đối tượng). Prompt đã dùng xem lại được qua "View custom prompt". ([Create a notebook](https://support.google.com/notebooklm/answer/16206563?hl=en), [Audio Overview](https://support.google.com/notebooklm/answer/16212820?hl=en), [Blog: upgraded Studio](https://blog.google/innovation-and-ai/models-and-research/google-labs/notebooklm-video-overviews-studio-upgrades/))
- **[Fact]** Có thể có artifact tự sinh một lần khi mới thêm nguồn (report, flashcards...), không tính vào hạn mức. ([Create a notebook](https://support.google.com/notebooklm/answer/16206563?hl=en))
- **[Fact]** Slide Deck có luồng "co-create": bật "Review outline notes", AI sinh outline trước, người dùng xem rồi "Proceed"; sau Proceed không sửa outline được nữa. ([Slide Decks](https://support.google.com/notebooklm/answer/16757456?hl=en))

### App tương tự

- **[Fact] Copilot Notebooks**: Study Guide với Summary (luôn có), Topic pages, Flashcards, Quiz; ngoài ra soạn thảo tự do qua chat (vd "Draft a go-to-market plan using the uploaded product brief..."). ([Study Guide](https://support.microsoft.com/en-us/microsoft-365-copilot/create-a-copilot-notebook-study-guide), [How Copilot Notebooks works](https://support.microsoft.com/en-us/microsoft-365-copilot/how-microsoft-365-copilot-notebooks-works))
- **[Fact] Notion AI**: không có danh mục kiểu kết quả cố định; dùng prompt tự do kèm vài gợi ý ("summarize this in bullet points", "organize this information into a table"). ([Notion AI for docs](https://www.notion.com/help/guides/notion-ai-for-docs))

## 5. Gợi ý cho Bản soạn của Trí Thư (#28)

Toàn bộ mục này là **[Suy luận]**, đầu vào cho phiên grilling, chưa phải quyết định.

1. **Chọn nguồn**: mẫu phổ biến là checkbox trên danh sách nguồn, mặc định chọn hết, kèm tuỳ chọn chọn lại nguồn ngay trong hộp thoại sinh (như Slide Deck). Với Trí Thư: hộp thoại "Tạo Bản soạn" liệt kê Note trong Workspace, mặc định tick hết, cho bỏ tick.
2. **Nơi lưu**: cả NotebookLM lẫn Copilot đều lưu mỗi lần sinh thành **một mục riêng, không ghi đè** (danh sách Studio; trang mới khi regenerate). Đề xuất: Bản soạn là thực thể riêng trong Workspace, nhiều bản cùng kiểu, có tên, xoá được.
3. **Sửa**: Copilot cho sửa trang như văn bản thường; NotebookLM hạn chế (Report không sửa trong app, Slide revise sinh bản mới). Mức đơn giản nhất cho Trí Thư là Bản soạn sửa được như Note, hoặc có "Lưu thành Note".
4. **Trích dẫn**: NotebookLM đánh đổi "sửa được thì mất trích dẫn" cho note từ chat. Đây là một lựa chọn cụ thể có thể chép: trích dẫn [n] nối về Note nguồn, khi người dùng sửa thì có thể mất liên kết. Trí Thư đã có trích dẫn [n] trong Chat (#107, #108), có thể tái dùng.
5. **Kiểu kết quả và yêu cầu tự do**: mẫu chung là vài preset (Tóm tắt, Outline...) + ô prompt tuỳ chỉnh tuỳ chọn + "tự tạo kiểu". Điều này trả lời mục "Soạn thảo gõ tự do hay theo mẫu" trong "Not yet specified" của #28 theo hướng: cả hai, preset kèm ô prompt.
6. **Lưu prompt đã dùng** ("View custom prompt") là chi phí thấp, giúp hiểu vì sao bản soạn ra như vậy.
7. **Note nguồn bị sửa sau khi sinh**: không nguồn nào mô tả đồng bộ tự động; Copilot dùng regenerate ra bản mới, Overview dùng nút refresh. Gợi ý: Bản soạn là ảnh chụp tại thời điểm sinh, sinh lại tạo bản mới.
8. **Huỷ và chạy nền**: NotebookLM sinh nền, có thông báo khi xong; Chat có nút Stop. Phù hợp với CS-13 (huỷ khi đổi Workspace).

## Khoảng trống chưa xác minh

- Report của NotebookLM có trích dẫn inline bấm được hay không.
- Report và Audio có hộp chọn nguồn riêng trong bảng tuỳ chỉnh hay chỉ theo checkbox của panel Sources.
- Readwise và Obsidian: không tìm thấy tài liệu chính thức mô tả luồng tương đương "chọn tập ghi chú, sinh bản soạn" ở mức đủ tin cậy, nên không đưa vào.
