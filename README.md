# FraudShield Clean Architecture

Ứng dụng học phần FinTech: ASP.NET Core 10, React TypeScript và Python AI API độc lập. Mã nguồn nằm trong thư mục mới, không sửa dự án Python cũ.

## Chạy nhanh trên Windows

Yêu cầu: .NET SDK 10, Node.js 20.19 trở lên hoặc Node 22, Python 3.12 cho AI. Giữa kỳ chỉ cần .NET và Node; AI không phải điều kiện để chạy Rules Only.

Mở CMD ở `D:\CongNgheTaiChinh\CongNgheTaiChinh`:

```cmd
dotnet restore FraudShield.Clean.slnx
cd Front-end
npm ci
cd ..
```

Chạy hai file trong hai cửa sổ riêng:

```cmd
scripts\start-api.cmd
scripts\start-web.cmd
```

Mở http://127.0.0.1:5173. Tài khoản Development được tạo lần đầu:

| Tài khoản | Mật khẩu | Quyền |
|---|---|---|
| admin | Admin@123456 | Xem, phân tích, kiểm tra, chỉnh luật, xem audit |
| analyst | Analyst@123456 | Xem, phân tích và kiểm tra; không sửa luật |

Backend: http://127.0.0.1:5080/health. Web gọi `/api` qua Vite proxy; không cần cấu hình CORS hoặc Supabase. SQLite được tự khởi tạo/migrate dưới thư mục chạy API `data/fraudshield.db`. Đây là database mới, không dùng lại database Python.

## Chạy AI cho cuối kỳ

Nếu chưa có môi trường Python:

```cmd
py -3.12 -m venv .venv
.venv\Scripts\python.exe -m pip install -r ai-service\requirements.txt
```

Huấn luyện offline, rồi chạy AI:

```cmd
scripts\train-ai.cmd
scripts\start-ai.cmd
```

AI chạy http://127.0.0.1:8001. Sau khi huấn luyện lại phải khởi động lại AI API. Trên web chọn **Hybrid + AI**, vào **Mô hình AI** để xem metrics thật từ artifact và so sánh trên cùng đặc trưng. Không có bước huấn luyện trong request giao dịch.

Dữ liệu mặc định là **6.000 giao dịch mô phỏng**, seed 42. Không dùng số liệu này để khẳng định hiệu quả ở ngân hàng hoặc thị trường thật. Muốn train dữ liệu có nhãn riêng:

```cmd
.venv\Scripts\python.exe ai-service\train.py --csv path\to\transactions.csv
```

CSV cần các cột `userId,amount,currency,merchant,category,country,deviceId,ipAddress,timestamp,label`. Chỉ VND; timestamp ISO 8601 có múi giờ; label 0/1 được xác minh độc lập. Ví dụ dữ liệu sinh ra tại `ai-service/data/training_transactions.csv`. Không map các cột có ý nghĩa khác thành feature cùng tên. Pipeline tính lịch sử trước giao dịch, chia 60/20/20 theo thời gian, không tách một timestamp sang nhiều tập; chọn ngưỡng trên validation và báo cáo test độc lập.

## Cấu trúc theo tài liệu tham khảo

```text
Back-end/
  src/Services/FraudDetectionService/
    FraudDetectionService.Domain/           Entities, ValueObjects, Repositories
    FraudDetectionService.Application/      Features, Commands, Queries, DTOs, Validators
    FraudDetectionService.Infrastructure/   Persistence, Configurations, Migrations, Repositories, Services
    FraudDetectionService.Api/              Controllers, composition root, authentication
  src/Shared/Shared.Security/Permissions/
  tests/FraudDetectionService.Tests/
Front-end/src/
  features/{auth,transactions,rules,ai}/
    _components/  _services/  _schema/
  components/  shared/services/  App.tsx
ai-service/                                  Offline train + inference
contracts/                                   Golden feature schema fixture
docs/                                        Kiến trúc, API, demo, giới hạn
scripts/                                     Lệnh khởi động và kiểm tra
```

