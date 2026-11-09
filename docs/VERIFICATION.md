# Kiểm chứng phiên bản cục bộ

Ngày kiểm tra: 30/10/2026. Môi trường Windows, .NET SDK 10.0.400, Node 20.19, Python 3.12. Phiên bản thư viện frontend nằm trong package-lock.json; Python được ghi trong requirements.lock.txt. Mã mới nằm riêng, không thay backend Python cũ.

## Cập nhật ngày 09/11/2026 — Alert riêng

- Người dùng chốt mặc định Q1–Q8 trong `docs/team/01-requirements.md`. Triển khai entity Alert riêng, sáu trạng thái, owner/version tại DB, ưu tiên/SLA, chuyển cấp, đóng Unknown/Disputed có lý do, evidence cho Fraud/Legitimate, timeline và mở lại.
- Build backend 0 warnings/errors; 54 ca .NET và 7 ca Python pass. Frontend TypeScript/Vite build và kiểm tra định dạng pass.
- Integration API–AI thật, outage fallback, C#/Python parity và script hồi quy cũ pass. Smoke cập nhật dùng Alert đóng Unknown; không tiếp tục ghi review boolean.
- HTTP Alert dùng API/SQLite tạm: claim race hai tài khoản một thắng, replay receipt, fake actor/owner bị từ chối, stale version, Waiting/Unknown/evidence/reopen, filter/stats/legacy 409 pass.
- Edge headless trên dist và proxy local: login/claim/start/note/close Unknown, refresh giữ draft, escape script, offline/reconnect, desktop/mobile pass; ảnh/log cục bộ trong `test-results/alerts/`.
- Test migration nâng schema cũ và backfill rerun không reset SLA, giữ review mâu thuẫn; test trigger gây lỗi chứng minh rollback transaction/evaluation/Alert hoặc owner/event/receipt/audit.
- Đã deploy lên demo public sau yêu cầu tiếp theo của người dùng: Release build, backup SQLite integrity=ok, migration và 9 Alert backfill. Hash dữ liệu gốc giữ nguyên 36 Transactions/36 Evaluations/2 Users/1 Policy và Reviews/Audits cũ. Localhost và HTTPS kiểm tra health/asset/login hai vai trò/Alert/audit permission/AI Ready đều đạt; không smoke ghi dữ liệu public.
- Windows khóa thư mục app khi đổi tên: đã khôi phục bằng chạy thư mục phiên bản mới, giữ tunnel/AI/DB; runtime ghi AppPath và script khởi động/redeploy dùng đường dẫn này. Kết quả cục bộ ở `test-results/public-demo/alert-deployment-results.json`.
- Chưa đo mục tiêu p95 100.000 Alert hoặc SLA worker với backlog lớn; không có kết luận QA độc lập. Chi tiết truy vết và giới hạn ở `docs/team/02-implementation.md`.

## Cập nhật ngày 06/11/2026

Phần này là lịch sử phiên bản 06/11; thay đổi Alert ngày 09/11 chưa được deploy lên link public đó.

- Sửa và kiểm chứng các vấn đề review: xóa trạng thái theo phiên, hủy request cũ, ID ổn định khi thử lại, forwarded headers, so sánh chính sách, truy vấn lịch sử, danh sách gọn, thống kê toàn bộ và khóa theo tài nguyên liên quan.
- 21 test .NET và 7 test Python đã qua; build Debug và publish Release thành công; frontend build thành công.
- Test truy vấn xác nhận trang 12 giao dịch dùng hai lệnh đọc SQL. Test thống kê bao gồm 10.001 giao dịch và 10.001 review, kiểm tra pending và nhãn cuối cùng ngoài mẫu biểu đồ.
- Kiểm tra HTTP độc lập bằng database tạm đã qua: chính sách mới, replay cùng ID, giao dịch độc lập khi AI chậm, tổng dashboard vượt 10.000 dòng và hạn mức từng IP chuyển tiếp. Kết quả cục bộ: `test-results/review/regression-results.json`.
- Tích hợp với AI thật và fallback khi dừng AI đã qua. Script kiểm tra chọn cổng trống để không ảnh hưởng demo đang chạy.
- Trình duyệt: Admin tạo audit, đăng xuất rồi Analyst đăng nhập trên cùng tab không thấy audit/navigation Admin; tạo demo, tìm kiếm nhanh, mở chi tiết và tạo giao dịch hoạt động.
- Redeploy bản Production tại link tunnel hiện có; sao lưu SQLite và kiểm tra integrity trước khi cập nhật. Đối chiếu sau deploy giữ 36 giao dịch, 36 evaluation, hai tài khoản và cấu hình luật. Kiểm tra frontend asset, health, đăng nhập hai vai trò, quyền audit, dashboard và AI Ready qua localhost lẫn HTTPS công khai. Kết quả cục bộ: `test-results/review/deployment-results.json`.
- Chưa kiểm chứng Docker Engine hoặc tải sản xuất. Khóa theo tài nguyên chỉ bảo vệ một API instance; nhiều instance cần cơ chế đồng bộ phù hợp.

