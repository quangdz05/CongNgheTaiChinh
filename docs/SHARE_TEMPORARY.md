# Chia sẻ bản demo bằng link tạm

Trên máy Windows đã được chuẩn bị trong phiên này, FraudShield chạy ở chế độ Production với database riêng tại `test-results/public-demo/fraudshield.db`. Frontend đã được đóng gói cùng API, còn AI chạy cục bộ. Cloudflare Quick Tunnel tạo link HTTPS `trycloudflare.com` để người khác dùng trên trình duyệt. Link chỉ còn hoạt động khi máy tính, Internet và ba tiến trình demo còn chạy; mỗi lần tạo lại tunnel sẽ có link mới.

## Mở lại link sau khi khởi động máy

Mở Command Prompt ở thư mục gốc dự án và chạy:

```cmd
scripts\start-share-demo.cmd
```

Script dùng bản đã đóng gói trong `test-results/public-demo/app`, mô hình tại `ai-service/artifacts/model.joblib`, môi trường Python `.venv` và `cloudflared.exe` trong `.tools`. Nó giữ database và mật khẩu cũ, rồi in link HTTPS mới. Nếu đã sửa mã nguồn sau lần đóng gói, cần build và publish lại trước khi chạy script.

Sau mỗi lần chỉ sửa frontend trong `Front-end/src`, chạy `scripts\redeploy-share-fe.cmd` từ thư mục gốc. Script build React và chép các tệp mới vào API đang chạy, chép `index.html` sau cùng. Link, API, AI và SQLite giữ nguyên; người đang mở trang nhấn `Ctrl+F5` để tải giao diện mới.

Khi sửa cả backend, chạy `scripts\redeploy-share.cmd` lúc bản demo đang chạy. Script build/publish vào thư mục tạm, sao lưu SQLite bằng backup API và kiểm tra bản sao trước khi dừng API. Nó giữ AI và tunnel, thay thư mục ứng dụng rồi khởi động lại API trên cùng cổng, nên giữ được link hiện tại. Bản cũ nằm trong `test-results/public-demo/app-previous-*`; bản sao DB nằm trong `backups/`. Nếu API mới không khởi động được, script khôi phục thư mục ứng dụng cũ. Trong thời gian API khởi động lại, người xem có thể gặp lỗi kết nối ngắn hạn.

Để ngắt link và dừng API, AI:

```cmd
scripts\stop-share-demo.cmd
```

Chỉ chia sẻ tài khoản `analyst` và mật khẩu tương ứng trong `test-results/public-demo/secrets.json` qua kênh riêng. Giữ tài khoản `admin` cho người trình bày. Không chia sẻ file `secrets.json`; file này chứa cả khóa ký token và khóa mã hóa định danh thiết bị/IP. Link tạm và tài khoản chung chỉ phù hợp cho demo với dữ liệu mô phỏng.

## Dữ liệu và giới hạn

Database của link tạm tách biệt với database Windows tại `Back-end/.../FraudDetectionService.Api/data/fraudshield.db` và với Docker volume. Bản demo đã có 36 giao dịch mô phỏng để người xem thấy dashboard. Người đăng nhập có thể tạo thêm dữ liệu và ghi review; mọi thay đổi đều được lưu vào cùng database demo. Không có tài khoản riêng cho từng người.

Quick Tunnel không cần VPS, tên miền hoặc tài khoản Cloudflare. Bất kỳ ai có link và thông tin đăng nhập hợp lệ đều có thể dùng ứng dụng. Nếu cần giới hạn tới email người xem, Cloudflare hỗ trợ `--allowed-mail`, nhưng phải tạo lại tunnel với danh sách email. Quick Tunnel dành cho thử nghiệm, không có bảo đảm thời gian hoạt động và hostname sẽ đổi khi tạo lại. Muốn website chạy liên tục với địa chỉ ổn định, xem `docs/DEPLOY_PUBLIC.md`.
