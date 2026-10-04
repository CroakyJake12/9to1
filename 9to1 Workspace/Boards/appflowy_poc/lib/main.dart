import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:appflowy_board/appflowy_board.dart';
import 'package:flutter/material.dart';

import 'board_document_store.dart';
import 'board_ink_surface.dart';
import 'rnote_canvas.dart';

void main() {
  runApp(const HavenBoardsPocApp());
}

class HavenBoardsPocApp extends StatelessWidget {
  const HavenBoardsPocApp({
    super.key,
    this.enablePersistence = true,
  });

  final bool enablePersistence;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: 'Haven Boards AppFlowy PoC',
      theme: ThemeData(useMaterial3: true),
      home: HavenBoardsPocPage(enablePersistence: enablePersistence),
    );
  }
}

class HavenBoardsPocPage extends StatefulWidget {
  const HavenBoardsPocPage({
    super.key,
    this.enablePersistence = true,
  });

  final bool enablePersistence;

  @override
  State<HavenBoardsPocPage> createState() => _HavenBoardsPocPageState();
}

class _HavenBoardsPocPageState extends State<HavenBoardsPocPage> {
  late final AppFlowyBoardController _controller;
  late final BoardDocumentStore _documentStore;
  final GlobalKey<BoardInkSurfaceState> _inkSurfaceKey = GlobalKey<BoardInkSurfaceState>();
  RnoteCanvasEngine? _ink;
  RnoteTool _inkTool = RnoteTool.pen;
  bool _inkMode = false;
  bool _panMode = false;
  String _status = 'Local-first AppFlowy Board proof';
  int _nextCard = 4;
  int _nextGroup = 4;

  @override
  void initState() {
    super.initState();
    final dataDirectory = Platform.environment['HAVEN_BOARDS_DATA_DIR'] ?? Directory.current.path;
    _documentStore = BoardDocumentStore(File('$dataDirectory${Platform.pathSeparator}boards${Platform.pathSeparator}board-main.json'));
    _controller = AppFlowyBoardController(
      onMoveGroup: (_, __, ___, ____) => unawaited(_persist('Group reordered')),
      onMoveGroupItem: (_, __, ___) => unawaited(_persist('Card reordered')),
      onMoveGroupItemToGroup: (_, __, ___, ____) => unawaited(_persist('Card moved between groups')),
    );
    _loadDefaults();
    try {
      _ink = RnoteCanvasEngine.open();
    } on Object catch (error) {
      _status = 'AppFlowy board ready; Rnote ink runtime unavailable: $error';
    }
    if (widget.enablePersistence) {
      unawaited(_restore());
    }
  }

  @override
  void dispose() {
    _ink?.close();
    _controller.dispose();
    super.dispose();
  }

  void _loadDefaults() {
    _controller.addGroups([
      AppFlowyGroupData(
        id: 'todo',
        name: 'To do',
        items: [HavenBoardItem('card-1', 'First task')],
      ),
      AppFlowyGroupData(
        id: 'doing',
        name: 'Doing',
        items: [HavenBoardItem('card-2', 'Try AppFlowy Board')],
      ),
      AppFlowyGroupData(
        id: 'done',
        name: 'Done',
        items: [HavenBoardItem('card-3', 'Persist locally')],
      ),
    ]);
  }

  Future<void> _restore() async {
    if (!widget.enablePersistence) return;

    final document = await _documentStore.load();
    if (document == null) {
      await _persist('Created local snapshot');
      return;
    }

    try {
      final snapshot = Map<String, dynamic>.from(document['snapshot'] as Map);
      final groups = (snapshot['groups'] as List<dynamic>? ?? const [])
          .map((raw) => BoardGroupSnapshot.fromJson(raw as Map<String, dynamic>))
          .toList();
      if (groups.isEmpty) return;

      _controller.clear();
      _controller.addGroups(
        groups
            .map(
              (group) => AppFlowyGroupData(
                id: group.id,
                name: group.title,
                items: group.cards
                    .map((card) => HavenBoardItem(card.id, card.title))
                    .toList(),
              ),
            )
            .toList(),
      );
      _nextCard = _largestCardNumber(groups) + 1;
      _nextGroup = _largestGroupNumber(groups) + 1;
      final rnote = document['rnote'];
      final viewport = document['viewport'];
      if (_ink != null && rnote is String && rnote.isNotEmpty) {
        _ink!.restoreRnote(base64Decode(rnote));
        if (viewport is Map) _ink!.setViewport(BoardWorldViewport.fromJson(Map<String, dynamic>.from(viewport)));
      }
      if (mounted) setState(() => _status = 'Restored local snapshot');
    } on Object catch (error) {
      if (mounted) setState(() => _status = 'Snapshot restore failed: $error');
    }
  }

