using CafeCrm.Client;
using CafeCrm.Contracts;

namespace CafeCrm.Mobile;

public sealed class MainPage : ContentPage
{
    private readonly CrmApiClient api;
    private readonly VerticalStackLayout body = new() { Spacing = 12, Padding = 24 };
    private bool initialized;
    private bool busy;
    private static readonly Color Green = Color.FromArgb("#255C47");
    public MainPage(CrmApiClient client) {
        api = client; Title = "Cafe CRM"; BackgroundColor = Color.FromArgb("#F6F5F1");
        Content = new ScrollView { Content = body };
    }
    protected override async void OnAppearing() {
        base.OnAppearing(); if (initialized) return; initialized = true;
        await Run(async () => { if (await api.RestoreSession()) await Profile(); else Login(); });
    }
    private async Task Run(Func<Task> action) {
        if (busy) return; busy = true;
        try { await action(); }
        catch (ApiException ex) {
            await DisplayAlertAsync("Cafe CRM",ex.Message,"OK");
            if (ex.Status == System.Net.HttpStatusCode.Unauthorized) Login();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) {
            await DisplayAlertAsync("Kết nối","Không kết nối được máy chủ. Hãy kiểm tra mạng rồi thử lại.","OK");
        } finally { busy = false; }
    }
    private void Reset(string title,bool navigation = true) {
        body.Children.Clear(); body.Add(new Label { Text = "CAFE CRM", FontSize = 14, TextColor = Green });
        body.Add(new Label { Text = title,FontSize = 28,FontAttributes = FontAttributes.Bold });
        if (!navigation) return;
        var nav = new HorizontalStackLayout { Spacing = 6 };
        nav.Add(Button("Hồ sơ",Profile)); nav.Add(Button("Góp ý",Feedback)); nav.Add(Button("Khảo sát",Inbox)); body.Add(nav);
        body.Add(Button("Đăng xuất",async () => { try { await api.Logout(); } finally { Login(); } }));
    }
    private Button Button(string title,Func<Task> action) {
        var button = new Button { Text = title,BackgroundColor = Green,TextColor = Colors.White,CornerRadius = 9 };
        button.Clicked += async (_,_) => { button.IsEnabled = false; try { await Run(action); } finally { button.IsEnabled = true; } }; return button;
    }
    private Entry Field(string label,string? value = null,bool password = false) {
        body.Add(new Label { Text = label }); var field = new Entry { Text = value,IsPassword = password }; body.Add(field); return field;
    }
    private void Login() {
        Reset("Chào ông đến với quán",false); var email = Field("Email"); email.Keyboard = Keyboard.Email;
        var password = Field("Mật khẩu",password:true);
        body.Add(Button("Đăng nhập",async () => { await api.Login(email.Text ?? "",password.Text ?? ""); await Profile(); }));
        body.Add(Button("Tạo tài khoản",() => { Register(); return Task.CompletedTask; }));
    }
    private void Register() {
        Reset("Đăng ký khách hàng",false); var name = Field("Họ tên"); var email = Field("Email"); email.Keyboard = Keyboard.Email;
        var password = Field("Mật khẩu",password:true);
        body.Add(new Label { Text = "Ít nhất 8 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt." });
        body.Add(Button("Đăng ký",async () => { await api.Register(email.Text ?? "",password.Text ?? "",name.Text ?? ""); await DisplayAlertAsync("Thành công","Tài khoản đã tạo. Hãy đăng nhập.","OK"); Login(); }));
        body.Add(Button("Quay lại",() => { Login(); return Task.CompletedTask; }));
    }
    private async Task Profile() {
        var profile = await api.Profile(); var preferences = await api.Preferences();
        Reset("Hồ sơ của tôi"); body.Add(new Label { Text = profile.Email,TextColor = Colors.Gray });
        var name = Field("Họ tên",profile.FullName); var phone = Field("Số điện thoại",profile.Phone); phone.Keyboard = Keyboard.Telephone;
        body.Add(new Label { Text = "Ngày sinh" }); var known = new CheckBox { IsChecked = profile.BirthDate != null };
        body.Add(new HorizontalStackLayout { Children = { known,new Label { Text = "Cung cấp ngày sinh",VerticalOptions = LayoutOptions.Center } } });
        var birthday = new DatePicker { Date = profile.BirthDate?.ToDateTime(TimeOnly.MinValue) ?? new DateTime(2000,1,1),MaximumDate = DateTime.Today }; body.Add(birthday);
        var address = Field("Địa chỉ",profile.Address); body.Add(new Label { Text = "Sở thích" });
        var checks = new Dictionary<int,CheckBox>();
        foreach (var p in preferences) { var check = new CheckBox { IsChecked = profile.PreferenceIds.Contains(p.Id) }; checks[p.Id] = check;
            body.Add(new HorizontalStackLayout { Children = { check,new Label { Text = p.Name,VerticalOptions = LayoutOptions.Center } } }); }
        body.Add(Button("Lưu hồ sơ",async () => {
            await api.SaveProfile(new(name.Text ?? "",string.IsNullOrWhiteSpace(phone.Text) ? null : phone.Text,
                known.IsChecked && birthday.Date is { } date ? DateOnly.FromDateTime(date) : null,address.Text,
                checks.Where(x => x.Value.IsChecked).Select(x => x.Key).ToArray(),profile.ConcurrencyToken));
            await DisplayAlertAsync("Đã lưu","Thông tin khách hàng đã cập nhật.","OK"); await Profile();
        }));
    }
    private async Task Feedback() {
        var products = await api.Products(); var feedback = await api.Feedback(); Reset("Góp ý cho quán");
        body.Add(new Label { Text = "Sản phẩm hoặc dịch vụ" }); var product = new Picker { Title = "Chọn sản phẩm" };
        product.Items.Add("Dịch vụ chung"); foreach(var p in products) product.Items.Add(p.Name); product.SelectedIndex = 0; body.Add(product);
        body.Add(new Label { Text = "Đánh giá từ 1 đến 5" }); var rating = new Picker { ItemsSource = new List<string> { "1","2","3","4","5" },SelectedIndex = 4 }; body.Add(rating);
        var content = new Editor { Placeholder = "Góp ý của ông",AutoSize = EditorAutoSizeOption.TextChanges,MinimumHeightRequest = 100,MaxLength = 2000 }; body.Add(content);
        body.Add(Button("Gửi phản hồi",async () => { await api.SendFeedback(new(product.SelectedIndex <= 0 ? null : products[product.SelectedIndex-1].Id,rating.SelectedIndex+1,content.Text ?? "")); await Feedback(); }));
        body.Add(new Label { Text = "Lịch sử phản hồi",FontSize = 21,FontAttributes = FontAttributes.Bold });
        foreach(var f in feedback) { body.Add(new Label { Text = $"{f.ProductName ?? "Dịch vụ"} · {f.Rating}/5 · {f.Status}",FontAttributes = FontAttributes.Bold }); body.Add(new Label { Text = f.Content });
            foreach(var r in f.Replies) body.Add(new Label { Text = "Quán trả lời: " + r.Content,TextColor = Green }); }
    }
    private async Task Inbox() {
        var invitations = await api.Inbox(); Reset("Khảo sát nhận được");
        if(invitations.Length == 0) body.Add(new Label { Text = "Ông chưa nhận được khảo sát nào." });
        foreach(var invitation in invitations) { body.Add(new Label { Text = invitation.Title,FontSize = 20,FontAttributes = FontAttributes.Bold });
            body.Add(new Label { Text = "Hạn: " + invitation.ClosesAtUtc.AddHours(7).ToString("dd/MM/yyyy HH:mm") });
            if(invitation.HasResponded) body.Add(new Label { Text = "Đã trả lời",TextColor = Green });
            else body.Add(Button("Mở khảo sát",() => Survey(invitation.SurveyId))); }
    }
    private async Task Survey(Guid id) {
        var survey = await api.Survey(id); Reset(survey.Title); body.Add(new Label { Text = survey.Description });
        var values = new List<Func<AnswerInput>>();
        foreach(var q in survey.Questions) {
            body.Add(new Label { Text = q.Text + (q.IsRequired ? " *" : ""),FontSize = 18,FontAttributes = FontAttributes.Bold });
            if(q.Kind == QuestionKind.Text) { var text = new Editor { AutoSize = EditorAutoSizeOption.TextChanges,MinimumHeightRequest = 70,MaxLength = 2000 }; body.Add(text);
                values.Add(() => new(q.Id,text.Text,null,[])); }
            else if(q.Kind == QuestionKind.Rating) { var picker = new Picker { Title = "Chọn điểm",ItemsSource = new List<string> { "1","2","3","4","5" } }; body.Add(picker);
                values.Add(() => new(q.Id,null,picker.SelectedIndex < 0 ? null : picker.SelectedIndex+1,[])); }
            else if(q.Kind == QuestionKind.SingleChoice) { var picker = new Picker { Title = "Chọn một đáp án",ItemsSource = q.Options.Select(x => x.Text).ToList() }; body.Add(picker);
                values.Add(() => new(q.Id,null,null,picker.SelectedIndex < 0 ? [] : [q.Options[picker.SelectedIndex].Id])); }
            else { var checks = new Dictionary<Guid,CheckBox>(); foreach(var option in q.Options) { var check = new CheckBox(); checks[option.Id] = check;
                    body.Add(new HorizontalStackLayout { Children = { check,new Label { Text = option.Text,VerticalOptions = LayoutOptions.Center } } }); }
                values.Add(() => new(q.Id,null,null,checks.Where(x => x.Value.IsChecked).Select(x => x.Key).ToArray())); }
        }
        body.Add(Button("Gửi câu trả lời",async () => { await api.Submit(id,new(values.Select(x => x()).ToArray())); await DisplayAlertAsync("Cảm ơn ông","Câu trả lời đã được ghi nhận.","OK"); await Inbox(); }));
    }
}
