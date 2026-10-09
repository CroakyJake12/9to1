from pathlib import Path
import gzip,hashlib,json,zipfile
LOGICAL=Path('/tmp/astra-framework-checkpoint24-fixture-only-owning-compiler-20261006-01/CHECKPOINT24-FIXTURE-OWNING-COMPILER-RECEIPT01.json.gz')
ZIP=Path('/tmp/astra-safety-net-checkpoint24-export19-20261006/astra-safety-net-source-checkpoint24-cumulative-export19.zip')
ZIP_SHA='d45b9cf3c103199397b043f4f1b8fa4d7867ecc13a8e7e29e7961fefb81e34bd'
MEMBER='evidence/CHECKPOINT24-FIXTURE-OWNING-COMPILER-RECEIPT01.json.gz'
WHOLE_BYTES=598073;WHOLE_SHA='078e81d6e56a511687704b2796f2b723463fe9af0c96c27c2f52a99ea5ae14cb'
DECODED_BYTES=3496192;DECODED_SHA='89a7cfed2dcb0d975834fef01a95b17b035057914d8f6562be5b3be4e57edce1'
def exact_c24_receipt(prefer_archive=False):
    if LOGICAL.is_file() and not prefer_archive:
        data=LOGICAL.read_bytes();transport={'kind':'present-original','path':str(LOGICAL)}
    else:
        assert hashlib.sha256(ZIP.read_bytes()).hexdigest()==ZIP_SHA
        with zipfile.ZipFile(ZIP) as archive:
            data=archive.read(MEMBER)
        transport={'kind':'exact-export-member','zipPath':str(ZIP),'zipSha256':ZIP_SHA,'member':MEMBER,
                   'originalLogicalPath':str(LOGICAL),'originalRawPathCurrentlyAvailable':LOGICAL.is_file()}
    assert len(data)==WHOLE_BYTES and hashlib.sha256(data).hexdigest()==WHOLE_SHA
    decoded=gzip.decompress(data)
    assert len(decoded)==DECODED_BYTES and hashlib.sha256(decoded).hexdigest()==DECODED_SHA
    return json.loads(decoded),transport
if __name__=='__main__':
    ordinary,source=exact_c24_receipt();archived,custody=exact_c24_receipt(prefer_archive=True)
    assert archived==ordinary and archived['exitCode']==0 and archived['inputsUnchangedAtCompletion']
    print(json.dumps({'qualification':'READONLY_NO_SDK_NO_RETIREMENT','logicalPath':str(LOGICAL),
      'wholeBytes':WHOLE_BYTES,'wholeSha256':WHOLE_SHA,'decodedBytes':DECODED_BYTES,'decodedSha256':DECODED_SHA,
      'archiveReader':custody,'originalReceiptEquality':True,'outputRecords':len(archived['outputs']),
      'outputArchives':len(archived['outputArchives']),'sourceInputs':len(archived['inputs'])}))
