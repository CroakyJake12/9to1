This isolated fixture links 15 existing Mathematics test methods and four unchanged
native owner control/adapter files. The tests use the original shared math models,
Application sessions, CUI runtime, Home equation renderer, CSharpMath and ScottPlot.
The only new C# is headless test infrastructure reproducing the original owner's
PixelAppBuilder. It supplies no domain model, parser, storage or renderer.

The actual runner discovered and executed 22 cases. The first bounded run passed
19 and failed three native cases because the fixture omitted the original owner
assembly-wide test isolation policy. Linking the unchanged original AssemblyInfo.cs
then passed all 22 unchanged cases, with zero build warnings or errors. Both cohorts,
source cuts, exact commands and executed runtime hashes remain preserved in the B2
evidence. This is bounded native acceptance; no parent application reference or
browser route is registered.

Current canonical requirements are Forms mathematical authoring and reusable shared
objects at paragraphs 4943–4951 in the current 8534-paragraph live snapshot. A
codec round trip or native control test does not establish durable Forms publication,
browser compatibility, permissions or complete mathematical authoring. The actual
Forms typed math field/response/commit contract and symbolic evaluator remain
owner dependencies; see OWNER-CONTRACT-REQUEST.json and the B2 evidence inventory.
