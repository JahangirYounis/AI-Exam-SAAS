(() => {
    const input = document.getElementById('faqSearch');
    if (!input) return;
    const entries = [...document.querySelectorAll('.pub-faq')];
    input.addEventListener('input', () => {
        const query = input.value.trim().toLocaleLowerCase();
        let count = 0;
        entries.forEach(entry => {
            const match = entry.textContent.toLocaleLowerCase().includes(query);
            entry.hidden = !match;
            if (match) count++;
        });
        document.querySelectorAll('.pub-faq-group').forEach(group => {
            group.hidden = ![...group.querySelectorAll('.pub-faq')].some(entry => !entry.hidden);
        });
        document.getElementById('faqCount').textContent = `${count} ${count === 1 ? 'question' : 'questions'}${query ? ' found' : ''}`;
        document.getElementById('faqEmpty').hidden = count !== 0;
    });
})();
