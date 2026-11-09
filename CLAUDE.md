# FraudShield — hướng dẫn cho Claude và các sub agent

Ứng dụng phát hiện gian lận giao dịch (học phần FinTech): ASP.NET Core 10 (Clean Architecture, CQRS thủ công) + React 18/TypeScript/Vite + Python AI service (FastAPI, HistGradientBoosting). Chi tiết: `README.md`. Định vị file theo tính năng: `docs/ARCHITECTURE_MAP.md` — đọc trước khi tìm/sửa code.

## Lệnh build / test (Windows, chạy từ thư mục gốc)
```cmd
dotnet build FraudShield.Clean.slnx
dotnet test FraudShield.Clean.slnx
.venv\Scripts\python.exe -m pytest ai-service\tests -q
cd Front-end && npm run build && cd ..
.venv\Scripts\python.exe scripts\integration_check.py   :: tự bật API+AI ở cổng riêng, DB tạm; cần build Debug + đã train
.venv\Scripts\python.exe scripts\regression_check.py
```
Huấn luyện AI mô phỏng: `.venv\Scripts\python.exe ai-service\train.py --synthetic --samples 6000`.
CI (`.github/workflows/ci.yml`) chạy đúng chuỗi trên; mọi thay đổi phải giữ CI xanh.

Chạy thủ công: `scripts\start-api.cmd` (http://127.0.0.1:5080), `scripts\start-web.cmd` (http://127.0.0.1:5173), `scripts\start-ai.cmd` (http://127.0.0.1:8001). Tài khoản Development: xem README (admin / analyst).

## Kiến trúc & quy ước bắt buộc
- Backend gốc `Back-end/src/Services/FraudDetectionService/`: Domain → Application → Infrastructure → Api.
  - Domain không tham chiếu layer khác; không gọi EF/HttpClient từ Domain.
  - Application: command/query handler tường minh qua DI — **không dùng MediatR, không dùng AutoMapper** (map DTO bằng code). Validate bằng FluentValidation.
  - Infrastructure: EF Core + SQLite, migration trong `Persistence/Migrations/`, adapter AI/HMAC.
  - Quyền: `Back-end/src/Shared/Shared.Security/Permissions/FraudPermissions.cs` (Admin, Analyst).
- `Directory.Build.props`: `Nullable` bật, `TreatWarningsAsErrors=true` → không được để warning.
- Frontend `Front-end/src/features/{auth,transactions,rules,ai}/{_components,_services,_schema}`; HTTP client chung `shared/services/api.ts`. Chỉ React state — không thêm Zustand/SWR/Yup/thư viện mới khi chưa cần.
- AI: `features.py` (feature v1), `train.py` (offline), `app.py` (chỉ suy luận). Feature C# và Python phải khớp fixture `contracts/feature-v1-fixture.json`.
- Format theo `.editorconfig`; frontend có prettier.

## Quy tắc nghiệp vụ không được phá vỡ
- Giao dịch bất biến; cùng ID + cùng nội dung → trả kết quả cũ, khác nội dung → 409 (idempotency).
- `RulesOnly` không gọi AI. `Hybrid`: AI lỗi → giữ quyết định luật, ALLOW nâng thành REVIEW, `MlScore=null`.
- Mỗi đánh giá lưu snapshot cấu hình luật, feature schema, điểm, chế độ, phiên bản mô hình.
- Không huấn luyện trong request; không đưa nhãn review vào pipeline huấn luyện tự động.
- Không thêm gateway, message broker, Keycloak, Redis, Elasticsearch.

## An toàn
- Không commit secret; `.env`, `*.db`, `ai-service/artifacts/*`, `ai-service/data/*` đã nằm trong `.gitignore`.
- Không chạy smoke test trên database thật. Không sửa dự án Python cũ ở thư mục bên cạnh.
- Không commit/push khi người dùng chưa yêu cầu.

## Làm việc nhóm sub agent
Agent `ba`, `dev`, `tester`, `qa` trong `.claude/agents/`, trao đổi qua `docs/team/01..04-*.md`. Prompt điều phối: `docs/team/ORCHESTRATION.md`.
