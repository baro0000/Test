function post(obj) {
    try {
        chrome.webview.postMessage(obj);
    } catch (e) {
        console.error(e);
    }
}

function chooseCsv() { post({ cmd: "chooseCsv" }); }
function chooseExcel() { post({ cmd: "chooseExcel" }); }
function loadTransactions() { post({ cmd: "loadTransactions" }); }
function updateBudget() { post({ cmd: "updateBudget" }); }
function getLog() { post({ cmd: "getLog" }); }

// ----------------- odbiór wiadomoœci z C# -----------------
window.chrome.webview.addEventListener('message', event => {
    try {
        const msg = event.data;
        if (msg.cmd === 'log') {
            document.getElementById('log').innerText = normalize(msg.text);
        }
        if (msg.cmd === 'transactionsLoaded') {
            const items = msg.items || [];
            const tbody = document.querySelector('#txTable tbody');
            tbody.innerHTML = "";
            for (const it of items) {
                const tr = document.createElement("tr");
                tr.innerHTML = `<td>${normalize(it.date)}</td>
                        <td>${normalize(it.recipient)}</td>
                        <td>${normalize(it.opis)}</td>
                        <td>${it.kwota}</td>
                        <td>${normalize(it.category || "")}</td>`;
                tbody.appendChild(tr);
            }
        }
        if (msg.cmd === 'classify') {
            openClassifyModal(msg.items || [], msg.categories || []);
        }
    } catch (e) {
        console.error(e);
    }
});

// ----------------- helper: usuwanie polskich znaków -----------------
function normalize(str) {
    if (!str) return "";
    return str.normalize("NFD").replace(/[\u0300-\u036f]/g, "")
        .replace(/³/g, "l").replace(/£/g, "L")
        .replace(/¹/g, "a").replace(/æ/g, "c")
        .replace(/ê/g, "e").replace(/ñ/g, "n")
        .replace(/ó/g, "o").replace(/œ/g, "s")
        .replace(/Ÿ/g, "z").replace(/¿/g, "z");
}

// ----------------- MODAL: klasyfikacja -----------------
function openClassifyModal(t, callback) {
    const modal = document.getElementById("classifyModal");
    const modalContent = document.getElementById("modalContent");
    modalContent.innerHTML = "";

    // czytelne wyœwietlanie transakcji
    modalContent.innerHTML = `
    <div class="tx-block">
      <div><strong>Data transakcji:</strong> ${t.DataTransakcji}</div>
      <div><strong>Odbiorca:</strong> ${t.Odbiorca}</div>
      <div><strong>Tytu³:</strong> ${t.Tytul}</div>
      <div><strong>Kwota:</strong> ${t.Obciazenia || t.Uznania}</div>
    </div>
  `;

    // okreœlamy czy to uznanie czy obci¹¿enie
    const isUznanie = parseFloat(t.Uznania) > 0;

    // zestawy kategorii
    let cats = [];
    if (isUznanie) {
        cats = ["Bartek", "Gosia", "Inne"];
    } else {
        cats = [
            "Jedzenie",
            "Transport",
            "Mieszkanie",
            "Rachunki",
            "Rozrywka",
            "Zdrowie",
            "Edukacja",
            "Inne_wydatki"
        ];
    }

    const select = document.createElement("select");
    select.id = "categorySelect";
    select.style.padding = "6px";
    select.style.marginTop = "10px";
    select.style.fontSize = "15px";

    cats.forEach(c => {
        const option = document.createElement("option");
        option.value = c;
        option.textContent = c;
        select.appendChild(option);
    });

    modalContent.appendChild(select);

    const btn = document.createElement("button");
    btn.textContent = "ZatwierdŸ";
    btn.style.marginTop = "10px";
    btn.style.padding = "6px 12px";
    btn.onclick = () => {
        const selected = select.value;
        modal.style.display = "none";
        callback(selected);
    };
    modalContent.appendChild(btn);

    modal.style.display = "block";
}


function closeClassify() {
    document.getElementById('classifyModal').style.display = 'none';
}

function submitClassification() {
    const rows = document.querySelectorAll('#classifyRows .classify-card');
    const mappings = [];
    rows.forEach(row => {
        const select = row.querySelector('.cat-select');
        const idx = parseInt(select.dataset.idx);
        const category = select.value;
        const applyEl = row.querySelector('.applyAll');
        const applyToAll = applyEl.checked;
        const keywordEl = row.querySelector('.keyword');
        const keyword = keywordEl.value || "";
        mappings.push({ idx, category, applyToAll, keyword });
    });

    post({ cmd: 'classifyResult', mappings });
    closeClassify();
}
