# Triển khai quản lý Alert — 09/11/2026

## Thiết kế kỹ thuật

Người dùng đã chọn **Dùng toàn bộ mặc định Q1–Q8** ngày 09/11/2026. Đây là cơ sở triển khai bản BA 0.1; không mở rộng sang chuyển tiền, SMS hoặc mô hình AI. Theo yêu cầu tiếp theo cùng ngày, đã tạo commit code `3dde305` và deploy lên demo tạm; trạng thái push phải đối chiếu remote GitHub.

- Domain: Alert riêng, sáu trạng thái, priority/SLA, owner, resolution; event append-only và receipt chống lặp lệnh.
- Application: workflow thuần với kiểm quyền/chuyển trạng thái/validation; port repository riêng; TimeProvider để kiểm thử clock.
- Infrastructure: SQLite FK/unique/concurrency token Version. Giao dịch và Alert tạo trong cùng SaveChanges transaction. Mutation lưu Alert/event/receipt/audit cùng commit; worker dùng scope riêng và xử lý race bằng version DB.
- API: /api/alerts truy vấn/lệnh/stats/users; actor từ tài khoản đang xác thực. API review boolean cũ ngừng ghi có mã lỗi rõ ràng, GET còn nguyên.
- Migration: thêm schema; backfill idempotent tại startup sau migration, trước worker, dùng mốc import thay timestamp giao dịch. Không chạy migration trên database đang phục vụ trong quá trình phát triển.
- UI: queue Alert riêng, lọc/nhận việc/điều tra/chờ/chuyển cấp/đóng Unknown hoặc Disputed, timeline. Poll 10s; stale quá 30s hoặc lỗi; không mất draft khi refresh.

## Danh sách file thay đổi

Đường dẫn backend dưới `Back-end/src/Services/FraudDetectionService/`:

| File | Thay đổi |
|---|---|
| `FraudDetectionService.Domain/Entities/Alert.cs` | Alert, event, receipt; policy mặc định; `ClosedWithinSla` tính bằng UTC ticks |
| `FraudDetectionService.Application/Features/Alerts/AlertContracts.cs` | Port, DTO, actor, command strict JSON, filter, lỗi có mã |
| `FraudDetectionService.Application/Features/Alerts/AlertWorkflowService.cs` | Validation, quyền owner/Admin, state machine, receipt replay, clock, escalation |
| `FraudDetectionService.Infrastructure/Repositories/AlertRepository.cs` | Projection/lọc/count/phân trang tại DB; compare-and-swap Version; event/receipt/audit nguyên tử |
| `FraudDetectionService.Infrastructure/Repositories/FraudRepository.cs` | Tạo Alert/event cùng transaction/evaluation bằng một SaveChanges |
| `FraudDetectionService.Infrastructure/Persistence/FraudDbContext.cs` | Ba DbSet mới |
| `FraudDetectionService.Infrastructure/Persistence/Configurations/AlertConfiguration.cs` | FK restrictive, check state/owner/priority, unique TransactionId/version/episode |
| `FraudDetectionService.Infrastructure/Persistence/Migrations/20261109023043_AddAlertWorkflow{,.Designer}.cs`, `FraudDbContextModelSnapshot.cs` | Schema mới và snapshot EF |
| `FraudDetectionService.Infrastructure/Services/Alerts/AlertBackfill.cs` | Backfill batch 500, chỉ thiếu Alert; event ImportedLegacy; không reset khi chạy lại |
| `FraudDetectionService.Api/Controllers/AlertsController.cs` | List/detail/timeline/stats/users và command; actor tra từ tài khoản server |
| `FraudDetectionService.Api/Services/AlertSlaWorker.cs` | Quét ngay khi startup và mỗi 15 giây, retry kỳ sau khi lỗi |
| `FraudDetectionService.Api/Controllers/TransactionsController.cs` | POST review cũ trả 409 LEGACY_REVIEW_DISABLED; GET không đổi |
| `FraudDetectionService.Api/Program.cs` | DI, TimeProvider, worker, lỗi Alert, startup backfill trước phục vụ |
| `Back-end/tests/FraudDetectionService.Tests/AlertWorkflowTests.cs` | Kiểm thử SQLite thật, hai connection, clock và migration |
| `Front-end/src/features/alerts/_services/services.ts` | DTO/API/nhãn trạng thái và kết luận |
| `Front-end/src/features/alerts/_components/alert-queue.tsx` | Queue, dialog, command, timeline dễ đọc, polling/stale/retry |
| `Front-end/src/App.tsx` | Route Alert riêng, badge/dashboard openAlerts; link từ chi tiết giao dịch |
| `Front-end/src/features/transactions/_components/transaction-detail.tsx` | Bỏ nút ép review bool; giữ lịch sử review legacy |
| `Front-end/src/features/transactions/_services/services.ts` | Bỏ client submitReview cũ |
| `Front-end/src/shared/services/api.ts`, `styles.css` | ApiError có status/code; kiểu hiển thị Alert và mobile |
| `scripts/alert_check.py`, `alert_ui_check.cjs` | HTTP regression bằng DB tạm; UI Edge headless tùy chọn |
| `scripts/smoke_test.py`, `.github/workflows/ci.yml` | Smoke đóng Unknown, xác nhận chặn legacy; thêm Alert HTTP vào CI |
| `README.md`, `docs/ARCHITECTURE_MAP.md`, `docs/VERIFICATION.md`, `docs/team/01-requirements.md` | Contract/migration, định vị, kết quả và ghi nhận chốt Q1–Q8 |

