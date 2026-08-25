#!/usr/bin/env python3
"""Build biometric-style .xls test workbooks for Import Attendance.

Produces June 1-30 and July 1-31 2026 files with three side-by-side
time cards (User IDs 1, 2, 3). Punch layout matches AttendanceFileParser.
"""
from __future__ import annotations

import calendar
from datetime import date
from pathlib import Path

import xlwt

OUT_DIR = Path(__file__).resolve().parent

EMPLOYEES = [
    {"user_id": 1, "name": "patrick bateman"},
    {"user_id": 2, "name": "travis bickle"},
    {"user_id": 3, "name": "louis bloom"},
]

COMPLETE = ("08:00", "12:00", "13:00", "17:00", None, None)
OT = ("08:00", "12:00", "13:00", "17:00", "17:30", "19:30")
LATE = ("08:40", "12:00", "13:00", "17:00", None, None)
ABSENT = (None, None, None, None, None, None)
HALF = ("08:00", "12:00", None, None, None, None)

# day -> punch tuple per employee (1-based day of month)
JUNE_SPECIALS = {
    1: {3: OT, 5: OT, 17: OT, 24: OT},
    2: {4: LATE, 11: LATE, 18: LATE, 7: ABSENT, 14: ABSENT, 21: ABSENT},
    3: {6: ABSENT, 13: ABSENT, 20: ABSENT, 9: HALF, 16: HALF, 30: HALF},
}
JULY_SPECIALS = {
    1: {3: OT, 8: OT, 17: OT, 24: OT},
    2: {4: LATE, 10: LATE, 17: LATE, 7: ABSENT, 14: ABSENT, 21: ABSENT},
    3: {6: ABSENT, 13: ABSENT, 20: ABSENT, 9: HALF, 16: HALF, 31: HALF},
}

BLOCK_STARTS = (0, 15, 30)
WEEKDAY = ("Mo", "Tu", "We", "Th", "Fr", "Sa", "Su")


def punches_for(month: int, day: int, user_id: int) -> tuple:
    specials = JUNE_SPECIALS if month == 6 else JULY_SPECIALS
    return specials.get(user_id, {}).get(day, COMPLETE)


def hours_from(punches: tuple) -> float:
    if punches == ABSENT:
        return 0.0
    if punches == HALF:
        return 4.0
    if punches == OT:
        return 10.0
    return 8.0


def ot_hours(punches: tuple) -> float:
    return 2.0 if punches == OT else 0.0


def is_late(punches: tuple) -> bool:
    return punches == LATE


def is_absent(punches: tuple) -> bool:
    return punches == ABSENT


def is_half(punches: tuple) -> bool:
    return punches == HALF


def write_cell(sheet, r, c, value, style=None):
    if value is None or value == "":
        return
    if style is None:
        sheet.write(r, c, value)
    else:
        sheet.write(r, c, value, style)


def build_workbook(year: int, month: int, last_day: int) -> xlwt.Workbook:
    start = date(year, month, 1)
    end = date(year, month, last_day)
    period = f"{start.isoformat()}~{end.isoformat()}"
    days = list(range(1, last_day + 1))

    emp_days = {}
    for emp in EMPLOYEES:
        uid = emp["user_id"]
        emp_days[uid] = {d: punches_for(month, d, uid) for d in days}

    wb = xlwt.Workbook()
    write_shift_sheet(wb, period, days, start, emp_days)
    write_stat_sheet(wb, period, emp_days)
    write_timecard_sheet(wb, period, days, start, emp_days)
    return wb


