// Ortak yardımcılar (her iki sayfa da kullanır)

const PRIORITIES = ["Critical", "High", "Medium", "Low", "Info"];
const PRIORITY_TR = { Critical: "Kritik", High: "Yüksek", Medium: "Orta", Low: "Düşük", Info: "Bilgi" };
const ROLE_TR = { To: "Kime (TO)", Cc: "Bilgi (CC)", Bcc: "BCC / liste", None: "-" };

async function api(method, url, body) {
  const res = await fetch(url, {
    method,
    headers: body ? { "Content-Type": "application/json" } : {},
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`);
  const text = await res.text();
  return text ? JSON.parse(text) : null;
}

function esc(s) {
  return String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

function badge(p) {
  return `<span class="badge p-${p}">${PRIORITY_TR[p] ?? p}</span>`;
}

function priorityOptions(selected) {
  return PRIORITIES.map(p => `<option value="${p}" ${p === selected ? "selected" : ""}>${PRIORITY_TR[p]}</option>`).join("");
}

// "Mehmet Demir → BT bütçe sunumunu gönder · ⏰ bu akşam 18:00" şeklinde tek aksiyon satırı
function dueBadge(r, state) {
  if (!r.dueDate) return `<span class="due none">Deadline yok</span>`;
  const iso = r.dueDateIso ? ` (${new Date(r.dueDateIso + "T00:00").toLocaleDateString("tr-TR", { day: "2-digit", month: "2-digit" })})` : "";
  const label = state === "overdue" ? "⚠️ Gecikti: " : state === "today" ? "🔥 Bugün: " : "⏰ ";
  return `<span class="due ${state ?? "upcoming"}">${label}${esc(r.dueDate)}${iso}</span>`;
}

function dueState(r) {
  if (!r.dueDate) return "none";
  if (!r.dueDateIso) return "upcoming";
  const today = new Date().toLocaleDateString("sv-SE"); // yyyy-MM-dd yerel saat
  return r.dueDateIso < today ? "overdue" : r.dueDateIso === today ? "today" : "upcoming";
}

function actionList(requests, fallbackFrom) {
  if (!requests?.length) return "";
  return `<ul class="actions-list">${requests.map(r => `
    <li><b>${esc(r.requestedBy || fallbackFrom)}</b> → ${esc(r.description)} ${dueBadge(r, dueState(r))}
      ${r.fromThread ? `<span class="tag" title="Mailin altındaki önceki yazışmadan">↩ önceki yazışma</span>` : ""}</li>`).join("")}</ul>`;
}

function fmtDate(d) {
  return d ? new Date(d).toLocaleString("tr-TR") : "-";
}

async function refreshHeaderStatus() {
  const el = document.getElementById("hdr-status");
  if (!el) return null;
  try {
    const st = await api("GET", "/api/status");
    const who = st.signedInAs ? `📬 ${esc(st.signedInAs)}` : "📭 Outlook'a henüz bağlanılmadı";
    const scan = st.isScanning ? "⏳ taranıyor…" : `Son tarama: ${fmtDate(st.lastScanAt)}`;
    el.innerHTML = `${who} · ${scan}`;
    return st;
  } catch {
    el.textContent = "Sunucuya ulaşılamıyor";
    return null;
  }
}
