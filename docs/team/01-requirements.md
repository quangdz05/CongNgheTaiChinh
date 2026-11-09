# Quản lý cảnh báo FraudShield — đặc tả BA

**Ngày:** 06/11/2026 · **Phiên bản:** 0.1 · **Trạng thái:** BẢN NHÁP CHỜ CHỐT NGHIỆP VỤ.

> **Cập nhật 09/11/2026:** Người dùng đã trả lời “Dùng toàn bộ mặc định Q1–Q8” và yêu cầu bắt đầu sửa code. Các mặc định trong mục 11 được chọn làm cơ sở triển khai MVP; nội dung “chưa chốt” bên dưới mô tả trạng thái tại thời điểm BA lập bản nháp. Tiến độ và kết quả thực tế xem `02-implementation.md`.

Chỉ khảo sát và đặc tả, chưa sửa code, chạy test hay thay đổi database. Các quy tắc ghi **đề xuất** và AC tương ứng là phương án thảo luận, chưa phải quyết định đã được người dùng phê duyệt. Cần chốt Q1–Q8 trước khi triển khai.

## 1. Bối cảnh & mục tiêu

Hiện tại, “Hàng đợi cảnh báo” là danh sách giao dịch `Evaluation.Decision != ALLOW`, không có entity Alert. `ReviewTransactionCommand` ghi `AnalystReview(IsFraud: bool, Notes)`; chưa có người phụ trách, trạng thái công việc, ưu tiên hoặc SLA. Chi tiết giao dịch chỉ có “Giao dịch hợp lệ” và “Xác nhận gian lận”. Người xử lý thiếu bằng chứng chưa có lựa chọn phù hợp.

`FraudRepository.StatsAsync` tính `pending` là giao dịch non-ALLOW chưa có bất kỳ review; `confirmedFraud` lấy IsFraud của review mới nhất. Không thể dùng hai chỉ số này trực tiếp để đếm Alert mở hoặc xác minh mới. Transaction/Evaluation là dữ liệu gốc bất biến.

Mục tiêu: công việc điều tra có ID riêng, một owner tại một thời điểm, vòng đời được kiểm soát, có hạn và lịch sử; đóng công việc không đồng nghĩa xác nhận hợp lệ. Tách ba trục: **quyết định tự động** ALLOW/REVIEW/BLOCK; **trạng thái công việc** sáu giá trị; **kết luận xử lý** Unknown/Fraud/Legitimate/Disputed. Kết luận xử lý MVP chưa phải ground truth đã được xác minh độc lập, không tự chuyển vào huấn luyện hoặc metric FP/FN.

### Nguồn đã đọc

Đường dẫn tính từ thư mục gốc. Đây là khảo sát tĩnh, không xác nhận runtime/test hiện tại.

- `CLAUDE.md`, `README.md`, `docs/ARCHITECTURE_MAP.md`, `.claude/agents/ba.md`, `docs/team/ORCHESTRATION.md`.
- Dưới `Back-end/src/Services/FraudDetectionService/`:
  - `FraudDetectionService.Domain/Entities/Transaction.cs`, `Evaluation.cs`, `AnalystReview.cs`, `AppUser.cs`, `AuditEntry.cs`, `RulePolicy.cs`; `FraudDetectionService.Domain/Repositories/IFraudRepository.cs`.
  - `FraudDetectionService.Application/Features/Transactions/DTOs/Contracts.cs`, `Features/Transactions/Services/DetectionService.cs`, `Features/Alerts/Commands/ReviewTransactionCommand.cs` (hai đường dẫn Features sau cũng thuộc Application).
  - `FraudDetectionService.Infrastructure/Repositories/FraudRepository.cs`; `Persistence/FraudDbContext.cs`; `Persistence/Configurations/TransactionConfiguration.cs`, `EvaluationConfiguration.cs`, `AnalystReviewConfiguration.cs`, `AppUserConfiguration.cs`, `AuditEntryConfiguration.cs`, `RulePolicyConfiguration.cs`; `Persistence/Migrations/20261030035859_InitialCreate.cs`, `20261106021653_OptimizeHistoryIndexes.cs` (các Persistence đều thuộc Infrastructure).
  - `FraudDetectionService.Api/Program.cs`, `Controllers/TransactionsController.cs`, `Controllers/OperationsController.cs` (Controllers thuộc Api).
- `Back-end/src/Shared/Shared.Security/Permissions/FraudPermissions.cs`; `Back-end/tests/FraudDetectionService.Tests/DetectionTests.cs`, `ReviewRegressionTests.cs`.
- Dưới `Front-end/src/`: `App.tsx`, `features/transactions/_components/transaction-detail.tsx`, `features/transactions/_services/services.ts`, `shared/services/api.ts`.
- Đã trích đọc văn bản/bảng trong `docs/FraudShield_Yeu_cau_nguoi_dung_va_lo_trinh_nang_cap.docx`: phần 1–4 hiện trạng/vai trò/tách trạng thái; phần 6 FR01/04/05/06; phần 7 FR07–FR12; phần 8 FR13–FR18; phần 9 FR21–FR23; phần 16 dữ liệu; phần 17–20 màn hình/lộ trình/AT01/02/05/11/12/câu hỏi nghiệp vụ. Dùng tham khảo nội bộ, không khảo sát lại website nhà cung cấp trong lần này.

## 2. Phạm vi (Trong / Ngoài)

**Trong:** Alert riêng; New, Assigned, InProgress, Waiting, Escalated, Closed; nhận việc nguyên tử; quyền theo owner; ưu tiên; thời hạn; quá hạn/chuyển cấp; lý do chuyển/đóng; Unknown/Disputed; timeline; lọc/chi tiết; tác động dữ liệu cũ, API và thống kê. Giao lại/giải phóng/mở lại được đặc tả dưới dạng đề xuất cần chốt vì ảnh hưởng trực tiếp vòng đời.

**Ngoài:** chặn/chuyển tiền thật; SMS/cổng khách hàng; thay AI/huấn luyện; full Case nhiều Alert; gộp theo thực thể/cửa sổ; graph; upload tệp; webhook/email; maker-checker/phê duyệt nhãn; multi-tenant; broker/Redis/Elasticsearch. Word có phạm vi rộng hơn yêu cầu lần này.

