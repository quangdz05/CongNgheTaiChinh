# Bản đồ kiến trúc và định vị file FraudShield

Tài liệu định vị theo cách tổ chức service/layer/feature của mẫu BTC-QLTCDT. Mọi đường dẫn dưới đây tính từ thư mục `fraudshield-dotnet`.

## Backend

Gốc service: `Back-end/src/Services/FraudDetectionService/`.

| Công việc | Vị trí |
|---|---|
| Entity giao dịch | `FraudDetectionService.Domain/Entities/Transaction.cs` |
| Kết quả tự động | `FraudDetectionService.Domain/Entities/Evaluation.cs` |
| Cấu hình luật | `FraudDetectionService.Domain/Entities/RulePolicy.cs` |
| Repository contract | `FraudDetectionService.Domain/Repositories/IFraudRepository.cs` |
| Request, response, feature contract | `FraudDetectionService.Application/Features/Transactions/DTOs/Contracts.cs` |
| AI và HMAC port | `FraudDetectionService.Application/Common/Interfaces/IDetectionAdapters.cs` |
| Analyze command và FluentValidation | `FraudDetectionService.Application/Features/Transactions/Commands/AnalyzeTransactionCommand.cs` |
| Truy vấn danh sách | `FraudDetectionService.Application/Features/Transactions/Queries/GetTransactionsQuery.cs` |
| Điều phối chấm điểm | `FraudDetectionService.Application/Features/Transactions/Services/DetectionService.cs` |
| Tạo đặc trưng | `FraudDetectionService.Application/Features/Transactions/Services/FeatureBuilder.cs` |
| Bộ luật | `FraudDetectionService.Application/Features/Rules/Engine/RuleEngine.cs` |
| Ghi nhận kiểm tra | `FraudDetectionService.Application/Features/Alerts/Commands/ReviewTransactionCommand.cs` |
| Sửa chính sách | `FraudDetectionService.Application/Features/Rules/Commands/UpdateRulePolicyCommand.cs` |
| Sinh demo | `FraudDetectionService.Application/Features/Demo/Commands/RunDemoCommand.cs` |
| So sánh luật và AI | `FraudDetectionService.Application/Features/Ai/Queries/CompareEvaluationQuery.cs` |
| EF DbContext | `FraudDetectionService.Infrastructure/Persistence/FraudDbContext.cs` |
| EF mapping | `FraudDetectionService.Infrastructure/Persistence/Configurations/{Entity}Configuration.cs` |
| Migration | `FraudDetectionService.Infrastructure/Persistence/Migrations/` |
| Repository implementation | `FraudDetectionService.Infrastructure/Repositories/FraudRepository.cs` |
| HTTP AI adapter | `FraudDetectionService.Infrastructure/Services/Ai/AiClient.cs` |
| HMAC và password | `FraudDetectionService.Infrastructure/Services/Security/Security.cs` |
| API auth | `FraudDetectionService.Api/Controllers/AuthController.cs` |
| Login use case | `FraudDetectionService.Application/Features/Auth/Commands/LoginCommand.cs` |
| API giao dịch, export, review | `FraudDetectionService.Api/Controllers/TransactionsController.cs` |
| API dashboard, rules, AI, demo | `FraudDetectionService.Api/Controllers/OperationsController.cs` |
| DI, auth, rate limit, startup | `FraudDetectionService.Api/Program.cs` |
| SQLite và AI URL | `FraudDetectionService.Api/appsettings.json` |

Quyền: `Back-end/src/Shared/Shared.Security/Permissions/FraudPermissions.cs`. Test: `Back-end/tests/FraudDetectionService.Tests/DetectionTests.cs`.

Dependency: Domain không tham chiếu layer khác. Application chỉ tham chiếu Domain và thư viện validation. Infrastructure tham chiếu Application và triển khai các port. API tham chiếu Infrastructure để đăng ký DI và Application để gọi use case. Không gọi EF hoặc HttpClient từ Domain.

## Frontend React

| Chức năng | Vị trí từ `Front-end/src/` |
|---|---|
| Layout, điều hướng và dữ liệu dashboard | `App.tsx` |
| Đăng nhập | `features/auth/_components/login-form.tsx` |
| Tạo giao dịch | `features/transactions/_components/transaction-form.tsx` |
| Chi tiết và đánh giá | `features/transactions/_components/transaction-detail.tsx` |
| Cấu hình luật | `features/rules/_components/rules-form.tsx` |
| AI metrics và so sánh | `features/ai/_components/ai-panel.tsx` |
| API từng feature | `features/{feature}/_services/services.ts` |
| Ràng buộc UI | `features/{feature}/_schema/schema.ts` |
| HTTP client và DTO dùng chung | `shared/services/api.ts` |
| Badge, empty state | `components/common.tsx` |
| Giao diện responsive | `styles.css` |

React Vite dùng entry `main.tsx`, không phải Next App Router. `_schema` là ràng buộc TypeScript và HTML hiện tại; backend FluentValidation mới là cổng kiểm tra cuối cùng. Giao diện và store dùng React state; không thêm Zustand/SWR/Yup khi chưa có nhu cầu.

## AI và luồng dữ liệu

`ai-service/features.py`: đặc trưng v1 và baseline. `train.py`: đọc CSV hoặc sinh dữ liệu mô phỏng, chia theo thời gian, huấn luyện HistGradientBoosting, chọn ngưỡng validation và xuất metrics. `app.py`: chỉ nạp artifact và suy luận, không huấn luyện trong request. `contracts/feature-v1-fixture.json`: fixture đối chiếu hai ngôn ngữ.

Frontend → ASP.NET API → command handler → đặc trưng/bộ luật → AI adapter khi Hybrid → quyết định → repository/SQLite. AI API không ghi DB nghiệp vụ. Không chuyển nhãn review sang pipeline huấn luyện tự động. Gateway riêng, message broker, Keycloak, Redis và Elasticsearch không được thêm chỉ để giống số lượng service trong dự án tham khảo.
