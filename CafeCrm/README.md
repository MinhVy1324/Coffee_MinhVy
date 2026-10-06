# Cafe CRM bằng C#

Đồ án CRM quán cà phê gồm web ASP.NET Core MVC và app khách hàng Android .NET MAUI. Một server cung cấp cả MVC và REST API, cùng service nghiệp vụ và cơ sở dữ liệu. Đây là bản nền để phát triển và bảo vệ đồ án, chưa phải hệ thống vận hành thương mại.

## Chức năng đã có trong mã nguồn

- Khách hàng đăng ký có xác nhận mật khẩu, đăng nhập, xem tổng quan riêng, cập nhật hồ sơ và sở thích, đổi mật khẩu, gửi đánh giá và phản hồi sản phẩm/dịch vụ, xem trả lời, nhận lời mời, trả lời và xem lại khảo sát đã gửi.
- Nhân viên thêm, tra cứu, sửa hồ sơ và tiếp nhận phản hồi. Quản lý có thêm quyền khóa, lưu trữ khách, soạn và sửa bản nháp khảo sát, phát hành, đóng và xem kết quả; báo cáo tuổi, sở thích và hài lòng.
- Admin có menu riêng, tạo và vô hiệu hóa tài khoản, đổi vai trò nhân sự; tìm, lọc, sắp xếp sản phẩm và nhà cung cấp; thêm và sửa danh mục.
- Chặn phiên hiện có khi tài khoản bị khóa, lưu trữ hoặc đổi quyền. Bảo vệ form web bằng antiforgery; API nghiệp vụ chỉ nhận Bearer token. App lưu token qua SecureStorage.
- Unique và composite FK ngăn lời mời, câu trả lời trùng và đáp án thuộc câu hỏi hoặc khảo sát khác. Kiểm tra nghiệp vụ nằm trong service dùng chung.

## Phân quyền các chức năng CRM

**Trang khách hàng đã được hoàn thiện:** xem [hướng dẫn nghiệp vụ và giải thích toàn bộ luồng code khách hàng](docs/CafeCrm_KhachHang.md). Tài liệu có bản đồ file/route, giải thích controller–service–Identity–database, validation, phân quyền, bốn loại câu khảo sát và cách chạy test. Các phần quyết định nghiệp vụ trong code đã có comment tiếng Việt.

Kết quả kiểm thử bản hiện tại trên database SQLite riêng: [70 kiểm tra nghiệp vụ khách](docs/customer-verification.json), [30 kiểm tra trình duyệt ở kích thước máy tính/điện thoại](docs/customer-browser-verification.json) và [61 kiểm tra hồi quy CRM](docs/crm-verification.json) đều đạt. Server, Contracts và Client build thành công. Chưa xác nhận runtime SQL Server hoặc APK trong đợt cập nhật trang khách này.

Đây là cách phân quyền được triển khai trong dự án: nhân viên làm nghiệp vụ tại quầy, quản lý có các quyền đó và thêm quyền quản trị nghiệp vụ. Admin quản trị tài khoản/danh mục, không mặc nhiên là quản lý CRM.

| Chức năng                                  | Nhân viên (Staff) | Quản lý (Manager) | Lý do                                                   |
| ------------------------------------------ | ----------------- | ----------------- | ------------------------------------------------------- |
| Thêm, tra cứu, sửa khách hàng              | Có                | Có                | Công việc tiếp nhận và chăm sóc khách tại quầy          |
| Xóa/lưu trữ khách hàng                     | Không             | Có                | Ảnh hưởng quyền đăng nhập và lịch sử nghiệp vụ          |
| Khóa/mở khóa tài khoản khách               | Không             | Có                | Quyết định hạn chế quyền truy cập của khách             |
| Tiếp nhận, trả lời, xử lý phản hồi         | Có                | Có                | Công việc chăm sóc khách hằng ngày                      |
| Báo cáo cơ cấu độ tuổi, sở thích, hài lòng | Không             | Có                | Hỗ trợ quyết định quản lý và phát triển sản phẩm        |
| Tạo/sửa bản nháp, gửi, đóng khảo sát       | Không             | Có                | Quản lý quyết định nội dung và nhóm khách được khảo sát |
| Thống kê kết quả khảo sát                  | Không             | Có                | Phân tích ý kiến để quyết định cải thiện dịch vụ        |

Khách hàng chỉ sửa hồ sơ của mình, gửi/xem phản hồi của mình và trả lời khảo sát đã được gửi đến tài khoản. Không có quyền xem danh sách khách hoặc báo cáo.

