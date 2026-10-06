using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CafeCrm.Contracts;

namespace CafeCrm.Client;

public interface ITokenStore // interface giao diện 
{
    Task<string?> Read();
    Task Write(string value);
    Task Clear();
}
public sealed class ApiException(HttpStatusCode status, string message) : Exception(message)
{ public HttpStatusCode Status { get; } = status; }
public sealed class CrmApiClient(HttpClient http, ITokenStore store) //contructor 
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim refreshLock = new(1,1);//đây là mọt ổ lock cho phep xư li    
    private TokenResponse? tokens;
    public async Task<bool> RestoreSession() {
        var saved = await store.Read();
        if (saved == null) return false;
        try { tokens = JsonSerializer.Deserialize<TokenResponse>(saved,Json); return tokens != null; }
        catch (JsonException) { await store.Clear(); return false; }
    }
    public async Task Login(string email,string password) {
        using var response = await http.PostAsJsonAsync("api/auth/login",new LoginRequest(email,password));
        await Ensure(response);
        tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(Json) ?? throw new InvalidOperationException("Thiếu thông tin đăng nhập.");
        await store.Write(JsonSerializer.Serialize(tokens,Json));
    }
    public async Task Register(string email,string password,string name) {
        using var response = await http.PostAsJsonAsync("api/auth/register",new RegisterRequest(email,password,name)); await Ensure(response);
    }
    public async Task Logout() {
        try { await Send<object>(HttpMethod.Post,"api/auth/logout-all",new { }); }
        finally { tokens = null; await store.Clear(); }
    }
    public Task<ProfileDto> Profile() => Send<ProfileDto>(HttpMethod.Get,"api/customer/profile");
    public Task<LookupDto[]> Preferences() => Send<LookupDto[]>(HttpMethod.Get,"api/customer/preferences");
    public Task<ProductDto[]> Products() => Send<ProductDto[]>(HttpMethod.Get,"api/customer/products");
    public Task<object> SaveProfile(ProfileUpdate value) => Send<object>(HttpMethod.Put,"api/customer/profile",value);
    public Task<FeedbackDto[]> Feedback() => Send<FeedbackDto[]>(HttpMethod.Get,"api/customer/feedback");
    public Task<object> SendFeedback(FeedbackCreate value) => Send<object>(HttpMethod.Post,"api/customer/feedback",value);
    public Task<InvitationDto[]> Inbox() => Send<InvitationDto[]>(HttpMethod.Get,"api/customer/surveys");
    public Task<SurveyDto> Survey(Guid id) => Send<SurveyDto>(HttpMethod.Get,$"api/customer/surveys/{id}");
    public Task<object> Submit(Guid id,SubmitResponse value) => Send<object>(HttpMethod.Post,$"api/customer/surveys/{id}/responses",value);
    private async Task<T> Send<T>(HttpMethod method,string path,object? body = null) {
        var access = tokens?.AccessToken ?? throw new ApiException(HttpStatusCode.Unauthorized,"Hãy đăng nhập lại.");
        using var first = await Request(method,path,body,access);
        if (first.StatusCode != HttpStatusCode.Unauthorized) return await Read<T>(first);
        await refreshLock.WaitAsync();
        try {
            if (tokens?.AccessToken == access) {
                using var refreshed = await http.PostAsJsonAsync("api/auth/refresh",new RefreshRequest(tokens.RefreshToken));
                if (!refreshed.IsSuccessStatusCode) { tokens = null; await store.Clear(); throw new ApiException(HttpStatusCode.Unauthorized,"Phiên hết hiệu lực hoặc tài khoản bị khóa."); }
                tokens = await refreshed.Content.ReadFromJsonAsync<TokenResponse>(Json) ?? throw new ApiException(HttpStatusCode.Unauthorized,"Không thể làm mới phiên.");
                await store.Write(JsonSerializer.Serialize(tokens,Json));
            }
        } finally { refreshLock.Release(); }
        using var retry = await Request(method,path,body,tokens!.AccessToken); return await Read<T>(retry);
    }
    private Task<HttpResponseMessage> Request(HttpMethod method,string path,object? body,string access) {
        var request = new HttpRequestMessage(method,path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",access);
        if (body != null) request.Content = JsonContent.Create(body,body.GetType(),options:Json);
        return Execute(request);
    }
    private async Task<HttpResponseMessage> Execute(HttpRequestMessage request) { using(request) return await http.SendAsync(request); }
    private static async Task<T> Read<T>(HttpResponseMessage response) {
        await Ensure(response);
        if (response.StatusCode == HttpStatusCode.NoContent) return default!;
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
    private static async Task Ensure(HttpResponseMessage response) {
        if (response.IsSuccessStatusCode) return;
        var message = "Không thể thực hiện yêu cầu.";
        try {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (document.RootElement.TryGetProperty("title",out var title)) message = title.GetString() ?? message;
            if (document.RootElement.TryGetProperty("errors",out var errors)) message += " " + string.Join(" ",errors.EnumerateObject().SelectMany(x => x.Value.EnumerateArray().Select(a => a.GetString())));
        } catch (JsonException) { }
        throw new ApiException(response.StatusCode,message);
    }
}
