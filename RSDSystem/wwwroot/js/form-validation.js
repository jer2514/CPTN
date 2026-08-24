// Same rules as Validation/InputRules.cs, run in the browser before the form posts.
(function () {
    const NAME_RE = /^[A-Za-zÑñ][A-Za-zÑñ\s.'\-]{0,79}$/;
    const MI_RE = /^[A-Za-zÑñ]{1,2}$/;
    const PHONE_RE = /^09\d{9}$/;
    const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    const ADDRESS_RE = new RegExp("^[A-Za-z0-9Ññ\\s,.\\-/#')(&]{5,250}$");
    const PROJECT_NAME_RE = new RegExp("^[A-Za-z0-9Ññ][A-Za-z0-9Ññ\\s.'\\-/&#()]{1,149}$");
    const ALLOWED_PHOTO = ['image/jpeg', 'image/png', 'image/webp'];
    const MAX_PHOTO_BYTES = 2 * 1024 * 1024;
    const MIN_AGE = 18;
    const MAX_AGE = 80;
    const MIN_YEAR = 2000;
    const MAX_YEAR = 2099;
    const DATE_MIN = '2000-01-01';
    const DATE_MAX = '2099-12-31';

    // Turns an <input type="date"> value into whole years of age (or null if blank/invalid).
    function ageFrom(isoDate) {
        if (!isoDate) return null;
        const birth = new Date(isoDate + 'T00:00:00');
        if (Number.isNaN(birth.getTime())) return null;
        const today = new Date();
        let age = today.getFullYear() - birth.getFullYear();
        const m = today.getMonth() - birth.getMonth();
        if (m < 0 || (m === 0 && today.getDate() < birth.getDate())) age--;
        return age;
    }

    // Shows ISO yyyy-mm-dd as mm/dd/yyyy in "must be on or after …" error text.
    function formatIsoDate(iso) {
        if (!iso) return '';
        const parts = iso.split('-');
        if (parts.length !== 3) return iso;
        return parts[1] + '/' + parts[2] + '/' + parts[0];
    }

    // Reads the trimmed value, or the chosen file name for photo uploads.
    function valueOf(el) {
        if (!el) return '';
        if (el.type === 'file') return el.files && el.files.length ? el.files[0].name : '';
        return (el.value || '').trim();
    }

    // Walks data-validate="required|name|…" and returns the first error string for that field.
    function messageFor(el) {
        const rules = (el.getAttribute('data-validate') || '').split('|').filter(Boolean);
        const value = valueOf(el);
        const label = el.getAttribute('data-label') || 'This field';

        for (const rule of rules) {
            if (rule === 'required' && !value) {
                return el.type === 'file' ? 'Please choose a file.' : label + ' is required.';
            }
            if (rule === 'money') {
                const n = parseFloat(el.value);
                if (el.value === '' || Number.isNaN(n)) return label + ' is required.';
                if (n <= 0) return label + ' must be greater than 0.';
                continue;
            }
            if (rule === 'nonNeg') {
                const n = parseFloat(el.value);
                if (el.value === '' || Number.isNaN(n)) return label + ' is required.';
                if (n < 0) return label + ' cannot be negative.';
                continue;
            }
            if (rule === 'nonNegInt') {
                if (el.value === '' || !/^\d+$/.test(el.value.trim())) return label + ' must be a whole number.';
                if (parseInt(el.value, 10) < 0) return label + ' cannot be negative.';
                continue;
            }
            if (rule === 'positiveInt') {
                if (el.value === '' || !/^\d+$/.test(el.value.trim())) return label + ' must be a whole number.';
                if (parseInt(el.value, 10) < 1) return label + ' must be at least 1.';
                continue;
            }
            if (rule === 'match') {
                const otherId = el.getAttribute('data-match');
                const other = otherId ? document.getElementById(otherId) : null;
                if (other && other.value !== el.value) return 'Passwords do not match.';
                continue;
            }
            if (rule === 'fileImage') {
                if (el.files && el.files[0]) {
                    const file = el.files[0];
                    if (file.size > MAX_PHOTO_BYTES) return 'Photo must be 2 MB or smaller.';
                    if (file.type && ALLOWED_PHOTO.indexOf(file.type) === -1) {
                        return 'Photo must be a JPG, PNG, or WEBP image.';
                    }
                }
                continue;
            }
            if (!value) continue;

            if (rule === 'name' && !NAME_RE.test(value)) {
                return 'Use letters only. Spaces, hyphens, and apostrophes are allowed.';
            }
            if (rule === 'mi' && !MI_RE.test(value)) {
                return 'Middle initial must be 1–2 letters.';
            }
            if (rule === 'email' && !EMAIL_RE.test(value)) {
                return 'Enter a valid email address.';
            }
            if (rule === 'phone' && !PHONE_RE.test(value)) {
                return 'Contact number must be 11 digits starting with 09.';
            }
            if (rule === 'address' && !ADDRESS_RE.test(value)) {
                return 'Enter a complete address (Barangay, Municipality/City, Province).';
            }
            if (rule === 'dob') {
                const age = ageFrom(el.value);
                if (age === null) return 'Enter a valid date of birth.';
                if (age < 0) return 'Date of birth cannot be in the future.';
                if (age < MIN_AGE) return 'Must be at least ' + MIN_AGE + ' years old.';
                if (age > MAX_AGE) return 'Please enter a valid date of birth.';
            }
            if (rule === 'minlen8' && value.length < 8) {
                return 'Password must be at least 8 characters.';
            }
            if (rule === 'staffPassword') {
                if (value.length < 8 || !/[A-Z]/.test(value) || !/[0-9]/.test(value) || !/[^A-Za-z0-9]/.test(value)) {
                    return 'Password must be at least 8 characters and include 1 capital letter, 1 number, and 1 special character.';
                }
            }
            if (rule === 'projectName' && !PROJECT_NAME_RE.test(value)) {
                return 'Enter a valid project name.';
            }
            if (rule === 'dateYear') {
                const yearPart = (el.value || '').split('-')[0] || '';
                const year = parseInt(yearPart, 10);
                if (yearPart.length !== 4 || Number.isNaN(year) || year < MIN_YEAR || year > MAX_YEAR) {
                    return 'Enter a valid date with a 4-digit year (2000–2099).';
                }
            }
            if (rule === 'dateAfter') {
                const otherId = el.getAttribute('data-after');
                const other = otherId ? document.getElementById(otherId) : null;
                if (other && other.value && el.value && el.value < other.value) {
                    return label + ' must be on or after the starting date.';
                }
            }
            if (rule === 'dateWithin') {
                const min = el.getAttribute('data-min');
                const max = el.getAttribute('data-max');
                if (min && el.value && el.value < min) {
                    return label + ' must be on or after ' + formatIsoDate(min) + '.';
                }
                if (max && el.value && el.value > max) {
                    return label + ' must be on or before ' + formatIsoDate(max) + '.';
                }
            }
        }
        return '';
    }

    // Finds the <span data-error-for="FieldName"> next to this input.
    function errorEl(field) {
        const name = field.getAttribute('name') || field.id;
        if (!name) return null;
        const form = field.closest('form');
        if (!form) return null;
        return form.querySelector('[data-error-for="' + name + '"]');
    }

    // Toggles the red is-invalid class and writes the message under the field.
    function show(field, message) {
        field.classList.toggle('is-invalid', !!message);
        const dest = errorEl(field);
        if (dest) dest.textContent = message || '';
        return !message;
    }

    // Validates one data-validate field on blur/input; returns true if it is OK.
    function validateField(field) {
        if (!field.hasAttribute('data-validate')) return true;
        return show(field, messageFor(field));
    }

    // Validates every field on submit, focuses the first error, and blocks the POST if any fail.
    function validateForm(form) {
        let ok = true;
        let firstInvalid = null;
        form.querySelectorAll('[data-validate]').forEach(function (field) {
            if (!validateField(field) && !firstInvalid) firstInvalid = field;
            if (field.classList.contains('is-invalid')) ok = false;
        });
        if (firstInvalid) {
            firstInvalid.focus();
            firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
        return ok;
    }

    // Keeps the read-only Age box in sync when Date of Birth changes on employee/user forms.
    function bindAge(form) {
        const dob = form.querySelector('#dobInput');
        const age = form.querySelector('#ageInput');
        if (!dob || !age) return;

        // Recalculates Age from #dobInput so the user sees years as they pick a date.
        function sync() {
            const years = ageFrom(dob.value);
            age.value = years !== null && years >= 0 ? years : '';
        }

        dob.addEventListener('change', sync);
        dob.addEventListener('input', sync);
        sync();
    }

    // True if data-validate includes this rule name (phone, mi, dob, …).
    function hasRule(el, rule) {
        return (el.getAttribute('data-validate') || '').split('|').indexOf(rule) !== -1;
    }

    // Strips non-digits and caps contact numbers at 11 characters as the user types.
    function bindPhone(form) {
        form.querySelectorAll('[data-validate]').forEach(function (el) {
            if (!hasRule(el, 'phone')) return;
            el.addEventListener('input', function () {
                el.value = el.value.replace(/\D/g, '').slice(0, 11);
            });
        });
    }

    // Sets min/max 2000–2099 on date inputs that are not date-of-birth.
    function applyDefaultDateBounds(el) {
        if (!el || el.type !== 'date' || hasRule(el, 'dob')) return;
        if (!el.min) el.min = DATE_MIN;
        if (!el.max) el.max = DATE_MAX;
        if (el.min && el.max && el.min > el.max) {
            el.max = el.min;
        }
    }

    // Truncates years longer than 4 digits and clears dates after 2099 in the date picker.
    function sanitizeDateValue(el) {
        if (!el || el.type !== 'date') return;
        applyDefaultDateBounds(el);

        const raw = el.value || '';
        if (!raw) return;

        const parts = raw.split('-');
        if (!parts[0]) return;

        if (parts[0].length > 4) {
            parts[0] = parts[0].slice(0, 4);
            el.value = parts.length >= 3 ? parts[0] + '-' + parts[1] + '-' + parts[2] : parts[0];
        }

        const year = parseInt(parts[0], 10);
        if (!Number.isNaN(year) && parts[0].length === 4 && year > MAX_YEAR) {
            el.value = '';
        }
    }

    // Wires every date input so typing a year cannot go past 2099.
    function bindDateInputs(root) {
        (root || document).querySelectorAll('input[type="date"]').forEach(function (el) {
            if (el.getAttribute('data-date-bound') === '1') return;
            el.setAttribute('data-date-bound', '1');
            applyDefaultDateBounds(el);
            el.addEventListener('input', function () { sanitizeDateValue(el); });
            el.addEventListener('change', function () { sanitizeDateValue(el); });
            sanitizeDateValue(el);
        });
    }

    // Forces middle-initial inputs to 1–2 letters, uppercase, as the user types.
    function bindMi(form) {
        form.querySelectorAll('[data-validate]').forEach(function (el) {
            if (!hasRule(el, 'mi')) return;
            el.addEventListener('input', function () {
                el.value = el.value.replace(/[^A-Za-zÑñ]/g, '').slice(0, 2).toUpperCase();
            });
        });
    }

    // Attaches blur/submit listeners to one form.js-validate (Create/Edit Employee, User, Project).
    function initForm(form) {
        if (form.getAttribute('data-validation-bound') === '1') return;
        form.setAttribute('data-validation-bound', '1');
        form.setAttribute('novalidate', 'novalidate');

        bindAge(form);
        bindPhone(form);
        bindMi(form);
        bindDateInputs(form);

        form.addEventListener('submit', function (e) {
            if (!validateForm(form)) e.preventDefault();
        });

        form.querySelectorAll('[data-validate]').forEach(function (field) {
            const evt = field.type === 'file' || field.tagName === 'SELECT' || field.type === 'date'
                ? 'change'
                : 'blur';
            field.addEventListener(evt, function () { validateField(field); });
            field.addEventListener('input', function () {
                if (field.classList.contains('is-invalid')) validateField(field);
            });
        });

        form.querySelectorAll('[data-after]').forEach(function (field) {
            const other = document.getElementById(field.getAttribute('data-after') || '');
            if (other) other.addEventListener('change', function () { validateField(field); });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('form.js-validate').forEach(initForm);
        bindDateInputs(document);
    });

    window.RsdFormValidation = { init: initForm, validate: validateForm, sanitizeDate: sanitizeDateValue };
})();