**Đơn vị MVP đề xuất Q1:** một Alert/Evaluation; hiện Evaluation có khóa TransactionId nên tối đa một Alert/giao dịch. Nhiều luật khớp vẫn một Alert giữ toàn bộ căn cứ gốc. Không tạo thủ công Alert cho ALLOW hoặc gộp nhiều giao dịch trong phiên này.

## 3. Tác nhân & phân quyền

Hiện có Admin/Analyst. `ReviewTransactions` cho cả hai; `ManageRules`, `ReadAudit` chỉ Admin. Chưa có Lead/Manager. Đề xuất Admin điều phối MVP, chờ Q2/Q5; không tự thêm vai trò.

| Thao tác đề xuất | Analyst | Admin | System |
|---|---|---|---|
| Xem Alert/timeline nghiệp vụ | Có trong workspace | Có | Không UI |
| Nhận New chưa owner | Tự nhận | Tự nhận | Không |
| Bắt đầu/chờ/tiếp tục/đóng/thêm note | Chỉ owner | Chỉ khi là owner | Không |
| Chuyển cấp thủ công | Owner, có lý do | Có, có lý do | Theo SLA |
| Phân công/giao lại/giải phóng | Không | Có, có lý do | Không tự chọn Analyst |
| Đổi priority | Không | Có, có lý do | Mapping lúc tạo |
| Nhận tiếp Escalated | Không tự giành | Tự nhận hoặc phân công | Không |
| Mở lại Closed | Không | Có, có lý do | Không |
| Đọc audit hệ thống | Không | Giữ quyền hiện có | Ghi audit |

Owner tham chiếu `AppUser.Id`; actor lấy principal đã xác thực và giải quyết ra UserId ở server, không nhận tên actor trong request. `Transaction.UserId` là khách hàng, không phải Analyst. Admin muốn đóng Alert của người khác phải giao lại cho mình trước; lưu cả hai bước. Timeline Alert cho Analyst không mở quyền `/api/audit`.

## 4. Quy tắc nghiệp vụ (BR-01, BR-02…)

