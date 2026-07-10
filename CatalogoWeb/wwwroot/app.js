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
  card.tabIndex = 0;
  card.setAttribute("role", "button");
  card.setAttribute("aria-label", `Ver detalles de ${producto.nombre}`);

  const media = buildMediaGallery(producto);

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

  card.addEventListener("click", (event) => {
    if (event.target.closest(".image-nav")) {
      return;
    }
    openProductModal(producto);
  });

  card.addEventListener("keydown", (event) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      openProductModal(producto);
    }
  });

  return card;
}

function buildMediaGallery(producto) {
  const media = document.createElement("div");
  media.className = "product-media";
  const imagenes = Array.isArray(producto.imagenes) && producto.imagenes.length > 0
    ? producto.imagenes
    : producto.imagenUrl
      ? [producto.imagenUrl]
      : [];
  let imagenActual = 0;

  if (imagenes.length > 0) {
    const image = document.createElement("img");
    image.alt = producto.nombre;
    image.loading = "lazy";
    image.addEventListener("error", () => renderPlaceholder(media, producto.categoria));
    media.append(image);
    renderProductImage(image, imagenes, imagenActual);

    if (imagenes.length > 1) {
      const previous = imageNavButton("Anterior", "<");
      const next = imageNavButton("Siguiente", ">");
      const counter = document.createElement("span");
      counter.className = "image-count";

      const updateImage = () => {
        renderProductImage(image, imagenes, imagenActual);
        counter.textContent = `${imagenActual + 1}/${imagenes.length}`;
      };

      previous.addEventListener("click", (event) => {
        event.preventDefault();
        imagenActual = (imagenActual - 1 + imagenes.length) % imagenes.length;
        updateImage();
      });

      next.addEventListener("click", (event) => {
        event.preventDefault();
        imagenActual = (imagenActual + 1) % imagenes.length;
        updateImage();
      });

      updateImage();
      media.append(previous, next, counter);
    }
  } else {
    renderPlaceholder(media, producto.categoria);
  }

  const availability = document.createElement("span");
  availability.className = availabilityClass(producto.estado);
  availability.textContent = producto.estado;
  media.append(availability);

  return media;
}

function renderProductImage(image, imagenes, index) {
  image.parentElement?.querySelector(".product-placeholder")?.remove();
  image.src = imagenes[index];
}

function imageNavButton(label, text) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = `image-nav ${label === "Anterior" ? "prev" : "next"}`;
  button.setAttribute("aria-label", label);
  button.textContent = text;
  return button;
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

const modalElements = {
  overlay: document.querySelector("#productModal"),
  close: document.querySelector("#modalClose"),
  media: document.querySelector("#modalMedia"),
  category: document.querySelector("#modalCategory"),
  title: document.querySelector("#modalTitle"),
  desc: document.querySelector("#modalDesc"),
  price: document.querySelector("#modalPrice"),
  stock: document.querySelector("#modalStock"),
  unit: document.querySelector("#modalUnit"),
};

function openProductModal(producto) {
  modalElements.category.textContent = producto.categoria || "General";
  modalElements.title.textContent = producto.nombre;
  modalElements.desc.textContent = producto.descripcion || "Producto de mostrador.";

  const esGranel = producto.tipoVenta && producto.tipoVenta !== "Unidad";
  modalElements.price.textContent = esGranel
    ? `${money(producto.precioVenta)} por ${producto.tipoVenta.toLowerCase()}`
    : money(producto.precioVenta);
  modalElements.stock.textContent = `${quantity(producto.stockActual)} ${unitLabel(producto.tipoVenta)}`;
  modalElements.unit.textContent = producto.tipoVenta || "Unidad";

  modalElements.media.innerHTML = "";
  modalElements.media.append(buildMediaGallery(producto));

  modalElements.overlay.hidden = false;
  document.body.classList.add("modal-open");
  modalElements.close.focus();
}

function closeProductModal() {
  modalElements.overlay.hidden = true;
  modalElements.media.innerHTML = "";
  document.body.classList.remove("modal-open");
}

modalElements.close.addEventListener("click", closeProductModal);
modalElements.overlay.addEventListener("click", (event) => {
  if (event.target === modalElements.overlay) {
    closeProductModal();
  }
});
document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && !modalElements.overlay.hidden) {
    closeProductModal();
  }
});

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