def write_shift_sheet(wb, period, days, start, emp_days):
    sh = wb.add_sheet("Shift Setting Table")
    write_cell(sh, 0, 1, "                     Shift Setting Table (For Verification Only)")
    write_cell(sh, 1, 0, f"Date:{period}")
    write_cell(sh, 1, 17, "Special Shift: 25-Business trip, 26-Leave, Empty-Holiday")
    write_cell(sh, 1, 34, "Special Shift: 2-Business trip, 3-Leave, Empty-Holiday")
    write_cell(sh, 2, 0, "User ID")
    write_cell(sh, 2, 1, "Name")
    write_cell(sh, 2, 2, "Department")
    for i, d in enumerate(days):
        write_cell(sh, 2, 3 + i, d)
        wd = WEEKDAY[calendar.weekday(start.year, start.month, d)]
        write_cell(sh, 3, 3 + i, wd)
    for row_i, emp in enumerate(EMPLOYEES):
        r = 4 + row_i
        write_cell(sh, r, 0, emp["user_id"])
        write_cell(sh, r, 1, emp["name"])
        write_cell(sh, r, 2, "COMPANY")
        for i, d in enumerate(days):
            punches = emp_days[emp["user_id"]][d]
            write_cell(sh, r, 3 + i, 0 if is_absent(punches) else 1)


def write_stat_sheet(wb, period, emp_days):
    sh = wb.add_sheet("Attendance Statistic Table")
    write_cell(sh, 0, 0, "Attendance Statistic Table")
    write_cell(sh, 1, 0, f"Date:{period}")
    headers = [
        (0, "User ID"),
        (1, "Name"),
        (2, "Department"),
        (3, "Worktime(hrs.)"),
        (5, "Late"),
        (7, "Early"),
        (9, "Overtime(hrs.)"),
        (11, "Workday\n(Normal/Actual)"),
        (12, "Trip\n(Day)"),
        (13, "Absence\n(Day)"),
        (14, "Leave\n(Day)"),
        (15, "Work\nRate"),
        (16, "Add Pay"),
        (19, "Leave Pay"),
        (22, "Payroll"),
        (23, "Remark"),
    ]
    for c, text in headers:
        write_cell(sh, 2, c, text)
    sub = [
        (3, "Normal"),
        (4, "Actual"),
        (5, "Times"),
        (6, "Minute"),
        (7, "Times"),
        (8, "Minute"),
        (9, "Normal"),
        (10, "Holiday"),
        (16, "Normal"),
        (17, "Overtime"),
        (18, "Allowance"),
        (19, "Late/Early"),
        (20, "NoPaidLeave"),
        (21, "Deduction"),
    ]
    for c, text in sub:
        write_cell(sh, 3, c, text)

    for row_i, emp in enumerate(EMPLOYEES):
        punches = emp_days[emp["user_id"]]
        actual_hours = sum(hours_from(p) for p in punches.values())
        ot = sum(ot_hours(p) for p in punches.values())
        late_times = sum(1 for p in punches.values() if is_late(p))
        late_minutes = late_times * 10
        absences = sum(1 for p in punches.values() if is_absent(p))
        work_days = sum(1 for p in punches.values() if not is_absent(p))
        half_days = sum(1 for p in punches.values() if is_half(p))
        # half-day still counts as a work day in the original week file
        calendar_days = len(punches)
        expected_hours = calendar_days * 8
        r = 4 + row_i
        write_cell(sh, r, 0, emp["user_id"])
        write_cell(sh, r, 1, emp["name"])
        write_cell(sh, r, 2, "COMPANY")
        write_cell(sh, r, 3, expected_hours)
        write_cell(sh, r, 4, actual_hours)
        write_cell(sh, r, 5, late_times)
        write_cell(sh, r, 6, late_minutes)
        write_cell(sh, r, 7, 0)
        write_cell(sh, r, 8, 0)
        write_cell(sh, r, 9, ot)
        write_cell(sh, r, 10, 0)
        write_cell(sh, r, 11, f" {calendar_days}/{work_days} ")
        write_cell(sh, r, 12, 0)
        write_cell(sh, r, 13, absences)
        write_cell(sh, r, 14, 0)
        _ = half_days  # documented in notes; not a separate statistic column


