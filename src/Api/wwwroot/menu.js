// Display only: the server prices every order from the published menu, whatever this script shows.
// In a block, so it can run again when the answer to a posted form replaces the page.
{
    document.documentElement.classList.add("js");

    // A placed order empties the cart kept for its menu.
    const placed = document.querySelector("[data-placed]");
    if (placed) try { sessionStorage.removeItem("cart:" + placed.dataset.placed); } catch { }

    const form = document.querySelector("form[data-cart]");
    if (form) {
        const lira = new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 2 });
        const total = form.querySelector("[data-cart-total]");
        const fields = [...form.querySelectorAll("input.qty")];
        const clamp = value => Math.min(20, Math.max(0, parseInt(value, 10) || 0));

        // The cart is kept in this tab too, so a reload, or an in-app browser that reloads on a tab tap, does not empty it.
        const key = "cart:" + form.elements.menu.value;
        try {
            const kept = JSON.parse(sessionStorage.getItem(key) ?? "{}");
            for (const field of fields) if (kept[field.name]) field.value = clamp(kept[field.name]);
            if (kept.note) form.elements.note.value = kept.note;
        } catch { }
        const save = () => {
            const kept = Object.fromEntries(fields.filter(f => clamp(f.value) > 0).map(f => [f.name, clamp(f.value)]));
            if (form.elements.note.value) kept.note = form.elements.note.value;
            try { sessionStorage.setItem(key, JSON.stringify(kept)); } catch { }
        };

        const update = () => {
            let count = 0, sum = 0;
            for (const field of fields) {
                // Typing 25 shows 20: the box and the total never disagree with what the server accepts.
                if (field.value !== "" && String(clamp(field.value)) !== field.value) field.value = clamp(field.value);
                count += clamp(field.value);
                sum += clamp(field.value) * Number(field.dataset.price);
            }
            total.textContent = count ? `Sepet · ${count} ürün · ${lira.format(sum)} ₺` : "Siparişi gözden geçir";
            save();
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

        // Scrolled in place rather than followed as a link, since some in-app browsers reload the page on "#section".
        document.querySelector(".tabs")?.addEventListener("click", event => {
            const tab = event.target.closest("a");
            if (!tab) return;
            event.preventDefault();
            document.getElementById(tab.hash.slice(1))?.scrollIntoView();
        });

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
}
