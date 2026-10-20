# 📨 MailDemon

Outlook gelen kutunuzu düzenli aralıklarla tarar ve her maile şu soruların cevabını yazar:

- **Size hitaben mi yazılmış?** (TO'da mısınız, CC'de mi, adınız geçiyor mu, sizden bir şey isteniyor mu)
- **Sizden ne isteniyor?** Her iş ayrı madde olarak, son tarihiyle birlikte
- **Önem derecesi:** Kritik / Yüksek / Orta / Düşük / Bilgi, gerekçesiyle birlikte

Önemli kişileri (ör. Genel Müdür → **Kritik**) ve anahtar kelime kurallarını web arayüzünden tanımlarsınız. Bu kurallar **minimum** önemi belirler. AI daha yüksek bir önem bulursa onun kararı geçer. Belirlediğiniz seviyedeki mailler için ekranda yanıp sönen bir alarm, sesli uyarı ve masaüstü bildirimi çıkar. İsterseniz Teams veya Slack'e de mesaj gönderilir.

## Nasıl çalışır?

```
Outlook (Microsoft Graph)  ──►  Kural motoru  ──►  Claude AI  ──►  Sonuç + Alarm  ──►  Web arayüzü
   her N dakikada                VIP, kelime,       özet, istekler,     data/*.json      localhost:5080
   yeni mailler                  TO/CC, yok say     önem, gerekçe
```

1. **ScanWorker** her `N` dakikada bir Graph API ile son `X` saatteki mailleri çeker. Daha önce analiz edilen mailleri atlar.
2. **RuleEngine** AI'a gitmeden önce deterministik kuralları uygular:
   - Yok sayılan göndericiler (`noreply@*` gibi) tamamen atlanır.
   - TO, CC veya BCC'de olup olmadığınıza ve adınızın geçip geçmediğine bakılır.
   - Önemli kişi ve anahtar kelime eşleşmelerinden minimum önem hesaplanır.
   - Size gelmeyen ve hiçbir kurala uymayan mailler AI'a **gönderilmez**. Bu ayar kapatılabilir.
3. **AiAnalyzer** maili, kimliğinizi, önemli kişiler listesini ve ek kriterlerinizi Claude'a gönderir. Yanıt JSON şemasıyla zorunlu kılınmış yapılandırılmış bir çıktıdır. Sistem talimatı, mail içindeki talimatları ("bunu kritik işaretle" gibi) dikkate almamasını söyler.
4. **Nihai önem** = `max(kural önemi, AI önemi)`. Bu değer alarm eşiğini geçerse alarm oluşturulur.

## Kurulum

