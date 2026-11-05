# Kịch bản trình diễn

## Giữa kỳ

1. Khởi động API và React; không cần AI. Đăng nhập admin.
2. Giữ Rules Only. Nêu rõ dữ liệu mô phỏng, tiền VND, giờ luật UTC.
3. Bấm tạo bộ demo 18 giao dịch. Mỗi lần có mã đợt riêng; dữ liệu không bị xóa.
4. Mở NORMAL, HIGH, CRITICAL: giải thích ALLOW, REVIEW, BLOCK dựa luật.
5. Tìm VELOCITY: giao dịch thứ 11 có 10 giao dịch trước đó trong một giờ. Dữ liệu lúc 12 UTC để tránh kích hoạt luật đêm.
6. Mở nhóm RING: giao dịch thứ tư trên thiết bị có 3 người trong lịch sử kích hoạt luật thiết bị.
7. Xem chi tiết và ghi nhận false positive có căn cứ; cho thấy review được lưu tách điểm.
8. Đăng nhập analyst để chứng minh không sửa được luật. Nhóm admin chỉnh ngưỡng tạo phiên bản mới, kết quả cũ giữ snapshot.

## Cuối kỳ

1. Huấn luyện bằng script; đọc `ai-service/artifacts/metrics.json`. Nêu nguồn synthetic và giới hạn.
2. Chạy AI API, chọn Hybrid. Màn hình model hiển thị phiên bản, split và confusion matrix test.
3. Chọn giao dịch có sẵn và so sánh bằng cùng feature snapshot. Việc này không thêm giao dịch, dùng cấu hình hiện tại.
4. Gửi Hybrid và chỉ rõ điểm luật/ML/cuối. Luật mạnh và critical có quyền ghi đè BLOCK theo chính sách.
5. Dừng AI bằng Ctrl+C; gửi giao dịch mới Hybrid. Giao diện ghi UnavailableReviewFallback; ML null; giao dịch ít rủi ro chuyển REVIEW thay vì âm thầm cho qua.
6. Chọn Rules Only để chứng minh backend vẫn hoạt động khi AI tắt.

Không diễn giải score thành xác suất đã hiệu chuẩn hoặc coi mọi BLOCK là gian lận xác nhận. Metrics mô hình dùng ngưỡng chọn trên validation; decision Hybrid có ngưỡng riêng. Độ trễ thống kê không phải độ trễ đầu cuối.
