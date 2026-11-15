# FUJIFILM X SDK (runtime)

Host dùng FUJIFILM X Series Digital Camera Shooting SDK để điều khiển máy Fujifilm X (X-T, X-S, X-H...) qua USB.

- Cần các file runtime 64-bit của SDK (bản đang dùng: XAPI.dll 1.17.0.0):
  `XAPI.dll`, `XSDK.DAT`, `FTLPTP.dll`, `FTLPTPIP.dll`, `FF0000API.dll` … `FF0022API.dll` (mỗi file ứng với một nhóm dòng máy).
- Bản hiện tại chép từ `D:\SETUP\HIHIBOOTH\sidecars\hihicam-bridge-bin\`.
- Khi build, `PhotoBooth.Host.csproj` tự chép mọi file ở đây ra cạnh `PhotoBooth.Host.exe`.
- Các file này không được commit (xem `.gitignore`): SDK độc quyền của FUJIFILM, tải qua chương trình SDK của Fujifilm.

Trên máy ảnh: Menu Kết nối (Connection Setting) > Chế độ kết nối PC / USB > **USB TETHER SHOOTING AUTO**,
chất lượng ảnh JPEG hoặc RAW+JPEG. Tắt X Acquire / FUJIFILM X Webcam trước khi mở app.
