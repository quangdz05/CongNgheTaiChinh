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

Mở http://127.0.0.1:5173. Thông tin đăng nhập được cung cấp riêng bởi người quản trị môi trường.

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

## Mô hình dữ liệu thực và bộ dữ liệu cá nhân

Trong **Mô hình AI**, tab **Dữ liệu thực ULB** hiển thị mô hình đã train từ ULB Credit Card Fraud Detection và cho chấm thử bằng `Time`, `Amount`, `V1`–`V28`. Đây là schema riêng `ulb-pca-v1`; không thay thế mô hình 10 đặc trưng dùng cho form giao dịch VND. Không thể tạo lại các cột PCA chỉ từ số tiền, thiết bị và IP.

Nguồn CSV công khai: https://storage.googleapis.com/download.tensorflow.org/data/creditcard.csv. Tải vào `ai-service/data/creditcard.csv`, rồi chạy `.venv\Scripts\python.exe ai-service\train_ulb.py --csv ai-service\data\creditcard.csv`. Artifact lưu trong `ai-service/artifacts/ulb/`. Pipeline loại các dòng trùng hoàn toàn, chia theo thời gian 60/20/20, giữ cùng timestamp trong một tập và chỉ chọn ngưỡng bằng validation. Bản train ngày 12/11/2026 còn 283.726 mẫu sau loại trùng; test 56.746 mẫu đạt precision 0,8235, recall 0,7568, F1 0,7887, AP 0,8015. Đây là kết quả benchmark lịch sử, không chứng minh hiệu quả trên giao dịch VND thực tế.

Tab **Bộ dữ liệu của tôi** cho Admin và Analyst tạo bộ riêng, nhập giao dịch với nhãn hợp lệ/gian lận/chưa xác định, hoặc sinh 1.000–10.000 giao dịch mô phỏng với seed tự chọn. Có xem 20 dòng gần nhất, tải CSV, xóa bộ, huấn luyện nền và chấm thử bằng mô hình của bộ đó. Mẫu chưa xác định không tham gia train. Cần ít nhất 500 mẫu có nhãn và cả hai lớp ở mỗi tập theo thời gian. Nguồn mô phỏng và nhãn do người dùng nhập được ghi rõ; các bộ riêng không tự thay mô hình Hybrid toàn hệ thống. Mỗi tài khoản tối đa 10 bộ, mỗi bộ tối đa 10.000 dòng, toàn dịch vụ chỉ chạy một job train cùng lúc.

Dữ liệu và mô hình riêng lưu dưới `ai-service/artifacts/datasets/`, cùng volume artifacts khi dùng Docker. API yêu cầu token, Python yêu cầu service key và chỉ cho chủ bộ truy cập. Job đang chạy sẽ được đánh dấu gián đoạn nếu AI khởi động lại. Sau khi cập nhật mã Python và train ULB, chạy `scripts/redeploy-share-ai.ps1` rồi `scripts/redeploy-share.ps1` để cập nhật demo public; giữ nguyên DB nghiệp vụ và tunnel hiện tại. Tài liệu Word AI soạn trước thay đổi này mô tả phiên bản cũ; dùng phần này cho trạng thái mới.

## Phân nhóm dấu hiệu rủi ro

Chính sách `RiskSignals` dùng cho giao dịch mới thay vì gộp mọi dấu hiệu thành một phần trăm rủi ro. Một giao dịch có thể có nhiều nhóm: **tần suất**, **giá trị**, **thay đổi truy cập**, **liên kết tài khoản**, **chất lượng dữ liệu** và **tín hiệu mô hình**. Mỗi dấu hiệu lưu mã, nhóm, bằng chứng, nguồn và hành động `REVIEW` / `OBSERVE` / `INFORMATION`. Đây là phân nhóm dấu hiệu quan sát được bằng luật, không phải mô hình AI phân loại các kiểu gian lận. Không suy ra chiếm đoạt tài khoản hay rửa tiền từ nhãn gian lận nhị phân.

