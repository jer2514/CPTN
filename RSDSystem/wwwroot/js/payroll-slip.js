(function () {
    const slipForm = document.getElementById('payrollSlipForm');
    if (!slipForm) return;

    const generateBtn = document.getElementById('generateBtn');
    const successOverlay = document.getElementById('successOverlay');
    const successMsg = document.getElementById('successMsg');
    const successOkBtn = document.getElementById('successOkBtn');
    const configEl = document.getElementById('slip-config');
    const config = configEl ? JSON.parse(configEl.textContent || '{}') : {};
    const employeeId = Number(config.employeeId || 0);
    const projectId = Number(config.projectId || 0);
    const dailyRate = Number(config.dailyRate || 0);
    const hourlyRate = Number(config.hourlyRate || 0);
    const schedules = Array.isArray(config.schedules) ? config.schedules : [];
    const boundMin = config.boundMin || '';
    const boundMax = config.boundMax || '';
    const isEdit = !!config.isEdit;
    const existingPayrollId = Number(config.payrollId || 0);
    const returnUrl = config.returnUrl || '';
    const submitLabel = isEdit ? 'Save Changes' : 'Generate Payroll';
    const busyLabel = isEdit ? 'Saving...' : 'Generating...';
    let redirectProjectId = null;
    let savedPayrollId = existingPayrollId;

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

    function clampBounds(min, max) {
        const calendarMin = '2000-01-01';
        const calendarMax = '2099-12-31';
        let nextMin = laterDate(min || calendarMin, calendarMin);
        let nextMax = earlierDate(max || calendarMax, calendarMax);
        if (nextMin && nextMax && nextMin.localeCompare(nextMax) > 0) {
            nextMax = nextMin;
        }
        return { min: nextMin, max: nextMax };
    }

    function syncPeriodBounds() {
        const startEl = document.getElementById('payPeriodStart');
        const endEl = document.getElementById('payPeriodEnd');
        if (!startEl || !endEl) return;

        if (window.RsdFormValidation) {
            window.RsdFormValidation.sanitizeDate(startEl);
            window.RsdFormValidation.sanitizeDate(endEl);
        }

        const start = startEl.value;
        const cover = coveringSchedule(start);
        const rawMax = earlierDate(cover ? cover.end : boundMax, boundMax);
        const rawMin = laterDate(boundMin, cover ? cover.start : boundMin) || boundMin;
        const range = clampBounds(rawMin, rawMax);

        startEl.min = range.min;
        let startMax = earlierDate(range.max, endEl.value) || range.max;
        if (startMax && startMax.localeCompare(range.min) < 0) startMax = range.max;
        startEl.max = startMax;
        if (boundMin) startEl.setAttribute('data-min', range.min);
        else startEl.removeAttribute('data-min');
        if (boundMax) startEl.setAttribute('data-max', range.max);
        else startEl.removeAttribute('data-max');

        const endMin = laterDate(range.min, start) || range.min;
        let endMax = range.max;
        if (endMin && endMax && endMin.localeCompare(endMax) > 0) endMax = endMin;
        endEl.min = endMin;
        endEl.max = endMax;
        if (endMin) endEl.setAttribute('data-min', endMin);
        else endEl.removeAttribute('data-min');
        if (range.max) endEl.setAttribute('data-max', range.max);
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
        if (total === null) return 0;
        const start = new Date(startIso + 'T00:00:00');
        let days = 0;
        for (let i = 0; i < total; i++) {
            const day = new Date(start.getTime());
            day.setDate(start.getDate() + i);
            const weekday = day.getDay();
            if (weekday !== 0 && weekday !== 6) days++;
        }
        return days;
    }

    function setNumberValue(id, value) {
        const el = document.getElementById(id);
        if (!el) return;
        el.value = value;
        el.classList.remove('is-invalid');
        const dest = slipForm.querySelector('[data-error-for="' + id + '"]');
        if (dest) dest.textContent = '';
    }

    async function fillAttendanceFromPeriod() {
        const start = document.getElementById('payPeriodStart').value;
        const end = document.getElementById('payPeriodEnd').value;
        if (!employeeId || !projectId || !start || !end) return;

        try {
            const url = '/PayrollStaff/GetAttendanceTotals?employeeId=' + encodeURIComponent(employeeId)
                + '&projectId=' + encodeURIComponent(projectId)
                + '&periodStart=' + encodeURIComponent(start)
                + '&periodEnd=' + encodeURIComponent(end);
            const res = await fetch(url);
            const data = await res.json();
            if (!data.success) return;

            setNumberValue('regularDaysWorked', String(data.daysWorked ?? 0));
            setNumberValue('absentDays', String(data.daysAbsent ?? 0));
            const ot = Number(data.overtimeHours || 0);
            setNumberValue('overtimeHours', ot.toFixed(ot % 1 === 0 ? 0 : 2));
        } catch (err) {
            setNumberValue('regularDaysWorked', String(weekdayCount(start, end)));
            setNumberValue('absentDays', '0');
            setNumberValue('overtimeHours', '0');
        }
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
        } else if (daysWorked === 0 && ot > 24) {
            showFieldError('overtimeHours', 'Overtime hours cannot exceed 24 hours per day worked.');
            ok = false;
        }

        const gross = (dailyRate * daysWorked) + (hourlyRate * ot);
        if (cash > gross) {
            showFieldError('cashAdvance', 'Cash advance cannot be greater than gross pay.');
            ok = false;
        }

        if (boundMin && start && start.localeCompare(boundMin) < 0) {
            showFieldError('payPeriodStart', 'Pay period starting date must be on or after ' + formatIsoDate(boundMin) + '.');
            ok = false;
        }
        if (boundMax && end && end.localeCompare(boundMax) > 0) {
            showFieldError('payPeriodEnd', 'Pay period ending date must be on or before ' + formatIsoDate(boundMax) + '.');
            ok = false;
        }

        return ok;
    }

    document.getElementById('payPeriodStart').addEventListener('change', function () {
        syncPeriodBounds();
        fillAttendanceFromPeriod();
    });
    document.getElementById('payPeriodEnd').addEventListener('change', function () {
        syncPeriodBounds();
        fillAttendanceFromPeriod();
    });
    syncPeriodBounds();

    slipForm.addEventListener('submit', async function (e) {
        e.preventDefault();
        clearServerErrors();

        if (window.RsdFormValidation && !window.RsdFormValidation.validate(slipForm)) return;
        if (!validateSlipExtras()) return;

        generateBtn.disabled = true;
        generateBtn.textContent = busyLabel;

        try {
            const res = await fetch('/PayrollStaff/GeneratePayrollSlip', {
                method: 'POST',
                body: new FormData(slipForm)
            });
            const data = await res.json();

            if (data.success) {
                const nextUrl = data.returnUrl || returnUrl;
                if (isEdit && nextUrl) {
                    window.location.href = nextUrl;
                    return;
                }
                successMsg.textContent = data.message;
                redirectProjectId = data.projectId;
                savedPayrollId = Number(data.payrollId || existingPayrollId || 0);
                successOverlay.classList.add('open');
            } else {
                if (data.errors) {
                    Object.keys(data.errors).forEach(function (name) {
                        showFieldError(name, data.errors[name]);
                    });
                }
                generateBtn.disabled = false;
                generateBtn.textContent = submitLabel;
            }
        } catch (err) {
            showFieldError('regularDaysWorked', 'Something went wrong. Please try again.');
            generateBtn.disabled = false;
            generateBtn.textContent = submitLabel;
        }
    });

    successOkBtn.addEventListener('click', function () {
        if (returnUrl) {
            window.location.href = returnUrl;
            return;
        }
        if (isEdit && savedPayrollId) {
            window.location.href = '/PayrollStaff/ViewPayroll/' + savedPayrollId;
            return;
        }
        window.location.href = '/PayrollStaff/GeneratePayroll?projectId=' + redirectProjectId;
    });
})();
