#!/usr/bin/env python3
"""Verify official artifacts and reassemble exact original sealed Web ZIP."""
import hashlib,json,stat,subprocess,zipfile
from pathlib import Path,PurePosixPath

SOURCE='df547b5c7492050dc2ad2bdd99e250358cf444fc'
META=Path('/workspace/team-c-resume-evidence/c5-b-pr6-receiving/global-web-df547-retained-success')
OUT=Path('/tmp/team-c-c5-web-df547-20261004')
REPO=Path('/workspace/team-c-integration')
sha=lambda b:hashlib.sha256(b).hexdigest()
rows=[('part01',11308169581,Path('/workspace/attachments/64bf0e65-05ba-47e7-978c-2ceb0167a20e/team-c-df547-part01.zip')),
      ('part02',11308139722,Path('/workspace/attachments/4a24a41d-bf0f-4133-9650-232d3b34f920/team-c-df547-part02.zip')),
      ('publisher',11308889500,Path('/workspace/attachments/e8a5dcbb-80d8-441d-8d66-6727b4704732/team-c-df547-publisher.zip')),
      ('browser',11309450284,Path('/workspace/attachments/fd4ee934-6e10-4bc9-ae17-1a5c7ea8c750/team-c-df547-browser.zip'))]
official={r['id']:r for r in json.loads((META/'artifacts.json').read_text())['artifacts']}
assert not OUT.exists();OUT.mkdir()
custody=[]
for kind,artifact_id,path in rows:
    artifact=official[artifact_id]
    assert artifact['workflow_run']['id']==37217686116 and artifact['workflow_run']['head_sha']==SOURCE
    assert path.stat().st_size==artifact['size_in_bytes'] and sha(path.read_bytes())==artifact['digest'].removeprefix('sha256:')
    dest=OUT/(('transport/'+kind) if kind.startswith('part') else kind);dest.mkdir(parents=True)
    with zipfile.ZipFile(path) as archive:
        infos=archive.infolist();assert len({r.filename for r in infos})==len(infos) and archive.testzip() is None
        assert sum(r.file_size for r in infos)<128000000
        members=[]
        for info in infos:
            name=PurePosixPath(info.filename)
            assert not name.is_absolute() and '..' not in name.parts and '\\' not in info.filename
            assert not stat.S_ISLNK(info.external_attr>>16) and not info.flag_bits&1
            target=dest/info.filename;assert target.resolve().is_relative_to(dest.resolve())
            body=archive.read(info);target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(body)
            members.append({'path':info.filename,'bytes':len(body),'sha256':sha(body)})
    custody.append({'kind':kind,'artifactId':artifact_id,'archiveBytes':path.stat().st_size,'archiveSha256':sha(path.read_bytes()),
        'originalArchiveLocalPath':str(path),'members':members})
part01=OUT/'transport/part01';part02=OUT/'transport/part02'
for name in ['parts-manifest.json','publish-manifest.json','seal.json']:
    assert (part01/name).read_bytes()==(part02/name).read_bytes()
index=json.loads((part01/'parts-manifest.json').read_text())
manifest_bytes=(part01/'publish-manifest.json').read_bytes();seal_bytes=(part01/'seal.json').read_bytes()
manifest=json.loads(manifest_bytes);seal=json.loads(seal_bytes)
assert index['sourceCommit']==manifest['sourceCommit']==manifest['sourceCommitAfter']==seal['sourceCommit']==SOURCE
assert manifest['exitCode']==0 and manifest['addedSourceInputs']==manifest['removedSourceInputs']==manifest['changedSourceInputs']==manifest['trackedChangesAfter']==[]
assert sha(manifest_bytes)==index['originalReceiptSha256']==seal['receiptSha256']
assert sha(seal_bytes)==index['originalSealSha256']
assert index['partBytesLimit']==19*1024*1024 and [r['part'] for r in index['parts']]==[1,2]
publication=OUT/'original-publication';publication.mkdir()
(publication/'publish-manifest.json').write_bytes(manifest_bytes);(publication/'seal.json').write_bytes(seal_bytes)
with (publication/'wwwroot.zip').open('wb') as target:
    for part in index['parts']:
        path=OUT/'transport'/part['path'];body=path.read_bytes()
        assert len(body)==part['bytes']<=index['partBytesLimit'] and sha(body)==part['sha256']
        target.write(body)
whole=(publication/'wwwroot.zip').read_bytes()
assert len(whole)==index['archiveBytes']==seal['zipBytes'] and sha(whole)==index['archiveSha256']==seal['zipSha256']
with zipfile.ZipFile(publication/'wwwroot.zip') as archive:
    infos=archive.infolist();assert len({r.filename for r in infos})==len(infos)==manifest['fileCount']==285
    assert archive.testzip() is None
    expected={r['path']:r for r in manifest['publishFiles']}
    assert set(expected)=={r.filename for r in infos}
    for info in infos:
        name=PurePosixPath(info.filename);assert not name.is_absolute() and '..' not in name.parts and not stat.S_ISLNK(info.external_attr>>16)
        body=archive.read(info);row=expected[info.filename];assert len(body)==row['bytes'] and sha(body)==row['sha256']
    assert sum(i.file_size for i in infos)==manifest['totalBytes']==seal['totalBytes']
