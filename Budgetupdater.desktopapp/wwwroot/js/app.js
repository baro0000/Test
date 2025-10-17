// small helper to post messages to host
function post(obj) {
  try {
    chrome.webview.postMessage(obj);
  } catch (e) {
    console.error(e);
  }
}
function chooseCsv() { post({ cmd: 'chooseCsv' }); }
function chooseExcel() { post({ cmd: 'chooseExcel' }); }
function loadTransactions() { post({ cmd: 'loadTransactions' }); }
function updateBudget() { post({ cmd: 'updateBudget' }); }
function getLog() { post({ cmd: 'getLog' }); }

// normalize polish diacritics to ASCII-like characters
function normalize(str) {
  if (!str) return '';
  return str.normalize('NFD').replace(/[̀-ͯ]/g, '')
            .replace(/ł/g, 'l').replace(/Ł/g,'L')
            .replace(/ą/g,'a').replace(/ć/g,'c')
            .replace(/ę/g,'e').replace(/ń/g,'n')
            .replace(/ó/g,'o').replace(/ś/g,'s')
            .replace(/ź/g,'z').replace(/ż/g,'z');
}

// receive messages from host (C#)
window.chrome.webview.addEventListener('message', event => {
  try {
    const msg = event.data;
    if (msg.cmd === 'log') {
      document.getElementById('log').innerText = normalize(msg.text);
    }
    if (msg.cmd === 'transactionsLoaded') {
      const items = msg.items || [];
      const tbody = document.querySelector('#txTable tbody');
      tbody.innerHTML = '';
      for (const it of items) {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td>${normalize(it.date)}</td><td>${normalize(it.recipient)}</td><td>${normalize(it.opis)}</td><td>${it.kwota}</td><td>${normalize(it.category||'')}</td>`;
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

function openClassifyModal(items, categories) {
  const modal = document.getElementById('classifyModal');
  const container = document.getElementById('classifyRows');
  container.innerHTML = '';

  items.forEach(it => {
    const kwota = parseFloat(it.kwota);
    const isUznanie = kwota >= 0;

    // define category lists explicitly
    const incomeCats = ['Bartek','Gosia','Inne'];
    // expense: keep 'Inne_wydatki' and other expense categories will be listed from categories param if needed
    const expenseCats = (categories || []).filter(c => !incomeCats.includes(c));
    if (!expenseCats.includes('Inne_wydatki')) expenseCats.push('Inne_wydatki');

    const cats = isUznanie ? incomeCats : expenseCats;

    const card = document.createElement('div');
    card.className = 'classify-card';
    card.innerHTML = `
      <div class="tx-line"><b>Data transakcji:</b> ${normalize(it.date)}</div>
      <div class="tx-line"><b>Odbiorca:</b> ${normalize(it.recipient)}</div>
      <div class="tx-line"><b>Opis:</b> ${normalize(it.opis)}</div>
      <div class="tx-line"><b>Kwota:</b> <span class="${kwota<0?'minus':'plus'}">${kwota.toFixed(2)}</span></div>
      <div class="tx-line"><b>Kategoria:</b>
        <select data-idx="${it.idx}" class="cat-select">
          ${cats.map(c=>`<option value="${c}">${c}</option>`).join('')}
        </select>
      </div>
      <div class="tx-line"><label><input type="checkbox" data-idx="${it.idx}" class="applyAll"> Zapamietaj regule (dla przyszlych)</label></div>
      <div class="tx-line"><b>Slowo klucz (opcjonalne):</b> <input type="text" data-idx="${it.idx}" class="keyword" placeholder="np. orange, biedronka..." /></div>
      <hr/>`;
    container.appendChild(card);
  });

  modal.style.display = 'block';
}

function closeClassify() {
  document.getElementById('classifyModal').style.display = 'none';
}

function submitClassification() {
  const rows = document.querySelectorAll('#classifyRows .classify-card');
  const mappings = [];
  rows.forEach(row=>{
    const select = row.querySelector('.cat-select');
    const idx = parseInt(select.dataset.idx);
    const category = select.value;
    const applyEl = row.querySelector('.applyAll');
    const applyToAll = applyEl.checked;
    const keywordEl = row.querySelector('.keyword');
    const keyword = keywordEl.value || '';
    mappings.push({ idx, category, applyToAll, keyword });
  });
  // send mappings to C#
  post({ cmd: 'classifyResult', mappings });
  closeClassify();
}
