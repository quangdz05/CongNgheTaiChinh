# Triển khai FraudShield để người khác truy cập

Phương án này chạy bản demo trên một máy chủ Linux có Docker Compose và một tên miền. Người dùng mở `https://<tên-miền>`, đăng nhập rồi sử dụng giao diện giống khi chạy trên Windows. Toàn bộ người dùng cùng xem và cập nhật **một** database; ứng dụng hiện chưa có đăng ký tài khoản hay tách dữ liệu theo từng người hoặc tổ chức.

## Chuẩn bị

1. Có máy chủ Linux với Docker Engine và Docker Compose plugin. Đưa toàn bộ mã nguồn dự án lên máy chủ. Không chép `.venv`, `node_modules`, `bin`, `obj` hoặc database demo vào image.
2. Có tên miền trỏ bản ghi DNS A/AAAA về IP công khai của máy chủ. Mở TCP 80 và 443 ở firewall. Caddy cần hai cổng này để cấp và gia hạn HTTPS tự động.
3. Trong thư mục gốc dự án trên máy chủ, tạo `.env` từ `deploy/public-env.example`. Thay tất cả giá trị `CHANGE_ME`. Dùng chuỗi ngẫu nhiên độc lập, dài ít nhất 32 ký tự cho `JWT_SECRET`, `PII_SALT` và `AI_SERVICE_KEY`; dùng mật khẩu riêng, dài ít nhất 12 ký tự cho `ADMIN_PASSWORD` và `ANALYST_PASSWORD`. `SITE_DOMAIN` chỉ chứa tên miền, ví dụ `fraud.example.com`, không thêm `https://`.

Ví dụ tạo từng chuỗi bí mật trên máy chủ có OpenSSL: `openssl rand -hex 32`. Không commit, gửi ảnh hoặc chia sẻ file `.env`. Giữ `PII_SALT` ổn định khi redeploy vì đổi salt sẽ làm các mã thiết bị/IP mới không khớp lịch sử cũ. Đổi `JWT_SECRET` sẽ làm token đăng nhập hiện tại hết hiệu lực.

## Triển khai lần đầu

Chạy trong thư mục gốc dự án:

```sh
cp deploy/public-env.example .env
# Sửa .env và thay toàn bộ giá trị mẫu trước khi tiếp tục.
docker compose -f compose.public.yaml config --quiet
docker compose -f compose.public.yaml --profile training run --rm train
docker compose -f compose.public.yaml up --build -d
docker compose -f compose.public.yaml ps
```

Lệnh `train` tạo mô hình từ 6.000 giao dịch **mô phỏng** vào volume `models`. Không dùng số liệu này để khẳng định hiệu quả trên giao dịch thật. Nếu AI báo chưa sẵn sàng, xem `docker compose -f compose.public.yaml logs ai`; sau khi huấn luyện lại, chạy `docker compose -f compose.public.yaml restart ai` để nạp mô hình mới.

Kiểm tra `https://<tên-miền>/health` trả trạng thái `healthy`, rồi mở trang chủ và đăng nhập. Database mới được tạo tự động ở lần chạy API đầu tiên. Tài khoản `admin` và `analyst` được tạo với mật khẩu trong `.env`; nên chia sẻ **chỉ** mật khẩu `analyst` cho người xem demo. Analyst có thể thêm giao dịch, ghi nhận kiểm tra và chạy bộ dữ liệu demo; Admin còn được chỉnh luật và xem audit. Không có chức năng đăng ký hoặc cấp tài khoản cá nhân trong bản này.

Nếu trình duyệt chưa vào được, kiểm tra DNS, firewall 80/443 và log Caddy:

```sh
docker compose -f compose.public.yaml logs --tail=100 caddy web api ai
```

## Dữ liệu được lưu ở đâu

| Dữ liệu | Vị trí trong container | Lưu bền qua lần khởi động lại |
|---|---|---|
| Giao dịch, kết quả, review, luật, người dùng, audit | `api:/app/data/fraudshield.db` | Docker volume `database` |
| Mô hình và metrics AI | `ai:/app/artifacts` | Docker volume `models` |
| Chứng chỉ HTTPS | `caddy:/data` | Docker volume `caddy_data` |
| Token đăng nhập trên trình duyệt | `sessionStorage` | Chỉ tồn tại trong phiên trình duyệt |

Database SQLite trên Windows và Docker volume là hai bộ dữ liệu riêng. Muốn đưa dữ liệu demo cũ từ Windows lên máy chủ phải xuất/nhập database khi API đã dừng; không tự đồng bộ. Tệp CSV huấn luyện mô phỏng do container `train` sinh trong `/app/data` không được gắn volume, nên không lưu lại sau `run --rm`; mô hình và metrics thì được lưu trong `models`.

`docker compose -f compose.public.yaml down` dừng và xóa container nhưng giữ các volume. **Không dùng `down -v` nếu muốn giữ dữ liệu.** Trước khi cập nhật phiên bản hoặc thay máy chủ, sao lưu volume `database`, `models` và `caddy_data`; dừng API trước khi sao chép SQLite để tránh bản sao thiếu dữ liệu đang nằm trong WAL. Thử khôi phục từ bản sao lưu trên môi trường riêng trước khi cần dùng thật.

## Cập nhật phiên bản

Sau khi đưa mã nguồn mới lên máy chủ:

```sh
docker compose -f compose.public.yaml up --build -d
docker compose -f compose.public.yaml ps
docker compose -f compose.public.yaml logs --tail=100 api ai web caddy
```

API tự chạy migration SQLite khi khởi động. Hãy sao lưu database trước khi cập nhật mã có migration mới. Việc thay `ADMIN_PASSWORD` hoặc `ANALYST_PASSWORD` trong `.env` **không đổi mật khẩu của tài khoản đã được tạo** trong database; các biến này chỉ dùng khi database chưa có người dùng.

Compose cấu hình mạng riêng `172.30.0.0/24` và API chỉ tin forwarded headers từ mạng này cùng loopback. Nginx giữ chuỗi IP qua Caddy; API đọc tối đa hai proxy trước khi áp dụng rate limit. Nếu subnet trùng mạng hiện có, đặt `FRAUDSHIELD_SUBNET` thành một subnet riêng khác trong `.env`; cùng giá trị được dùng cho mạng Docker và danh sách proxy tin cậy. Không cấu hình tin mọi địa chỉ proxy.

## Giới hạn của bản demo công khai

Đây là một workspace chung và một API instance dùng SQLite. Nhiều người có thể mở cùng lúc, nhưng mọi giao dịch và review cùng xuất hiện cho tất cả tài khoản có quyền; không có quyền sở hữu dữ liệu theo người dùng. Không đưa dữ liệu giao dịch hoặc thông tin cá nhân thật lên bản demo. Muốn triển khai cho khách hàng thật cần cơ chế quản lý tài khoản, cách ly dữ liệu, sao lưu và phục hồi tự động, giám sát, kiểm thử tải và đánh giá bảo mật riêng.
