"""Require actual PE/PDB identity and complete reviewed Home source documents.
This observes compiled files and never grants authority or fabricates runtime outcomes.
"""
import hashlib, importlib.util, json, pathlib, shutil, sys

sys.dont_write_bytecode = True


def verify(root, out, target, suite, cut_paths, digest):
    root = pathlib.Path(root)
    target = pathlib.Path(target)
    home = '9to1 Workspace/Home/'
    shared = '9to1 Workspace/shared/'
    assemblies = {
        'HavenOS.Home': [
            home + 'Source/Home/HomePermissionTrustService.cs',
            home + 'Source/Home/Core/HomeAppAiServices.cs',
            home + 'Source/Home/Core/HomeApprovalPromptFlow.cs',
            home + 'Source/Home/Core/HomeCoreApi.cs',
        ],
    }
    if suite == 'home-full':
        assemblies['HavenOS.Home.Tests'] = [
            home + 'Tests/HomeNativeActionPolicyTests.cs',
            home + 'Tests/HomePermissionPromptDisplayTests.cs',
            home + 'Tests/HomeCoreApiSnapshotFenceTests.cs',
        ]
    elif suite == 'files-desktop':
        assemblies.update({
            'HavenOS.Home.NativeUI': [home + 'NativeUI/Controls/HomeApprovalCuiSurface.cs'],
            'Haven.Infrastructure': [shared + 'src/Haven.Infrastructure/DI/ServiceCollectionExtensions.cs'],
            'Haven.Desktop': [
                shared + 'src/Haven.Desktop/App.axaml.cs',
                shared + 'src/Haven.Desktop/Views/Pages/Home/HomePage.ApprovalNavigation.cs',
                shared + 'src/Haven.Desktop/Services/NativeHomeApprovalPromptPresenter.cs',
            ],
            'Haven.Desktop.Tests': [
                shared + 'tests/Haven.Desktop.Tests/HomeApprovalCuiSurfaceTests.cs',
                shared + 'tests/Haven.Desktop.Tests/HomePromptHostCompositionTests.cs',
            ],
        })
    else:
        raise ValueError('Unexpected Home source-proof suite')
    spec = importlib.util.spec_from_file_location('home_portable_pdb', root / '.github/scripts/astra-home-portable-pdb.py')
    parser = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(parser)
    retained = pathlib.Path(out) / 'home-source-pairs' / suite
    retained.mkdir(parents=True, exist_ok=False)
    records = []
    for assembly, paths in assemblies.items():
        dll = target.parent / (assembly + '.dll')
        pdb = target.parent / (assembly + '.pdb')
        if any(not file.is_file() or file.is_symlink() for file in (dll, pdb)):
            raise ValueError('Missing actual compiled Home pair: ' + assembly)
        dll_bytes, pdb_bytes = dll.read_bytes(), pdb.read_bytes()
        pair = parser.assert_actual_pair(dll_bytes, pdb_bytes)
        documents = parser.pdb_documents(pdb_bytes)
        source_records = []
        for path in paths:
            source = root / path
            if path not in cut_paths or digest(source) != cut_paths[path]:
                raise ValueError('Reviewed Home source is not pinned: ' + path)
            matches = [(name, row) for name, row in documents.items()
                       if name.replace('\\', '/').endswith('/' + path)]
            if len(matches) != 1:
                raise ValueError('Missing/ambiguous complete Home PDB document: ' + path)
            name, row = matches[0]
            if hashlib.new(row['hashName'], source.read_bytes()).hexdigest() != row['digest']:
                raise ValueError('Actual compiled Home source differs: ' + path)
            source_records.append({'path': path, 'sourceSha256': cut_paths[path], 'document': name, **row})
        for file in (dll, pdb):
            destination = retained / file.name
            shutil.copyfile(file, destination)
            if digest(destination) != digest(file):
                raise ValueError('Retained original Home pair differs')
        records.append({'assembly': assembly, 'dllSha256': digest(dll), 'pdbSha256': digest(pdb),
                        'identity': pair, 'sources': source_records})
    (retained / 'receipt.json').write_text(json.dumps({
        'status': 'ACTUAL_COMPLETE_REVIEWED_SOURCE_DOCUMENTS_MATCH_COMPILED_PAIRS',
        'suite': suite, 'pairs': records,
        'qualification': 'Compiled source identity only. Actual whole cohort, native resource, display and normal-host behavior require their separate original runtime outcomes.'
    }, indent=2) + '\n')
