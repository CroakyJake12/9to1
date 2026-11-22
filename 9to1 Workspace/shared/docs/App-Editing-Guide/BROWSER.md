# Browse sessions, tabs and private data

The top application strip belongs to the native Browse workspace. It presents tabs, selection and overflow. The browser session service and browser data service own navigation state and persisted user data. Keep a tab’s stable identity when its title or visible position changes.

Document the operating-system backend used by the candidate when diagnosing a browser failure. A native page embedded in Haven and a website loaded through a web engine have different close, permission and storage paths. Exercise both when changing the shared strip.

Test a long title, multiple tabs, close, keyboard selection and overflow. With a private tab, check the actual history and restore paths after closing it. An available address or tab title alone does not demonstrate that an interactive browser tool can access the page.

[Workspace strip](../../../Browse/NativeUI/UI/BrowseWorkspace.cui) · [Session service](../../src/Haven.Browser/Session/BrowserSessionService.cs) · [Browser data](../../src/Haven.Browser/Data/BrowserDataService.cs) · [Browser action boundaries](../BROWSER-AUTOMATION-SAFETY.md)
