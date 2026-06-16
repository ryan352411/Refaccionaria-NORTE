const currency = new Intl.NumberFormat("es-MX", {
  style: "currency",
  currency: "MXN",
});

const numberFormat = new Intl.NumberFormat("es-MX", {
  maximumFractionDigits: 3,
});

const state = {
  categorias: [],
  pagina: 1,
  limite: 24,
  totalPaginas: 1,
  loading: false,
};

const elements = {
  form: document.querySelector("#catalogForm"),
  searchInput: document.querySelector("#searchInput"),
  categorySelect: document.querySelector("#categorySelect"),
  stockToggle: document.querySelector("#stockToggle"),
  categoryChips: document.querySelector("#categoryChips"),
  productGrid: document.querySelector("#productGrid"),
  connectionStatus: document.querySelector("#connectionStatus"),
  availableCount: document.querySelector("#availableCount"),
  categoryCount: document.querySelector("#categoryCount"),
  totalCount: document.querySelector("#totalCount"),
  resultTitle: document.querySelector("#resultTitle"),
  resultMeta: document.querySelector("#resultMeta"),
  prevButton: document.querySelector("#prevButton"),
  nextButton: document.querySelector("#nextButton"),
  pageMeta: document.querySelector("#pageMeta"),
  toast: document.querySelector("#toast"),
};

function money(value) {
  return currency.format(Number(value || 0));
}

function quantity(value) {
  const numeric = Number(value || 0);
  return numberFormat.format(numeric);
}

function setStatus(kind, text) {
  elements.connectionStatus.className = `status-pill ${kind}`;
  elements.connectionStatus.textContent = text;
}

function showToast(message) {
  elements.toast.textContent = message;
  elements.toast.classList.add("show");
  window.clearTimeout(showToast.timeout);
  showToast.timeout = window.setTimeout(() => {
    elements.toast.classList.remove("show");
  }, 2600);
}

async function fetchJson(url) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(await response.text());
  }

  return response.json();
}

async function loadInitialData() {
  setStatus("", "Cargando");

  try {
    const [resumen, categorias] = await Promise.all([
      fetchJson("/api/catalogo/resumen"),
      fetchJson("/api/catalogo/categorias"),
    ]);

    state.categorias = categorias;
    renderSummary(resumen);
    renderCategories(categorias);
    await loadProducts();
    setStatus("online", "En linea");
  } catch (error) {
    console.error(error);
    setStatus("error", "Sin conexion");
    showToast("No se pudo cargar el catalogo");
    renderErrorState();
  }
}

async function loadProducts() {
  if (state.loading) {
    return;
  }

  state.loading = true;
  renderLoadingState();

  const params = new URLSearchParams({
    buscar: elements.searchInput.value.trim(),
    categoria: elements.categorySelect.value,
    soloDisponibles: String(elements.stockToggle.checked),
    pagina: String(state.pagina),
    limite: String(state.limite),
  });

  try {
    const result = await fetchJson(`/api/catalogo/productos?${params}`);
    state.totalPaginas = result.totalPaginas;
    renderProducts(result.productos || []);
    renderResultMeta(result.total, result.pagina, result.totalPaginas);
    setStatus("online", "En linea");
  } catch (error) {
    console.error(error);
    setStatus("error", "Sin conexion");
    showToast("No se pudo actualizar el catalogo");
    renderErrorState();
  } finally {
    state.loading = false;
  }
}

function renderSummary(resumen) {
  elements.availableCount.textContent = resumen.disponibles || 0;
  elements.categoryCount.textContent = resumen.categorias || 0;
  elements.totalCount.textContent = resumen.total || 0;
}

function renderCategories(categorias) {
  elements.categorySelect.innerHTML = '<option value="">Todas</option>';
  elements.categoryChips.innerHTML = "";

  for (const categoria of categorias) {
    const option = document.createElement("option");
    option.value = categoria.nombre;
    option.textContent = `${categoria.nombre} (${categoria.disponibles})`;
    elements.categorySelect.append(option);

    const chip = document.createElement("button");
    chip.type = "button";
    chip.className = "category-chip";
    chip.dataset.category = categoria.nombre;
    chip.textContent = `${categoria.nombre} ${categoria.disponibles}`;
    chip.addEventListener("click", () => selectCategory(categoria.nombre));
    elements.categoryChips.append(chip);
  }
}

function selectCategory(categoria) {
  elements.categorySelect.value = categoria;
  state.pagina = 1;
  syncActiveCategory();
  loadProducts();
}

function syncActiveCategory() {
  const selected = elements.categorySelect.value;
  document.querySelectorAll(".category-chip").forEach((chip) => {
    chip.classList.toggle("active", chip.dataset.category === selected);
  });
}

function renderLoadingState() {
  elements.productGrid.innerHTML = '<div class="loading-state">Cargando catalogo...</div>';
}

