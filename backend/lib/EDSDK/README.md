# Canon EDSDK

Host dùng Canon EDSDK để điều khiển máy ảnh Canon EOS (đã nhắm tới EOS R50) qua USB.

- Chép `EDSDK.dll` **bản 64-bit, EDSDK 13.17 trở lên** vào thư mục này. Bản đang dùng lấy từ `D:\WORK\FavoriCurrent\app\net8.0-windows\EDSDK.dll` (13.17.0.6401).
- Khi build, `PhotoBooth.Host.csproj` tự chép mọi `*.dll` ở đây ra cạnh `PhotoBooth.Host.exe`.
- File DLL không được commit (xem `.gitignore`): EDSDK là thư viện độc quyền của Canon, tải về qua chương trình nhà phát triển của Canon.
- Thiếu DLL thì app vẫn chạy, bước chụp dùng webcam; admin panel (tab Kiểm Tra Thiết Bị) sẽ báo thiếu EDSDK.
