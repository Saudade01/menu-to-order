// A second tap while the first request is still on its way would send the form twice.
document.addEventListener("submit", event => {
    if (event.target.dataset.sent) event.preventDefault();
    else event.target.dataset.sent = "true";
}, true);

// Coming back with the browser's back button must leave the forms usable again.
window.addEventListener("pageshow", event => {
    if (event.persisted) document.querySelectorAll("form[data-sent]").forEach(form => delete form.dataset.sent);
});

// The Google app's in-app browser silently drops every form post, so forms go up with fetch instead.
document.addEventListener("submit", async event => {
    const form = event.target;
    // Read as attributes: a field named "action" would shadow form.action.
    if (event.defaultPrevented || form.getAttribute("method") !== "post") return;
    event.preventDefault();
    const button = event.submitter, label = button?.textContent;
    if (button) button.textContent = form.getAttribute("enctype") ? "Yükleniyor…" : "Gönderiliyor…";
    try {
        const response = await fetch(form.getAttribute("action"), { method: "post", body: new FormData(form, button) });
        if (response.redirected) return location.assign(response.url);
        // A page that answers the post itself (the cart, a refusal) replaces this one.
        document.open();
        document.write(await response.text());
        document.close();
    } catch {
        delete form.dataset.sent;
        if (button) button.textContent = label;
        alert("Gönderilemedi. Bağlantınızı kontrol edip tekrar deneyin.");
    }
});