- **BR-01 — bảo toàn:** không sửa Transaction/Evaluation/feature/policy/model snapshot khi Alert thay đổi. BLOCK là đề xuất rủi ro, không phải xác nhận đã chặn tiền hoặc gian lận thật.
- **BR-02 — tạo, đề xuất Q1:** REVIEW/BLOCK tạo New, owner null, Unknown. Hybrid fallback REVIEW cũng tạo, source `AiUnavailableFallback`. ALLOW không tạo; nhiều rule không tạo nhiều Alert. Nguồn/lý do từ snapshot server.
- **BR-03 — nguyên tử/idempotency:** Transaction+Evaluation+Alert+Created event cùng transaction DB; lỗi bất kỳ phần nào rollback tất cả, không trả thành công nếu REVIEW/BLOCK thiếu Alert. Unique TransactionId bảo vệ cả race khác process. Replay cùng ID/nội dung trả kết quả cũ, không lặp Alert/event; khác nội dung 409 theo quy tắc hiện có. Replay dữ liệu cũ không thay thế backfill.
- **BR-04 — version:** mutation dùng version hiện tại và requestId UUID. Update có điều kiện tại DB, không chỉ khóa RAM. Commit tăng version đúng một, history cùng commit. Stale 409; hai claim chỉ một thắng. SQLite busy xử lý lỗi tạm thời, không trả thành công khi chưa commit.
- **BR-05 — retry lệnh:** receipt unique `(AlertId,requestId)` lưu actor, command, hash payload và kết quả commit. Cùng actor/ID/nội dung trả kết quả cũ, không tăng version/event; khác actor/nội dung 409. Tra receipt đã commit trước kiểm stale version, vẫn xác thực/quyền truy cập. UI không dùng response replay cũ đè bản mới hơn. Lệnh thất bại không có tác động nghiệp vụ.
- **BR-06 — owner, đề xuất Q2:** New owner null; Assigned/InProgress/Waiting bắt buộc owner; Escalated giữ owner cũ hoặc null nếu đến từ New; Closed giữ owner lúc đóng. Escalation không tự giao tất cả Admin hoặc đổi owner âm thầm. Chưa có lease tự hết hạn.
- **BR-07 — vòng đời:** chỉ các cạnh mục 6; không arbitrary status PATCH. Assigned chưa bắt đầu không đóng thẳng. Escalated phải được Admin phân công thành Assigned rồi owner Start trước khi Close.
- **BR-08 — chờ:** InProgress→Waiting cần type, reason trim 5–1000 ký tự và waitingUntil tương lai; giữ owner. Hết hẹn mà chưa tiếp tục/đóng thì Escalated một lần/episode, không tự hợp lệ/Closed. Mọi cạnh rời Waiting (Resume/Reassign/Escalate/Close) xóa WaitingType/WaitingReason/WaitingUntil khỏi snapshot hiện hành sau khi lưu đủ giá trị cũ trong event; không hiển thị thông tin chờ cũ như trạng thái hiện tại.
- **BR-09 — priority, đề xuất Q3:** P1/Critical cho BLOCK; P2/High cho REVIEW thông thường; P3/Normal cho REVIEW do AI unavailable fallback. Fallback P3 là đề xuất cần cân nhắc rủi ro. Priority công việc độc lập FinalScore. Admin đổi cần reason 5–1000 ký tự; lưu cũ/mới, không sửa Evaluation. Chưa có P4.
- **BR-10 — SLA, đề xuất Q3/Q4:** 24/7, không lịch nghỉ. Hạn đóng P1=15 phút, P2=60 phút, P3=240 phút, là đề xuất demo chưa cam kết vận hành. `slaStartedAt` là mốc server UTC chụp một lần trong transaction tạo Alert, chỉ có hiệu lực nếu transaction commit; không lấy Transaction.Timestamp hoặc suy ra chính xác thời điểm commit tương lai. `dueAt=start+budget`; commit latency đo riêng. Đang mở và `now>=dueAt` là overdue. Claim/reassign/Waiting/escalate không dừng/reset đồng hồ.
- **BR-11 — đổi SLA:** lưu snapshot/version trên Alert; sửa cấu hình tương lai không áp ngầm dữ liệu cũ. Nâng priority: `dueAt=min(dueAt cũ,slaStartedAt+budget mới)`; hạ không kéo dài hạn. `closedAt<=dueAt` là đóng đúng hạn; trước Close tại đúng D, Alert đang mở đã đạt mốc overdue. Không xóa breach event đã ghi. Mốc server để đóng/cập nhật được chụp một lần trong transaction; đo latency commit riêng, không dùng clock client.
- **BR-12 — chuyển cấp:** worker trong API quét tối đa mỗi 30 giây, xét `min(dueAt,waitingUntil nếu Waiting)` và grace BR-13. Reason SlaBreached/WaitingExpired/Manual; hai mốc cùng hết chọn SlaBreached, ghi cả điều kiện. Chuyển đến queue Admin với badge/count nội bộ, không email/SMS. Closed không đổi; đang Escalated không lặp event mỗi tick.
- **BR-13 — episode:** `handlingEpisode` bắt đầu 1; tăng khi Admin tiếp nhận Escalated hoặc reopen. Tối đa một Escalated event/episode. Tiếp nhận việc đã quá hạn cho grace 5 phút trước lần auto-escalate tiếp, dueAt/overdue không đổi. Mốc auto-escalate sau tiếp nhận là muộn hơn deadline hiện hành và hết grace; deadline tương lai vẫn có badge đúng hạn. Grace không làm SLA đạt giả. EscalatedAt/EscalationReason trong snapshot chỉ có giá trị khi status=Escalated; khi rời trạng thái này phải xóa snapshot sau khi lưu giá trị trong history. Nếu UI cần lần chuyển cấp gần nhất thì lấy từ history và ghi rõ là lịch sử, không dùng như escalation đang có hiệu lực.
- **BR-14 — đóng, đề xuất Q5:** bắt buộc closureReasonCode+notes trim 5–1000 và resolution. Cặp hợp lệ: FraudConfirmed→Fraud; LegitimateConfirmed→Legitimate; InsufficientEvidence→Unknown; ConflictingEvidence→Disputed. Không boolean mặc định. Fraud/Legitimate cần ≥1 evidence reference có nguồn (mục 7); chưa có xác minh độc lập/maker-checker. Unknown/Disputed là công việc đóng chưa rõ thật/giả, báo cáo riêng, không tính hợp lệ/gian lận xác nhận, không auto train.
- **BR-15 — reopen, đề xuất Q6:** Admin có lý do mở Closed→Assigned, owner chính mình hoặc user Admin/Analyst tồn tại. Episode/vòng SLA mới từ reopenedAt theo priority hiện tại, giữ nguyên closure/kết luận/SLA vòng trước trong history. Snapshot hiện hành xóa ClosureReasonCode/ClosureNotes/ClosedAt/ClosedByUserId, Resolution về Unknown; không mang dữ liệu Closed/Waiting/Escalated của vòng cũ sang trạng thái hiện hành. Không sửa review/ground truth cũ âm thầm.
- **BR-16 — lịch sử:** Created/Claimed/Started/WaitingEntered/Resumed/Reassigned/Released/PriorityChanged/Escalated/Closed/Reopened/NoteAdded append-only, actor/system, UTC, before/after version, reason/requestId. Rollback không có event thành công. Note đính chính bằng bản bổ sung, không sửa/xóa. Audit hệ thống giữ quyền riêng.
- **BR-17 — số liệu, đề xuất Q7:** `openAlerts=status!=Closed`, gồm Waiting/Escalated; đếm riêng states/overdue/ClosedUnknown/ClosedDisputed. Giữ `pending` cũ với nhãn “giao dịch cảnh báo chưa có review cũ”, không đổi nghĩa âm thầm; UI badge Alert dùng openAlerts. `confirmedFraud` cũ là legacy review, không ground truth của Alert mới.
- **BR-18 — legacy, đề xuất Q7:** backfill mọi non-ALLOW chưa có Alert thành New/Unknown kể cả đã review; CreatedAt/SLA bắt đầu tại import, giữ OriginalEvaluatedAt, source ImportedLegacy. Review cũ chỉ hiển thị “Kết luận cũ, chưa xác minh theo quy trình Alert”; không tự Closed/map false→Legitimate. Đề xuất ngừng ghi POST review cũ: 409 `LEGACY_REVIEW_DISABLED`, hướng dẫn Alert; GET lịch sử còn, không song song hai cơ chế ghi nhãn.
- **BR-19 — UI, đề xuất Q8:** poll 10 giây khi tab visible, refresh sau mutation/reconnect/focus; hiển thị last successful sync. Gắn badge stale khi quá 30 giây từ lần đồng bộ thành công, hoặc ngay khi request lỗi/offline; chưa đồng bộ lần nào cũng stale. Chỉ bỏ badge sau khi snapshot mới tải thành công, không chỉ vì mạng vừa nối lại. Ngưỡng 10/30 giây là đề xuất Q8. 409 tải bản hiện hành, không tự gửi lại quyết định. Chưa SignalR/SSE hoặc delivery realtime. Quyền và concurrency vẫn ở server khi UI cũ.

## 5. User stories & Acceptance criteria (US-01 → AC-01.1, AC-01.2…)

AC theo phương án đề xuất; sửa đồng bộ khi chốt Q1–Q8. Test dùng clock điều khiển và DB cô lập, không DB demo/public. Hiện chưa chạy các TC này.

### US-01 — Tạo công việc riêng
Là Analyst, tôi muốn một đánh giá đáng ngờ tạo đúng một công việc để không bỏ sót/xử lý lặp (BR-01–03).
- **AC-01.1:** Given giao dịch mới RulesOnly/Hybrid; When kết quả REVIEW/BLOCK; Then đúng một Alert New/Unknown, owner null, link Evaluation, đầy đủ lý do/priority theo BR-09; ALLOW không Alert. Bao gồm AI timeout→REVIEW fallback và nhiều rule cùng khớp.
- **AC-01.2:** Given yêu cầu đã commit; When replay cùng ID/nội dung 100 lần; Then một Transaction/Evaluation/Alert/Created event; đổi nội dung cùng ID 409, không đổi dữ liệu.
- **AC-01.3:** Given lỗi ghi Alert/event được tiêm trước commit; When phân tích REVIEW/BLOCK; Then rollback cả bộ, không trả thành công; retry sau khắc phục tạo đúng một bộ.

