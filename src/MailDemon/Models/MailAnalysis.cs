using System.Text.Json.Serialization;

namespace MailDemon.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecipientRole
{
    None,
    Bcc,
    Cc,
    To,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnalysisStatus
{
    New,
    Done,
    Dismissed,
}

/// <summary>Taranmış bir mail (AI'a sorulmadan önceki ham hali).</summary>
public class MailItem
{
    public string Id { get; set; } = "";
    public string Subject { get; set; } = "";
    public string FromName { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    public DateTimeOffset ReceivedAt { get; set; }
    public string Body { get; set; } = "";
    public string? WebLink { get; set; }
    public bool HasAttachments { get; set; }
    public string? Importance { get; set; }
}

/// <summary>Bir mailden istenen tek bir iş.</summary>
public class MailRequest
{
    public string Description { get; set; } = "";

    /// <summary>Mailde belirtilen veya çıkarılan son tarih (serbest metin, ör. "Cuma 17:00").</summary>
    public string? DueDate { get; set; }
}

/// <summary>Kural motoru + AI yorumunun birleşmiş sonucu.</summary>
public class MailAnalysis
{
    public string Id { get; set; } = "";
    public string Subject { get; set; } = "";
    public string FromName { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public string? WebLink { get; set; }

    public RecipientRole RecipientRole { get; set; }

    /// <summary>Mail bana hitaben mi yazılmış (AI kararı; AI yoksa kural tahmini).</summary>
    public bool AddressedToMe { get; set; }

    public string Summary { get; set; } = "";
    public List<MailRequest> Requests { get; set; } = new();

    public Priority Priority { get; set; }
    public Priority? AiPriority { get; set; }
    public Priority RulePriority { get; set; }

    /// <summary>Önem derecesinin gerekçesi (AI).</summary>
    public string Reason { get; set; } = "";

    /// <summary>Eşleşen kurallar: "VIP: Genel Müdür", "Anahtar kelime: acil"...</summary>
    public List<string> MatchedRules { get; set; } = new();

    public AnalysisStatus Status { get; set; } = AnalysisStatus.New;
    public DateTimeOffset AnalyzedAt { get; set; }

    /// <summary>AI'a gönderilmedi veya hata aldıysa açıklaması.</summary>
    public string? AiNote { get; set; }
}

public class Alert
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MailId { get; set; } = "";
    public Priority Priority { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Acknowledged { get; set; }
}