Giữ nguyên các thay đổi có sẵn trước phiên này; bảng trên chỉ mô tả phần Alert, không nhận là tác giả của các sửa lỗi phiên trước.

## Migration / cấu hình mới

Migration `20261109023043_AddAlertWorkflow` thêm `Alerts`, `AlertEvents`, `AlertCommandReceipts`. SLA mặc định P1=15, P2=60, P3=240 phút; grace=5 phút, policy version=1. Không yêu cầu hạ tầng mới.

- Unique TransactionId nối FK Evaluation; New không owner; Assigned/InProgress/Waiting/Closed bắt buộc owner. Version là concurrency token do ứng dụng quản lý, không dựa vào rowversion của SQL Server.
- Note được biểu diễn bằng event `NoteAdded` (ID/actor/At/Reason/version), không cần bảng AlertNote trùng dữ liệu. Evidence reference lưu JSON trên snapshot đóng, đồng thời bảo toàn trong event từng vòng.
- `SaveChanges` tạo transaction tự động cho nhiều câu SQL. Worker/lệnh người dùng cập nhật cùng Version; thua race trả 409 hoặc quét lại kỳ sau. SQLite busy trả 503 và Retry-After=1 cho command Alert.
- Backfill chạy ở startup sau migration, trước HTTP/worker. Chỉ non-ALLOW chưa có Alert; review false/true cũ đều không trở thành kết luận hiện hành. Mốc SLA là import, không thời điểm giao dịch lịch sử.
- Có Down của EF, nhưng **không dùng Down để rollback sau khi đã phát sinh điều tra** vì xóa bảng mới. Giữ schema/history khi rollback ứng dụng; restore backup phải đối soát dữ liệu sau backup.
- Đây là prototype một API instance. Hai connection bảo vệ nhận việc ở DB; chưa xác nhận triển khai nhiều instance đồng thời chạy migration/backfill hoặc đồng bộ feature history.

## Ánh xạ AC → nơi triển khai

Đường dẫn đầy đủ ở bảng trên. “Triển khai” không đồng nghĩa mọi tổ hợp UI/tải đã được QA nghiệm thu.

