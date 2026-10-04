import 'package:appflowy_board/appflowy_board.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:haven_boards_appflowy_poc/main.dart';

void main() {
  test('AppFlowy controller reports and applies Haven-compatible reorder', () {
    String? movedGroup;
    int? movedFrom;
    int? movedTo;
    final controller = AppFlowyBoardController(
      onMoveGroupItem: (groupId, fromIndex, toIndex) {
        movedGroup = groupId;
        movedFrom = fromIndex;
        movedTo = toIndex;
      },
    );
    addTearDown(controller.dispose);

    controller.addGroup(
      AppFlowyGroupData(
        id: 'todo',
        name: 'To do',
        items: [
          HavenBoardItem('a', 'A'),
          HavenBoardItem('b', 'B'),
          HavenBoardItem('c', 'C'),
        ],
      ),
    );

    controller.moveGroupItem('todo', 0, 1);

    expect(movedGroup, 'todo');
    expect(movedFrom, 0);
    expect(movedTo, 1);
    expect(
      controller.getGroupController('todo')!.items.map((item) => item.id),
      ['b', 'a', 'c'],
    );
  });

  testWidgets('every card exposes non-drag movement controls', (tester) async {
    // Filesystem persistence has independent .NET contract/store coverage. Disable it
    // here so this test covers AppFlowy rendering and accessibility controls only.
    await tester.pumpWidget(const HavenBoardsPocApp(enablePersistence: false));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 150));

    expect(find.byType(AppFlowyBoard), findsOneWidget);
    expect(find.byTooltip('Move card up'), findsWidgets);
    expect(find.byTooltip('Move card down'), findsWidgets);
    expect(find.byTooltip('Move card to previous group'), findsWidgets);
    expect(find.byTooltip('Move card to next group'), findsWidgets);

    final firstCard = find.ancestor(
      of: find.text('First task'),
      matching: find.byType(Card),
    );
    expect(firstCard, findsOneWidget);
    expect(
      find.descendant(of: firstCard, matching: find.byType(Semantics)),
      findsWidgets,
    );

    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pump();
  });
}