def write_timecard_sheet(wb, period, days, start, emp_days):
    sh = wb.add_sheet("1,2,3")
    write_cell(sh, 0, 11, "Employee Attendance Table")
    write_cell(sh, 1, 33, f"Attendance date:{period}")
    write_cell(sh, 2, 33, "Tabling date:2026-08-24 12:00:00")

    for i, emp in enumerate(EMPLOYEES):
        b = BLOCK_STARTS[i]
        write_cell(sh, 3, b, "Dept.")
        write_cell(sh, 3, b + 1, "COMPANY")
        write_cell(sh, 3, b + 8, "Name")
        write_cell(sh, 3, b + 9, emp["name"])
        write_cell(sh, 4, b, "Date")
        write_cell(sh, 4, b + 1, period)
        write_cell(sh, 4, b + 8, "User ID")
        write_cell(sh, 4, b + 9, emp["user_id"])

        write_cell(sh, 5, b, "Absence\n(Day)")
        write_cell(sh, 5, b + 1, "Leave\n(Day)")
        write_cell(sh, 5, b + 2, "Trip\n(Day)")
        write_cell(sh, 5, b + 4, "Work\n(Day)")
        write_cell(sh, 5, b + 5, "Overtime(hrs.)")
        write_cell(sh, 5, b + 8, "Late")
        write_cell(sh, 5, b + 11, "Early")

        write_cell(sh, 6, b + 5, "Normal")
        write_cell(sh, 6, b + 7, "Special")
        write_cell(sh, 6, b + 8, "(Times)")
        write_cell(sh, 6, b + 9, "(Minute)")
        write_cell(sh, 6, b + 11, "(Times)")
        write_cell(sh, 6, b + 13, "(Minute)")

        punches = emp_days[emp["user_id"]]
        absences = sum(1 for p in punches.values() if is_absent(p))
        work_days = sum(1 for p in punches.values() if not is_absent(p))
        ot = sum(ot_hours(p) for p in punches.values())
        late_times = sum(1 for p in punches.values() if is_late(p))
        late_minutes = late_times * 10
        write_cell(sh, 7, b, absences)
        write_cell(sh, 7, b + 1, 0)
        write_cell(sh, 7, b + 2, 0)
        write_cell(sh, 7, b + 4, work_days)
        write_cell(sh, 7, b + 5, f"{ot:.1f}")
        write_cell(sh, 7, b + 7, "0.0")
        write_cell(sh, 7, b + 8, late_times)
        write_cell(sh, 7, b + 9, late_minutes)
        write_cell(sh, 7, b + 11, 0)
        write_cell(sh, 7, b + 13, 0)

        write_cell(sh, 9, b, "Time Card")
        write_cell(sh, 10, b, "Date/\nWeekday")
        write_cell(sh, 10, b + 1, "Before Noon")
        write_cell(sh, 10, b + 6, "After Noon")
        write_cell(sh, 10, b + 10, "Overtime")
        write_cell(sh, 11, b + 1, "In")
        write_cell(sh, 11, b + 3, "Out")
        write_cell(sh, 11, b + 6, "In")
        write_cell(sh, 11, b + 8, "Out")
        write_cell(sh, 11, b + 10, "In")
        write_cell(sh, 11, b + 12, "Out")

        for di, d in enumerate(days):
            r = 12 + di
            wd = WEEKDAY[calendar.weekday(start.year, start.month, d)]
            write_cell(sh, r, b, f"{d:02d} {wd}")
            in1, out1, in2, out2, ot_in, ot_out = punches[d]
            write_cell(sh, r, b + 1, in1)
            write_cell(sh, r, b + 3, out1)
            write_cell(sh, r, b + 6, in2)
            write_cell(sh, r, b + 8, out2)
            write_cell(sh, r, b + 10, ot_in)
            write_cell(sh, r, b + 12, ot_out)


def main():
    june = build_workbook(2026, 6, 30)
    june_path = OUT_DIR / "1_(June)Attendance Report(01-30)-TEST.xls"
    june.save(str(june_path))

    july = build_workbook(2026, 7, 31)
    july_path = OUT_DIR / "1_(July)Attendance Report(01-31)-TEST.xls"
    july.save(str(july_path))

    print(f"Wrote {june_path.name}")
    print(f"Wrote {july_path.name}")


if __name__ == "__main__":
    main()