### US-02 — Nhận việc an toàn
Là Analyst, tôi muốn rõ người phụ trách để không cùng nhận một việc (BR-04–06).
- **AC-02.1:** Given New version 1; When hai Analyst claim đồng thời qua hai DB context/connection; Then một commit Assigned/owner thắng/version 2/event Claimed, bên thua 409; tải lại thấy owner. Không dùng chung EF context để chứng minh concurrency.
- **AC-02.2:** Given claim requestId R đã commit nhưng mất response; When cùng actor replay R/payload/version cũ; Then trả kết quả commit cũ, không thêm event; đổi payload/actor dùng R trả 409.
- **AC-02.3:** Given Analyst không owner hoặc payload giả actor/owner; When gọi lệnh xử lý Assigned/InProgress; Then 403 hoặc 400 cho field không hỗ trợ, không đổi version/owner/history; thiếu token 401.

### US-03 — Điều tra theo vòng đời
Là owner, tôi muốn bắt đầu/chờ/tiếp tục mà vẫn chịu trách nhiệm (BR-06–08).
- **AC-03.1:** Given owner nhận Assigned; When Start, Wait có lý do/hạn tương lai, Resume; Then InProgress→Waiting→InProgress, giữ owner, mỗi bước tăng version/history; thiếu lý do hoặc hạn sai 400.
- **AC-03.2:** Given New/Assigned/Closed; When gọi cạnh không cho phép gồm New→Closed, Assigned→Closed, Closed→InProgress; Then 409 transition invalid, không thay dữ liệu.
- **AC-03.3:** Given hai UI cùng version InProgress; When một UI Wait rồi UI kia Close version cũ; Then Close 409, không đóng; UI reload, không tự gửi lại quyết định.

### US-04 — Điều phối/ưu tiên
Là Admin, tôi muốn phân công và đổi mức xử lý có lý do (BR-06/09/11).
- **AC-04.1:** Given Assigned/InProgress/Waiting/Escalated; When Admin giao lại owner hợp lệ với reason; Then Assigned/owner mới/xóa waiting hiện hành, giữ dueAt/history; user không tồn tại 400, Analyst giao người khác 403.
- **AC-04.2:** Given Assigned chưa Start; When Admin Release có lý do; Then New/null owner/giữ dueAt; Release từ InProgress/Waiting/Closed 409.
- **AC-04.3:** Given Alert chưa Closed P2 có deadline D; When Admin nâng P1 rồi hạ P3; Then deadline giữ hoặc sớm hơn đúng BR-11, history cũ/mới/reason; Analyst đổi 403, đổi Closed 409.

### US-05 — Theo dõi thời hạn
Là owner, tôi muốn thời gian còn lại đúng dù giao lại hoặc chờ (BR-10–11).
- **AC-05.1:** Given P1 tạo tại T với transaction timestamp cũ; When đọc T+14m59s và T+15m; Then dueAt=T+15m, lần đầu chưa overdue/lần sau overdue; P2/P3 đúng 60/240 phút.
- **AC-05.2:** Given deadline D; When claim/start/wait/resume/reassign nhiều lần hoặc sửa default policy; Then D/snapshot cũ không đổi, Waiting qua D vẫn overdue, không auto Closed/Legitimate.
- **AC-05.3:** Given đang mở có D; When Close tại D và D+1 tick trong hai fixture; Then closedWithinSla lần lượt true/false; Closed không nằm trong overdue mở, không xóa breach history.

### US-06 — Chuyển cấp có tiếp nhận
Là Admin, tôi muốn thấy việc hết hạn mà không lặp thông báo (BR-12–13).
- **AC-06.1:** Given New owner null hoặc Waiting có owner đạt dueAt/waitingUntil; When worker quét trong ≤30 giây khi hoạt động bình thường; Then Escalated/giữ owner null hoặc cũ, đúng một event/reason/episode, có queue Admin; nhiều tick không lặp, không SMS/email.
- **AC-06.2:** Given API dừng qua deadline rồi khởi động; When worker quét dữ liệu đã lưu; Then chưa Closed chuyển cấp tối đa một lần/episode, Closed không đổi; worker và Close đồng thời không thể cùng commit một version.
- **AC-06.3:** Given Escalated overdue; When Admin Assign tại T; Then Assigned/episode mới/overdue vẫn true; trước T+5m không auto-escalate, từ T+5m đúng một lần nếu còn mở. Admin có thể Start/Close trước đó; deadline tương lai dùng mốc muộn hơn dueAt và hết grace.

### US-07 — Đóng trung thực
Là owner, tôi muốn ghi được chưa rõ thay vì ép chọn hợp lệ (BR-01/14).
- **AC-07.1:** Given owner InProgress/Waiting; When Close từng cặp reason/resolution BR-14 và notes hợp lệ; Then Closed lưu actor/UTC/owner/kết luận; sai cặp, Fraud/Legitimate thiếu evidence, notes trim <5 hoặc >1000 trả 400.
- **AC-07.2:** Given thiếu/mâu thuẫn bằng chứng; When đóng InsufficientEvidence/Unknown hoặc ConflictingEvidence/Disputed; Then đóng được, không sinh review false/đếm hợp lệ/sửa Evaluation/auto train; UI ghi đúng chưa đủ bằng chứng hoặc mâu thuẫn.
- **AC-07.3:** Given owner khác hoặc status New/Assigned/Escalated/Closed; When Close; Then sai quyền 403, có quyền nhưng sai state 409; replay requestId thành công không thêm closure/event.

### US-08 — Dấu vết/mở lại
Là Analyst/Admin, tôi muốn xem lịch sử và tiếp tục khi có tin mới (BR-15–16).
- **AC-08.1:** Given owner có Alert chưa Closed; When thêm note trim 5–1000 ký tự; Then append-only với actor/UTC/version trong timeline, không endpoint sửa/xóa; Analyst khác không thêm được.
- **AC-08.2:** Given Closed có kết luận vòng trước; When Admin Reopen có lý do/owner hợp lệ; Then Assigned/Unknown/episode và SLA mới, xóa ClosureReasonCode/ClosureNotes/ClosedAt/ClosedByUserId khỏi snapshot hiện hành nhưng giữ nguyên closure/kết luận/SLA cũ trong history, không mang Waiting/Escalated hiện hành của vòng cũ; Analyst 403, replay không lặp vòng.
- **AC-08.3:** Given một mutation commit và một rollback; When Analyst xem timeline/Admin xem audit; Then chỉ thay đổi thành công theo version/event ID, không secret/raw IP/device, audit truy cùng requestId; Analyst gọi audit hệ thống 403.