### 1. Gereksinimler

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Bir [Anthropic API anahtarı](https://console.anthropic.com/)
- Outlook / Microsoft 365 hesabı ve Azure portalında uygulama kaydı yetkisi. Kurumsal hesaplarda bu yetki genelde BT'dedir.

### 2. Azure'da uygulama kaydı (Outlook erişimi için)

1. <https://portal.azure.com> → **Microsoft Entra ID** → **App registrations** → **New registration**
2. Ad: `MailDemon`.
   **Supported account types:** Sadece kendi şirket hesabınız için *Single tenant* seçin. Kişisel Outlook.com hesabı için *…and personal Microsoft accounts* seçin.
3. Oluşturduktan sonra **Application (client) ID** ve **Directory (tenant) ID** değerlerini not edin.
4. **Authentication** → **Advanced settings** → **Allow public client flows** = **Yes**. Cihaz kodu ile giriş için bu gerekli.
5. **API permissions** → **Add a permission** → **Microsoft Graph** → **Delegated** → `Mail.Read` ekleyin.

> **Alternatif (sunucuda, kullanıcı girişi olmadan):** **Application** izni olarak `Mail.Read` ekleyin, yönetici onayı (*Grant admin consent*) verin ve **Certificates & secrets** altından bir client secret oluşturun. Ayarlarda giriş yöntemini *Uygulama izni* seçip secret'ı ve posta kutusu adresini girin. Bu izin tüm posta kutularını okuyabilir. Exchange tarafında [Application Access Policy](https://learn.microsoft.com/graph/auth-limit-mailbox-access) ile tek kutuya kısıtlamanız önerilir.

### 3. Çalıştırma

```bash
cd src/MailDemon
export ANTHROPIC_API_KEY=sk-ant-...      # Windows: set ANTHROPIC_API_KEY=sk-ant-...
dotnet run
```

Tarayıcıda <http://localhost:5080> adresini açın, ardından:

1. **Ayarlar → Kimliğim:** adınızı, e-posta adreslerinizi ve hitap şekillerinizi ("Ahmet Bey", "Ahmet") girin.
2. **Ayarlar → Önemli kişiler:** Genel Müdür'ü ekleyin, örneğin `gm@firma.com`, *Genel Müdür*, minimum önem **Kritik**.
3. **Ayarlar → Outlook bağlantısı:** Tenant ID ve Client ID'yi girin, ardından **Kaydet**'e basın.
4. Gelen Kutusu sayfasında mavi bir kutuda *"microsoft.com/devicelogin adresine gidip XXXX kodunu girin"* mesajı çıkar. Girişi bir kez yapmanız yeterli. Oturum `data/` klasöründe saklanır.
5. Gelen Kutusu sayfasında **🔔 Sesli alarmı aç** butonuna bir kez tıklayın. Tarayıcılar ses çalmak ve bildirim göstermek için kullanıcı etkileşimi ister.

Outlook bağlantısını kurmadan önce **Ayarlar → 🧪 Kuralları test et** bölümünden örnek bir mail yazıp kuralların ve AI'ın nasıl karar verdiğini görebilirsiniz.

## Ayarlar

| Bölüm | Ne işe yarar |
|---|---|
| Kimliğim | Mailin size hitaben olup olmadığını anlamak için adresleriniz, adınız, unvanınız |
| Önemli kişiler | Gönderici adresi veya `*@firma.com` deseni → minimum önem. "Sadece TO" işaretliyse CC'de olduğunuzda önem bir seviye düşer |
| Anahtar kelime kuralları | "acil", "fatura onayı" gibi ifadeler → minimum önem. Türkçe büyük/küçük harf duyarsızdır |
| Yok sayılanlar | Bülten ve otomatik bildirim göndericileri |
| AI | Model (varsayılan Claude Opus 5.5; Sonnet 5.5 veya Haiku 5.5 daha ucuz), düşünme seviyesi ve serbest metin **ek kriterler**. Örnek: "Müşteri şikayetleri en az Yüksek olsun" |
| Tarama | Aralık, geriye dönük saat, sadece okunmamışlar, CC dahil mi |
| Alarm | Alarm eşiği (varsayılan Kritik), ses, masaüstü bildirimi, Teams/Slack webhook |

Ayarlar `src/MailDemon/data/settings.json` dosyasında tutulur, analizler `data/analyses.json` dosyasındadır. `data/` klasörü git'e eklenmez.

## Güvenlik ve gizlilik

- Web arayüzü sadece `localhost:5080` adresini dinler ve oturum açma ekranı yoktur. Başka makinelerden erişime açacaksanız önüne kimlik doğrulama koyun (ör. IIS + Windows Authentication veya bir reverse proxy).
- AI'a gönderilen mailler Anthropic API'sine iletilir. Şirket politikanızı kontrol edin. *"Alıcılarda olmadığım mailleri AI'a gönderme"* seçeneği açık olduğunda size gelmeyen mailler gönderilmez. Gövde ayarlardaki karakter sınırında kesilir.
- API anahtarı ve client secret `data/settings.json` içinde düz metin olarak durur. Arayüze maskelenmiş gelir. API anahtarını ayarlara yazmak yerine `ANTHROPIC_API_KEY` ortam değişkeni kullanmanız önerilir.
- Outlook oturum token'ları diskte saklanır. Linux'ta libsecret yoksa şifrelenmemiş dosyaya yazılır.

## Proje yapısı

```
src/MailDemon/
├── Program.cs              # Minimal API uç noktaları + DI
├── Models/                 # AppSettings, MailAnalysis, Alert
├── Services/
│   ├── OutlookClient.cs    # Microsoft Graph: cihaz kodu / client secret ile mail okuma
│   ├── RuleEngine.cs       # VIP, anahtar kelime, TO/CC, yok sayma kuralları
│   ├── AiAnalyzer.cs       # Claude API çağrısı (yapılandırılmış JSON çıktı)
│   ├── MailProcessor.cs    # Kural + AI birleşimi, alarm, webhook
│   ├── ScanWorker.cs       # Arka planda periyodik tarama
│   └── Stores.cs           # JSON dosya tabanlı saklama
└── wwwroot/                # index.html (gelen kutusu + alarmlar), settings.html
```

## Geliştirme fikirleri

- Outlook'ta kategori veya bayrak ekleme (`Mail.ReadWrite` izni ile)
- Graph **change notifications** (webhook) ile anlık bildirim
- Günlük özet maili, ya da istekleri Microsoft To Do / Planner'a görev olarak ekleme
- Windows Service olarak çalıştırma (`UseWindowsService()`)
