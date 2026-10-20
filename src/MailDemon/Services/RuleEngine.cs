using System.Text.RegularExpressions;
using MailDemon.Models;

namespace MailDemon.Services;

/// <summary>AI'dan önce çalışan, tamamen deterministik kurallar.</summary>
public class RuleResult
{
    public RecipientRole Role { get; set; }
    public bool NameMentioned { get; set; }
    public Priority Priority { get; set; } = Priority.Info;
    public List<string> Matched { get; } = new();
    public VipSender? Vip { get; set; }
    public bool Ignored { get; set; }

    /// <summary>AI'a sormaya değer mi?</summary>
    public bool IsRelevant => Role is RecipientRole.To or RecipientRole.Cc || NameMentioned || Vip is not null || Matched.Count > 0;
}

public static class RuleEngine
{
    public static RuleResult Evaluate(MailItem mail, AppSettings s)
    {
        var r = new RuleResult();

        if (s.IgnoredSenders.Any(p => WildcardMatch(mail.FromEmail, p)))
        {
            r.Ignored = true;
            r.Matched.Add("Yok sayılan gönderici");
            return r;
        }

        var myEmails = s.Me.Emails.Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim()).ToList();
        bool Mine(string addr) => myEmails.Any(e => string.Equals(e, addr, StringComparison.OrdinalIgnoreCase));

        r.Role = mail.To.Any(Mine) ? RecipientRole.To
            : mail.Cc.Any(Mine) ? RecipientRole.Cc
            // Ne To ne Cc'de varsam ama kutuma düştüyse: Bcc veya dağıtım listesi.
            : RecipientRole.Bcc;

        if (!s.Scan.IncludeCc && r.Role == RecipientRole.Cc)
            r.Role = RecipientRole.None;

        var names = s.Me.NameVariants.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        r.NameMentioned = names.Any(n => ContainsWord(mail.Subject + "\n" + mail.Body, n));

        if (r.Role == RecipientRole.To)
            r.Priority = Priority.Low;

        r.Vip = s.VipSenders.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v.Email) && WildcardMatch(mail.FromEmail, v.Email));
        if (r.Vip is not null)
        {
            var p = r.Vip.Priority;
            // Kural "sadece bana direkt yazdığında" ise ve ben CC'deysem bir seviye düşür.
            if (r.Vip.OnlyWhenDirect && r.Role != RecipientRole.To && p > Priority.Info)
                p -= 1;
            r.Priority = Max(r.Priority, p);
            var label = string.IsNullOrWhiteSpace(r.Vip.Title) ? r.Vip.Name : $"{r.Vip.Name} ({r.Vip.Title})";
            r.Matched.Add($"VIP: {label} → {p}");
        }

        foreach (var k in s.KeywordRules.Where(k => !string.IsNullOrWhiteSpace(k.Keyword)))
        {
            var hit = k.Scope switch
            {
                KeywordScope.Subject => ContainsWord(mail.Subject, k.Keyword),
                KeywordScope.Body => ContainsWord(mail.Body, k.Keyword),
                _ => ContainsWord(mail.Subject, k.Keyword) || ContainsWord(mail.Body, k.Keyword),
            };
            if (!hit) continue;
            r.Priority = Max(r.Priority, k.Priority);
            r.Matched.Add($"Anahtar kelime: \"{k.Keyword}\" → {k.Priority}");
        }

        if (string.Equals(mail.Importance, "high", StringComparison.OrdinalIgnoreCase))
        {
            r.Priority = Max(r.Priority, Priority.Medium);
            r.Matched.Add("Outlook: Yüksek önem işaretli");
        }

        return r;
    }

    public static Priority Max(Priority a, Priority b) => a >= b ? a : b;

    /// <summary>"*@firma.com", "gm@firma.com" gibi desenler; büyük/küçük harf duyarsız.</summary>
    public static bool WildcardMatch(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var regex = "^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(value ?? "", regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Kelime sınırlarıyla, Türkçe büyük/küçük harf duyarsız arama.</summary>
    public static bool ContainsWord(string text, string word)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(word)) return false;
        var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(TurkishLower(word.Trim()))}(?![\p{{L}}\p{{N}}])";
        return Regex.IsMatch(TurkishLower(text), pattern);
    }

    private static string TurkishLower(string s) =>
        s.Replace('I', 'ı').Replace('İ', 'i').ToLowerInvariant();
}