**Xóa là xóa mềm:** `Customer.IsDeleted = true` và `AppUser.IsDisabled = true`. Hồ sơ biến mất khỏi danh sách hiện tại, xuất hiện trong “Hồ sơ đã lưu trữ”, không thể đăng nhập/nhận khảo sát mới. Phản hồi và câu trả lời cũ được giữ lại. Khóa chỉ ngăn đăng nhập; hồ sơ vẫn ở danh sách hiện tại và có thể mở khóa. Mở khóa không khôi phục phiên cũ; khách cần đăng nhập lại.

### Cách chạy thử đủ các nghiệp vụ

1. Đăng nhập `staff@cafe.test` hoặc `manager@cafe.test`, mở **Khách hàng → Thêm khách hàng**. Sau khi tạo tài khoản, bổ sung số điện thoại, ngày sinh và sở thích tại trang hồ sơ.
2. Với quản lý, thử **Khóa/Mở khóa**; chọn **Xóa** để lưu trữ. Nhân viên không có các nút này và bị server từ chối nếu tự gửi request.
3. Đăng nhập tài khoản khách, mở **Phản hồi của tôi**, chọn sản phẩm, chấm điểm và gửi góp ý. Nhân viên/quản lý mở **Phản hồi**, trả lời và chuyển trạng thái; khách thấy câu trả lời trong lịch sử của mình.
4. Quản lý mở **Báo cáo**. Cơ cấu tuổi/sở thích chỉ tính khách đang hoạt động; thiếu ngày sinh được thống kê riêng. Một khách chọn nhiều sở thích nên tổng tỷ lệ có thể vượt 100%.
5. Quản lý mở **Khảo sát → Tạo khảo sát**, thêm/xóa câu hỏi và lưu nháp. Câu lựa chọn cần 2–10 đáp án (mỗi dòng một đáp án); có thể dùng thang điểm 1–5 hoặc văn bản.
6. Chọn **Gửi khảo sát**, gửi tất cả khách hoạt động hoặc chọn từng khách. Lời mời xuất hiện trong hộp khảo sát của tài khoản khách, **không gửi email**. Không gửi trùng, không gửi cho khách đã khóa/lưu trữ. Chọn chế độ gửi cụ thể mà chưa chọn ai sẽ báo lỗi.
7. Khách mở **Khảo sát của tôi**, trả lời một lần trước hạn. Quản lý mở **Xem kết quả** để xem tỷ lệ tham gia, lượt chọn, điểm trung bình và góp ý văn bản. Khảo sát đã phát hành không sửa cấu trúc câu hỏi; đã đóng/hết hạn không nhận thêm câu trả lời.

### Đọc các comment giải thích code

- `src/CafeCrm.Contracts/Contracts.cs`: ý nghĩa bốn vai trò và dữ liệu trao đổi.
- `src/CafeCrm.Server/Controllers/WebControllers.cs`: từng chức năng 1–6 và quyền tương ứng. `[Authorize(Roles = "Manager,Staff")]` nghĩa là Manager **hoặc** Staff; action thêm `[Authorize(Roles = "Manager")]` chỉ cho quản lý.
- `src/CafeCrm.Server/Controllers/ApiControllers.cs`: bảo vệ cùng nghiệp vụ cho client/app bằng Bearer token.
- `src/CafeCrm.Server/Services/CrmServices.cs`: xóa mềm, khóa phiên, chống ghi đè, gửi khảo sát và công thức thống kê.
- `src/CafeCrm.Server/Program.cs`: chặn cookie/token cũ ngay khi khóa hoặc đổi quyền.
- `src/CafeCrm.Server/wwwroot/js/survey-editor.js`: đánh lại chỉ số câu hỏi sau khi xóa để MVC nhận đủ danh sách.
- `src/CafeCrm.Client/CrmApiClient.cs`: hợp đồng lưu token, dependency injection và khóa làm mới phiên.

## Trạng thái kiểm tra

Web/server, Contracts và Client đã build. Bộ `tests/smoke.py` chạy thực tế trên SQLite và kiểm tra đăng nhập, quyền, phản hồi, khảo sát, thống kê, khóa phiên, XSS và CSRF. Kết quả cuối cùng nằm ở `docs/verification.json`.

