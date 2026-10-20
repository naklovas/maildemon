using System.Net.Http.Json;
using MailDemon.Models;

namespace MailDemon.Services;

/// <summary>Tek bir maili kurallardan ve AI'dan geçirir, sonucu kaydeder, gerekirse alarm üretir.</summary>
public class MailProcessor(
    AiAnalyzer ai,
    AnalysisStore store,
    IHttpClientFactory httpFactory,
    ILogger<MailProcessor> logger)
{
    /// <summary>Mail yok sayıldıysa null döner.</summary>
    public async Task<MailAnalysis?> ProcessAsync(MailItem mail, AppSettings s, bool save, CancellationToken ct)
    {
        var rules = RuleEngine.Evaluate(mail, s);
        if (rules.Ignored) return null;

        var analysis = new MailAnalysis
        {
            Id = mail.Id,
            Subject = mail.Subject,
            FromName = mail.FromName,
            FromEmail = mail.FromEmail,
            ReceivedAt = mail.ReceivedAt,
            WebLink = mail.WebLink,
            RecipientRole = rules.Role,
            RulePriority = rules.Priority,
            MatchedRules = rules.Matched,
            AnalyzedAt = DateTimeOffset.UtcNow,
            // AI yoksa kural motorunun tahmini geçerli.
            AddressedToMe = rules.Role == RecipientRole.To || rules.NameMentioned,
            Priority = rules.Priority,
        };

        if (!s.Ai.Enabled)
        {
            analysis.AiNote = "AI kapalı; sadece kurallar uygulandı.";
        }
        else if (s.Scan.SkipIrrelevant && !rules.IsRelevant)
        {
            analysis.AiNote = "Alıcılarda yoksunuz ve hiçbir kural eşleşmedi; AI'a gönderilmedi.";
        }
        else
        {
            try
            {
                var verdict = await ai.AnalyzeAsync(mail, rules, s, ct);
                analysis.AddressedToMe = verdict.AddressedToMe;
                analysis.Summary = verdict.Summary;
                analysis.Requests = verdict.Requests;
                analysis.Reason = verdict.Reason;
                analysis.AiPriority = verdict.Priority;
                // Kurallar taban (minimum) belirler; AI daha yüksek bulursa onu kullan.
                analysis.Priority = RuleEngine.Max(rules.Priority, verdict.Priority);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "AI analizi başarısız: {Subject}", mail.Subject);
                analysis.AiNote = "AI hatası: " + ex.Message;
            }
        }

        if (!save) return analysis;

        Alert? alert = null;
        if (analysis.Priority >= s.Alerts.MinPriority && analysis.Priority > Priority.Info)
        {
            alert = new Alert
            {
                MailId = analysis.Id,
                Priority = analysis.Priority,
                Title = $"{analysis.Priority}: {analysis.Subject}",
                Message = $"{analysis.FromName} <{analysis.FromEmail}>\n{analysis.Summary}",
            };
            await SendWebhookAsync(s, analysis, ct);
        }
        store.Add(analysis, alert);
        return analysis;
    }

    private async Task SendWebhookAsync(AppSettings s, MailAnalysis a, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Alerts.WebhookUrl)) return;
        try
        {
            var requests = a.Requests.Count == 0 ? "" :
                "\nİstenenler:\n" + string.Join("\n", a.Requests.Select(r => $"• {r.Description}{(r.DueDate is null ? "" : $" (son: {r.DueDate})")}"));
            var text = $"🚨 [{a.Priority}] {a.Subject}\nGönderen: {a.FromName} <{a.FromEmail}>\n{a.Summary}{requests}";
            using var http = httpFactory.CreateClient();
            var resp = await http.PostAsJsonAsync(s.Alerts.WebhookUrl, new { text }, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Webhook gönderilemedi");
        }
    }
}
