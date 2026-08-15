(function () {
    const slipForm = document.getElementById('payrollSlipForm');
    if (!slipForm) return;

    const generateBtn = document.getElementById('generateBtn');
    const successOverlay = document.getElementById('successOverlay');
    const successMsg = document.getElementById('successMsg');
    const successOkBtn = document.getElementById('successOkBtn');
    const configEl = document.getElementById('slip-config');
    const config = configEl ? JSON.parse(configEl.textContent || '{}') : {};
    const dailyRate = Number(config.dailyRate || 0);
    const hourlyRate = Number(config.hourlyRate || 0);
    const schedules = Array.isArray(config.schedules) ? config.schedules : [];
    const boundMin = config.boundMin || '';
    const boundMax = config.boundMax || '';
    let redirectProjectId = null;

    function formatIsoDate(iso) {
        if (!iso) return '';
        const parts = String(iso).split('-');
        if (parts.length !== 3) return iso;
        return parts[1] + '/' + parts[2] + '/' + parts[0];
    }

    function laterDate(a, b) {
        if (!a) return b || '';
        if (!b) return a;
        return a.localeCompare(b) >= 0 ? a : b;
    }

    function earlierDate(a, b) {
        if (!a) return b || '';
        if (!b) return a;
        return a.localeCompare(b) <= 0 ? a : b;
    }

    function coveringSchedule(startIso) {
        if (!startIso) return null;
        return schedules.find(function (s) {
            return s.start.localeCompare(startIso) <= 0 && startIso.localeCompare(s.end) <= 0;
        }) || null;
    }

    function syncPeriodBounds() {
        const startEl = document.getElementById('payPeriodStart');
        const endEl = document.getElementById('payPeriodEnd');
        if (!startEl || !endEl) return;

        const start = startEl.value;
        const cover = coveringSchedule(start);
        const rangeMax = cover ? cover.end : boundMax;
        const rangeMin = boundMin;

        startEl.min = rangeMin || '';
        startEl.max = earlierDate(rangeMax, endEl.value) || rangeMax || '';
        if (rangeMin) startEl.setAttribute('data-min', rangeMin);
        else startEl.removeAttribute('data-min');
        if (rangeMax) startEl.setAttribute('data-max', rangeMax);
        else startEl.removeAttribute('data-max');

        const endMin = laterDate(rangeMin, start);
        endEl.min = endMin || '';
        endEl.max = rangeMax || '';
        if (endMin) endEl.setAttribute('data-min', endMin);
        else endEl.removeAttribute('data-min');
        if (rangeMax) endEl.setAttribute('data-max', rangeMax);
        else endEl.removeAttribute('data-max');
    }

    function showFieldError(name, message) {
        const field = slipForm.querySelector('[name="' + name + '"]');
        const dest = slipForm.querySelector('[data-error-for="' + name + '"]');
        if (field) field.classList.toggle('is-invalid', !!message);
        if (dest) dest.textContent = message || '';
    }

    function clearServerErrors() {
        ['payPeriodStart', 'payPeriodEnd', 'regularDaysWorked', 'absentDays', 'overtimeHours', 'cashAdvance']
            .forEach(function (name) { showFieldError(name, ''); });
    }

    function inclusiveDays(startIso, endIso) {
        if (!startIso || !endIso) return null;
        const start = new Date(startIso + 'T00:00:00');
        const end = new Date(endIso + 'T00:00:00');
        if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end.getTime() < start.getTime()) return null;
        return Math.round((end.getTime() - start.getTime()) / 86400000) + 1;
    }

    function weekdayCount(startIso, endIso) {
        const total = inclusiveDays(startIso, endIso);
        if (total === null) return 1;
        const start = new Date(startIso + 'T00:00:00');
        let days = 0;
        for (let i = 0; i < total; i++) {
            const day = new Date(start.getTime());
            day.setDate(start.getDate() + i);
            const weekday = day.getDay();
            if (weekday !== 0 && weekday !== 6) days++;
        }
        return days > 0 ? days : 1;
    }

    function fillDaysFromPeriod() {
        const start = document.getElementById('payPeriodStart').value;
        const end = document.getElementById('payPeriodEnd').value;
        const daysEl = document.getElementById('regularDaysWorked');
        if (!daysEl || !start || !end) return;
        daysEl.value = String(weekdayCount(start, end));
        daysEl.classList.remove('is-invalid');
        const dest = slipForm.querySelector('[data-error-for="regularDaysWorked"]');
        if (dest) dest.textContent = '';
    }

    function validateSlipExtras() {
        let ok = true;
        const start = document.getElementById('payPeriodStart').value;
        const end = document.getElementById('payPeriodEnd').value;
        const daysWorked = parseInt(document.getElementById('regularDaysWorked').value, 10) || 0;
        const absent = parseInt(document.getElementById('absentDays').value, 10) || 0;
        const ot = parseFloat(document.getElementById('overtimeHours').value) || 0;
        const cash = parseFloat(document.getElementById('cashAdvance').value) || 0;
        const periodDays = inclusiveDays(start, end);

        if (periodDays !== null && daysWorked + absent > periodDays) {
            showFieldError('regularDaysWorked', 'Days worked plus absences cannot exceed the pay period.');
            ok = false;
        }

        if (daysWorked > 0 && ot > daysWorked * 24) {
            showFieldError('overtimeHours', 'Overtime hours cannot exceed 24 hours per day worked.');
            ok = false;
        }

        const gross = (dailyRate * daysWorked) + (hourlyRate * ot);
        if (cash > gross) {
            showFieldError('cashAdvance', 'Cash advance cannot be greater than gross pay.');
            ok = false;
        }

        if (schedules.length && start && end) {
            const inside = schedules.some(function (s) {
                return s.start.localeCompare(start) <= 0 && end.localeCompare(s.end) <= 0;
            });
            if (!inside) {
                const maxEnd = schedules.reduce(function (m, s) {
                    return !m || s.end.localeCompare(m) > 0 ? s.end : m;
                }, '');
                const minStart = schedules.reduce(function (m, s) {
                    return !m || s.start.localeCompare(m) < 0 ? s.start : m;
                }, '');
                if (end.localeCompare(maxEnd) > 0) {
                    showFieldError('payPeriodEnd', 'Pay period ending date must be on or before ' + formatIsoDate(maxEnd) + '.');
                } else if (start.localeCompare(minStart) < 0) {
                    showFieldError('payPeriodStart', 'Pay period starting date must be on or after ' + formatIsoDate(minStart) + '.');
                } else {
                    showFieldError('payPeriodEnd', 'Pay period must fall within a payroll schedule for this project.');
                }
                ok = false;
            }
        }

        return ok;
    }

    document.getElementById('payPeriodStart').addEventListener('change', function () {
        syncPeriodBounds();
        fillDaysFromPeriod();
    });
    document.getElementById('payPeriodEnd').addEventListener('change', function () {
        syncPeriodBounds();
        fillDaysFromPeriod();
    });
    syncPeriodBounds();
    if (!parseInt(document.getElementById('regularDaysWorked').value, 10)) {
        fillDaysFromPeriod();
    }

    slipForm.addEventListener('submit', async function (e) {
        e.preventDefault();
        clearServerErrors();

        if (window.RsdFormValidation && !window.RsdFormValidation.validate(slipForm)) return;
        if (!validateSlipExtras()) return;

        generateBtn.disabled = true;
        generateBtn.textContent = 'Generating...';

        try {
            const res = await fetch('/PayrollStaff/GeneratePayrollSlip', {
                method: 'POST',
                body: new FormData(slipForm)
            });
            const data = await res.json();

            if (data.success) {
                successMsg.textContent = data.message;
                redirectProjectId = data.projectId;
                successOverlay.classList.add('open');
            } else {
                if (data.errors) {
                    Object.keys(data.errors).forEach(function (name) {
                        showFieldError(name, data.errors[name]);
                    });
                }
                generateBtn.disabled = false;
                generateBtn.textContent = 'Generate Payroll';
            }
        } catch (err) {
            showFieldError('regularDaysWorked', 'Something went wrong. Please try again.');
            generateBtn.disabled = false;
            generateBtn.textContent = 'Generate Payroll';
        }
    });

    successOkBtn.addEventListener('click', function () {
        window.location.href = '/PayrollStaff/GeneratePayroll?projectId=' + redirectProjectId;
    });
})();