| AC | Nơi triển khai chính | Bằng chứng kiểm tra |
|---|---|---|
| AC-01.1 | FraudRepository.AddAsync; AlertPolicy.Create | CreationSeparatesEvaluationAndSlaClock, AllowNeverCreatesAlert; HTTP fallback |
| AC-01.2 | DetectionService.AnalyzeAsync (giữ nguyên); FraudRepository.AddAsync | DetectionReplayedOneHundredTimesCreatesOneAlertAndSnapshot |
| AC-01.3 | FraudRepository.AddAsync; EF transaction | EventFailureRollsBackTransactionEvaluationAndAlert |
| AC-02.1 | AlertWorkflowService.ExecuteAsync; AlertRepository.SaveAsync | ConcurrentClaimsOnSeparateConnectionsHaveOneWinner; HTTP race |
| AC-02.2 | ExecuteAsync/Replay; receipt PK | ReceiptReplaySurvivesNewerStateButRejectsDifferentActorOrPayload; HTTP replay |
| AC-02.3 | AlertsController.Actor; ExecuteAsync/ValidateFields; strict JSON | OwnerAndAdminCannotBypassWorkflow; HTTP actor/owner giả, 401/403 |
| AC-03.1 | ExecuteAsync wait/resume/start | WaitingKeepsOwnerAndDeadlineStaleCloseFails; WaitingDeadlineBoundsAreValidated |
| AC-03.2 | Allowed/ExecuteAsync | OwnerAndAdminCannotBypassWorkflow; chưa duyệt tự động toàn bộ tổ hợp state×command |
| AC-03.3 | Version check; AlertInspector.send | WaitingKeepsOwnerAndDeadlineStaleCloseFails; HTTP stale close |
| AC-04.1 | ExecuteAsync assign | AdminReassignmentAndReleasePreserveSla; HTTP tiếp nhận escalation |
| AC-04.2 | ExecuteAsync release | AdminReassignmentAndReleasePreserveSla |
| AC-04.3 | ExecuteAsync priority | PriorityUpgradeShortensButDowngradeNeverExtendsDeadline |
| AC-05.1 | AlertPolicy.Create; StatsAsync | CreationSeparatesEvaluationAndSlaClock; SlaBoundaryCountsOverdueOpenAndPreservesClosedUnknownSeparately |
| AC-05.2 | Workflow chỉ đổi deadline ở priority/reopen | WaitingKeepsOwnerAndDeadlineStaleCloseFails; AdminReassignmentAndReleasePreserveSla |
| AC-05.3 | Alert.ClosedWithinSla; StatsAsync | SlaBoundary… tại D và D+1 tick |
| AC-06.1 | DueAsync; EscalateDueAsync; AlertSlaWorker | WorkerEscalatesWaitingOnceRetainsOwnerAndRecoversAfterRestart; unique episode |
| AC-06.2 | Worker khôi phục từ DB; SaveAsync Version | ConcurrentWorkerAndCloseNeverCommitTheSameVersion; worker mới từ context mới |
| AC-06.3 | ExecuteAsync assign; EscalationEligibleAt | TakingEscalatedWorkStartsGraceButDoesNotEraseBreach |
| AC-07.1 | ExecuteAsync close | ClosurePairsAreExplicitAndNeverWriteLegacyBool; ClosureRejects… |
| AC-07.2 | close Unknown/Disputed; AlertStats; UI | C# bốn cặp kết luận; HTTP/UI đóng Unknown không review false |
| AC-07.3 | Allowed/Require/version/receipt | OwnerAndAdminCannotBypassWorkflow; HTTP owner/state/replay |
| AC-08.1 | notes → NoteAdded event; UI text escaping | NotesAreAppendOnlyAndAtomicWithAuditReceipt; browser script hiển thị text |
| AC-08.2 | ExecuteAsync reopen | ReopenClearsCurrentClosureAndPreservesPreviousCycleInHistory; HTTP reopen |
| AC-08.3 | SaveAsync audit/event/receipt; TimelineAsync | EventFailureDuringMutationRollsBackOwnerVersionAuditAndReceipt; HTTP audit 403 |
| AC-09.1 | AlertRepository.ListAsync | FiltersTotalsAndPaginationAreAppliedInDatabase; HTTP filters; UI tìm transaction |
| AC-09.2 | AlertQueue/AlertInspector effects, AbortController; App session | Browser refresh giữ draft, offline/reconnect, XSS; hủy request/đổi phiên dùng cơ chế hiện có; chưa UI E2E mọi tổ hợp hai tài khoản/response đảo thứ tự |
| AC-09.3 | StatsAsync; App badge/dashboard | SlaBoundary…; HTTP ClosedUnknown/openAlerts; legacy dashboard regression |
| AC-10.1 | EF migration; AlertBackfill.RunAsync | LegacyBackfillKeepsConflictingReviewsAndDoesNotResetOnRerun; ActualMigrationsUpgradeExistingDatabaseWithoutChangingLegacyRows |
| AC-10.2 | TransactionsController.Review; Alert UI | HTTP LEGACY_REVIEW_DISABLED; integration GET/CSV; bỏ submitReview |
| AC-10.3 | DetectionTests/ReviewRegressionTests; smoke | Toàn bộ .NET và integration/regression chạy pass |