Bộ `tests/crm-workflows.mjs` kiểm tra 61 tình huống của các nghiệp vụ trên cả form MVC và API; kết quả nằm ở `docs/crm-verification.json`. Không cần cài npm package. Dùng database kiểm thử riêng vì bộ kiểm tra sẽ tạo, khóa và lưu trữ khách thử nghiệm.

Chạy server kiểm thử từ thư mục `CafeCrm` trong một terminal:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --project src/CafeCrm.Server --no-launch-profile -- --urls http://127.0.0.1:5261 --DatabaseProvider Sqlite --ConnectionStrings:Sqlite "Data Source=crm-test.db"
```

Trong terminal khác, cũng ở thư mục `CafeCrm`:

```powershell
node tests/crm-workflows.mjs http://127.0.0.1:5261 docs/crm-verification.json
```

Giao diện mới cũng đã đạt 28 kiểm tra trực tiếp trên Edge ở kích thước máy tính và điện thoại, gồm thêm/xóa câu hỏi, chọn người nhận, trả lời khảo sát và xử lý trường bắt buộc. Kết quả nằm ở `docs/crm-browser-verification.json`.

App Android có mã nguồn và hướng dẫn, chưa build APK hoặc chạy trên thiết bị trong môi trường bàn giao. SQL Server có EF migration và script DDL; chưa kiểm tra kết nối SQL Server thực tế. Cần hoàn thành hai kiểm tra này trên máy Windows của sinh viên.

## Tệp thiết kế và lịch sử Git

Thư mục `docs` chứa báo cáo Word 30 trang, 12 trang sơ đồ chỉnh sửa bằng diagrams.net, phác thảo HTML, 14 hình minh họa, script SQL Server và kết quả kiểm tra. HTML là bản mô phỏng giao diện; web thực tế chạy từ project Server.

Gói ZIP có `CafeCrm_History.bundle` bên cạnh thư mục `CafeCrm`, lưu commit bàn giao ban đầu. Để khôi phục repository có lịch sử, mở terminal tại thư mục giải nén rồi chạy:

```powershell
git clone CafeCrm_History.bundle CafeCrm-Git
```

Có thể mở trực tiếp thư mục `CafeCrm` để chạy mà không cần khôi phục Git. Khi tự phát triển, tạo commit sau mỗi nhóm chức năng đã chạy và kiểm tra.

## Môi trường

1. Cài .NET 10 SDK bản ổn định mới nhất. Với Windows dùng Visual Studio tương thích .NET 10 và workload ASP.NET/web; hoặc VS Code cùng công cụ C# chính thức. Không dùng bản Visual Studio cũ chỉ hỗ trợ .NET 8.
2. Khi làm app cài workload .NET MAUI, Android SDK, JDK và Android emulator theo hướng dẫn Microsoft: https://learn.microsoft.com/dotnet/maui/get-started/installation?view=net-maui-10.0
3. SQL Server LocalDB hoặc SQL Server Express cùng SSMS cho mô hình CSDL chính. Chạy thử nhanh bằng SQLite không cần cài SQL Server.
4. Git để lưu phiên bản. Không commit mật khẩu triển khai, database thật, bin/obj hoặc APK có khóa ký riêng.

## Chạy web nhanh với SQLite

Mở terminal tại thư mục `CafeCrm` đã giải nén:

```powershell
dotnet restore src/CafeCrm.Server/CafeCrm.Server.csproj
dotnet run --project src/CafeCrm.Server
```

Mở http://localhost:5240 hoặc https://localhost:7240. Nếu cần, chạy `dotnet dev-certs https --trust` trên máy phát triển. Cấu hình profile chạy đã đặt môi trường Development; server tự tạo database demo và bốn tài khoản.

| Vai trò    | Email              | Mật khẩu demo |
| ---------- | ------------------ | ------------- |
| Admin      | admin@cafe.test    | CafeDemo@123  |
| Quản lý    | manager@cafe.test  | CafeDemo@123  |
| Nhân viên  | staff@cafe.test    | CafeDemo@123  |
| Khách hàng | customer@cafe.test | CafeDemo@123  |

Các tài khoản này chỉ được tạo khi `ASPNETCORE_ENVIRONMENT=Development`. Server Production không tạo chúng. Một tài khoản ở một vai trò, khách hàng và nhân sự dùng tài khoản riêng. Admin không mặc nhiên vào nghiệp vụ CRM.

## Chuyển sang SQL Server

Không dùng database SQLite demo làm đầu vào migration SQL Server. Hai provider có schema độc lập.

```powershell
dotnet tool install --global dotnet-ef --version 10.0.9
cd src/CafeCrm.Server
dotnet user-secrets set "ConnectionStrings:SqlServer" "Server=(localdb)\MSSQLLocalDB;Database=CafeCrm;Trusted_Connection=True;TrustServerCertificate=True"
dotnet ef database update
$env:DatabaseProvider="SqlServer"
dotnet run
```

`Data/DesignTimeFactory.cs` luôn tạo migration SQL Server. Có sẵn migration InitialCreate trong `Data/Migrations`. Script `docs/CafeCrm_SQLServer.sql` được sinh từ migration này và có bảng lịch sử migration. Dùng migration hoặc script một cách nhất quán; không chạy lặp hai cách trên một database đang có dữ liệu.

Khi đổi model, tạo migration kế tiếp bằng `dotnet ef migrations add TenThayDoi` rồi `dotnet ef database update`. Không dùng `EnsureCreated` trên SQL Server. Hạn chế thay đổi model khi đã có dữ liệu báo cáo; sao lưu trước migration.

## Chạy Android

```powershell
dotnet workload install maui-android
dotnet restore src/CafeCrm.Mobile/CafeCrm.Mobile.csproj
dotnet build src/CafeCrm.Mobile/CafeCrm.Mobile.csproj -f net10.0-android -c Debug
```

Chọn Android emulator trong IDE và chạy project `CafeCrm.Mobile`. Server web phải đang chạy trên cùng máy tại http://localhost:5240. App Debug gọi `http://10.0.2.2:5240/`, là địa chỉ Android emulator dùng để truy cập máy chủ trên máy phát triển.

