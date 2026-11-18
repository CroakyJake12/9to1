# Terminal — Command and AI modes — Terminal sessions and process state (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2318 · v2 lines 13469–13469

### UX27-OTHER-012 — Terminal sessions and process state

### V2-B2319 · v2 lines 13471–13471

**B/D.** Tabs/panes show actual session, working directory and process status. Terminal text selection/copy is distinct from sending input; pasting multiline commands follows the configured safety/confirmation policy. Closing an active terminal makes the effect on its owned process clear instead of silently killing unrelated services.

### V2-B2320 · v2 lines 13473–13473

Resize, scrollback, Unicode/colour handling and keyboard routing use the real terminal adapter. A disconnected remote terminal retains its transcript and explains reconnect semantics; a new shell is not falsely presented as continuation of the previous process. Secrets must not be copied into project reports merely because they appear in terminal output.