Xem [bản đồ định vị file](docs/ARCHITECTURE_MAP.md) trước khi tìm hoặc sửa tính năng. Giữ .NET 10 và React Vite như phương án đã chọn; áp dụng cách chia lớp và feature của tài liệu BTC-QLTCDT. Không sao chép các nghiệp vụ kế toán, bảng master, SSO hoặc Kafka không thuộc bài toán. CQRS dùng command/query handler tường minh qua DI, không phụ thuộc MediatR. Ánh xạ DTO dùng mã tường minh thay AutoMapper.

## Kiểm tra

```cmd
dotnet test FraudShield.Clean.slnx
.venv\Scripts\python.exe -m pytest ai-service\tests -q
cd Front-end
npm run build
cd ..
.venv\Scripts\python.exe scripts\smoke_test.py
.venv\Scripts\python.exe scripts\regression_check.py
```

Smoke test cần backend và AI đang chạy, tạo dữ liệu giả có mã riêng, ghi một đánh giá và tăng phiên bản luật một lần. Không chạy trên database thật. Test bao gồm quyền Analyst, key/token sai, idempotency, xung đột cấu hình, AI thật qua HTTP và đối chiếu đặc trưng C#/Python.

Để tự khởi động hai dịch vụ kiểm tra ở cổng riêng, dùng database tạm và tự dừng sau kiểm tra: ` .venv\Scripts\python.exe scripts\integration_check.py `. Cần build Debug và huấn luyện trước. Script còn dừng AI để kiểm tra fallback HTTP thật. Quy trình CI trong `.github/workflows/ci.yml` thực hiện build, test, train mô phỏng và tích hợp; chưa được chạy trên GitHub từ phiên làm việc này.

## Cấu hình

Không cần chuỗi nối DB server để demo. `appsettings.json` cấu hình SQLite và URL AI. Biến môi trường ghi đè theo cú pháp .NET:

```cmd
set "ConnectionStrings__FraudShield=Data Source=data/fraudshield.db"
set "Ai__BaseUrl=http://127.0.0.1:8001/"
set "Ai__ApiKey=local-ai-service-key"
set "Ai__TimeoutSeconds=3"
```

Chế độ `RulesOnly` hoàn toàn không gọi AI predict. `Hybrid` gọi dịch vụ và giữ luật nghiêm trọng/luật đạt ngưỡng BLOCK. Nếu AI không khả dụng, giữ quyết định luật và nâng ALLOW thành REVIEW; `MlScore=null`, trạng thái lỗi được hiển thị. Điểm số không phải xác suất gian lận đã hiệu chuẩn.

## Docker demo cục bộ

```cmd
docker compose --profile training run --rm train
docker compose up --build
```

Mở http://localhost:8080. Cấu hình compose dành cho **demo Development trên loopback**, có tài khoản mẫu; không dùng nguyên cấu hình này làm public deployment. AI model lưu trong volume riêng. Docker cần được kiểm chứng trên máy có Docker Engine; xem giới hạn kiểm chứng trong `docs/VERIFICATION.md`.

Để người khác truy cập qua tên miền và HTTPS, dùng cấu hình riêng `compose.public.yaml` theo [hướng dẫn triển khai công khai](docs/DEPLOY_PUBLIC.md). Cấu hình này cần máy chủ Linux, tên miền và các bí mật riêng trong `.env`; không dùng mật khẩu Development.

Nếu chỉ cần link tạm cho buổi demo mà chưa có VPS/tên miền, xem [hướng dẫn chia sẻ tạm thời](docs/SHARE_TEMPORARY.md).

## Phạm vi và giới hạn