Điện thoại thật dùng địa chỉ IP LAN của máy chủ, cùng mạng Wi-Fi và cổng server phải được mở theo cấu hình mạng của máy. Khi đó thay BaseAddress trong `MauiProgram.cs`; không dùng 10.0.2.2. Không bỏ kiểm tra chứng chỉ TLS. Bản Release bắt buộc thay địa chỉ `https://your-cafe-server.example/` bằng HTTPS server thật. Manifest Release không cho HTTP cleartext; Debug có manifest riêng cho emulator.

## Cấu trúc

| Project hoặc thư mục       | Vai trò                                     |
| -------------------------- | ------------------------------------------- |
| CafeCrm.Contracts          | DTO, enum và vai trò dùng chung             |
| CafeCrm.Server/Data        | Entity, DbContext, seed và migration        |
| CafeCrm.Server/Services    | Quy tắc và transaction nghiệp vụ            |
| CafeCrm.Server/Controllers | Web MVC và API theo vai trò                 |
| CafeCrm.Server/Views       | Giao diện Admin, CRM và portal khách        |
| CafeCrm.Client             | HttpClient, token và refresh dùng cho app   |
| CafeCrm.Mobile             | App Android, trang C# native, SecureStorage |
| tests                      | Kiểm tra tích hợp tự động                   |
| docs                       | Thiết kế, sơ đồ, SQL và kết quả kiểm tra    |

`CafeCrm.Web.slnx` chỉ chứa server, client và contracts để build web không bị yêu cầu Android workload. `CafeCrm.slnx` có đủ app Android, dùng sau khi cài workload.

## Quy tắc dữ liệu

- Email được Identity kiểm tra duy nhất; không lưu mật khẩu dạng rõ. Phone nếu cung cấp phải có 10 số, bắt đầu bằng 0, và không trùng trong khách chưa lưu trữ.
- Ngày sinh là dữ liệu tùy chọn; nhóm chưa cung cấp ngày sinh xuất hiện riêng trong thống kê. Không suy ra ngày sinh hoặc sở thích từ dữ liệu không có.
- Xóa khách là lưu trữ `IsDeleted=true` và khóa tài khoản, giữ lịch sử phản hồi và khảo sát. Xóa account trong Admin là vô hiệu hóa account. Nếu giảng viên yêu cầu xóa vật lý, cần chốt lại quy tắc giữ hoặc xóa lịch sử trước khi thay schema.
- Khảo sát Published không sửa cấu trúc câu hỏi hoặc đáp án. Hết hạn hoặc Closed không được nhận câu trả lời. Khách chỉ đọc và trả lời khảo sát có lời mời của mình, một lần mỗi lời mời.
- Câu SingleChoice chọn một; MultipleChoice chọn một hoặc nhiều; Rating từ 1 đến 5; Text tối đa 2000 ký tự. Câu bắt buộc không được bỏ trống.
- Hồ sơ, phản hồi, khảo sát có `ConcurrencyToken`. Sửa trên phiên bản cũ nhận 409, phải tải lại.
- Mẫu số tỷ lệ tham gia là số lời mời, mẫu số tỷ lệ lựa chọn là số người trả lời câu đó. Câu nhiều lựa chọn có thể có tổng tỷ lệ vượt 100%. Trung bình không có dữ liệu trả null, giao diện hiển thị chưa có.
- UTC dùng khi lưu timestamp; giao diện và báo cáo tuổi dùng múi giờ Việt Nam UTC+7.
- Đăng xuất app thu hồi toàn bộ phiên của tài khoản và xóa token cục bộ. Web đăng xuất phiên trình duyệt hiện tại.

