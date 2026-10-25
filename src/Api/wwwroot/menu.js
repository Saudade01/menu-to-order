// Display only: the server prices every order from the published menu, whatever this script shows.
document.documentElement.classList.add("js");

const form = document.querySelector("form[data-cart]");
if (form) {
    const lira = new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 2 });
    const total = form.querySelector("[data-cart-total]");
    const fields = [...form.querySelectorAll("input.qty")];
    const clamp = value => Math.min(20, Math.max(0, parseInt(value, 10) || 0));

    const update = () => {
        let count = 0, sum = 0;
        for (const field of fields) {
            // Typing 25 shows 20: the box and the total never disagree with what the server accepts.
            if (field.value !== "" && String(clamp(field.value)) !== field.value) field.value = clamp(field.value);
            count += clamp(field.value);
            sum += clamp(field.value) * Number(field.dataset.price);
        }
        total.textContent = count ? `Sepet · ${count} ürün · ${lira.format(sum)} ₺` : "Siparişi gözden geçir";
    };

    form.addEventListener("click", event => {
        const step = event.target.closest("[data-step]");
        if (!step) return;
        const field = step.parentElement.querySelector("input.qty");
        field.value = clamp(field.value) + Number(step.dataset.step);
        field.value = clamp(field.value);
        update();
    });
    form.addEventListener("input", update);
    update();

    const tabs = new Map([...document.querySelectorAll(".tabs a")].map(a => [a.hash.slice(1), a]));
    const watch = new IntersectionObserver(entries => {
        for (const entry of entries.filter(e => e.isIntersecting)) {
            tabs.forEach(tab => tab.removeAttribute("aria-current"));
            const tab = tabs.get(entry.target.id);
            tab?.setAttribute("aria-current", "true");
            tab?.scrollIntoView({ block: "nearest", inline: "nearest" });
        }
    }, { rootMargin: "-25% 0px -70% 0px" });
    form.querySelectorAll("section[id]").forEach(section => watch.observe(section));
}