### US-09 — Hàng đợi đáng tin
Là Analyst/Admin, tôi muốn lọc công việc và biết khi dữ liệu cũ (BR-17/19).
- **AC-09.1:** Given đủ sáu state/priority/owner/overdue; When lọc Mine/Unassigned/status/priority/overdue/search và phân trang; Then items/total cùng filter DB; mặc định ẩn Closed, sort P1→P2→P3, dueAt tăng, createdAt tăng, Id; page≥1,size 1–100, enum sai 400.
- **AC-09.2:** Given hai phiên; When A claim/update, B polling rồi mất mạng/focus lại; Then B lấy snapshot commit mới, hiển thị last successful sync; quá 30 giây chưa đồng bộ thành công hoặc request lỗi/offline thì stale ngay, reconnect chưa bỏ badge cho đến khi tải snapshot mới thành công; response cũ không đè version mới; logout/đổi tài khoản xóa state và hủy request cũ.
- **AC-09.3:** Given review legacy, Closed Unknown, Waiting chưa review; When xem dashboard; Then openAlerts đếm Waiting/loại Closed Unknown; ClosedUnknown riêng; pending legacy giữ công thức/nhãn rõ; REVIEW/BLOCK giữ quyết định gốc.

### US-10 — Nâng cấp giữ dữ liệu
Là chủ dự án, tôi muốn bảo toàn lịch sử và biết thay đổi API (BR-18).
- **AC-10.1:** Given DB cũ gồm ALLOW, non-ALLOW có/không review, review trái nhau và reviewer không khớp Users; When migration/backfill rồi rerun; Then mỗi non-ALLOW một ImportedLegacy/New/Unknown, ALLOW không Alert, không mất review/giả owner/nhãn; SLA từ import; rerun không reset mốc/event.
- **AC-10.2:** Given frontend mới/client cũ; When GET giao dịch/reviews cũ hoặc POST reviews; Then GET còn tương thích, POST 409 LEGACY_REVIEW_DISABLED có hướng dẫn/traceId; API mới không ghi bool legacy/đổi GET âm thầm.
- **AC-10.3:** Given test hiện có RulesOnly/fallback/critical/idempotency/snapshot/listing/dashboard legacy; When triển khai sau này trên DB test; Then giữ các bất biến và thêm Alert/SLA riêng; công khai assertion cần bổ sung, không đổi kỳ vọng legacy để che lỗi.

## 6. Luồng xử lý (chính / thay thế / ngoại lệ)

### Ma trận chuyển trạng thái — đề xuất Q2/Q5/Q6

A=owner Analyst/Admin, M=Admin, S=system. Cạnh không liệt kê bị từ chối.

| Từ | Lệnh → Đến | Actor/điều kiện | Owner/SLA |
|---|---|---|---|
| Chưa tồn tại | Create→New | S, REVIEW/BLOCK | Null, mở SLA |
| New | Claim→Assigned | Analyst/Admin tự nhận | Chính mình, giữ SLA |
| New | Assign→Assigned | M chỉ định | User hợp lệ, giữ SLA |
| Assigned | Start→InProgress | A | Giữ |
| Assigned | Release→New | M, reason | Null, giữ SLA |
| Assigned/InProgress/Waiting | Reassign→Assigned | M, reason | Owner mới, xóa waiting, giữ SLA |
| InProgress | Wait→Waiting | A, reason/until | Giữ, SLA chạy |
| Waiting | Resume→InProgress | A | Giữ, xóa waiting hiện hành |
| New/Assigned/InProgress/Waiting | Escalate→Escalated | S theo hạn; A/M thủ công reason (New chỉ M) | Giữ owner/null, giữ SLA |
| Escalated | Assign/TakeOver→Assigned | M, reason | Owner chỉ định/M, episode/grace mới |
| InProgress/Waiting | Close→Closed | A, reason/resolution/notes | Giữ owner, kết thúc vòng SLA |
| Closed | Reopen→Assigned | M, reason/owner | Vòng SLA/episode mới, giữ history |

Priority/note không đổi state. Reassign trạng thái chưa Escalated phải chọn owner khác, nếu cùng owner trả 400; Escalated cho Admin tiếp nhận lại cùng owner qua Assign/TakeOver có lý do. Tất cả mutation kiểm version, actor và điều kiện; system unique escalation theo episode.

**Chính:** analyze→commit đủ bộ→New→claim→Assigned→Start→InProgress→Close→Closed.

**Thay thế:** InProgress→Waiting→Resume; hết hẹn/SLA→Escalated→Admin Assign→Assigned; thiếu chứng cứ có thể đóng Unknown hoặc tiếp tục Waiting; Admin Reopen khi có tin mới. Không tự chốt nhãn vì hết hạn.

**Ngoại lệ:** 401 thiếu/sai token; 403 không quyền; 404 không tồn tại; 400 dữ liệu sai; 409 state/version/idempotency conflict; 503 DB tạm bận kèm Retry-After. Lỗi có code/traceId, không lộ DB. Timeout sau commit dùng requestId retry; không suy thất bại là chưa lưu. Server UTC quyết định, UI không quyết định deadline.

## 7. Dữ liệu & ràng buộc

Mô hình logic đề xuất; Dev quyết định mapping theo Clean Architecture, không đổi framework.

