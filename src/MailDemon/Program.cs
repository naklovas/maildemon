using MailDemon.Models;
using MailDemon.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<AnalysisStore>();
builder.Services.AddSingleton<OutlookClient>();
builder.Services.AddSingleton<AiAnalyzer>();
builder.Services.AddSingleton<MailProcessor>();
builder.Services.AddSingleton<ScanWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScanWorker>());
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

const string Mask = "********";

// ---- Ayarlar ----

app.MapGet("/api/settings", (SettingsStore store) =>
{
    var s = store.Read();
    // Gizli değerleri tarayıcıya göndermiyoruz.
    if (s.Ai.ApiKey != "") s.Ai.ApiKey = Mask;
    return s;
});

app.MapPut("/api/settings", (AppSettings incoming, SettingsStore store, ScanWorker worker) =>
{
    var current = store.Read();
    if (incoming.Ai.ApiKey == Mask) incoming.Ai.ApiKey = current.Ai.ApiKey;
    incoming.Scan.IntervalMinutes = Math.Clamp(incoming.Scan.IntervalMinutes, 1, 1440);
    incoming.Scan.MaxMailsPerScan = Math.Clamp(incoming.Scan.MaxMailsPerScan, 1, 200);
    incoming.Ai.MaxBodyChars = Math.Clamp(incoming.Ai.MaxBodyChars, 500, 100_000);
    incoming.Ai.MaxTokens = Math.Clamp(incoming.Ai.MaxTokens, 100, 100_000);

    store.Write(incoming);
    worker.TriggerNow();
    return Results.Ok();
});

// ---- Durum ----

app.MapGet("/api/status", (SettingsStore settings, AnalysisStore store, OutlookClient outlook, ScanWorker worker) =>
{
    var s = settings.Read();
    var warnings = new List<string>();
    if (s.Me.Emails.Count == 0 && outlook.DetectedEmails.Count == 0)
        warnings.Add("E-posta adresiniz girilmemiş; maillerin size gelip gelmediği anlaşılamaz (Ayarlar > Kimliğim).");
    if (s.Ai.Enabled && (string.IsNullOrWhiteSpace(s.Ai.BaseUrl) || string.IsNullOrWhiteSpace(s.Ai.Model)))
        warnings.Add("AI servis adresi veya model adı girilmemiş (Ayarlar > AI).");

    return new
    {
        worker.IsScanning,
        worker.LastError,
        worker.NextScanAt,
        LastScanAt = store.Query(st => st.LastScanAt),
        outlook.SignedInAs,
        outlook.DetectedEmails,
        Warnings = warnings,
    };
});

app.MapPost("/api/scan", (ScanWorker worker) =>
{
    worker.TriggerNow();
    return Results.Accepted();
});

app.MapPost("/api/analyses/{id}/open", async (string id, OutlookClient outlook) =>
{
    await outlook.OpenAsync(id);
    return Results.Ok();
});

// ---- Analizler ----

app.MapGet("/api/analyses", (AnalysisStore store, Priority? minPriority, AnalysisStatus? status, bool? onlyMine) =>
    store.Query(st => st.Analyses
        .Where(a => minPriority is null || a.Priority >= minPriority)
        .Where(a => status is null || a.Status == status)
        .Where(a => onlyMine != true || a.AddressedToMe)
        .OrderByDescending(a => a.Status == AnalysisStatus.New)
        .ThenByDescending(a => a.Priority)
        .ThenByDescending(a => a.ReceivedAt)
        .Take(500)
        .ToList()));

app.MapPost("/api/analyses/{id}/status", (string id, StatusChange body, AnalysisStore store) =>
{
    store.Update(st =>
    {
        var a = st.Analyses.FirstOrDefault(x => x.Id == id);
        if (a is not null) a.Status = body.Status;
        foreach (var alert in st.Alerts.Where(x => x.MailId == id)) alert.Acknowledged = true;
    });
    return Results.Ok();
});

