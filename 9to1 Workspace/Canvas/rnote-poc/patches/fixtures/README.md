# Exact upstream source fixtures

These two unmodified, noncompiled test fixtures come from flxzt/rnote commit
8cac558fa73ee63d958cec05124ea2fdbd7d608d, crates/rnote-engine. They support
offline exact-preimage patch tests; they are not another drawing implementation
or a substituted Cargo dependency. GPL-3.0-or-later; see the adjacent preserved
LICENSE-GPL-3.0-or-later.txt. The source-patches manifest pins original/postimage
bytes. Real builds vendor the full locked upstream/dependency source and preserve
its actual licence files plus the applied changes in corresponding-source output.
