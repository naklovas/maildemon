using System.Text.Json.Serialization;

namespace MailDemon.Models;

/// <summary>Önem derecesi. Sayısal değer büyüdükçe önem artar.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Priority
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

/// <summary>Web arayüzünden düzenlenen tüm ayarlar. data/settings.json dosyasında saklanır.</summary>
public class AppSettings
{
    public MailboxSettings Mailbox { get; set; } = new();
    public IdentitySettings Me { get; set; } = new();
    public ScanSettings Scan { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public AlertSettings Alerts { get; set; } = new();

    /// <summary>Özel göndericiler (ör. Genel Müdür → Kritik).</summary>
    public List<VipSender> VipSenders { get; set; } = new();

    /// <summary>Konu/gövdede geçen anahtar kelimelere göre minimum önem.</summary>
    public List<KeywordRule> KeywordRules { get; set; } =
    [
        new() { Keyword = "acil", Priority = Priority.High },
        new() { Keyword = "urgent", Priority = Priority.High },
    ];

    /// <summary>Hiç taranmayacak göndericiler. "*@newsletter.com" gibi joker karakter desteklenir.</summary>
    public List<string> IgnoredSenders { get; set; } = ["noreply@*", "no-reply@*"];
}

public enum MailboxAuthMode
{
    /// <summary>Kişisel / kurumsal hesap, cihaz kodu ile tek seferlik oturum açma (delegated Mail.Read).</summary>
    DeviceCode,

    /// <summary>Azure AD uygulama kaydı + client secret (application Mail.Read, yönetici onayı gerekir).</summary>
    ClientSecret,
}

public class MailboxSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MailboxAuthMode AuthMode { get; set; } = MailboxAuthMode.DeviceCode;

    /// <summary>Kurumsal hesap için tenant id; kişisel Outlook.com için "common" veya "consumers".</summary>
    public string TenantId { get; set; } = "common";

    /// <summary>Azure portalındaki uygulama kaydının (App registration) Application (client) ID'si.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Sadece ClientSecret modunda kullanılır.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>ClientSecret modunda okunacak posta kutusu (ör. ahmet@firma.com).</summary>
    public string UserEmail { get; set; } = "";

    /// <summary>Taranacak klasör. Graph'ın iyi bilinen adları: inbox, archive...</summary>
    public string Folder { get; set; } = "inbox";
}

public class IdentitySettings
{
    /// <summary>Ad Soyad, ör. "Ahmet Yılmaz".</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Unvan / rol, AI'ın bağlamı anlaması için (ör. "IT Müdürü").</summary>
    public string Title { get; set; } = "";

    /// <summary>Tüm e-posta adreslerim ve takma adlarım.</summary>
    public List<string> Emails { get; set; } = new();

    /// <summary>Mail içinde bana hitap edilirken kullanılabilecek isimler: "Ahmet Bey", "Ahmet", "Sayın Yılmaz".</summary>
    public List<string> NameVariants { get; set; } = new();
}

public class ScanSettings
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>İlk taramada ve her taramada geriye dönük kaç saat bakılacağı.</summary>
    public int LookbackHours { get; set; } = 24;

    public int MaxMailsPerScan { get; set; } = 25;
    public bool OnlyUnread { get; set; } = false;

    /// <summary>Bilgi (CC) olarak eklendiğim mailleri de değerlendir.</summary>
    public bool IncludeCc { get; set; } = true;

    /// <summary>
    /// true ise: alıcılarda yoksam, adım geçmiyorsa ve hiçbir kural eşleşmiyorsa mail AI'a gönderilmez (maliyet + gizlilik).
    /// </summary>
    public bool SkipIrrelevant { get; set; } = true;
}

public class AiSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Boş ise ANTHROPIC_API_KEY ortam değişkeni kullanılır.</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>low / medium / high — düşük değer daha hızlı ve ucuz.</summary>
    public string Effort { get; set; } = "medium";

    /// <summary>AI'a ekstra talimatlar (serbest metin), ör. "Fatura onayı isteyen mailler en az Yüksek olsun".</summary>
    public string ExtraInstructions { get; set; } = "";

    /// <summary>Mail gövdesinin AI'a gönderilecek maksimum karakter sayısı.</summary>
    public int MaxBodyChars { get; set; } = 12000;
}

public class AlertSettings
{
    /// <summary>Bu önem ve üstündeki mailler için alarm üretilir.</summary>
    public Priority MinPriority { get; set; } = Priority.Critical;

    public bool PlaySound { get; set; } = true;
    public bool BrowserNotification { get; set; } = true;

    /// <summary>Opsiyonel: Teams/Slack incoming webhook adresi. {"text": "..."} JSON'u POST edilir.</summary>
    public string WebhookUrl { get; set; } = "";
}

public class VipSender
{
    public string Name { get; set; } = "";

    /// <summary>Tam adres (gm@firma.com) veya alan adı (*@firma.com).</summary>
    public string Email { get; set; } = "";

    /// <summary>Unvan, ör. "Genel Müdür". AI'a bağlam olarak verilir.</summary>
    public string Title { get; set; } = "";

    /// <summary>Bu göndericiden gelen ve bana yönelik maillerin minimum önemi.</summary>
    public Priority Priority { get; set; } = Priority.Critical;

    /// <summary>true ise sadece TO alanında olduğumda bu öncelik uygulanır; CC'de bir seviye düşer.</summary>
    public bool OnlyWhenDirect { get; set; } = false;

    public string Note { get; set; } = "";
}

public enum KeywordScope
{
    SubjectOrBody,
    Subject,
    Body,
}

public class KeywordRule
{
    public string Keyword { get; set; } = "";
    public Priority Priority { get; set; } = Priority.High;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public KeywordScope Scope { get; set; } = KeywordScope.SubjectOrBody;
}
