# IP Setting app — code audit

Scope: `App.xaml(.cs)`, `MainWindow.xaml(.cs)`, `models/*.cs`, `xaml/*.cs(.xaml)`, `ip.csproj`, `app.manifest`, `setup-windows.ps1`. No `dotnet` SDK in this sandbox (and WPF can't run on Linux anyway), so nothing here was build-verified — build + smoke-test on Windows before shipping.

## Fixed directly (already written back to your files)

**`GetAdvancedNICProperty` never matched the NIC it was asked about.** It walked every adapter's registry subkey under `Class\{4d36e972-...}` and returned the first one with any "jumbo"-named value — regardless of which NIC you'd selected. So the Jumbo Frame status shown for a device could genuinely be a different adapter's setting. Now it matches on `NetCfgInstanceId` (== `NetworkInterface.Id`) before reading the property, and actually uses the `propertyName` argument instead of a hardcoded `"jumbo"` literal.

**`jumbo_frame_maybe` and `jumbo_frame_no` had the identical condition.** Both fired together, always — "check Device Manager" and "update your driver" showed up as the same case, so the distinction never existed in practice. Split them: `maybe` = the NIC's jumbo setting couldn't be read at all; `no` = it was read and it's confirmed wrong.

**Null crash in the Jumbo Frame block.** `selectedMachineType.JumboFrame` is a nullable CSV column; the old code called `.Replace()` / `.Equals()` on it directly with no null guard and *outside* any try/catch (unlike the IP block right above it). Any machine-type row with a blank Jumbo Frame cell would throw an unhandled `NullReferenceException` on the UI thread — there's no `DispatcherUnhandledException` handler in `App.xaml.cs`, so that's a hard crash, not a caught error. Now null-safe.

**`SelectedMachineType` setter crashed on null.** `_selectedMachineType?.System.Equals(value.System)` dereferences `value` unconditionally — clearing the Machine Type grid's selection (`SelectedItem` → null) would NRE. Now both sides are null-guarded.

**Orphaned `cmd.exe` process per IP change.** `Run()` launched `cmd /k script.bat` — `/k` keeps the shell open at a prompt after the script finishes. Every "Next" / "Retry" left a hidden `cmd.exe` running for the rest of the app's life. Changed to `/c`, which exits when the script does.

**Busy-loop before you've selected anything.** The background refresh thread called `Refresh()` in a tight `while (!closed)` loop; the only throttle (`Thread.Sleep(1000)`) lived *inside* `Refresh()`, gated behind "NIC and machine type both selected." Before that point — e.g. right after launch — it was pegging a CPU core running WMI queries and adapter enumeration as fast as it could. Moved the sleep to the outer loop so it paces at ~1s regardless of selection state.

**Machine-type/device-IP data fetched over plain HTTP.** `devices.csv` came from `http://am.co.za/...` while the icon images on the same host correctly use `https://`. That CSV drives what IP gets written to a machine, so it's exactly the kind of response you don't want tamperable in transit. Switched to `https://`.

**`new HttpClient()` per request, 7 call sites.** Known .NET foot-gun — each instance owns its own connection pool that isn't released promptly on `Dispose`, and repeated churn can exhaust sockets. Replaced with one shared static `HttpClient` for the app's lifetime.

**Dead/broken XAML bindings.** `cbCountry1`/`cbCountry2`'s `SelectedValue` and `txtPhone1`/`txtPhone2`/`txtOTP`'s `Text` all bound to `WindowViewModel` properties (`Country1DialCode`, `Country2DialCode`, `UserInput`) that don't exist on the class — and all three text boxes bound to the *same* nonexistent `UserInput` property. These bindings did nothing (the code-behind reads the controls directly instead), but they spam binding-failure noise into the debug output on every keystroke. Removed since nothing consumed them.

**Shared `RadioButton` `GroupName` across two different DataGrids.** Both the Network Interface grid and Machine Type grid's "Select" radio column used the literal `GroupName="Selected"`. WPF scopes radio-group exclusivity to the nearest `NameScope` (the Window, here), not per-DataGrid — so the two grids' selection radios were in the same exclusivity group even though they're conceptually unrelated. Split into `"SelectedNic"` and `"SelectedMachineType"`.

**`ip.csproj` had a stale `<None Remove="no.png" />`** that doesn't match anything (the actual file is `img\no.png`) — a no-op left over from a refactor. Removed.

## Left alone, flagged for you to decide

**`SkipLoginForTesting = true`** (`MainWindow.xaml.cs`, top of the class) bypasses the entire WhatsApp OTP activation flow. It's already commented "Set back to false before shipping" — that's your call, not a bug I should silently flip, since you're clearly using it to develop against right now. Just don't forget it's still `true`.

**MD5(OTP) stored permanently in the registry as the auth token.** `ConfirmOTP()` stores `Registry...\Login = MD5(otp)` and re-sends that same hash to the server on every future launch as proof of activation — effectively turning a short-lived numeric OTP into a permanent password. A numeric OTP's hash is trivially brute-forceable offline if ever intercepted or leaked (e.g. from the registry on a shared machine), and OTPs generally shouldn't outlive their one-time purpose. This is a client+server protocol change, not something I should patch unilaterally from the client side — flagging for when you touch that flow.

**`netsh` commands interpolate the raw adapter name** (`nicItem.name`) into a command string without quoting/escaping beyond the surrounding `"..."`. Adapter names come from Windows, not attacker input, so practical risk is low, but it's worth a `Replace("\"", "")`-style guard if you ever accept custom adapter naming.

## Design / "modern look" — what I changed vs. what's still open

**Changed:** the DataGrid columns didn't stretch to fill the card — visible in your own screenshot as a wide empty gutter to the right of "Up Time"/past the last column. Gave both grids `ColumnWidth="*"` with fixed pixel widths on the short columns (Select/Type/Speed/Up Time/Image/NIC IP/Jumbo Frame/Device IP), so the free-text columns (Network, System) absorb the remaining width instead of leaving dead space.

**Not changed** (visual/behavioral, wanted your steer before touching further — see below):
- No dark-mode palette. The whole "Amber Glass" system is light-only; Windows 11 apps that don't respect system theme read as dated next to ones that do.
- Native OS title bar doesn't match the glass/rounded-corner aesthetic used everywhere inside the window. `WindowBackdrop.cs` already talks to DWM for rounded corners — the natural next step is `DWMWA_CAPTION_COLOR`/`DWMWA_TEXT_COLOR` to tint the title bar to match, or a fully custom `WindowChrome`.
- Notification banners (info/danger cards) are text-only — no leading icon. A small ⚠/ℹ glyph would make the banner list easier to scan at a glance, closer to Fluent's InfoBar pattern.
- The Network Interface tab has a large dead vertical gap between the grid card and the notification banner when there's only 1–2 NIC rows (fixed `RowDefinition Height="*"` soaking up leftover space with nothing in it).

Want me to go implement any of those? Dark mode and the title-bar tint are the two with the most visual payoff for the effort.
