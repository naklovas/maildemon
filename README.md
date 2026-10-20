# 📨 MailDemon

Bilgisayarınızdaki **Outlook masaüstü uygulamasının** gelen kutusunu düzenli aralıklarla tarar ve **şirket içi AI servisinize** (OpenAI uyumlu API) yorumlatarak her maile şu soruların cevabını yazar:

- **Size hitaben mi yazılmış?** (TO'da mısınız, CC'de mi, adınız geçiyor mu, sizden bir şey isteniyor mu)
- **Sizden ne isteniyor?** Her iş ayrı madde olarak, son tarihiyle birlikte
- **Önem derecesi:** Kritik / Yüksek / Orta / Düşük / Bilgi, gerekçesiyle birlikte

Her mail için kısa bir **aksiyon listesi** çıkar: *kim → ne istiyor → deadline var mı*. Mailin altındaki önceki yazışma da okunur; daha önce istenip hâlâ açık olan işler "↩ önceki yazışma" etiketiyle listeye girer. **Analiz** sayfası tüm açık aksiyonları tek tabloda gösterir: önce gecikmiş ve bugün son günü olanlar, kişiye göre filtre ve kopyalanabilir özet liste.

Önemli kişileri (ör. Genel Müdür → **Kritik**) ve anahtar kelime kurallarını web arayüzünden tanımlarsınız. Bu kurallar **minimum** önemi belirler. AI daha yüksek bir önem bulursa onun kararı geçer. Belirlediğiniz seviyedeki mailler için ekranda yanıp sönen bir alarm, sesli uyarı ve masaüstü bildirimi çıkar. İsterseniz Teams veya Slack'e de mesaj gönderilir.

## Nasıl çalışır?

```
Lokal Outlook (COM)  ──►  Kural motoru  ──►  Şirket içi AI  ──►  Sonuç + Alarm  ──►  Web arayüzü
   her N dakikada          VIP, kelime,       /chat/completions   data/*.json      localhost:5080
   yeni mailler            TO/CC, yok say     özet, istekler, önem
```

1. **ScanWorker** her `N` dakikada bir, açık olan Outlook uygulamasından son `X` saatteki mailleri okur. Daha önce analiz edilen mailleri atlar. Outlook'taki hesap adresleriniz otomatik algılanır.
2. **RuleEngine** AI'a gitmeden önce deterministik kuralları uygular:
   - Yok sayılan göndericiler (`noreply@*` gibi) tamamen atlanır.
   - TO, CC veya BCC'de olup olmadığınıza ve adınızın geçip geçmediğine bakılır.
   - Önemli kişi ve anahtar kelime eşleşmelerinden minimum önem hesaplanır.
   - Size gelmeyen ve hiçbir kurala uymayan mailler AI'a **gönderilmez**. Bu ayar kapatılabilir.
3. **AiAnalyzer** maili, kimliğinizi, önemli kişiler listesini ve ek kriterlerinizi şirket içi AI servisinin `/chat/completions` adresine gönderir ve JSON yanıt ister. Model JSON'u kod bloğu içinde ya da önüne açıklama ekleyerek döndürse veya önemi Türkçe yazsa ("Kritik") da yanıt çözümlenir. Sistem talimatı, mail içindeki talimatları ("bunu kritik işaretle" gibi) dikkate almamasını söyler.
4. **Nihai önem** = `max(kural önemi, AI önemi)`. Bu değer alarm eşiğini geçerse alarm oluşturulur.

## Kurulum

### 1. Gereksinimler

- Windows ve **klasik Outlook** masaüstü uygulaması (Microsoft 365 / Outlook 2016+). *"Yeni Outlook"* COM bağlantısını desteklemez. Sağ üstteki "Yeni Outlook" anahtarını kapatarak klasik Outlook'a dönebilirsiniz.
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Şirket içi, OpenAI uyumlu AI servisinin adresi, model adı ve token'ı. Örnekler: vLLM, LiteLLM, Ollama, Azure OpenAI ya da kurum içi bir gateway.

### 2. Çalıştırma

Uygulamayı **Outlook'un açık olduğu Windows oturumunda, kendi kullanıcınızla** çalıştırın. Yönetici olarak çalıştırmayın: Outlook ve uygulama aynı yetki seviyesinde olmalıdır, yoksa bağlantı kurulamaz.

```bat
cd src\MailDemon
dotnet run
```

Tarayıcıda <http://localhost:5080> adresini açın, ardından:

1. **Ayarlar → AI yorumlama:**
   - **Servis adresi:** ör. `https://ai.firma.local/v1`. Sonuna `/chat/completions` otomatik eklenir. Azure OpenAI'da tam URL'yi yapıştırın: `https://<kaynak>.openai.azure.com/openai/deployments/<ad>/chat/completions?api-version=2024-10-21`.
   - **Model adı:** servisinizdeki model adı.
   - **Token:** Token `Authorization: Bearer <token>` başlığıyla gönderilir. Azure OpenAI için başlığı `api-key` seçin. Token'ı ayarlara yazmak yerine `AI_API_KEY` ortam değişkeniyle de verebilirsiniz.
   - **JSON modu:** Servisiniz `response_format` desteklemiyorsa ve 400 hatası alıyorsanız *Yok* seçin. Model sıcaklık parametresini kabul etmiyorsa *Temperature* alanını boş bırakın.
