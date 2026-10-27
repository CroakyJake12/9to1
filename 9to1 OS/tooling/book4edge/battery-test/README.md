# Experimental NP960XMA battery reporting

The live desktop displayed 0% and UPower logged invalid readings from
`qcom-battmgr-bat`. Samsung's EC is the actual battery interface.

`book4_ec.c` derives from n1c0la84/book4-edge-linux at commit
`6026e235a389a81f9264cad587c6410d902633ec`,
`drivers/book4-ec/book4_ec.c` (GPL-2.0-only). Original SHA-256:
`ea477c5020ff844767dd51b2273154ad525862b0ece51c3a1980bf10d0de7f66`.
Source: https://github.com/n1c0la84/book4-edge-linux/blob/6026e235a389a81f9264cad587c6410d902633ec/drivers/book4-ec/book4_ec.c

The local NP960XMA-KB1UK Windows EC2.sys has the exact hash documented by
that project's protocol research. This laptop's DSDT independently confirms
SAM060B, I2C1 address 0x64, and the battery fields at 0x80, 0x84, 0xa0,
0xa4, 0xb0 and 0xb4. Private hardware tables and driver binaries are not
included in this repository.

Local changes restrict the model, reject implausible readings, derive
percentage from remaining/full charge, match the local ACPI signed-current
conversion, and stop polling across system sleep. The driver issues only
the EC battery read command (0x88), with the normal volatile mailbox
coordination writes. It does not implement EC-space writes (0x89), firmware
updates, fan controls or charging limits.

Build against the headers and symbol versions from the existing matched
6.19.0-9to1-book4edge16 kernel. A successful build is not a hardware pass.
Loading is opt-in from the live USB menu; the previous desktop boot remains
available. Battery percentage and charger transitions require physical tests.
