# FraudShield — hướng dẫn cho Codex

Ứng dụng phát hiện gian lận giao dịch (học phần FinTech): ASP.NET Core 10 (Clean Architecture, CQRS thủ công) + React 18/TypeScript/Vite + Python AI service (FastAPI, HistGradientBoosting). Chi tiết: `README.md`. Định vị file theo tính năng: `docs/ARCHITECTURE_MAP.md` — đọc trước khi tìm/sửa code.

Trong repo này Codex thường nhận từng bước việc do Claude lập plan. Chỉ làm đúng phạm vi bước được giao; nếu thấy cần làm khác plan thì dừng lại và báo, không tự mở rộng.

## Lệnh build / test (Windows, chạy từ thư mục gốc)
```cmd
dotnet build FraudShield.Clean.slnx
dotnet test FraudShield.Clean.slnx
.venv\Scripts\python.exe -m pytest ai-service\tests -q
cd Front-end && npm run build && cd ..
```
Sau khi sửa, chạy lệnh build/test của phần mình đã đụng tới và báo kết quả. Không chạy `scripts\integration_check.py`, `scripts\regression_check.py` hay `scripts\start-*.cmd` (cần mở cổng mạng; người điều phối sẽ chạy). Không chạy `ai-service\train.py`.

## Kiến trúc & quy ước bắt buộc
- Backend gốc `Back-end/src/Services/FraudDetectionService/`: Domain → Application → Infrastructure → Api.
  - Domain không tham chiếu layer khác; không gọi EF/HttpClient từ Domain.
  - Application: command/query handler tường minh qua DI — **không dùng MediatR, không dùng AutoMapper** (map DTO bằng code). Validate bằng FluentValidation.
  - Infrastructure: EF Core + SQLite, migration trong `Persistence/Migrations/`, adapter AI/HMAC.
  - Quyền: `Back-end/src/Shared/Shared.Security/Permissions/FraudPermissions.cs` (Admin, Analyst).
- `Directory.Build.props`: `Nullable` bật, `TreatWarningsAsErrors=true` → không được để warning.
- Frontend `Front-end/src/features/{auth,transactions,rules,ai,alerts}/{_components,_services,_schema}`; HTTP client chung `Front-end/src/shared/services/api.ts`. Chỉ React state — không thêm Zustand/SWR/Yup hay thư viện mới.
- AI: `ai-service/features.py` (feature v1), `train.py` (offline), `app.py` (chỉ suy luận). Feature C# và Python phải khớp fixture `contracts/feature-v1-fixture.json`.
- Format theo `.editorconfig`; frontend có prettier.
- Không thêm package NuGet/npm/pip mới khi bước không yêu cầu rõ.

## File dùng chung — hạn chế sửa
Ưu tiên sửa trong file riêng của tính năng (handler, `_components`, `_services`, `_schema` của tính năng đó). Chỉ sửa các file sau khi bước được giao nêu rõ; nếu thấy cần sửa mà bước không cho phép thì dừng lại và báo:
- `Directory.Build.props`, `.editorconfig`, `FraudShield.Clean.slnx`, `*.csproj`, `package.json`, `requirements*.txt`
- `Back-end/src/Shared/**`
- `FraudDetectionService.Api/Program.cs`, `FraudDbContext` và `Persistence/Migrations/**` (migration mới chỉ tạo khi bước yêu cầu)
- `Front-end/src/shared/**`, `Front-end/src/App.tsx`
- `contracts/feature-v1-fixture.json`, `ai-service/features.py`
- `.github/workflows/**`

## Quy tắc nghiệp vụ không được phá vỡ
- Giao dịch bất biến; cùng ID + cùng nội dung → trả kết quả cũ, khác nội dung → 409 (idempotency).
- `RulesOnly` không gọi AI. `Hybrid`: AI lỗi → giữ quyết định luật, ALLOW nâng thành REVIEW, `MlScore=null`.
- Mỗi đánh giá lưu snapshot cấu hình luật, feature schema, điểm, chế độ, phiên bản mô hình.
- Không huấn luyện trong request; không đưa nhãn review vào pipeline huấn luyện tự động.
- Không thêm gateway, message broker, Keycloak, Redis, Elasticsearch.

## An toàn
- Không commit, không push, không tạo branch, không xoá file khi chưa được yêu cầu.
- Không commit secret; không đọc/sửa `.env`, `*.db`, `ai-service/artifacts/*`, `ai-service/data/*`.
- Không chạy lệnh ghi vào database thật. Không sửa dự án Python cũ ở thư mục bên cạnh.

## Báo cáo khi xong
Liệt kê: file đã sửa, tóm tắt thay đổi, lệnh build/test đã chạy và kết quả, điều gì chưa làm được hoặc còn nghi ngờ.
