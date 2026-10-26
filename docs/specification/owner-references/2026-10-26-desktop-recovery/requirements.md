# Desktop recovery additions — 26 October 2026

These are current owner instructions, extending the canonical specification. Older
screenshots describe observations to recheck, not proof about the current source.
The scoped recovery is authorized; unrelated local teams and Thunder stay preserved.
Full acceptance remains paused; affected compilation and basic checks still apply.

- Dulche is the intended default model connection. Distinguish installed artefacts
  from models genuinely available for inference; empty discovery must not conceal
  a verified installed model or invent runtime readiness.
- Extend the existing model browser with Hugging Face search, repository/model
  details and in-app installation of formats supported by the local engine.
  Public browsing needs no key. An optional key enables gated access only when
  the user's HF account already has access; it cannot bypass licence agreements.
- Store HF tokens only through the existing platform secure credential store,
  bound to the current Home profile. Never persist plaintext tokens in settings,
  environment, arguments, logs, URLs or Git. No plaintext fallback. Provide save,
  remove and credential-presence controls without redisplaying the stored token.
- Replace the large attachment-heading/tile menu with a compact rounded popup,
  grouped list rows, small functional icons and subtle hover states, akin to the
  supplied ChatGPT reference. Preserve real actions, search and keyboard access.
  The popup must render above the native composer, without stealing the caret
  during mention search or restoring focus over an intentional new selection.
- Use real Unicode emoji rendered by the device's colour emoji fonts. Do not
  substitute monochrome icon SVGs. Preserve search, joined sequences, skin tones
  and the existing random emoji trigger. Retain licensed fallback coverage for
  platforms without a suitable installed emoji font.

The inspected local main was clean at 903c858a; the inspected detached transition
worktree was clean at 106dfeed, already in main. The 903c858a push contains generated
Canvas outputs and an xdotool package, not app source fixes. Do not repeatedly ask
for another push from those clean checkouts. Installed desktop behaviour remains
a separate native verification requirement.

## Later menu naming and light appearance clarification

- Attachment menu labels are Specialists (formerly Agents), Assistants (add a real
  catalogue/action), Skills, Plugin (singular) and Apps. Skills, Plugin and Apps
  need distinct icons that match their functions; preserve the compact list design.
- Repair incomplete light-mode adaptation through shared theme ownership. Check
  live dark/light switching as well as startup colours, including popup surfaces,
  composer, text and icons. The isolated native-probe owner window had a default
  white background while its content was dark; that capture alone is not evidence
  of a fully initialized product light theme. Retain the owner's broader report
  and verify actual application surfaces rather than correcting only the probe.

- Hover modifies the element's current effective colour instead of replacing it
  with an unrelated theme palette colour (for example, turning white black).
  Preserve app/custom colours, enabled/selected/focus state and theme effects,
  including Glow. Verify light, dark and explicitly coloured surfaces.
- Hovering a tab applies one continuous effect across its label and close area.
  The close control may add its own local cue when directly hovered, while keeping
  selected/focus states and the close hit target intact.
