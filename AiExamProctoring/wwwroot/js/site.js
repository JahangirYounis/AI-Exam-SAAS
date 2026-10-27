// Shared UI helpers for the AI Based Online Examination System.
(function () {
    "use strict";

    // Bootstrap alerts auto-dismiss after a short delay.
    document.addEventListener("DOMContentLoaded", function () {
        document.querySelectorAll(".alert-success.alert-dismissible").forEach(function (alert) {
            window.setTimeout(function () {
                if (window.bootstrap && bootstrap.Alert.getOrCreateInstance(alert)) {
                    bootstrap.Alert.getOrCreateInstance(alert).close();
                }
            }, 6500);
        });

        // Prevent double submits on normal forms.
        document.querySelectorAll("form").forEach(function (form) {
            form.addEventListener("submit", function (event) {
                if (form.dataset.preventDoubleSubmit === "false" || form.dataset.allowDoubleSubmit === "true") return;
                if (typeof form.checkValidity === "function" && !form.checkValidity()) return;

                const submitButtons = form.querySelectorAll('button[type="submit"], button:not([type])');
                submitButtons.forEach(function (button) {
                    if (button.dataset.noDisable === "true") return;
                    window.setTimeout(function () {
                        // Other listeners (including jQuery validation) can cancel
                        // submission after this listener has run.
                        if (event.defaultPrevented || (window.jQuery && window.jQuery(form).data('validator') && !window.jQuery(form).valid())) return;
                        button.disabled = true;
                        button.dataset.originalText = button.innerHTML;
                        const icon = button.querySelector("i");
                        button.innerHTML = (icon ? icon.outerHTML + " " : "") + "Saving...";
                    }, 0);
                });
            });
        });
        window.addEventListener('pageshow', function () {
            document.querySelectorAll('button[data-original-text]').forEach(function (button) {
                button.innerHTML = button.dataset.originalText;
                button.disabled = false;
                delete button.dataset.originalText;
            });
        });


        // Professional form polish: humanize technical property names and provide
        // useful placeholders where a page did not explicitly define one.
        const humanize = function (value) {
            if (!value) return "";
            return value
                .replace(/[_-]+/g, " ")
                .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
                .replace(/\bId\b/g, "ID")
                .replace(/\bCnic\b/gi, "CNIC")
                .replace(/\bPrc\b/gi, "PRC")
                .replace(/\bCgpa\b/gi, "CGPA")
                .replace(/\s+/g, " ")
                .trim();
        };

        document.querySelectorAll("label.form-label, label.form-check-label").forEach(function (label) {
            if (label.dataset.keepLabel === "true") return;
            const raw = (label.textContent || "").trim();
            if (/^[A-Za-z0-9_]+$/.test(raw) && /[a-z][A-Z]|Id$/.test(raw)) {
                label.textContent = humanize(raw);
            }
        });

        document.querySelectorAll("input.form-control, textarea.form-control").forEach(function (field) {
            if (field.hasAttribute("placeholder") || field.readOnly || field.disabled) return;
            const type = (field.getAttribute("type") || "text").toLowerCase();
            if (["date", "time", "datetime-local", "file", "checkbox", "radio", "hidden"].includes(type)) return;
            const id = field.id;
            let label = id ? document.querySelector('label[for="' + CSS.escape(id) + '"]') : null;
            const name = field.getAttribute("name")?.split(".").pop() || "";
            const friendly = (label?.textContent || humanize(name) || "value").trim();
            if (type === "email") field.placeholder = "e.g. name@example.com";
            else if (type === "tel") field.placeholder = "Enter contact number";
            else if (type === "password") field.placeholder = friendly.toLowerCase().includes("confirm") ? "Re-enter your password" : "Enter your password";
            else if (type === "number") field.placeholder = "Enter " + friendly.toLowerCase();
            else field.placeholder = "Enter " + friendly.toLowerCase();
        });

        document.querySelectorAll("select.form-select").forEach(function (field) {
            const first = field.options && field.options.length ? field.options[0] : null;
            if (!first || String(first.value || "").trim() !== "" || first.dataset.keepText === "true") return;
            const id = field.id;
            const label = id ? document.querySelector('label[for="' + CSS.escape(id) + '"]') : null;
            const name = field.getAttribute("name")?.split(".").pop() || "option";
            const friendly = (label?.textContent || humanize(name) || "option").trim();
            if (["Select", "Optional", "Choose"].includes((first.textContent || "").trim()) || !(first.textContent || "").trim())
                first.textContent = "Select " + friendly.toLowerCase();
        });

                // Confirmation for destructive actions.
        document.querySelectorAll("[data-confirm]").forEach(function (element) {
            element.addEventListener("click", function (event) {
                const message = element.getAttribute("data-confirm") || "Are you sure?";
                if (!window.confirm(message)) event.preventDefault();
            });
        });

        // Keep Bootstrap offcanvas navigation usable on mobile.
        document.querySelectorAll("#adminSidebar .sidebar-link").forEach(function (link) {
            link.addEventListener("click", function () {
                const canvas = document.getElementById("adminSidebar");
                if (canvas && window.bootstrap) {
                    const instance = bootstrap.Offcanvas.getInstance(canvas);
                    if (instance) instance.hide();
                }
            });
        });
    });
})();
