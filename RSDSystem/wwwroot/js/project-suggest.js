// Typeahead for project name fields (Generate Payroll, Attendance Import, etc.).
(function () {
    var dataEl = document.getElementById('project-suggest-data');
    var input = document.getElementById('projectInput');
    var idInput = document.getElementById('projectIdInput');
    var list = document.getElementById('projectSuggestions');
    if (!dataEl || !input || !idInput || !list) return;

    var projects = [];
    try {
        projects = JSON.parse(dataEl.textContent || '[]');
    } catch (err) {
        projects = [];
    }

    // Escapes project names so suggestion HTML cannot inject tags.
    function escapeHtml(value) {
        return String(value || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // Bolds the typed characters inside a matching project name in the dropdown.
    function highlight(name, query) {
        var text = String(name || '');
        if (!query) return escapeHtml(text);
        var index = text.toLowerCase().indexOf(query.toLowerCase());
        if (index < 0) return escapeHtml(text);
        return escapeHtml(text.slice(0, index))
            + '<strong class="suggest-match">'
            + escapeHtml(text.slice(index, index + query.length))
            + '</strong>'
            + escapeHtml(text.slice(index + query.length));
    }

    // Filters the JSON project list by name or location (max 12 rows).
    function matchesFor(query) {
        if (!query) return projects.slice(0, 12);
        return projects.filter(function (project) {
            var name = (project.name || '').toLowerCase();
            var location = (project.location || '').toLowerCase();
            return name.indexOf(query) !== -1 || location.indexOf(query) !== -1;
        }).slice(0, 12);
    }

    // Draws the dropdown under #projectInput (or "No matching project").
    function render(query) {
        var matches = matchesFor(query);
        if (matches.length === 0) {
            list.innerHTML = '<li class="genpay-suggest-empty">No matching project</li>';
            list.style.display = 'block';
            return;
        }

        list.innerHTML = matches.map(function (project) {
            var location = project.location
                ? '<span class="suggest-meta">' + escapeHtml(project.location) + '</span>'
                : '';
            return '<li data-id="' + project.id + '" data-name="' + escapeHtml(project.name) + '">'
                + '<span class="suggest-name">' + highlight(project.name, query) + '</span>'
                + location
                + '</li>';
        }).join('');
        list.style.display = 'block';
    }

    input.addEventListener('input', function () {
        idInput.value = '';
        var query = input.value.trim().toLowerCase();
        if (!query) {
            list.style.display = 'none';
            return;
        }
        render(query);
    });

    input.addEventListener('focus', function () {
        render(input.value.trim().toLowerCase());
    });

    list.addEventListener('click', function (event) {
        var item = event.target.closest('li[data-id]');
        if (!item) return;
        input.value = item.getAttribute('data-name') || '';
        idInput.value = item.getAttribute('data-id') || '';
        list.style.display = 'none';
        var form = input.closest('form');
        if (form) form.submit();
    });

    document.addEventListener('click', function (event) {
        if (!event.target.closest('.genpay-autocomplete')) {
            list.style.display = 'none';
        }
    });
})();
