const API_BASE = "/api";

const STORAGE_KEYS = {
    favorites: "favorites",
    chatMessages: "chat"
};

const elements = {
    pages: [...document.querySelectorAll(".page")],
    navItems: [...document.querySelectorAll(".nav-item")],
    uniList: document.getElementById("uniList"),
    favList: document.getElementById("favList"),
    aiResult: document.getElementById("aiResult"),
    chartCanvas: document.getElementById("chart"),
    searchInput: document.getElementById("searchInput"),
    cityFilter: document.getElementById("cityFilter"),
    scoreTypeFilter: document.getElementById("scoreTypeFilter"),
    searchSummary: document.getElementById("searchSummary"),
    loadMoreButton: document.getElementById("loadMoreButton"),
    rankInput: document.getElementById("rankInput"),
    scoreInput: document.getElementById("scoreInput"),
    scoreTypeSelect: document.getElementById("scoreTypeSelect"),
    citySelect: document.getElementById("citySelect"),
    aiButton: document.getElementById("aiButton"),
    aiSummary: document.getElementById("aiSummary"),
    chatBox: document.getElementById("chatBox"),
    chatInput: document.getElementById("chatInput"),
    sendButton: document.getElementById("sendButton")
};

const appState = {
    metadata: null,
    programs: [],
    currentProgramMap: new Map(),
    favorites: loadStoredList(STORAGE_KEYS.favorites, []).filter((favorite) => {
        return favorite && typeof favorite === "object" && favorite.yopCode;
    }),
    chatMessages: loadStoredList(STORAGE_KEYS.chatMessages, []),
    search: {
        query: "",
        city: "",
        scoreType: "say",
        page: 1,
        pageSize: 24
    }
};

let rankingChart = null;

initializeApp();

async function initializeApp() {
    bindEvents();
    renderChatMessages();
    renderFavorites();

    if (window.location.protocol === "file:") {
        showBackendMessage(
            "Bu ekranı backend ile açman gerekiyor. `dotnet run --project TercihAI.Backend` komutunu çalıştırıp tarayıcıda verilen adresi aç."
        );
        return;
    }

    try {
        await loadMetadata();
        await loadPrograms({ reset: true });
    } catch (error) {
        showBackendMessage(error.message);
    }
}

function bindEvents() {
    elements.navItems.forEach((item) => {
        item.addEventListener("click", () => showPage(item.dataset.page));
    });

    elements.searchInput.addEventListener(
        "input",
        debounce(() => {
            appState.search.query = elements.searchInput.value.trim();
            loadPrograms({ reset: true }).catch(handleProgramLoadError);
        }, 350)
    );

    elements.cityFilter.addEventListener("change", () => {
        appState.search.city = elements.cityFilter.value;
        loadPrograms({ reset: true }).catch(handleProgramLoadError);
    });

    elements.scoreTypeFilter.addEventListener("change", () => {
        appState.search.scoreType = elements.scoreTypeFilter.value;
        elements.scoreTypeSelect.value = elements.scoreTypeFilter.value;
        loadPrograms({ reset: true }).catch(handleProgramLoadError);
    });

    elements.loadMoreButton.addEventListener("click", () => {
        appState.search.page += 1;
        loadPrograms({ reset: false }).catch(handleProgramLoadError);
    });

    elements.aiButton.addEventListener("click", runAI);
    elements.sendButton.addEventListener("click", sendMessage);

    elements.chatInput.addEventListener("keydown", (event) => {
        if (event.key === "Enter") {
            sendMessage();
        }
    });

    elements.uniList.addEventListener("click", handleUniversityCardClick);
    elements.favList.addEventListener("click", handleFavoriteCardClick);
}

function showPage(pageId) {
    elements.pages.forEach((page) => {
        page.classList.toggle("active", page.id === pageId);
    });

    elements.navItems.forEach((item) => {
        item.classList.toggle("active", item.dataset.page === pageId);
    });

    if (pageId === "favoriler") {
        renderFavorites();
    }
}

async function loadMetadata() {
    const metadata = await fetchJson(`${API_BASE}/meta`);

    appState.metadata = metadata;
    populateSelect(elements.cityFilter, metadata.cities, "Şehir");
    populateSelect(elements.citySelect, metadata.cities, "Şehir seç");
    populateSelect(elements.scoreTypeFilter, metadata.scoreTypes, "Puan Türü", formatScoreType);
    populateSelect(elements.scoreTypeSelect, metadata.scoreTypes, "Puan türü seç", formatScoreType);

    elements.scoreTypeFilter.value = appState.search.scoreType;
    elements.scoreTypeSelect.value = appState.search.scoreType;
    elements.aiSummary.textContent = metadata.forecastMethod;
}

