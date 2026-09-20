import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'rnote_canvas.dart';

class BoardDocumentStore {
  BoardDocumentStore(this.file);

  final File file;

  Future<Map<String, dynamic>?> load() async {
    if (!await file.exists()) return null;
    return jsonDecode(await file.readAsString()) as Map<String, dynamic>;
  }

  /// The structured AppFlowy projection, Rnote bytes, and world viewport share
  /// one envelope and one same-directory replacement operation.
  Future<void> save({
    required Map<String, dynamic> snapshot,
    required Uint8List rnoteBytes,
    required BoardWorldViewport viewport,
  }) async {
    await file.parent.create(recursive: true);
    final temporary = File('${file.path}.${Random.secure().nextInt(1 << 32)}.tmp');
    final document = <String, dynamic>{
      'schemaVersion': 1,
      'snapshot': snapshot,
      'rnote': base64Encode(rnoteBytes),
      'viewport': viewport.toJson(),
    };

    try {
      await temporary.writeAsString(const JsonEncoder.withIndent('  ').convert(document), flush: true);
      // On the Linux preview target, rename is an atomic replacement because
      // both names live in the same directory and filesystem.
      await temporary.rename(file.path);
    } finally {
      if (await temporary.exists()) await temporary.delete();
    }
  }
}