## Unit test đã thêm

`AlertWorkflowTests.cs`: SQLite file tạm, không mock EF provider; hai connection cho race; TimeProvider điều khiển; trigger cố ý làm hỏng insert event để chứng minh rollback; test migration nâng từ schema cũ thật. Tổng solution sau bổ sung: **54 ca .NET**, gồm 21 ca có sẵn và 33 ca Alert (tính cả Theory).

## Kết quả build/lint/test

| Lệnh | Kết quả |
|---|---|
| `dotnet build FraudShield.Clean.slnx --no-restore -m:1 /nr:false -v:minimal` | Pass, 0 warnings/errors |
| `dotnet test FraudShield.Clean.slnx --no-build -m:1 /nr:false -v:minimal` | 54 pass, 0 fail/skip; lượt cuối 26 giây |
| `npm run build` trong Front-end | Pass TypeScript + Vite |
| `npx --no-install prettier --write …` các file UI đổi | Đã định dạng; không có script lint riêng trong package.json |
| `.venv/Scripts/python.exe -m pytest ai-service/tests -q` | 7 pass; 4 warnings môi trường/deprecation/cache, không sửa AI |
| `.venv/Scripts/python.exe scripts/integration_check.py` | Pass API+AI thật, parity C#/Python, mất AI và RulesOnly offline |
| `.venv/Scripts/python.exe scripts/regression_check.py` | Pass; dashboard 10.005 hàng, khóa giao dịch độc lập, replay, policy, forwarded IP |
| `.venv/Scripts/python.exe scripts/alert_check.py` | Pass HTTP, race Admin/Analyst, receipt, owner/JSON spoof, stale version, Unknown/evidence/reopen/escalation, filters/stats/legacy |
| `ALERT_UI_CHECK=1` + script trên | Edge headless desktop/mobile pass; login/claim/start/note/Unknown, draft khi refresh, text escaping; lượt cuối thêm offline/reconnect và không tràn ngang mobile |

Tất cả script dùng cổng riêng, DB tạm và dừng tiến trình mình tạo. API/AI public, dữ liệu demo, mô hình và artifacts huấn luyện không được thay thế. Log/ảnh cục bộ ở `test-results/alerts/`, `test-results/review/`, `test-results/integration-*.log` (gitignored). Không có quyết định QA độc lập hoặc kết quả tải sản xuất.

## Hướng dẫn chạy & kiểm thử thủ công cho Tester

Dùng database tạm/bản sao; không dùng database demo/public.

1. Build backend và frontend, chạy `scripts/alert_check.py`. Muốn UI test: đặt `ALERT_UI_CHECK=1`, có Playwright trong `CODEX_NODE_MODULES` (hoặc runtime Codex) và Edge. Script tự phục vụ dist qua proxy local, không dùng Vite/API demo đang chạy.
2. Mở queue: lọc New/Mine/Unassigned/Escalated, xem owner/SLA và căn cứ giao dịch gốc; hai tài khoản cùng claim phải chỉ một thắng.
3. Claim → Start → Waiting (lý do/hạn) → Resume; Admin giao lại. Analyst khác không sửa được.
4. Đóng InsufficientEvidence/Unknown và ConflictingEvidence/Disputed; thử Fraud/Legitimate không reference phải lỗi; đủ reference mới đóng. Không tăng review bool cũ.
5. Với clock trong test hoặc DB bản sao: quá hạn, chỉ một Escalated/episode, Admin tiếp nhận có grace 5 phút nhưng badge vẫn overdue; Close/worker race không lưu hai sự kiện cùng version.
6. Ngắt mạng trong dialog: stale và nút command bị khóa, draft còn; kết nối lại chỉ hết stale sau tải thành công. 409 không tự gửi quyết định. Mất response có thể retry đúng requestId/payload cũ.
7. Backfill bản sao có reviews true/false/mâu thuẫn: New/Unknown, không giả owner/Closed, chạy lại không reset SLA. Đối chiếu counts/hash dữ liệu gốc và lượng backlog trước deploy.

## Hạn chế / nợ kỹ thuật / vấn đề với đặc tả

Các giá trị SLA là mặc định đã chốt cho MVP. Polling chưa phải push; kết luận xử lý chưa phải ground truth.

