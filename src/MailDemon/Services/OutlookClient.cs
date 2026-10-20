using System.Runtime.InteropServices;
using MailDemon.Models;

namespace MailDemon.Services;

/// <summary>
/// Bilgisayarda kurulu Outlook masaüstü uygulamasından (klasik Outlook, COM) mail okur.
/// Azure kaydı veya bulut erişimi gerekmez; Outlook'un açık olduğu Windows oturumunda çalışmalıdır.
/// </summary>
public class OutlookClient(ILogger<OutlookClient> logger)
{
    private const int OlFolderInbox = 6;
    private const int OlMailItem = 43;
    private const int OlTo = 1;
    private const int OlCc = 2;
    private const string PrSmtpAddress = "http://schemas.microsoft.com/mapi/proptag/0x39FE001E";

    /// <summary>Outlook'taki hesaplarımın adresleri (otomatik algılanır, kimlik ayarlarına eklenir).</summary>
    public IReadOnlyList<string> DetectedEmails { get; private set; } = [];

    public string? SignedInAs { get; private set; }

    /// <summary>
    /// <paramref name="since"/> sonrasındaki mailleri yeniden eskiye okur. Daha önce işlenmiş olanlar
    /// (<paramref name="isKnown"/>) atlanır ve MaxMailsPerScan sınırına sayılmaz; böylece yoğun günlerde
    /// eski mailler sonraki taramalarda sırayla yakalanır.
    /// </summary>
    public Task<List<MailItem>> GetRecentMailsAsync(AppSettings s, DateTimeOffset since, Func<string, bool> isKnown, CancellationToken ct) =>
        RunSta(() =>
        {
            var ns = GetNamespace();
            DetectAccounts(ns);
            var folder = ResolveFolder(ns, s.Mailbox);
            var items = folder.Items;
            items.Sort("[ReceivedTime]", true);

            var result = new List<MailItem>();
            dynamic? item = items.GetFirst();
            while (item is not null && result.Count < s.Scan.MaxMailsPerScan && !ct.IsCancellationRequested)
            {
                try
                {
                    // Toplantı istekleri, okundu bilgileri vb. atlanır.
                    if ((int)item.Class == OlMailItem)
                    {
                        var received = new DateTimeOffset((DateTime)item.ReceivedTime);
                        if (received < since) break;
                        if (!isKnown((string)item.EntryID) && (!s.Scan.OnlyUnread || (bool)item.UnRead))
                            result.Add(ToMailItem(item, received));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Bir mail okunamadı, atlanıyor");
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
                item = items.GetNext();
            }
            return result;
        });

    /// <summary>Maili Outlook'ta açar (arayüzdeki "Outlook'ta aç" butonu).</summary>
    public Task OpenAsync(string entryId) =>
        RunSta(() =>
        {
            var ns = GetNamespace();
            ns.GetItemFromID(entryId).Display(false);
            return 0;
        });

    private static MailItem ToMailItem(dynamic m, DateTimeOffset received)
    {
        var to = new List<string>();
        var cc = new List<string>();
        foreach (dynamic r in m.Recipients)
        {
            string address = RecipientSmtp(r);
            if ((int)r.Type == OlTo) to.Add(address);
            else if ((int)r.Type == OlCc) cc.Add(address);
        }

        string fromEmail = m.SenderEmailAddress ?? "";
        if ((string?)m.SenderEmailType == "EX")
        {
            try { fromEmail = ExchangeSmtp(m.Sender) ?? fromEmail; } catch { }
        }

        return new MailItem
        {
            Id = (string)m.EntryID,
            Subject = (string?)m.Subject ?? "(konu yok)",
            FromName = (string?)m.SenderName ?? "",
            FromEmail = fromEmail,
            To = to,
            Cc = cc,
            ReceivedAt = received,
            Body = (string?)m.Body ?? "",
            HasAttachments = (int)m.Attachments.Count > 0,
            // olImportanceHigh = 2
            Importance = (int)m.Importance == 2 ? "high" : null,
        };
    }

    /// <summary>Exchange iç adreslerini (/O=EXCHANGELABS/...) SMTP adresine çevirir.</summary>
    private static string RecipientSmtp(dynamic r)
    {
        string address = r.Address ?? "";
        if (address.Contains('@')) return address;
        try
        {
            string? smtp = ExchangeSmtp(r.AddressEntry);
            if (!string.IsNullOrEmpty(smtp)) return smtp;
        }
        catch { }
        try
        {
            return (string)r.PropertyAccessor.GetProperty(PrSmtpAddress);
        }
        catch
        {
            return address;
        }
    }

    private static string? ExchangeSmtp(dynamic? entry)
    {
        if (entry is null) return null;
        var user = entry.GetExchangeUser();
        if (user is not null) return (string)user.PrimarySmtpAddress;
        var list = entry.GetExchangeDistributionList();
        if (list is not null) return (string)list.PrimarySmtpAddress;
        return null;
    }

    private void DetectAccounts(dynamic ns)
    {
        var emails = new List<string>();
        try
        {
            foreach (dynamic acc in ns.Accounts)
            {
                string smtp = acc.SmtpAddress ?? "";
                if (smtp.Contains('@')) emails.Add(smtp);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Outlook hesapları okunamadı");
        }
        DetectedEmails = emails;
        SignedInAs = emails.FirstOrDefault() ?? "Outlook";
    }

    private static dynamic ResolveFolder(dynamic ns, MailboxSettings m)
    {
        dynamic inbox = ns.GetDefaultFolder(OlFolderInbox);
        if (!string.IsNullOrWhiteSpace(m.Account))
        {
            dynamic? found = null;
            foreach (dynamic acc in ns.Accounts)
            {
                if (string.Equals((string)acc.SmtpAddress, m.Account.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    found = acc.DeliveryStore.GetDefaultFolder(OlFolderInbox);
                    break;
                }
            }
            inbox = found ?? throw new InvalidOperationException($"Outlook'ta '{m.Account}' hesabı bulunamadı.");
        }

        var folder = inbox;
        foreach (var part in (m.Folder ?? "").Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                folder = folder.Folders[part];
            }
            catch (COMException)
            {
                throw new InvalidOperationException($"Gelen Kutusu altında '{part}' klasörü bulunamadı.");
            }
        }
        return folder;
    }

    private static dynamic GetNamespace()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Outlook bağlantısı sadece Windows'ta, Outlook masaüstü uygulaması kuruluyken çalışır.");

        var type = Type.GetTypeFromProgID("Outlook.Application")
            ?? throw new InvalidOperationException("Klasik Outlook masaüstü uygulaması bulunamadı. (\"Yeni Outlook\" bu bağlantıyı desteklemez; klasik Outlook'a geçin.)");

        // Outlook zaten açıksa çalışan örneğe bağlanır; değilse arka planda başlatır.
        dynamic app = Activator.CreateInstance(type)!;
        dynamic ns = app.GetNamespace("MAPI");
        ns.Logon(Type.Missing, Type.Missing, false, false);
        return ns;
    }

    /// <summary>Outlook nesne modeli tek iş parçacıklı (STA) çağrılmak ister.</summary>
    private static Task<T> RunSta<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }
}
