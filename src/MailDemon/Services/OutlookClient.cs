using Azure.Core;
using Azure.Identity;
using MailDemon.Models;
using Microsoft.Graph;
using Message = Microsoft.Graph.Models.Message;

namespace MailDemon.Services;

/// <summary>Outlook / Microsoft 365 posta kutusunu Microsoft Graph üzerinden okur.</summary>
public class OutlookClient(IConfiguration config, ILogger<OutlookClient> logger)
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/Mail.Read", "offline_access"];

    private readonly string _authRecordPath = Path.Combine(DataDir.Get(config), "auth-record.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GraphServiceClient? _graph;
    private string? _graphKey;

    /// <summary>Cihaz kodu ile giriş bekleniyorsa kullanıcıya gösterilecek mesaj.</summary>
    public string? PendingDeviceCodeMessage { get; private set; }

    public string? SignedInAs { get; private set; }

    public async Task<List<MailItem>> GetRecentMailsAsync(AppSettings s, DateTimeOffset since, CancellationToken ct)
    {
        var graph = await GetGraphAsync(s, ct);

        var filter = $"receivedDateTime ge {since.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
        if (s.Scan.OnlyUnread) filter += " and isRead eq false";

        string[] select = ["id", "subject", "from", "toRecipients", "ccRecipients", "receivedDateTime", "body", "webLink", "hasAttachments", "importance"];
        string[] orderBy = ["receivedDateTime desc"];
        // Gövdeyi HTML yerine düz metin iste.
        const string prefer = "outlook.body-content-type=\"text\"";

        // ClientSecret modunda /users/{adres}, cihaz kodu modunda /me kullanılır.
        // İki uç noktanın sorgu tipleri farklı olduğu için yapılandırma iki kez yazılıyor.
        var page = s.Mailbox.AuthMode == MailboxAuthMode.ClientSecret
            ? await graph.Users[s.Mailbox.UserEmail].MailFolders[s.Mailbox.Folder].Messages.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = filter;
                rc.QueryParameters.Orderby = orderBy;
                rc.QueryParameters.Top = s.Scan.MaxMailsPerScan;
                rc.QueryParameters.Select = select;
                rc.Headers.Add("Prefer", prefer);
            }, ct)
            : await graph.Me.MailFolders[s.Mailbox.Folder].Messages.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = filter;
                rc.QueryParameters.Orderby = orderBy;
                rc.QueryParameters.Top = s.Scan.MaxMailsPerScan;
                rc.QueryParameters.Select = select;
                rc.Headers.Add("Prefer", prefer);
            }, ct);

        return (page?.Value ?? []).Select(ToMailItem).ToList();
    }

    private static MailItem ToMailItem(Message m) => new()
    {
        Id = m.Id ?? "",
        Subject = m.Subject ?? "(konu yok)",
        FromName = m.From?.EmailAddress?.Name ?? "",
        FromEmail = m.From?.EmailAddress?.Address ?? "",
        To = (m.ToRecipients ?? []).Select(r => r.EmailAddress?.Address ?? "").Where(a => a != "").ToList(),
        Cc = (m.CcRecipients ?? []).Select(r => r.EmailAddress?.Address ?? "").Where(a => a != "").ToList(),
        ReceivedAt = m.ReceivedDateTime ?? DateTimeOffset.UtcNow,
        Body = m.Body?.Content ?? "",
        WebLink = m.WebLink,
        HasAttachments = m.HasAttachments ?? false,
        Importance = m.Importance?.ToString(),
    };

    /// <summary>Ayarlar değiştiyse istemciyi yeniden oluşturur.</summary>
    public void Reset()
    {
        _graph = null;
        _graphKey = null;
        SignedInAs = null;
    }

    private async Task<GraphServiceClient> GetGraphAsync(AppSettings s, CancellationToken ct)
    {
        var m = s.Mailbox;
        if (string.IsNullOrWhiteSpace(m.ClientId))
            throw new InvalidOperationException("Ayarlar > Outlook bölümünde Client ID girilmemiş.");

        var key = $"{m.AuthMode}|{m.TenantId}|{m.ClientId}|{m.ClientSecret}|{m.UserEmail}";
        await _gate.WaitAsync(ct);
        try
        {
            if (_graph is not null && _graphKey == key) return _graph;

            TokenCredential credential;
            if (m.AuthMode == MailboxAuthMode.ClientSecret)
            {
                if (string.IsNullOrWhiteSpace(m.UserEmail))
                    throw new InvalidOperationException("ClientSecret modunda posta kutusu adresi (UserEmail) gerekli.");
                credential = new ClientSecretCredential(m.TenantId, m.ClientId, m.ClientSecret);
                _graph = new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
                SignedInAs = m.UserEmail;
            }
            else
            {
                credential = await CreateDeviceCodeCredentialAsync(m, ct);
                _graph = new GraphServiceClient(credential, Scopes);
            }
            _graphKey = key;
            return _graph;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DeviceCodeCredential> CreateDeviceCodeCredentialAsync(MailboxSettings m, CancellationToken ct)
    {
        var options = new DeviceCodeCredentialOptions
        {
            TenantId = string.IsNullOrWhiteSpace(m.TenantId) ? "common" : m.TenantId,
            ClientId = m.ClientId,
            // Token'lar diskte saklanır, böylece uygulama yeniden başlayınca tekrar giriş gerekmez.
            // Linux'ta libsecret yoksa şifrelenmemiş dosyaya düşer.
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                Name = "maildemon",
                UnsafeAllowUnencryptedStorage = true,
            },
            DeviceCodeCallback = (info, _) =>
            {
                PendingDeviceCodeMessage = info.Message;
                logger.LogWarning("Outlook girişi gerekiyor: {Message}", info.Message);
                return Task.CompletedTask;
            },
        };

        if (File.Exists(_authRecordPath))
        {
            await using var stream = File.OpenRead(_authRecordPath);
            options.AuthenticationRecord = await AuthenticationRecord.DeserializeAsync(stream, ct);
        }

        var credential = new DeviceCodeCredential(options);

        if (options.AuthenticationRecord is null)
        {
            // İlk giriş: kullanıcı web arayüzünde gösterilen kodu microsoft.com/devicelogin'e girer.
            AuthenticationRecord record;
            try
            {
                record = await credential.AuthenticateAsync(new TokenRequestContext(Scopes), ct);
            }
            finally
            {
                // Başarısız veya süresi dolmuş kodu ekranda bırakma.
                PendingDeviceCodeMessage = null;
            }
            await using var stream = File.Create(_authRecordPath);
            await record.SerializeAsync(stream, ct);
            SignedInAs = record.Username;
        }
        else
        {
            SignedInAs = options.AuthenticationRecord.Username;
        }

        return credential;
    }

    public void SignOut()
    {
        if (File.Exists(_authRecordPath)) File.Delete(_authRecordPath);
        Reset();
    }
}
