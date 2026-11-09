# Điều phối nhóm 4 sub agent: BA → Dev → Tester → QA

## Cài đặt
Chép thư mục `.claude/agents/` (ba.md, dev.md, tester.md, qa.md) vào thư mục gốc dự án
(hoặc `~/.claude/agents/` để dùng cho mọi dự án). Khởi động lại Claude Code, kiểm tra bằng `/agents`.

## Luồng làm việc
```
BA ──> Dev ──> Tester ──┬─(Fail)──> Dev ──> Tester (lặp, tối đa 3 vòng)
                        └─(Pass)──> QA ──┬─ GO ──> Bàn giao
                                         └─ NO-GO ──> BA / Dev / Tester theo phân công
```
Mọi trao đổi giữa các agent đi qua file trong `docs/team/`:

| File | Người viết | Người đọc |
|---|---|---|
| 01-requirements.md | BA | Dev, Tester, QA |
| 02-implementation.md | Dev | Tester, QA |
| 03-test-report.md | Tester | Dev, QA |
| 04-qa-review.md | QA | Tất cả |

## Prompt điều phối (dán vào phiên Claude Code chính)

```
Bạn là agent điều phối dự án. Hãy hoàn thiện yêu cầu sau bằng nhóm sub agent ba, dev, tester, qa:

<YÊU CẦU>
[Mô tả tính năng / mục tiêu dự án ở đây]
</YÊU CẦU>

Quy tắc điều phối:
1. Gọi agent `ba` để phân tích và tạo docs/team/01-requirements.md.
   Nếu có "Câu hỏi mở" ảnh hưởng nghiệp vụ, DỪNG và hỏi tôi trước khi sang bước 2.
2. Gọi agent `dev` để thiết kế + triển khai + unit test, ghi docs/team/02-implementation.md.
3. Gọi agent `tester` để kiểm thử, ghi docs/team/03-test-report.md.
   - Nếu CHƯA SẴN SÀNG: gọi lại `dev` sửa các BUG, rồi `tester` re-test. Tối đa 3 vòng;
     quá 3 vòng thì dừng và báo cáo tôi.
4. Gọi agent `qa` để rà soát cuối, ghi docs/team/04-qa-review.md.
   - NO-GO: chuyển việc cho đúng agent được QA phân công, sau đó chạy lại `tester` và `qa`.
5. Kết thúc: báo cáo tôi quyết định của QA, danh sách file thay đổi, kết quả test,
   rủi ro còn lại. Không commit/push nếu tôi chưa yêu cầu.

Không tự làm thay việc của sub agent; chỉ điều phối, truyền ngữ cảnh ngắn gọn
(đường dẫn file docs/team/*) và tổng hợp kết quả.
```

## Gọi riêng từng agent
- `Dùng agent ba phân tích yêu cầu: ...`
- `Dùng agent dev sửa các BUG trong docs/team/03-test-report.md`
- `Dùng agent tester kiểm thử lại sau khi dev sửa`
- `Dùng agent qa review và cho quyết định GO/NO-GO`