function populateSelect(selectElement, values, defaultLabel, labelFormatter = (value) => value) {
    selectElement.innerHTML = `<option value="">${defaultLabel}</option>`;

    values.forEach((value) => {
        selectElement.innerHTML += `<option value="${value}">${labelFormatter(value)}</option>`;
    });
}

function formatScoreType(scoreType) {
    return scoreType.toLocaleUpperCase("tr-TR");
}

async function loadPrograms({ reset }) {
    if (reset) {
        appState.search.page = 1;
        appState.programs = [];
        elements.uniList.innerHTML = createInfoCard("Veriler yükleniyor...");
    }

    const params = new URLSearchParams({
        page: String(appState.search.page),
        pageSize: String(appState.search.pageSize),
        scoreType: appState.search.scoreType
    });

    if (appState.search.query !== "") {
        params.set("query", appState.search.query);
    }

    if (appState.search.city !== "") {
        params.set("city", appState.search.city);
    }

    const response = await fetchJson(`${API_BASE}/programs/search?${params.toString()}`);

    const nextItems = reset
        ? response.items
        : [...appState.programs, ...response.items.filter((item) => !appState.currentProgramMap.has(item.yopCode))];

    appState.programs = nextItems;
    rebuildCurrentProgramMap();
    refreshFavoritesWithLatestData();
    renderUniversityList(appState.programs);
    updateSearchSummary(response.totalFiltered, appState.programs.length);
    updateLoadMoreButton(response.totalFiltered);
}

function rebuildCurrentProgramMap() {
    appState.currentProgramMap = new Map(
        appState.programs.map((program) => [program.yopCode, program])
    );
}

function refreshFavoritesWithLatestData() {
    appState.favorites = appState.favorites.map((favorite) => {
        return appState.currentProgramMap.get(favorite.yopCode) || favorite;
    });

    saveList(STORAGE_KEYS.favorites, appState.favorites);
    renderFavorites();
}

function updateSearchSummary(totalFiltered, shownCount) {
    elements.searchSummary.textContent =
        `${formatNumber(totalFiltered)} sonuç bulundu. Şu an ${formatNumber(shownCount)} kayıt gösteriliyor.`;
}

function updateLoadMoreButton(totalFiltered) {
    const hasMore = appState.programs.length < totalFiltered;
    elements.loadMoreButton.style.display = hasMore ? "inline-block" : "none";
}

function renderUniversityList(programList) {
    if (programList.length === 0) {
        elements.uniList.innerHTML = createInfoCard("Bu filtrelere uygun program bulunamadı.");
        return;
    }

    elements.uniList.innerHTML = programList.map((program) => createProgramCard(program, false)).join("");
}

function createProgramCard(program, favoriteMode) {
    const year2024 = program.years["2024"];
    const year2025 = program.years["2025"];
    const year2026 = program.years["2026"];

    return `
        <div class="card">
            <h3>${program.programName}</h3>
            <p>${program.universityName}</p>
            <p>${program.faculty}</p>
            <p>${program.city} • ${program.universityType}</p>
            <p>${program.programDetails || "Detay bilgisi yok"}</p>
            <div class="metrics">
                <p><strong>2024:</strong> ${formatMetric(year2024)}</p>
                <p><strong>2025:</strong> ${formatMetric(year2025)}</p>
                <p><strong>2026 Tahmini:</strong> ${formatMetric(year2026)}</p>
            </div>
            <button
                type="button"
                ${favoriteMode ? `data-remove-favorite="${program.yopCode}"` : `data-favorite="${program.yopCode}"`}>
                ${favoriteMode ? "Favoriden Çıkar" : "Favorilere Ekle"}
            </button>
        </div>
    `;
}

function formatMetric(metric) {
    if (!metric) {
        return "Veri yok";
    }

    const scoreText = metric.score !== null && metric.score !== undefined
        ? `Puan ${formatScore(metric.score)}`
        : "Puan yok";
    const rankingText = metric.ranking !== null && metric.ranking !== undefined
        ? `Sıralama ${formatNumber(metric.ranking)}`
        : "Sıralama yok";

    return `${scoreText} • ${rankingText}`;
}

