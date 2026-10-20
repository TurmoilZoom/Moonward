<p align="center">
  <img src="images/banner.png" alt="Moonward" width="720" />
</p>

<p align="center">
  Trình khởi chạy mã nguồn mở bên thứ ba dựa trên <a href="https://github.com/Scighost/Starward">Starward</a>, dành cho game PC của miHoYo<br/>
  <a href="https://github.com/TurmoilZoom/Moonward/releases/latest">Tải xuống</a>
</p>

<p align="center">
  <a href="../README.md">简体中文</a>
  · <a href="README.zh-TW.md">繁體中文</a>
  · <a href="README.en-US.md">English</a>
  · <a href="README.de-DE.md">Deutsch</a>
  · <a href="README.es-ES.md">Español</a>
  · <a href="README.it-IT.md">Italiano</a>
  · <a href="README.ja-JP.md">日本語</a>
  · <a href="README.ko-KR.md">한국어</a>
  · <a href="README.ru-RU.md">Русский</a>
  · <a href="README.th-TH.md">ไทย</a>
  · Tiếng Việt
</p>


---

Trên nền Starward thượng nguồn, các thao tác thường dùng được gom vào lối tắt màn hình và một URL, đồng thời tăng cường gacha, chiến tích, cài đặt game, hình nền, v.v. Tính năng chính:

#### Gacha

- **Nhật ký gacha** — Thống kê banner có thể kéo để sắp xếp và ghim ở trên cùng; chuyển đổi giữa dạng danh sách và lưới biểu tượng thu gọn; chuỗi UP / chuỗi trượt và tỷ lệ thắng 50/50 hiện rõ
- **Lọc và chia sẻ** — Menu thả xuống trên thanh tiêu đề chọn banner cần hiển thị, có chọn tất cả / đảo chọn / đặt lại; một cú nhấp tạo ảnh chia sẻ kiểu kính mờ, kèm số lượt đã kê và tiến độ bảo hiểm
- **Đồng bộ gacha** — Genshin Impact (máy chủ Trung Quốc) và Zenless Zone Zero có thể đồng bộ nhật ký từ miHoYo BBS, Zenless Zone Zero còn hỗ trợ HoYoLAB; vật phẩm mới chưa có trong danh mục được tự động bổ sung biểu tượng và tên; tên vật phẩm theo ngôn ngữ ứng dụng
- **Kỳ quan Miliastra** — Trang nhật ký cầu nguyện hoạt động giống trang nhật ký gacha (lọc, dạng thu gọn, kéo sắp xếp, ảnh chia sẻ); bổ sung các biểu tượng vật phẩm còn thiếu ở thượng nguồn
- **UIGF** — Nhập / xuất nhật ký gacha theo UIGF

#### Chiến tích và hộp công cụ

- **Điểm danh hàng ngày** — Điểm danh miHoYo BBS / HoYoLAB, bật tắt riêng cho từng game, hỗ trợ tự động điểm danh và điểm danh bù; khi thường trú ở khay hệ thống sẽ tự điểm danh lại sau mỗi lần làm mới hằng ngày
- **Cải thiện đăng nhập** — Máy chủ Trung Quốc đăng nhập bằng mã SMS qua số điện thoại, máy chủ quốc tế đăng nhập qua web; khi phiên hết hạn sẽ cố gắng tự gia hạn, không phải đăng nhập lại nhiều lần
- **Tự động cập nhật chiến tích** — Đặt tần suất cập nhật riêng cho từng tài khoản và mục dữ liệu (gồm cả tháng này và tháng trước của báo cáo tháng); chiến tích tự làm mới khi khởi động ứng dụng
- **Báo cáo tháng và ghi chú** — Báo cáo tháng trong hộp công cụ (Lịch tháng Khai Phá / Báo cáo tháng Inter-Knot / Nhật ký Lữ Khách) dùng chung bố cục; báo cáo Inter-Knot tính dữ liệu ngày theo múi giờ máy chủ và mặc định hiển thị tháng hiện tại; ghi chú thời gian thực của Genshin Impact hiển thị nhiều nội dung hơn; khi gặp kiểm soát rủi ro sẽ có lối vào xác minh
- **Hiển thị chiến tích** — Chiến tích và báo cáo tháng có thể xuất thành ảnh chia sẻ; Imaginarium Theater đánh dấu nhân vật dùng thử và nhân vật hỗ trợ, Anomaly Arbitration hiển thị lượt vượt nhanh và tên độ khó
- **Game đám mây** — Xem thời gian chơi còn lại của Cloud Genshin và Cloud Zenless Zone Zero, tự động nhận thời gian miễn phí mỗi ngày

#### Khởi chạy

- **Nhiều cấu hình khởi chạy** — Mỗi game lưu không giới hạn bộ tham số khởi chạy và trình khởi chạy tùy chỉnh; mỗi cấu hình có thiết lập riêng cho tham số DX12 và việc khởi chạy qua CMD, có thể đặt tên lưu lại và tạo lối tắt trên màn hình
- **Công cụ bên thứ ba** — Trình khởi chạy tùy chỉnh cũng có thể là liên kết giao thức URL; tham số dòng lệnh thường dùng có sẵn nhóm cho BetterGI, ZZZ OneDragon và March7th Assistant
- **Giao thức URL** — `moonward://` chỉ định game, cấu hình và tài khoản để khởi động / dừng / khởi động lại ngay, hoặc chỉ điểm danh; nhúng được vào script hoặc trang web (xem [docs/UrlProtocol](UrlProtocol.md))
- **Khởi chạy nhanh** — Menu hamburger trang chủ tích hợp cài đặt game, khởi chạy nhanh và lối tắt game; lối tắt có thể khởi chạy không cần hộp thoại UAC, các tác vụ đã lên lịch tương ứng được quản lý theo từng game trong cài đặt
- **Thời gian chơi** — Khi game đang chạy, nút Bắt đầu game hiển thị bộ đếm giờ; thống kê thời gian chơi cho phép đổi game, chọn năm và tháng, tạo ảnh chia sẻ; thời gian máy tính ở chế độ ngủ không được tính

