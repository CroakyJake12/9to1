"""No disks are accessed: exercise the storage plan and rejection boundaries."""
import copy
from pathlib import Path
import unittest
from common import UBUNTU_BYTES, check_original, storage_plan
from prepare import REQUIRED, accepted, installer_config
from finish import installed_menu

TYPES = ['c12a7328-f81f-11d2-ba4b-00a0c93ec93b','e3c9e316-0b5c-4db8-817d-f92df00215ae',
         'ebd0a0a2-b9e5-4433-87c0-68b6b72699c7','de94bba4-06d1-4d40-a16a-bfd50179d6ac',
         'de94bba4-06d1-4d40-a16a-bfd50179d6ac']
LAYOUT = [(1048576,272629760),(273678336,16777216),(290455552,490488201216),
          (490778656768,891289600),(491669946368,20193476608)]
ORIGINAL = [dict(PartitionNumber=n,Offset=o,Size=s,Guid=f'00000000-0000-0000-0000-{n:012d}',GptType=TYPES[n-1])
            for n,(o,s) in enumerate(LAYOUT,1)]


def records():
    return {p['PartitionNumber']:dict(offset=p['Offset'],size=p['Size'],guid=p['Guid'],partition_type=p['GptType']) for p in ORIGINAL}


class Guards(unittest.TestCase):
    def test_only_new_root_is_formatted(self):
        plan = storage_plan('/dev/sdz',ORIGINAL)
        formats = [p for p in plan if p['type']=='format' and not p['preserve']]
        self.assertEqual([(p['volume'],p['fstype']) for p in formats],[('ubuntu','ext4')])
        self.assertTrue(plan[0]['preserve'])
        existing = [p for p in plan if p['type']=='partition' and p['preserve']]
        self.assertEqual(len(existing),5)
        for part,old in zip(existing,ORIGINAL):
            self.assertEqual(part['partition_type'],old['GptType'])
            self.assertEqual(part['uuid'],old['Guid'])
            self.assertEqual(part['offset'],old['Offset'])
        new = next(p for p in plan if p['id']=='ubuntu')
        resized = next(p for p in plan if p['id']=='p3')
        self.assertEqual(new['size'],UBUNTU_BYTES)
        self.assertEqual(new['offset'],resized['offset']+resized['size'])
        self.assertEqual(new['offset']+new['size'],ORIGINAL[3]['Offset'])

    def test_changed_disk_layout_rejected(self):
        check_original(records(),ORIGINAL)
        for field,value in [('size',1024),('offset',0),('guid','different'),('partition_type',TYPES[0])]:
            data=records();data[3][field]=value
            with self.assertRaises(ValueError): check_original(data,ORIGINAL)
        data=records();data.pop(5)
        with self.assertRaises(ValueError): check_original(data,ORIGINAL)

    def test_new_root_exact_bounds(self):
        data=records();data[3]['size']-=UBUNTU_BYTES
        data[6]=dict(offset=data[3]['offset']+data[3]['size'],size=UBUNTU_BYTES)
        check_original(data,ORIGINAL,True)
        data[6]['offset']+=1048576
        with self.assertRaises(ValueError): check_original(data,ORIGINAL,True)

    def test_current_boot_manual_acceptance_required(self):
        report=dict(bootId='current',checks={k:dict(status='passed') for k in REQUIRED})
        accepted(report,'current')
        with self.assertRaises(ValueError): accepted(report,'different')
        for status in ('failed','not tested','blocked'):
            failed=copy.deepcopy(report);failed['checks']['camera']['status']=status
            with self.assertRaises(ValueError): accepted(failed,'current')

    def test_config_does_not_request_boot_order_changes(self):
        cfg=installer_config('/dev/sdz',ORIGINAL,Path('/verified-usb/backup'))['autoinstall']
        self.assertFalse(cfg['storage']['grub']['update_nvram'])
        self.assertFalse(cfg['storage']['grub']['reorder_uefi'])
        self.assertEqual(cfg['interactive-sections'],['identity'])
        self.assertNotIn('identity',cfg)

    def test_installed_menu_has_no_live_wrapper(self):
        menu=installed_menu('00000000-0000-0000-0000-000000000001','ABCD-1234')
        self.assertNotIn('casper',menu)
        self.assertNotIn('book4edge-init',menu)
        windows=menu[menu.index('menuentry "Windows"'):]
        self.assertNotIn('cutmem',windows)
        self.assertNotIn('devicetree',windows)
        with self.assertRaises(ValueError): installed_menu('invalid','ABCD-1234')


if __name__=='__main__':
    unittest.main()