// Kaydı silip yeniden taramayı tetikler; mail bir sonraki taramada (lookback içindeyse) tekrar analiz edilir.
app.MapPost("/api/analyses/{id}/reanalyze", (string id, AnalysisStore store, ScanWorker worker) =>
{
    store.Update(st => st.Analyses.RemoveAll(x => x.Id == id));
    worker.TriggerNow();
    return Results.Accepted();
});

// ---- Aksiyon listesi (Analiz sayfası) ----

app.MapGet("/api/actions", (AnalysisStore store) =>
{
    var today = DateOnly.FromDateTime(DateTime.Now);
    var actions = store.Query(st => st.Analyses
        .Where(a => a.Status == AnalysisStatus.New && a.AddressedToMe)
        .SelectMany(a => a.Requests.Select(r => new ActionItem(
            MailId: a.Id,
            Subject: a.Subject,
            Priority: a.Priority,
            ReceivedAt: a.ReceivedAt,
            RequestedBy: string.IsNullOrWhiteSpace(r.RequestedBy) ? a.FromName : r.RequestedBy,
            Description: r.Description,
            DueDate: r.DueDate,
            DueDateIso: r.DueDateIso,
            FromThread: r.FromThread,
            DueState: DueState(r.DueDateIso, r.DueDate, today))))
        .ToList());

    // Önce gecikmiş/bugün, sonra önem, sonra en yakın son tarih.
    return actions
        .OrderBy(x => x.DueState switch { "overdue" => 0, "today" => 1, "upcoming" => 2, _ => 3 })
        .ThenByDescending(x => x.Priority)
        .ThenBy(x => x.DueDateIso ?? "9999")
        .ThenByDescending(x => x.ReceivedAt)
        .ToList();
});

// ---- Alarmlar ----

app.MapGet("/api/alerts", (AnalysisStore store) =>
    store.Query(st => st.Alerts.Where(a => !a.Acknowledged).ToList()));

app.MapPost("/api/alerts/{id}/ack", (string id, AnalysisStore store) =>
{
    store.Update(st =>
    {
        foreach (var a in st.Alerts.Where(x => x.Id == id || id == "all")) a.Acknowledged = true;
    });
    return Results.Ok();
});

// ---- Test: Outlook'a bağlanmadan bir maili kurallar + AI ile dene ----

app.MapPost("/api/test-analyze", async (TestMail t, SettingsStore settings, MailProcessor processor, CancellationToken ct) =>
{
    var mail = new MailItem
    {
        Id = "test-" + Guid.NewGuid().ToString("N"),
        FromName = t.FromName,
        FromEmail = t.FromEmail,
        To = Split(t.To),
        Cc = Split(t.Cc),
        Subject = t.Subject,
        Body = t.Body,
        ReceivedAt = DateTimeOffset.Now,
    };
    var result = await processor.ProcessAsync(mail, settings.Read(), save: false, ct);
    return result is null
        ? Results.Ok(new { ignored = true })
        : Results.Ok(result);
});

app.Run();

static List<string> Split(string? s) =>
    (s ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

static string DueState(string? iso, string? text, DateOnly today)
{
    if (iso is not null && DateOnly.TryParse(iso, out var due))
        return due < today ? "overdue" : due == today ? "today" : "upcoming";
    // Tarihe çevrilemeyen ama yazılmış bir son tarih (ör. "en kısa sürede") yine de deadline sayılır.
    return string.IsNullOrWhiteSpace(text) ? "none" : "upcoming";
}

record StatusChange(AnalysisStatus Status);

record ActionItem(
    string MailId, string Subject, Priority Priority, DateTimeOffset ReceivedAt,
    string RequestedBy, string Description, string? DueDate, string? DueDateIso, bool FromThread, string DueState);

record TestMail(string FromName, string FromEmail, string? To, string? Cc, string Subject, string Body);