function handleUniversityCardClick(event) {
    const favoriteButton = event.target.closest("[data-favorite]");

    if (!favoriteButton) {
        return;
    }

    addFavorite(favoriteButton.dataset.favorite);
}

function addFavorite(yopCode) {
    const program = appState.currentProgramMap.get(yopCode);

    if (!program) {
        return;
    }

    if (appState.favorites.some((favorite) => favorite.yopCode === yopCode)) {
        return;
    }

    appState.favorites.unshift(program);
    saveList(STORAGE_KEYS.favorites, appState.favorites);
    renderFavorites();
}

function renderFavorites() {
    if (appState.favorites.length === 0) {
        elements.favList.innerHTML = createInfoCard("Henüz favori eklenmedi.");
        return;
    }

    elements.favList.innerHTML = appState.favorites
        .map((program) => createProgramCard(program, true))
        .join("");
}

function handleFavoriteCardClick(event) {
    const removeButton = event.target.closest("[data-remove-favorite]");

    if (!removeButton) {
        return;
    }

    removeFavorite(removeButton.dataset.removeFavorite);
}

function removeFavorite(yopCode) {
    appState.favorites = appState.favorites.filter((favorite) => favorite.yopCode !== yopCode);
    saveList(STORAGE_KEYS.favorites, appState.favorites);
    renderFavorites();
}

async function runAI() {
    const targetRank = Number(elements.rankInput.value);
    const targetScore = Number(elements.scoreInput.value);
    const selectedCity = elements.citySelect.value;
    const scoreType = elements.scoreTypeSelect.value || appState.search.scoreType || "say";

    const params = new URLSearchParams({
        scoreType,
        page: "1",
        pageSize: "100"
    });

    if (selectedCity !== "") {
        params.set("city", selectedCity);
    }

    if (targetRank > 0) {
        params.set("minRank", String(Math.max(1, Math.floor(targetRank * 0.75))));
        params.set("maxRank", String(Math.ceil(targetRank * 1.25)));
    }

    elements.aiResult.innerHTML = createInfoCard("Öneriler hazırlanıyor...");

    try {
        const response = await fetchJson(`${API_BASE}/programs/search?${params.toString()}`);
        const recommendations = response.items
            .filter((program) => isSuitableProgram(program, targetRank, targetScore))
            .sort((firstProgram, secondProgram) => comparePrograms(firstProgram, secondProgram, targetRank, targetScore))
            .slice(0, 10);

        renderAIResults(recommendations);
        drawChart(recommendations);

        elements.aiSummary.textContent =
            recommendations.length === 0
                ? "Girilen değerlere göre öneri bulunamadı."
                : `${recommendations.length} öneri bulundu. Sıralama, 2026 tahmini taban sıralamasına göre gösteriliyor.`;
    } catch (error) {
        elements.aiResult.innerHTML = createInfoCard(error.message);
        elements.aiSummary.textContent = "AI önerileri alınamadı.";
    }
}

function isSuitableProgram(program, targetRank, targetScore) {
    const estimatedYear = program.years["2026"] || program.years["2025"];
    const estimatedRanking = estimatedYear?.ranking;
    const estimatedScore = estimatedYear?.score;

    const rankMatches = targetRank === 0 || estimatedRanking === null || estimatedRanking === undefined
        ? true
        : estimatedRanking <= Math.ceil(targetRank * 1.25);
    const scoreMatches = targetScore === 0 || estimatedScore === null || estimatedScore === undefined
        ? true
        : estimatedScore <= targetScore + 20;

    return rankMatches && scoreMatches;
}

function comparePrograms(firstProgram, secondProgram, targetRank, targetScore) {
    const firstDistance = calculateDistance(firstProgram, targetRank, targetScore);
    const secondDistance = calculateDistance(secondProgram, targetRank, targetScore);

    return firstDistance - secondDistance;
}

function calculateDistance(program, targetRank, targetScore) {
    const estimatedYear = program.years["2026"] || program.years["2025"];
    const ranking = estimatedYear?.ranking ?? Number.MAX_SAFE_INTEGER;
    const score = estimatedYear?.score ?? 0;
    const rankDistance = targetRank > 0 ? Math.abs(ranking - targetRank) : ranking;
    const scoreDistance = targetScore > 0 ? Math.abs(score - targetScore) : 0;

    return rankDistance + scoreDistance * 100;
}