before_bytes=(OUT/'publisher/source-before.json').read_bytes()
assert before_bytes==(OUT/'publisher/source-after.json').read_bytes()==(OUT/'publisher/source-after-final.json').read_bytes()
sources=json.loads(before_bytes);assert len(sources)==manifest['sourceInputs']==manifest['sourceInputsAfter']==4548
batch=subprocess.check_output(['git','cat-file','--batch'],cwd=REPO,input=('\n'.join(SOURCE+':'+r['path'] for r in sources)+'\n').encode());pos=0
for row in sources:
    end=batch.index(b'\n',pos);blob,kind,size=batch[pos:end].decode().split();pos=end+1;size=int(size);body=batch[pos:pos+size];pos+=size
    assert batch[pos:pos+1]==b'\n';pos+=1
    assert kind=='blob' and blob==row['gitBlob'] and size==row['bytes'] and sha(body)==row['sha256']
assert pos==len(batch)
producer=json.loads((OUT/'publisher/result.json').read_text());assert producer['status']=='PASS' and producer['sourceCommit']==SOURCE and producer['seal']==seal
raw=json.loads((OUT/'browser/raw/results.json').read_text());browser=json.loads((OUT/'browser/diagnostics/result.json').read_text())
counts={'discovered':9,'executed':9,'passed':9,'failed':0,'notRun':0}
assert raw['counts']==browser['counts']==counts and raw['exit']==0 and raw['integrity']['state']=='PASS'
assert raw['candidate']['sourceCommit']==SOURCE and raw['candidate']['manifestSHA256']==sha(manifest_bytes)
assert raw['candidate']['sourceBeforeSHA256']==raw['candidate']['sourceAfterSHA256']==sha(before_bytes)
assert raw['bundleInventoryBefore']==raw['bundleInventoryAfter']
assert {r['path']:(r['bytes'],r['sha256']) for r in raw['bundleInventoryBefore']}=={r['path']:(r['bytes'],r['sha256']) for r in manifest['publishFiles']}
assert len(raw['launches'])==len(raw['closedLaunches'])==2 and len({r['browserPID'] for r in raw['launches']})==2
assert all(r['contextCloseResolved'] for r in raw['closedLaunches']) and raw['portRebind'] and sum(raw['diagnostics']['counts'].values())==0
families=[]
for folder in [OUT/'publisher',OUT/'browser/diagnostics']:
    for command in json.loads((folder/'commands.json').read_text()):
        assert command['exit']==command['exitAfterDrain']==0 and command['normalEOF'] and command['familyClosed'] and command['finalECHILD']
        assert not command['signals'] and command['error'] is None and all(b['gone'] for b in command['births'])
        families.append(command)
receipt={'state':'ACK_EXACT_CURRENT_SOURCE_PUBLICATION_TRANSPORT_AND_BROWSER_NINE','sourceCommit':SOURCE,'runId':37217686116,
    'officialWholeArtifactRetained':official[11308274250],
    'wholeArtifactDownloadQualification':'Official whole three-file artifact retained on GitHub;36MBcontainer exceeds32MiB file download cap. Original manifest/seal and exact original ZIP read back through both reviewed <=19MiB parts with equal metadata and ordered whole checksum.',
    'artifactCustody':custody,'originalManifestPath':str(publication/'publish-manifest.json'),'originalManifestBytes':len(manifest_bytes),'originalManifestSha256':sha(manifest_bytes),
    'originalSealPath':str(publication/'seal.json'),'originalSealBytes':len(seal_bytes),'originalSealSha256':sha(seal_bytes),
    'originalZipPath':str(publication/'wwwroot.zip'),'originalZipBytes':len(whole),'originalZipSha256':sha(whole),
    'sourceBeforePath':str(OUT/'publisher/source-before.json'),'sourceAfterPath':str(OUT/'publisher/source-after.json'),
    'sourceCatalogueBytes':len(before_bytes),'sourceCatalogueSha256':sha(before_bytes),'sourceInputs':len(sources),'allActualGitBodiesVerified':True,
    'publishFileCount':285,'publishBytes':manifest['totalBytes'],'allZipMembersAndManifestHashesVerified':True,
    'browserCounts':counts,'commandFamilies':len(families),'allNormalEOF_ECHILD_FamilyClosed_NoSignals':True,
    'qualifications':['Exact df547 source producer identity retained; not relabeled as later global Motion27dd.','Original7ded success and ce50 failure preserved separately.','No publishRoot rewriting or consumer rebinding here; original manifest/seal/ZIP bytes retained.','Nine local Picture browser groups and source publication only; no provider/deploy/full parity release acceptance.']}
(META/'verification.json').write_text(json.dumps(receipt,indent=2)+'\n')
print(json.dumps({k:receipt[k] for k in ['state','sourceCommit','originalManifestPath','originalManifestSha256','originalSealPath','originalSealSha256','originalZipPath','originalZipBytes','originalZipSha256','sourceBeforePath','sourceAfterPath','sourceCatalogueSha256','sourceInputs','browserCounts','commandFamilies']},indent=2))