| Trường/đối tượng | Kiểu | Bắt buộc | Ràng buộc |
|---|---|---|---|
| Alert.Id | UUID PK | Có | Sinh server |
| TransactionId/EvaluationRef | UUID FK | Có | Unique TransactionId, Evaluation tồn tại; hạn chế xóa hồ sơ dây chuyền |
| Status | enum string | Có | Đúng sáu giá trị |
| OwnerUserId | int FK Users | Theo state | Null chỉ New/Escalated chưa nhận; không dùng customer ID |
| Priority/PriorityReason | P1/P2/P3, text | Có | Mapping hoặc override reason trim 5–1000 |
| SourceType | enum | Có | Rules/Hybrid/AiUnavailableFallback/ImportedLegacy |
| CreatedAt/OriginalEvaluatedAt/UpdatedAt | UTC | Có | Server/mốc gốc, không reset CreatedAt |
| Version | long | Có | Khởi tạo 1, token tăng một/mutation; SQLite không có rowversion tự động |
| Resolution | enum | Có | Unknown/Fraud/Legitimate/Disputed, không default bool false |
| ClosureReasonCode/ClosureNotes | enum/text | Khi Closed | Cặp BR-14, trim 5–1000 |
| ClosedAt/ClosedByUserId | UTC/int FK | Khi Closed | Server; người đóng owner |
| EvidenceReferences | records | Fraud/Legitimate khi Close | 1–10; sourceType ManualInvestigation/InternalRecord/Other; reference 1–300, description 5–1000, capturedAt UTC không tương lai, creator server. Không upload/fetch URL/secret/PII thô; không tự xác minh nguồn |
| WaitingType/Reason/Until | enum/text/UTC | Khi Waiting | InternalReview/ExternalInformation/Other; reason trim 5–1000; now<until≤now+7 ngày (đề xuất Q4) |
| SlaPolicyVersion/BudgetMinutes | int | Có | Snapshot dương mỗi vòng SLA |
| SlaStartedAt/DueAt | UTC | Có | BR-10/11; giữ từng vòng trong history |
| HandlingEpisode/EscalatedAt/Reason | int/UTC/enum | Theo state | Episode≥1, unique event Escalated/Alert/episode |
| EscalationEligibleAt | UTC | Có | Lúc tạo=CreatedAt; sau tiếp nhận Escalated=now+5m |
| AlertEvent | UUID, AlertId, type, actor/system, UTC, before/after, version, reason, requestId | Mỗi mutation | Append-only; đủ snapshot SLA/owner/resolution từng vòng; thứ tự version rồi event ID |
| AlertCommandReceipt | AlertId+RequestId unique, actor/command/hash/result/version/UTC | Lệnh người dùng commit | Cùng transaction; không secret; đề xuất giữ theo vòng đời hồ sơ |
| AlertNote | UUID,AlertId,text,actor,UTC | Khi ghi | Trim 5–1000; append-only, link event/version |

Index dự kiến: unique TransactionId; `(Status,Priority,DueAt,Id)`, `(OwnerUserId,Status,DueAt)`, `(Status,DueAt)`, `(AlertId,Version)` cho timeline; receipt unique riêng. Count/filter tại DB, không cắt mẫu 10.000 rồi tính tổng. Validation server là cổng cuối; DB bảo vệ FK/unique/compare-and-swap. Tham chiếu snapshot Evaluation bất biến, không nhân bản toàn bộ snapshot trong mọi event.

## 8. Phân tích tác động (file/module/bảng/API)

### Module dự kiến

Đường dẫn backend dưới service/layer mục 1; đây là dự kiến, chưa tạo code.

| Mã | File/module | Tác động |
|---|---|---|
| M1 | Domain/Entities, Domain/Repositories/IFraudRepository.cs | Alert/event/note/receipt, enums và invariant; Domain không EF/HTTP |
| M2 | Application/Features/Transactions/Services/DetectionService.cs, Infrastructure/Repositories/FraudRepository.cs | Tạo bộ nguyên tử, giữ idempotency |
| M3 | Application/Features/Alerts/Commands | Claim/start/wait/resume/assign/release/priority/escalate/close/reopen/note; validators/owner |
| M4 | Infrastructure/Persistence/FraudDbContext.cs, Configurations, Migrations, repository | Bảng/FK/index/concurrency/receipt/transaction; migration mới |
| M5 | Alert controller mới, TransactionsController.cs, Program.cs, Shared.Security/Permissions/FraudPermissions.cs | Policies/DI/principal→UserId/lỗi/legacy write |
| M6 | Infrastructure worker mới, clock port Application | Quét SLA/restart recovery, cùng invariant/transaction, không broker |
| M7 | Query/DTO Alert mới; Front-end/src/App.tsx, feature Alerts mới, shared/services/api.ts | Queue/filter/timeline/action/Unknown/Disputed; tách detail Alert với transaction |
| M8 | FraudRepository.StatsAsync, OperationsController.cs, Stats DTO/UI | Counter Alert riêng, giữ legacy/risk decisions |
| M9 | Migration/backfill, ReviewTransactionCommand.cs, frontend review services | Không giả nhãn cũ, chặn ghi legacy theo Q7 |

### API đề xuất

- `GET /api/alerts?status=&priority=&owner=mine|unassigned|<userId>&overdue=&search=&page=&size=` trả `{items,total,page,size,serverNow}`. Search≤100 ký tự; AlertId/TransactionId khi UUID hợp lệ, UserId/Merchant theo text; sort AC-09.1. Owner cụ thể chỉ Admin; Analyst mine/unassigned/all workspace.
- `GET /api/alerts/{id}` trả Alert, đánh giá gốc/reference, timeline phân trang, version/dueAt/serverNow/allowedActions. Commands vẫn kiểm quyền lại.
- `POST /api/alerts/{id}/claim|start|wait|resume|assign|release|priority|escalate|close|reopen|notes`: luôn `{requestId,expectedVersion,...fields}`; actor server. Claim chính mình; assign/reopen ownerUserId; close reason/resolution/notes/evidence; wait type/reason/until. Không endpoint set state tùy ý.
- Mutation trả 200 `{alert,appliedVersion,replayed}`; replay có thể là version cũ đã commit, UI tải snapshot hiện tại nếu cần. Lỗi `{error,code,traceId,currentVersion?}`; không tiết lộ dữ liệu ngoài quyền, chỉ trả currentVersion sau kiểm quyền.
- `GET /api/alerts/stats`: tổng state/priority/owner, openAlerts/overdue/ClosedUnknown/ClosedDisputed, asOf UTC; công bố rõ phạm vi filter. Dashboard hiện tại có thể thêm trường, không đổi kiểu/nghĩa trường cũ.
- Giữ GET transaction/export/reviews; optional alertId additive, không thay Transaction.Id thành AlertId. UI mới không gọi POST boolean review. Không map Unknown/Disputed sang ReviewRequest.

### Migration/backfill và phát hành dự kiến

