// Bootstrap toast helper for TempData success/error messages.
(function () {
    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function container() {
        var el = document.querySelector('.toast-container');
        if (el) return el;
        el = document.createElement('div');
        el.className = 'toast-container position-fixed top-0 end-0 p-3';
        el.style.zIndex = '1080';
        document.body.appendChild(el);
        return el;
    }

    window.showToast = function (message, type) {
        if (!message || !window.bootstrap) return;
        var kind = type === 'success' ? 'success' : 'error';
        var toast = document.createElement('div');
        toast.className = 'toast align-items-center rsd-toast rsd-toast-' + kind;
        toast.setAttribute('role', 'alert');
        toast.setAttribute('aria-live', 'assertive');
        toast.setAttribute('aria-atomic', 'true');
        toast.innerHTML =
            '<div class="d-flex">' +
                '<div class="toast-body">' +
                    '<i class="bi ' + (kind === 'success' ? 'bi-check-circle-fill' : 'bi-exclamation-triangle-fill') + ' me-2"></i>' +
                    escapeHtml(message) +
                '</div>' +
                '<button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>' +
            '</div>';
        container().appendChild(toast);
        var instance = bootstrap.Toast.getOrCreateInstance(toast, { delay: 5000 });
        toast.addEventListener('hidden.bs.toast', function () { toast.remove(); });
        instance.show();
    };
})();
