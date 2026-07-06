# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

.NET MAUI (net10.0) point-of-sale app named **MPos Accurate** that is a mobile/desktop frontend for **Accurate Online** (Indonesian cloud accounting). The app never calls Accurate directly — it talks to a PHP bridge backend (`App.API_HOST`) which proxies to the Accurate API. The codebase and UI are in **Indonesian**; match that language in user-facing strings and comments.

Domain glossary: *Faktur* = invoice, *Pembayaran* = payment, *Diskon* = discount, *Konsumen* = customer, *Barang* = item/goods, *Biaya* = expense/cost, *Gudang* = warehouse, *Kuota* = quota, *Karyawan/Sales* = employee/salesperson, *Kas/Bank* = cash/bank account, *Lunas* = paid, *PPN* = VAT (11%), *COA* = chart of accounts, *No Seri/SN* = serial number.

## Build & Run

Targets multiple platforms; on Windows the active dev target is `net10.0-windows10.0.19041.0`. Other frameworks (`net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`) only build on their respective host OSes.

```bash
# Build for Windows (the VS Code default build task = "build-maui-win")
dotnet build MyPosAccurate2026.csproj -f net10.0-windows10.0.19041.0

# Run (VS Code launch config ".NET MAUI Windows (F5)" builds then runs the exe)
dotnet build MyPosAccurate2026.csproj -f net10.0-windows10.0.19041.0
# then launch bin/Debug/net10.0-windows10.0.19041.0/win-x64/MyPosAccurate2026.exe

# Android
dotnet build MyPosAccurate2026.csproj -f net10.0-android
```

There are no tests in this project.

## Switching the startup page (dev shortcut)

`App.xaml.cs` sets `MainPage` directly to whichever page is being worked on (currently `Sales.Pembayaran_Faktur`), bypassing login. The normal flow starts at `Login`, which on success sets `Application.Current.MainPage = new Sales.List_Faktur()`. When finishing a feature, restore the intended startup page. `AppShell`/`MainPage` are the unused MAUI template scaffolding — real navigation is plain `NavigationPage` push/pop, not Shell routing.

## API configuration

`App.xaml.cs` holds two static endpoints set in the constructor:
- `App.API_HOST` — base for all data endpoints, e.g. `https://php.ahlikoding.online/pos-accurate/api/`
- `App.API_LOGIN` — the login endpoint (`config/mobile-login-api.php`)

To point at a local backend, swap the commented `publik` URL in the constructor. Product images are served from `{API_HOST without "api/"}images/{itemNo}.jpg`.

## Architecture

This is a **code-behind app — there is no MVVM, no DI for services, no shared API client.** Each `ContentPage` is self-contained and does its own networking. Understand this pattern before adding code; follow it rather than introducing a framework unless asked.

The repeated per-page idiom (see any `Load*`/`Fetch*` method):
1. `string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();`
2. `new HttpClient()` locally (often `using`), set `Authorization = new AuthenticationHeaderValue("Bearer", cleanToken)`.
3. `await client.GetAsync/PostAsync`, read string, guard `responseContent.StartsWith("<")` (PHP error/HTML instead of JSON).
4. `JsonConvert.DeserializeObject<...>` (Newtonsoft) into response classes that are **nested inside the page class**, checking `status == "success"`.
5. **Wrap every UI mutation after an await in `MainThread.BeginInvokeOnMainThread(...)`** — required to avoid crashes on Windows/Android. This is load-bearing; preserve it.

Session state lives entirely in `Preferences` (`TOKEN_KEY`, `ID_USER`, `USERNAME`, `NAMA_LENGKAP`) — there is no auth/session service.

### Pages (`Sales/`)
- **List-Faktur** — invoice list with date-range + search filter, manual paging (`page`/`limit=100`, `_hasMoreData`/`_currentPage`), grand-total accumulation. Tapping an unpaid invoice offers Edit (fetches `detail-invoice.php`, hands data to `New_Faktur.LoadEditData`) or Hapus (DELETE with JSON body).
- **New-Faktur** — create/edit invoice. Same page serves both modes; edit mode is flagged by `_editInvoiceId > 0`. Item search → autocomplete → opens **ItemAdd**, which returns a `CartItemModel` via the `OnItemSaved` event. Deleted DB-backed rows are tracked in `_deletedCartItems`/`_deletedBiayaList` and re-sent with `_status = "delete"` so the backend knows to remove them. Up to 3 promos per invoice, carried as `numericField1..3`. `KalkulasiSemuaTotal()` is the single source of truth for all money math (subtotal, discount nominal-or-percent, expenses, 11% PPN, grand total, round-down to nearest 100).
- **ItemAdd** — per-item dialog: loads stock/price (by customer price category), promo list, salesman list, serial numbers. Validates qty vs available stock and SN count vs qty. Applying a promo adjusts unit price and calls `update-kuota.php`; removing a promo'd item calls `cancel-kuota.php` to restore quota.
- **Pembayaran-Faktur** — payment screen (in progress); loads cash/bank accounts.

### Shared model conventions
- JSON DTO classes are declared **inside** the page class that uses them (e.g. `List_Faktur.InvoiceData`, `New_Faktur.CartItemModel`). `CartItemModel`/`DetailSerialNumber` from `New_Faktur` are reused by `ItemAdd` via `using static`.
- Models expose computed display properties (e.g. `FormattedTotalAmount`, `DisplayImage`, `StatusBgColor`) for direct XAML binding — UI formatting lives on the model, not in converters.
- Currency is formatted with `CultureInfo("id-ID")` as `Rp {n:N0}`; parse user input by stripping `"."`, `"Rp"`, `"%"` before `double.TryParse`.

## Conventions

- **Indonesian** for all user-visible text (`DisplayAlertAsync` titles/messages, button labels) and most comments.
- `Nullable` is **disabled** and `ImplicitUsings` enabled project-wide.
- XAML uses compiled source-gen (`MauiXamlInflator=SourceGen`). New XAML pages under `Sales/` need a `<MauiXaml Update="..."><Generator>MSBuild:Compile</Generator></MauiXaml>` entry in the `.csproj` (see existing entries).
- Number-entry fields use a `_isFormatting*` bool guard around programmatic `.Text` assignment to prevent the `TextChanged` handler from infinite-looping while inserting thousands separators.

## Dependencies

CommunityToolkit.Maui (registered via `.UseMauiCommunityToolkit()` in `MauiProgram.cs`), Newtonsoft.Json (all serialization — not System.Text.Json).
