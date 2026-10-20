namespace MailDemon.Services;

/// <summary>Belirlenen aralıkla posta kutusunu tarayan arka plan servisi.</summary>
public class ScanWorker(
    SettingsStore settings,
    AnalysisStore store,
    OutlookClient outlook,
    MailProcessor processor,
    ILogger<ScanWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim _wake = new(0, 1);

    public bool IsScanning { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? NextScanAt { get; private set; }

    /// <summary>Arayüzdeki "Şimdi tara" butonu.</summary>
    public void TriggerNow()
    {
        if (_wake.CurrentCount == 0) _wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var s = settings.Read();
            if (s.Scan.Enabled)
                await ScanOnceAsync(stoppingToken);

            var interval = TimeSpan.FromMinutes(Math.Max(1, s.Scan.IntervalMinutes));
            NextScanAt = DateTimeOffset.UtcNow + interval;
            try
            {
                await _wake.WaitAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        IsScanning = true;
        try
        {
            var s = settings.Read();
            var since = DateTimeOffset.UtcNow.AddHours(-Math.Max(1, s.Scan.LookbackHours));
            var mails = await outlook.GetRecentMailsAsync(s, since, ct);

            // Outlook'taki hesap adreslerim kimlik ayarlarına girilmemiş olsa da "bana mı" kontrolünde kullanılsın.
            foreach (var email in outlook.DetectedEmails)
                if (!s.Me.Emails.Contains(email, StringComparer.OrdinalIgnoreCase))
                    s.Me.Emails.Add(email);

            var newMails = mails.Where(m => !store.Contains(m.Id)).OrderBy(m => m.ReceivedAt).ToList();
            logger.LogInformation("{Total} mail bulundu, {New} yeni", mails.Count, newMails.Count);

            foreach (var mail in newMails)
            {
                ct.ThrowIfCancellationRequested();
                await processor.ProcessAsync(mail, s, save: true, ct);
            }

            store.Update(st => st.LastScanAt = DateTimeOffset.UtcNow);
            LastError = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            logger.LogError(ex, "Tarama başarısız");
        }
        finally
        {
            IsScanning = false;
        }
    }
}
