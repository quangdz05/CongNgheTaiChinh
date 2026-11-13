# Kịch bản trình diễn RiskSignals

## Chuẩn bị

- Chỉ dùng database tạm hoặc môi trường demo riêng, không dùng database thật; không nhập thông tin tài khoản hoặc thẻ thật.
- Chạy API và web theo README; chạy thêm AI khi trình diễn Hybrid. Đăng nhập Admin bằng tài khoản của môi trường demo. Quay video dự phòng trước buổi trình diễn.
- Kiểm tra **Luật nghiệp vụ** đang dùng `RiskSignals`. Môi trường mới có `AmountEnabled=false` (tắt kiểm tra hạn mức), chuỗi **3 giao dịch / 60 giây**, `MinimumHistory=20`, chung thiết bị **≥3 người dùng → REVIEW**, chung IP **≥5 người dùng → OBSERVE**. Đây là tham số thử nghiệm, không phải ngưỡng gian lận đã kiểm định.
- Trước phần Hybrid, khi AI đã chạy và tải được metrics, Admin bấm **Dùng ngưỡng validation của mô hình**, rồi **Lưu phiên bản mới**. Ngưỡng validation mô hình VND hiện là **0,675**; mặc định chính sách là **0,4**. Đối chiếu giá trị hiển thị nếu mô hình đã được thay đổi.

Với giao dịch mới, `RiskSignals` chỉ quyết định `ALLOW` hoặc `REVIEW`, không tự `BLOCK` và không cộng hoặc trộn điểm luật với điểm AI. `RulesOnly` chỉ yêu cầu kiểm tra khi có dấu hiệu luật mang hành động `REVIEW`; `OBSERVE` và `INFORMATION` riêng lẻ không tạo Alert. Trong `Hybrid`, luật **hoặc** điểm AI **≥ `reviewThreshold`** đưa giao dịch về `REVIEW`; AI lỗi cũng đưa về `REVIEW`. Mỗi giao dịch `REVIEW` tạo một Alert riêng.

## Phần 1 – RulesOnly (≈2 phút)

1. Chọn `RulesOnly`, bấm **Tạo bộ demo 18 giao dịch**. Mỗi lần chạy có mã đợt riêng. Bộ demo bắt đầu lúc **12:00 UTC hôm trước**, mỗi giao dịch tiếp theo cách 10 giây; khi xem trên UI lưu ý múi giờ hiển thị.
2. Mở **NORMAL** (150.000 VND): `ALLOW` nghĩa là chưa có cờ yêu cầu kiểm tra theo chính sách, không chứng minh giao dịch hợp lệ. `LIMITED_HISTORY` chỉ là thông tin về lịch sử ít.
3. Mở **giao dịch thứ 3 của VELOCITY**, xác định theo thời gian tăng dần. Nhóm có 11 giao dịch cùng người dùng, cách nhau 10 giây; từ giao dịch thứ 3 có `RAPID_SEQUENCE → REVIEW`. Chỉ ra bằng chứng số giao dịch trong cửa sổ 60 giây, hành động yêu cầu kiểm tra và mở **Nguồn tham khảo và phạm vi áp dụng**. Đọc ghi chú thiếu lịch sử tham chiếu: chưa đủ 20 mẫu trước chuỗi, nên chưa đủ cơ sở kết luận tần suất bất thường so với hành vi cá nhân.
4. Mở **RING-2**: bốn người dùng RING-0 đến RING-3 dùng chung thiết bị; RING-2 là người thứ ba, đạt ngưỡng `SHARED_DEVICE → REVIEW`. RING-3 cũng có dấu hiệu này. Đây là dấu hiệu cần xác minh liên kết, chưa chứng minh gian lận.
5. Mở **HIGH** (150 triệu VND), đối chiếu **CRITICAL** (300 triệu VND). NORMAL, HIGH và CRITICAL dùng người dùng và thiết bị riêng. Khi hạn mức tắt, HIGH/CRITICAL đều là `ALLOW`, chỉ có `INFORMATION: LIMITED_HISTORY`. Tên HIGH/CRITICAL là tên mẫu demo, không phải quyết định của chính sách.

## Phần 2 – Quản lý Alert (≈1,5 phút)

1. Đăng nhập Analyst, mở một Alert từ VELOCITY hoặc RING. Bấm **Nhận việc** (`New → Assigned`), sau đó **Bắt đầu điều tra** (`InProgress`).
2. Chỉ ra ưu tiên mặc định **P2**, ngân sách SLA **60 phút** và hạn SLA cụ thể trên màn hình. SLA chạy liên tục 24/7. `BLOCK` chỉ còn ở dữ liệu lịch sử có ưu tiên P1 (15 phút); `REVIEW` hoàn toàn do AI không khả dụng là P3 (240 phút). Nếu AI không khả dụng nhưng có dấu hiệu luật yêu cầu kiểm tra thì vẫn là P2.
3. Đóng cảnh báo với kết luận **Chưa đủ bằng chứng — Unknown**, kèm lý do, ví dụ: “Dữ liệu mô phỏng, thiếu lịch sử tham chiếu và chưa có bằng chứng độc lập để xác nhận gian lận.” Unknown không được tự coi là hợp lệ hoặc dùng làm nhãn huấn luyện AI.
4. Xem **Lịch sử xử lý**: người thực hiện, thời điểm, lý do và các bước chuyển trạng thái.

