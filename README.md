# 📸 SnapTranslate - Chụp màn hình rồi dịch, thế thôi!

[![Target Framework](https://img.shields.io/badge/.NET-8.0--windows-blue.svg?style=flat-color)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-lightgrey.svg?style=flat-color)](#)
[![License](https://img.shields.io/badge/License-MIT-green.svg?style=flat-color)](#)

Đã bao giờ bạn lướt web, đọc truyện tranh, xem video hay đang ngồi debug mà bắt gặp một đoạn text tiếng nước ngoài nhưng lại không thể bôi đen để copy chưa? Lười gõ lại từng chữ để dịch thì làm sao?

Đó là lý do mình viết ra **SnapTranslate**. Chỉ cần bấm `Alt + Space`, quét bôi đen vùng màn hình chứa chữ, và bản dịch sẽ hiện ra ngay lập tức. 

App không có quảng cáo, không thu thập dữ liệu. Mình lấy ý tưởng từ tính năng "Circle to Search" trên điện thoại và mang nó lên Windows, tối ưu cho gọn nhẹ và tiện lợi nhất có thể.

---

## 🛠️ Tính năng chính

### 1. Phím tắt Global (`Alt + Space`)
App sử dụng Low-Level Keyboard Hook (`WH_KEYBOARD_LL`) của Windows để bắt phím tắt. Khi bạn bấm `Alt + Space`, SnapTranslate sẽ phản hồi ngay lập tức, làm mờ màn hình để bạn quét chữ mà không lo bị khựng hay xung đột với các phần mềm khác.

### 2. Bóc tách chữ (OCR) siêu chuẩn qua Google Lens
Thay vì tích hợp mấy API trả phí đắt đỏ của Google Cloud hay Azure, app giao tiếp thẳng với dịch vụ **Google Lens** thông qua định dạng Protobuf. Kết quả là chúng ta có một bộ OCR cực kỳ chính xác, nhận diện tốt bố cục dòng, và quan trọng nhất là hoàn toàn miễn phí.
(nếu bị fix thì tui cũng chịu không biết có dịch vụ nào khác tương đương đâu :( )

### 3. Hỗ trợ nhiều nguồn dịch
*   **Google Translate:** Nhanh, gọn, miễn phí, không giới hạn. Mở app lên là xài luôn không cần setup thêm.
*   **DeepSeek API:** Phù hợp để dịch truyện tranh (manga/manhua) hoặc tài liệu cần văn phong mượt mà tự nhiên. Giao diện có tích hợp sẵn bảng thống kê Token (`Total Tokens`, `Cache Hit`, `Cache Miss`) để anh em tiện theo dõi chi phí API.
*   **Local LLM:** Dành cho anh em thích vọc vạch AI offline hoặc có sẵn card đồ họa mạnh. App hỗ trợ kết nối với LM Studio, Ollama hoặc bất kỳ API nào tương thích với OpenAI.

### 4. Tương thích đa màn hình & Scale DPI
Dù bạn dùng 1 màn hình laptop hay cắm 2-3 màn rời với độ phân giải và tỷ lệ scale (DPI) khác nhau, overlay chụp ảnh vẫn sẽ tự động căn chỉnh chuẩn xác. Không có tình trạng bị lệch chuột hay ảnh chụp bị vỡ hạt.

### 5. Xử lý triệt để lỗi Windows Clipboard
Anh em dev chắc hay gặp cảnh Windows Clipboard thi thoảng bị kẹt do các app khác chiếm dụng. Mình đã viết lại cơ chế retry và fallback kết hợp cả WPF lẫn WinForms để đảm bảo việc copy text luôn hoạt động mượt mà, kèm theo cái thông báo "Đã sao chép" nhỏ gọn.

### 6. Gọn nhẹ, nằm im ở System Tray
Lúc không xài, app sẽ thu nhỏ xuống khay hệ thống, ăn cực kỳ ít RAM và không làm phiền đến công việc của bạn.

---

## 📂 Cấu trúc dự án (Cho anh em Dev)

Project được viết theo mô hình MVVM cơ bản, anh em có thể tham khảo sơ đồ bên dưới:
```text
SnapTranslate/
├── Models/                    # Data models & configs
│   ├── AppSettings.cs         # Đọc/ghi file config.json & chỉnh Registry chạy ngầm
│   ├── LensProtos.cs          # File Protobuf để gọi Google Lens
│   └── LensOverlayServer...   # Các class phụ trợ xử lý request/response
│
├── ViewModels/                # Logic điều khiển UI
│   └── MainViewModel.cs       # Xử lý Data Binding chính cho MainWindow
│
├── Services/                  # Nơi chứa các service xử lý nghiệp vụ
│   ├── LensOcrService.cs      # Gọi Google Lens bóc text từ ảnh
│   ├── LanguageDetector.cs    # Check ngôn ngữ nguồn (chạy offline)
│   ├── GoogleTranslateService.cs   # Xử lý dịch qua Google
│   ├── DeepSeekTranslateService.cs # Xử lý dịch qua API DeepSeek
│   └── LlmOpenAiService.cs         # Xử lý gọi Local LLM
│
├── Win32/                     # P/Invoke giao tiếp với Windows API
│   └── NativeMethods.cs       # Hook phím, check DPI màn hình, giấu cửa sổ khỏi Alt-Tab
│
├── Fonts/ & Resources/        # Chứa assets và font Outfit
├── MainWindow.xaml (.cs)      # UI chính hiển thị text và kết quả dịch
├── SnippingWindow.xaml (.cs)  # Cửa sổ overlay mờ để quét vùng chọn
├── SettingsWindow.xaml (.cs)  # Cửa sổ setting API key, URL...
└── SnapTranslate.Tests/       # Unit tests (MSTest & Moq)

```

---

## 🚀 Build và chạy thử

### Yêu cầu:

* OS: **Windows 10** hoặc **Windows 11**.
* Công cụ: **.NET 8.0 SDK** và IDE tùy thích (Visual Studio 2022, Rider, VS Code).

### Các bước:

1. **Clone code về:**
```bash
git clone [https://github.com/YOUR_USERNAME/SnapTranslate.git](https://github.com/YOUR_USERNAME/SnapTranslate.git)
cd SnapTranslate

```


2. **Restore packages:**
```bash
dotnet restore

```


3. **Run app:**
```bash
dotnet run --project SnapTranslate.csproj

```


4. **Chạy unit test (tùy chọn):**
```bash
dotnet test

```



---

## 📖 Hướng dẫn sử dụng nhanh

1. **Chạy app**: Icon của **SnapTranslate** sẽ xuất hiện dưới System Tray (góc phải dưới màn hình).
2. **Sử dụng**: Bấm **`Alt + Space`** -> kéo chuột để bôi đen vùng có chữ -> thả chuột. Text và bản dịch sẽ hiện ra.
3. **Thao tác thêm**:
* Bấm nút **Vi** hoặc **En** trên tab để đổi nhanh ngôn ngữ đích.
* Đổi nguồn dịch (Google/DeepSeek/Local) ở ComboBox góc trên bên phải.
* Bạn có thể sửa text trực tiếp ở ô văn bản gốc, app sẽ tự động dịch lại sau khi bạn dừng gõ khoảng 800ms.


4. **Settings**: Click vào icon bánh răng để điền API Key, sửa link Local LLM hoặc bật chế độ tự khởi động cùng Windows.

---

## ⚙️ Cấu hình `config.json`

File config sẽ được lưu ở thư mục Roaming của user:
`C:\Users\<Tên_User>\AppData\Roaming\SnapTranslate\config.json`

App sẽ tự tạo file này, nhưng bạn cũng có thể mở lên sửa trực tiếp nếu muốn:

```json
{
  "DeepSeekApiKey": "sk-your-deepseek-key",
  "WindowWidth": 500,
  "WindowHeight": 0,
  "SelectedEngineIndex": 0,
  "LocalLlmUrl": "http://localhost:1234/v1/chat/completions",
  "StartWithWindows": true
}

```

---

## 🤝 Đóng góp (Contributions)

Rất hoan nghênh anh em đóng góp để hoàn thiện app (tối ưu code, thêm tính năng, fix bug...). Cứ thoải mái fork repo, tạo branch (`git checkout -b feature/AddSomethingNice`) và tạo Pull Request cho mình nhé.

---

## 📄 License

Project sử dụng giấy phép **MIT License** - chi tiết xem tại file `LICENSE`.

Hy vọng **SnapTranslate** sẽ giúp anh em đỡ mỏi tay gõ phím hơn. Nếu thấy tool hữu ích thì để lại cho dự án một ⭐️ nhé!

```

```