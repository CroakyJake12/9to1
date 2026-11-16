(() => {
    "use strict";

    performance.mark("9to1:host");
    try {
        const palette = JSON.parse(localStorage.getItem("9to1.cui.host-palette.v1"));
        if (Array.isArray(palette) && palette.length === 3 &&
            palette.every(value => typeof value === "string" && /^#[0-9a-f]{6}$/i.test(value))) {
            ["background", "foreground", "panel"].forEach((name, index) =>
                document.documentElement.style.setProperty("--cui-boot-" + name, palette[index]));
        }
    } catch { /* Unavailable presentation cache cannot block application startup. */ }

    // Install in the head so resource errors are handled even if the module never evaluates.
    document.addEventListener("error", event => {
        const appModule = document.getElementById("nine-to-one-main");
        if (!appModule || event.target !== appModule) return;
        const status = document.getElementById("browser-status");
        const retry = document.getElementById("browser-retry");
        status.textContent = "9to1 could not load its resources. Check your connection and try again.";
        status.dataset.code = "BrowserResourceUnavailable";
        retry.hidden = false;
    }, true);

    document.addEventListener("click", event => {
        const retry = document.getElementById("browser-retry");
        if (retry && event.target === retry) location.reload();
    });
})();
