// Bootstrap toast helper for TempData success/error messages.
(function () {
    // Escapes toast text so TempData messages cannot inject HTML.
    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // Finds or creates the top-right .toast-container where popups stack.
    function container() {
        var el = document.querySelector('.toast-container');
        if (el) return el;
        el = document.createElement('div');
        el.className = 'toast-container position-fixed top-0 end-0 p-3';
        el.style.zIndex = '1080';
        document.body.appendChild(el);
        return el;
    }

    window.showToast = function (message, type, options) {
        if (!message || !window.bootstrap) return;
        options = options || {};
        var kind = type === 'success' ? 'success' : 'error';
        var delay = typeof options.delay === 'number' ? options.delay : 5000;
        var action = options.action;
        var toast = document.createElement('div');
        toast.className = 'toast align-items-center rsd-toast rsd-toast-' + kind;
        toast.setAttribute('role', 'alert');
        toast.setAttribute('aria-live', 'assertive');
        toast.setAttribute('aria-atomic', 'true');

        var actionHtml = '';
        if (action && action.label && action.href) {
            actionHtml = '<a class="rsd-toast-action" href="' + escapeHtml(action.href) + '">' +
                escapeHtml(action.label) + '</a>';
        }

        toast.innerHTML =
            '<div class="d-flex">' +
                '<div class="toast-body">' +
                    '<i class="bi ' + (kind === 'success' ? 'bi-check-circle-fill' : 'bi-exclamation-triangle-fill') + ' me-2"></i>' +
                    '<span class="rsd-toast-copy">' + escapeHtml(message) + '</span>' +
                    actionHtml +
                '</div>' +
                '<button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>' +
            '</div>';
        container().appendChild(toast);

        if (action && typeof action.onClick === 'function') {
            var link = toast.querySelector('.rsd-toast-action');
            if (link) {
                link.addEventListener('click', function (e) {
                    action.onClick(e, action.href);
                });
            }
        }

        var instance = bootstrap.Toast.getOrCreateInstance(toast, { delay: delay });
        toast.addEventListener('hidden.bs.toast', function () { toast.remove(); });
        instance.show();
    };

    window.rsdToast = window.showToast;
})();
