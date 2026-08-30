(function () {
    const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    const token = tokenInput ? tokenInput.value : '';
    const isAdmin = document.body.dataset.role === 'Admin';

    function hideReturnReason() {
        const wrap = document.getElementById('corrReturnWrap');
        const reason = document.getElementById('corrReturnReason');
        if (wrap) wrap.hidden = true;
        if (reason) reason.value = '';
    }

    function hideTaskReturnReason() {
        const wrap = document.getElementById('taskReturnWrap');
        const reason = document.getElementById('taskReturnReason');
        if (wrap) wrap.hidden = true;
        if (reason) reason.value = '';
    }

    function wrapBells() {
        document.querySelectorAll('.bell-btn').forEach(function (btn) {
            if (btn.closest('.notif-bell-wrap')) return;
            const wrap = document.createElement('div');
            wrap.className = 'notif-bell-wrap';
            btn.parentNode.insertBefore(wrap, btn);
            wrap.appendChild(btn);
            btn.setAttribute('type', 'button');
            btn.setAttribute('aria-label', 'Notifications');
            const badge = document.createElement('span');
            badge.className = 'notif-badge';
            badge.hidden = true;
            wrap.appendChild(badge);
            wrap.appendChild(buildPanel());
        });
    }

    function buildPanel() {
        const panel = document.createElement('div');
        panel.className = 'notif-panel';
        panel.hidden = true;
        panel.innerHTML =
            '<div class="notif-panel-head">' +
                '<span>Notifications</span>' +
                '<i class="bi bi-bell-fill"></i>' +
            '</div>' +
            '<div class="notif-panel-list"></div>' +
            '<a class="notif-view-all" href="/Notification">View All Notifications</a>';
        return panel;
    }

    function closeAll() {
        document.querySelectorAll('.notif-panel').forEach(function (panel) {
            panel.hidden = true;
        });
    }

    async function loadPanel(panel) {
        const list = panel.querySelector('.notif-panel-list');
        list.innerHTML = '<div class="notif-empty">Loading...</div>';
        try {
            const res = await fetch('/Notification/Recent', { headers: { 'Accept': 'application/json' } });
            const data = await res.json();
            if (!data.success) {
                list.innerHTML = '<div class="notif-empty">Could not load notifications.</div>';
                return;
            }
            setBadges(data.unread || 0);
            const headSpan = panel.querySelector('.notif-panel-head span');
            if (headSpan) {
                headSpan.textContent = data.unread > 0
                    ? 'Notifications · ' + data.unread + ' unread'
                    : 'Notifications';
            }
            const viewAll = panel.querySelector('.notif-view-all');
            if (viewAll && data.viewAllUrl) viewAll.href = data.viewAllUrl;
            if (!data.items || !data.items.length) {
                list.innerHTML = '<div class="notif-empty">No notifications yet.</div>';
                return;
            }
            list.innerHTML = data.items.map(function (item) {
                return '<button type="button" class="notif-item' + (item.isRead ? ' is-read' : '') + '"' +
                    ' data-id="' + item.id + '"' +
                    ' data-url="' + (item.url || '') + '"' +
                    ' data-kind="' + (item.kind || '') + '"' +
                    ' data-related="' + (item.relatedId || '') + '"' +
                    ' data-project="' + (item.projectId || '') + '">' +
                    '<span class="notif-icon ' + item.iconClass + '"><i class="bi ' + item.icon + '"></i></span>' +
                    '<span class="notif-item-copy">' +
                        '<span class="notif-item-top"><span class="notif-item-title">' + escapeHtml(item.title) + '</span>' +
                        '<span class="notif-item-time">' + escapeHtml(item.timeAgo) + '</span></span>' +
                        '<span class="notif-item-msg">' + escapeHtml(item.message) + '</span>' +
                    '</span>' +
                    (item.isRead ? '' : '<span class="notif-unread-dot"></span>') +
                    '</button>';
            }).join('');
        } catch (err) {
            list.innerHTML = '<div class="notif-empty">Could not load notifications.</div>';
        }
    }

    function setBadges(count) {
        document.querySelectorAll('.notif-badge').forEach(function (badge) {
            if (count > 0) {
                badge.hidden = false;
                badge.textContent = count > 9 ? '9+' : String(count);
            } else {
                badge.hidden = true;
            }
        });
    }

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    async function postForm(url, fields) {
        const body = new FormData();
        Object.keys(fields || {}).forEach(function (key) {
            body.set(key, fields[key]);
        });
        if (token) body.set('__RequestVerificationToken', token);
        const res = await fetch(url, {
            method: 'POST',
            headers: { 'Accept': 'application/json', 'RequestVerificationToken': token },
            body
        });
        return res.json();
    }

    async function openItem(el) {
        const id = el.dataset.id;
        const kind = el.dataset.kind;
        const related = el.dataset.related;
        if (id) {
            try { await postForm('/Notification/MarkRead', { id: id }); } catch (err) { /* ignore */ }
        }
        if (isAdmin && related && (kind === 'AttendanceCorrectionRequest' || kind === 'AttendanceCorrectionResubmitted')) {
            closeAll();
            openCorrectionModal(related);
            return;
        }
        if (isAdmin && kind === 'TaskCompletionRequested' && related) {
            closeAll();
            openTaskModal(related);
            return;
        }
        if ((kind === 'CashAdvanceDeduction' || kind === 'CashAdvanceAdded') && el.dataset.project) {
            closeAll();
            openCashAdvanceSummary(el.dataset.project);
            return;
        }
        if (el.dataset.url) window.location.href = el.dataset.url;
    }

    async function openCorrectionModal(id) {
        const overlay = document.getElementById('attendanceCorrectionOverlay');
        if (!overlay) {
            window.location.href = '/Notification';
            return;
        }
        overlay.classList.add('open');
        overlay.dataset.id = id;
        document.getElementById('corrStaff').textContent = '…';
        document.getElementById('corrProject').textContent = '…';
        document.getElementById('corrReason').textContent = '…';
        try {
            const res = await fetch('/Notification/GetCorrection?id=' + encodeURIComponent(id), {
                headers: { 'Accept': 'application/json' }
            });
            const data = await res.json();
            if (!data.success) {
                showToast(data.message || 'Could not load the correction request.');
                overlay.classList.remove('open');
                return;
            }
            document.getElementById('corrStaff').textContent = data.payrollStaff || '—';
            document.getElementById('corrProject').textContent = data.projectName || '—';
            document.getElementById('corrName').textContent = data.employeeName || '—';
            document.getElementById('corrDate').textContent = data.date || '—';
            document.getElementById('corrIn1').textContent = data.timeIn1 || '—';
            document.getElementById('corrOut1').textContent = data.timeOut1 || '—';
            document.getElementById('corrIn2').textContent = data.timeIn2 || '—';
            document.getElementById('corrOut2').textContent = data.timeOut2 || '—';
            document.getElementById('corrOtIn').textContent = data.overtimeIn || '—';
            document.getElementById('corrOtOut').textContent = data.overtimeOut || '—';
            document.getElementById('corrReason').textContent = data.reason || '—';
            const actions = document.getElementById('corrActions');
            if (actions) actions.style.display = data.pending ? 'flex' : 'none';
            hideReturnReason();
        } catch (err) {
            showToast('Could not load the correction request.');
            overlay.classList.remove('open');
        }
    }

    window.rsdOpenCorrection = openCorrectionModal;

    async function openCashAdvanceSummary(projectId) {
        const overlay = document.getElementById('cashAdvanceNotifOverlay');
        if (!overlay) {
            window.location.href = '/PayrollStaff/GeneratePayroll?projectId=' + encodeURIComponent(projectId);
            return;
        }
        overlay.hidden = false;
        const msg = document.getElementById('caNotifMessage');
        const rows = document.getElementById('caNotifRows');
        const openBtn = document.getElementById('caNotifOpenBtn');
        if (msg) msg.textContent = 'Loading...';
        if (rows) rows.innerHTML = '<tr><td colspan="3" class="text-center text-muted py-3">Loading...</td></tr>';
        try {
            const res = await fetch('/Notification/GetCashAdvanceSummary?projectId=' + encodeURIComponent(projectId), {
                headers: { 'Accept': 'application/json' }
            });
            const data = await res.json();
            if (!data.success) {
                showToast(data.message || 'Could not load cash advances.');
                overlay.hidden = true;
                return;
            }
            if (msg) msg.textContent = data.message || '';
            if (openBtn) {
                openBtn.href = data.generateUrl || ('/PayrollStaff/GeneratePayroll?projectId=' + projectId);
                openBtn.textContent = isAdmin ? 'Open Cash Advance' : 'Open Payroll';
            }
            if (rows) {
                if (!data.employees || !data.employees.length) {
                    rows.innerHTML = '<tr><td colspan="3" class="text-center text-muted py-3">No cash advances are waiting for the next payroll.</td></tr>';
                } else {
                    rows.innerHTML = data.employees.map(function (row) {
                        return '<tr><td>' + escapeHtml(row.name) + '</td><td>' + escapeHtml(row.job) +
                            '</td><td>₱ ' + Number(row.amount).toLocaleString('en-PH', { minimumFractionDigits: 2 }) + '</td></tr>';
                    }).join('');
                }
            }
        } catch (err) {
            showToast('Could not load cash advances.');
            overlay.hidden = true;
        }
    }

    const caNotifOverlay = document.getElementById('cashAdvanceNotifOverlay');
    if (caNotifOverlay) {
        caNotifOverlay.addEventListener('click', function (e) {
            if (e.target === caNotifOverlay) caNotifOverlay.hidden = true;
        });
        const closeBtn = document.getElementById('caNotifCloseBtn');
        if (closeBtn) closeBtn.addEventListener('click', function () { caNotifOverlay.hidden = true; });
    }

    async function openTaskModal(id) {
        const overlay = document.getElementById('taskApprovalOverlay');
        if (!overlay) {
            window.location.href = '/Notification';
            return;
        }
        overlay.classList.add('open');
        overlay.dataset.id = id;
        hideTaskReturnReason();
        document.getElementById('taskStaff').textContent = '…';
        document.getElementById('taskProject').textContent = '…';
        document.getElementById('taskType').textContent = '…';
        document.getElementById('taskPeriod').textContent = '…';
        try {
            const res = await fetch('/Notification/GetTask?id=' + encodeURIComponent(id), {
                headers: { 'Accept': 'application/json' }
            });
            const data = await res.json();
            if (!data.success) {
                showToast(data.message || 'Could not load the task.');
                overlay.classList.remove('open');
                return;
            }
            document.getElementById('taskStaff').textContent = data.payrollStaff || '—';
            document.getElementById('taskProject').textContent = data.projectName || '—';
            document.getElementById('taskType').textContent = data.projectType || '—';
            document.getElementById('taskPeriod').textContent = data.period || '—';
            const actions = document.getElementById('taskActions');
            if (actions) actions.style.display = data.pending ? 'flex' : 'none';
            if (!data.pending) hideTaskReturnReason();
        } catch (err) {
            showToast('Could not load the task.');
            overlay.classList.remove('open');
        }
    }

    document.addEventListener('click', function (e) {
        const bell = e.target.closest('.bell-btn');
        if (bell) {
            e.preventDefault();
            const wrap = bell.closest('.notif-bell-wrap');
            const panel = wrap && wrap.querySelector('.notif-panel');
            if (!panel) return;
            const open = panel.hidden;
            closeAll();
            if (open) {
                panel.hidden = false;
                loadPanel(panel);
            }
            return;
        }

        const item = e.target.closest('.notif-item, .notif-page-item');
        if (item) {
            e.preventDefault();
            openItem(item);
            return;
        }

        if (!e.target.closest('.notif-panel') && !e.target.closest('.notif-bell-wrap'))
            closeAll();
    });

    const pageMarkAll = document.getElementById('notifPageMarkAll');
    if (pageMarkAll) {
        pageMarkAll.addEventListener('click', async function () {
            await postForm('/Notification/MarkAllRead', {});
            window.location.reload();
        });
    }

    const overlay = document.getElementById('attendanceCorrectionOverlay');
    if (overlay) {
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) {
                overlay.classList.remove('open');
                hideReturnReason();
            }
        });
        const approveBtn = document.getElementById('corrApprove');
        const returnBtn = document.getElementById('corrReturn');
        if (approveBtn) {
            approveBtn.addEventListener('click', async function () {
                const id = overlay.dataset.id;
                const data = await postForm('/Notification/ApproveCorrection', { id: id });
                if (!data.success) { showToast(data.message || 'Could not approve.'); return; }
                overlay.classList.remove('open');
                window.location.reload();
            });
        }
        const returnWrap = document.getElementById('corrReturnWrap');
        const returnReason = document.getElementById('corrReturnReason');
        const returnCancel = document.getElementById('corrReturnCancel');
        const returnSend = document.getElementById('corrReturnSend');
        const corrActions = document.getElementById('corrActions');

        function showReturnReason() {
            if (corrActions) corrActions.style.display = 'none';
            if (returnWrap) returnWrap.hidden = false;
            if (returnReason) {
                returnReason.value = '';
                returnReason.focus();
            }
        }

        if (returnBtn) {
            returnBtn.addEventListener('click', function () {
                showReturnReason();
            });
        }
        if (returnCancel) {
            returnCancel.addEventListener('click', function () {
                hideReturnReason();
                if (corrActions) corrActions.style.display = 'flex';
            });
        }
        if (returnSend) {
            returnSend.addEventListener('click', async function () {
                const id = overlay.dataset.id;
                const reason = returnReason ? returnReason.value.trim() : '';
                if (!reason) {
                    showToast('Enter a reason for returning this correction.');
                    return;
                }
                const data = await postForm('/Notification/ReturnCorrection', { id: id, reason: reason });
                if (!data.success) { showToast(data.message || 'Could not return the request.'); return; }
                overlay.classList.remove('open');
                hideReturnReason();
                window.location.reload();
            });
        }
    }

    const taskOverlay = document.getElementById('taskApprovalOverlay');
    if (taskOverlay) {
        taskOverlay.addEventListener('click', function (e) {
            if (e.target === taskOverlay) {
                taskOverlay.classList.remove('open');
                hideTaskReturnReason();
            }
        });
        const taskApproveBtn = document.getElementById('taskApprove');
        const taskReturnBtn = document.getElementById('taskReturn');
        const taskReturnWrap = document.getElementById('taskReturnWrap');
        const taskReturnReason = document.getElementById('taskReturnReason');
        const taskReturnCancel = document.getElementById('taskReturnCancel');
        const taskReturnSend = document.getElementById('taskReturnSend');
        const taskActions = document.getElementById('taskActions');

        function showTaskReturnReason() {
            if (taskActions) taskActions.style.display = 'none';
            if (taskReturnWrap) taskReturnWrap.hidden = false;
            if (taskReturnReason) {
                taskReturnReason.value = '';
                taskReturnReason.focus();
            }
        }

        if (taskApproveBtn) {
            taskApproveBtn.addEventListener('click', async function () {
                const id = taskOverlay.dataset.id;
                const data = await postForm('/Notification/ApproveTask', { id: id });
                if (!data.success) { showToast(data.message || 'Could not approve the task.'); return; }
                taskOverlay.classList.remove('open');
                hideTaskReturnReason();
                window.location.reload();
            });
        }
        if (taskReturnBtn) {
            taskReturnBtn.addEventListener('click', function () {
                showTaskReturnReason();
            });
        }
        if (taskReturnCancel) {
            taskReturnCancel.addEventListener('click', function () {
                hideTaskReturnReason();
                if (taskActions) taskActions.style.display = 'flex';
            });
        }
        if (taskReturnSend) {
            taskReturnSend.addEventListener('click', async function () {
                const id = taskOverlay.dataset.id;
                const reason = taskReturnReason ? taskReturnReason.value.trim() : '';
                if (!reason) {
                    showToast('Enter a reason for returning this task.');
                    return;
                }
                const data = await postForm('/Notification/ReturnTask', { id: id, reason: reason });
                if (!data.success) { showToast(data.message || 'Could not return the task.'); return; }
                taskOverlay.classList.remove('open');
                hideTaskReturnReason();
                window.location.reload();
            });
        }
    }

    wrapBells();
    function refreshUnread() {
        fetch('/Notification/UnreadCount', { headers: { 'Accept': 'application/json' } })
            .then(function (res) { return res.json(); })
            .then(function (data) { if (data.success) setBadges(data.unread || 0); })
            .catch(function () { });
    }
    refreshUnread();
    window.setInterval(refreshUnread, 15000);

    const isStaff = document.body.dataset.role === 'PayrollStaff';

    function shownToastKey(id) {
        return 'rsd-notif-toast-' + id;
    }

    function rememberToast(id) {
        var key = shownToastKey(id);
        try {
            if (sessionStorage.getItem(key)) return false;
            sessionStorage.setItem(key, '1');
            return true;
        } catch (err) {
            return true;
        }
    }

    async function showLiveNotificationToasts() {
        if (typeof window.showToast !== 'function') return;
        if (window.location.pathname.toLowerCase().indexOf('/payrollstaff/downloadpayslips') === 0)
            return;
        try {
            const res = await fetch('/Notification/Recent', { headers: { 'Accept': 'application/json' } });
            const data = await res.json();
            if (!data.success) return;
            if (typeof data.unread === 'number') setBadges(data.unread);
            (data.items || []).forEach(function (item) {
                if (item.isRead) return;
                if (!rememberToast(item.id)) return;

                if (isStaff && item.kind === 'PayslipsSent') {
                    var href = item.url || '/PayrollStaff/PendingPayroll';
                    window.showToast(item.message || 'Payslips are ready to download.', 'success', {
                        delay: 14000,
                        action: {
                            label: 'Download',
                            href: href,
                            onClick: function (e, url) {
                                if (e) e.preventDefault();
                                postForm('/Notification/MarkRead', { id: item.id })
                                    .catch(function () { })
                                    .finally(function () {
                                        window.location.href = url || href;
                                    });
                            }
                        }
                    });
                    return;
                }

                if (isAdmin && (item.kind === 'PayrollResubmitted'
                    || item.kind === 'AttendanceCorrectionResubmitted'
                    || item.kind === 'TaskCompletionRequested')) {
                    window.showToast(item.message || item.title || 'You have a new notification.', 'success', {
                        delay: 12000,
                        action: item.url ? {
                            label: 'Open',
                            href: item.url,
                            onClick: function (e, url) {
                                if (e) e.preventDefault();
                                postForm('/Notification/MarkRead', { id: item.id })
                                    .catch(function () { })
                                    .finally(function () {
                                        window.location.href = url || item.url;
                                    });
                            }
                        } : null
                    });
                }
            });
        } catch (err) { /* ignore */ }
    }

    showLiveNotificationToasts();
    window.setInterval(showLiveNotificationToasts, 15000);
})();