function renderAIResults(programList) {
    if (programList.length === 0) {
        elements.aiResult.innerHTML = createInfoCard("Bu filtrelere uygun öneri bulunamadı.");
        return;
    }

    elements.aiResult.innerHTML = programList.map((program) => {
        const estimatedYear = program.years["2026"];
        const latestYear = program.years["2025"];

        return `
            <div class="card">
                <h3>${program.programName}</h3>
                <p>${program.universityName}</p>
                <p>${program.city}</p>
                <div class="metrics">
                    <p><strong>2025 Gerçek:</strong> ${formatMetric(latestYear)}</p>
                    <p><strong>2026 Tahmini:</strong> ${formatMetric(estimatedYear)}</p>
                </div>
            </div>
        `;
    }).join("");
}

function drawChart(programList) {
    const labels = programList.map((program) => {
        return `${program.universityName} - ${program.programName}`;
    });

    const rankingValues = programList.map((program) => {
        return program.years["2026"]?.ranking ?? program.years["2025"]?.ranking ?? null;
    });

    const context = elements.chartCanvas.getContext("2d");

    if (rankingChart) {
        rankingChart.destroy();
    }

    rankingChart = new Chart(context, {
        type: "bar",
        data: {
            labels,
            datasets: [
                {
                    label: "2026 Tahmini Taban Sıralama",
                    data: rankingValues,
                    backgroundColor: "#00cec9"
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: true,
            plugins: {
                legend: {
                    labels: {
                        color: "#111827"
                    }
                }
            },
            scales: {
                y: {
                    ticks: {
                        callback(value) {
                            return formatNumber(value);
                        }
                    }
                }
            }
        }
    });
}

function sendMessage() {
    const messageText = elements.chatInput.value.trim();

    if (messageText === "") {
        return;
    }

    appState.chatMessages.push({ text: messageText, type: "user" });
    appState.chatMessages.push({
        text: "Mesaj kaydedildi. Bu alan şu an örnek sohbet kutusu olarak çalışıyor.",
        type: "bot"
    });

    saveList(STORAGE_KEYS.chatMessages, appState.chatMessages);
    renderChatMessages();

    elements.chatInput.value = "";
    elements.chatInput.focus();
}

function renderChatMessages() {
    if (appState.chatMessages.length === 0) {
        elements.chatBox.innerHTML = createInfoCard("Sohbet başlatmak için bir mesaj yaz.");
        return;
    }

    elements.chatBox.innerHTML = appState.chatMessages
        .map((message) => `<div class="msg ${message.type}">${message.text}</div>`)
        .join("");

    elements.chatBox.scrollTop = elements.chatBox.scrollHeight;
}

async function fetchJson(url) {
    const response = await fetch(url);

    if (!response.ok) {
        let errorMessage = "Veri alınırken bir sorun oluştu.";

        try {
            const errorData = await response.json();
            errorMessage = errorData.detail || errorData.title || errorMessage;
        } catch (error) {
            errorMessage = `${errorMessage} (HTTP ${response.status})`;
        }

        throw new Error(errorMessage);
    }

    return response.json();
}

function loadStoredList(key, fallbackValue) {
    const storedValue = localStorage.getItem(key);

    if (!storedValue) {
        return [...fallbackValue];
    }

    try {
        const parsedValue = JSON.parse(storedValue);
        return Array.isArray(parsedValue) ? parsedValue : [...fallbackValue];
    } catch (error) {
        return [...fallbackValue];
    }
}

function saveList(key, value) {
    localStorage.setItem(key, JSON.stringify(value));
}

function createInfoCard(message) {
    return `
        <div class="card">
            <p>${message}</p>
        </div>
    `;
}

function showBackendMessage(message) {
    elements.searchSummary.textContent = message;
    elements.uniList.innerHTML = createInfoCard(message);
    elements.aiSummary.textContent = message;
    elements.aiResult.innerHTML = createInfoCard(message);
}

function handleProgramLoadError(error) {
    showBackendMessage(error.message || "Program verileri alınamadı.");
}

function formatNumber(value) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) {
        return "Yok";
    }

    return Number(value).toLocaleString("tr-TR");
}

function formatScore(value) {
    return Number(value).toLocaleString("tr-TR", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 5
    });
}

function debounce(callback, wait) {
    let timeoutId;

    return (...args) => {
        clearTimeout(timeoutId);
        timeoutId = setTimeout(() => callback(...args), wait);
    };
}