#### Cài đặt và cập nhật

- **Máy chủ dùng liên kết cứng** — Khi cập nhật hoặc sửa game, các máy chủ khác dùng chung tệp qua liên kết cứng cũng được xử lý cùng (luôn cập nhật bản gốc trước); hộp thoại gỡ cài đặt liệt kê mọi máy chủ đã cài của cùng một game và có thể gỡ cùng lúc
- **Tùy chọn cài đặt** — Zenless Zone Zero có thể chọn tài nguyên đầy đủ hoặc tài nguyên cơ bản; bên cạnh đường dẫn cài đặt hiển thị loại ổ đĩa (SSD / HDD / di động / mạng); tải Chunk chạy song song theo từng khối
- **Sửa chữa và thông tin phiên bản** — Sửa game giờ có hộp thoại riêng và có thể chỉ sửa Miliastra Sandbox; số luồng kiểm tra được điều chỉnh theo loại ổ đĩa; trang「Thông tin phiên bản」trong cài đặt game hiển thị phiên bản, bản vá tăng dần và MD5 của tệp chương trình chính; tìm kiếm tự động có thể tìm lại các game đã cài trong ứng dụng (kể cả liên kết cứng)

#### Giao diện và hình nền

- **Hình nền Trust** — Trong Zenless Zone Zero, có thể tải「hình nền động Trust」và「hình nền tĩnh Mindscape」từ wiki rồi đặt làm hình nền tùy chỉnh; chế độ ngẫu nhiên đổi hình khi khởi động, khi chuyển game hoặc khi mở từ khay hệ thống; mở thư viện là dùng bộ nhớ đệm cục bộ, cập nhật được kiểm tra âm thầm ở nền
- **Hình nền tùy chỉnh** — Hộp thoại hình nền tùy chỉnh riêng, hỗ trợ ảnh / video (kéo vào trang chủ để thay trực tiếp); sau khi danh sách hình nền cập nhật vẫn giữ lựa chọn poster
- **Hình nền video** — Video không giải mã được bằng phần cứng sẽ tự động chuyển mã sang H.264; nếu thiếu tiện ích mở rộng HEVC / VP9, ứng dụng sẽ hướng dẫn cài đặt hoặc hiển thị ảnh tĩnh; khi tắt màn hình bộ giải mã được giải phóng, không còn chiếm CPU vô ích
- **Thanh công cụ trang chủ** — Dòng thời gian (đếm ngược banner / sự kiện), mã đổi thưởng từ livestream (máy chủ Trung Quốc) và bản đồ tương tác (Genshin Impact / Honkai: Star Rail)

#### Khác

- **Thường trú ở khay hệ thống** — Có thể tự khởi động vào khay hệ thống khi đăng nhập; khi thường trú, phím tắt toàn cục và điều khiển bằng tay cầm vẫn dùng được, menu khay có thể bật tắt các tính năng này và kiểm tra cập nhật
- **Đồng bộ LAN** — Lấy nhật ký gacha, dữ liệu hộp công cụ và thời gian chơi từ thiết bị khác trong mạng nội bộ; chỉ thêm những bản ghi máy này chưa có, không đồng bộ thông tin đăng nhập tài khoản
- **Quản lý dữ liệu** — Cơ sở dữ liệu, bộ nhớ đệm, nhật ký và hình nền được lưu chung trong thư mục bạn chọn; trong cài đặt có thể sao lưu cơ sở dữ liệu và xóa bộ nhớ đệm; lần khởi động đầu có thể nhập dữ liệu từ Starward thượng nguồn (chỉ đọc, không thay đổi dữ liệu gốc)
- **Phản hồi** — Trang Giới thiệu điền sẵn thông tin môi trường bằng một cú nhấp, mở trang phản hồi trên GitHub và đồng thời mở thư mục nhật ký
- **Cập nhật im lặng** — Tải phiên bản mới ở nền (khi thường trú ở khay cũng kiểm tra định kỳ), cài khi thoát hoặc khi khởi động lần sau, không làm gián đoạn game đang được ghi thời gian chơi; sau khi cài sẽ hiển thị nội dung cập nhật; gói cập nhật ưu tiên tải từ CNB, nếu lỗi sẽ chuyển sang GitHub (Velopack)

Gói cài đặt xem [GitHub Releases](https://github.com/TurmoilZoom/Moonward/releases); người dùng ở Trung Quốc đại lục cũng có thể tải từ [CNB](https://cnb.cool/TurmoilZoom/Moonward/-/releases).

Dự án thượng nguồn: [Scighost/Starward](https://github.com/Scighost/Starward)  
Ghi nhận: [CREDITS.md](../CREDITS.md) (các dự án mã nguồn mở được tham khảo về tính năng và thiết kế)  
Giấy phép: [MIT](../LICENSE)

Chính sách quyền riêng tư: [docs/Privacy.md](Privacy.md) · [Tiếng Việt](Privacy.vi-VN.md)