2. **Ayarlar → Kimliğim:** adınızı ve hitap şekillerinizi ("Ahmet Bey", "Ahmet") girin. Outlook hesap adresleriniz otomatik algılanır, başka takma adlarınız varsa ekleyin.
3. **Ayarlar → Önemli kişiler:** Genel Müdür'ü ekleyin, örneğin `gm@firma.com`, *Genel Müdür*, minimum önem **Kritik**.
4. **Kaydet**'e basın. Tarama hemen başlar.
5. Gelen Kutusu sayfasında **🔔 Sesli alarmı aç** butonuna bir kez tıklayın. Tarayıcılar ses çalmak ve bildirim göstermek için kullanıcı etkileşimi ister.

**Ayarlar → 🧪 Kuralları test et** bölümünden örnek bir mail yazıp kuralların ve AI'ın nasıl karar verdiğini görebilirsiniz. AI bağlantısını test etmek için de bu bölümü kullanın.

### Olası sorunlar

| Belirti | Çözüm |
|---|---|
| *"Bir program e-posta adresi bilgilerine erişmeye çalışıyor"* uyarısı | Outlook → Dosya → Seçenekler → Güven Merkezi → Programlı Erişim. Antivirüs güncelse uyarı çıkmaz. Kurumsal ortamda BT bu ayarı GPO ile yönetiyor olabilir |
| *"Klasik Outlook masaüstü uygulaması bulunamadı"* | Yeni Outlook kullanılıyor ya da Outlook kurulu değil |
| Tarama hatası: `0x80080005` / `CO_E_SERVER_EXEC_FAILURE` | Uygulama ile Outlook farklı yetki seviyesinde çalışıyor. İkisini de normal kullanıcı olarak açın |
| AI hatası: HTTP 400 `response_format` | JSON modunu *json_object* veya *Yok* yapın |
| AI hatası: SSL / sertifika | Şirket kök sertifikası Windows'a yüklü olmalı. BT'den isteyin |

## Ayarlar

| Bölüm | Ne işe yarar |
|---|---|
| Kimliğim | Mailin size hitaben olup olmadığını anlamak için adresleriniz, adınız, unvanınız |
| Önemli kişiler | Gönderici adresi veya `*@firma.com` deseni → minimum önem. "Sadece TO" işaretliyse CC'de olduğunuzda önem bir seviye düşer |
| Anahtar kelime kuralları | "acil", "fatura onayı" gibi ifadeler → minimum önem. Türkçe büyük/küçük harf duyarsızdır |
| Yok sayılanlar | Bülten ve otomatik bildirim göndericileri |
| AI | Servis adresi, model, token, JSON modu ve serbest metin **ek kriterler**. Örnek: "Müşteri şikayetleri en az Yüksek olsun" |
| Tarama | Aralık, geriye dönük saat, sadece okunmamışlar, CC dahil mi |
| Outlook | Taranacak klasör (boş = Gelen Kutusu, alt klasör için `Projeler/2026`) ve birden fazla hesap varsa hangisi |
| Alarm | Alarm eşiği (varsayılan Kritik), ses, masaüstü bildirimi, Teams/Slack webhook |

Ayarlar `src/MailDemon/data/settings.json` dosyasında tutulur, analizler `data/analyses.json` dosyasındadır. `data/` klasörü git'e eklenmez.

## Güvenlik ve gizlilik

- Mailler sadece ayarlarda girdiğiniz şirket içi AI adresine gönderilir. Başka bir buluta gitmez. *"Alıcılarda olmadığım mailleri AI'a gönderme"* seçeneği açık olduğunda size gelmeyen mailler gönderilmez. Gövde ayarlardaki karakter sınırında kesilir.
- Web arayüzü sadece `localhost:5080` adresini dinler ve oturum açma ekranı yoktur. Başka makinelerden erişime açmayın.
- AI token'ı `data/settings.json` içinde düz metin olarak durur. Arayüze maskelenmiş gelir. İsterseniz token'ı ayarlara yazmak yerine `AI_API_KEY` ortam değişkeniyle verin.

## Proje yapısı

```
src/MailDemon/
├── Program.cs              # Minimal API uç noktaları + DI
├── Models/                 # AppSettings, MailAnalysis, Alert
├── Services/
│   ├── OutlookClient.cs    # Lokal Outlook (COM) üzerinden mail okuma, "Outlook'ta aç"
│   ├── RuleEngine.cs       # VIP, anahtar kelime, TO/CC, yok sayma kuralları
│   ├── AiAnalyzer.cs       # OpenAI uyumlu /chat/completions çağrısı + toleranslı JSON çözümleme
│   ├── MailProcessor.cs    # Kural + AI birleşimi, alarm, webhook
│   ├── ScanWorker.cs       # Arka planda periyodik tarama
│   └── Stores.cs           # JSON dosya tabanlı saklama
└── wwwroot/                # index.html (gelen kutusu + alarmlar), analiz.html (aksiyon listesi), settings.html
```

## Geliştirme fikirleri

- Outlook'ta kategori veya bayrak ekleme (COM ile `item.Categories`)
- Outlook `NewMailEx` olayı ile anlık tetikleme
- Windows başlangıcında otomatik çalıştırma (Görev Zamanlayıcı, "kullanıcı oturum açtığında")
- Günlük özet maili, ya da istekleri Microsoft To Do / Planner'a görev olarak ekleme
