# TBK Report

Collector numbering: the header in both apps has “№ трубы”, a fixed “ГК” label,
and an optional field of up to three decimal digits (0–999). The number is saved
in each report header, saved report defaults and transfer files, but not drafts.
It is saved with the other report data and restored from that saved header.
Numbering continues
from the latest earlier report with welding for that collector, across months;
a new collector starts at 1. Changing collector rebases both shift ranges while
retaining their entered counts. Unnumbered legacy reports remain a separate group.
Monthly opening data is keyed by both month and collector number. A blank collector
is its own group and applies only to reports whose header also has no collector.
Last-joint numbering and monthly welded/repair totals use only the matching group.
Changing the collector in the header immediately loads that collector's opening data.
Excel/PDF formatting stays unchanged; the in-app history shows the collector number.

Data transfer: Windows settings let the user choose any combination of history, problems,
equipment, monthly opening data, application settings, header fields, and technicians with
their signatures. Header fields exclude technicians and signatures because those form a
separate transfer section. History entries remain complete records. Version 3 records the
selected sections in `TBK Backup.tbkdata`; import changes only those sections and preserves
all unselected local data. Transferred application settings are calculation mode and the two
automatic export switches. The device-specific export path and drafts are never transferred.
Export writes the file directly to the folder currently used for Excel and PDF reports,
replacing the previous backup with the same name. History uses raw stored records without
rebasing numbers. Import requires confirmation, merges dates and replaces matching dates and
supplied opening records. Months with no supplied opening retain their local values. Versions
1 and 2 remain readable. Android offers the same section selection and uses the same versioned format.
Changing only the report date does not create a draft for either shift; an existing
draft follows the selected date.
All affected files are prepared before replacement, with backups for rollback.
The current editor is retained; imported defaults load on the next startup.
Settings also contain independent automatic Excel and PDF export switches. After a report passes validation and is saved to history, enabled formats are written to the configured export folder; existing files for that date are updated. A failed history save never starts automatic export.
Transfer checks are in `tests/DataTransferChecks`; running them also requires compilation.

C# / Avalonia / MVVM. Windows prototype first; Android later.

Android has a separate UI project at `src/TbkReport.Android` (see its README),
and does not reference the Windows application. Shared models and services live
in the parallel `src/TbkReport.Core` project. Platform-specific paths live in
each app's `Common.cs`: Windows stores settings, drafts and history in
`%LocalAppData%\TBK Report`, while Android uses its app-specific
`Android/data/.../files` directory. Windows copies legacy settings, drafts and
history from the executable folder on first launch after an update.
Android opens the first shift of the report editor, with a persistent report date
above the scrollable form. All report sections are selected in the left drawer;
there are no tab strips. Mobile shift, problems, equipment, header and monthly
opening forms use the shared Core ViewModels, including draft and history-save
rules. The top-right menu contains save, export, new report and settings.
Android history uses the same month/day/report flow and shared totals, with
phone-sized tables and matching on-screen/system Back navigation. Android export
uses the shared Excel/PDF exporters. Mobile settings select a persistent export
folder through the Android document picker, with Documents/TBK Report as default.

Application version: v1.3. The main window uses the TBK logo from the Excel report template as its application icon.

Official Android Release APKs are signed with the permanent `TBK Report` identity.
`scripts/Build-AndroidRelease.ps1` loads the local key and password from `signing`;
both secret files are excluded from Git and must be backed up together. Version code 5
is the first build with this identity. Earlier Android Debug-signed installations must
be removed before installing it, so their application data should be exported first.

The Windows distribution uses `installer/TbkReport.iss` and is built by
`scripts/Build-WindowsInstaller.ps1`. It contains the x64 .NET 10 runtime and does not
require .NET to be installed separately. Its only configuration page contains the
installation path and a desktop-shortcut checkbox. The default path is Program Files;
the installed application directory remains read-only for normal users. Uninstall asks
whether to preserve or delete user data and cancels when the user closes that choice.

