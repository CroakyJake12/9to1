# Build document tabs

Own one patch to the internal document strip in `9to1 Workspace/Dev/UI/DeveloperWorkbench.cui`, beginning at the container holding `dev-source-tabs`, on revision `2a9c4d980c4b16741f7d3880f1c982c0560eae88`. Atlas v4 routes `build-code` and `build-code-diagnostics`. Requirements UI-006 and APP-BUILD-001/002.

Center the open document group in the available space before the trailing Pin/Unpin action. Retain existing stable item keys, selection and close actions, keyboard names, order and narrow wrapping. Do not change the top application tab strip. Return the complete replacement container only, followed by up to three focused check descriptions. No source edits or other tools are needed for this specialist ticket. The coordinator will apply and verify the result.