Luật chuỗi giao dịch đếm cả giao dịch hiện tại trong cửa sổ cấu hình (khởi tạo **3 giao dịch / 60 giây**). Nếu đủ ít nhất **20** giao dịch tham chiếu trước chuỗi trong 24 giờ, số đếm còn phải vượt mức cao nhất của các cửa sổ lịch sử cùng độ dài. Thiếu mẫu tham chiếu vẫn có thể yêu cầu kiểm tra chuỗi nhưng ghi rõ chưa đủ cơ sở kết luận tần suất bất thường. Những số này là tham số thử nghiệm có thể sửa, không phải chuẩn ngân hàng hoặc ngưỡng được chứng minh bằng dữ liệu thực tế.

Luật giá trị so sánh z-score với lịch sử cá nhân 24 giờ khi có đủ mẫu (mặc định 2,5; độ lệch chuẩn được chặn dưới 1 VND theo feature builder hiện tại). Hạn mức tiền cố định là kiểm soát vận hành tùy chọn, mặc định tắt ở môi trường mới. Thay đổi đồng thời thiết bị và quốc gia được ghi nhận nếu người dùng có lịch sử. Liên kết thiết bị/IP đếm người dùng phân biệt trong 24 giờ, gồm người dùng hiện tại; IP chung riêng lẻ chỉ là `OBSERVE` vì có thể là NAT hoặc mạng công cộng. Lịch sử ít được ghi `INFORMATION`, không tự tạo cảnh báo.

Trong `RulesOnly`, chỉ dấu hiệu luật yêu cầu kiểm tra mới đưa về `REVIEW`. Trong `Hybrid`, luật **hoặc** AI vượt ngưỡng điểm kiểm tra sẽ đưa về `REVIEW`; AI không khả dụng cũng yêu cầu kiểm tra. Không cộng điểm luật với AI, không tự `BLOCK` bằng hạn mức, giờ giao dịch hoặc loại merchant. `ALLOW` chỉ có nghĩa chưa có dấu hiệu yêu cầu kiểm tra theo chính sách. Điểm mô hình VND vẫn là mô hình 10 đặc trưng từ dữ liệu mô phỏng, tách khỏi mô hình ULB; chưa hiệu chuẩn cho vận hành thực tế.

Admin chỉnh tham số, bật/tắt nhóm kiểm tra và lưu phiên bản ở **Luật nghiệp vụ**; Analyst chỉ xem. Các giá trị cần được chọn và đánh giá trên tập validation có nhãn theo nghiệp vụ, đo precision/recall, số cảnh báo và khả năng xử lý; không tự sinh nhãn train từ các luật này. Lịch sử chỉ 24 giờ và dữ liệu do người dùng nhập chưa đủ để phát hiện mọi kiểu gian lận. Không tự train lại hoặc thay mô hình khi tạo giao dịch.

Migration bổ sung dữ liệu có mặc định `LegacyScore` cho bản ghi cũ; không tính lại hoặc thay quyết định cũ. Chính sách trên DB đã có phải chuyển sang `RiskSignals` qua API quản trị để có nhật ký thay đổi. Môi trường mới khởi tạo `RiskSignals`. Kết quả mới lưu bất biến dấu hiệu, đặc trưng và phiên bản chính sách; API trả `ruleScore`/`finalScore` là null ở phương pháp này, điểm AI hiển thị riêng. CSV có thêm phương pháp và JSON dấu hiệu. So sánh AI dùng dấu hiệu/chính sách gốc cùng dự đoán AI mới, không lấy lịch sử tương lai để sửa đánh giá cũ.

### Nguồn tham khảo và trạng thái kiểm định

Trang **Luật nghiệp vụ** và từng dấu hiệu mới hiển thị nguồn kèm phạm vi áp dụng. API `GET /api/rules/references` trả danh mục nguồn; kết quả lưu bản chụp nguồn trong `riskFindings`, cùng `validationStatus=Experimental` và `inputTrust=UserProvidedDemo`. Thiết bị, IP và quốc gia nhập tay hoặc mô phỏng chưa phải thông tin truy cập được xác minh. Không lấy IP của nhân viên đang nhập làm IP người thực hiện giao dịch tài chính.