## Thứ tự triển khai tiếp

| Tuần tham khảo | Việc làm                                    | Điều kiện hoàn thành                               |
| -------------- | ------------------------------------------- | -------------------------------------------------- |
| 1              | Chốt use case, ERD, chạy server và Git      | Bốn vai trò đăng nhập đúng màn hình                |
| 2              | Hồ sơ, sở thích, tìm khách, khóa và lưu trữ | Cập nhật đồng bộ web/API, token cũ bị chặn         |
| 3              | Phản hồi và xử lý                           | Khách nhìn thấy câu trả lời của nhân viên          |
| 4              | Khảo sát, lời mời, bốn kiểu câu             | Không nhận câu trùng hoặc đáp án sai               |
| 5              | Android kết nối và chạy các luồng khách     | Demo trên emulator, lưu token an toàn              |
| 6              | Báo cáo, giao diện, SQL Server              | Số liệu đúng và migration chạy được                |
| 7              | Kiểm tra lỗi, tài liệu, đóng gói            | Chạy demo từ database sạch và viết báo cáo kết quả |
| 8              | Dự phòng và mở rộng nếu còn thời gian       | Ưu tiên sửa lỗi trước chức năng mới                |

Lịch này là ước lượng cho một sinh viên, không phải thời hạn cam kết. Đặt món, POS, tích điểm, voucher, email/SMS, ảnh phản hồi, nhiều chi nhánh và phân tích RFM nằm ngoài phần cốt lõi. Chỉ thêm sau khi các luồng trong ảnh yêu cầu đã chạy và được kiểm tra.

## Kiểm tra và demo

```powershell
dotnet build CafeCrm.Web.slnx
python tests/smoke.py http://localhost:5240
node tests/customer-workflows.mjs http://127.0.0.1:5263 docs/customer-verification.json
```

Kiểm tra dùng tài khoản tổng hợp và tạo dữ liệu thử nghiệm. Chạy trên Development với database thử riêng. Script tự tạo tài khoản mới nên có thể chạy lại; không dùng database khách thật.

Demo: khách đăng ký trên app hoặc portal, sửa sở thích, gửi phản hồi; nhân viên trả lời; quản lý tạo khảo sát về món mới và gửi vào tài khoản; khách trả lời; quản lý xem thống kê; khóa tài khoản và chứng minh token cũ bị từ chối; đăng nhập Admin để chứng minh giao diện riêng và lọc danh mục.

## Phần cần hoàn thiện trước khi triển khai thật

Build và kiểm tra APK trên thiết bị; chạy migration và kiểm tra transaction đồng thời trên SQL Server; phân trang danh sách lớn; bổ sung xác minh email, đổi mật khẩu bắt buộc lần đầu cho tài khoản do nhân viên tạo và khôi phục mật khẩu bằng kênh đã xác minh; thử nghiệm khả năng truy cập giao diện; kiểm tra sao lưu/khôi phục; cấu hình HTTPS, khóa Data Protection và bí mật server bền vững. Đổi mật khẩu tự nguyện, thông báo lỗi và giữ dữ liệu form khách đã có. Không có SMS/email hoặc thanh toán thật trong bản này.

## Tài liệu chính thức

- .NET support: https://learn.microsoft.com/dotnet/core/releases-and-support
- .NET MAUI: https://learn.microsoft.com/dotnet/maui/what-is-maui?view=net-maui-10.0
- ASP.NET Core Identity: https://learn.microsoft.com/aspnet/core/security/authentication/identity?view=aspnetcore-10.0
- Identity bearer tokens không phải JWT: https://learn.microsoft.com/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0