Current UI: File, Help, Report History, and Settings menus. The report has exactly three tabs: First Shift, Second Shift, Report Data. Report Data tabs begin with Problems and Equipment, followed by Header and month-specific opening data (last joint number and accumulated monthly welded/repair counts). Opening counts seed cumulative monthly totals: first shift, then second shift; an absent first shift passes the baseline directly to the second. The final total is the last cumulative value, never the sum of shift snapshots; the last joint seeds continuing joint numbering. Existing opening data cannot be overwritten accidentally: identical values are ignored, and replacement requires clearing the saved values first. Report History is a reusable view in the main application surface for month/year, day, and report preview, without report editing tabs or a separate window. A date picker, save-to-history button and new-report button sit above the report tabs. The selected date determines the history day/month file, Excel date/month and monthly totals. Header fields are customer, project, site, complex, pipe, and up to two technicians per shift; these belong to each report and are stored in its history entry. The shift tabs have first/last joint, start/stop time, repair count, inclusive joint count, and comments with either one time or a time range. Comments are always ordered by their start time; changing a shift start/stop time creates or updates its matching “Старт СМР” / “Стоп СМР” comment. When no draft exists, startup leaves the shift report form empty even when today's report exists in history; history data is only used to calculate the next joint number and monthly totals.

In the Windows Header tab, entering the first technician for a shift reveals an Add Signature button above that technician field. The signature is drawn inline either by holding the left mouse button or, for touchpad use, by holding either Shift key while moving the pointer; releasing Shift finishes the stroke. Opening the editor centers the complete drawing area and its Clear/Save buttons in the visible header area. The signature is stored with the header in defaults and dated history, and inserted into C14:C15 or G14:G15 in Excel and PDF exports, scaled proportionally and aligned to the right edge of the cell. A legacy history entry without a signature uses the saved signature only when its first-technician name exactly matches the saved header name. Clearing the first technician also clears that shift's signature.
Android provides the same signature actions in its Header section. The user draws directly with a finger; Save keeps the new signature, while Cancel restores the value present when the editor was opened. Existing signatures change the action label from Add Signature to Edit Signature. Android Back cancels an open signature editor. Both platforms hide New Report while a history entry is being edited and provide an explicit Cancel action that restores the normal draft without changing history.

Draft: `draft.json` is updated atomically after every change to the report date, either shift's times, joint numbers, repair count, saved comments, or the comment currently being entered or edited. If it exists at startup, the draft is restored and the application shows a notification. Header, problems, and equipment are excluded because they remain controlled by the Report Data save button. A successful history save or a confirmed new report deletes the draft, so the next startup opens a clean shift report.

Calculation mode is selected in Settings → «Расчёт по» in both applications and saved immediately. The default «Номерам стыков» keeps the existing input and inclusive count calculation. In «Количеству стыков», the user enters a positive integer count in place of the number inputs; first and last joint numbers become large read-only results below. The first number continues history/monthly opening data, and the second shift follows the first shift's last number. Last = first + count − 1. Changing the count, date, or monthly opening data recalculates the ranges. Invalid, zero, negative, fractional and overflowing counts are rejected; reports without welding still use the existing timed-comment flow. Mode and unfinished count input survive restart; history and exports continue to store/use canonical joint numbers. Old settings and drafts remain readable.

Rules: joint count = last - first + 1 for valid positive integer ranges; empty or invalid input gives zero. In number mode, the next shift suggests last actual welded joint + 1 without overriding a manually entered first joint.

History save, Excel export, and PDF export require a start time in at least one shift, unless at least one timed comment has been added to explain why welding did not occur. Any shift with a start time must also have its first and last joint numbers and stop time; missing required fields are highlighted until filled.