1. Khi triển khai sau này, backup DB, kiểm migration trên bản sao; baseline counts/checksum giao dịch/đánh giá/review/audit. Không chạy smoke ghi dữ liệu trên DB thật.
2. Thêm schema/index/constraint, backfill có migration ID/checkpoint và unique TransactionId; event ImportedLegacy cùng commit. Rerun bỏ qua Alert có sẵn, không reset SLA/mốc. Dữ liệu lớn phải đo thời gian/dung lượng/cửa sổ trước deploy.
3. Reviewer cũ không khớp Users giữ source text; không tạo user giả/gán Admin. Review trái nhau chỉ thông tin legacy, không latest bool→kết luận hiện hành.
4. Đối soát mọi non-ALLOW đúng một Alert, ALLOW không Alert, count/hash gốc không đổi, FK không orphan, không tự Closed. Chỉ bật worker sau backfill; backlog import có thể hết hạn đồng loạt, phải chốt Q7 và kiểm lượng trước deploy.
5. Backend/frontend phát hành phối hợp: POST review bị chặn là thay đổi có chủ ý, không gọi tương thích ghi hoàn toàn. GET/trường stats cũ còn và gắn nhãn legacy.
6. Rollback ứng dụng phải dừng worker và giữ bảng/history mới, không chạy Down xóa điều tra phát sinh. Restore backup cần đối soát điều tra sau backup. Lần này không thực hiện các bước phát hành.

## 9. Yêu cầu phi chức năng

- DB là nguồn owner/version; unique/FK/transaction qua nhiều connection. EvaluationGate chỉ xử lý giao dịch trong một process, không thay concurrency Alert.
- **Mục tiêu hiệu năng đề xuất, chưa đo:** SQLite test 100.000 Alert, page 20, 10 client đọc+2 mutation; máy 4 vCPU/8GB/SSD, warm-up 30s rồi đo 5 phút; p95 list/detail/mutation≤1s (không tính AI/mạng ngoài). Count toàn DB, tránh N+1. Ghi điều kiện/kết quả; không hứa đạt khi chưa chạy.
- Worker hoạt động bình thường commit escalation≤30s sau mốc đủ điều kiện; restart quét backlog kỳ đầu. Batch lớn cần metric oldest overdue/escalation lag, đo năng lực trước cam kết. Worker retry, không dùng log thay persisted event.
- Kiểm quyền server mọi command; actor không từ body; không token/password/raw IP/device trong history/log. Notes render text escape, không HTML. Scope một workspace, chưa hứa multi-tenant.
- Logs traceId/requestId/AlertId/command/conflict/error/latency; metrics open/overdue/worker last success/escalation lag. Audit cùng commit, rollback không có event thành công.
- UI tiếng Việt, enum API ổn định; UTC lưu, hiển thị múi giờ trình duyệt có thông tin timezone; state/priority/SLA có nhãn ngoài màu. Dialog labels/focus/bàn phím; refresh không mất draft note hoặc tự gửi quyết định stale.
- Poll visible 10s và refresh mutation/reconnect; hiển thị last-sync/stale. Dùng tên “cập nhật định kỳ”, chưa quảng bá realtime push.
- Không thêm state framework, MediatR/AutoMapper hoặc hạ tầng trái CLAUDE.md; không tự train từ kết luận Alert.

## 10. Giả định

- A1: một workspace, Admin/Analyst; không thêm quản trị tài khoản/khóa user. Nếu sau này deactivate user phải chặn assign và bàn giao; hiện chưa có IsActive, không giả field đó tồn tại.
- A2: Alert MVP, chưa full Case. ALLOW có khiếu nại muộn cần feature tạo thủ công ở giai đoạn sau.
- A3: priority/SLA 15/60/240m, grace 5m, waitingUntil≤7 ngày, poll 10s là đề xuất demo cần chốt, không SLA thật.
- A4: Closed Unknown nghĩa kết thúc công việc chưa rõ thật/giả. Chủ dự án có thể yêu cầu luôn Waiting/Escalated thay vì Closed — Q5.
- A5: evidence reference nhập tay tối thiểu, chưa xác thực nguồn độc lập. Không triển khai nhãn ground truth/FP/FN/precision hoặc train trong scope này.
- A6: chưa đặt retention/xóa dữ liệu mới; đề xuất giữ history/receipt, cần chính sách trước production. Không sửa DB demo khi làm BA.

## 11. Câu hỏi mở (kèm phương án đề xuất)

| Mã | Quyết định cần trả lời trước Dev | Mặc định đề xuất và ảnh hưởng |
|---|---|---|
| Q1 | Trigger Alert nào? Một Alert/giao dịch đủ không? | REVIEW+BLOCK kể cả fallback; một Alert/Evaluation; chưa ALLOW/thủ công/gộp nhiều giao dịch. ALLOW báo muộn ở feature sau. |
| Q2 | Admin kiêm trưởng nhóm? Ai giao lại/giải phóng/nhận escalation? | Admin điều phối; Analyst claim New và chỉ sửa việc mình. Escalated giữ owner cũ/null, Admin nhận/giao. Cả hai role xem workspace queue. |
| Q3 | Mức ưu tiên/mapping/deadline cụ thể? Fallback AI có được P3? | P1 BLOCK/15m, P2 REVIEW/60m, P3 fallback/240m. Fallback có thể cần P2/P1 nếu rủi ro cao. Nâng rút hạn, hạ không kéo dài. |
| Q4 | SLA 24/7 hay giờ làm? Waiting dừng không? Chuyển cấp lặp thế nào? | 24/7, Waiting không dừng; hết waitingUntil (≤7 ngày) hoặc SLA chuyển cấp; tiếp nhận lại grace 5m nhưng vẫn overdue. Chưa SLA phản hồi riêng. |
| Q5 | Ai đóng? Cho Closed Unknown/Disputed? Mức bằng chứng nào? | Owner từ InProgress/Waiting; Admin nhận lại trước khi đóng hộ. Cho Unknown/Disputed với lý do; Fraud/Legitimate có nguồn+reference, chưa ground truth độc lập. Nếu chưa rõ phải luôn mở thì đổi sang Waiting/Escalated. |
| Q6 | Reopen có trong MVP? Ai làm, hạn tính lại thế nào? | Admin mở thành Assigned, chọn owner, Unknown/vòng SLA mới; giữ kết luận/SLA cũ, chưa maker-checker. |
| Q7 | Backfill mọi non-ALLOW hay chỉ chưa review/từ cutover? Chấp nhận ngừng POST review cũ? | Mọi non-ALLOW thành New/Unknown từ import; giữ reviews legacy, không tự Closed. POST 409, GET còn, stats Alert riêng. Có thể backlog/quá hạn đồng loạt, cần chốt lượng trước deploy. |
| Q8 | Cập nhật định kỳ/nội bộ đủ hay bắt buộc push/email? Chấp nhận ngưỡng stale nào? | Poll 10s+refresh sau thao tác/reconnect, queue Admin; stale khi quá 30s chưa sync thành công hoặc request lỗi/offline, chỉ bỏ sau snapshot mới thành công. Chưa SignalR/SSE/email/webhook. Nếu push bắt buộc phải bổ sung AC commit/reconnect/đối soát. |