## Đã kiểm chứng ngày 30/10/2026

- Build solution .NET thành công, không cảnh báo/lỗi.
- 12 ca .NET kiểm tra ngưỡng tiền, cold start, lịch sử quá hạn/tương lai, RulesOnly không gọi AI, fallback khi AI lỗi, ghi đè luật nghiêm trọng, replay idempotent, xung đột phiên bản chính sách và HMAC.
- 7 ca Python kiểm tra feature contract, timestamp đồng thời, baseline nghiêm trọng, key sai, schema sai, feature thừa và suy luận mô hình đã huấn luyện.
- Build React/TypeScript strict thành công. npm audit không phát hiện lỗ hổng tại thời điểm cài phiên bản khóa.
- Migration SQLite khởi tạo và khởi động lại không áp lại schema.
- Smoke test HTTP: đăng nhập và quyền Analyst, validation, RulesOnly, Hybrid qua AI API thật, critical override, idempotency, optimistic concurrency, review, CSV, so sánh cùng feature và đối chiếu đặc trưng C#/Python.
- Script integration dùng database tạm đã chạy thành công, tự khởi động/dừng dịch vụ và kiểm chứng HTTP thật khi AI ngừng phục vụ: Hybrid chuyển REVIEW với ML null; RulesOnly vẫn hoạt động.
- Trình duyệt: đăng nhập, dashboard, tạo bộ demo 18 giao dịch, mở chi tiết luật thiết bị dùng chung và xem metrics/confusion matrix từ AI API.

Ảnh kiểm chứng trong `screenshots/`. Dữ liệu chạy thử trong SQLite là dữ liệu giả có tiền tố SMOKE hoặc DEMO. Smoke test cập nhật phiên bản luật và thêm nhận xét giả; không chạy trên database thật.

## Mô hình mẫu

Artifact được huấn luyện bằng HistGradientBoostingClassifier trên 6.000 giao dịch mô phỏng, seed 42. Chia theo thời gian: train 3.600, validation 1.200, test 1.200. Ngưỡng ML 0,675 được chọn bằng validation. Metrics test mẫu: Precision 0,6569; Recall 0,7283; F1 0,6907; PR-AUC 0,5560; ROC-AUC 0,8780. Confusion matrix: TN 1073, FP 35, FN 25, TP 67. Xem toàn bộ kết quả, tham số và SHA-256 dữ liệu trong `MODEL_EVALUATION.json`.

Đây là số liệu trên dữ liệu mô phỏng do pipeline tạo, không phải chất lượng đã được xác nhận trên dữ liệu tài chính thật. ML threshold không đồng nhất với ngưỡng quyết định Hybrid trong C#. Không sử dụng test để chọn lại ngưỡng.

## Chưa kiểm chứng hoặc ngoài phạm vi

- Dockerfile và compose đã được chuẩn bị, nhưng Docker Engine chưa chạy trên máy nên chưa build/chạy container.
- Chưa có dữ liệu giao dịch thực có quyền sử dụng và nhãn đã xác minh.
- Chưa kiểm tra tải sản xuất, nhiều instance backend, nhiều tổ chức, SSO, refresh/revoke token, vận hành liên tục, sao lưu và khôi phục.
- SQLite phù hợp prototype một instance. Khóa theo người dùng, thiết bị, IP và ID chỉ đồng bộ trong một tiến trình; không phải khóa phân tán.
- Trang mạng liên kết là tín hiệu điều tra, không phải bằng chứng xác nhận một đường dây gian lận.

Các giới hạn này không được thay thế bằng số liệu giả hoặc tuyên bố sản phẩm thương mại đã hoàn thiện.
