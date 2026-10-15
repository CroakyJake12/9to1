import importlib.util
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError

spec = importlib.util.spec_from_file_location('prepare_media', Path(__file__).parents[1] / 'prepare-media.py')
media = importlib.util.module_from_spec(spec)
spec.loader.exec_module(media)


class MediaTests(unittest.TestCase):
    def test_accepts_only_final_desktop_arm64(self):
        names = ['ubuntu-26.04.1-desktop-arm64.iso', 'ubuntu-26.10-beta-desktop-arm64.iso',
                 'ubuntu-26.10-snapshot4-desktop-arm64.iso', 'ubuntu-26.04.1-live-server-arm64.iso',
                 'ubuntu-26.04.1-desktop-amd64.iso', '../ubuntu-26.04.1-desktop-arm64.iso']
        self.assertEqual(list(media.stable_images('\n'.join('a' * 64 + ' *' + n for n in names))), [names[0]])

    def test_unpublished_scheduled_release_falls_back_to_stable(self):
        def fetch(url):
            if url == media.RELEASES:
                return b'<a href="26.10/">new</a><a href="26.04.1/">lts</a>'
            if '/26.10/' in url:
                raise HTTPError(url, 404, 'Not Found', {}, None)
            if url.endswith('.gpg'):
                return b'signature'
            return (('a' * 64) + ' *ubuntu-26.04.1-desktop-arm64.iso\n').encode()
        with patch.object(media, 'fetch', side_effect=fetch):
            self.assertEqual(media.resolve()[1], 'ubuntu-26.04.1-desktop-arm64.iso')

    def test_network_denial_is_not_silently_treated_as_old_release(self):
        with patch.object(media, 'fetch', side_effect=HTTPError('url', 403, 'Forbidden', {}, None)):
            with self.assertRaises(HTTPError):
                media.resolve('26.10')

    def test_invalid_release_refused_before_network(self):
        with patch.object(media, 'fetch') as fetch:
            with self.assertRaises(ValueError):
                media.resolve('../../etc')
            fetch.assert_not_called()
