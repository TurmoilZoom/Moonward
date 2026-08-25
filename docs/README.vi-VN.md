<h1 align="center">Moonward</h1>

<p align="center">
  Trình khởi chạy mã nguồn mở bên thứ ba dựa trên <a href="https://github.com/Scighost/Starward">Starward</a>, dành cho game PC của miHoYo<br/>
  <a href="https://github.com/TurmoilZoom/Moonward/releases/latest">Tải xuống</a>
</p>

<p align="center">
  <a href="../README.md">简体中文</a>
  · <a href="README.ja-JP.md">日本語</a>
  · <a href="README.ru-RU.md">Русский</a>
  · <a href="README.th-TH.md">ไทย</a>
  · Tiếng Việt
</p>


---

Trên nền Starward thượng nguồn, các thao tác thường dùng được gom vào lối tắt màn hình và một URL, đồng thời tăng cường điểm danh, gacha, hình nền. Tính năng chính:

#### Gacha

- **Nhật ký gacha** — thống kê banner có thể kéo để sắp xếp (tự cuộn ngang khi sát mép), danh sách hỗ trợ kéo để cuộn, thống kê neo trên cùng; chuỗi UP / chuỗi trượt và tỷ lệ trúng hiện rõ; 「Đảm bảo」của Kỳ quan Miliastra dùng thanh tiến độ
- **Lọc và chia sẻ** — menu thả xuống trên thanh tiêu đề chọn banner nào được hiện, chọn tất cả / đảo chọn / đặt lại; tạo ảnh chia sẻ kiểu mờ sương một chạm, gồm số đã tích và tiến độ bảo đảm
- **Đồng bộ gacha** — Genshin Impact / Zenless Zone Zero… có thể cập nhật nhật ký qua các cách liên quan miHoYo BBS; khi ra nhân vật mới chưa có trong kho, icon và tên được bổ sung tự động; tên vật phẩm theo ngôn ngữ ứng dụng
- **Trao đổi dữ liệu** — nhập / xuất nhật ký gacha UIGF; có thể nhập lịch sử từ Starward thượng nguồn ở chế độ chỉ đọc

#### Tài khoản và hộp công cụ

- **Điểm danh hàng ngày** — điểm danh miHoYo BBS / HoYoLAB, công tắc riêng từng game, hỗ trợ điểm danh tự động và điểm danh bù; khi mở game bằng lối tắt / URL / dòng lệnh, tài khoản đó cũng được điểm danh thêm một lần
- **Cải thiện đăng nhập** — máy chủ Trung Quốc đăng nhập bằng mã xác nhận gửi tới số điện thoại, máy chủ quốc tế dùng đăng nhập web; khi phiên hết hạn sẽ cố gia hạn tự động, không phải đăng nhập lại liên tục
- **Báo cáo tháng và ghi chú** — bố cục báo cáo tháng trong hộp công cụ (Lịch tháng khai phá / Báo cáo tháng Inter-Knot / Nhật kí nhà lữ hành) thống nhất; báo cáo Inter-Knot sửa dữ liệu theo ngày khi lệch múi giờ, mặc định hiện tháng hiện tại; ghi chú thời gian thực gặp kiểm soát rủi ro thì có lối xác minh

#### Khởi chạy

- **Nhiều cấu hình khởi chạy** — cùng một game có thể lưu không giới hạn bộ tham số khởi chạy và chương trình khởi chạy tùy chỉnh; đổi cấu hình hay sửa tham số không phải điền lại mỗi lần, đặt tên rồi tạo lối tắt màn hình được
- **Giao thức URL** — `moonward://` chỉ định game, cấu hình và tài khoản để khởi chạy / dừng / khởi động lại, hoặc chỉ kích hoạt điểm danh; nhúng vào script hay trang web được (xem [docs/UrlProtocol](UrlProtocol.md))
- **Khởi chạy nhanh** — menu hamburger trang chủ gom cài đặt game, khởi chạy nhanh và 「tạo lối tắt menu Start」

#### Giao diện và hình nền

- **Hình nền Trust** — Zenless Zone Zero có thể tải 「hình nền động Trust」và 「hình nền tĩnh Mindscape」từ bách khoa rồi đặt làm nền tùy chỉnh; mở thư viện là dùng cache máy, kiểm tra cập nhật im lặng ở nền
- **Hình nền tùy chỉnh** — hộp thoại nền riêng, hỗ trợ ảnh / video (kéo vào trang chủ để thay ngay); khôi phục từ khay hệ thống không nhấp nháy; sau khi danh sách nền cập nhật vẫn giữ lựa chọn poster

#### Khác

- **Tích hợp hệ thống** — có thể mở cùng Windows vào khay hệ thống; trang Giới thiệu điền sẵn thông tin chẩn đoán rồi nhảy tới GitHub Feedback trong một chạm, đồng thời mở thư mục nhật ký
- **Cập nhật im lặng** — tải phiên bản mới ở nền, tự cài sau khi thoát phần mềm, lần mở sau hiện nội dung cập nhật (Velopack + GitHub Releases)

Gói cài đặt xem [Releases](https://github.com/TurmoilZoom/Moonward/releases).

Dự án thượng nguồn: [Scighost/Starward](https://github.com/Scighost/Starward)  
Ghi nhận: [CREDITS.md](../CREDITS.md) (các dự án mã nguồn mở được tham khảo về tính năng và thiết kế)  
Giấy phép: [MIT](../LICENSE)

Chính sách quyền riêng tư: [docs/Privacy.md](Privacy.md) · [Tiếng Việt](Privacy.vi-VN.md)