Excel export fills the scaled legacy report template. Each timed comment is written to its own wrapped cell; longer comments receive a merged block with enough rows, and the report below moves down when the comment area overflows. Monthly welded/repair totals are calculated from saved reports from the first of the month through the report date, as cumulative snapshots after each active shift and a final total. The current report replaces its saved version in that calculation. The template's repair percentage formulas are preserved and recalculated by Excel. PDF export first creates the same filled Excel report and then renders it with `GemBox.Spreadsheet` 2026.9.128. It prints the actual report area on portrait A4 at 55% scale, uses 0.75-inch top/bottom and 0.25-inch left/right margins, and centers the table horizontally. The GemBox package and its dependencies are kept in the offline cache; the free edition rejects reports over its 150-row limit instead of truncating them.

Windows storage: history files `yyyy-MM.json.gz` live in `%LocalAppData%\TBK Report\History`; `settings.json`, `draft.json` and application temporary files live under `%LocalAppData%\TBK Report`. The separate `settings.json` stores default header, equipment, problems, and the shared Excel/PDF export folder; the Data Report Save button updates the report defaults, and the Settings view updates the export folder. These values load at startup. The default export path is `Документы\ТБК Отчёт`. Exports write `Отчёт-dd-MM-yyyy.xlsx` or `Отчёт-dd-MM-yyyy.pdf` directly to that folder and ask before replacing an existing file. Export does not save history or clear the current draft. Dated history remains separate and preserves each saved report's own values. Each versioned gzip JSON file holds one month of input data (including timed comments and optional monthly opening data) as indented, line-broken UTF-8 JSON with readable Russian text, without document formatting or cached monthly totals; legacy version 1 files remain supported. Files are written to a temporary file and then atomically replaced; unreadable history is never silently overwritten. Use the built-in data export to transfer selected data to another computer. History includes only reports explicitly saved with the Save button; earlier Excel and PDF files are not imported automatically. Missing counts remain dashes; an entered zero is retained as zero. There is currently one report per date and no separate sites/projects. All NuGet packages, including PDFsharp and its dependencies, are mirrored under `offline-cache/packages` and can be restored with `NuGet.Offline.Config` without internet access.

Problems: untimed entries with add/edit/delete and automatic renumbering. They are saved per report, shown in history and exported as separate numbered merged blocks in I:M under Problem Questions. Long text grows vertically; overflow inserts rows below the reserved problem area, independently of shift comments. Empty problem lists export a dash.

Equipment: add/edit/delete a name and nonnegative integer quantities in work and reserve. Entries are stored with the report and displayed in history. Excel fills I:K, L (Рб), M (Рз), one entry per row starting at row 29. The problem heading follows the last equipment row without a blank row; adding/removing equipment moves that heading down/up independently of comment overflow. With an empty list, the problem heading starts directly below the equipment headings. Template sample equipment is cleared. Problem headings and text use the template's Times New Roman 18 styles.

History report previews show welded joints and repairs from the start of the month through the selected report date, as cumulative snapshots after each active shift and a final total. Totals are calculated from saved inputs when opening the entry, including older history files; missing values display a dash and explicit zero remains zero. Joint numbers continue across months. A new report reserves the next joint number from the latest earlier report in history, preferring its second shift's last joint and falling back to the first shift. The number is inserted into a shift's first-joint field only after its start time is entered, so work in the second shift alone fills the correct field. When the first shift has a valid joint range, the second shift uses the next number after its last joint. Users can edit either field.

Both applications can delete an individual report from its history preview after confirmation. Deletion atomically rewrites only its month, preserves other reports and monthly opening data, and leaves the current editor, draft and exported files untouched. Monthly totals are recalculated from the remaining reports. An empty month disappears from the history list while its opening data remain stored. Android Back cancels a pending deletion before leaving the preview.

Build only on explicit user command. Build: dotnet build src/TbkReport/TbkReport.csproj -c Release
Run: src/TbkReport/bin/Release/net10.0/TbkReport.exe

Excel and PDF export follow the visible report: the editor exports current inputs, while an open history day exports that saved report with its date and monthly totals. History lists require opening a day first. Export captures a snapshot before overwrite confirmation and leaves the editor and draft untouched.
