"""Fixed independent Home437 Debug/Release evidence replay; no original payload is executed."""
import base64, hashlib, io, json, os, re, resource, stat, struct, sys, time, urllib.error, urllib.parse, urllib.request, zipfile, zlib
from pathlib import PurePosixPath

REPOSITORY = 'CroakyJake12/9to1'
HEAD = '910ecec8780bfdfad1b44495c9e4e38d0b00fd86'
RUN_ID = 37189893269
SOURCE_BRANCH = 'validation/astra-home437-original-owning-20261004'
CUSTODY_BRANCH = 'validation/astra-home437-original-custody-20261004'
HEX64 = re.compile(r'[0-9a-f]{64}\Z')
DOWNLOAD_HOST = re.compile(r'productionresultssa[0-9]{1,3}\.blob\.core\.windows\.net\Z')
SHA256_GUID = '8829d00f-11b8-4213-878b-770e8597ac16'

class Refusal(Exception):
    def __init__(self, code):
        self.code = code
        super().__init__(code)

def require(condition, code):
    if not condition:
        raise Refusal(code)

def add(errors, error):
    if all(error is not old for old in errors):
        errors.append(error)

def settle(errors):
    if len(errors) == 1:
        raise errors[0]
    if errors:
        raise BaseExceptionGroup('Original replay and independent custody failures.', errors)

def projections(errors):
    # Never stringify network/IO errors: signed URLs and credentials are not diagnostics.
    result = []
    for error in errors:
        row = {'type': type(error).__name__, 'code': error.code if isinstance(error, Refusal) else 'ORIGINAL_EXCEPTION_RETAINED'}
        if isinstance(error, BaseExceptionGroup):
            row['causes'] = projections(error.exceptions)
        result.append(row)
    return result

def digest(raw):
    return hashlib.sha256(raw).hexdigest()

def safe_path(value):
    require(type(value) is str and 0 < len(value) <= 4096, 'UNSAFE_PATH_LENGTH_OR_TYPE')
    require(value == str(PurePosixPath(value)) and not value.startswith('/') and not value.endswith('/'), 'UNSAFE_PATH_NORMALIZATION')
    require(all(part not in ('', '.', '..') for part in value.split('/')), 'UNSAFE_PATH_COMPONENT')
    require(not any(ord(char) < 32 or ord(char) == 127 or char in '\\:' for char in value), 'UNSAFE_PATH_CHARACTER')
    return value

def unique_json(raw):
    def pairs(rows):
        output = {}
        for key, value in rows:
            require(key not in output, 'DUPLICATE_JSON_MEMBER')
            output[key] = value
        return output
    def invalid_number(value):
        raise Refusal('NONFINITE_JSON_NUMBER')
    return json.loads(raw.decode('utf-8', errors='strict'), object_pairs_hook=pairs, parse_constant=invalid_number)

def exact_int(value, code):
    require(type(value) is int and value >= 0, code)
    return value

def exact_sha(value):
    require(type(value) is str and HEX64.fullmatch(value) is not None, 'INVALID_SHA256_SYNTAX')
    return value

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, newurl):
        return None

def api_url(suffix):
    require(type(suffix) is str and suffix.startswith('/') and '?' not in suffix and '#' not in suffix, 'INVALID_FIXED_API_SUFFIX')
    return 'https://api.github.com/repos/' + REPOSITORY + suffix

def validate_download_url(value):
    require(type(value) is str and len(value) <= 16384 and not any(ord(c) < 32 for c in value), 'DOWNLOAD_URL_TYPE_OR_LENGTH')
    parsed = urllib.parse.urlsplit(value)
    require(parsed.scheme == 'https' and parsed.username is None and parsed.password is None and parsed.fragment == '', 'DOWNLOAD_URL_NOT_TRUSTED_HTTPS')
    require(parsed.port in (None, 443) and DOWNLOAD_HOST.fullmatch(parsed.hostname or '') is not None, 'DOWNLOAD_HOST_OUTSIDE_FIXED_ALLOWLIST')
    require(parsed.path.startswith('/actions-results/') and parsed.query != '', 'DOWNLOAD_PATH_OUTSIDE_FIXED_ALLOWLIST')
    return value

def bounded_host_text(path, cap):
    descriptor = None
    errors = []
    result = None
    try:
        descriptor = os.open(path, os.O_RDONLY | os.O_CLOEXEC | os.O_NOFOLLOW)
        raw = bytearray()
        while True:
            part = os.read(descriptor, min(4096, cap - len(raw) + 1))
            if not part:
                break
            raw.extend(part)
            require(len(raw) <= cap, 'HOST_RESOURCE_OBSERVATION_CAP_REFUSED')
        result = bytes(raw).decode('ascii', errors='strict')
    except BaseException as error:
        add(errors, error)
    if descriptor is not None:
        try:
            os.close(descriptor)
        except BaseException as error:
            add(errors, error)
    settle(errors)
    return result

def available_memory():
    rows = {}
    for line in bounded_host_text('/proc/meminfo', 16384).splitlines():
        key, value = line.split(':', 1)
        if key == 'MemAvailable':
            rows[key] = int(value.split()[0]) * 1024
    require('MemAvailable' in rows, 'MEMORY_AVAILABLE_UNOBSERVED')
    # A hosted cgroup may be narrower than /proc/meminfo.
    cap_path = '/sys/fs/cgroup/memory.max'
    used_path = '/sys/fs/cgroup/memory.current'
    if os.path.isfile(cap_path) and os.path.isfile(used_path):
        maximum = bounded_host_text(cap_path, 64).strip()
        current = int(bounded_host_text(used_path, 64).strip())
        if maximum != 'max':
            rows['MemAvailable'] = min(rows['MemAvailable'], max(0, int(maximum) - current))
    return rows['MemAvailable']

