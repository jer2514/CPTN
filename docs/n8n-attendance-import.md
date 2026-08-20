# n8n attendance import

This guide explains how RSD imports biometric Excel attendance files, why n8n is used, and how to wire the workflow.

## Why n8n instead of clicking the web form

The Import Attendance screen is for payroll staff review:

1. Choose a project and click **Load**
2. Choose the biometric file and click **Load File**
3. Review the preview table
4. Click **Import Attendance** to save

n8n should not drive that browser. The biometric device already writes an Excel file (often `.xls` in Compatibility Mode). n8n watches that file, then calls a dedicated API that runs the same parser and save path as the green Import button.

```
Biometric export (.xls / .xlsx / .csv)
        │
        ▼
n8n (folder / Drive / email watch)
        │  POST multipart + X-Api-Key
        ▼
POST /api/attendance/import
        │
        ▼
AttendanceFileParser  →  match project employees  →  drop others  →  AttendanceImports / AttendanceRecords
        │
        ▼
Attendance → Attendance Records (staff / admin review and edit)
```

## What was implemented

| Piece | Role |
| --- | --- |
| `AttendanceFileParser` | Reads `.xls`, `.xlsx`, and `.csv`. Primary format is the biometric **Employee Attendance Table** (side-by-side time cards). |
| `AttendanceImportService` | Resolves the project, matches employees, previews, and saves a batch. |
| `AttendanceController` | Staff/admin UI: preview, import, records, row edit. |
| `AttendanceApiController` | n8n endpoint. No login cookie. Uses `X-Api-Key`. |
| `Attendance:ApiKey` in `appsettings.json` | Shared secret n8n sends on every request. |

Startup also creates `AttendanceImports` and `AttendanceRecords` if they are missing, so the feature works even when `Database.Migrate()` is commented out.

## File formats the parser accepts

### 1. Employee Attendance Table (primary)

Typical device file: `1_(August)Attendance Report.xls`

- Title: **Employee Attendance Table**
- Period: `Attendance date: 2026-08-01 ~ 2026-08-16`
- Employees are laid out **side by side**, each with Name, User ID, and a **Time Card**
- Time Card columns: Date (`01 Sa` … `16 Su`), Before Noon In/Out, After Noon In/Out, Overtime In/Out

Each person-day becomes one preview row:

| Employee ID | Employee Name | Date | Time In (1) | Time Out (1) | Time In (2) | Time Out (2) | Overtime In | Overtime Out | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | jun | August 16, 2026 | — | — | 12:52 | 14:02 | — | — | Complete |
| 2 | raph | August 16, 2026 | — | — | — | 14:02 | — | — | Incomplete |
| 3 | j | August 16, 2026 | — | — | — | — | — | — | Absent |

Days with no punches import as **Absent**. A sample of this layout is in `docs/samples/attendance-timecard-sample.csv`.

### 2. Flat daily or statistic sheet (fallback)

A single-table sheet with User ID / Employee ID headers still works if someone exports a flat list instead of the tiled time card.

## How employees are matched

The biometric **User ID** is matched to `Employees.EmployeeCode`, not the internal database key.

Examples that all resolve to employee `00001`:

- `1`
- `00001`
- `26000001` (device / plant prefix; the trailing sequence is used)

If the ID does not match, the parser tries the **Name** column against the employee full name or first name.

Matching is limited to employees assigned to the selected project (`Employee.ProjectId`). There is no fallback to the full employee list.

1. The file is extracted as-is (every person-day on the biometric dump).
2. User ID / name is matched to that project’s roster.
3. People who are not assigned to the loaded project are **filtered out** and are not previewed or imported.

If the project has no assigned employees, import stops with an error. If nobody in the file matches the project, nothing is saved.

## How status is derived

When the file has no Status column:

| Condition | Status |
| --- | --- |
| No punches that day | **Absent** |
| Late minutes &gt; 0 | **Late** |
| In without out (or out without in) | **Incomplete** |
| A complete Before Noon or After Noon pair | **Complete** |

Staff can change punches and status later from **Attendance Records**.

## Manual path (staff UI)

1. Sign in as payroll staff or admin.
2. Open **Attendance → Import Attendance**.
3. Type the project name (same autocomplete as Generate Payroll) and click **Load**.
4. **Choose File** → biometric Excel → **Load File**.
5. Review the extract summary and the filtered preview (only project employees). Check status pills (Complete / Incomplete / Late / Absent).
6. Click **Import Attendance**.
7. Open **Attendance Records** to search, filter, and edit.

n8n skips steps 4–6 and writes the same tables. Staff still use Records to review.

## API used by n8n

Base URL is your running RSD site, for example `https://payroll.example.com` or `http://localhost:5000`.

