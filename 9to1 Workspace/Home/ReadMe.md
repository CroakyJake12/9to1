# Home and global settings

Home owns the Dashboard, app discovery and global settings shared by the supported hosts. Dashboard and New Tab are distinct destinations. Appearance belongs to the current Home profile, including the theme, mode, material levels, accessibility and relevant accent choices.

Start at the Home CUI document for visible layout and the core service for state. On native hosts, the preference adapter supplies the authorized snapshot, accepts revision-checked actions and publishes the resulting presentation. Preserve selected scope, pending changes and existing values when reopening the profile.

Test the actual Global Settings route after editing its CUI. A saved value, native material change and re-opened control state answer three separate verification questions.

## Read the current device state

The native preference file projects legacy appearance values into the current Home observation. Preserve existing colour overrides and custom definitions on unrelated changes. Use the expected revision before writing and accept only the owning service’s receipt. Follow-system mode uses the platform colour observation. High contrast keeps stored brightness and contrast for later use.

[Home CUI](UI/Home.cui) · [Preference service](Source/Home/Core/HomeThemePreferenceService.cs) · [Native adapter](../shared/src/Haven.Desktop/Services/NativeHomeThemePreferences.cs) · [Home tests](Tests) · [Home and settings requirements](../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