class Ledger:
    def __init__(self, config, output_parent):
        self.limits = config['limits']
        self.output_parent = output_parent
        self.attempts = {'archiveBytes': 0, 'expandedBytes': 0, 'apiRequests': 0, 'outputBytes': 0, 'outputAllocatedBytes': 0, 'outputFiles': 0}
        self.timeline = []
        self.sequence = 0
        self.deadline = time.monotonic() + 900
    def phase(self, label):
        self.sequence += 1
        self.timeline.append({'sequence': self.sequence, 'phase': label, 'monotonicNs': time.monotonic_ns(), 'attempts': dict(self.attempts)})
    def guard(self):
        require(time.monotonic() <= self.deadline, 'REPLAY_WALL_DEADLINE_REFUSED')
        require(available_memory() >= self.limits['ramAvailableFloorBytes'], 'MEMORY_FLOOR_REFUSED')
        filesystem = os.fstatvfs(self.output_parent)
        remaining = self.limits['outputAllocationReservationBytes'] - self.attempts['outputAllocatedBytes']
        require(filesystem.f_bavail * filesystem.f_frsize >= self.limits['filesystemFreeFloorBytes'] + max(0, remaining), 'FILESYSTEM_FIXED_FLOOR_REFUSED')
        require(resource.getrusage(resource.RUSAGE_SELF).ru_maxrss * 1024 <= self.limits['ramWorkingSetBytes'], 'RAM_WORKING_SET_REFUSED')
    def charge(self, key, amount, cap):
        exact_int(amount, 'NEGATIVE_ATTEMPT_CHARGE')
        require(self.attempts[key] + amount <= cap, 'ATTEMPT_BUDGET_REFUSED_' + key)
        # The reservation precedes IO/decompression/creation and is never refunded.
        self.attempts[key] += amount
        self.guard()
    def api(self):
        self.charge('apiRequests', 1, self.limits['apiAttempts'])
    def archive(self, amount):
        self.charge('archiveBytes', amount, self.limits['archiveAttemptBytes'])
    def expanded(self, amount):
        self.charge('expandedBytes', amount, self.limits['expandedAttemptBytes'])
    def output(self, amount, *, reserved=0):
        allocated = ((amount + 4095) // 4096) * 4096
        require(self.attempts['outputBytes'] + amount <= self.limits['outputAttemptBytes'], 'OUTPUT_BYTES_CAP_REFUSED')
        require(self.attempts['outputAllocatedBytes'] + allocated + reserved <= self.limits['outputAllocationReservationBytes'], 'OUTPUT_ALLOCATION_CAP_REFUSED')
        require(self.attempts['outputFiles'] + 1 <= self.limits['maxOutputFiles'], 'OUTPUT_FILE_SLOTS_REFUSED')
        self.attempts['outputBytes'] += amount
        self.attempts['outputAllocatedBytes'] += allocated
        self.attempts['outputFiles'] += 1
        self.guard()

def finish_handle(handle, errors):
    if handle is not None:
        try:
            handle.close()
        except BaseException as error:
            add(errors, error)

def read_response(response, amount, ledger, *, archive=False):
    errors = []
    raw = None
    try:
        require(response.headers.get('Content-Encoding', 'identity').lower() == 'identity', 'HTTP_CONTENT_ENCODING_REFUSED')
        length = response.headers.get('Content-Length')
        if archive:
            require(length is not None and int(length) == amount, 'OUTER_HTTP_LENGTH_MISMATCH')
        elif length is not None:
            require(int(length) <= amount, 'API_HTTP_LENGTH_CAP_REFUSED')
        pieces = []
        seen = 0
        while True:
            ledger.guard()
            piece = response.read(min(65536, amount - seen + 1))
            if not piece:
                break
            seen += len(piece)
            require(seen <= amount, 'HTTP_RUNNING_LENGTH_CAP_REFUSED')
            pieces.append(piece)
        require(not archive or seen == amount, 'OUTER_HTTP_COMPLETE_LENGTH_MISMATCH')
        raw = b''.join(pieces)
    except BaseException as error:
        add(errors, error)
    finish_handle(response, errors)
    settle(errors)
    return raw

class GitHubReader:
    def __init__(self, ledger, token):
        require(type(token) is str and token and '\r' not in token and '\n' not in token, 'API_TOKEN_UNAVAILABLE')
        self.ledger = ledger
        self.token = token
        self.opener = urllib.request.build_opener(NoRedirect())
    def request(self, suffix, accept):
        self.ledger.api()
        request = urllib.request.Request(api_url(suffix), headers={'Authorization': 'Bearer ' + self.token, 'Accept': accept, 'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'sol-happy-sdk07-original-custody', 'Accept-Encoding': 'identity'})
        return request
    def json(self, suffix):
        response = None
        errors = []
        raw = None
        try:
            response = self.opener.open(self.request(suffix, 'application/vnd.github+json'), timeout=60)
            require(response.status == 200, 'API_STATUS_REFUSED')
            held = response
            response = None
            raw = read_response(held, self.ledger.limits['apiBodyBytes'], self.ledger)
        except BaseException as error:
            if response is None and isinstance(error, urllib.error.HTTPError):
                response = error
            add(errors, error)
        finish_handle(response, errors)
        settle(errors)
        return unique_json(raw)
    def artifact(self, row):
        meta = self.json('/actions/artifacts/' + str(row['id']))
        require(meta['id'] == row['id'] and meta['name'] == row['name'] and meta['size_in_bytes'] == row['bytes'] and meta['expired'] is False, 'ARTIFACT_METADATA_IDENTITY_MISMATCH')
        require(meta.get('digest') == 'sha256:' + row['sha256'], 'ARTIFACT_API_DIGEST_MISMATCH')
        original_run = meta['workflow_run']
        require(original_run['id'] == RUN_ID and original_run['head_sha'] == HEAD and original_run['head_branch'] == SOURCE_BRANCH, 'ARTIFACT_FOREIGN_ORIGINAL_RUN')
        self.ledger.archive(row['bytes'])
        response = None
        errors = []
        download = None
        try:
            try:
                response = self.opener.open(self.request('/actions/artifacts/' + str(row['id']) + '/zip', 'application/vnd.github+json'), timeout=60)
                raise Refusal('ARTIFACT_DOWNLOAD_EXPECTED_EXPLICIT_302')
            except urllib.error.HTTPError as redirect:
                response = redirect
                require(redirect.code == 302, 'ARTIFACT_REDIRECT_STATUS_REFUSED')
                download = validate_download_url(redirect.headers.get('Location'))
        except BaseException as error:
            add(errors, error)
        finish_handle(response, errors)
        settle(errors)
        response = None
        raw = None
        errors = []
        try:
            # New request, no Authorization/cookie header and no redirect handler.
            request = urllib.request.Request(download, headers={'Accept-Encoding': 'identity', 'User-Agent': 'sol-happy-sdk07-original-custody'})
            require('Authorization' not in request.headers and 'Cookie' not in request.headers, 'TOKEN_FORWARDED_TO_DOWNLOAD_REFUSED')
            response = self.opener.open(request, timeout=60)
            require(response.status == 200, 'SIGNED_DOWNLOAD_STATUS_REFUSED')
            held = response
            response = None
            raw = read_response(held, row['bytes'], self.ledger, archive=True)
            require(digest(raw) == row['sha256'], 'OUTER_ZIP_SHA256_MISMATCH')
        except BaseException as error:
            if response is None and isinstance(error, urllib.error.HTTPError):
                response = error
            add(errors, error)
        finish_handle(response, errors)
        settle(errors)
        return raw, {'id': row['id'], 'name': row['name'], 'bytes': row['bytes'], 'sha256': row['sha256'], 'originalRunId': RUN_ID, 'originalHead': HEAD, 'metadataDigest': meta['digest'], 'redirectStatus': 302, 'authorizationForwarded': False}

def safe_zip_info(info, expected_name, expected_size):
    require(info.filename == expected_name and safe_path(info.filename) == info.filename, 'ZIP_MEMBER_PATH_SET_MISMATCH')
    require(not info.is_dir() and not info.flag_bits & 1, 'ZIP_DIRECTORY_OR_ENCRYPTION_REFUSED')
    require(stat.S_IFMT(info.external_attr >> 16) in (0, stat.S_IFREG) and not info.external_attr & 0x10, 'ZIP_MEMBER_TYPE_REFUSED')
    require(info.file_size == expected_size and info.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED), 'ZIP_MEMBER_SIZE_OR_METHOD_MISMATCH')
    require(info.compress_size >= 0, 'ZIP_COMPRESSED_LENGTH_REFUSED')
    return {'name': info.filename, 'bytes': info.file_size, 'compressedBytes': info.compress_size, 'crc32': format(info.CRC, '08x'), 'externalAttributes': info.external_attr, 'createSystem': info.create_system, 'dateTime': list(info.date_time), 'compression': info.compress_type}

def zip_read(archive, info, expected_size, ledger):
    ledger.expanded(expected_size)
    handle = None
    errors = []
    result = None
    try:
        handle = archive.open(info, 'r')
        pieces = []
        total = 0
        while True:
            ledger.guard()
            part = handle.read(min(65536, expected_size - total + 1))
            if not part:
                break
            total += len(part)
            require(total <= expected_size, 'ZIP_MEMBER_RUNNING_LENGTH_REFUSED')
            pieces.append(part)
        require(total == expected_size, 'ZIP_MEMBER_COMPLETE_LENGTH_MISMATCH')
        result = b''.join(pieces)
        require((zlib.crc32(result) & 0xffffffff) == info.CRC, 'ZIP_CRC_MISMATCH')
    except BaseException as error:
        add(errors, error)
    finish_handle(handle, errors)
    settle(errors)
    return result

def outer_member(raw, row, ledger):
    archive = None
    errors = []
    result = None
    receipt = None
    try:
        archive = zipfile.ZipFile(io.BytesIO(raw), 'r')
        require(len(archive.infolist()) == 1, 'OUTER_ZIP_MEMBER_COUNT_MISMATCH')
        info = archive.infolist()[0]
        receipt = safe_zip_info(info, row['member'], row['innerBytes'])
        result = zip_read(archive, info, row['innerBytes'], ledger)
        require(digest(result) == row['innerSha256'], 'OUTER_MEMBER_SHA256_MISMATCH')
    except BaseException as error:
        add(errors, error)
    finish_handle(archive, errors)
    settle(errors)
    return result, receipt

def validate_index(raw, lane, config):
    manifest = unique_json(raw)
    require(manifest['commit'] == HEAD and str(manifest['runId']) == str(RUN_ID), 'INDEX_ORIGINAL_IDENTITY_MISMATCH')
    require(manifest['partCount'] == 1 and manifest['maxPayloadBytes'] == 27262976, 'INDEX_PART_OR_PAYLOAD_CAP_MISMATCH')
    counts = config['expectedCounts'][lane]
    require(len(manifest['files']) == counts['files'] and len(manifest['bundles']) == 1, 'INDEX_FILE_OR_BUNDLE_COUNT_MISMATCH')
    files = {}
    pieces = {}
    nonempty = 0
    total = 0
    payload = {0: 0}
    for file in manifest['files']:
        require(type(file) is dict and set(file) == {'path', 'bytes', 'sha256', 'pieces'} and type(file['pieces']) is list, 'INDEX_FULL_FILE_SCHEMA_MISMATCH')
        path = safe_path(file['path'])
        require(path not in files, 'DUPLICATE_LANE_FILE_PATH')
        size = exact_int(file['bytes'], 'INDEX_FILE_LENGTH_INVALID')
        exact_sha(file['sha256'])
        offset = 0
        for piece in file['pieces']:
            require(type(piece) is dict and set(piece) == {'artifactPart', 'piece', 'path', 'offset', 'bytes', 'sha256'} and type(piece['offset']) is int, 'INDEX_PIECE_SCHEMA_MISMATCH')
            part = exact_int(piece['artifactPart'], 'INDEX_PART_INVALID')
            require(part in payload and piece['path'] == path and piece['offset'] == offset, 'INDEX_PIECE_PATH_OFFSET_OR_PART_MISMATCH')
            name = safe_path(piece['piece'])
            require(name == 'piece-' + format(len(pieces), '06d') + '.part' and name not in pieces, 'INDEX_GLOBAL_PIECE_SEQUENCE_MISMATCH')
            amount = exact_int(piece['bytes'], 'INDEX_PIECE_LENGTH_INVALID')
            require(amount > 0 and amount <= manifest['maxPayloadBytes'], 'INDEX_PIECE_LENGTH_REFUSED')
            exact_sha(piece['sha256'])
            offset += amount
            payload[part] += amount
            pieces[name] = piece
        if len(file['pieces']) == 1:
            require(file['pieces'][0]['sha256'] == file['sha256'], 'INDEX_SINGLE_PIECE_FULL_HASH_MISMATCH')
        require(offset == size and (size != 0 or not file['pieces']), 'INDEX_FULL_FILE_COVERAGE_MISMATCH')
        require(size != 0 or file['sha256'] == digest(b''), 'INDEX_EMPTY_FILE_SHA_MISMATCH')
        total += size
        nonempty += int(size > 0)
        files[path] = file
    require(len(pieces) == counts['pieces'] and nonempty == counts['nonempty'] and total == counts['bytes'], 'INDEX_AGGREGATE_COUNT_MISMATCH')
    require(all(amount <= manifest['maxPayloadBytes'] for amount in payload.values()), 'INDEX_DECLARED_PART_PAYLOAD_REFUSED')
    for number, bundle in enumerate(manifest['bundles']):
        require(type(bundle) is dict and set(bundle) == {'artifactPart', 'file', 'bytes', 'sha256'} and type(bundle['artifactPart']) is int and bundle['artifactPart'] == number, 'INDEX_BUNDLE_SCHEMA_OR_SEQUENCE_MISMATCH')
        selected = next(row for row in config['artifacts'] if row['lane'] == lane and row['role'] == 'part' and row['part'] == bundle['artifactPart'])
        require(bundle['file'] == selected['member'] and bundle['bytes'] == selected['innerBytes'] and bundle['sha256'] == selected['innerSha256'], 'INDEX_BUNDLE_DESCRIPTOR_MISMATCH')
    require(set(manifest) == {'commit', 'runId', 'status', 'files', 'partCount', 'maxPayloadBytes', 'qualification', 'bundles'} and manifest['status'] == 'INDEPENDENT PARTS NO CROSSARTIFACT ATOMICITY' and type(manifest['qualification']) is str and manifest['qualification'], 'INDEX_ORIGINAL_SCHEMA_OR_QUALIFICATION_MISMATCH')
    require(all(path in files for path in config['required'][lane]), 'REQUIRED_CAUSAL_FILE_ABSENT_FROM_INDEX')
    return manifest, files, pieces

def selected_text(path, config):
    suffix = PurePosixPath(path).suffix.lower()
    if suffix in config['selection']['binarySuffixesNotPublished']:
        return False
    return suffix in config['selection']['wholeSuffixes'] or any(path.startswith(prefix) for prefix in config['selection']['wholePrefixes'])

def pe_entries(data):
    require(data[:2] == b'MZ' and len(data) >= 64, 'TASK_PE_HEADER_REFUSED')
    pe = struct.unpack_from('<I', data, 60)[0]
    require(data[pe:pe + 4] == b'PE\0\0', 'TASK_PE_SIGNATURE_REFUSED')
    count = struct.unpack_from('<H', data, pe + 6)[0]
    size = struct.unpack_from('<H', data, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from('<H', data, opt)[0]
    require(magic in (0x10b, 0x20b) and 0 < count <= 96, 'TASK_PE_OPTIONAL_HEADER_REFUSED')
    directory = opt + (96 if magic == 0x10b else 112)
    require(directory + 56 <= opt + size and opt + size + count * 40 <= len(data), 'TASK_PE_DEBUG_DIRECTORY_HEADER_BOUNDS')
    rva, amount = struct.unpack_from('<II', data, directory + 48)
    require(rva > 0 and amount > 0 and amount % 28 == 0 and amount <= 28 * 32, 'TASK_PE_DEBUG_DIRECTORY_SIZE_REFUSED')
    start = None
    for index in range(count):
        virtual_size, address, raw_size, raw = struct.unpack_from('<IIII', data, opt + size + index * 40 + 8)
        if address <= rva < address + max(virtual_size, raw_size):
            require(rva - address + amount <= raw_size, 'TASK_PE_DEBUG_DIRECTORY_RAW_SECTION_BOUNDS')
            start = raw + rva - address
    require(start is not None and start + amount <= len(data), 'TASK_PE_DEBUG_DIRECTORY_PHYSICAL_BOUNDS')
    result = []
    for offset in range(start, start + amount, 28):
        _, stamp, major, minor, kind, length, address, pointer = struct.unpack_from('<IIHHIIII', data, offset)
        require(pointer + length <= len(data), 'TASK_PE_DEBUG_PAYLOAD_PHYSICAL_BOUNDS')
        result.append({'stamp': stamp, 'major': major, 'minor': minor, 'type': kind, 'bytes': length, 'pointer': pointer})
    return result

def physical_symbols(pe, external, config, ledger):
    entries = pe_entries(pe)
    embedded = [row for row in entries if row['type'] == 17]
    require(len(embedded) <= 1, 'TASK_MULTIPLE_EMBEDDED_PDBS_REFUSED')
    if embedded:
        row = embedded[0]
        raw = pe[row['pointer']:row['pointer'] + row['bytes']]
        require(row['major'] >= 0x0100 and row['minor'] == 0x0100 and len(raw) > 8 and raw[:4] == b'MPDB', 'TASK_MPDB_VERSION_OR_SIGNATURE_REFUSED')
        size = struct.unpack_from('<I', raw, 4)[0]
        require(0 < size <= config['limits']['pdbExpandedBytes'], 'TASK_MPDB_DECLARED_SIZE_REFUSED')
        ledger.expanded(size)
        decoder = zlib.decompressobj(-15)
        pdb = decoder.decompress(raw[8:], size + 1)
        require(len(pdb) == size and decoder.eof and not decoder.unused_data and not decoder.unconsumed_tail, 'TASK_MPDB_COMPLETE_RAW_DEFLATE_REFUSED')
        storage = 'embedded-portable-pdb'
        if external is not None:
            require(external == pdb, 'TASK_EMBEDDED_EXTERNAL_SYMBOLS_DISAGREE')
    else:
        require(external is not None and len(external) <= config['limits']['pdbExpandedBytes'], 'TASK_PHYSICAL_SYMBOLS_UNAVAILABLE')
        pdb = external
        storage = 'external-portable-pdb'
    identity = assert_actual_pair(pe, pdb)
    streams = metadata_streams(pdb)
    tables = streams['#~']
    require(len(tables) >= 24, 'TASK_PDB_TABLES_HEADER_REFUSED')
    valid = struct.unpack_from('<Q', tables, 8)[0]
    cursor = 24
    documents_count = 0
    for index in range(64):
        if valid >> index & 1:
            count = struct.unpack_from('<I', tables, cursor)[0]
            cursor += 4
            if index == 48:
                documents_count = count
    require(0 < documents_count <= 50000, 'TASK_PDB_DOCUMENT_COUNT_REFUSED')
    documents = pdb_documents(pdb)
    require(len(documents) == documents_count, 'TASK_PDB_DUPLICATE_DOCUMENT_PATH_REFUSED')
    return {'peBytes': len(pe), 'peSha256': digest(pe), 'physicalStorage': storage, 'portablePdbBytes': len(pdb), 'portablePdbSha256': digest(pdb), 'identity': identity, 'debugDirectory': entries, 'documentRows': documents_count, 'documents': documents, 'qualification': 'Actual original physical symbols and all Document rows only. No new build, source-file availability, evaluated Compile closure, generated-document persistence or native acceptance is inferred.'}

def directory_edges(path, callback):
    require(os.path.isabs(path) and path == os.path.normpath(path), 'OUTPUT_PARENT_NOT_DIRECT_ABSOLUTE')
    handles = []
    links = []
    errors = []
    returned = None
    def identity(value):
        return (value.st_dev, value.st_ino, value.st_uid, value.st_gid, stat.S_IMODE(value.st_mode))
    def verify():
        for parent, name, descriptor, witness in links:
            current = os.fstat(descriptor)
            present = os.stat(name, dir_fd=parent, follow_symlinks=False) if parent is not None else os.stat('/', follow_symlinks=False)
            require(stat.S_ISDIR(current.st_mode) and stat.S_ISDIR(present.st_mode) and identity(current) == witness == identity(present), 'OUTPUT_PARENT_HANDLE_OR_LINK_CHANGED')
    try:
        descriptor = os.open('/', os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC | os.O_NOFOLLOW)
        handles.append(descriptor)
        links.append((None, '/', descriptor, identity(os.fstat(descriptor))))
        for component in path.split('/')[1:]:
            if not component:
                continue
            previous = descriptor
            before = os.stat(component, dir_fd=previous, follow_symlinks=False)
            require(stat.S_ISDIR(before.st_mode), 'OUTPUT_PARENT_INDIRECT_DIRECTORY_REFUSED')
            descriptor = os.open(component, os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC | os.O_NOFOLLOW, dir_fd=previous)
            handles.append(descriptor)
            require(identity(os.fstat(descriptor)) == identity(before), 'OUTPUT_PARENT_ADMISSION_CHANGED')
            links.append((previous, component, descriptor, identity(before)))
        verify()
        returned = callback(descriptor, verify)
    except BaseException as error:
        add(errors, error)
    try:
        verify()
    except BaseException as error:
        add(errors, error)
    for handle in reversed(handles):
        try:
            os.close(handle)
        except BaseException as error:
            add(errors, error)
    settle(errors)
    return returned

class Publisher:
    def __init__(self, directory, ledger, verify):
        self.directory = directory
        self.ledger = ledger
        self.verify_root = verify
        self.directories = {'': directory}
        self.directory_links = []
        self.handles = []
        self.slots = {}
    def verify(self):
        self.verify_root()
        for parent, name, descriptor, original in self.directory_links:
            current = os.fstat(descriptor)
            present = os.stat(name, dir_fd=parent, follow_symlinks=False)
            require(stat.S_ISDIR(current.st_mode) and stat.S_ISDIR(present.st_mode) and current.st_dev == original.st_dev == present.st_dev and current.st_ino == original.st_ino == present.st_ino, 'OUTPUT_CHILD_DIRECTORY_LINK_CHANGED')
    def parent(self, name):
        safe_path(name)
        path, _, leaf = name.rpartition('/')
        current = ''
        descriptor = self.directory
        for component in path.split('/') if path else []:
            current = current + '/' + component if current else component
            if current not in self.directories:
                self.ledger.charge('outputAllocatedBytes', 4096, self.ledger.limits['outputAllocationReservationBytes'])
                os.mkdir(component, mode=0o700, dir_fd=descriptor)
                admitted = os.open(component, os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC | os.O_NOFOLLOW, dir_fd=descriptor)
                self.handles.append(admitted)
                original = os.fstat(admitted)
                self.directory_links.append((descriptor, component, admitted, original))
                self.directories[current] = admitted
                os.fsync(descriptor)
            descriptor = self.directories[current]
        return descriptor, leaf
    def write(self, name, raw):
        self.ledger.output(len(raw))
        parent, leaf = self.parent(name)
        errors = []
        descriptor = None
        try:
            self.verify()
            descriptor = os.open(leaf, os.O_RDWR | os.O_CREAT | os.O_EXCL | os.O_CLOEXEC | os.O_NOFOLLOW, 0o600, dir_fd=parent)
            before = os.fstat(descriptor)
            require(stat.S_ISREG(before.st_mode), 'OUTPUT_NOT_REGULAR_FILE')
            view = memoryview(raw)
            while view:
                count = os.write(descriptor, view)
                require(count > 0, 'OUTPUT_WRITE_NO_PROGRESS')
                view = view[count:]
            os.fsync(descriptor)
            os.lseek(descriptor, 0, os.SEEK_SET)
            replay = bytearray()
            while True:
                part = os.read(descriptor, min(65536, len(raw) - len(replay) + 1))
                if not part:
                    break
                replay.extend(part)
                require(len(replay) <= len(raw), 'OUTPUT_RUNNING_READBACK_CAP')
            after = os.fstat(descriptor)
            present = os.stat(leaf, dir_fd=parent, follow_symlinks=False)
            require(bytes(replay) == raw and before.st_dev == after.st_dev == present.st_dev and before.st_ino == after.st_ino == present.st_ino and after.st_size == len(raw), 'OUTPUT_SAME_HANDLE_READBACK_REFUSED')
            os.fchmod(descriptor, 0o400)
            os.fsync(descriptor)
        except BaseException as error:
            add(errors, error)
        if descriptor is not None:
            try:
                os.close(descriptor)
            except BaseException as error:
                add(errors, error)
        try:
            os.fsync(parent)
            self.verify()
            self.ledger.guard()
        except BaseException as error:
            add(errors, error)
        settle(errors)
    def reserve_slot(self, name, amount):
        self.ledger.output(amount)
        descriptor = None
        errors = []
        try:
            descriptor = os.open(name, os.O_RDWR | os.O_CREAT | os.O_EXCL | os.O_CLOEXEC | os.O_NOFOLLOW, 0o600, dir_fd=self.directory)
            self.slots[name] = (descriptor, amount, None)
            self.slots[name] = (descriptor, amount, os.fstat(descriptor))
            chunk = b' ' * 65536
            remaining = amount
            while remaining:
                block = chunk[:min(remaining, len(chunk))]
                count = os.write(descriptor, block)
                require(count > 0, 'DIAGNOSTIC_RESERVE_NO_PROGRESS')
                remaining -= count
            os.fsync(descriptor)
            os.fsync(self.directory)
        except BaseException as error:
            add(errors, error)
        # A successfully admitted slot remains retained even if reserve/fsync fails.
        settle(errors)
    def overwrite_slot(self, name, value):
        descriptor, amount, initial = self.slots[name]
        require(initial is not None, 'DIAGNOSTIC_SLOT_ORIGINAL_WITNESS_UNAVAILABLE')
        self.verify()
        raw = json.dumps(value, ensure_ascii=True, indent=2, sort_keys=True).encode('ascii') + b'\n'
        require(len(raw) <= amount, 'DIAGNOSTIC_SLOT_COMPLETE_RECORD_CAP_REFUSED')
        current = os.fstat(descriptor)
        present = os.stat(name, dir_fd=self.directory, follow_symlinks=False)
        require(current.st_dev == initial.st_dev == present.st_dev and current.st_ino == initial.st_ino == present.st_ino and current.st_size == amount, 'DIAGNOSTIC_SLOT_IDENTITY_REFUSED')
        os.lseek(descriptor, 0, os.SEEK_SET)
        data = raw + b' ' * (amount - len(raw))
        view = memoryview(data)
        while view:
            count = os.write(descriptor, view)
            require(count > 0, 'DIAGNOSTIC_WRITE_NO_PROGRESS')
            view = view[count:]
        os.fsync(descriptor)
        os.lseek(descriptor, 0, os.SEEK_SET)
        replay = bytearray()
        while len(replay) < amount:
            part = os.read(descriptor, min(65536, amount - len(replay)))
            require(part, 'DIAGNOSTIC_READBACK_INCOMPLETE')
            replay.extend(part)
        after = os.fstat(descriptor)
        present = os.stat(name, dir_fd=self.directory, follow_symlinks=False)
        require(bytes(replay) == data and after.st_dev == initial.st_dev == present.st_dev and after.st_ino == initial.st_ino == present.st_ino and after.st_size == amount, 'DIAGNOSTIC_READBACK_MISMATCH')
        os.fsync(self.directory)
        self.verify()
    def close(self):
        errors = []
        for descriptor, _, _ in tuple(self.slots.values()):
            try:
                os.close(descriptor)
            except BaseException as error:
                add(errors, error)
        for descriptor in reversed(self.handles):
            try:
                os.close(descriptor)
            except BaseException as error:
                add(errors, error)
        settle(errors)

def replay(config, ledger, publisher):
    controls = pure_controls()
    api = GitHubReader(ledger, os.environ.get('GH_TOKEN'))
    ledger.phase('original-run-metadata')
    run = api.json('/actions/runs/' + str(RUN_ID))
    require(run['id'] == RUN_ID and run['head_sha'] == HEAD and run['head_branch'] == SOURCE_BRANCH and run['run_attempt'] == config['runAttempt'] and run['status'] == 'completed', 'ORIGINAL_RUN_IDENTITY_OR_COMPLETION_MISMATCH')
    require(run['repository']['full_name'] == REPOSITORY and run['head_repository']['full_name'] == REPOSITORY, 'ORIGINAL_RUN_REPOSITORY_MISMATCH')
    commit = api.json('/git/commits/' + HEAD)
    require(commit['sha'] == HEAD and commit['tree']['sha'] == config['tree'], 'ORIGINAL_HEAD_TREE_MISMATCH')
    report = {'schema': 1, 'status': 'REPLAY_PENDING', 'originalRepository': REPOSITORY, 'originalHead': HEAD, 'originalTree': config['tree'], 'originalRunId': RUN_ID, 'originalRunAttempt': config['runAttempt'], 'originalRunConclusion': run['conclusion'], 'custodyRunId': os.environ['GITHUB_RUN_ID'], 'custodyRunAttempt': os.environ['GITHUB_RUN_ATTEMPT'], 'archives': [], 'lanes': {}, 'fullFiles': [], 'fullPieces': [], 'selectedOriginalTexts': [], 'qualifications': ['Both exact Home437 Debug and Release families replay independently and fully. Metadata paths and one lane never establish the other cause or successful Home/compile/native acceptance.', 'Original payloads are never imported, invoked, loaded, installed, compiled or retested.', 'Replay proves available original byte/path/type/CRC/SHA custody only; it does not prove semantic correctness, authority, compile closure, full drains or native acceptance.', 'Every full indexed original is hashed. Needed text is retained whole; unavailable additional source or original families remain missing.']}
    report['readerPureControls'] = controls
    report['readerSourceCommit'] = os.environ['GITHUB_SHA']
    cached = {}
    indices = {}
    selected = {}
    binaries = {}
    errors = []
    try:
        for row in config['artifacts']:
            ledger.phase('artifact-' + str(row['id']))
            raw, identity = api.artifact(row)
            inner, member = outer_member(raw, row, ledger)
            del raw
            identity['member'] = member
            identity['memberSha256'] = digest(inner)
            report['archives'].append(identity)
            if row['role'] == 'index':
                manifest, files, pieces = validate_index(inner, row['lane'], config)
                indices[row['lane']] = (manifest, files, pieces, inner)
            else:
                archive = zipfile.ZipFile(io.BytesIO(inner), 'r')
                cached[(row['lane'], row['part'])] = archive
            del inner
        for lane in ('debug', 'release'):
            manifest, files, pieces, raw_index = indices[lane]
            for part in (0,):
                archive = cached[(lane, part)]
                expected = {name: piece for name, piece in pieces.items() if piece['artifactPart'] == part}
                names = [info.filename for info in archive.infolist()]
                require(len(names) == len(expected) and len(set(names)) == len(names) and set(names) == set(expected), 'INNER_ZIP_COMPLETE_MEMBER_SET_MISMATCH')
                for info in archive.infolist():
                    piece = expected[info.filename]
                    row = safe_zip_info(info, piece['piece'], piece['bytes'])
                    data = zip_read(archive, info, piece['bytes'], ledger)
                    require(digest(data) == piece['sha256'], 'INDEXED_PIECE_SHA256_MISMATCH')
                    report['fullPieces'].append(dict(row, lane=lane, artifactPart=part, sha256=piece['sha256'], fullFile=piece['path'], offset=piece['offset']))
            selected_size = sum(row['bytes'] for path, row in files.items() if selected_text(path, config))
            require(sum(len(raw) for raw in selected.values()) + selected_size + sum(len(item[3]) for item in indices.values()) + config['limits']['reportSlotBytes'] + config['limits']['diagnosticSlotBytes'] <= config['limits']['outputAttemptBytes'], 'WHOLE_SELECTED_TEXT_OUTPUT_CAP_REFUSED')
            for path, file in files.items():
                state = hashlib.sha256()
                length = 0
                keep = selected_text(path, config)
                task = any(lane == row['lane'] and path in (row['path'], row['path'][:-4] + '.pdb') for row in config['taskPes'])
                content = bytearray() if keep or task else None
                for piece in file['pieces']:
                    archive = cached[(lane, piece['artifactPart'])]
                    data = zip_read(archive, archive.getinfo(piece['piece']), piece['bytes'], ledger)
                    require(digest(data) == piece['sha256'] and length == piece['offset'], 'RECONSTRUCTED_PIECE_OR_OFFSET_MISMATCH')
                    state.update(data)
                    length += len(data)
                    if content is not None:
                        content.extend(data)
                require(length == file['bytes'] and state.hexdigest() == file['sha256'], 'RECONSTRUCTED_FULL_FILE_SHA256_MISMATCH')
                report['fullFiles'].append({'lane': lane, 'path': path, 'bytes': length, 'sha256': file['sha256'], 'pieces': len(file['pieces']), 'selectedText': keep})
                if keep:
                    raw = bytes(content)
                    raw.decode('utf-8', errors='strict')  # No sampled/prefix decoding or binary publication.
                    selected[(lane, path)] = raw
                if task:
                    binaries[path] = bytes(content)
            report['lanes'][lane] = {'indexBytes': len(raw_index), 'indexSha256': digest(raw_index), 'fullFiles': len(files), 'fullPieces': len(pieces), 'nonempty': sum(row['bytes'] > 0 for row in files.values()), 'wholeDeclaredBytes': sum(row['bytes'] for row in files.values()), 'selectedTextBytes': selected_size, 'selectionComplete': True}
        report['originalTaskPhysicalSymbolsByLane'] = {}
        for task_row in config['taskPes']:
            pe = binaries[task_row['path']]
            require(len(pe) == task_row['bytes'] and digest(pe) == task_row['sha256'], 'TASK_PE_ORIGINAL_DESCRIPTOR_MISMATCH')
            report['originalTaskPhysicalSymbolsByLane'][task_row['lane']] = physical_symbols(pe, binaries.get(task_row['path'][:-4] + '.pdb'), config, ledger)
        require(len(report['fullFiles']) == 406 and len(report['fullPieces']) == 390, 'FINAL_FAMILY_SCOPED_COUNT_MISMATCH')
        ledger.phase('complete-original-replay-before-publication')
    except BaseException as error:
        add(errors, error)
    for archive in tuple(cached.values()):
        finish_handle(archive, errors)
    settle(errors)
    # No text publication occurs until ALL originals and physical symbols replay and close.
    for lane, (_, _, _, raw_index) in indices.items():
        publisher.write('originals/' + lane + '/manifest.json', raw_index)
    for (lane, path), raw in selected.items():
        publisher.write('originals/' + lane + '/' + path, raw)
        report['selectedOriginalTexts'].append({'lane': lane, 'path': path, 'bytes': len(raw), 'sha256': digest(raw)})
    ledger.phase('complete-text-publication')
    report['status'] = 'COMPLETE_ORIGINAL_BYTES_PENDING_FINAL_IO_OUTCOME'
    report['finalIoAcceptanceRequires'] = 'Successful custody job plus final safe console status; any later report/slot/directory/ancestor close failure refuses completion.'
    report['attempts'] = dict(ledger.attempts)
    report['timeline'] = list(ledger.timeline)
    return report

def run_in_directory(parent, verify, config):
    verify()
    leaf = 'home437-original-custody-' + os.environ['GITHUB_RUN_ID'] + '-' + os.environ['GITHUB_RUN_ATTEMPT']
    require(re.fullmatch(r'home437-original-custody-[0-9]+-[0-9]+', leaf) is not None, 'OUTPUT_DIRECTORY_NAME_REFUSED')
    initial = os.fstatvfs(parent)
    require(initial.f_frsize <= 4096, 'FILESYSTEM_ALLOCATION_UNIT_UNSUPPORTED')
    require(initial.f_bavail * initial.f_frsize >= config['limits']['filesystemFreeFloorBytes'] + config['limits']['outputAllocationReservationBytes'], 'INITIAL_OUTPUT_FIXED_FLOOR_REFUSED')
    ledger = Ledger(config, parent)
    ledger.charge('outputAllocatedBytes', 4096, ledger.limits['outputAllocationReservationBytes'])
    os.mkdir(leaf, mode=0o700, dir_fd=parent)
    directory = None
    publisher = None
    errors = []
    report = None
    try:
        directory = os.open(leaf, os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC | os.O_NOFOLLOW, dir_fd=parent)
        witness = os.fstat(directory)
        def verify_output():
            verify()
            current = os.fstat(directory)
            present = os.stat(leaf, dir_fd=parent, follow_symlinks=False)
            require(current.st_dev == witness.st_dev == present.st_dev and current.st_ino == witness.st_ino == present.st_ino and stat.S_ISDIR(present.st_mode), 'OUTPUT_DIRECTORY_ORIGINAL_LINK_CHANGED')
        ledger.guard()
        publisher = Publisher(directory, ledger, verify_output)
        publisher.reserve_slot('replay-report.json', config['limits']['reportSlotBytes'])
        publisher.reserve_slot('diagnostic.json', config['limits']['diagnosticSlotBytes'])
        publisher.overwrite_slot('diagnostic.json', {'status': 'REPLAY_PENDING', 'qualification': 'No original body or cause has been accepted.', 'attempts': dict(ledger.attempts)})
        report = replay(config, ledger, publisher)
    except BaseException as error:
        add(errors, error)
    if publisher is not None:
        if report is not None:
            try:
                publisher.overwrite_slot('replay-report.json', report)
            except BaseException as error:
                add(errors, error)
        try:
            publisher.overwrite_slot('diagnostic.json', {'status': 'REFUSED' if errors else 'COMPLETE_CUSTODY_ONLY', 'originalCauses': projections(errors), 'attempts': dict(publisher.ledger.attempts), 'timeline': list(publisher.ledger.timeline), 'reportCompleted': report is not None, 'qualification': 'Full byte custody only; native/runtime/authority/drain semantics remain independently reviewed.'})
        except BaseException as error:
            add(errors, error)
        try:
            publisher.close()
        except BaseException as error:
            add(errors, error)
    if directory is not None:
        try:
            os.fsync(directory)
        except BaseException as error:
            add(errors, error)
        try:
            os.close(directory)
        except BaseException as error:
            add(errors, error)
    try:
        verify()
        os.fsync(parent)
    except BaseException as error:
        add(errors, error)
    # Fixed console label only; full original objects retained until final settle.
    settle(errors)

def main(config):
    require(sys.flags.optimize == 0 and sys.version_info >= (3, 11) and os.name == 'posix', 'READER_RUNTIME_UNSUPPORTED')
    require(config['repository'] == REPOSITORY and config['head'] == HEAD and config['runId'] == RUN_ID and config['sourceBranch'] == SOURCE_BRANCH, 'FIXED_SOURCE_CONFIGURATION_MISMATCH')
    require(os.environ.get('GITHUB_REPOSITORY') == REPOSITORY and os.environ.get('GITHUB_EVENT_NAME') == 'workflow_dispatch' and os.environ.get('GITHUB_REF') == 'refs/heads/' + CUSTODY_BRANCH, 'CUSTODY_JOB_CONTEXT_MISMATCH')
    require(len(config['artifacts']) == 4 and len({row['id'] for row in config['artifacts']}) == 4 and sum(row['bytes'] for row in config['artifacts']) == 18540573, 'FIXED_FOUR_ARTIFACT_ALLOWLIST_MISMATCH')
    require(len(config['taskPes']) == 2 and {row['lane'] for row in config['taskPes']} == {'debug', 'release'}, 'FIXED_BOTH_ORIGINAL_TASK_WITNESSES_REQUIRED')
    require(available_memory() >= config['limits']['ramWorkingSetBytes'] + config['limits']['ramAvailableFloorBytes'], 'INITIAL_MEMORY_RESERVATION_REFUSED')
    _, hard = resource.getrlimit(resource.RLIMIT_AS)
    amount = config['limits']['ramWorkingSetBytes']
    require(hard == resource.RLIM_INFINITY or hard >= amount, 'EXISTING_ADDRESS_SPACE_LIMIT_REFUSED')
    resource.setrlimit(resource.RLIMIT_AS, (amount, hard))
    parent = os.environ.get('RUNNER_TEMP', '')
    errors = []
    try:
        directory_edges(parent, lambda descriptor, verify: run_in_directory(descriptor, verify, config))
    except BaseException as error:
        add(errors, error)
    # This final safe projection includes even last-slot/directory/ancestor close failures.
    # Raw exception messages/tracebacks and temporary URLs are never printed.
    final = {'status': 'REFUSED' if errors else 'COMPLETE_ORIGINAL_CUSTODY_NO_RUNTIME_ACCEPTANCE', 'originalCauses': projections(errors), 'qualification': 'Any final diagnostic/close refusal invalidates complete publication even when original text files are retained.'}
    try:
        print(json.dumps(final, ensure_ascii=True, sort_keys=True), flush=True)
    except BaseException as error:
        add(errors, error)
    settle(errors)

# Exact maintained actual e0c portable-PDB reader (f7f4ac108d287f211eb43e26c5602a2646f149382af0bc8f3fcfff9c4a1d98c8).
import struct,uuid
def pdb_documents(data):
 assert data[:4]==b'BSJB'; n=struct.unpack_from('<I',data,12)[0]; pos=(16+n+3)&~3; _,count=struct.unpack_from('<HH',data,pos);pos+=4; streams={}
 for _ in range(count):
  off,size=struct.unpack_from('<II',data,pos);pos+=8;end=data.index(0,pos);name=data[pos:end].decode();pos=(end+4)&~3;streams[name]=data[off:off+size]
 tables=streams['#~']; heap=tables[6]; valid=struct.unpack_from('<Q',tables,8)[0];pos=24; rows={}
 for i in range(64):
  if valid>>i&1:rows[i]=struct.unpack_from('<I',tables,pos)[0];pos+=4
 assert not any(i<48 for i in rows); blob=streams['#Blob']; guids=streams['#GUID']; bw=4 if heap&4 else 2;gw=4 if heap&2 else 2
 def uint(data,at):
  v=data[at]
  if v<128:return v,at+1
  if v<192:return ((v&63)<<8)|data[at+1],at+2
  return ((v&31)<<24)|(data[at+1]<<16)|(data[at+2]<<8)|data[at+3],at+4
 def getblob(at):
  size,p=uint(blob,at);return blob[p:p+size]
 result={}
 for _ in range(rows.get(48,0)):
  vals=[]
  for width in (bw,gw,bw,gw):vals.append(int.from_bytes(tables[pos:pos+width],'little'));pos+=width
  name,algo,digest,lang=vals; pathdata=getblob(name); separator=chr(pathdata[0]);q=1;parts=[]
  while q<len(pathdata):v,q=uint(pathdata,q);parts.append(getblob(v).decode())
  path=separator.join(parts); algorithm=str(uuid.UUID(bytes_le=guids[(algo-1)*16:algo*16]));assert algorithm in ('8829d00f-11b8-4213-878b-770e8597ac16','ff1816ec-aa5e-4d10-87f7-6f4963833460');digest=getblob(digest).hex();assert len(digest)==(64 if algorithm=='8829d00f-11b8-4213-878b-770e8597ac16' else 40)
  result[path]={'digest':digest,'algorithm':algorithm,'hashName':'sha256' if len(digest)==64 else 'sha1'}
 return result

def metadata_streams(data):
 assert data[:4]==b'BSJB';n=struct.unpack_from('<I',data,12)[0];pos=(16+n+3)&~3;_,count=struct.unpack_from('<HH',data,pos);pos+=4;result={}
 for _ in range(count):
  off,size=struct.unpack_from('<II',data,pos);pos+=8;end=data.index(0,pos);name=data[pos:end].decode();pos=(end+4)&~3
  assert name not in result and off+size<=len(data);result[name]=data[off:off+size]
 return result

def assert_actual_pair(dll,pdb):
 # Portable PDB content identity must match the actual PE CodeView record and stamp.
 assert dll[:2]==b'MZ';pe=struct.unpack_from('<I',dll,0x3c)[0];assert dll[pe:pe+4]==b'PE\0\0'
 sections=struct.unpack_from('<H',dll,pe+6)[0];optSize=struct.unpack_from('<H',dll,pe+20)[0];opt=pe+24;magic=struct.unpack_from('<H',dll,opt)[0];assert magic in (0x10b,0x20b)
 directory=opt+(96 if magic==0x10b else 112);rva,size=struct.unpack_from('<II',dll,directory+6*8);assert rva and size and size%28==0
 def rva_offset(value):
  for i in range(sections):
   row=opt+optSize+i*40;virtualSize,address,rawSize,raw=struct.unpack_from('<IIII',dll,row+8)
   if address<=value<address+max(virtualSize,rawSize):
    offset=raw+(value-address);assert offset<len(dll);return offset
  raise ValueError('actual PE debug directory RVA outside sections')
 at=rva_offset(rva);matches=[]
 for i in range(size//28):
  row=at+i*28;stamp=struct.unpack_from('<I',dll,row+4)[0];kind,amount,address,pointer=struct.unpack_from('<IIII',dll,row+12)
  if kind==2:
   content=dll[pointer:pointer+amount];assert len(content)==amount and content[:4]==b'RSDS' and struct.unpack_from('<I',content,20)[0]==1
   matches.append((content[4:20],stamp))
 assert len(matches)==1
 identifier=metadata_streams(pdb)['#Pdb'][:20];assert len(identifier)==20
 assert identifier[:16]==matches[0][0] and struct.unpack_from('<I',identifier,16)[0]==matches[0][1], 'actual DLL/PDB identity mismatch'
 return {'portablePdbId':identifier.hex(),'codeViewGuid':str(uuid.UUID(bytes_le=matches[0][0])),'codeViewStamp':matches[0][1]}


def pure_controls():
    """Own-reader pure memory controls only; no original artifacts/FS/network/process."""
    outcomes = []
    def pass_case(name, callback):
        callback()
        outcomes.append({'name': name, 'outcome': 'PASS_OWN_READER_MEMORY_CONTROL'})
    def reject_case(name, callback):
        try:
            callback()
        except (Refusal, ValueError, UnicodeError):
            outcomes.append({'name': name, 'outcome': 'PASS_REFUSAL_MEMORY_CONTROL'})
            return
        raise Refusal('PURE_CONTROL_EXPECTED_REFUSAL_' + name)
    pass_case('canonical-lane-relative-path', lambda: require(safe_path('joint-sdk/logs/debug-build-entry-01.log') == 'joint-sdk/logs/debug-build-entry-01.log', 'PURE_PATH_VALUE'))
    for index, path in enumerate(('/root', '../file', 'x/../file', 'x//file', 'x/./file', 'x\\file', 'C:drive', 'x/\x00name', 'x/\x7fname', 'x/')):
        reject_case('unsafe-path-' + str(index), lambda path=path: safe_path(path))
    reject_case('duplicate-json-member', lambda: unique_json(b'{"drained":false,"drained":true}'))
    reject_case('nonfinite-json-number', lambda: unique_json(b'{"n":NaN}'))
    pass_case('ordinary-json-complete', lambda: require(unique_json(b'{"drained":false,"nested":{"a":1}}')['drained'] is False, 'PURE_JSON_VALUE'))
    good = 'https://productionresultssa0.blob.core.windows.net/actions-results/owned/path?signature=memory-only'
    pass_case('fixed-trusted-https-download-host', lambda: require(validate_download_url(good) == good, 'PURE_URL_VALUE'))
    for index, url in enumerate(('http://productionresultssa0.blob.core.windows.net/actions-results/x?q=x', 'https://api.github.com/actions-results/x?q=x', 'https://productionresultssa0.blob.core.windows.net.evil.invalid/actions-results/x?q=x', 'https://user@productionresultssa0.blob.core.windows.net/actions-results/x?q=x', 'https://productionresultssa0.blob.core.windows.net/foreign/x?q=x', 'https://productionresultssa0.blob.core.windows.net/actions-results/x?q=x#fragment')):
        reject_case('untrusted-download-' + str(index), lambda url=url: validate_download_url(url))
    reject_case('uppercase-sha-refused', lambda: exact_sha('A' * 64))
    reject_case('boolean-length-refused', lambda: exact_int(True, 'PURE_BOOLEAN_LENGTH'))
    info = zipfile.ZipInfo('piece-000000.part')
    info.file_size = 3
    info.compress_size = 3
    info.compress_type = zipfile.ZIP_STORED
    info.CRC = zlib.crc32(b'abc') & 0xffffffff
    info.external_attr = (stat.S_IFREG | 0o400) << 16
    pass_case('regular-whole-zip-member', lambda: safe_zip_info(info, 'piece-000000.part', 3))
    link = zipfile.ZipInfo('piece-000000.part')
    link.file_size = 3
    link.compress_size = 3
    link.compress_type = zipfile.ZIP_STORED
    link.CRC = zlib.crc32(b'abc') & 0xffffffff
    link.external_attr = (stat.S_IFLNK | 0o777) << 16
    reject_case('symlink-zip-member-refused', lambda: safe_zip_info(link, 'piece-000000.part', 3))
    reject_case('foreign-zip-member-refused', lambda: safe_zip_info(info, 'piece-000001.part', 3))
    reject_case('wrong-member-size-refused', lambda: safe_zip_info(info, 'piece-000000.part', 4))
    ledger = Ledger.__new__(Ledger)
    ledger.attempts = {'archiveBytes': 0}
    ledger.guard = lambda: None
    pass_case('attempt-charged-before-body', lambda: ledger.charge('archiveBytes', 4, 8))
    # A downstream failure does not roll back the admitted attempt.
    try:
        raise Refusal('PURE_DOWNSTREAM_IO_FAILURE')
    except Refusal:
        pass
    pass_case('failed-body-charge-retained', lambda: require(ledger.attempts['archiveBytes'] == 4, 'PURE_CHARGE_REFUNDED'))
    reject_case('second-attempt-cap-refusal', lambda: ledger.charge('archiveBytes', 5, 8))
    primary = OSError('pure-original-body')
    cleanup = OSError('pure-independent-close')
    errors = []
    add(errors, primary); add(errors, cleanup); add(errors, primary)
    try:
        settle(errors)
    except BaseExceptionGroup as group:
        pass_case('same-primary-and-distinct-cleanup-retained', lambda: require(group.exceptions[0] is primary and group.exceptions[1] is cleanup and len(group.exceptions) == 2, 'PURE_EXCEPTION_OBJECTS'))
    else:
        raise Refusal('PURE_GROUP_NOT_RETAINED')
    protected = projections([urllib.error.URLError('https://signed.invalid/private?credential=not-real')])
    pass_case('network-error-projection-withholds-url', lambda: require('signed.invalid' not in json.dumps(protected) and 'credential' not in json.dumps(protected), 'PURE_URL_LOG_DISCLOSURE'))
    # Family identity never deduplicates equal logical path strings.
    table = {('joint', 'receipt.json'): b'joint', ('owning', 'receipt.json'): b'owning'}
    pass_case('lane-scoped-identical-paths-distinct', lambda: require(len(table) == 2 and table[('joint', 'receipt.json')] != table[('owning', 'receipt.json')], 'PURE_LANE_COLLAPSE'))
    # These eight controls exercise the new one-part index boundary in memory only.
    sample_file = {'path': 'corrected-v3-debug/receipt.json', 'bytes': 3,
        'sha256': digest(b'abc'), 'pieces': [{'artifactPart': 0,
        'piece': 'piece-000000.part', 'path': 'corrected-v3-debug/receipt.json',
        'offset': 0, 'bytes': 3, 'sha256': digest(b'abc')}]}
    sample_bundle = {'artifactPart': 0, 'file': 'bundle-00.zip',
        'bytes': 17, 'sha256': digest(b'memory-bundle')}
    sample_index = {'commit': HEAD, 'runId': str(RUN_ID),
        'status': 'INDEPENDENT PARTS NO CROSSARTIFACT ATOMICITY',
        'files': [sample_file], 'partCount': 1, 'maxPayloadBytes': 27262976,
        'qualification': 'Pure own-reader one-part index control.',
        'bundles': [sample_bundle]}
    sample_config = {'expectedCounts': {'compiler': {'files': 1,
        'pieces': 1, 'nonempty': 1, 'bytes': 3}},
        'artifacts': [{'lane': 'compiler', 'role': 'part', 'part': 0,
            'member': 'bundle-00.zip', 'innerBytes': 17,
            'innerSha256': digest(b'memory-bundle')}],
        'required': {'compiler': ['corrected-v3-debug/receipt.json']}}
    def one_part(changed=None, config_change=None):
        value = unique_json(json.dumps(sample_index).encode('utf-8'))
        if changed is not None:
            changed(value)
        configuration = unique_json(json.dumps(sample_config).encode('utf-8'))
        if config_change is not None:
            config_change(configuration)
        return validate_index(json.dumps(value).encode('utf-8'), 'compiler', configuration)
    pass_case('one-part-complete-index', lambda: require(
        len(one_part()[1]) == 1 and len(one_part()[2]) == 1,
        'PURE_ONE_PART_COMPLETE'))
    reject_case('one-part-foreign-part', lambda: one_part(
        lambda value: value['files'][0]['pieces'][0].update(artifactPart=1)))
    reject_case('one-part-offset-gap', lambda: one_part(
        lambda value: value['files'][0]['pieces'][0].update(offset=1)))
    reject_case('one-part-duplicate-full-file', lambda: one_part(
        lambda value: value['files'].append(value['files'][0]),
        lambda configuration: configuration['expectedCounts']['compiler'].update(
            files=2, nonempty=2, bytes=6)))
    reject_case('one-part-foreign-piece-name', lambda: one_part(
        lambda value: value['files'][0]['pieces'][0].update(piece='piece-000001.part')))
    reject_case('one-part-bundle-hash-mismatch', lambda: one_part(
        lambda value: value['bundles'][0].update(sha256=digest(b'other-bundle'))))
    reject_case('one-part-required-file-missing', lambda: one_part(
        lambda value: (value['files'][0].update(path='corrected-v3-debug/other.json'),
            value['files'][0]['pieces'][0].update(path='corrected-v3-debug/other.json'))))
    reject_case('one-part-incomplete-full-file', lambda: one_part(
        lambda value: value['files'][0].update(bytes=4)))
    return {'status': 'OWN_READER_PURE_CONTROLS_ONLY', 'outcomes': outcomes, 'count': len(outcomes), 'qualification': 'These controls do not acquire originals, exercise OS custody, execute payloads, prove original drains or confer runtime acceptance.'}
