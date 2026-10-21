// Grants the authentic manual (no-AI) Home source-EDIT fence test project friend access so
// existing, preserved assertions can observe the fence's retirement state (fence.IsClosed)
// directly. This is an accessibility grant only — it is not a security boundary, and it never
// affects runtime admission, currentness, or claim behavior. It lives beside the fence source
// so it is compiled into whichever assembly hosts HomeSourceEditCommitFence.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Haven.Desktop.Tests")]
