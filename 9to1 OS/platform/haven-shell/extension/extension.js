/* Haven Shell extension source (Worker A).
 *
 * System-wide GNOME Shell treatment for HavenOS. Adds a visible Haven
 * top-bar indicator and a Haven panel style class. Fail-closed: no
 * session override, no settings daemon dependency, no wallpaper
 * dependency, no network access.
 *
 * Install source (Workers D/E): copy this directory to
 * /usr/share/gnome-shell/extensions/haven-shell@havenos/.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 */

import Gio from 'gi://Gio';
import GObject from 'gi://GObject';
import St from 'gi://St';

import {Extension, gettext as _} from 'resource:///org/gnome/shell/extensions/extension.js';
import * as PanelMenu from 'resource:///org/gnome/shell/ui/panelMenu.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';

function launchDesktopEntry(desktopId) {
    const appInfo = Gio.DesktopAppInfo.new(desktopId);
    if (!appInfo) {
        console.error(`Haven Shell could not find ${desktopId}`);
        return;
    }

    try {
        appInfo.launch([], null);
    } catch (error) {
        console.error(`Haven Shell could not launch ${desktopId}: ${error}`);
    }
}

const HavenIndicator = GObject.registerClass(
class HavenIndicator extends PanelMenu.Button {
    _init() {
        super._init(0.0, _('Haven'));

        this.add_style_class_name('haven-indicator');

        const box = new St.BoxLayout({style_class: 'haven-indicator-box'});
        const dot = new St.Widget({style_class: 'haven-indicator-dot'});
        const label = new St.Label({
            text: _('Haven'),
            style_class: 'haven-indicator-label',
        });

        box.add_child(dot);
        box.add_child(label);
        this.add_child(box);

        const item = new PopupMenu.PopupMenuItem(_('Haven session active'));
        item.setOrnament(PopupMenu.Ornament.NONE);
        item.reactive = false;
        this.menu.addMenuItem(item);

        this.menu.addMenuItem(new PopupMenu.PopupSeparatorMenuItem());
        for (const [label, desktopId] of [
            [_('Open Haven Welcome'), 'haven-welcome.desktop'],
            [_('Open HavenOS Studio'), 'havenos-studio.desktop'],
        ]) {
            const launcher = new PopupMenu.PopupMenuItem(label);
            launcher.connect('activate', () => launchDesktopEntry(desktopId));
            this.menu.addMenuItem(launcher);
        }
    }
});

export default class HavenShellExtension extends Extension {
    enable() {
        this._indicator = new HavenIndicator();
        Main.panel.addToStatusArea(this.uuid, this._indicator, 0, 'left');
        Main.panel.add_style_class_name('haven-panel');
    }

    disable() {
        if (this._indicator) {
            this._indicator.destroy();
            this._indicator = null;
        }
        Main.panel.remove_style_class_name('haven-panel');
    }
}