Nêu hạn chế hiện tại: một chuỗi giao dịch có thể tạo nhiều Alert riêng; hệ thống chưa gộp chúng thành một vụ việc điều tra.

## Phần 3 – Luật nghiệp vụ & phân quyền (≈1 phút)

1. Với Analyst, mở **Luật nghiệp vụ** để cho thấy chỉ xem, không sửa được luật.
2. Đăng nhập lại Admin. Bật **Kiểm tra hạn mức nghiệp vụ**, đặt hạn mức **125.000.000 VND**, rồi bấm **Lưu phiên bản mới**. Có thể minh họa đổi ngưỡng AI thay cho hạn mức; thay đổi chỉ áp dụng cho giao dịch mới.
3. Nếu bật hạn mức rồi chạy lại bộ demo trong `RulesOnly`, HIGH và CRITICAL mới có `BUSINESS_LIMIT → REVIEW`, không tự BLOCK. Mở giao dịch của đợt cũ để đối chiếu: kết quả cũ giữ snapshot cấu hình, dấu hiệu và phiên bản chính sách tại lúc đánh giá.

## Phần 4 – Hybrid + AI (≈1,5 phút)

1. Chạy AI, đăng nhập Admin và áp dụng ngưỡng validation như phần chuẩn bị. Chọn `Hybrid` cho giao dịch mới. Nếu vừa bật hạn mức ở phần 3, có thể tắt và lưu phiên bản mới để dễ quan sát tín hiệu AI riêng.
2. Tạo giao dịch thử với nhóm merchant `crypto`, thiết bị mới, quốc gia `US`, số tiền lớn. Đây chỉ là đầu vào minh họa, không bảo đảm điểm vượt ngưỡng. Xem điểm AI thực tế: **nếu điểm ≥ ngưỡng**, có `MODEL_SIGNAL → REVIEW`; dấu hiệu luật yêu cầu kiểm tra cũng đủ để REVIEW, độc lập với điểm AI. Không mô tả các đầu vào này là luật tự BLOCK.
3. Mở **Mô hình AI**, chọn tab **AI giao dịch VND**. Chỉ ra phiên bản, metrics, ngưỡng validation và confusion matrix trên tập test. Nêu rõ nguồn dữ liệu mô phỏng; metrics không đo hiệu quả thực tế hoặc hiệu quả của toàn bộ chính sách Hybrid.
4. Tùy chọn: mở tab **Dữ liệu thực ULB**, giải thích đây là mô hình/schema PCA riêng, không thay mô hình VND dùng cho form giao dịch.

## Phần 5 – AI lỗi (chỉ trên môi trường local)

1. Trên môi trường local riêng, dừng AI rồi gửi một giao dịch **mới** ở chế độ `Hybrid`. Dùng người dùng, thiết bị và IP riêng, số tiền nhỏ, hạn mức tắt để tránh dấu hiệu luật yêu cầu kiểm tra.
2. Mở kết quả: `AI_UNAVAILABLE → REVIEW`, `MlScore=null` (điểm AI trống), không coi lỗi AI là điểm bằng 0. Nếu REVIEW hoàn toàn do AI không khả dụng, Alert mặc định **P3 / 240 phút**; các dấu hiệu INFORMATION như `LIMITED_HISTORY` không làm tăng ưu tiên.
3. Gửi thêm giao dịch Hybrid có dấu hiệu luật yêu cầu kiểm tra, chẳng hạn hoàn thành chuỗi 3 giao dịch trong 60 giây. Dù AI vẫn lỗi, Alert mặc định **P2 / 60 phút**. Có thể chuyển sang `RulesOnly` để minh họa chế độ này không gọi AI.
4. Khởi động lại AI khi kết thúc. Không cố gây lỗi hoặc dừng AI trên bản public.

## Nhập tay chuỗi giao dịch (tùy chọn)

- Dùng cùng mã người dùng cho ít nhất 3 giao dịch, giữ thiết bị/IP nhất quán để dễ giải thích.
- Ô **Thời gian giao dịch** nhập được đến giây theo giờ trên máy. Chọn thời điểm trong quá khứ gần, nhập tăng dần, ví dụ `10:00:00`, `10:00:10`, `10:00:20` cùng ngày, đều trong 60 giây; gửi theo đúng thứ tự thời gian.
- Không dùng cùng một thời điểm cho cả chuỗi. Khi chưa đủ lịch sử tham chiếu, chỉ ra ghi chú giới hạn thay vì khẳng định hành vi bất thường đã được chứng minh.

## Không nói

- “Điểm AI là xác suất gian lận đã hiệu chuẩn.”
- “REVIEW là gian lận” hoặc “ALLOW là giao dịch đã được xác nhận hợp lệ”.
- “Metrics trên dữ liệu mô phỏng chứng minh hiệu quả vận hành thực tế.”
- “Luật mạnh ghi đè BLOCK”, “trộn điểm luật và AI” hoặc “VELOCITY là 10 giao dịch/giờ” khi mô tả RiskSignals. Cơ chế chấm điểm/BLOCK cũ chỉ còn để đọc kết quả lịch sử `LegacyScore`.