- Chưa đo p95 với 100.000 Alert/12 clients; chưa cam kết worker ≤30s khi backlog lớn. Mỗi scan tối đa 500; log lỗi có nhưng chưa bổ sung metric exporter/alerting vận hành riêng.
- Chưa chạy ma trận đầy đủ mọi cạnh cấm, mọi role/state UI, tải cạnh tranh kéo dài, khôi phục backup có dữ liệu điều tra mới, hoặc nhiều API instance đồng thời backfill. Các invariant chính đã kiểm tra qua DB/HTTP.
- API review cũ bị chặn có chủ ý. Client dùng endpoint cũ cần chuyển sang Alert; không tự chuyển false sang Legitimate.
- Mọi non-ALLOW cũ sẽ tạo New/Unknown khi startup bản mới. Có thể tạo backlog lớn và đồng loạt quá SLA; cần định lượng từ bản sao trước deploy.
- Receipt/event giữ theo vòng đời hồ sơ; chưa có retention/purge. Snapshot JSON chỉ dùng để giữ lịch sử nghiệp vụ; UI trình bày thay đổi có nhãn thay vì JSON thô.
- Note nằm trong event thay cho bảng AlertNote riêng; không có upload hoặc xác minh chứng cứ tự động. UI hiện nhập một evidence reference/lần đóng; API hỗ trợ tối đa 10.
- Quy tắc lỗi 403 owner có ưu tiên trước 409 state/version cho lệnh của người không phụ trách. Ví dụ Close New bởi người chưa là owner có thể 403; các cạnh cấm của actor có quyền xử lý trả 409. Đây là thứ tự kiểm quyền cần thống nhất khi QA viết toàn bộ ma trận.

## Lịch sử sửa lỗi

Các lỗi phát hiện qua tự kiểm, không gán là kết quả subagent Tester/QA:

- DEV-01: EF không dịch được OrderBy trên DTO record constructor → projection anonymous trước sort/filter, map DTO ở Select cuối; test FiltersTotals… pass.
- DEV-02: label select thao tác chưa rõ với exact accessible selector → aria-label tường minh; Edge test claim/start/close pass.
- DEV-03: action group gây tràn ngang mobile → wrap và giới hạn dialog theo viewport; browser kiểm tra scrollWidth và ảnh mobile.
- DEV-04: thông báo lỗi mạng còn sau tải thành công → tách syncError với lỗi command, chỉ xóa lỗi đồng bộ khi tải thành công; không xóa cảnh báo kết quả lệnh chưa rõ.
- DEV-05: phần tử evidence null có thể gây lỗi 500 → kiểm tra null và từ chối 400, strict JSON cho cả evidence; bổ sung C#/HTTP regression.

## Kết quả deploy demo ngày 09/11/2026

- Người dùng yêu cầu push vào `quangdz05/CongNgheTaiChinh` và deploy. Commit ứng dụng: `3dde305`.
- Frontend build và backend publish Release thành công. SQLite backup `fraudshield-20261109-153714.db` có integrity_check=ok; snapshot/count/hash gốc lưu trong log gitignored.
- Đã áp migration AddAlertWorkflow, backfill 9 Alert ImportedLegacy/New/Unknown. Hash giữ nguyên toàn bộ 36 Transactions, 36 Evaluations, 0 Reviews, 2 Users, 1 Policy và 0 Audits tại lúc đối soát; FK/integrity đều đạt.
- Kiểm tra localhost:18080 và HTTPS tunnel: health, asset SHA-256 khớp build, login Admin/Analyst, queue/stats/detail, phân quyền audit, dashboard và AI Ready đều đạt. Chỉ đăng nhập và đọc dữ liệu, không smoke ghi giao dịch/claim vào DB public.
- Lần đổi tên thư mục app cũ bị khóa working directory trên Windows sau khi dừng API. Đã chạy bản mới trực tiếp từ thư mục phiên bản và ghi AppPath trong runtime; giữ nguyên DB, AI và tunnel. Script redeploy/start/frontend đã sửa để dùng AppPath và không đổi tên/xóa thư mục đang được dùng.
- Log kết quả tại `test-results/public-demo/alert-deployment-results.json`; backup và secrets không đưa vào Git. Link là Quick Tunnel, phụ thuộc máy và tiến trình đang chạy.
