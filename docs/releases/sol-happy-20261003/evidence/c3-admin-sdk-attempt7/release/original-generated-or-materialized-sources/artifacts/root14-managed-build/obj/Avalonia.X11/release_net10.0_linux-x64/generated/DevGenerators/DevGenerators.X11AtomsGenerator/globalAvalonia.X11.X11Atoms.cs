using System;
using static Avalonia.X11.XLib;
namespace Avalonia.X11;
partial class X11Atoms
{
    private void PopulateAtoms(IntPtr display)
    {
        SetName("PRIMARY", 1);
        SetName("SECONDARY", 2);
        SetName("ARC", 3);
        SetName("ATOM", 4);
        SetName("BITMAP", 5);
        SetName("CARDINAL", 6);
        SetName("COLORMAP", 7);
        SetName("CURSOR", 8);
        SetName("CUT_BUFFER0", 9);
        SetName("CUT_BUFFER1", 10);
        SetName("CUT_BUFFER2", 11);
        SetName("CUT_BUFFER3", 12);
        SetName("CUT_BUFFER4", 13);
        SetName("CUT_BUFFER5", 14);
        SetName("CUT_BUFFER6", 15);
        SetName("CUT_BUFFER7", 16);
        SetName("DRAWABLE", 17);
        SetName("FONT", 18);
        SetName("INTEGER", 19);
        SetName("PIXMAP", 20);
        SetName("POINT", 21);
        SetName("RECTANGLE", 22);
        SetName("RESOURCE_MANAGER", 23);
        SetName("RGB_COLOR_MAP", 24);
        SetName("RGB_BEST_MAP", 25);
        SetName("RGB_BLUE_MAP", 26);
        SetName("RGB_DEFAULT_MAP", 27);
        SetName("RGB_GRAY_MAP", 28);
        SetName("RGB_GREEN_MAP", 29);
        SetName("RGB_RED_MAP", 30);
        SetName("STRING", 31);
        SetName("VISUALID", 32);
        SetName("WINDOW", 33);
        SetName("WM_COMMAND", 34);
        SetName("WM_HINTS", 35);
        SetName("WM_CLIENT_MACHINE", 36);
        SetName("WM_ICON_NAME", 37);
        SetName("WM_ICON_SIZE", 38);
        SetName("WM_NAME", 39);
        SetName("WM_NORMAL_HINTS", 40);
        SetName("WM_SIZE_HINTS", 41);
        SetName("WM_ZOOM_HINTS", 42);
        SetName("MIN_SPACE", 43);
        SetName("NORM_SPACE", 44);
        SetName("MAX_SPACE", 45);
        SetName("END_SPACE", 46);
        SetName("SUPERSCRIPT_X", 47);
        SetName("SUPERSCRIPT_Y", 48);
        SetName("SUBSCRIPT_X", 49);
        SetName("SUBSCRIPT_Y", 50);
        SetName("UNDERLINE_POSITION", 51);
        SetName("UNDERLINE_THICKNESS", 52);
        SetName("STRIKEOUT_ASCENT", 53);
        SetName("STRIKEOUT_DESCENT", 54);
        SetName("ITALIC_ANGLE", 55);
        SetName("X_HEIGHT", 56);
        SetName("QUAD_WIDTH", 57);
        SetName("WEIGHT", 58);
        SetName("POINT_SIZE", 59);
        SetName("RESOLUTION", 60);
        SetName("COPYRIGHT", 61);
        SetName("NOTICE", 62);
        SetName("FONT_NAME", 63);
        SetName("FAMILY_NAME", 64);
        SetName("FULL_NAME", 65);
        SetName("CAP_HEIGHT", 66);
        SetName("WM_CLASS", 67);
        SetName("WM_TRANSIENT_FOR", 68);
        var atoms = new IntPtr[82];
        var atomNames = new string[82] {
            "EDID",
            "WM_PROTOCOLS",
            "WM_DELETE_WINDOW",
            "WM_TAKE_FOCUS",
            "_NET_SUPPORTED",
            "_NET_CLIENT_LIST",
            "_NET_NUMBER_OF_DESKTOPS",
            "_NET_DESKTOP_GEOMETRY",
            "_NET_DESKTOP_VIEWPORT",
            "_NET_CURRENT_DESKTOP",
            "_NET_DESKTOP_NAMES",
            "_NET_ACTIVE_WINDOW",
            "_NET_WORKAREA",
            "_NET_SUPPORTING_WM_CHECK",
            "_NET_VIRTUAL_ROOTS",
            "_NET_DESKTOP_LAYOUT",
            "_NET_SHOWING_DESKTOP",
            "_NET_CLOSE_WINDOW",
            "_NET_MOVERESIZE_WINDOW",
            "_NET_WM_MOVERESIZE",
            "_NET_RESTACK_WINDOW",
            "_NET_REQUEST_FRAME_EXTENTS",
            "_NET_WM_NAME",
            "_NET_WM_VISIBLE_NAME",
            "_NET_WM_ICON_NAME",
            "_NET_WM_VISIBLE_ICON_NAME",
            "_NET_WM_DESKTOP",
            "_NET_WM_WINDOW_TYPE",
            "_NET_WM_STATE",
            "_NET_WM_ALLOWED_ACTIONS",
            "_NET_WM_ACTION_MAXIMIZE_VERT",
            "_NET_WM_ACTION_MAXIMIZE_HORZ",
            "_NET_WM_ACTION_FULLSCREEN",
            "_NET_WM_ACTION_MINIMIZE",
            "_NET_WM_STRUT",
            "_NET_WM_STRUT_PARTIAL",
            "_NET_WM_ICON_GEOMETRY",
            "_NET_WM_ICON",
            "_NET_WM_PID",
            "_NET_WM_HANDLED_ICONS",
            "_NET_WM_USER_TIME",
            "_NET_FRAME_EXTENTS",
            "_NET_WM_PING",
            "_NET_WM_SYNC_REQUEST",
            "_NET_WM_SYNC_REQUEST_COUNTER",
            "_NET_SYSTEM_TRAY_S",
            "_NET_SYSTEM_TRAY_ORIENTATION",
            "_NET_SYSTEM_TRAY_OPCODE",
            "_NET_WM_STATE_MAXIMIZED_HORZ",
            "_NET_WM_STATE_MAXIMIZED_VERT",
            "_NET_WM_STATE_FULLSCREEN",
            "_XEMBED",
            "_XEMBED_INFO",
            "_MOTIF_WM_HINTS",
            "_NET_WM_STATE_SKIP_TASKBAR",
            "_NET_WM_STATE_ABOVE",
            "_NET_WM_STATE_MODAL",
            "_NET_WM_STATE_HIDDEN",
            "_NET_WM_CONTEXT_HELP",
            "_NET_WM_WINDOW_OPACITY",
            "_NET_WM_WINDOW_TYPE_DESKTOP",
            "_NET_WM_WINDOW_TYPE_DOCK",
            "_NET_WM_WINDOW_TYPE_TOOLBAR",
            "_NET_WM_WINDOW_TYPE_MENU",
            "_NET_WM_WINDOW_TYPE_UTILITY",
            "_NET_WM_WINDOW_TYPE_SPLASH",
            "_NET_WM_WINDOW_TYPE_DIALOG",
            "_NET_WM_WINDOW_TYPE_NORMAL",
            "CLIPBOARD",
            "CLIPBOARD_MANAGER",
            "SAVE_TARGETS",
            "MULTIPLE",
            "OEMTEXT",
            "UNICODETEXT",
            "TARGETS",
            "UTF8_STRING",
            "UTF16_STRING",
            "ATOM_PAIR",
            "MANAGER",
            "_KDE_NET_WM_BLUR_BEHIND_REGION",
            "INCR",
            "_NET_WM_STATE_FOCUSED",
        };
        XInternAtoms(display, atomNames, atomNames.Length, false, atoms);
        InitAtom(ref EDID, "EDID", atoms[0]);
        InitAtom(ref WM_PROTOCOLS, "WM_PROTOCOLS", atoms[1]);
        InitAtom(ref WM_DELETE_WINDOW, "WM_DELETE_WINDOW", atoms[2]);
        InitAtom(ref WM_TAKE_FOCUS, "WM_TAKE_FOCUS", atoms[3]);
        InitAtom(ref _NET_SUPPORTED, "_NET_SUPPORTED", atoms[4]);
        InitAtom(ref _NET_CLIENT_LIST, "_NET_CLIENT_LIST", atoms[5]);
        InitAtom(ref _NET_NUMBER_OF_DESKTOPS, "_NET_NUMBER_OF_DESKTOPS", atoms[6]);
        InitAtom(ref _NET_DESKTOP_GEOMETRY, "_NET_DESKTOP_GEOMETRY", atoms[7]);
        InitAtom(ref _NET_DESKTOP_VIEWPORT, "_NET_DESKTOP_VIEWPORT", atoms[8]);
        InitAtom(ref _NET_CURRENT_DESKTOP, "_NET_CURRENT_DESKTOP", atoms[9]);
        InitAtom(ref _NET_DESKTOP_NAMES, "_NET_DESKTOP_NAMES", atoms[10]);
        InitAtom(ref _NET_ACTIVE_WINDOW, "_NET_ACTIVE_WINDOW", atoms[11]);
        InitAtom(ref _NET_WORKAREA, "_NET_WORKAREA", atoms[12]);
        InitAtom(ref _NET_SUPPORTING_WM_CHECK, "_NET_SUPPORTING_WM_CHECK", atoms[13]);
        InitAtom(ref _NET_VIRTUAL_ROOTS, "_NET_VIRTUAL_ROOTS", atoms[14]);
        InitAtom(ref _NET_DESKTOP_LAYOUT, "_NET_DESKTOP_LAYOUT", atoms[15]);
        InitAtom(ref _NET_SHOWING_DESKTOP, "_NET_SHOWING_DESKTOP", atoms[16]);
        InitAtom(ref _NET_CLOSE_WINDOW, "_NET_CLOSE_WINDOW", atoms[17]);
        InitAtom(ref _NET_MOVERESIZE_WINDOW, "_NET_MOVERESIZE_WINDOW", atoms[18]);
        InitAtom(ref _NET_WM_MOVERESIZE, "_NET_WM_MOVERESIZE", atoms[19]);
        InitAtom(ref _NET_RESTACK_WINDOW, "_NET_RESTACK_WINDOW", atoms[20]);
        InitAtom(ref _NET_REQUEST_FRAME_EXTENTS, "_NET_REQUEST_FRAME_EXTENTS", atoms[21]);
        InitAtom(ref _NET_WM_NAME, "_NET_WM_NAME", atoms[22]);
        InitAtom(ref _NET_WM_VISIBLE_NAME, "_NET_WM_VISIBLE_NAME", atoms[23]);
        InitAtom(ref _NET_WM_ICON_NAME, "_NET_WM_ICON_NAME", atoms[24]);
        InitAtom(ref _NET_WM_VISIBLE_ICON_NAME, "_NET_WM_VISIBLE_ICON_NAME", atoms[25]);
        InitAtom(ref _NET_WM_DESKTOP, "_NET_WM_DESKTOP", atoms[26]);
        InitAtom(ref _NET_WM_WINDOW_TYPE, "_NET_WM_WINDOW_TYPE", atoms[27]);
        InitAtom(ref _NET_WM_STATE, "_NET_WM_STATE", atoms[28]);
        InitAtom(ref _NET_WM_ALLOWED_ACTIONS, "_NET_WM_ALLOWED_ACTIONS", atoms[29]);
        InitAtom(ref _NET_WM_ACTION_MAXIMIZE_VERT, "_NET_WM_ACTION_MAXIMIZE_VERT", atoms[30]);
        InitAtom(ref _NET_WM_ACTION_MAXIMIZE_HORZ, "_NET_WM_ACTION_MAXIMIZE_HORZ", atoms[31]);
        InitAtom(ref _NET_WM_ACTION_FULLSCREEN, "_NET_WM_ACTION_FULLSCREEN", atoms[32]);
        InitAtom(ref _NET_WM_ACTION_MINIMIZE, "_NET_WM_ACTION_MINIMIZE", atoms[33]);
        InitAtom(ref _NET_WM_STRUT, "_NET_WM_STRUT", atoms[34]);
        InitAtom(ref _NET_WM_STRUT_PARTIAL, "_NET_WM_STRUT_PARTIAL", atoms[35]);
        InitAtom(ref _NET_WM_ICON_GEOMETRY, "_NET_WM_ICON_GEOMETRY", atoms[36]);
        InitAtom(ref _NET_WM_ICON, "_NET_WM_ICON", atoms[37]);
        InitAtom(ref _NET_WM_PID, "_NET_WM_PID", atoms[38]);
        InitAtom(ref _NET_WM_HANDLED_ICONS, "_NET_WM_HANDLED_ICONS", atoms[39]);
        InitAtom(ref _NET_WM_USER_TIME, "_NET_WM_USER_TIME", atoms[40]);
        InitAtom(ref _NET_FRAME_EXTENTS, "_NET_FRAME_EXTENTS", atoms[41]);
        InitAtom(ref _NET_WM_PING, "_NET_WM_PING", atoms[42]);
        InitAtom(ref _NET_WM_SYNC_REQUEST, "_NET_WM_SYNC_REQUEST", atoms[43]);
        InitAtom(ref _NET_WM_SYNC_REQUEST_COUNTER, "_NET_WM_SYNC_REQUEST_COUNTER", atoms[44]);
        InitAtom(ref _NET_SYSTEM_TRAY_S, "_NET_SYSTEM_TRAY_S", atoms[45]);
        InitAtom(ref _NET_SYSTEM_TRAY_ORIENTATION, "_NET_SYSTEM_TRAY_ORIENTATION", atoms[46]);
        InitAtom(ref _NET_SYSTEM_TRAY_OPCODE, "_NET_SYSTEM_TRAY_OPCODE", atoms[47]);
        InitAtom(ref _NET_WM_STATE_MAXIMIZED_HORZ, "_NET_WM_STATE_MAXIMIZED_HORZ", atoms[48]);
        InitAtom(ref _NET_WM_STATE_MAXIMIZED_VERT, "_NET_WM_STATE_MAXIMIZED_VERT", atoms[49]);
        InitAtom(ref _NET_WM_STATE_FULLSCREEN, "_NET_WM_STATE_FULLSCREEN", atoms[50]);
        InitAtom(ref _XEMBED, "_XEMBED", atoms[51]);
        InitAtom(ref _XEMBED_INFO, "_XEMBED_INFO", atoms[52]);
        InitAtom(ref _MOTIF_WM_HINTS, "_MOTIF_WM_HINTS", atoms[53]);
        InitAtom(ref _NET_WM_STATE_SKIP_TASKBAR, "_NET_WM_STATE_SKIP_TASKBAR", atoms[54]);
        InitAtom(ref _NET_WM_STATE_ABOVE, "_NET_WM_STATE_ABOVE", atoms[55]);
        InitAtom(ref _NET_WM_STATE_MODAL, "_NET_WM_STATE_MODAL", atoms[56]);
        InitAtom(ref _NET_WM_STATE_HIDDEN, "_NET_WM_STATE_HIDDEN", atoms[57]);
        InitAtom(ref _NET_WM_CONTEXT_HELP, "_NET_WM_CONTEXT_HELP", atoms[58]);
        InitAtom(ref _NET_WM_WINDOW_OPACITY, "_NET_WM_WINDOW_OPACITY", atoms[59]);
        InitAtom(ref _NET_WM_WINDOW_TYPE_DESKTOP, "_NET_WM_WINDOW_TYPE_DESKTOP", atoms[60]);
        InitAtom(ref _NET_WM_WINDOW_TYPE_DOCK, "_NET_WM_WINDOW_TYPE_DOCK", atoms[61]);
        InitAtom(ref _NET_WM_WINDOW_TYPE_TOOLBAR, "_NET_WM_WINDOW_TYPE_TOOLBAR", atoms[62]);
        InitAtom(ref _NET_WM_WINDOW_TYPE_MENU, "_NET_WM_WINDOW_TYPE_MENU", atoms[63]);
        InitAtom(ref _NET_WM_WINDOW_TYPE_UTILITY, "_NET_WM_WINDOW_TYPE_UTILITY", atoms[64]);
        InitAtom(ref _NET_WM_WINDOW_TYPE_SPLASH, "_NET_WM_WINDOW_TYPE_SPLASH", atoms[65]);
        InitAtom(ref _NET_WM_WINDOW_TYPE_DIALOG, "_NET_WM_WINDOW_TYPE_DIALOG", atoms[66]);
        InitAtom(ref _NET_WM_WINDOW_TYPE_NORMAL, "_NET_WM_WINDOW_TYPE_NORMAL", atoms[67]);
        InitAtom(ref CLIPBOARD, "CLIPBOARD", atoms[68]);
        InitAtom(ref CLIPBOARD_MANAGER, "CLIPBOARD_MANAGER", atoms[69]);
        InitAtom(ref SAVE_TARGETS, "SAVE_TARGETS", atoms[70]);
        InitAtom(ref MULTIPLE, "MULTIPLE", atoms[71]);
        InitAtom(ref OEMTEXT, "OEMTEXT", atoms[72]);
        InitAtom(ref UNICODETEXT, "UNICODETEXT", atoms[73]);
        InitAtom(ref TARGETS, "TARGETS", atoms[74]);
        InitAtom(ref UTF8_STRING, "UTF8_STRING", atoms[75]);
        InitAtom(ref UTF16_STRING, "UTF16_STRING", atoms[76]);
        InitAtom(ref ATOM_PAIR, "ATOM_PAIR", atoms[77]);
        InitAtom(ref MANAGER, "MANAGER", atoms[78]);
        InitAtom(ref _KDE_NET_WM_BLUR_BEHIND_REGION, "_KDE_NET_WM_BLUR_BEHIND_REGION", atoms[79]);
        InitAtom(ref INCR, "INCR", atoms[80]);
        InitAtom(ref _NET_WM_STATE_FOCUSED, "_NET_WM_STATE_FOCUSED", atoms[81]);
    }
}
