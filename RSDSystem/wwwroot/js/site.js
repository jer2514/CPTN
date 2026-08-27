(function () {
    function itemLabel(item) {
        return (item.getAttribute('data-filter-label') || item.textContent || '')
            .replace(/\s+/g, ' ')
            .trim();
    }

    window.setFilterButtonLabel = function (btnOrItem, label) {
        var btn = btnOrItem && btnOrItem.classList && btnOrItem.classList.contains('filter-label-btn')
            ? btnOrItem
            : (btnOrItem && btnOrItem.closest
                ? (btnOrItem.closest('.dropdown') || document).querySelector('.filter-label-btn')
                : document.querySelector('.filter-label-btn'));
        if (btn)
            btn.textContent = (label || '').replace(/\s+/g, ' ').trim() || 'Filter';
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
            btn.textContent = label;
    });

    document.addEventListener('click', function (e) {
        var wrap = document.querySelector('.profile-wrap');
        if (!wrap)
            return;
        var toggle = document.getElementById('profileToggle');
        var menu = document.getElementById('profileMenu');
        if (!toggle || !menu)
            return;

        if (toggle.contains(e.target)) {
            var open = wrap.classList.toggle('open');
            menu.hidden = !open;
            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
            return;
        }

        if (!wrap.contains(e.target) && wrap.classList.contains('open')) {
            wrap.classList.remove('open');
            menu.hidden = true;
            toggle.setAttribute('aria-expanded', 'false');
        }
    });

    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape')
            return;
        var wrap = document.querySelector('.profile-wrap');
        var toggle = document.getElementById('profileToggle');
        var menu = document.getElementById('profileMenu');
        if (!wrap || !wrap.classList.contains('open'))
            return;
        wrap.classList.remove('open');
        if (menu)
            menu.hidden = true;
        if (toggle)
            toggle.setAttribute('aria-expanded', 'false');
    });
})();
