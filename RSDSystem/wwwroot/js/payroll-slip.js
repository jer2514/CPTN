// Payroll slip page: live pay math (days × rate, OT, cash advance → net) then POST generate.
(function () {
    const slipForm = document.getElementById('payrollSlipForm');
    if (!slipForm) return;

    const generateBtn = document.getElementById('generateBtn');
    const successOverlay = document.getElementById('successOverlay');
    const successMsg = document.getElementById('successMsg');
    const successOkBtn = document.getElementById('successOkBtn');
    const configEl = document.getElementById('slip-config');
    const config = configEl ? JSON.parse(configEl.textContent || '{}') : {};
    const hourlyRate = Number(config.hourlyRate || 0);
    const hasSchedule = !!config.hasSchedule;
    const isEdit = !!config.isEdit;
    const existingPayrollId = Number(config.payrollId || 0);
    const returnUrl = config.returnUrl || '';
    const regularHours = Number(config.regularHours || 0);
    const overtimeHours = Number(config.overtimeHours || 0);
    const submitLabel = isEdit ? 'Save Changes' : 'Generate Payroll';
    const busyLabel = isEdit ? 'Saving...' : 'Generating...';
    let redirectProjectId = null;
    let savedPayrollId = existingPayrollId;

    function showFieldError(name, message) {
        const field = slipForm.querySelector('[name="' + name + '"]');
        const dest = slipForm.querySelector('[data-error-for="' + name + '"]');
        if (field) field.classList.toggle('is-invalid', !!message);
        if (dest) dest.textContent = message || '';
    }

    // Clears previous server/client errors before a new Generate click.
    function clearServerErrors() {
        showFieldError('cashAdvance', '');
    }

    // Extra checks the HTML5 form cannot do: days vs period, OT cap, cash advance vs gross, schedule window.
    function validateSlipExtras() {
        const cash = parseFloat(document.getElementById('cashAdvance').value) || 0;
        const gross = (hourlyRate * regularHours) + (hourlyRate * overtimeHours);

        if (!hasSchedule) {
            showFieldError('cashAdvance', 'A payroll schedule must be added by the admin before generating payroll.');
            return false;
        }

        if (cash > gross) {
            showFieldError('cashAdvance', 'Cash advance cannot be greater than gross pay.');
            return false;
        }

        return true;
    }

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
                if (data.message && (!data.errors || !data.errors.cashAdvance)) {
                    showFieldError('cashAdvance', data.message);
                }
                generateBtn.disabled = false;
                generateBtn.textContent = submitLabel;
            }
        } catch (err) {
            showFieldError('cashAdvance', 'Something went wrong. Please try again.');
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
