const currency = new Intl.NumberFormat("es-MX", {
  style: "currency",
  currency: "MXN",
});

const denominations = [1000, 500, 200, 100, 50, 20, 10, 5, 2, 1, 0.5];
const state = {
  dashboard: null,
  denominationCounts: new Map(),
  saving: false,
};

const elements = {
  dateInput: document.querySelector("#dateInput"),
  refreshButton: document.querySelector("#refreshButton"),
  connectionStatus: document.querySelector("#connectionStatus"),
  daySales: document.querySelector("#daySales"),
  dayMeta: document.querySelector("#dayMeta"),
  monthSales: document.querySelector("#monthSales"),
  monthMeta: document.querySelector("#monthMeta"),
  yearSales: document.querySelector("#yearSales"),
  yearMeta: document.querySelector("#yearMeta"),
  dayCost: document.querySelector("#dayCost"),
  dayProfit: document.querySelector("#dayProfit"),
  expectedCash: document.querySelector("#expectedCash"),
  originList: document.querySelector("#originList"),
  originCount: document.querySelector("#originCount"),
  responsibleInput: document.querySelector("#responsibleInput"),
  openingCashInput: document.querySelector("#openingCashInput"),
  withdrawalsInput: document.querySelector("#withdrawalsInput"),
  denominationList: document.querySelector("#denominationList"),
  notesInput: document.querySelector("#notesInput"),
  countedCash: document.querySelector("#countedCash"),
  differenceBadge: document.querySelector("#differenceBadge"),
  saveButton: document.querySelector("#saveButton"),
  lastClosePanel: document.querySelector("#lastClosePanel"),
  lastCloseTitle: document.querySelector("#lastCloseTitle"),
  lastCloseDetail: document.querySelector("#lastCloseDetail"),
  toast: document.querySelector("#toast"),
};

function todayIso() {
  const now = new Date();
  const offset = now.getTimezoneOffset();
  const local = new Date(now.getTime() - offset * 60_000);
  return local.toISOString().slice(0, 10);
}

function money(value) {
  return currency.format(Number(value || 0));
}

function numericValue(input) {
  return Number.parseFloat(input.value || "0") || 0;
}

function showToast(message) {
  elements.toast.textContent = message;
  elements.toast.classList.add("show");
  window.clearTimeout(showToast.timeout);
  showToast.timeout = window.setTimeout(() => {
    elements.toast.classList.remove("show");
  }, 2600);
}

function setStatus(kind, text) {
  elements.connectionStatus.className = `status-pill ${kind}`;
  elements.connectionStatus.textContent = text;
}

async function loadDashboard() {
  const selectedDate = elements.dateInput.value || todayIso();
  setStatus("", "Cargando");

  try {
    const response = await fetch(`/api/corte?fecha=${encodeURIComponent(selectedDate)}`);
    if (!response.ok) {
      throw new Error(await response.text());
    }

    state.dashboard = await response.json();
    renderDashboard();
    setStatus("online", "En línea");
  } catch (error) {
    setStatus("error", "Error");
    showToast("No se pudo cargar el corte");
    console.error(error);
  }
}

function renderDashboard() {
  const dashboard = state.dashboard;
  if (!dashboard) {
    return;
  }

  renderPeriod(elements.daySales, elements.dayMeta, dashboard.dia);
  renderPeriod(elements.monthSales, elements.monthMeta, dashboard.mes);
  renderPeriod(elements.yearSales, elements.yearMeta, dashboard.anio);

  elements.dayCost.textContent = money(dashboard.dia.inversion);
  elements.dayProfit.textContent = money(dashboard.dia.utilidad);
  renderOrigins(dashboard.origenes || []);
  renderLastClose(dashboard.ultimoCorte);
  updateCashTotals();
}

function renderPeriod(amountElement, metaElement, period) {
  amountElement.textContent = money(period.ventas);
  metaElement.textContent = `${period.tickets} ticket${period.tickets === 1 ? "" : "s"}`;
}

function renderOrigins(origins) {
  elements.originCount.textContent = origins.length;
  elements.originList.innerHTML = "";

  if (origins.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty-state";
    empty.textContent = "Sin ventas registradas en esta fecha.";
    elements.originList.append(empty);
    return;
  }

  for (const origin of origins) {
    const row = document.createElement("div");
    row.className = "origin-row";
    row.innerHTML = `
      <strong>${escapeHtml(origin.origen)}</strong>
      <span class="amount">${money(origin.ventas)}</span>
      <small>${origin.tickets} ticket${origin.tickets === 1 ? "" : "s"}</small>
      <small>Utilidad ${money(origin.utilidad)}</small>
    `;
    elements.originList.append(row);
  }
}

