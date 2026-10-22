"""External installed-package onboarding acceptance. AT-SPI/X11 input and read-only Home state only."""
from pathlib import Path
import datetime
import hashlib
import json
import os
import subprocess
import time
import uuid


class OnboardingDriver:
    CONTROLS = {
        'start': ('onboarding-start', 'Start using 9to1'),
        'signin': ('onboarding-signin-primary', 'Sign in to 9to1'),
        'notnow': ('onboarding-notnow', 'Continue in local-only mode'),
        'continue': ('onboarding-continue', 'Continue without downloading a model'),
    }

    def __init__(self, pid, window, profile, output, label, event, is_running):
        self.pid, self.window = int(pid), window
        self.profile, self.output = Path(profile), Path(output)
        self.label, self.event, self.is_running = label, event, is_running
        self.sequence = 0

    @staticmethod
    def command(args, timeout=10):
        return subprocess.run(args, capture_output=True, text=True, check=True, timeout=timeout)

    def observe(self):
        if not self.is_running():
            raise RuntimeError('Owned app exited during actual onboarding')
        observer = Path(__file__).resolve().with_name('desktop-observe.py')
        result = self.command(['/usr/bin/python3', str(observer), '--pid', str(self.pid)], 8)
        data = json.loads(result.stdout)
        # Unknown PID is never accepted as proof that a similarly named desktop control belongs
        # to this package. The observer reports this as an explicit accessibility blocker.
        return [row for row in data.get('rows', []) if row.get('process_id') == self.pid and row.get('showing')]

    def checkpoint(self, stage, rows):
        self.sequence += 1
        stem = self.output / (self.label + '-onboarding-' + str(self.sequence) + '-' + stage)
        stem.with_suffix('.accessibility.json').write_text(json.dumps(rows, indent=2) + '\n')
        self.command(['import', '-window', 'root', str(stem.with_suffix('.png'))])
        for suffix, args in [('windows.txt', ['wmctrl', '-lpG']),
                             ('window-tree.txt', ['xwininfo', '-root', '-tree']),
                             ('window-properties.txt', ['xprop', '-id', self.window])]:
            result = subprocess.run(args, capture_output=True, text=True, timeout=8)
            Path(str(stem) + '.' + suffix).write_text(result.stdout + '\nSTDERR:\n' + result.stderr)
        self.event('ONBOARDING_CHECKPOINT', stage=stage, evidence=str(stem), owned_pid=self.pid,
                   home_kind=self.home_kind(rows), dashboard_acceptance='NOT CLAIMED')

    def control(self, rows, key):
        identifier, name = self.CONTROLS[key]
        matches = [row for row in rows if row.get('enabled') and 'button' in row.get('role', '').casefold()
                   and (row.get('accessible_id') == identifier or row.get('name', '').casefold() == name.casefold())]
        if len(matches) > 1:
            raise RuntimeError('Ambiguous actual onboarding control: ' + identifier)
        return matches[0] if matches else None

    @staticmethod
    def home_kind(rows):
        visible = [row for row in rows if row.get('showing') and row.get('enabled')
                   and row.get('rectangle') and row['rectangle'][2] > 0 and row['rectangle'][3] > 0]
        entries = {row.get('name', '').casefold() for row in visible if row.get('role') == 'entry'}
        buttons = {row.get('name', '').casefold() for row in visible if 'button' in row.get('role', '').casefold()}
        if 'search or enter web address' not in entries:
            return None
        # The canonical catalogue owns Build and Spaces. Chat is a Space, not a
        # separately navigable app; requiring its retired tile caused all three
        # installed-package journeys to reject a genuinely rendered Home.
        if 'search apps' in entries and {'open build', 'open spaces', 'open files', 'open write'}.issubset(buttons):
            return 'APPS_HOME'
        if {'edit dashboard page', 'open home discover model catalogue'}.issubset(buttons):
            return 'DASHBOARD_HOME'
        return None

    @staticmethod
    def home(rows):
        return bool(OnboardingDriver.home_kind(rows))

    def wait(self, predicate, stage, timeout=180):
        end = time.monotonic() + timeout
        rows = []
        while time.monotonic() < end:
            rows = self.observe()
            value = predicate(rows)
            if value:
                self.checkpoint(stage, rows)
                return value
            time.sleep(.5)
        self.checkpoint(stage + '-NOT-OBSERVED', rows)
        raise LookupError('Actual owned accessible onboarding state unavailable: ' + stage)

    def click(self, row, stage):
        rectangle = row.get('rectangle')
        if not rectangle or len(rectangle) != 4 or rectangle[2] <= 0 or rectangle[3] <= 0:
            raise LookupError('Actual onboarding control has no visible bounds: ' + stage)
        self.command(['wmctrl', '-ia', self.window])
        self.command(['xdotool', 'mousemove', str(rectangle[0] + rectangle[2] // 2),
                      str(rectangle[1] + rectangle[3] // 2), 'click', '1'])
        self.event('ACTUAL_ONBOARDING_CLICK', stage=stage, owned_pid=self.pid,
                   name=row.get('name'), accessible_id=row.get('accessible_id'), rectangle=rectangle)

    def saved(self, require_completion=True, expected=None):
        # This is the production App.OriginalWindowsHome.cs store path beneath HAVEN_DATA_DIR.
        path = self.profile / 'Home' / 'state.json'
        data = path.read_bytes()  # never opened for writing; atomic production replacement remains intact
        state = json.loads(data)
        assert state['SchemaVersion'] == 1
        records = state['Records']
        identities = [record for record in records if record['RecordId'] == 'home.local-profile']
        assert len(identities) == 1
        identity = identities[0]
        assert identity['RecordType'] == 'home.local-profile' and identity['SchemaVersion'] == 1
        assert identity['Scope'] in (0, 'DeviceLocal') and identity['Authority'] in (0, 'LocalCanonical')
        profile = identity['Payload']
        profile_id = str(uuid.UUID(profile['ProfileId']))
        assert profile_id != str(uuid.UUID(int=0))
        assert profile['PrincipalDigest'] == hashlib.sha256(('unix-euid:' + str(os.geteuid())).encode()).hexdigest().upper()
        completion_records = [record for record in records if record['RecordId'] == 'home.onboarding.' + profile_id]
        assert len(completion_records) <= 1
        completion = completion_records[0] if completion_records else None
        if require_completion:
            assert completion is not None, 'Actual Home appeared without persisted profile completion'
        if completion is not None:
            assert completion['RecordType'] == 'home.onboarding' and completion['SchemaVersion'] == 1
            assert completion['Scope'] in (0, 'DeviceLocal') and completion['Authority'] in (0, 'LocalCanonical')
            assert completion['Revision'] >= 1
            payload = completion['Payload']
            assert payload['SchemaVersion'] == 1 and payload['ProfileId'] == profile_id
            assert payload['SignedIn'] is False  # this driver actually selects Not Now
            assert isinstance(payload['ModelEligible'], bool)
            stamp = datetime.datetime.fromisoformat(payload['CompletedAtUtc'].replace('Z', '+00:00'))
            assert stamp.tzinfo is not None and stamp.year >= 2026
        summary = {'store_path': str(path), 'store_sha256': hashlib.sha256(data).hexdigest(),
                   'profile_id': profile_id, 'os_principal_verified': True,
                   'completion': completion, 'state_revision': state['Revision']}
        if expected is not None:
            assert profile_id == expected['profile_id'], 'Restart changed the actual local profile'
            assert completion == expected['completion'], 'Returning-profile bypass rewrote completion'
        self.output.joinpath(self.label + '-onboarding-saved-state.json').write_text(json.dumps(summary, indent=2) + '\n')
        self.event('ACTUAL_ONBOARDING_STATE_READ', profile_id=profile_id, store_sha256=summary['store_sha256'],
                   completion_present=completion is not None, mode='READ_ONLY')
        return summary

    def fresh(self):
        def welcome(rows):
            if self.home(rows):
                raise RuntimeError('Fresh profile bypassed the required welcome interaction')
            return self.control(rows, 'start')
        start = self.wait(welcome, 'welcome')
        before = self.saved(require_completion=False)
        assert before['completion'] is None, 'Fresh welcome already has a completion record'
        self.click(start, 'Start')
        def signin(rows):
            return self.control(rows, 'notnow') if self.control(rows, 'signin') else None
        notnow = self.wait(signin, 'sign-in-decision')
        self.click(notnow, 'Not Now')
        def decision(rows):
            button = self.control(rows, 'continue')
            return ('download', button) if button else ('eligible-auto-home', None) if self.home(rows) else None
        branch, button = self.wait(decision, 'model-decision')
        if button is not None:
            self.click(button, 'Continue without downloading')
            self.wait(self.home, 'home-after-explicit-continue')
        saved = self.saved()
        if branch == 'eligible-auto-home':
            assert saved['completion']['Payload']['ModelEligible'] is True, 'Download UI skipped without eligible-model evidence'
        assert saved['profile_id'] == before['profile_id']
        self.event('ACTUAL_FRESH_ONBOARDING_COMPLETE', branch=branch, profile_id=saved['profile_id'],
                   home_kind=self.home_kind(self.observe()), dashboard_acceptance='NOT CLAIMED',
                   observed_actions=['Start', 'Not Now'] + (['Continue without downloading'] if button else []))
        return saved

    def returning(self, expected):
        self.saved(expected=expected)
        def bypass(rows):
            if any(self.control(rows, key) for key in self.CONTROLS):
                self.checkpoint('returning-profile-unexpected-onboarding', rows)
                raise RuntimeError('Returning same profile displayed onboarding controls')
            return self.home(rows)
        self.wait(bypass, 'same-profile-direct-home')
        saved = self.saved(expected=expected)
        self.event('ACTUAL_SAME_PROFILE_ONBOARDING_BYPASS', profile_id=saved['profile_id'],
                   home_kind=self.home_kind(self.observe()), dashboard_acceptance='NOT CLAIMED')
        return saved
