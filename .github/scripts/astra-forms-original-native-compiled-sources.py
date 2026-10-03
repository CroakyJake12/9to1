"""Actual original native Desktop/Home source identity; never a UI/auth result."""
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sys

sys.dont_write_bytecode = True

def verify(root, out, target, cut_paths, digest):
    root, out, target = Path(root), Path(out), Path(target)
    home = '9to1 Workspace/Home/'
    shared = '9to1 Workspace/shared/'
    assemblies = {'HavenOS.Home': ['9to1 Workspace/Home/Source/Home/HomePermissionTrustService.cs', '9to1 Workspace/Home/Source/Home/Core/HomeAppAiServices.cs', '9to1 Workspace/Home/Source/Home/Core/HomeApprovalPromptFlow.cs', '9to1 Workspace/Home/Source/Home/Core/HomeCoreApi.cs'], 'HavenOS.Home.NativeUI': ['9to1 Workspace/Home/NativeUI/Controls/HomeApprovalCuiSurface.cs'], 'Haven.Infrastructure': ['9to1 Workspace/shared/src/Haven.Infrastructure/DI/ServiceCollectionExtensions.cs'], 'Haven.Desktop': ['9to1 Workspace/shared/src/Haven.Desktop/App.axaml.cs', '9to1 Workspace/shared/src/Haven.Desktop/Program.cs', '9to1 Workspace/shared/src/Haven.Desktop/MainWindow.axaml.cs', '9to1 Workspace/shared/src/Haven.Desktop/Interface/Shell/MainView.axaml.cs', '9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Home/HomePage.ApprovalNavigation.cs', '9to1 Workspace/shared/src/Haven.Desktop/Services/NativeHomeApprovalPromptPresenter.cs', '9to1 Workspace/shared/src/Haven.Desktop/Diagnostics/FormsMathematicsNativeActivationProbe.cs', '9to1 Workspace/shared/src/Haven.Desktop/Diagnostics/NativeProbeProfileWitness.cs', '9to1 Workspace/shared/src/Haven.Desktop/Diagnostics/NativeProbeOriginalDesktopShutdown.cs', '9to1 Workspace/shared/src/Haven.Desktop/Interface/Shell/MainView.Forms.cs', '9to1 Workspace/shared/src/Haven.Desktop/Mathematics/CSharpMathSyntaxAdapter.cs', '9to1 Workspace/shared/src/Haven.Desktop/Mathematics/FormMathematicsAuthoringControl.cs', '9to1 Workspace/shared/src/Haven.Desktop/Mathematics/FormNativeMathematicsProvider.cs', '9to1 Workspace/shared/src/Haven.Desktop/Mathematics/NativeMathInputObservation.cs', '9to1 Workspace/shared/src/Haven.Desktop/Mathematics/NumericMathAnswerEditorControl.cs', '9to1 Workspace/shared/src/Haven.Desktop/Mathematics/ScottPlotGraphAdapter.cs', '9to1 Workspace/shared/src/Haven.Desktop/Mathematics/SharedGraphEditorControl.cs', '9to1 Workspace/shared/src/Haven.Desktop/Mathematics/SharedMathEditorControl.cs', '9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Forms/FormsNativeWorkspaceHost.cs'], 'Haven.Core': ['9to1 Workspace/shared/src/Haven.Core/Forms/FormMathematics.cs', '9to1 Workspace/shared/src/Haven.Core/Forms/FormProject.cs', '9to1 Workspace/shared/src/Haven.Core/Forms/FormResponseRuntime.cs', '9to1 Workspace/shared/src/Haven.Core/Mathematics/GraphDefinition.cs', '9to1 Workspace/shared/src/Haven.Core/Mathematics/GraphMarking.cs', '9to1 Workspace/shared/src/Haven.Core/Mathematics/GraphResponse.cs', '9to1 Workspace/shared/src/Haven.Core/Mathematics/GraphResponseProjection.cs', '9to1 Workspace/shared/src/Haven.Core/Mathematics/MathAnswer.cs', '9to1 Workspace/shared/src/Haven.Core/Mathematics/MathExpression.cs', '9to1 Workspace/shared/src/Haven.Core/Mathematics/MathMarking.cs', '9to1 Workspace/shared/src/Haven.Core/Mathematics/MathObjectCodec.cs'], 'Haven.Application': ['9to1 Workspace/shared/src/Haven.Application/Mathematics/GraphEditorSession.cs', '9to1 Workspace/shared/src/Haven.Application/Mathematics/GraphResponseEditorSession.cs', '9to1 Workspace/shared/src/Haven.Application/Mathematics/MathEditorSession.cs', '9to1 Workspace/shared/src/Haven.Application/Mathematics/MathSyntaxContracts.cs'], 'HavenOS.Forms': ['9to1 Workspace/Forms/CUI/FormNativeAnswerInput.cs', '9to1 Workspace/Forms/CUI/FormNativePreview.cs', '9to1 Workspace/Forms/CUI/FormNativePublicationValidator.cs', '9to1 Workspace/Forms/CUI/FormNativeResponseSurface.cs', '9to1 Workspace/Forms/CUI/FormsCuiWorkspace.cs', '9to1 Workspace/Forms/CUI/IFormNativeMathematicsProvider.cs']}
    spec = importlib.util.spec_from_file_location('home_native_actual_pdb', root / '.github/scripts/astra-home-portable-pdb.py')
    parser = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(parser)
    retained = out / 'forms-original-native-source-pairs'
    retained.mkdir(exist_ok=False)
    records = []
    for logical_assembly, paths in assemblies.items():
        physical_assembly = 'Haven' if logical_assembly == 'Haven.Desktop' else logical_assembly
        dll, pdb = [target.parent / (physical_assembly + suffix) for suffix in ('.dll', '.pdb')]
        if any(not file.is_file() or file.is_symlink() for file in (dll, pdb)):
            raise ValueError('Missing actual original native compiled pair: ' + logical_assembly)
        dll_bytes, pdb_bytes = dll.read_bytes(), pdb.read_bytes()
        identity = parser.assert_actual_pair(dll_bytes, pdb_bytes)
        documents = parser.pdb_documents(pdb_bytes)
        sources = []
        for name in paths:
            source = root / name
            if name not in cut_paths or not source.is_file() or source.is_symlink() or digest(source) != cut_paths[name]:
                raise ValueError('Reviewed original native source not pinned/regular: ' + name)
            matches = [(document, row) for document, row in documents.items()
                       if document.replace('\\', '/').endswith('/' + name)]
            if len(matches) != 1:
                raise ValueError('Missing/ambiguous complete original native PDB document: ' + name)
            document, row = matches[0]
            if hashlib.new(row['hashName'], source.read_bytes()).hexdigest() != row['digest']:
                raise ValueError('Actual compiled original native source differs: ' + name)
            sources.append({'path': name, 'sourceSha256': cut_paths[name], 'document': document, **row})
        for file in (dll, pdb):
            destination = retained / file.name
            shutil.copyfile(file, destination)
            if digest(destination) != digest(file):
                raise ValueError('Retained actual original native pair differs')
        records.append({'assembly': logical_assembly, 'physicalAssembly': physical_assembly,
                        'dllSha256': digest(dll), 'pdbSha256': digest(pdb), 'identity': identity, 'sources': sources})
    receipt = {'status': 'ACTUAL_COMPLETE_ORIGINAL_NATIVE_FORMS_HOME_MATH_SOURCE_DOCUMENTS_MATCH_COMPILED_PAIRS',
               'pairs': records,
               'qualification': 'Actual compiled source identity only; native display/pending/no-grant/original callback task and process/profile custody have separate mandatory outcomes.'}
    (retained / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