  int _largestGroupNumber(List<BoardGroupSnapshot> groups) {
    var largest = 3;
    for (final group in groups) {
      final match = RegExp(r'^group-(\d+)$').firstMatch(group.id);
      final value = match == null ? null : int.tryParse(match.group(1)!);
      if (value != null && value > largest) largest = value;
    }
    return largest;
  }
    var largest = 3;
    for (final card in groups.expand((group) => group.cards)) {
      final match = RegExp(r'^card-(\d+)$').firstMatch(card.id);
      final value = match == null ? null : int.tryParse(match.group(1)!);
      if (value != null && value > largest) largest = value;
    }
    return largest;
  }

  Future<void> _persist(String reason) async {
    if (!widget.enablePersistence) return;

    final snapshot = BoardSnapshot(
      id: 'board-main',
      title: 'Haven Boards',
      version: DateTime.now().microsecondsSinceEpoch,
      groups: _controller.groupDatas
          .map(
            (group) => BoardGroupSnapshot(
              id: group.id,
              title: group.headerData.groupName,
              cards: group.items
                  .whereType<HavenBoardItem>()
                  .map((item) => BoardCardSnapshot(item.id, item.title))
                  .toList(),
            ),
          )
          .toList(),
    );

    final ink = _ink;
    final bytes = ink?.saveRnote() ?? Uint8List(0);
    final viewport = ink?.viewport() ?? const BoardWorldViewport(centerX: 304, centerY: 204, zoom: 1);
    await _documentStore.save(snapshot: snapshot.toJson(), rnoteBytes: bytes, viewport: viewport);
    if (mounted) {
      final suffix = ink == null ? 'AppFlowy structure saved; Rnote unavailable' : 'saved locally with Rnote ink';
      setState(() => _status = '$reason · $suffix');
    }
  }

  void _addCard(String groupId) {
    final card = HavenBoardItem('card-${_nextCard++}', 'New card');
    _controller.addGroupItem(groupId, card);
    unawaited(_persist('Card created'));
  }

  void _deleteCard(String groupId, HavenBoardItem item) {
    _controller.removeGroupItem(groupId, item.id);
    unawaited(_persist('Card deleted'));
  }

  void _addGroup() {
    final id = 'group-${_nextGroup++}';
    _controller.addGroup(AppFlowyGroupData(id: id, name: 'New group', items: []));
    unawaited(_persist('Group created'));
  }

  void _deleteGroup(String groupId) {
    _controller.removeGroup(groupId);
    unawaited(_persist('Group deleted'));
  }

  void _renameGroup(String groupId, String name) {
    _controller.getGroupController(groupId)?.updateGroupName(name.trim().isEmpty ? 'Untitled group' : name.trim());
    unawaited(_persist('Group renamed'));
  }

  void _moveCardWithin(String groupId, HavenBoardItem item, int delta) {
    final group = _controller.getGroupController(groupId);
    if (group == null) return;
    final from = group.items.indexWhere((candidate) => candidate.id == item.id);
    if (from < 0) return;
    final to = from + delta;
    if (to < 0 || to >= group.items.length) return;
    _controller.moveGroupItem(groupId, from, to);
  }

  void _moveCardAcross(String groupId, HavenBoardItem item, int delta) {
    final groupIndex = _controller.groupIds.indexOf(groupId);
    final targetIndex = groupIndex + delta;
    if (groupIndex < 0 || targetIndex < 0 || targetIndex >= _controller.groupIds.length) return;

    final source = _controller.getGroupController(groupId);
    if (source == null) return;
    final itemIndex = source.items.indexWhere((candidate) => candidate.id == item.id);
    if (itemIndex < 0) return;

    final targetId = _controller.groupIds[targetIndex];
    final target = _controller.getGroupController(targetId);
    if (target == null) return;

    _controller.removeGroupItem(groupId, item.id);
    _controller.insertGroupItem(targetId, target.items.length, item);
    unawaited(_persist('Card moved with accessible command'));
  }

  void _moveGroup(String groupId, int delta) {
    final from = _controller.groupIds.indexOf(groupId);
    final to = from + delta;
    if (from < 0 || to < 0 || to >= _controller.groupIds.length) return;
    _controller.moveGroup(from, to);
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(_inkMode ? 'Haven Boards · Rnote ink' : 'Haven Boards · AppFlowy'),
        actions: [
          IconButton(
            tooltip: 'Edit AppFlowy board',
            onPressed: () => setState(() {
              _inkMode = false;
              _panMode = false;
            }),
            icon: const Icon(Icons.view_kanban_outlined),
          ),
          if (_ink != null) ...[
            _inkButton(RnoteTool.pen, Icons.edit_outlined, 'Rnote pen'),
            _inkButton(RnoteTool.highlighter, Icons.format_color_fill_outlined, 'Rnote highlighter'),
            _inkButton(RnoteTool.eraser, Icons.auto_fix_high_outlined, 'Rnote eraser'),
            _inkButton(RnoteTool.selector, Icons.gesture_outlined, 'Rnote lasso select'),
            _inkButton(RnoteTool.shape, Icons.circle_outlined, 'Rnote shape'),
            IconButton(
              tooltip: 'Pan board world',
              onPressed: () => setState(() {
                _inkMode = true;
                _panMode = true;
              }),
              icon: const Icon(Icons.pan_tool_outlined),
            ),
            IconButton(
              tooltip: 'Zoom out',
              onPressed: () => _inkSurfaceKey.currentState?.zoomBy(1 / 1.2),
              icon: const Icon(Icons.zoom_out),
            ),
            IconButton(
              tooltip: 'Zoom in',
              onPressed: () => _inkSurfaceKey.currentState?.zoomBy(1.2),
              icon: const Icon(Icons.zoom_in),
            ),
            IconButton(
              tooltip: 'Undo Rnote operation',
              onPressed: () => _inkSurfaceKey.currentState?.undo(),
              icon: const Icon(Icons.undo),
            ),
            IconButton(
              tooltip: 'Redo Rnote operation',
              onPressed: () => _inkSurfaceKey.currentState?.redo(),
              icon: const Icon(Icons.redo),
            ),
          ],
          IconButton(
            tooltip: 'Save board locally',
            onPressed: widget.enablePersistence ? () => unawaited(_persist('Manual save')) : null,
            icon: const Icon(Icons.save_outlined),
          ),
          IconButton(
            tooltip: 'Add group',
            onPressed: () => _addGroup(),
            icon: const Icon(Icons.view_column_outlined),
          ),
        ],
      ),
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
            child: Semantics(
              liveRegion: true,
              child: Text(_status),
            ),
          ),
          Expanded(
            child: Stack(
              fit: StackFit.expand,
              children: [
                AppFlowyBoard(
                  controller: _controller,
                  groupConstraints: const BoxConstraints.tightFor(width: 300),
                  config: const AppFlowyBoardConfig(cardPageSize: 0),
                  headerBuilder: (context, group) => _buildHeader(group),
                  cardBuilder: (context, group, groupItem) {
                    final item = groupItem as HavenBoardItem;
                    return _buildCard(group.id, item);
                  },
                ),
                if (_ink case final ink?)
                  IgnorePointer(
                    ignoring: !_inkMode,
                    child: BoardInkSurface(
                      key: _inkSurfaceKey,
                      engine: ink,
                      tool: _inkTool,
                      panMode: _panMode,
                      onChanged: () => unawaited(_persist('Rnote board-world ink updated')),
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _inkButton(RnoteTool tool, IconData icon, String tooltip) {
    return IconButton(
      tooltip: tooltip,
      isSelected: _inkMode && !_panMode && _inkTool == tool,
      onPressed: () => setState(() {
        _inkMode = true;
        _panMode = false;
        _inkTool = tool;
      }),
      icon: Icon(icon),
    );
  }

  Widget _buildHeader(AppFlowyGroupData group) {
    final index = _controller.groupIds.indexOf(group.id);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Row(
        children: [
          Expanded(
            // Donor multi-board example pattern: the header name is editable
            // and commits through updateGroupName, then persists locally.
            child: TextFormField(
              key: ValueKey('group-name-${group.id}'),
              initialValue: group.headerData.groupName,
              decoration: const InputDecoration(
                border: InputBorder.none,
                isDense: true,
              ),
              style: const TextStyle(fontWeight: FontWeight.w700),
              onFieldSubmitted: (value) => _renameGroup(group.id, value),
            ),
          ),
          IconButton(
            tooltip: 'Move ${group.headerData.groupName} left',
            onPressed: index > 0 ? () => _moveGroup(group.id, -1) : null,
            icon: const Icon(Icons.chevron_left),
          ),
          IconButton(
            tooltip: 'Move ${group.headerData.groupName} right',
            onPressed: index >= 0 && index < _controller.groupIds.length - 1
                ? () => _moveGroup(group.id, 1)
                : null,
            icon: const Icon(Icons.chevron_right),
          ),
          IconButton(
            tooltip: 'Add card to ${group.headerData.groupName}',
            onPressed: () => _addCard(group.id),
            icon: const Icon(Icons.add),
          ),
          IconButton(
            tooltip: 'Delete group ${group.headerData.groupName}',
            onPressed: () => _deleteGroup(group.id),
            icon: const Icon(Icons.delete_outline),
          ),
        ],
      ),
    );
  }

  Widget _buildCard(String groupId, HavenBoardItem item) {
    final group = _controller.getGroupController(groupId);
    final cardIndex = group?.items.indexWhere((candidate) => candidate.id == item.id) ?? -1;
    final groupIndex = _controller.groupIds.indexOf(groupId);

    return Semantics(
      label: 'Board card ${item.title}',
      container: true,
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(item.title),
              const SizedBox(height: 8),
              Wrap(
                spacing: 2,
                children: [
                  IconButton(
                    tooltip: 'Move card up',
                    onPressed: cardIndex > 0 ? () => _moveCardWithin(groupId, item, -1) : null,
                    icon: const Icon(Icons.keyboard_arrow_up),
                  ),
                  IconButton(
                    tooltip: 'Move card down',
                    onPressed: group != null && cardIndex >= 0 && cardIndex < group.items.length - 1
                        ? () => _moveCardWithin(groupId, item, 1)
                        : null,
                    icon: const Icon(Icons.keyboard_arrow_down),
                  ),
                  IconButton(
                    tooltip: 'Move card to previous group',
                    onPressed: groupIndex > 0 ? () => _moveCardAcross(groupId, item, -1) : null,
                    icon: const Icon(Icons.keyboard_arrow_left),
                  ),
                  IconButton(
                    tooltip: 'Move card to next group',
                    onPressed: groupIndex >= 0 && groupIndex < _controller.groupIds.length - 1
                        ? () => _moveCardAcross(groupId, item, 1)
                        : null,
                    icon: const Icon(Icons.keyboard_arrow_right),
                  ),
                  IconButton(
                    tooltip: 'Delete card',
                    onPressed: () => _deleteCard(groupId, item),
                    icon: const Icon(Icons.delete_outline),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class HavenBoardItem extends AppFlowyGroupItem {
  HavenBoardItem(this.id, this.title);

  @override
  final String id;
  final String title;
}

class BoardSnapshot {
  const BoardSnapshot({
    required this.id,
    required this.title,
    required this.version,
    required this.groups,
  });

  final String id;
  final String title;
  final int version;
  final List<BoardGroupSnapshot> groups;

  factory BoardSnapshot.fromJson(Map<String, dynamic> json) => BoardSnapshot(
        id: json['id'] as String,
        title: json['title'] as String,
        version: json['version'] as int,
        groups: (json['groups'] as List<dynamic>? ?? const [])
            .map((raw) => BoardGroupSnapshot.fromJson(raw as Map<String, dynamic>))
            .toList(),
      );

  Map<String, dynamic> toJson() => {
        'id': id,
        'title': title,
        'version': version,
        'groups': groups.map((group) => group.toJson()).toList(),
      };
}

class BoardGroupSnapshot {
  const BoardGroupSnapshot({required this.id, required this.title, required this.cards});

  factory BoardGroupSnapshot.fromJson(Map<String, dynamic> json) => BoardGroupSnapshot(
        id: json['id'] as String,
        title: json['title'] as String,
        cards: (json['cards'] as List<dynamic>? ?? const [])
            .map((raw) => BoardCardSnapshot.fromJson(raw as Map<String, dynamic>))
            .toList(),
      );

  final String id;
  final String title;
  final List<BoardCardSnapshot> cards;

  Map<String, dynamic> toJson() => {
        'id': id,
        'title': title,
        'cards': cards.map((card) => card.toJson()).toList(),
      };
}

class BoardCardSnapshot {
  const BoardCardSnapshot(this.id, this.title);

  factory BoardCardSnapshot.fromJson(Map<String, dynamic> json) =>
      BoardCardSnapshot(json['id'] as String, json['title'] as String);

  final String id;
  final String title;

  Map<String, dynamic> toJson() => {'id': id, 'title': title};
}
