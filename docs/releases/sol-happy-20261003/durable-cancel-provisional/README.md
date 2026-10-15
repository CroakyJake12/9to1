# Provisional durable cancellation repair

Source commit `a4be0cd0fd8af22ccde5fb609e5af70ee0ace271` starts from exact owner delivery `eecadfc7463c944bcc6f9788075c293ab14120f9`. This branch is a reviewable proposal; current Team A source adoption and receiving/global acceptance remain open.

A cancellation that reads a task before another service commits completion could overwrite that completion and its result. `TryCancelAsync` now writes only when the requested ID, owner, exact original persisted JSON and current terminal guards still match. A changed or deleted row returns false. The original Completed/Cancelled guard and all other source bytes are preserved.

The unchanged three original regression controls, seven new real SQLite race/representation controls and two existing owning persistence controls pass in Debug and Release: 12 passed, 0 failed, 0 skipped in each configuration. Exact commands, TRX, source hashes and safe retained-database observations accompany `proposal.json`. Original failing records remain preserved by immutable release references and hashes; they were not rerun or relabelled.

No shared active owner branch, root release/coordination tree, production service or provider was changed. Full current normal-project/native/hosted/global gates remain separate. `adoption-request.json` states the exact source/body approval requested.