Nguồn về tần suất: [Federal Reserve](https://www.frbservices.org/resources/resource-centers/fraud-mitigation-resource-center/frfs-fraud-mitigation-products-services/). Nguồn về hồ sơ và kiểm định ngưỡng: [FFIEC Monitoring](https://bsaaml.ffiec.gov/manual/AssessingComplianceWithBSARegulatoryRequirements/04). Ngoại lệ thống kê: [NIST](https://itl.nist.gov/div898/handbook/eda/section3/eda35h.htm). Thiết bị và vị trí: [FFIEC Access 2021](https://www.federalreserve.gov/frrs/guidance/authentication-and-access-to-financial-institution-services-and-systems-interagency-guidance.htm). Liên kết tài khoản: [FinCEN FIN-2020-A005, dấu hiệu 6 trong bối cảnh COVID-19](https://www.fincen.gov/system/files/advisory/2020-07-30/FinCEN%20Advisory%20Covid%20Cybercrime%20508%20FINAL.pdf). Nguồn hỗ trợ nhóm dấu hiệu, không chứng minh 3/60 giây, 20 mẫu, z=2,5 hoặc số người dùng 3/5 là ngưỡng gian lận chuẩn.

Chính sách mới chỉ lưu được `RiskSignals`; `LegacyScore` chỉ giữ cho kết quả lịch sử. RiskSignals không chạy luật ban đêm hoặc merchant của cách cũ. Feature AI `hourOfDay` vẫn là UTC và `merchantRisk` vẫn là giả định của schema đã train; không đổi ngầm định nghĩa feature để tránh lệch giữa train và suy luận. Ngưỡng AI từ validation được hiển thị riêng, quản trị viên có thể chọn áp dụng; benchmark mô hình không phải phép đo hiệu quả Hybrid mới.

Tài liệu cập nhật: [Hướng dẫn AI và luật](docs/Tai_lieu_mo_hinh_AI_FraudShield_Cap_nhat.docx) và [Slide cập nhật](docs/slides/FraudShield_Cap_nhat.pptx). Bản Word cũ có thể đang mở nên bản cập nhật được lưu riêng. Hai bản cũ được giữ để đối chiếu, không dùng làm mô tả chính sách mới.

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

Với chính sách `RiskSignals`, chế độ `RulesOnly` hoàn toàn không gọi AI predict; chỉ dấu hiệu luật có hành động `REVIEW` mới yêu cầu kiểm tra. `Hybrid` gọi AI: có dấu hiệu luật yêu cầu kiểm tra hoặc điểm AI ≥ `reviewThreshold` (`MODEL_SIGNAL`) thì quyết định `REVIEW`. Nếu AI không khả dụng, quyết định `REVIEW` với dấu hiệu `AI_UNAVAILABLE` và `MlScore=null`. Chính sách này không cộng hoặc trộn điểm luật với AI, không tự `BLOCK`; cơ chế trộn điểm và BLOCK chỉ còn trong kết quả lịch sử `LegacyScore`. Điểm AI không phải xác suất gian lận đã hiệu chuẩn.

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

Ưu tiên mặc định P3 chỉ áp dụng khi `REVIEW` hoàn toàn do AI không khả dụng; nếu đồng thời có dấu hiệu luật yêu cầu kiểm tra thì vẫn là P2.

API mới: `GET /api/alerts`, `/api/alerts/stats`, `/api/alerts/{id}`, `/api/alerts/{id}/timeline`; các lệnh `POST /api/alerts/{id}/{command}` cần `requestId` và `expectedVersion`. `POST /api/transactions/{id}/reviews` trả 409 `LEGACY_REVIEW_DISABLED`; GET giao dịch/review cũ vẫn giữ. Khi chạy bản mới, migration thêm bảng Alert và startup backfill mọi non-ALLOW thành New/Unknown, giữ review cũ và bắt đầu SLA tại import. Đã deploy lên demo tạm ngày 09/11/2026 sau sao lưu; giữ 36 giao dịch và tạo 9 Alert. Mỗi môi trường mới vẫn cần sao lưu/đối soát backlog trước triển khai.

Chi tiết: [đặc tả](docs/team/01-requirements.md), [triển khai và kiểm chứng](docs/team/02-implementation.md). Kiểm tra HTTP với database tạm: `.venv\Scripts\python.exe scripts\alert_check.py`. Kiểm tra UI tùy chọn: đặt `ALERT_UI_CHECK=1` rồi chạy script trên; cần Playwright và Edge, không cài thêm thư viện vào ứng dụng.

Nguồn ý tưởng và các tình huống luật được tham khảo từ dự án FraudShield Python trong thư mục bên cạnh; bản .NET/React được triển khai mới. Xem `THIRD_PARTY_NOTICES.md`.
