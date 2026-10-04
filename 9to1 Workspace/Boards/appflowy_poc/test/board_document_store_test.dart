import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:haven_boards_appflowy_poc/board_document_store.dart';
import 'package:haven_boards_appflowy_poc/rnote_canvas.dart';

void main() {
  test('one local document retains AppFlowy structure, Rnote bytes, and world coordinates', () async {
    final root = await Directory.systemTemp.createTemp('haven-boards-document-');
    addTearDown(() => root.delete(recursive: true));
    final store = BoardDocumentStore(File('${root.path}${Platform.pathSeparator}board-main.json'));

    await store.save(
      snapshot: {
        'id': 'board-main',
        'groups': [
          {
            'id': 'todo',
            'cards': [
              {'id': 'card-1', 'title': 'First task'},
            ],
          },
        ],
      },
      rnoteBytes: Uint8List.fromList([1, 2, 3, 4]),
      viewport: const BoardWorldViewport(centerX: 480, centerY: -120, zoom: 1.5),
    );

    final document = await store.load();
    expect(document?['schemaVersion'], 1);
    expect((document?['snapshot'] as Map)['id'], 'board-main');
    expect(document?['rnote'], 'AQIDBA==');
    final viewport = BoardWorldViewport.fromJson(Map<String, dynamic>.from(document?['viewport'] as Map));
    expect(viewport.centerX, 480);
    expect(viewport.centerY, -120);
    expect(viewport.zoom, 1.5);
    expect(
      root.listSync().where((entry) => entry.path.endsWith('.tmp')),
      isEmpty,
    );
  });
}
