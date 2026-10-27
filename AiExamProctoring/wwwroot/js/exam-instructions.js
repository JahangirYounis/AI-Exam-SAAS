(() => {
    const buttons = document.querySelectorAll('[data-instruction-language]');
    function select(language) {
        buttons.forEach(button => {
            const active = button.dataset.instructionLanguage === language;
            button.setAttribute('aria-pressed', String(active));
            button.classList.toggle('btn-primary', active);
            button.classList.toggle('btn-outline-primary', !active);
            const panel = document.getElementById(button.getAttribute('aria-controls'));
            if (panel) panel.hidden = !active;
        });
    }
    buttons.forEach(button => button.addEventListener('click', () => select(button.dataset.instructionLanguage)));
    select('english');
})();