function renderLastClose(lastClose) {
  if (!lastClose) {
    elements.lastClosePanel.hidden = true;
    return;
  }

  elements.lastClosePanel.hidden = false;
  const sign = lastClose.diferencia > 0 ? "sobran" : lastClose.diferencia < 0 ? "faltan" : "cuadra";
  elements.lastCloseTitle.textContent = `${lastClose.responsable} cerró con ${money(lastClose.efectivoContado)}`;
  elements.lastCloseDetail.textContent = `${sign}: ${money(Math.abs(lastClose.diferencia))} · ${new Date(lastClose.creadoEn).toLocaleString("es-MX")}`;
}

function renderDenominations() {
  elements.denominationList.innerHTML = "";

  for (const value of denominations) {
    state.denominationCounts.set(value, 0);

    const row = document.createElement("label");
    row.className = "denomination-row";
    row.innerHTML = `
      <strong>${money(value)}</strong>
      <input type="number" min="0" step="1" inputmode="numeric" value="0" aria-label="Cantidad de ${money(value)}">
      <output>${money(0)}</output>
    `;

    const input = row.querySelector("input");
    const output = row.querySelector("output");
    input.addEventListener("input", () => {
      const count = Math.max(0, Number.parseInt(input.value || "0", 10) || 0);
      state.denominationCounts.set(value, count);
      output.textContent = money(value * count);
      updateCashTotals();
    });

    elements.denominationList.append(row);
  }
}

function updateCashTotals() {
  const dashboard = state.dashboard;
  const counted = [...state.denominationCounts.entries()]
    .reduce((total, [value, count]) => total + value * count, 0);
  const openingCash = numericValue(elements.openingCashInput);
  const withdrawals = numericValue(elements.withdrawalsInput);
  const sales = Number(dashboard?.dia?.ventas || 0);
  const expected = openingCash + sales - withdrawals;
  const difference = counted - expected;

  elements.countedCash.textContent = money(counted);
  elements.expectedCash.textContent = money(expected);
  elements.differenceBadge.textContent = money(difference);
  elements.differenceBadge.classList.toggle("negative", difference < 0);
  elements.differenceBadge.classList.toggle("positive", difference >= 0);
}

async function saveCloseout() {
  if (state.saving) {
    return;
  }

  const responsable = elements.responsibleInput.value.trim();
  if (!responsable) {
    showToast("Falta el responsable");
    elements.responsibleInput.focus();
    return;
  }

  state.saving = true;
  elements.saveButton.disabled = true;
  elements.saveButton.textContent = "Guardando";

  try {
    const payload = {
      fecha: elements.dateInput.value,
      responsable,
      fondoInicial: numericValue(elements.openingCashInput),
      retiros: numericValue(elements.withdrawalsInput),
      efectivoContado: 0,
      denominaciones: [...state.denominationCounts.entries()]
        .filter(([, cantidad]) => cantidad > 0)
        .map(([valor, cantidad]) => ({ valor, cantidad })),
      notas: elements.notesInput.value,
    };

    const response = await fetch("/api/cortes", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      const problem = await response.json().catch(() => ({ message: "No se pudo guardar" }));
      throw new Error(problem.message || "No se pudo guardar");
    }

    showToast("Corte guardado");
    await loadDashboard();
  } catch (error) {
    showToast(error.message || "No se pudo guardar");
    console.error(error);
  } finally {
    state.saving = false;
    elements.saveButton.disabled = false;
    elements.saveButton.textContent = "Guardar corte";
  }
}

function escapeHtml(value) {
  const div = document.createElement("div");
  div.textContent = value;
  return div.innerHTML;
}

function registerServiceWorker() {
  if ("serviceWorker" in navigator) {
    navigator.serviceWorker.register("/service-worker.js").catch((error) => {
      console.warn("No se pudo registrar el service worker", error);
    });
  }
}

elements.dateInput.value = todayIso();
renderDenominations();
elements.refreshButton.addEventListener("click", loadDashboard);
elements.dateInput.addEventListener("change", loadDashboard);
elements.openingCashInput.addEventListener("input", updateCashTotals);
elements.withdrawalsInput.addEventListener("input", updateCashTotals);
elements.saveButton.addEventListener("click", saveCloseout);
registerServiceWorker();
loadDashboard();
