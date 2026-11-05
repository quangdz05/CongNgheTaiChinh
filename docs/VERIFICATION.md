# Kiểm chứng phiên bản cục bộ

Ngày kiểm tra: 30/10/2026. Môi trường Windows, .NET SDK 10.0.400, Node 20.19, Python 3.12. Phiên bản thư viện frontend nằm trong package-lock.json; Python được ghi trong requirements.lock.txt. Mã mới nằm riêng, không thay backend Python cũ.

## Đã kiểm chứng

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
- SQLite phù hợp prototype một instance. Semaphore chỉ tuần tự hóa trong một tiến trình; không phải khóa phân tán.
- Trang mạng liên kết là tín hiệu điều tra, không phải bằng chứng xác nhận một đường dây gian lận.

Các giới hạn này không được thay thế bằng số liệu giả hoặc tuyên bố sản phẩm thương mại đã hoàn thiện.
