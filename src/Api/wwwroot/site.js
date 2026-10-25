// A second tap while the first request is still on its way would send the form twice.
document.addEventListener("submit", event => {
    if (event.target.dataset.sent) event.preventDefault();
    else event.target.dataset.sent = "true";
}, true);

// Coming back with the browser's back button must leave the forms usable again.
window.addEventListener("pageshow", event => {
    if (event.persisted) document.querySelectorAll("form[data-sent]").forEach(form => delete form.dataset.sent);
});
