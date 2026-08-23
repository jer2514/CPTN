(function () {
    function itemLabel(item) {
        return (item.getAttribute('data-filter-label') || item.textContent || '')
            .replace(/\s+/g, ' ')
            .trim();
    }

    function formatFilterButton(label) {
        var value = (label || '').replace(/\s+/g, ' ').trim();
        if (!value)
            return 'Filter';
        if (/^filter\b/i.test(value))
            return value;
        return 'Filter: ' + value;
    }

    window.setFilterButtonLabel = function (btnOrItem, label) {
        var btn = btnOrItem && btnOrItem.classList && btnOrItem.classList.contains('filter-label-btn')
            ? btnOrItem
            : (btnOrItem && btnOrItem.closest
                ? (btnOrItem.closest('.dropdown') || document).querySelector('.filter-label-btn')
                : document.querySelector('.filter-label-btn'));
        if (btn)
            btn.textContent = formatFilterButton(label);
    };

    document.addEventListener('click', function (e) {
        var item = e.target.closest('.filter-menu .dropdown-item');
        if (!item)
            return;
        var label = itemLabel(item);
        if (!label)
            return;
        var dropdown = item.closest('.dropdown');
        var btn = dropdown && dropdown.querySelector('.filter-label-btn');
        if (btn)
            btn.textContent = formatFilterButton(label);
    });
})();