Người dùng có thể chấp nhận mặc định Q1–Q8 và chỉ rõ mục muốn đổi. Đây chỉ là đầu ra BA, chưa chuyển Dev/Tester/QA/deploy.

## 12. Ma trận truy vết (AC ↔ module dự kiến ↔ test case dự kiến)

**Toàn bộ TC là kế hoạch, chưa có test Alert/chưa chạy lần này.** M1–M9 tại mục 8. Đề xuất file `AlertWorkflowTests.cs`, `AlertConcurrencyTests.cs`, `AlertSlaTests.cs`, `AlertMigrationTests.cs`, API integration DB tạm và UI E2E; không ghi “đã pass” cho kế hoạch.

| AC | BR | Module | Test case dự kiến |
|---|---|---|---|
| AC-01.1 | 01,02,09 | M1,M2,M4 | TC-A01: RulesOnly/Hybrid/ALLOW/REVIEW/BLOCK/fallback, nhiều rule một Alert |
| AC-01.2 | 03 | M2,M4 | TC-A02: 100 replay/conflict payload/count bộ dữ liệu |
| AC-01.3 | 03 | M2,M4 | TC-A03: inject failure trước commit, rollback/retry |
| AC-02.1 | 04,06 | M3,M4,M5 | TC-A04: SQLite file tạm/hai connection, race claim 200/409, một owner/event |
| AC-02.2 | 05 | M3,M4 | TC-A05: mất response/replay version cũ/đổi actor/payload |
| AC-02.3 | 04,06 | M3,M5 | TC-A06: API owner khác/actor giả/thiếu token 403/400/401 |
| AC-03.1 | 07,08 | M1,M3,M7 | TC-A07: workflow start/wait/resume, biên reason/until |
| AC-03.2 | 07 | M1,M3 | TC-A08: bảng toàn bộ cặp chuyển trạng thái bị cấm |
| AC-03.3 | 04 | M3,M4,M7 | TC-A09: hai tab, stale close sau wait, không tự retry |
| AC-04.1 | 06,07 | M3,M4,M5 | TC-A10: reassign mọi state hợp lệ, user thiếu/role sai |
| AC-04.2 | 06,07 | M1,M3 | TC-A11: release Assigned, từ chối state khác, dueAt giữ |
| AC-04.3 | 09,11 | M3,M6 | TC-A12: clock nâng/hạ priority, min deadline, role/Closed denial |
| AC-05.1 | 10 | M1,M6,M7 | TC-A13: clock trước/tại hạn P1/2/3, eventTime cũ |
| AC-05.2 | 10,11 | M3,M6 | TC-A14: clock Waiting/reassign/policy không reset/pause |
| AC-05.3 | 11,17 | M3,M6,M8 | TC-A15: Close tại D/D+tick, breach history/counter |
| AC-06.1 | 06,08,12 | M4,M6,M7 | TC-A16: worker New/Waiting, deadline cùng lúc, owner, nhịp 30s/unique episode |
| AC-06.2 | 04,12 | M3,M4,M6 | TC-A17: restart và hai connection race worker/close |
| AC-06.3 | 13 | M3,M6 | TC-A18: clock Admin tiếp nhận, grace 5m, overdue, một event/episode |
| AC-07.1 | 14 | M1,M3,M7 | TC-A19: bốn cặp reason/resolution, evidence, notes trim 4/5/1000/1001 |
| AC-07.2 | 01,14,17 | M3,M7,M8 | TC-A20: Unknown/Disputed, không false review/sửa Evaluation/train |
| AC-07.3 | 05,07,14 | M3,M5 | TC-A21: close sai owner/state, replay không thêm closure |
| AC-08.1 | 16 | M3,M4,M7 | TC-A22: note boundary/owner/Closed/append-only, escape script |
| AC-08.2 | 15 | M3,M4,M6 | TC-A23: reopen giữ vòng trước, vòng mới Unknown, role/requestId |
| AC-08.3 | 16 | M4,M5,M7 | TC-A24: rollback/audit permissions/redaction/timeline order |
| AC-09.1 | 17,19 | M4,M7 | TC-A25: filter/count/sort/page/size/enum/search biên |
| AC-09.2 | 04,19 | M5,M7 | TC-A26: UI hai phiên/poll/reconnect/response đảo thứ tự/đổi user |
| AC-09.3 | 17 | M8 | TC-A27: dashboard ClosedUnknown/Waiting/legacy, count toàn DB |
| AC-10.1 | 18 | M4,M9 | TC-A28: migration fixture cũ đủ nhánh, rerun/checkpoint/count/hash/FK |
| AC-10.2 | 18 | M5,M7,M9 | TC-A29: GET/export/review cũ, POST 409, frontend dùng Alert |
| AC-10.3 | 01,03,17 | M2,M8,M9 | TC-A30: suite regression hiện có + assertion Alert mới có giải thích |

### Test hiện có cần giữ/mở rộng sau triển khai

- `DetectionTests.cs`: `RulesOnlyNeverCallsAiAndPersistsNullScore`, `UnavailableAiDoesNotFailOpen`, `CriticalRuleCannotBeDilutedByModel`, `IdempotentReplayDoesNotAddHistoryAndConflictingReplayFails`, `SnapshotKeepsRuleVersionAfterPolicyChanges`; bổ sung Alert khi phù hợp TC-A01/A02/A30. AmountBoundaries/FeatureHistory/Hmac bảo vệ scoring/PII, không chứng minh workflow Alert.
- `ReviewRegressionTests.cs`: `ListingUsesTwoQueriesRegardlessOfPageSize`, `DashboardCountsAllRowsAndReviewsBeyondTheChartSample` bảo vệ list/stats legacy; thêm suite Alert riêng, giữ tổng toàn DB. `GateAllowsUnrelatedWorkAndCancellationReleasesPartialLocks`, `GateSerializesEverySharedResource` kiểm gate giao dịch một process, không chứng minh claim Alert hai DB connection.
- Hai file hiện có chưa có test sáu trạng thái/owner/SLA/Closed Unknown/backfill Alert. Dev/Tester cần hiện thực TC-A01–A30 sau khi được duyệt; QA đối chiếu từng AC và phân biệt kết quả chạy thực tế với kế hoạch.
