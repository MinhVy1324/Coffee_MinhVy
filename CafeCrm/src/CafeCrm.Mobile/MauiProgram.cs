using CafeCrm.Client;
using Microsoft.Extensions.DependencyInjection;

namespace CafeCrm.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() {
        var builder = MauiApp.CreateBuilder(); builder.UseMauiApp<App>();
        builder.Services.AddSingleton<ITokenStore,MauiTokenStore>();
        builder.Services.AddSingleton(service => new CrmApiClient(new HttpClient {
            // Android emulator routes 10.0.2.2 to the developer machine. Release requires your HTTPS server.
#if DEBUG
            BaseAddress = new Uri("http://10.0.2.2:5240/"),
#else
            BaseAddress = new Uri("https://your-cafe-server.example/"),
#endif
            Timeout = TimeSpan.FromSeconds(20)
        },service.GetRequiredService<ITokenStore>()));
        builder.Services.AddSingleton<MainPage>(); return builder.Build();
    }
}
public sealed class MauiTokenStore : ITokenStore
{
    private const string Key = "cafecrm.session";
    public Task<string?> Read() => SecureStorage.Default.GetAsync(Key);
    public Task Write(string value) => SecureStorage.Default.SetAsync(Key,value);
    public Task Clear() { SecureStorage.Default.Remove(Key); return Task.CompletedTask; }
}