Prototype một workspace, hai vai trò Admin/Analyst; chưa hỗ trợ cô lập nhiều tổ chức. Các ngưỡng và nhóm merchant là giả định demo. HMAC thiết bị/IP nhất quán trước khi lưu và tạo lịch sử. Token một giờ nằm trong sessionStorage; không có cơ chế thu hồi/refresh trong phiên bản này. Triển khai thật cần HTTPS, quản lý khóa, chính sách lưu dữ liệu, sao lưu, phân quyền tổ chức và đánh giá rủi ro dữ liệu. Production yêu cầu `Security__JwtSecret`, `Security__PiiSalt` ít nhất 32 ký tự, `Security__AdminPassword` từ 12 ký tự khi khởi tạo và `Ai__ApiKey`; AI đặt `APP_ENV=production` và `AI_SERVICE_KEY` tương ứng.

Mỗi giao dịch lưu snapshot cấu hình luật, feature schema v1, điểm, chế độ và phiên bản mô hình. Giao dịch là bất biến; giao diện giữ ID khi thử lại cùng nội dung, gửi lại cùng ID/nội dung trả kết quả cũ, khác nội dung trả 409. Alert được tạo riêng cho REVIEW/BLOCK và lưu lịch sử điều tra; BLOCK không được tự coi là nhãn thật. Độ trễ hiển thị gồm chờ xử lý, tính điểm và HTTP AI, chưa bao gồm commit DB hay thời gian mạng frontend. Giao dịch dùng chung người dùng, thiết bị, IP hoặc ID được đồng bộ trong một API instance; các giao dịch độc lập có thể xử lý song song. Chạy nhiều API instance cần cơ chế đồng bộ ở cấp cơ sở dữ liệu hoặc hệ thống phân tán cho luồng phân tích giao dịch; nhận việc Alert đã có kiểm tra version ở DB.

## Quản lý cảnh báo (09/11/2026)

Alert có vòng đời `New → Assigned → InProgress → Waiting / Escalated / Closed`, owner, ưu tiên P1/P2/P3 và hạn đóng 15/60/240 phút chạy 24/7. Waiting không dừng SLA. Admin điều phối/chuyển cấp/mở lại; Analyst nhận việc và xử lý việc mình. Đóng phải có căn cứ; `Unknown` và `Disputed` được lưu riêng, không tự coi là hợp lệ hoặc đưa vào huấn luyện AI. Queue cập nhật định kỳ 10 giây, hiển thị trạng thái dữ liệu cũ khi mất mạng/quá 30 giây chưa đồng bộ.

API mới: `GET /api/alerts`, `/api/alerts/stats`, `/api/alerts/{id}`, `/api/alerts/{id}/timeline`; các lệnh `POST /api/alerts/{id}/{command}` cần `requestId` và `expectedVersion`. `POST /api/transactions/{id}/reviews` trả 409 `LEGACY_REVIEW_DISABLED`; GET giao dịch/review cũ vẫn giữ. Khi chạy bản mới, migration thêm bảng Alert và startup backfill mọi non-ALLOW thành New/Unknown, giữ review cũ và bắt đầu SLA tại import. Đã deploy lên demo tạm ngày 09/11/2026 sau sao lưu; giữ 36 giao dịch và tạo 9 Alert. Mỗi môi trường mới vẫn cần sao lưu/đối soát backlog trước triển khai.

Chi tiết: [đặc tả](docs/team/01-requirements.md), [triển khai và kiểm chứng](docs/team/02-implementation.md). Kiểm tra HTTP với database tạm: `.venv\Scripts\python.exe scripts\alert_check.py`. Kiểm tra UI tùy chọn: đặt `ALERT_UI_CHECK=1` rồi chạy script trên; cần Playwright và Edge, không cài thêm thư viện vào ứng dụng.

Nguồn ý tưởng và các tình huống luật được tham khảo từ dự án FraudShield Python trong thư mục bên cạnh; bản .NET/React được triển khai mới. Xem `THIRD_PARTY_NOTICES.md`.
