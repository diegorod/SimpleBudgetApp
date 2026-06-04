using Microsoft.JSInterop;
using MudBlazor;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

public class GoogleDriveService
{
    private readonly IJSRuntime _js;
    private readonly HttpClient _httpClient;
    private readonly ISnackbar _snackbar;

    private string? _accessToken;
    private string? _fileId;
    private string? _folderId;

    private const string FILE_NAME = "simpleBudget.json";
    private const string FOLDER_NAME = "SimpleBudget";
    private const string LOCALSTORAGE_KEY = "simpleBudget_data";

    public event Action? OnBudgetChanged;
    public void InvokeOnBudgetChanged() => OnBudgetChanged?.Invoke();

    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    public Budget CurrentBudget { get; set; } = new();

    public GoogleDriveService(IJSRuntime js, ISnackbar snackbar, HttpClient httpClient)
    {
        _js = js;
        _snackbar = snackbar;
        _httpClient = httpClient;
    }

    public async Task InitializeGoogleAuth(string clientId)
    {
        await _js.InvokeVoidAsync("GoogleAuth.init", clientId);
        await _js.InvokeVoidAsync("eval", $"window.googleClientId = '{clientId}';");
    }

    public async Task LoginAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(CurrentBudget.GoogleClientId)) return;

            await InitializeGoogleAuth(CurrentBudget.GoogleClientId);

            var token = await _js.InvokeAsync<string>("GoogleAuth.requestToken",
                "https://www.googleapis.com/auth/drive.file");

            if (!string.IsNullOrEmpty(token))
            {
                _accessToken = token;
                _snackbar.Add("Connected to Google Drive", Severity.Success);
                await SaveAsync();
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            _snackbar.Add($"Login failed: {ex.Message}", Severity.Error);
        }
    }

    public async Task TryAutoConnectAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken)) return;
        if (string.IsNullOrWhiteSpace(CurrentBudget.GoogleClientId)) return;

        try
        {
            await InitializeGoogleAuth(CurrentBudget.GoogleClientId);
            var token = await _js.InvokeAsync<string>("GoogleAuth.requestToken",
    "https://www.googleapis.com/auth/drive.file");

            if (!string.IsNullOrEmpty(token))
            {
                _accessToken = token;
                _snackbar.Add("Auto-connected to Google Drive", Severity.Success);
            } else
            {
                CurrentBudget.GoogleClientId = null;
                await SaveAsync();
                await LoadAsync();
                _snackbar.Add("Auto-login to Google Drive failed. Please connect manually.", Severity.Info);
            }
        }
        catch
        {
            // Silent fail - user can connect manually
        }
    }

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(_accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return request;
    }

    // Always save locally first
    private async Task SaveToLocalAsync()
    {
        var json = JsonSerializer.Serialize(CurrentBudget, new JsonSerializerOptions { WriteIndented = true });
        await _js.InvokeVoidAsync("localStorage.setItem", LOCALSTORAGE_KEY, json);
    }

    public async Task SaveAsync()
    {
        // 1. Always save locally first
        await SaveToLocalAsync();

        // 2. Try to sync with Google Drive if connected
        if (!string.IsNullOrEmpty(_accessToken))
        {
            await SaveToGoogleDriveAsync();
        }
        else
        {
            _snackbar.Add("💾 Saved locally (Google Drive not connected)", Severity.Info);
        }

        OnBudgetChanged?.Invoke();
    }

    private async Task SaveToGoogleDriveAsync()
    {
        try
        {
            var folderId = await GetOrCreateFolderAsync();
            var json = JsonSerializer.Serialize(CurrentBudget, new JsonSerializerOptions { WriteIndented = true });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            if (string.IsNullOrEmpty(_fileId))
            {
                var createUrl = "https://www.googleapis.com/upload/drive/v3/files?uploadType=media";
                var request = CreateAuthorizedRequest(HttpMethod.Post, createUrl);
                request.Content = content;

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                    _fileId = result.GetProperty("id").GetString();
                    _snackbar.Add($"Synced to Google Drive /{FOLDER_NAME}/{FILE_NAME}", Severity.Success);
                }
            }
            else
            {
                var updateUrl = $"https://www.googleapis.com/upload/drive/v3/files/{_fileId}?uploadType=media";
                var request = CreateAuthorizedRequest(HttpMethod.Patch, updateUrl);
                request.Content = content;

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                    _snackbar.Add($"Synced to Google Drive /{FOLDER_NAME}/{FILE_NAME}", Severity.Success);
            }
        }
        catch (Exception ex)
        {
            _snackbar.Add($"Google Drive sync failed: {ex.Message}. Saved locally only.", Severity.Warning);
        }
    }

    public async Task LoadAsync()
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            await LocalFallbackLoad();
            return;
        }

        try
        {
            var folderId = await GetOrCreateFolderAsync();
            var query = $"name='{FILE_NAME}' and '{folderId}' in parents and trashed=false";
            var listUrl = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(query)}&fields=files(id,name)";

            var listRequest = CreateAuthorizedRequest(HttpMethod.Get, listUrl);
            var listResponse = await _httpClient.SendAsync(listRequest);

            if (listResponse.IsSuccessStatusCode)
            {
                var result = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
                var files = result.GetProperty("files");

                if (files.GetArrayLength() > 0)
                {
                    _fileId = files[0].GetProperty("id").GetString();

                    var downloadUrl = $"https://www.googleapis.com/drive/v3/files/{_fileId}?alt=media";
                    var downloadRequest = CreateAuthorizedRequest(HttpMethod.Get, downloadUrl);

                    var fileResponse = await _httpClient.SendAsync(downloadRequest);

                    if (fileResponse.IsSuccessStatusCode)
                    {
                        var json = await fileResponse.Content.ReadAsStringAsync();
                        CurrentBudget = JsonSerializer.Deserialize<Budget>(json) ?? new Budget();
                        _snackbar.Add("Loaded from Google Drive", Severity.Success);
                    }
                }
                else
                {
                    _snackbar.Add("No cloud file found. Using local data.", Severity.Info);
                }
            }
        }
        catch (Exception ex)
        {
            _snackbar.Add($"Load from Google failed. Using local backup.", Severity.Warning);
        }

        // Always ensure local copy is up to date
        await SaveToLocalAsync();
        OnBudgetChanged?.Invoke();
    }

    private async Task LocalFallbackLoad()
    {
        var json = await _js.InvokeAsync<string>("localStorage.getItem", LOCALSTORAGE_KEY);
        if (!string.IsNullOrEmpty(json))
            CurrentBudget = JsonSerializer.Deserialize<Budget>(json) ?? new Budget();
    }

    private async Task<string> GetOrCreateFolderAsync()
    {
        if (!string.IsNullOrEmpty(_folderId)) return _folderId;

        // Search for folder
        var query = $"name='{FOLDER_NAME}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
        var listUrl = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(query)}&fields=files(id)";

        var listRequest = CreateAuthorizedRequest(HttpMethod.Get, listUrl);
        var listResponse = await _httpClient.SendAsync(listRequest);

        if (listResponse.IsSuccessStatusCode)
        {
            var result = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
            var files = result.GetProperty("files");
            if (files.GetArrayLength() > 0)
            {
                _folderId = files[0].GetProperty("id").GetString();
                return _folderId!;
            }
        }

        // Create folder
        var folderMetadata = new { name = FOLDER_NAME, mimeType = "application/vnd.google-apps.folder" };
        var content = new StringContent(JsonSerializer.Serialize(folderMetadata), Encoding.UTF8, "application/json");

        var createRequest = CreateAuthorizedRequest(HttpMethod.Post, "https://www.googleapis.com/drive/v3/files");
        createRequest.Content = content;

        var createResponse = await _httpClient.SendAsync(createRequest);
        if (createResponse.IsSuccessStatusCode)
        {
            var result = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
            _folderId = result.GetProperty("id").GetString();
        }

        return _folderId ?? "";
    }

    public async Task DownloadBackupAsync()
    {
        var json = JsonSerializer.Serialize(CurrentBudget, new JsonSerializerOptions { WriteIndented = true });
        var fileName = $"budgetly-backup-{DateTime.Now:yyyy-MM-dd-HH-mm}.json";
        await _js.InvokeVoidAsync("downloadFile", fileName, json);
        _snackbar.Add($"Backup downloaded", Severity.Success);
    }
}