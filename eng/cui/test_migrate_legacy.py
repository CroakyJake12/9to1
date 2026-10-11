import unittest
import xml.etree.ElementTree as ET
from migrate_legacy import migrate, migrate_csharp_raw_strings, MigrationError

class CanonicalMigrationTests(unittest.TestCase):
    def test_real_action_binding_identity_and_grid_semantics(self):
        source='<Cui><Actions><Action name="SaveRow" command="Save" parameter="{Binding item.ID}" /></Actions><DefaultTheme value="Default"><Grid id="shell" rows="Auto,*" columns="220,2*"><TextBox id="title" grid-row="1" grid-column="1" text="{Binding Title, Mode=TwoWay}" /><Button action="SaveRow" content="Save" /></Grid></DefaultTheme></Cui>'
        after,_=migrate(source); root=ET.fromstring(after)
        grid=root.find('.//Container[@id="shell"]'); self.assertEqual(grid.attrib['RowSizes'],'Auto 1fr'); self.assertEqual(grid.attrib['ColumnSizes'],'220 2fr')
        value=root.find('.//Input'); self.assertEqual(value.attrib['Value'],'{Binding Title, Mode=TwoWay}'); self.assertEqual(value.attrib['Row'],'1'); self.assertEqual(value.attrib['Column'],'1')
        self.assertEqual(root.find('.//Action').attrib,{'name':'SaveRow','command':'Save','parameter':'{Binding item.ID}'})
        self.assertEqual(root.find('.//Theme').attrib['Name'],'user_preference'); self.assertIsNotNone(root.find('.//Page'))
    def test_independent_scrolling_and_padding(self):
        after,_=migrate('<Cui><ScrollViewer horizontal-scroll-bar-visibility="Auto" vertical-scroll-bar-visibility="Disabled"><Border padding="12" border-thickness="1" border-brush="{Resource CuiLineBrush}"><TextBlock text="Original" /></Border></ScrollViewer></Cui>')
        nodes=ET.fromstring(after).findall('.//Container'); self.assertEqual(nodes[0].attrib['HorizontalScrolling'],'True'); self.assertEqual(nodes[0].attrib['VerticalScrolling'],'False')
        self.assertEqual(nodes[1].attrib['padding'],'12'); self.assertEqual(nodes[1].attrib['BorderWidth'],'1'); self.assertEqual(nodes[1].attrib['BorderColor'],'{Resource CuiLineBrush}')
    def test_menu_is_real_registered_type_not_definition_alias(self):
        after,_=migrate('<Cui><Menu><MenuItem header="File"><MenuItem header="Save" action="Save" /></MenuItem></Menu></Cui>')
        root=ET.fromstring(after); self.assertEqual(root.find('.//Container').attrib['Type'],'Menu')
        item=root.find('.//Button/Button'); self.assertEqual(item.attrib['Type'],'Menu Item'); self.assertEqual(item.attrib['Content'],'Save'); self.assertEqual(item.attrib['action'],'Save')
    def test_repeat_key_and_scoped_input_survive(self):
        after,_=migrate('<Cui><StackPanel><Repeat Source="{Binding Items}" As="item" Key="{Binding item.ID}"><TextBlock text="{Binding item.Title}" /></Repeat></StackPanel></Cui>')
        repeat=ET.fromstring(after).find('.//Repeat'); self.assertEqual(repeat.attrib,{'Source':'{Binding Items}','As':'item','Key':'{Binding item.ID}'})
    def test_canonical_shorthand_not_reparsed_as_xml(self):
        source='<Page><Container Type:Vertical_Stack><Text::name>Hello <:Name:></Text></Container></Page>'
        self.assertEqual(migrate(source)[0],source)
    def test_unknown_private_app_and_native_namespace_refused(self):
        for source in ('<Cui><LegacyApplication /></Cui>','<Cui xmlns:n="clr-namespace:Avalonia.Controls"><n:Grid /></Cui>','<Cui><Button Click="Save_Click" /></Cui>','<Cui><Definition name="Grid"><Container Type="Grid" /></Definition></Cui>'):
            with self.subTest(source=source), self.assertRaises(MigrationError): migrate(source)
    def test_whitespace_and_literal_donor_words_are_content(self):
        source='<Cui><Text Whitespace="Preserve">   </Text><Text Text="Grid.Row and StackPanel are words here" /></Cui>'
        root=ET.fromstring(migrate(source)[0]); self.assertEqual(root.find('.//Text').text,'   '); self.assertEqual(root.findall('.//Text')[1].attrib['Text'],'Grid.Row and StackPanel are words here')
    def test_case_insensitive_builtins_and_idempotence(self):
        first,_=migrate('<cui><grid rows="Auto,*"><textblock text="Hello" /></grid></cui>'); self.assertEqual(migrate(first)[0],first)
    def test_absolute_coordinate_origin_is_never_guessed(self):
        with self.assertRaises(MigrationError):
            migrate('<Cui><Canvas><TextBlock canvas.left="12" canvas.top="34" text="Positioned" /></Canvas></Cui>')
    def test_embedded_csharp_keeps_code_and_literal_delimiters(self):
        source='namespace Existing.Api;\npublic const string Markup = """\n<Cui><TextBlock text="Hi" /></Cui>\n""";\npublic const string Api = "Haven.Core";\n'
        after,_=migrate_csharp_raw_strings(source,'Actual.cs'); self.assertTrue(after.startswith('namespace Existing.Api;')); self.assertTrue(after.endswith('public const string Api = "Haven.Core";\n')); self.assertEqual(after.count('"""'),2); self.assertIn('<Text text="Hi"',after)

if __name__=='__main__': unittest.main()