function renderErrorState() {
  elements.productGrid.innerHTML = '<div class="empty-state">El catalogo no esta disponible por el momento.</div>';
  renderResultMeta(0, 1, 1);
}

function renderProducts(productos) {
  elements.productGrid.innerHTML = "";

  if (productos.length === 0) {
    elements.productGrid.innerHTML = '<div class="empty-state">No hay productos con esos filtros.</div>';
    return;
  }

  const fragment = document.createDocumentFragment();
  for (const producto of productos) {
    fragment.append(createProductCard(producto));
  }
  elements.productGrid.append(fragment);
}

function createProductCard(producto) {
  const card = document.createElement("article");
  card.className = "product-card";

  const media = document.createElement("div");
  media.className = "product-media";

  if (producto.imagenUrl) {
    const image = document.createElement("img");
    image.src = producto.imagenUrl;
    image.alt = producto.nombre;
    image.loading = "lazy";
    image.addEventListener("error", () => renderPlaceholder(media, producto.categoria));
    media.append(image);
  } else {
    renderPlaceholder(media, producto.categoria);
  }

  const availability = document.createElement("span");
  availability.className = availabilityClass(producto.estado);
  availability.textContent = producto.estado;
  media.append(availability);

  const body = document.createElement("div");
  body.className = "product-body";

  const meta = document.createElement("div");
  meta.className = "product-meta";
  meta.append(textElement("span", producto.categoria));

  const title = textElement("h3", producto.nombre, "product-title");
  const desc = textElement("p", producto.descripcion || "Producto de mostrador.", "product-desc");

  const footer = document.createElement("div");
  footer.className = "product-footer";
  footer.append(textElement("strong", money(producto.precioVenta), "price"));
  footer.append(textElement("span", `${quantity(producto.stockActual)} ${unitLabel(producto.tipoVenta)}`, "stock"));

  body.append(meta, title, desc, footer);
  card.append(media, body);
  return card;
}

function renderPlaceholder(container, categoria) {
  container.querySelector("img")?.remove();
  if (container.querySelector(".product-placeholder")) {
    return;
  }

  const placeholder = document.createElement("div");
  placeholder.className = "product-placeholder";
  placeholder.textContent = initials(categoria);
  container.prepend(placeholder);
}

function availabilityClass(estado) {
  if (estado === "Agotado") {
    return "availability out";
  }

  if (estado === "Pocas piezas") {
    return "availability low";
  }

  return "availability";
}

function unitLabel(tipoVenta) {
  if (!tipoVenta || tipoVenta === "Unidad") {
    return "pzas";
  }

  return tipoVenta.toLowerCase();
}

function initials(value) {
  return (value || "RN")
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0])
    .join("")
    .toUpperCase();
}

function textElement(tagName, value, className = "") {
  const element = document.createElement(tagName);
  if (className) {
    element.className = className;
  }
  element.textContent = value;
  return element;
}

function renderResultMeta(total, pagina, totalPaginas) {
  const selectedCategory = elements.categorySelect.value;
  elements.resultTitle.textContent = selectedCategory ? selectedCategory : "Productos disponibles";
  elements.resultMeta.textContent = `${total} resultado${total === 1 ? "" : "s"}`;
  elements.pageMeta.textContent = `Pagina ${pagina} de ${totalPaginas}`;
  elements.prevButton.disabled = pagina <= 1;
  elements.nextButton.disabled = pagina >= totalPaginas;
}

function scheduleSearch() {
  window.clearTimeout(scheduleSearch.timeout);
  scheduleSearch.timeout = window.setTimeout(() => {
    state.pagina = 1;
    loadProducts();
  }, 220);
}

function registerServiceWorker() {
  if ("serviceWorker" in navigator) {
    navigator.serviceWorker.register("/service-worker.js").catch((error) => {
      console.warn("No se pudo registrar el service worker", error);
    });
  }
}

document.querySelector('[data-category=""]').addEventListener("click", () => selectCategory(""));
elements.form.addEventListener("submit", (event) => {
  event.preventDefault();
  state.pagina = 1;
  loadProducts();
});
elements.searchInput.addEventListener("input", scheduleSearch);
elements.categorySelect.addEventListener("change", () => {
  state.pagina = 1;
  syncActiveCategory();
  loadProducts();
});
elements.stockToggle.addEventListener("change", () => {
  state.pagina = 1;
  loadProducts();
});
elements.prevButton.addEventListener("click", () => {
  state.pagina = Math.max(1, state.pagina - 1);
  loadProducts();
});
elements.nextButton.addEventListener("click", () => {
  state.pagina = Math.min(state.totalPaginas, state.pagina + 1);
  loadProducts();
});

syncActiveCategory();
registerServiceWorker();
loadInitialData();
