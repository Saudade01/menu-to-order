// A second tap while the first request is still on its way would send the form twice.
document.addEventListener("submit", event => {
    if (event.target.dataset.sent) event.preventDefault();
    else event.target.dataset.sent = "true";
}, true);

// Coming back with the browser's back button must leave the forms usable again.
window.addEventListener("pageshow", event => {
    if (event.persisted) document.querySelectorAll("form[data-sent]").forEach(form => delete form.dataset.sent);
});

// A picked photo is shown with a way to drop it, since the phone's own picker cannot always be left without picking.
document.addEventListener("change", event => {
    const input = event.target;
    if (input.type !== "file") return;
    if (input.nextElementSibling?.matches(".picked")) input.nextElementSibling.remove();
    if (!input.files[0]) return;
    const picked = document.createElement("div");
    picked.className = "picked";
    picked.innerHTML = '<img alt="Seçilen fotoğraf"><button type="button" class="quiet">Fotoğrafı kaldır</button>';
    picked.querySelector("img").src = URL.createObjectURL(input.files[0]);
    picked.querySelector("button").addEventListener("click", () => { input.value = ""; picked.remove(); });
    input.after(picked);
});

// A phone photo carries where it was taken. Redrawing it drops that, keeps it upright and shrinks it for the upload.
// A file without camera data (such as the sample menus, replayed by their exact bytes) is sent as it is.
async function withoutLocation(file) {
    try {
        const head = new TextDecoder("latin1").decode(await file.slice(0, 64 * 1024).arrayBuffer());
        if (!head.includes("Exif\0\0")) return file;
        const image = await createImageBitmap(file, { imageOrientation: "from-image" });
        const scale = Math.min(1, 2400 / Math.max(image.width, image.height));
        const canvas = document.createElement("canvas");
        canvas.width = Math.round(image.width * scale);
        canvas.height = Math.round(image.height * scale);
        canvas.getContext("2d").drawImage(image, 0, 0, canvas.width, canvas.height);
        const blob = await new Promise(done => canvas.toBlob(done, "image/jpeg", 0.9));
        return blob ? new File([blob], "menu.jpg", { type: "image/jpeg" }) : file;
    } catch {
        return file;
    }
}

// The Google app's in-app browser silently drops every form post, so forms go up with fetch instead.
document.addEventListener("submit", async event => {
    const form = event.target;
    // Read as attributes: a field named "action" would shadow form.action.
    if (event.defaultPrevented || form.getAttribute("method") !== "post") return;
    event.preventDefault();
    const button = event.submitter, label = button?.textContent;
    if (button) button.textContent = form.getAttribute("enctype") ? "Yükleniyor…" : "Gönderiliyor…";
    try {
        const body = new FormData(form, button);
        for (const [name, value] of [...body])
            if (value instanceof File && value.size > 0) body.set(name, await withoutLocation(value));
        const response = await fetch(form.getAttribute("action"), { method: "post", body });
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