### Health check

```
GET /api/attendance/health
Header: X-Api-Key: <Attendance:ApiKey>
```

Returns `{ "success": true, "service": "attendance-import" }` when the key is valid.

### Import

```
POST /api/attendance/import
Header: X-Api-Key: <Attendance:ApiKey>
Content-Type: multipart/form-data
```

Form fields:

| Field | Required | Notes |
| --- | --- | --- |
| `file` | yes | The `.xls` / `.xlsx` / `.csv` binary |
| `projectName` | one of these | Exact project name, e.g. `Mandani Bay` |
| `projectId` | one of these | Internal project id if you already know it |

Success (200):

```json
{
  "success": true,
  "message": "Imported 4 row(s) for Mandani Bay.",
  "importId": 12,
  "projectId": 3,
  "projectName": "Mandani Bay",
  "fileName": "1_(August)Attendance Report.xls",
  "format": "Daily",
  "rowCount": 32,
  "matchedCount": 32,
  "unmatchedCount": 16,
  "filteredOutCount": 3
}
```

The API is excluded from the login filter (`AttendanceApi`). It does not use the staff session cookie.

### curl smoke test

```bash
curl -X POST "http://localhost:5000/api/attendance/import" \
  -H "X-Api-Key: change-me-n8n-attendance-key" \
  -F "projectName=Mandani Bay" \
  -F "file=@/path/to/1_(August)Attendance Report.xls"
```

Change the key in `appsettings.json` (and in n8n) before any shared or production use.

## n8n workflow

Use this after RSD is reachable from the n8n host (same machine, VPN, or public HTTPS).

### 1. Set credentials

In n8n, create a Header Auth credential:

- Name: `RSD Attendance API`
- Header name: `X-Api-Key`
- Header value: the same string as `Attendance:ApiKey`

### 2. Watch the export

Pick one trigger:

- **Local File Trigger** / **Read Binary File** if the device drops files on a shared folder
- **Google Drive Trigger** if someone uploads the monthly report to Drive
- **Email Trigger** if the device emails the `.xls`
- **Manual Trigger** while you are testing

Keep the original filename when possible. The parser only needs the file bytes and extension.

### 3. HTTP Request node

| Setting | Value |
| --- | --- |
| Method | POST |
| URL | `{{$env.RSD_BASE_URL}}/api/attendance/import` |
| Authentication | Header Auth (`RSD Attendance API`) |
| Send Body | yes |
| Body Content Type | Form-Data / n8n Binary |
| Field `projectName` | `Mandani Bay` (or an expression from the file name / folder) |
| Field `file` | Binary property from the previous node (usually `data`) |

If the project changes per file, map `projectName` from the folder name or a Set node. The name must match **Projects.ProjectName** exactly (case-insensitive).

### 4. Handle the response

- On `success: true`, optionally Slack/email “Imported N rows for {projectName}”.
- On 400/401, send the `message` field to the payroll staff. Typical causes: wrong key, unknown project, unreadable Excel, or no header row.

### 5. Optional schedule

If the device writes one file per cut-off, run the workflow after that time instead of watching the folder.

## Security notes

- Treat `Attendance:ApiKey` like a password. Do not commit a production key.
- The import API is reachable without a user login, so keep the key out of the browser and out of public n8n workflows.
- Prefer HTTPS in production.
- n8n should send only attendance files. The server rejects other extensions and caps uploads at 20 MB.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| 401 Invalid or missing X-Api-Key | Header name is exactly `X-Api-Key`. Value matches `Attendance:ApiKey`. |
| Project not found | `projectName` must match the project record. Staff spelling and extra spaces matter. |
| Could not find employee time cards | File should be the **Employee Attendance Table** with Name, User ID, and a Time Card (Before Noon / After Noon). |
| Could not read the attendance file | `.xls` needs `System.Text.Encoding.CodePages` (already registered at startup). Confirm the file is not password-protected. |
| Every row unmatched / filtered out | Employee codes in RSD should be the biometric sequence (`00001`). Device prefixes such as `26000001` are stripped to that sequence. Assign the employee to the selected project — people on other projects are dropped. |
| Tables missing | Restart the app so `Program.cs` can create `AttendanceImports` / `AttendanceRecords`, or apply the EF migration. |

## Code map

- Parser: `RSDSystem/Services/AttendanceFileParser.cs`
- Save / match: `RSDSystem/Services/AttendanceImportService.cs`
- UI: `RSDSystem/Controllers/AttendanceController.cs`, `RSDSystem/Views/Attendance/`
- n8n API: `RSDSystem/Controllers/AttendanceApiController.cs`
- Models: `RSDSystem/Models/Attendance.cs`
