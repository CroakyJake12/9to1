#pragma warning disable 108
// ReSharper disable RedundantUsingDirective
// ReSharper disable JoinDeclarationAndInitializer
// ReSharper disable ArrangeTypeMemberModifiers
// ReSharper disable UnusedType.Local
// ReSharper disable InconsistentNaming
// ReSharper disable RedundantNameQualifier
// ReSharper disable RedundantCast
// ReSharper disable IdentifierTypo
// ReSharper disable PartialTypeWithSinglePart
// ReSharper disable RedundantUnsafeContext
// ReSharper disable RedundantBaseQualifier
// ReSharper disable EmptyStatement
// ReSharper disable RedundantAttributeParentheses
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable FieldCanBeMadeReadOnly.Global
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using MicroCom.Runtime;

namespace Avalonia.Native.Interop
{
    internal enum AvnKey
    {
        AvnKeyNone = 0,
        AvnKeyCancel = 1,
        AvnKeyBack = 2,
        AvnKeyTab = 3,
        AvnKeyLineFeed = 4,
        AvnKeyClear = 5,
        AvnKeyReturn = 6,
        AvnKeyEnter = 6,
        AvnKeyPause = 7,
        AvnKeyCapsLock = 8,
        AvnKeyCapital = 8,
        AvnKeyHangulMode = 9,
        AvnKeyKanaMode = 9,
        AvnKeyJunjaMode = 10,
        AvnKeyFinalMode = 11,
        AvnKeyKanjiMode = 12,
        AvnKeyHanjaMode = 12,
        AvnKeyEscape = 13,
        AvnKeyImeConvert = 14,
        AvnKeyImeNonConvert = 15,
        AvnKeyImeAccept = 16,
        AvnKeyImeModeChange = 17,
        AvnKeySpace = 18,
        AvnKeyPageUp = 19,
        AvnKeyPrior = 19,
        AvnKeyPageDown = 20,
        AvnKeyNext = 20,
        AvnKeyEnd = 21,
        AvnKeyHome = 22,
        AvnKeyLeft = 23,
        AvnKeyUp = 24,
        AvnKeyRight = 25,
        AvnKeyDown = 26,
        AvnKeySelect = 27,
        AvnKeyPrint = 28,
        AvnKeyExecute = 29,
        AvnKeySnapshot = 30,
        AvnKeyPrintScreen = 30,
        AvnKeyInsert = 31,
        AvnKeyDelete = 32,
        AvnKeyHelp = 33,
        AvnKeyD0 = 34,
        AvnKeyD1 = 35,
        AvnKeyD2 = 36,
        AvnKeyD3 = 37,
        AvnKeyD4 = 38,
        AvnKeyD5 = 39,
        AvnKeyD6 = 40,
        AvnKeyD7 = 41,
        AvnKeyD8 = 42,
        AvnKeyD9 = 43,
        AvnKeyA = 44,
        AvnKeyB = 45,
        AvnKeyC = 46,
        AvnKeyD = 47,
        AvnKeyE = 48,
        AvnKeyF = 49,
        AvnKeyG = 50,
        AvnKeyH = 51,
        AvnKeyI = 52,
        AvnKeyJ = 53,
        AvnKeyK = 54,
        AvnKeyL = 55,
        AvnKeyM = 56,
        AvnKeyN = 57,
        AvnKeyO = 58,
        AvnKeyP = 59,
        AvnKeyQ = 60,
        AvnKeyR = 61,
        AvnKeyS = 62,
        AvnKeyT = 63,
        AvnKeyU = 64,
        AvnKeyV = 65,
        AvnKeyW = 66,
        AvnKeyX = 67,
        AvnKeyY = 68,
        AvnKeyZ = 69,
        AvnKeyLWin = 70,
        AvnKeyRWin = 71,
        AvnKeyApps = 72,
        AvnKeySleep = 73,
        AvnKeyNumPad0 = 74,
        AvnKeyNumPad1 = 75,
        AvnKeyNumPad2 = 76,
        AvnKeyNumPad3 = 77,
        AvnKeyNumPad4 = 78,
        AvnKeyNumPad5 = 79,
        AvnKeyNumPad6 = 80,
        AvnKeyNumPad7 = 81,
        AvnKeyNumPad8 = 82,
        AvnKeyNumPad9 = 83,
        AvnKeyMultiply = 84,
        AvnKeyAdd = 85,
        AvnKeySeparator = 86,
        AvnKeySubtract = 87,
        AvnKeyDecimal = 88,
        AvnKeyDivide = 89,
        AvnKeyF1 = 90,
        AvnKeyF2 = 91,
        AvnKeyF3 = 92,
        AvnKeyF4 = 93,
        AvnKeyF5 = 94,
        AvnKeyF6 = 95,
        AvnKeyF7 = 96,
        AvnKeyF8 = 97,
        AvnKeyF9 = 98,
        AvnKeyF10 = 99,
        AvnKeyF11 = 100,
        AvnKeyF12 = 101,
        AvnKeyF13 = 102,
        AvnKeyF14 = 103,
        AvnKeyF15 = 104,
        AvnKeyF16 = 105,
        AvnKeyF17 = 106,
        AvnKeyF18 = 107,
        AvnKeyF19 = 108,
        AvnKeyF20 = 109,
        AvnKeyF21 = 110,
        AvnKeyF22 = 111,
        AvnKeyF23 = 112,
        AvnKeyF24 = 113,
        AvnKeyNumLock = 114,
        AvnKeyScroll = 115,
        AvnKeyLeftShift = 116,
        AvnKeyRightShift = 117,
        AvnKeyLeftCtrl = 118,
        AvnKeyRightCtrl = 119,
        AvnKeyLeftAlt = 120,
        AvnKeyRightAlt = 121,
        AvnKeyBrowserBack = 122,
        AvnKeyBrowserForward = 123,
        AvnKeyBrowserRefresh = 124,
        AvnKeyBrowserStop = 125,
        AvnKeyBrowserSearch = 126,
        AvnKeyBrowserFavorites = 127,
        AvnKeyBrowserHome = 128,
        AvnKeyVolumeMute = 129,
        AvnKeyVolumeDown = 130,
        AvnKeyVolumeUp = 131,
        AvnKeyMediaNextTrack = 132,
        AvnKeyMediaPreviousTrack = 133,
        AvnKeyMediaStop = 134,
        AvnKeyMediaPlayPause = 135,
        AvnKeyLaunchMail = 136,
        AvnKeySelectMedia = 137,
        AvnKeyLaunchApplication1 = 138,
        AvnKeyLaunchApplication2 = 139,
        AvnKeyOemSemicolon = 140,
        AvnKeyOem1 = 140,
        AvnKeyOemPlus = 141,
        AvnKeyOemComma = 142,
        AvnKeyOemMinus = 143,
        AvnKeyOemPeriod = 144,
        AvnKeyOemQuestion = 145,
        AvnKeyOem2 = 145,
        AvnKeyOemTilde = 146,
        AvnKeyOem3 = 146,
        AvnKeyAbntC1 = 147,
        AvnKeyAbntC2 = 148,
        AvnKeyOemOpenBrackets = 149,
        AvnKeyOem4 = 149,
        AvnKeyOemPipe = 150,
        AvnKeyOem5 = 150,
        AvnKeyOemCloseBrackets = 151,
        AvnKeyOem6 = 151,
        AvnKeyOemQuotes = 152,
        AvnKeyOem7 = 152,
        AvnKeyOem8 = 153,
        AvnKeyOemBackslash = 154,
        AvnKeyOem102 = 154,
        AvnKeyImeProcessed = 155,
        AvnKeySystem = 156,
        AvnKeyOemAttn = 157,
        AvnKeyDbeAlphanumeric = 157,
        AvnKeyOemFinish = 158,
        AvnKeyDbeKatakana = 158,
        AvnKeyDbeHiragana = 159,
        AvnKeyOemCopy = 159,
        AvnKeyDbeSbcsChar = 160,
        AvnKeyOemAuto = 160,
        AvnKeyDbeDbcsChar = 161,
        AvnKeyOemEnlw = 161,
        AvnKeyOemBackTab = 162,
        AvnKeyDbeRoman = 162,
        AvnKeyDbeNoRoman = 163,
        AvnKeyAttn = 163,
        AvnKeyCrSel = 164,
        AvnKeyDbeEnterWordRegisterMode = 164,
        AvnKeyExSel = 165,
        AvnKeyDbeEnterImeConfigureMode = 165,
        AvnKeyEraseEof = 166,
        AvnKeyDbeFlushString = 166,
        AvnKeyPlay = 167,
        AvnKeyDbeCodeInput = 167,
        AvnKeyDbeNoCodeInput = 168,
        AvnKeyZoom = 168,
        AvnKeyNoName = 169,
        AvnKeyDbeDetermineString = 169,
        AvnKeyDbeEnterDialogConversionMode = 170,
        AvnKeyPa1 = 170,
        AvnKeyOemClear = 171,
        AvnKeyDeadCharProcessed = 172,
        AvnKeyFnLeftArrow = 10001,
        AvnKeyFnRightArrow = 10002,
        AvnKeyFnUpArrow = 10003,
        AvnKeyFnDownArrow = 10004
    }

    internal enum AvnPhysicalKey
    {
        AvnPhysicalKeyNone = 0,
        AvnPhysicalKeyBackquote = 1,
        AvnPhysicalKeyBackslash = 2,
        AvnPhysicalKeyBracketLeft = 3,
        AvnPhysicalKeyBracketRight = 4,
        AvnPhysicalKeyComma = 5,
        AvnPhysicalKeyDigit0 = 6,
        AvnPhysicalKeyDigit1 = 7,
        AvnPhysicalKeyDigit2 = 8,
        AvnPhysicalKeyDigit3 = 9,
        AvnPhysicalKeyDigit4 = 10,
        AvnPhysicalKeyDigit5 = 11,
        AvnPhysicalKeyDigit6 = 12,
        AvnPhysicalKeyDigit7 = 13,
        AvnPhysicalKeyDigit8 = 14,
        AvnPhysicalKeyDigit9 = 15,
        AvnPhysicalKeyEqual = 16,
        AvnPhysicalKeyIntlBackslash = 17,
        AvnPhysicalKeyIntlRo = 18,
        AvnPhysicalKeyIntlYen = 19,
        AvnPhysicalKeyA = 20,
        AvnPhysicalKeyB = 21,
        AvnPhysicalKeyC = 22,
        AvnPhysicalKeyD = 23,
        AvnPhysicalKeyE = 24,
        AvnPhysicalKeyF = 25,
        AvnPhysicalKeyG = 26,
        AvnPhysicalKeyH = 27,
        AvnPhysicalKeyI = 28,
        AvnPhysicalKeyJ = 29,
        AvnPhysicalKeyK = 30,
        AvnPhysicalKeyL = 31,
        AvnPhysicalKeyM = 32,
        AvnPhysicalKeyN = 33,
        AvnPhysicalKeyO = 34,
        AvnPhysicalKeyP = 35,
        AvnPhysicalKeyQ = 36,
        AvnPhysicalKeyR = 37,
        AvnPhysicalKeyS = 38,
        AvnPhysicalKeyT = 39,
        AvnPhysicalKeyU = 40,
        AvnPhysicalKeyV = 41,
        AvnPhysicalKeyW = 42,
        AvnPhysicalKeyX = 43,
        AvnPhysicalKeyY = 44,
        AvnPhysicalKeyZ = 45,
        AvnPhysicalKeyMinus = 46,
        AvnPhysicalKeyPeriod = 47,
        AvnPhysicalKeyQuote = 48,
        AvnPhysicalKeySemicolon = 49,
        AvnPhysicalKeySlash = 50,
        AvnPhysicalKeyAltLeft = 51,
        AvnPhysicalKeyAltRight = 52,
        AvnPhysicalKeyBackspace = 53,
        AvnPhysicalKeyCapsLock = 54,
        AvnPhysicalKeyContextMenu = 55,
        AvnPhysicalKeyControlLeft = 56,
        AvnPhysicalKeyControlRight = 57,
        AvnPhysicalKeyEnter = 58,
        AvnPhysicalKeyMetaLeft = 59,
        AvnPhysicalKeyMetaRight = 60,
        AvnPhysicalKeyShiftLeft = 61,
        AvnPhysicalKeyShiftRight = 62,
        AvnPhysicalKeySpace = 63,
        AvnPhysicalKeyTab = 64,
        AvnPhysicalKeyConvert = 65,
        AvnPhysicalKeyKanaMode = 66,
        AvnPhysicalKeyLang1 = 67,
        AvnPhysicalKeyLang2 = 68,
        AvnPhysicalKeyLang3 = 69,
        AvnPhysicalKeyLang4 = 70,
        AvnPhysicalKeyLang5 = 71,
        AvnPhysicalKeyNonConvert = 72,
        AvnPhysicalKeyDelete = 73,
        AvnPhysicalKeyEnd = 74,
        AvnPhysicalKeyHelp = 75,
        AvnPhysicalKeyHome = 76,
        AvnPhysicalKeyInsert = 77,
        AvnPhysicalKeyPageDown = 78,
        AvnPhysicalKeyPageUp = 79,
        AvnPhysicalKeyArrowDown = 80,
        AvnPhysicalKeyArrowLeft = 81,
        AvnPhysicalKeyArrowRight = 82,
        AvnPhysicalKeyArrowUp = 83,
        AvnPhysicalKeyNumLock = 84,
        AvnPhysicalKeyNumPad0 = 85,
        AvnPhysicalKeyNumPad1 = 86,
        AvnPhysicalKeyNumPad2 = 87,
        AvnPhysicalKeyNumPad3 = 88,
        AvnPhysicalKeyNumPad4 = 89,
        AvnPhysicalKeyNumPad5 = 90,
        AvnPhysicalKeyNumPad6 = 91,
        AvnPhysicalKeyNumPad7 = 92,
        AvnPhysicalKeyNumPad8 = 93,
        AvnPhysicalKeyNumPad9 = 94,
        AvnPhysicalKeyNumPadAdd = 95,
        AvnPhysicalKeyNumPadClear = 96,
        AvnPhysicalKeyNumPadComma = 97,
        AvnPhysicalKeyNumPadDecimal = 98,
        AvnPhysicalKeyNumPadDivide = 99,
        AvnPhysicalKeyNumPadEnter = 100,
        AvnPhysicalKeyNumPadEqual = 101,
        AvnPhysicalKeyNumPadMultiply = 102,
        AvnPhysicalKeyNumPadParenLeft = 103,
        AvnPhysicalKeyNumPadParenRight = 104,
        AvnPhysicalKeyNumPadSubtract = 105,
        AvnPhysicalKeyEscape = 106,
        AvnPhysicalKeyF1 = 107,
        AvnPhysicalKeyF2 = 108,
        AvnPhysicalKeyF3 = 109,
        AvnPhysicalKeyF4 = 110,
        AvnPhysicalKeyF5 = 111,
        AvnPhysicalKeyF6 = 112,
        AvnPhysicalKeyF7 = 113,
        AvnPhysicalKeyF8 = 114,
        AvnPhysicalKeyF9 = 115,
        AvnPhysicalKeyF10 = 116,
        AvnPhysicalKeyF11 = 117,
        AvnPhysicalKeyF12 = 118,
        AvnPhysicalKeyF13 = 119,
        AvnPhysicalKeyF14 = 120,
        AvnPhysicalKeyF15 = 121,
        AvnPhysicalKeyF16 = 122,
        AvnPhysicalKeyF17 = 123,
        AvnPhysicalKeyF18 = 124,
        AvnPhysicalKeyF19 = 125,
        AvnPhysicalKeyF20 = 126,
        AvnPhysicalKeyF21 = 127,
        AvnPhysicalKeyF22 = 128,
        AvnPhysicalKeyF23 = 129,
        AvnPhysicalKeyF24 = 130,
        AvnPhysicalKeyPrintScreen = 131,
        AvnPhysicalKeyScrollLock = 132,
        AvnPhysicalKeyPause = 133,
        AvnPhysicalKeyBrowserBack = 134,
        AvnPhysicalKeyBrowserFavorites = 135,
        AvnPhysicalKeyBrowserForward = 136,
        AvnPhysicalKeyBrowserHome = 137,
        AvnPhysicalKeyBrowserRefresh = 138,
        AvnPhysicalKeyBrowserSearch = 139,
        AvnPhysicalKeyBrowserStop = 140,
        AvnPhysicalKeyEject = 141,
        AvnPhysicalKeyLaunchApp1 = 142,
        AvnPhysicalKeyLaunchApp2 = 143,
        AvnPhysicalKeyLaunchMail = 144,
        AvnPhysicalKeyMediaPlayPause = 145,
        AvnPhysicalKeyMediaSelect = 146,
        AvnPhysicalKeyMediaStop = 147,
        AvnPhysicalKeyMediaTrackNext = 148,
        AvnPhysicalKeyMediaTrackPrevious = 149,
        AvnPhysicalKeyPower = 150,
        AvnPhysicalKeySleep = 151,
        AvnPhysicalKeyAudioVolumeDown = 152,
        AvnPhysicalKeyAudioVolumeMute = 153,
        AvnPhysicalKeyAudioVolumeUp = 154,
        AvnPhysicalKeyWakeUp = 155,
        AvnPhysicalKeyAgain = 156,
        AvnPhysicalKeyCopy = 157,
        AvnPhysicalKeyCut = 158,
        AvnPhysicalKeyFind = 159,
        AvnPhysicalKeyOpen = 160,
        AvnPhysicalKeyPaste = 161,
        AvnPhysicalKeyProps = 162,
        AvnPhysicalKeySelect = 163,
        AvnPhysicalKeyUndo = 164
    }

    internal enum SystemDecorations
    {
        SystemDecorationsNone = 0,
        SystemDecorationsBorderOnly = 1,
        SystemDecorationsFull = 2
    }

    internal enum AvnAutomationProperty
    {
        AutomationPeer_BoundingRectangle,
        AutomationPeer_ClassName,
        AutomationPeer_Name,
        RangeValueProvider_Value,
        ValueProvider_Value,
        ToggleProvider_ToggleState,
        ExpandCollapseProvider_ExpandCollapseState,
        SelectionItemProvider_IsSelected,
        SelectionProvider_Selection
    }

    internal enum AvnScreenOrientation
    {
        UnknownOrientation,
        Landscape,
        Portrait,
        LandscapeFlipped,
        PortraitFlipped
    }

    internal enum AvnPixelFormat
    {
        kAvnRgb565,
        kAvnRgba8888,
        kAvnBgra8888
    }

    internal enum AvnRawMouseEventType
    {
        LeaveWindow,
        LeftButtonDown,
        LeftButtonUp,
        RightButtonDown,
        RightButtonUp,
        MiddleButtonDown,
        MiddleButtonUp,
        XButton1Down,
        XButton1Up,
        XButton2Down,
        XButton2Up,
        Move,
        Wheel,
        NonClientLeftButtonDown,
        TouchBegin,
        TouchUpdate,
        TouchEnd,
        TouchCancel,
        Magnify,
        Rotate,
        Swipe
    }

    internal enum AvnRawKeyEventType
    {
        KeyDown,
        KeyUp
    }

    internal enum AvnInputModifiers
    {
        AvnInputModifiersNone = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Windows = 8,
        LeftMouseButton = 16,
        RightMouseButton = 32,
        MiddleMouseButton = 64,
        XButton1MouseButton = 128,
        XButton2MouseButton = 256
    }

    internal enum AvnDragDropEffects
    {
        None = 0,
        Copy = 1,
        Move = 2,
        Link = 4
    }

    internal enum AvnDragEventType
    {
        Enter,
        Over,
        Leave,
        Drop
    }

    internal enum AvnWindowState
    {
        Normal,
        Minimized,
        Maximized,
        FullScreen
    }

    internal enum AvnStandardCursorType
    {
        CursorArrow,
        CursorIbeam,
        CursorWait,
        CursorCross,
        CursorUpArrow,
        CursorSizeWestEast,
        CursorSizeNorthSouth,
        CursorSizeAll,
        CursorNo,
        CursorHand,
        CursorAppStarting,
        CursorHelp,
        CursorTopSide,
        CursorBottomSize,
        CursorLeftSide,
        CursorRightSide,
        CursorTopLeftCorner,
        CursorTopRightCorner,
        CursorBottomLeftCorner,
        CursorBottomRightCorner,
        CursorDragMove,
        CursorDragCopy,
        CursorDragLink,
        CursorNone
    }

    internal enum AvnWindowEdge
    {
        WindowEdgeNorthWest,
        WindowEdgeNorth,
        WindowEdgeNorthEast,
        WindowEdgeWest,
        WindowEdgeEast,
        WindowEdgeSouthWest,
        WindowEdgeSouth,
        WindowEdgeSouthEast
    }

    internal enum AvnMenuItemToggleType
    {
        None,
        CheckMark,
        Radio
    }

    internal enum AvnPlatformResizeReason
    {
        ResizeUnspecified,
        ResizeUser,
        ResizeApplication,
        ResizeLayout,
        ResizeDpiChange
    }

    internal enum AvnAutomationControlType
    {
        AutomationNone,
        AutomationButton,
        AutomationCalendar,
        AutomationCheckBox,
        AutomationComboBox,
        AutomationComboBoxItem,
        AutomationEdit,
        AutomationHyperlink,
        AutomationImage,
        AutomationListItem,
        AutomationList,
        AutomationMenu,
        AutomationMenuBar,
        AutomationMenuItem,
        AutomationProgressBar,
        AutomationRadioButton,
        AutomationScrollBar,
        AutomationSlider,
        AutomationSpinner,
        AutomationStatusBar,
        AutomationTab,
        AutomationTabItem,
        AutomationText,
        AutomationToolBar,
        AutomationToolTip,
        AutomationTree,
        AutomationTreeItem,
        AutomationCustom,
        AutomationGroup,
        AutomationThumb,
        AutomationDataGrid,
        AutomationDataItem,
        AutomationDocument,
        AutomationSplitButton,
        AutomationWindow,
        AutomationPane,
        AutomationHeader,
        AutomationHeaderItem,
        AutomationTable,
        AutomationTitleBar,
        AutomationSeparator,
        AutomationExpander
    }

    internal enum AvnLandmarkType
    {
        LandmarkNone = -1,
        LandmarkBanner,
        LandmarkComplementary,
        LandmarkContentInfo,
        LandmarkRegion,
        LandmarkForm,
        LandmarkMain,
        LandmarkNavigation,
        LandmarkSearch
    }

    internal enum AvnWindowTransparencyMode
    {
        Opaque,
        Transparent,
        Blur
    }

    internal enum AvnPlatformThemeVariant
    {
        Light,
        Dark,
        HighContrastLight,
        HighContrastDark
    }

    internal enum AvnPointerDeviceType
    {
        Mouse,
        Pen
    }

    internal enum AvnLiveSetting
    {
        LiveSettingOff,
        LiveSettingPolite,
        LiveSettingAssertive
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal unsafe partial struct AvnSize
    {
        public double Width;
        public double Height;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal unsafe partial struct AvnPixelSize
    {
        public int Width;
        public int Height;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal unsafe partial struct AvnRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal unsafe partial struct AvnVector
    {
        public double X;
        public double Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal unsafe partial struct AvnPoint
    {
        public double X;
        public double Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal unsafe partial struct AvnScreen
    {
        public AvnRect Bounds;
        public AvnRect WorkingArea;
        public float Scaling;
        public int IsPrimary;
        public AvnScreenOrientation Orientation;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal unsafe partial struct AvnFramebuffer
    {
        public void* Data;
        public int Width;
        public int Height;
        public int Stride;
        public AvnVector Dpi;
        public AvnPixelFormat PixelFormat;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal unsafe partial struct AvnColor
    {
        public byte Alpha;
        public byte Red;
        public byte Green;
        public byte Blue;
    }

    internal unsafe partial interface IAvaloniaNativeFactory : global::MicroCom.Runtime.IUnknown
    {
        void Initialize(IAvnGCHandleDeallocatorCallback deallocator, IAvnApplicationEvents appCb, IAvnDispatcher dispatcher);
        IAvnMacOptions MacOptions { get; }

        IAvnTopLevel CreateTopLevel(IAvnTopLevelEvents cb);
        IAvnWindow CreateWindow(IAvnWindowEvents cb);
        IAvnPopup CreatePopup(IAvnWindowEvents cb);
        IAvnPlatformThreadingInterface CreatePlatformThreadingInterface();
        IAvnStorageProvider CreateStorageProvider();
        IAvnScreens CreateScreens(IAvnScreenEvents cb);
        IAvnClipboard CreateClipboard();
        IAvnCursorFactory CreateCursorFactory();
        IAvnGlDisplay ObtainGlDisplay();
        IAvnMetalDisplay ObtainMetalDisplay();
        void SetAppMenu(IAvnMenu menu);
        void SetServicesMenu(IAvnMenu menu);
        IAvnMenu CreateMenu(IAvnMenuEvents cb);
        IAvnMenuItem CreateMenuItem();
        IAvnMenuItem CreateMenuItemSeparator();
        IAvnTrayIcon CreateTrayIcon();
        IAvnApplicationCommands CreateApplicationCommands();
        IAvnPlatformSettings CreatePlatformSettings();
        IAvnPlatformBehaviorInhibition CreatePlatformBehaviorInhibition();
        IAvnPlatformRenderTimer CreatePlatformRenderTimer();
        IAvnMTLSharedEvent ImportMTLSharedEvent(IntPtr idMtlSharedEvent);
        IAvnNativeObjectsMemoryManagement CreateMemoryManagementHelper();
        void SetDockMenu(IAvnMenu menu);
    }

    internal unsafe partial interface IAvnString : global::MicroCom.Runtime.IUnknown
    {
        void* Pointer();
        int Length();
    }

    internal unsafe partial interface IAvnTopLevel : global::MicroCom.Runtime.IUnknown
    {
        AvnSize ClientSize { get; }

        double Scaling { get; }

        void Invalidate();
        AvnPoint PointToClient(AvnPoint point);
        AvnPoint PointToScreen(AvnPoint point);
        void SetCursor(IAvnCursor cursor);
        IAvnGlSurfaceRenderTarget CreateGlRenderTarget(IAvnGlContext context);
        IAvnSoftwareRenderTarget CreateSoftwareRenderTarget();
        IAvnMetalRenderTarget CreateMetalRenderTarget(IAvnMetalDevice device);
        IntPtr ObtainNSViewHandle();
        IntPtr ObtainNSViewHandleRetained();
        IAvnNativeControlHost CreateNativeControlHost();
        IAvnTextInputMethod InputMethod { get; }

        void SetTransparencyMode(AvnWindowTransparencyMode mode);
        uint CurrentDisplayId { get; }

        void BeginDragAndDropOperation(AvnDragDropEffects effects, AvnPoint point, IAvnClipboardDataSource source, IAvnDndResultCallback callback, IntPtr sourceHandle);
    }

    internal unsafe partial interface IAvnWindowBase : IAvnTopLevel
    {
        void GetFrameSize(AvnSize* result);
        void SetFrameThemeVariant(AvnPlatformThemeVariant mode);
        void SetParent(IAvnWindowBase parent);
        void Show(int activate, int isDialog);
        void Hide();
        void Close();
        void Activate();
        void SetMinMaxSize(AvnSize minSize, AvnSize maxSize);
        void Resize(double width, double height, AvnPlatformResizeReason reason);
        void BeginMoveDrag();
        void BeginResizeDrag(AvnWindowEdge edge);
        AvnPoint Position { get; }

        void SetPosition(AvnPoint point);
        void SetTopMost(int value);
        void SetMainMenu(IAvnMenu menu);
        IntPtr ObtainNSWindowHandle();
        IntPtr ObtainNSWindowHandleRetained();
    }

    internal unsafe partial interface IAvnPopup : IAvnWindowBase
    {
    }

    internal unsafe partial interface IAvnWindow : IAvnWindowBase
    {
        void SetEnabled(int enable);
        void SetCanResize(int value);
        void SetCanMinimize(int value);
        void SetCanMaximize(int value);
        void SetDecorations(SystemDecorations value);
        void SetTitle(string utf8Title);
        void SetTitleBarColor(AvnColor color);
        void SetWindowState(AvnWindowState state);
        AvnWindowState WindowState { get; }

        void TakeFocusFromChildren();
        void SetExtendClientArea(int enable);
        double ExtendTitleBarHeight { get; }

        void SetExtendTitleBarHeight(double value);
        IntPtr WindowZOrder { get; }
    }

    internal unsafe partial interface IAvnTopLevelEvents : global::MicroCom.Runtime.IUnknown
    {
        void Closed();
        void Paint();
        void Resized(AvnSize* size, AvnPlatformResizeReason reason);
        void RawMouseEvent(AvnRawMouseEventType type, AvnPointerDeviceType deviceType, ulong timeStamp, AvnInputModifiers modifiers, AvnPoint point, AvnVector delta, float pressure, float xTilt, float yTilt);
        int RawKeyEvent(AvnRawKeyEventType type, ulong timeStamp, AvnInputModifiers modifiers, AvnKey key, AvnPhysicalKey physicalKey, string keySymbol);
        int RawTextInputEvent(ulong timeStamp, string text);
        void ScalingChanged(double scaling);
        void RunRenderPriorityJobs();
        void LostFocus();
        IAvnAutomationPeer AutomationPeer { get; }

        AvnDragDropEffects DragEvent(AvnDragEventType type, AvnPoint position, AvnInputModifiers modifiers, AvnDragDropEffects effects, IAvnClipboard clipboard, IntPtr dataTransferHandle);
    }

    internal unsafe partial interface IAvnWindowBaseEvents : IAvnTopLevelEvents
    {
        void Activated();
        void Deactivated();
        void PositionChanged(AvnPoint position);
    }

    internal unsafe partial interface IAvnWindowEvents : IAvnWindowBaseEvents
    {
        int Closing();
        void WindowStateChanged(AvnWindowState state);
        void GotInputWhenDisabled();
    }

    internal unsafe partial interface IAvnTextInputMethodClient : global::MicroCom.Runtime.IUnknown
    {
        void SetPreeditText(string preeditText);
        void SelectInSurroundingText(int start, int length);
    }

    internal unsafe partial interface IAvnTextInputMethod : global::MicroCom.Runtime.IUnknown
    {
        void SetClient(IAvnTextInputMethodClient client);
        void Reset();
        void SetCursorRect(AvnRect rect);
        void SetSurroundingText(string text, int anchorOffset, int cursorOffset);
    }

    internal unsafe partial interface IAvnMacOptions : global::MicroCom.Runtime.IUnknown
    {
        void SetShowInDock(int show);
        void SetApplicationTitle(string utf8string);
        void SetDisableSetProcessName(int disable);
        void SetDisableAppDelegate(int disable);
    }

    internal unsafe partial interface IAvnActionCallback : global::MicroCom.Runtime.IUnknown
    {
        void Run();
    }

    internal unsafe partial interface IAvnPlatformThreadingInterfaceEvents : global::MicroCom.Runtime.IUnknown
    {
        void Signaled();
        void Timer();
        void ReadyForBackgroundProcessing();
    }

    internal unsafe partial interface IAvnLoopCancellation : global::MicroCom.Runtime.IUnknown
    {
        void Cancel();
    }

    internal unsafe partial interface IAvnPlatformThreadingInterface : global::MicroCom.Runtime.IUnknown
    {
        int CurrentThreadIsLoopThread { get; }

        void SetEvents(IAvnPlatformThreadingInterfaceEvents cb);
        IAvnLoopCancellation CreateLoopCancellation();
        void RunLoop(IAvnLoopCancellation cancel);
        void Signal();
        void UpdateTimer(int ms);
        void RequestBackgroundProcessing();
    }

    internal unsafe partial interface IAvnSystemDialogEvents : global::MicroCom.Runtime.IUnknown
    {
        void OnCompleted(IAvnStringArray array);
        void OnCompletedWithFilter(IAvnStringArray array, int selectedFilterIndex);
    }

    internal unsafe partial interface IAvnStorageProvider : global::MicroCom.Runtime.IUnknown
    {
        void SelectFolderDialog(IAvnTopLevel parentTopLevel, IAvnSystemDialogEvents events, int allowMultiple, string title, string initialPath);
        void OpenFileDialog(IAvnTopLevel parentTopLevel, IAvnSystemDialogEvents events, int allowMultiple, string title, string initialDirectory, string initialFile, IAvnFilePickerFileTypes filters);
        void SaveFileDialog(IAvnTopLevel parentTopLevel, IAvnSystemDialogEvents events, string title, string initialDirectory, string initialFile, IAvnFilePickerFileTypes filters);
        IAvnString SaveBookmarkToBytes(IAvnString fileUri, void** err);
        IAvnString ReadBookmarkFromBytes(void* ptr, int len);
        void ReleaseBookmark(IAvnString fileUri);
        int OpenSecurityScope(IAvnString fileUri);
        void CloseSecurityScope(IAvnString fileUri);
        IAvnString TryResolveFileReferenceUri(IAvnString fileUri);
    }

    internal unsafe partial interface IAvnFilePickerFileTypes : global::MicroCom.Runtime.IUnknown
    {
        int Count { get; }

        int IsDefaultType(int index);
        int IsAnyType(int index);
        IAvnString GetName(int index);
        IAvnStringArray GetPatterns(int index);
        IAvnStringArray GetExtensions(int index);
        IAvnStringArray GetMimeTypes(int index);
        IAvnStringArray GetAppleUniformTypeIdentifiers(int index);
    }

    internal unsafe partial interface IAvnScreenEvents : global::MicroCom.Runtime.IUnknown
    {
        void OnChanged();
    }

    internal unsafe partial interface IAvnScreens : global::MicroCom.Runtime.IUnknown
    {
        int GetScreenIds(uint* ptrFirstResult);
        AvnScreen GetScreen(uint screenId, void** localizedName);
    }

    internal unsafe partial interface IAvnClipboard : global::MicroCom.Runtime.IUnknown
    {
        IAvnStringArray GetFormats(long changeCount);
        int GetItemCount(long changeCount);
        IAvnStringArray GetItemFormats(int index, long changeCount);
        IAvnString GetItemValueAsString(int index, long changeCount, string format);
        IAvnString GetItemValueAsBytes(int index, long changeCount, string format);
        long Clear();
        long ChangeCount { get; }

        void SetData(IAvnClipboardDataSource dataSource);
        int IsTextFormat(string format);
    }

    internal unsafe partial interface IAvnClipboardDataSource : global::MicroCom.Runtime.IUnknown
    {
        int ItemCount { get; }

        IAvnClipboardDataItem GetItem(int index);
    }

    internal unsafe partial interface IAvnClipboardDataItem : global::MicroCom.Runtime.IUnknown
    {
        IAvnStringArray ProvideFormats();
        IAvnClipboardDataValue GetValue(string format);
    }

    internal unsafe partial interface IAvnClipboardDataValue : global::MicroCom.Runtime.IUnknown
    {
        int IsString();
        IAvnString AsString();
        IntPtr ByteLength { get; }

        void CopyBytesTo(void* buffer);
    }

    internal unsafe partial interface IAvnCursor : global::MicroCom.Runtime.IUnknown
    {
    }

    internal unsafe partial interface IAvnCursorFactory : global::MicroCom.Runtime.IUnknown
    {
        IAvnCursor GetCursor(AvnStandardCursorType cursorType);
        IAvnCursor CreateCustomCursor(void* bitmapData, System.IntPtr length, AvnPixelSize hotPixel);
    }

    internal unsafe partial interface IAvnSoftwareRenderTarget : global::MicroCom.Runtime.IUnknown
    {
        void SetFrame(AvnFramebuffer* fb);
    }

    internal unsafe partial interface IAvnGlDisplay : global::MicroCom.Runtime.IUnknown
    {
        IAvnGlContext CreateContext(IAvnGlContext share);
        void LegacyClearCurrentContext();
        IAvnGlContext WrapContext(IntPtr native);
        IntPtr GetProcAddress(string proc);
    }

    internal unsafe partial interface IAvnGlContext : global::MicroCom.Runtime.IUnknown
    {
        IUnknown MakeCurrent();
        void LegacyMakeCurrent();
        int SampleCount { get; }

        int StencilSize { get; }

        IntPtr NativeHandle { get; }

        int texImageIOSurface2D(int target, int internal_format, int width, int height, int format, int type, IntPtr ioSurface, int plane);
        int GetIOKitRegistryId(ulong* value);
    }

    internal unsafe partial interface IAvnGlSurfaceRenderTarget : global::MicroCom.Runtime.IUnknown
    {
        IAvnGlSurfaceRenderingSession BeginDrawing();
    }

    internal unsafe partial interface IAvnGlSurfaceRenderingSession : global::MicroCom.Runtime.IUnknown
    {
        AvnPixelSize PixelSize { get; }

        double Scaling { get; }
    }

    internal unsafe partial interface IAvnMetalDisplay : global::MicroCom.Runtime.IUnknown
    {
        IAvnMetalDevice CreateDevice();
    }

    internal unsafe partial interface IAvnMetalDevice : global::MicroCom.Runtime.IUnknown
    {
        IntPtr Device { get; }

        IntPtr Queue { get; }

        int GetIOKitRegistryId(ulong* value);
        IAvnMetalTexture ImportIOSurface(IntPtr handle, AvnPixelFormat pixelFormat);
        IAvnMTLSharedEvent ImportSharedEvent(IntPtr mtlSharedEventInstance);
        void SubmitWait(IAvnMTLSharedEvent ev, ulong value);
        void SubmitSignal(IAvnMTLSharedEvent ev, ulong value);
    }

    internal unsafe partial interface IAvnMetalRenderTarget : global::MicroCom.Runtime.IUnknown
    {
        IAvnMetalRenderingSession BeginDrawing();
    }

    internal unsafe partial interface IAvnMTLSharedEvent : global::MicroCom.Runtime.IUnknown
    {
        IntPtr NativeHandle { get; }

        int Wait(ulong value, ulong timeoutMS);
        void SetSignaledValue(ulong value);
        ulong SignaledValue { get; }
    }

    internal unsafe partial interface IAvnMetalTexture : global::MicroCom.Runtime.IUnknown
    {
        IntPtr NativeHandle { get; }

        int Width { get; }

        int Height { get; }

        int SampleCount { get; }
    }

    internal unsafe partial interface IAvnNativeObjectsMemoryManagement : global::MicroCom.Runtime.IUnknown
    {
        void RetainNSObject(IntPtr obj);
        void ReleaseNSObject(IntPtr obj);
        ulong GetRetainCountForNSObject(IntPtr obj);
        void RetainCFObject(IntPtr obj);
        void ReleaseCFObject(IntPtr obj);
        long GetRetainCountForCFObject(IntPtr obj);
    }

    internal unsafe partial interface IAvnMetalRenderingSession : global::MicroCom.Runtime.IUnknown
    {
        AvnPixelSize PixelSize { get; }

        double Scaling { get; }

        IntPtr Texture { get; }
    }

    internal unsafe partial interface IAvnTrayIcon : global::MicroCom.Runtime.IUnknown
    {
        void SetIcon(void* data, System.IntPtr length);
        void SetMenu(IAvnMenu menu);
        void SetIsVisible(int isVisible);
        void SetToolTipText(string text);
        void SetIsTemplateIcon(int text);
    }

    internal unsafe partial interface IAvnMenu : global::MicroCom.Runtime.IUnknown
    {
        void InsertItem(int index, IAvnMenuItem item);
        void RemoveItem(IAvnMenuItem item);
        void SetTitle(string utf8String);
        void Clear();
    }

    internal unsafe partial interface IAvnPredicateCallback : global::MicroCom.Runtime.IUnknown
    {
        int Evaluate();
    }

    internal unsafe partial interface IAvnMenuItem : global::MicroCom.Runtime.IUnknown
    {
        void SetSubMenu(IAvnMenu menu);
        void SetTitle(string utf8String);
        void SetToolTip(string utf8String);
        void SetGesture(AvnKey key, AvnInputModifiers modifiers);
        void SetAction(IAvnPredicateCallback predicate, IAvnActionCallback callback);
        void SetIsChecked(int isChecked);
        void SetIsVisible(int isVisible);
        void SetToggleType(AvnMenuItemToggleType toggleType);
        void SetIcon(void* data, System.IntPtr length);
    }

    internal unsafe partial interface IAvnMenuEvents : global::MicroCom.Runtime.IUnknown
    {
        void NeedsUpdate();
        void Opening();
        void Closed();
    }

    internal unsafe partial interface IAvnStringArray : global::MicroCom.Runtime.IUnknown
    {
        uint Count { get; }

        IAvnString Get(uint index);
    }

    internal unsafe partial interface IAvnDndResultCallback : global::MicroCom.Runtime.IUnknown
    {
        void OnDragAndDropComplete(AvnDragDropEffects effecct);
    }

    internal unsafe partial interface IAvnGCHandleDeallocatorCallback : global::MicroCom.Runtime.IUnknown
    {
        void FreeGCHandle(IntPtr handle);
    }

    internal unsafe partial interface IAvnDispatcher : global::MicroCom.Runtime.IUnknown
    {
        void Post(IAvnActionCallback cb);
    }

    internal unsafe partial interface IAvnNativeControlHost : global::MicroCom.Runtime.IUnknown
    {
        IntPtr CreateDefaultChild(IntPtr parent);
        IAvnNativeControlHostTopLevelAttachment CreateAttachment();
        void DestroyDefaultChild(IntPtr child);
    }

    internal unsafe partial interface IAvnNativeControlHostTopLevelAttachment : global::MicroCom.Runtime.IUnknown
    {
        IntPtr ParentHandle { get; }

        void InitializeWithChildHandle(IntPtr child);
        void AttachTo(IAvnNativeControlHost host);
        void ShowInBounds(float x, float y, float width, float height);
        void HideWithSize(float width, float height);
        void ReleaseChild();
    }

    internal unsafe partial interface IAvnApplicationEvents : global::MicroCom.Runtime.IUnknown
    {
        void FilesOpened(IAvnStringArray args);
        void UrlsOpened(IAvnStringArray urls);
        int TryShutdown();
        void OnReopen();
        void OnHide();
        void OnUnhide();
        void OnActivate();
        void OnDeactivate();
    }

    internal unsafe partial interface IAvnApplicationCommands : global::MicroCom.Runtime.IUnknown
    {
        void UnhideApp();
        void HideApp();
        void ShowAll();
        void HideOthers();
    }

    internal unsafe partial interface IAvnAutomationPeer : global::MicroCom.Runtime.IUnknown
    {
        IAvnAutomationNode Node { get; }

        void SetNode(IAvnAutomationNode node);
        IAvnString AcceleratorKey { get; }

        IAvnString AccessKey { get; }

        AvnAutomationControlType AutomationControlType { get; }

        IAvnString AutomationId { get; }

        AvnRect BoundingRectangle { get; }

        IAvnAutomationPeerArray Children { get; }

        IAvnString ClassName { get; }

        IAvnAutomationPeer LabeledBy { get; }

        IAvnString Name { get; }

        IAvnAutomationPeer Parent { get; }

        IAvnAutomationPeer VisualRoot { get; }

        int HasKeyboardFocus();
        int IsContentElement();
        int IsControlElement();
        int IsEnabled();
        int IsKeyboardFocusable();
        void SetFocus();
        int ShowContextMenu();
        IAvnAutomationPeer RootPeer { get; }

        int IsInteropPeer();
        IntPtr InteropPeer_GetNativeControlHandle();
        int IsRootProvider();
        IAvnWindowBase RootProvider_GetWindow();
        IAvnAutomationPeer RootProvider_GetFocus();
        IAvnAutomationPeer RootProvider_GetPeerFromPoint(AvnPoint point);
        int IsEmbeddedRootProvider();
        IAvnAutomationPeer EmbeddedRootProvider_GetFocus();
        IAvnAutomationPeer EmbeddedRootProvider_GetPeerFromPoint(AvnPoint point);
        int IsExpandCollapseProvider();
        int ExpandCollapseProvider_GetIsExpanded();
        int ExpandCollapseProvider_GetShowsMenu();
        void ExpandCollapseProvider_Expand();
        void ExpandCollapseProvider_Collapse();
        int IsInvokeProvider();
        void InvokeProvider_Invoke();
        int IsRangeValueProvider();
        double RangeValueProvider_GetValue();
        double RangeValueProvider_GetMinimum();
        double RangeValueProvider_GetMaximum();
        double RangeValueProvider_GetSmallChange();
        double RangeValueProvider_GetLargeChange();
        void RangeValueProvider_SetValue(double value);
        int IsSelectionItemProvider();
        int SelectionItemProvider_IsSelected();
        int IsToggleProvider();
        int ToggleProvider_GetToggleState();
        void ToggleProvider_Toggle();
        int IsValueProvider();
        IAvnString ValueProvider_GetValue();
        void ValueProvider_SetValue(string value);
        IAvnString HelpText { get; }

        IAvnString PlaceholderText { get; }

        AvnLandmarkType LandmarkType { get; }

        int HeadingLevel { get; }

        AvnLiveSetting LiveSetting { get; }
    }

    internal unsafe partial interface IAvnAutomationPeerArray : global::MicroCom.Runtime.IUnknown
    {
        uint Count { get; }

        IAvnAutomationPeer Get(uint index);
    }

    internal unsafe partial interface IAvnAutomationNode : global::MicroCom.Runtime.IUnknown
    {
        void Dispose();
        void ChildrenChanged();
        void PropertyChanged(AvnAutomationProperty property);
        void FocusChanged();
    }

    internal unsafe partial interface IAvnPlatformSettings : global::MicroCom.Runtime.IUnknown
    {
        AvnPlatformThemeVariant PlatformTheme { get; }

        uint AccentColor { get; }

        void RegisterColorsChange(IAvnActionCallback callback);
    }

    internal unsafe partial interface IAvnPlatformBehaviorInhibition : global::MicroCom.Runtime.IUnknown
    {
        void SetInhibitAppSleep(int inhibitAppSleep, string reason);
    }

    internal unsafe partial interface IAvnPlatformRenderTimer : global::MicroCom.Runtime.IUnknown
    {
        int RegisterTick(IAvnActionCallback callback);
        void Start();
        void Stop();
        int RunsInBackground();
    }
}

namespace Avalonia.Native.Interop.Impl
{
    internal unsafe partial class __MicroComIAvaloniaNativeFactoryProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvaloniaNativeFactory
    {
        public void Initialize(IAvnGCHandleDeallocatorCallback deallocator, IAvnApplicationEvents appCb, IAvnDispatcher dispatcher)
        {
            int __result;
            using var __deallocator = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(deallocator);
            using var __appCb = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(appCb);
            using var __dispatcher = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(dispatcher);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, __deallocator.Pointer, __appCb.Pointer, __dispatcher.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Initialize failed", __result);
        }

        public IAvnMacOptions MacOptions
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 1])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMacOptions>(__result, true);
            }
        }

        public IAvnTopLevel CreateTopLevel(IAvnTopLevelEvents cb)
        {
            int __result;
            using var __cb = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(cb);
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, __cb.Pointer, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTopLevel failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnTopLevel>(__marshal_ppv, true);
        }

        public IAvnWindow CreateWindow(IAvnWindowEvents cb)
        {
            int __result;
            using var __cb = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(cb);
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, __cb.Pointer, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateWindow failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnWindow>(__marshal_ppv, true);
        }

        public IAvnPopup CreatePopup(IAvnWindowEvents cb)
        {
            int __result;
            using var __cb = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(cb);
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, __cb.Pointer, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreatePopup failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnPopup>(__marshal_ppv, true);
        }

        public IAvnPlatformThreadingInterface CreatePlatformThreadingInterface()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 5])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreatePlatformThreadingInterface failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnPlatformThreadingInterface>(__marshal_ppv, true);
        }

        public IAvnStorageProvider CreateStorageProvider()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateStorageProvider failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnStorageProvider>(__marshal_ppv, true);
        }

        public IAvnScreens CreateScreens(IAvnScreenEvents cb)
        {
            int __result;
            using var __cb = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(cb);
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 7])(PPV, __cb.Pointer, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateScreens failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnScreens>(__marshal_ppv, true);
        }

        public IAvnClipboard CreateClipboard()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 8])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateClipboard failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnClipboard>(__marshal_ppv, true);
        }

        public IAvnCursorFactory CreateCursorFactory()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 9])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateCursorFactory failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnCursorFactory>(__marshal_ppv, true);
        }

        public IAvnGlDisplay ObtainGlDisplay()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 10])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ObtainGlDisplay failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnGlDisplay>(__marshal_ppv, true);
        }

        public IAvnMetalDisplay ObtainMetalDisplay()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 11])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ObtainMetalDisplay failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMetalDisplay>(__marshal_ppv, true);
        }

        public void SetAppMenu(IAvnMenu menu)
        {
            int __result;
            using var __menu = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(menu);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 12])(PPV, __menu.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetAppMenu failed", __result);
        }

        public void SetServicesMenu(IAvnMenu menu)
        {
            int __result;
            using var __menu = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(menu);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 13])(PPV, __menu.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetServicesMenu failed", __result);
        }

        public IAvnMenu CreateMenu(IAvnMenuEvents cb)
        {
            int __result;
            using var __cb = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(cb);
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 14])(PPV, __cb.Pointer, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateMenu failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMenu>(__marshal_ppv, true);
        }

        public IAvnMenuItem CreateMenuItem()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 15])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateMenuItem failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMenuItem>(__marshal_ppv, true);
        }

        public IAvnMenuItem CreateMenuItemSeparator()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 16])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateMenuItemSeparator failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMenuItem>(__marshal_ppv, true);
        }

        public IAvnTrayIcon CreateTrayIcon()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 17])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateTrayIcon failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnTrayIcon>(__marshal_ppv, true);
        }

        public IAvnApplicationCommands CreateApplicationCommands()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 18])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateApplicationCommands failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnApplicationCommands>(__marshal_ppv, true);
        }

        public IAvnPlatformSettings CreatePlatformSettings()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 19])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreatePlatformSettings failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnPlatformSettings>(__marshal_ppv, true);
        }

        public IAvnPlatformBehaviorInhibition CreatePlatformBehaviorInhibition()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 20])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreatePlatformBehaviorInhibition failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnPlatformBehaviorInhibition>(__marshal_ppv, true);
        }

        public IAvnPlatformRenderTimer CreatePlatformRenderTimer()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 21])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreatePlatformRenderTimer failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnPlatformRenderTimer>(__marshal_ppv, true);
        }

        public IAvnMTLSharedEvent ImportMTLSharedEvent(IntPtr idMtlSharedEvent)
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 22])(PPV, idMtlSharedEvent, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ImportMTLSharedEvent failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMTLSharedEvent>(__marshal_ppv, true);
        }

        public IAvnNativeObjectsMemoryManagement CreateMemoryManagementHelper()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 23])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateMemoryManagementHelper failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnNativeObjectsMemoryManagement>(__marshal_ppv, true);
        }

        public void SetDockMenu(IAvnMenu menu)
        {
            int __result;
            using var __menu = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(menu);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 24])(PPV, __menu.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetDockMenu failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvaloniaNativeFactory), new Guid("809c652e-7396-11d2-9771-00a0c9b4d50c"), (p, owns) => new __MicroComIAvaloniaNativeFactoryProxy(p, owns));
        }

        protected __MicroComIAvaloniaNativeFactoryProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 25;
    }

    unsafe class __MicroComIAvaloniaNativeFactoryVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int InitializeDelegate(void* @this, void* deallocator, void* appCb, void* dispatcher);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Initialize(void* @this, void* deallocator, void* appCb, void* dispatcher)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Initialize(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnGCHandleDeallocatorCallback>(deallocator, false), global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnApplicationEvents>(appCb, false), global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnDispatcher>(dispatcher, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetMacOptionsDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetMacOptions(void* @this)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.MacOptions;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateTopLevelDelegate(void* @this, void* cb, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTopLevel(void* @this, void* cb, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTopLevel(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnTopLevelEvents>(cb, false));
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateWindowDelegate(void* @this, void* cb, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateWindow(void* @this, void* cb, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateWindow(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnWindowEvents>(cb, false));
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreatePopupDelegate(void* @this, void* cb, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreatePopup(void* @this, void* cb, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreatePopup(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnWindowEvents>(cb, false));
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreatePlatformThreadingInterfaceDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreatePlatformThreadingInterface(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreatePlatformThreadingInterface();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateStorageProviderDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateStorageProvider(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateStorageProvider();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateScreensDelegate(void* @this, void* cb, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateScreens(void* @this, void* cb, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateScreens(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnScreenEvents>(cb, false));
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateClipboardDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateClipboard(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateClipboard();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateCursorFactoryDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateCursorFactory(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateCursorFactory();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ObtainGlDisplayDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ObtainGlDisplay(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ObtainGlDisplay();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ObtainMetalDisplayDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ObtainMetalDisplay(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ObtainMetalDisplay();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetAppMenuDelegate(void* @this, void* menu);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetAppMenu(void* @this, void* menu)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetAppMenu(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMenu>(menu, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetServicesMenuDelegate(void* @this, void* menu);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetServicesMenu(void* @this, void* menu)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetServicesMenu(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMenu>(menu, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateMenuDelegate(void* @this, void* cb, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateMenu(void* @this, void* cb, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateMenu(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMenuEvents>(cb, false));
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateMenuItemDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateMenuItem(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateMenuItem();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateMenuItemSeparatorDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateMenuItemSeparator(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateMenuItemSeparator();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateTrayIconDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateTrayIcon(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateTrayIcon();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateApplicationCommandsDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateApplicationCommands(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateApplicationCommands();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreatePlatformSettingsDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreatePlatformSettings(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreatePlatformSettings();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreatePlatformBehaviorInhibitionDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreatePlatformBehaviorInhibition(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreatePlatformBehaviorInhibition();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreatePlatformRenderTimerDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreatePlatformRenderTimer(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreatePlatformRenderTimer();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ImportMTLSharedEventDelegate(void* @this, IntPtr idMtlSharedEvent, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ImportMTLSharedEvent(void* @this, IntPtr idMtlSharedEvent, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ImportMTLSharedEvent(idMtlSharedEvent);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateMemoryManagementHelperDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateMemoryManagementHelper(void* @this, void** ppv)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateMemoryManagementHelper();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetDockMenuDelegate(void* @this, void* menu);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetDockMenu(void* @this, void* menu)
        {
            IAvaloniaNativeFactory __target = null;
            try
            {
                {
                    __target = (IAvaloniaNativeFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetDockMenu(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMenu>(menu, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvaloniaNativeFactoryVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, int>)&Initialize); 
#else
            base.AddMethod((InitializeDelegate)Initialize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetMacOptions); 
#else
            base.AddMethod((GetMacOptionsDelegate)GetMacOptions); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&CreateTopLevel); 
#else
            base.AddMethod((CreateTopLevelDelegate)CreateTopLevel); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&CreateWindow); 
#else
            base.AddMethod((CreateWindowDelegate)CreateWindow); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&CreatePopup); 
#else
            base.AddMethod((CreatePopupDelegate)CreatePopup); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreatePlatformThreadingInterface); 
#else
            base.AddMethod((CreatePlatformThreadingInterfaceDelegate)CreatePlatformThreadingInterface); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateStorageProvider); 
#else
            base.AddMethod((CreateStorageProviderDelegate)CreateStorageProvider); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&CreateScreens); 
#else
            base.AddMethod((CreateScreensDelegate)CreateScreens); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateClipboard); 
#else
            base.AddMethod((CreateClipboardDelegate)CreateClipboard); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateCursorFactory); 
#else
            base.AddMethod((CreateCursorFactoryDelegate)CreateCursorFactory); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&ObtainGlDisplay); 
#else
            base.AddMethod((ObtainGlDisplayDelegate)ObtainGlDisplay); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&ObtainMetalDisplay); 
#else
            base.AddMethod((ObtainMetalDisplayDelegate)ObtainMetalDisplay); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetAppMenu); 
#else
            base.AddMethod((SetAppMenuDelegate)SetAppMenu); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetServicesMenu); 
#else
            base.AddMethod((SetServicesMenuDelegate)SetServicesMenu); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&CreateMenu); 
#else
            base.AddMethod((CreateMenuDelegate)CreateMenu); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateMenuItem); 
#else
            base.AddMethod((CreateMenuItemDelegate)CreateMenuItem); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateMenuItemSeparator); 
#else
            base.AddMethod((CreateMenuItemSeparatorDelegate)CreateMenuItemSeparator); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateTrayIcon); 
#else
            base.AddMethod((CreateTrayIconDelegate)CreateTrayIcon); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateApplicationCommands); 
#else
            base.AddMethod((CreateApplicationCommandsDelegate)CreateApplicationCommands); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreatePlatformSettings); 
#else
            base.AddMethod((CreatePlatformSettingsDelegate)CreatePlatformSettings); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreatePlatformBehaviorInhibition); 
#else
            base.AddMethod((CreatePlatformBehaviorInhibitionDelegate)CreatePlatformBehaviorInhibition); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreatePlatformRenderTimer); 
#else
            base.AddMethod((CreatePlatformRenderTimerDelegate)CreatePlatformRenderTimer); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&ImportMTLSharedEvent); 
#else
            base.AddMethod((ImportMTLSharedEventDelegate)ImportMTLSharedEvent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateMemoryManagementHelper); 
#else
            base.AddMethod((CreateMemoryManagementHelperDelegate)CreateMemoryManagementHelper); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetDockMenu); 
#else
            base.AddMethod((SetDockMenuDelegate)SetDockMenu); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvaloniaNativeFactory), new __MicroComIAvaloniaNativeFactoryVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnStringProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnString
    {
        public void* Pointer()
        {
            int __result;
            void* retOut = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &retOut);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Pointer failed", __result);
            return retOut;
        }

        public int Length()
        {
            int __result;
            int ret = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, &ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Length failed", __result);
            return ret;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnString), new Guid("233e094f-9b9f-44a3-9a6e-6948bbdd9fb1"), (p, owns) => new __MicroComIAvnStringProxy(p, owns));
        }

        protected __MicroComIAvnStringProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIAvnStringVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int PointerDelegate(void* @this, void** retOut);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Pointer(void* @this, void** retOut)
        {
            IAvnString __target = null;
            try
            {
                {
                    __target = (IAvnString)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Pointer();
                        *retOut = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int LengthDelegate(void* @this, int* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Length(void* @this, int* ret)
        {
            IAvnString __target = null;
            try
            {
                {
                    __target = (IAvnString)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Length();
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnStringVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&Pointer); 
#else
            base.AddMethod((PointerDelegate)Pointer); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int*, int>)&Length); 
#else
            base.AddMethod((LengthDelegate)Length); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnString), new __MicroComIAvnStringVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnTopLevelProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnTopLevel
    {
        public AvnSize ClientSize
        {
            get
            {
                int __result;
                AvnSize ret = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &ret);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetClientSize failed", __result);
                return ret;
            }
        }

        public double Scaling
        {
            get
            {
                int __result;
                double ret = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, &ret);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetScaling failed", __result);
                return ret;
            }
        }

        public void Invalidate()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 2])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Invalidate failed", __result);
        }

        public AvnPoint PointToClient(AvnPoint point)
        {
            int __result;
            AvnPoint ret = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnPoint, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, point, &ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("PointToClient failed", __result);
            return ret;
        }

        public AvnPoint PointToScreen(AvnPoint point)
        {
            int __result;
            AvnPoint ret = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnPoint, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, point, &ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("PointToScreen failed", __result);
            return ret;
        }

        public void SetCursor(IAvnCursor cursor)
        {
            int __result;
            using var __cursor = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(cursor);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 5])(PPV, __cursor.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetCursor failed", __result);
        }

        public IAvnGlSurfaceRenderTarget CreateGlRenderTarget(IAvnGlContext context)
        {
            int __result;
            using var __context = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(context);
            void* __marshal_ret = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, __context.Pointer, &__marshal_ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateGlRenderTarget failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnGlSurfaceRenderTarget>(__marshal_ret, true);
        }

        public IAvnSoftwareRenderTarget CreateSoftwareRenderTarget()
        {
            int __result;
            void* __marshal_ret = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 7])(PPV, &__marshal_ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateSoftwareRenderTarget failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnSoftwareRenderTarget>(__marshal_ret, true);
        }

        public IAvnMetalRenderTarget CreateMetalRenderTarget(IAvnMetalDevice device)
        {
            int __result;
            using var __device = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(device);
            void* __marshal_ret = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 8])(PPV, __device.Pointer, &__marshal_ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateMetalRenderTarget failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMetalRenderTarget>(__marshal_ret, true);
        }

        public IntPtr ObtainNSViewHandle()
        {
            int __result;
            IntPtr retOut = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 9])(PPV, &retOut);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ObtainNSViewHandle failed", __result);
            return retOut;
        }

        public IntPtr ObtainNSViewHandleRetained()
        {
            int __result;
            IntPtr retOut = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 10])(PPV, &retOut);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ObtainNSViewHandleRetained failed", __result);
            return retOut;
        }

        public IAvnNativeControlHost CreateNativeControlHost()
        {
            int __result;
            void* __marshal_retOut = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 11])(PPV, &__marshal_retOut);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateNativeControlHost failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnNativeControlHost>(__marshal_retOut, true);
        }

        public IAvnTextInputMethod InputMethod
        {
            get
            {
                int __result;
                void* __marshal_ppv = null;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 12])(PPV, &__marshal_ppv);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetInputMethod failed", __result);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnTextInputMethod>(__marshal_ppv, true);
            }
        }

        public void SetTransparencyMode(AvnWindowTransparencyMode mode)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnWindowTransparencyMode, int>)(*PPV)[base.VTableSize + 13])(PPV, mode);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetTransparencyMode failed", __result);
        }

        public uint CurrentDisplayId
        {
            get
            {
                int __result;
                uint ret = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 14])(PPV, &ret);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetCurrentDisplayId failed", __result);
                return ret;
            }
        }

        public void BeginDragAndDropOperation(AvnDragDropEffects effects, AvnPoint point, IAvnClipboardDataSource source, IAvnDndResultCallback callback, IntPtr sourceHandle)
        {
            int __result;
            using var __source = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(source);
            using var __callback = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(callback);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnDragDropEffects, AvnPoint, void*, void*, IntPtr, int>)(*PPV)[base.VTableSize + 15])(PPV, effects, point, __source.Pointer, __callback.Pointer, sourceHandle);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("BeginDragAndDropOperation failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnTopLevel), new Guid("e8cccd3e-e6dc-430a-a0b9-2ce7d7922de6"), (p, owns) => new __MicroComIAvnTopLevelProxy(p, owns));
        }

        protected __MicroComIAvnTopLevelProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 16;
    }

    unsafe class __MicroComIAvnTopLevelVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetClientSizeDelegate(void* @this, AvnSize* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetClientSize(void* @this, AvnSize* ret)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ClientSize;
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetScalingDelegate(void* @this, double* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetScaling(void* @this, double* ret)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Scaling;
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int InvalidateDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Invalidate(void* @this)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Invalidate();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int PointToClientDelegate(void* @this, AvnPoint point, AvnPoint* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int PointToClient(void* @this, AvnPoint point, AvnPoint* ret)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.PointToClient(point);
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int PointToScreenDelegate(void* @this, AvnPoint point, AvnPoint* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int PointToScreen(void* @this, AvnPoint point, AvnPoint* ret)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.PointToScreen(point);
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetCursorDelegate(void* @this, void* cursor);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetCursor(void* @this, void* cursor)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetCursor(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnCursor>(cursor, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateGlRenderTargetDelegate(void* @this, void* context, void** ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateGlRenderTarget(void* @this, void* context, void** ret)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateGlRenderTarget(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnGlContext>(context, false));
                        *ret = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateSoftwareRenderTargetDelegate(void* @this, void** ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateSoftwareRenderTarget(void* @this, void** ret)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateSoftwareRenderTarget();
                        *ret = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateMetalRenderTargetDelegate(void* @this, void* device, void** ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateMetalRenderTarget(void* @this, void* device, void** ret)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateMetalRenderTarget(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMetalDevice>(device, false));
                        *ret = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ObtainNSViewHandleDelegate(void* @this, IntPtr* retOut);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ObtainNSViewHandle(void* @this, IntPtr* retOut)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ObtainNSViewHandle();
                        *retOut = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ObtainNSViewHandleRetainedDelegate(void* @this, IntPtr* retOut);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ObtainNSViewHandleRetained(void* @this, IntPtr* retOut)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ObtainNSViewHandleRetained();
                        *retOut = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateNativeControlHostDelegate(void* @this, void** retOut);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateNativeControlHost(void* @this, void** retOut)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateNativeControlHost();
                        *retOut = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetInputMethodDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetInputMethod(void* @this, void** ppv)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.InputMethod;
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetTransparencyModeDelegate(void* @this, AvnWindowTransparencyMode mode);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetTransparencyMode(void* @this, AvnWindowTransparencyMode mode)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetTransparencyMode(mode);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetCurrentDisplayIdDelegate(void* @this, uint* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetCurrentDisplayId(void* @this, uint* ret)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CurrentDisplayId;
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int BeginDragAndDropOperationDelegate(void* @this, AvnDragDropEffects effects, AvnPoint point, void* source, void* callback, IntPtr sourceHandle);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int BeginDragAndDropOperation(void* @this, AvnDragDropEffects effects, AvnPoint point, void* source, void* callback, IntPtr sourceHandle)
        {
            IAvnTopLevel __target = null;
            try
            {
                {
                    __target = (IAvnTopLevel)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.BeginDragAndDropOperation(effects, point, global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnClipboardDataSource>(source, false), global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnDndResultCallback>(callback, false), sourceHandle);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnTopLevelVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnSize*, int>)&GetClientSize); 
#else
            base.AddMethod((GetClientSizeDelegate)GetClientSize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double*, int>)&GetScaling); 
#else
            base.AddMethod((GetScalingDelegate)GetScaling); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&Invalidate); 
#else
            base.AddMethod((InvalidateDelegate)Invalidate); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnPoint, AvnPoint*, int>)&PointToClient); 
#else
            base.AddMethod((PointToClientDelegate)PointToClient); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnPoint, AvnPoint*, int>)&PointToScreen); 
#else
            base.AddMethod((PointToScreenDelegate)PointToScreen); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetCursor); 
#else
            base.AddMethod((SetCursorDelegate)SetCursor); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&CreateGlRenderTarget); 
#else
            base.AddMethod((CreateGlRenderTargetDelegate)CreateGlRenderTarget); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateSoftwareRenderTarget); 
#else
            base.AddMethod((CreateSoftwareRenderTargetDelegate)CreateSoftwareRenderTarget); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&CreateMetalRenderTarget); 
#else
            base.AddMethod((CreateMetalRenderTargetDelegate)CreateMetalRenderTarget); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr*, int>)&ObtainNSViewHandle); 
#else
            base.AddMethod((ObtainNSViewHandleDelegate)ObtainNSViewHandle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr*, int>)&ObtainNSViewHandleRetained); 
#else
            base.AddMethod((ObtainNSViewHandleRetainedDelegate)ObtainNSViewHandleRetained); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateNativeControlHost); 
#else
            base.AddMethod((CreateNativeControlHostDelegate)CreateNativeControlHost); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&GetInputMethod); 
#else
            base.AddMethod((GetInputMethodDelegate)GetInputMethod); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnWindowTransparencyMode, int>)&SetTransparencyMode); 
#else
            base.AddMethod((SetTransparencyModeDelegate)SetTransparencyMode); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint*, int>)&GetCurrentDisplayId); 
#else
            base.AddMethod((GetCurrentDisplayIdDelegate)GetCurrentDisplayId); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnDragDropEffects, AvnPoint, void*, void*, IntPtr, int>)&BeginDragAndDropOperation); 
#else
            base.AddMethod((BeginDragAndDropOperationDelegate)BeginDragAndDropOperation); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnTopLevel), new __MicroComIAvnTopLevelVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnWindowBaseProxy : __MicroComIAvnTopLevelProxy, IAvnWindowBase
    {
        public void GetFrameSize(AvnSize* result)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, result);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetFrameSize failed", __result);
        }

        public void SetFrameThemeVariant(AvnPlatformThemeVariant mode)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnPlatformThemeVariant, int>)(*PPV)[base.VTableSize + 1])(PPV, mode);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetFrameThemeVariant failed", __result);
        }

        public void SetParent(IAvnWindowBase parent)
        {
            int __result;
            using var __parent = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(parent);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, __parent.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetParent failed", __result);
        }

        public void Show(int activate, int isDialog)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int, int>)(*PPV)[base.VTableSize + 3])(PPV, activate, isDialog);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Show failed", __result);
        }

        public void Hide()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 4])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Hide failed", __result);
        }

        public void Close()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 5])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Close failed", __result);
        }

        public void Activate()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 6])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Activate failed", __result);
        }

        public void SetMinMaxSize(AvnSize minSize, AvnSize maxSize)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnSize, AvnSize, int>)(*PPV)[base.VTableSize + 7])(PPV, minSize, maxSize);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetMinMaxSize failed", __result);
        }

        public void Resize(double width, double height, AvnPlatformResizeReason reason)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, double, double, AvnPlatformResizeReason, int>)(*PPV)[base.VTableSize + 8])(PPV, width, height, reason);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Resize failed", __result);
        }

        public void BeginMoveDrag()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 9])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("BeginMoveDrag failed", __result);
        }

        public void BeginResizeDrag(AvnWindowEdge edge)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnWindowEdge, int>)(*PPV)[base.VTableSize + 10])(PPV, edge);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("BeginResizeDrag failed", __result);
        }

        public AvnPoint Position
        {
            get
            {
                int __result;
                AvnPoint ret = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 11])(PPV, &ret);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetPosition failed", __result);
                return ret;
            }
        }

        public void SetPosition(AvnPoint point)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnPoint, int>)(*PPV)[base.VTableSize + 12])(PPV, point);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetPosition failed", __result);
        }

        public void SetTopMost(int value)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 13])(PPV, value);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetTopMost failed", __result);
        }

        public void SetMainMenu(IAvnMenu menu)
        {
            int __result;
            using var __menu = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(menu);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 14])(PPV, __menu.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetMainMenu failed", __result);
        }

        public IntPtr ObtainNSWindowHandle()
        {
            int __result;
            IntPtr retOut = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 15])(PPV, &retOut);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ObtainNSWindowHandle failed", __result);
            return retOut;
        }

        public IntPtr ObtainNSWindowHandleRetained()
        {
            int __result;
            IntPtr retOut = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 16])(PPV, &retOut);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ObtainNSWindowHandleRetained failed", __result);
            return retOut;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnWindowBase), new Guid("e5aca675-02b7-4129-aa79-d6e417210bda"), (p, owns) => new __MicroComIAvnWindowBaseProxy(p, owns));
        }

        protected __MicroComIAvnWindowBaseProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 17;
    }

    unsafe class __MicroComIAvnWindowBaseVTable : __MicroComIAvnTopLevelVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetFrameSizeDelegate(void* @this, AvnSize* result);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetFrameSize(void* @this, AvnSize* result)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.GetFrameSize(result);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetFrameThemeVariantDelegate(void* @this, AvnPlatformThemeVariant mode);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetFrameThemeVariant(void* @this, AvnPlatformThemeVariant mode)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetFrameThemeVariant(mode);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetParentDelegate(void* @this, void* parent);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetParent(void* @this, void* parent)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetParent(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnWindowBase>(parent, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ShowDelegate(void* @this, int activate, int isDialog);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Show(void* @this, int activate, int isDialog)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Show(activate, isDialog);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int HideDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Hide(void* @this)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Hide();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CloseDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Close(void* @this)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Close();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ActivateDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Activate(void* @this)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Activate();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetMinMaxSizeDelegate(void* @this, AvnSize minSize, AvnSize maxSize);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetMinMaxSize(void* @this, AvnSize minSize, AvnSize maxSize)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetMinMaxSize(minSize, maxSize);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ResizeDelegate(void* @this, double width, double height, AvnPlatformResizeReason reason);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Resize(void* @this, double width, double height, AvnPlatformResizeReason reason)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Resize(width, height, reason);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int BeginMoveDragDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int BeginMoveDrag(void* @this)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.BeginMoveDrag();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int BeginResizeDragDelegate(void* @this, AvnWindowEdge edge);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int BeginResizeDrag(void* @this, AvnWindowEdge edge)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.BeginResizeDrag(edge);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetPositionDelegate(void* @this, AvnPoint* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetPosition(void* @this, AvnPoint* ret)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Position;
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetPositionDelegate(void* @this, AvnPoint point);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetPosition(void* @this, AvnPoint point)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetPosition(point);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetTopMostDelegate(void* @this, int value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetTopMost(void* @this, int value)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetTopMost(value);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetMainMenuDelegate(void* @this, void* menu);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetMainMenu(void* @this, void* menu)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetMainMenu(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMenu>(menu, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ObtainNSWindowHandleDelegate(void* @this, IntPtr* retOut);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ObtainNSWindowHandle(void* @this, IntPtr* retOut)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ObtainNSWindowHandle();
                        *retOut = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ObtainNSWindowHandleRetainedDelegate(void* @this, IntPtr* retOut);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ObtainNSWindowHandleRetained(void* @this, IntPtr* retOut)
        {
            IAvnWindowBase __target = null;
            try
            {
                {
                    __target = (IAvnWindowBase)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ObtainNSWindowHandleRetained();
                        *retOut = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnWindowBaseVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnSize*, int>)&GetFrameSize); 
#else
            base.AddMethod((GetFrameSizeDelegate)GetFrameSize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnPlatformThemeVariant, int>)&SetFrameThemeVariant); 
#else
            base.AddMethod((SetFrameThemeVariantDelegate)SetFrameThemeVariant); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetParent); 
#else
            base.AddMethod((SetParentDelegate)SetParent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int, int>)&Show); 
#else
            base.AddMethod((ShowDelegate)Show); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&Hide); 
#else
            base.AddMethod((HideDelegate)Hide); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&Close); 
#else
            base.AddMethod((CloseDelegate)Close); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&Activate); 
#else
            base.AddMethod((ActivateDelegate)Activate); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnSize, AvnSize, int>)&SetMinMaxSize); 
#else
            base.AddMethod((SetMinMaxSizeDelegate)SetMinMaxSize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double, double, AvnPlatformResizeReason, int>)&Resize); 
#else
            base.AddMethod((ResizeDelegate)Resize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&BeginMoveDrag); 
#else
            base.AddMethod((BeginMoveDragDelegate)BeginMoveDrag); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnWindowEdge, int>)&BeginResizeDrag); 
#else
            base.AddMethod((BeginResizeDragDelegate)BeginResizeDrag); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnPoint*, int>)&GetPosition); 
#else
            base.AddMethod((GetPositionDelegate)GetPosition); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnPoint, int>)&SetPosition); 
#else
            base.AddMethod((SetPositionDelegate)SetPosition); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetTopMost); 
#else
            base.AddMethod((SetTopMostDelegate)SetTopMost); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetMainMenu); 
#else
            base.AddMethod((SetMainMenuDelegate)SetMainMenu); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr*, int>)&ObtainNSWindowHandle); 
#else
            base.AddMethod((ObtainNSWindowHandleDelegate)ObtainNSWindowHandle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr*, int>)&ObtainNSWindowHandleRetained); 
#else
            base.AddMethod((ObtainNSWindowHandleRetainedDelegate)ObtainNSWindowHandleRetained); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnWindowBase), new __MicroComIAvnWindowBaseVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnPopupProxy : __MicroComIAvnWindowBaseProxy, IAvnPopup
    {
        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnPopup), new Guid("83e588f3-6981-4e48-9ea0-e1e569f79a91"), (p, owns) => new __MicroComIAvnPopupProxy(p, owns));
        }

        protected __MicroComIAvnPopupProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 0;
    }

    unsafe class __MicroComIAvnPopupVTable : __MicroComIAvnWindowBaseVTable
    {
        protected __MicroComIAvnPopupVTable()
        {
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnPopup), new __MicroComIAvnPopupVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnWindowProxy : __MicroComIAvnWindowBaseProxy, IAvnWindow
    {
        public void SetEnabled(int enable)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 0])(PPV, enable);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetEnabled failed", __result);
        }

        public void SetCanResize(int value)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 1])(PPV, value);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetCanResize failed", __result);
        }

        public void SetCanMinimize(int value)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 2])(PPV, value);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetCanMinimize failed", __result);
        }

        public void SetCanMaximize(int value)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 3])(PPV, value);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetCanMaximize failed", __result);
        }

        public void SetDecorations(SystemDecorations value)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, SystemDecorations, int>)(*PPV)[base.VTableSize + 4])(PPV, value);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetDecorations failed", __result);
        }

        public void SetTitle(string utf8Title)
        {
            int __result;
            var __bytemarshal_utf8Title = new byte[System.Text.Encoding.UTF8.GetByteCount(utf8Title) + 1];
            System.Text.Encoding.UTF8.GetBytes(utf8Title, 0, utf8Title.Length, __bytemarshal_utf8Title, 0);
            fixed (byte* __fixedmarshal_utf8Title = __bytemarshal_utf8Title)
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 5])(PPV, __fixedmarshal_utf8Title);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetTitle failed", __result);
        }

        public void SetTitleBarColor(AvnColor color)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnColor, int>)(*PPV)[base.VTableSize + 6])(PPV, color);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetTitleBarColor failed", __result);
        }

        public void SetWindowState(AvnWindowState state)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnWindowState, int>)(*PPV)[base.VTableSize + 7])(PPV, state);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetWindowState failed", __result);
        }

        public AvnWindowState WindowState
        {
            get
            {
                int __result;
                AvnWindowState ret = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 8])(PPV, &ret);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetWindowState failed", __result);
                return ret;
            }
        }

        public void TakeFocusFromChildren()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 9])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("TakeFocusFromChildren failed", __result);
        }

        public void SetExtendClientArea(int enable)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 10])(PPV, enable);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetExtendClientArea failed", __result);
        }

        public double ExtendTitleBarHeight
        {
            get
            {
                int __result;
                double ret = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 11])(PPV, &ret);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetExtendTitleBarHeight failed", __result);
                return ret;
            }
        }

        public void SetExtendTitleBarHeight(double value)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, double, int>)(*PPV)[base.VTableSize + 12])(PPV, value);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetExtendTitleBarHeight failed", __result);
        }

        public IntPtr WindowZOrder
        {
            get
            {
                int __result;
                IntPtr ret = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 13])(PPV, &ret);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetWindowZOrder failed", __result);
                return ret;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnWindow), new Guid("cab661de-49d6-4ead-b59c-eac9b2b6c28d"), (p, owns) => new __MicroComIAvnWindowProxy(p, owns));
        }

        protected __MicroComIAvnWindowProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 14;
    }

    unsafe class __MicroComIAvnWindowVTable : __MicroComIAvnWindowBaseVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetEnabledDelegate(void* @this, int enable);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetEnabled(void* @this, int enable)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetEnabled(enable);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetCanResizeDelegate(void* @this, int value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetCanResize(void* @this, int value)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetCanResize(value);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetCanMinimizeDelegate(void* @this, int value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetCanMinimize(void* @this, int value)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetCanMinimize(value);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetCanMaximizeDelegate(void* @this, int value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetCanMaximize(void* @this, int value)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetCanMaximize(value);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetDecorationsDelegate(void* @this, SystemDecorations value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetDecorations(void* @this, SystemDecorations value)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetDecorations(value);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetTitleDelegate(void* @this, byte* utf8Title);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetTitle(void* @this, byte* utf8Title)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetTitle((utf8Title == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(utf8Title))));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetTitleBarColorDelegate(void* @this, AvnColor color);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetTitleBarColor(void* @this, AvnColor color)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetTitleBarColor(color);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetWindowStateDelegate(void* @this, AvnWindowState state);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetWindowState(void* @this, AvnWindowState state)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetWindowState(state);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetWindowStateDelegate(void* @this, AvnWindowState* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetWindowState(void* @this, AvnWindowState* ret)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.WindowState;
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int TakeFocusFromChildrenDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int TakeFocusFromChildren(void* @this)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.TakeFocusFromChildren();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetExtendClientAreaDelegate(void* @this, int enable);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetExtendClientArea(void* @this, int enable)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetExtendClientArea(enable);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetExtendTitleBarHeightDelegate(void* @this, double* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetExtendTitleBarHeight(void* @this, double* ret)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ExtendTitleBarHeight;
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetExtendTitleBarHeightDelegate(void* @this, double value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetExtendTitleBarHeight(void* @this, double value)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetExtendTitleBarHeight(value);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetWindowZOrderDelegate(void* @this, IntPtr* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetWindowZOrder(void* @this, IntPtr* ret)
        {
            IAvnWindow __target = null;
            try
            {
                {
                    __target = (IAvnWindow)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.WindowZOrder;
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnWindowVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetEnabled); 
#else
            base.AddMethod((SetEnabledDelegate)SetEnabled); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetCanResize); 
#else
            base.AddMethod((SetCanResizeDelegate)SetCanResize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetCanMinimize); 
#else
            base.AddMethod((SetCanMinimizeDelegate)SetCanMinimize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetCanMaximize); 
#else
            base.AddMethod((SetCanMaximizeDelegate)SetCanMaximize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, SystemDecorations, int>)&SetDecorations); 
#else
            base.AddMethod((SetDecorationsDelegate)SetDecorations); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, byte*, int>)&SetTitle); 
#else
            base.AddMethod((SetTitleDelegate)SetTitle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnColor, int>)&SetTitleBarColor); 
#else
            base.AddMethod((SetTitleBarColorDelegate)SetTitleBarColor); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnWindowState, int>)&SetWindowState); 
#else
            base.AddMethod((SetWindowStateDelegate)SetWindowState); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnWindowState*, int>)&GetWindowState); 
#else
            base.AddMethod((GetWindowStateDelegate)GetWindowState); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&TakeFocusFromChildren); 
#else
            base.AddMethod((TakeFocusFromChildrenDelegate)TakeFocusFromChildren); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetExtendClientArea); 
#else
            base.AddMethod((SetExtendClientAreaDelegate)SetExtendClientArea); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double*, int>)&GetExtendTitleBarHeight); 
#else
            base.AddMethod((GetExtendTitleBarHeightDelegate)GetExtendTitleBarHeight); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double, int>)&SetExtendTitleBarHeight); 
#else
            base.AddMethod((SetExtendTitleBarHeightDelegate)SetExtendTitleBarHeight); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr*, int>)&GetWindowZOrder); 
#else
            base.AddMethod((GetWindowZOrderDelegate)GetWindowZOrder); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnWindow), new __MicroComIAvnWindowVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnTopLevelEventsProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnTopLevelEvents
    {
        public void Closed()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 0])(PPV);
        }

        public void Paint()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 1])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Paint failed", __result);
        }

        public void Resized(AvnSize* size, AvnPlatformResizeReason reason)
        {
            ((delegate* unmanaged[Stdcall]<void*, void*, AvnPlatformResizeReason, void>)(*PPV)[base.VTableSize + 2])(PPV, size, reason);
        }

        public void RawMouseEvent(AvnRawMouseEventType type, AvnPointerDeviceType deviceType, ulong timeStamp, AvnInputModifiers modifiers, AvnPoint point, AvnVector delta, float pressure, float xTilt, float yTilt)
        {
            ((delegate* unmanaged[Stdcall]<void*, AvnRawMouseEventType, AvnPointerDeviceType, ulong, AvnInputModifiers, AvnPoint, AvnVector, float, float, float, void>)(*PPV)[base.VTableSize + 3])(PPV, type, deviceType, timeStamp, modifiers, point, delta, pressure, xTilt, yTilt);
        }

        public int RawKeyEvent(AvnRawKeyEventType type, ulong timeStamp, AvnInputModifiers modifiers, AvnKey key, AvnPhysicalKey physicalKey, string keySymbol)
        {
            int __result;
            var __bytemarshal_keySymbol = new byte[System.Text.Encoding.UTF8.GetByteCount(keySymbol) + 1];
            System.Text.Encoding.UTF8.GetBytes(keySymbol, 0, keySymbol.Length, __bytemarshal_keySymbol, 0);
            fixed (byte* __fixedmarshal_keySymbol = __bytemarshal_keySymbol)
                __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnRawKeyEventType, ulong, AvnInputModifiers, AvnKey, AvnPhysicalKey, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, type, timeStamp, modifiers, key, physicalKey, __fixedmarshal_keySymbol);
            return __result;
        }

        public int RawTextInputEvent(ulong timeStamp, string text)
        {
            int __result;
            var __bytemarshal_text = new byte[System.Text.Encoding.UTF8.GetByteCount(text) + 1];
            System.Text.Encoding.UTF8.GetBytes(text, 0, text.Length, __bytemarshal_text, 0);
            fixed (byte* __fixedmarshal_text = __bytemarshal_text)
                __result = (int)((delegate* unmanaged[Stdcall]<void*, ulong, void*, int>)(*PPV)[base.VTableSize + 5])(PPV, timeStamp, __fixedmarshal_text);
            return __result;
        }

        public void ScalingChanged(double scaling)
        {
            ((delegate* unmanaged[Stdcall]<void*, double, void>)(*PPV)[base.VTableSize + 6])(PPV, scaling);
        }

        public void RunRenderPriorityJobs()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 7])(PPV);
        }

        public void LostFocus()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 8])(PPV);
        }

        public IAvnAutomationPeer AutomationPeer
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 9])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationPeer>(__result, true);
            }
        }

        public AvnDragDropEffects DragEvent(AvnDragEventType type, AvnPoint position, AvnInputModifiers modifiers, AvnDragDropEffects effects, IAvnClipboard clipboard, IntPtr dataTransferHandle)
        {
            AvnDragDropEffects __result;
            using var __clipboard = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(clipboard);
            __result = (AvnDragDropEffects)((delegate* unmanaged[Stdcall]<void*, AvnDragEventType, AvnPoint, AvnInputModifiers, AvnDragDropEffects, void*, IntPtr, AvnDragDropEffects>)(*PPV)[base.VTableSize + 10])(PPV, type, position, modifiers, effects, __clipboard.Pointer, dataTransferHandle);
            return __result;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnTopLevelEvents), new Guid("fda9c1b3-69e0-43d7-9459-8cc97cb41f6a"), (p, owns) => new __MicroComIAvnTopLevelEventsProxy(p, owns));
        }

        protected __MicroComIAvnTopLevelEventsProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 11;
    }

    unsafe class __MicroComIAvnTopLevelEventsVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ClosedDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Closed(void* @this)
        {
            IAvnTopLevelEvents __target = null;
            try
            {
                {
                    __target = (IAvnTopLevelEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Closed();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int PaintDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Paint(void* @this)
        {
            IAvnTopLevelEvents __target = null;
            try
            {
                {
                    __target = (IAvnTopLevelEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Paint();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ResizedDelegate(void* @this, AvnSize* size, AvnPlatformResizeReason reason);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Resized(void* @this, AvnSize* size, AvnPlatformResizeReason reason)
        {
            IAvnTopLevelEvents __target = null;
            try
            {
                {
                    __target = (IAvnTopLevelEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Resized(size, reason);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void RawMouseEventDelegate(void* @this, AvnRawMouseEventType type, AvnPointerDeviceType deviceType, ulong timeStamp, AvnInputModifiers modifiers, AvnPoint point, AvnVector delta, float pressure, float xTilt, float yTilt);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void RawMouseEvent(void* @this, AvnRawMouseEventType type, AvnPointerDeviceType deviceType, ulong timeStamp, AvnInputModifiers modifiers, AvnPoint point, AvnVector delta, float pressure, float xTilt, float yTilt)
        {
            IAvnTopLevelEvents __target = null;
            try
            {
                {
                    __target = (IAvnTopLevelEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.RawMouseEvent(type, deviceType, timeStamp, modifiers, point, delta, pressure, xTilt, yTilt);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int RawKeyEventDelegate(void* @this, AvnRawKeyEventType type, ulong timeStamp, AvnInputModifiers modifiers, AvnKey key, AvnPhysicalKey physicalKey, byte* keySymbol);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int RawKeyEvent(void* @this, AvnRawKeyEventType type, ulong timeStamp, AvnInputModifiers modifiers, AvnKey key, AvnPhysicalKey physicalKey, byte* keySymbol)
        {
            IAvnTopLevelEvents __target = null;
            try
            {
                {
                    __target = (IAvnTopLevelEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RawKeyEvent(type, timeStamp, modifiers, key, physicalKey, (keySymbol == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(keySymbol))));
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int RawTextInputEventDelegate(void* @this, ulong timeStamp, byte* text);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int RawTextInputEvent(void* @this, ulong timeStamp, byte* text)
        {
            IAvnTopLevelEvents __target = null;
            try
            {
                {
                    __target = (IAvnTopLevelEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RawTextInputEvent(timeStamp, (text == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(text))));
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ScalingChangedDelegate(void* @this, double scaling);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ScalingChanged(void* @this, double scaling)
        {
            IAvnTopLevelEvents __target = null;
            try
            {
                {
                    __target = (IAvnTopLevelEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ScalingChanged(scaling);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void RunRenderPriorityJobsDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void RunRenderPriorityJobs(void* @this)
        {
            IAvnTopLevelEvents __target = null;
            try
            {
                {
                    __target = (IAvnTopLevelEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.RunRenderPriorityJobs();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void LostFocusDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void LostFocus(void* @this)
        {
            IAvnTopLevelEvents __target = null;
            try
            {
                {
                    __target = (IAvnTopLevelEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.LostFocus();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetAutomationPeerDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetAutomationPeer(void* @this)
        {
            IAvnTopLevelEvents __target = null;
            try
            {
                {
                    __target = (IAvnTopLevelEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.AutomationPeer;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate AvnDragDropEffects DragEventDelegate(void* @this, AvnDragEventType type, AvnPoint position, AvnInputModifiers modifiers, AvnDragDropEffects effects, void* clipboard, IntPtr dataTransferHandle);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static AvnDragDropEffects DragEvent(void* @this, AvnDragEventType type, AvnPoint position, AvnInputModifiers modifiers, AvnDragDropEffects effects, void* clipboard, IntPtr dataTransferHandle)
        {
            IAvnTopLevelEvents __target = null;
            try
            {
                {
                    __target = (IAvnTopLevelEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.DragEvent(type, position, modifiers, effects, global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnClipboard>(clipboard, false), dataTransferHandle);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComIAvnTopLevelEventsVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Closed); 
#else
            base.AddMethod((ClosedDelegate)Closed); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&Paint); 
#else
            base.AddMethod((PaintDelegate)Paint); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnSize*, AvnPlatformResizeReason, void>)&Resized); 
#else
            base.AddMethod((ResizedDelegate)Resized); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnRawMouseEventType, AvnPointerDeviceType, ulong, AvnInputModifiers, AvnPoint, AvnVector, float, float, float, void>)&RawMouseEvent); 
#else
            base.AddMethod((RawMouseEventDelegate)RawMouseEvent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnRawKeyEventType, ulong, AvnInputModifiers, AvnKey, AvnPhysicalKey, byte*, int>)&RawKeyEvent); 
#else
            base.AddMethod((RawKeyEventDelegate)RawKeyEvent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, ulong, byte*, int>)&RawTextInputEvent); 
#else
            base.AddMethod((RawTextInputEventDelegate)RawTextInputEvent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double, void>)&ScalingChanged); 
#else
            base.AddMethod((ScalingChangedDelegate)ScalingChanged); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&RunRenderPriorityJobs); 
#else
            base.AddMethod((RunRenderPriorityJobsDelegate)RunRenderPriorityJobs); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&LostFocus); 
#else
            base.AddMethod((LostFocusDelegate)LostFocus); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetAutomationPeer); 
#else
            base.AddMethod((GetAutomationPeerDelegate)GetAutomationPeer); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnDragEventType, AvnPoint, AvnInputModifiers, AvnDragDropEffects, void*, IntPtr, AvnDragDropEffects>)&DragEvent); 
#else
            base.AddMethod((DragEventDelegate)DragEvent); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnTopLevelEvents), new __MicroComIAvnTopLevelEventsVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnWindowBaseEventsProxy : __MicroComIAvnTopLevelEventsProxy, IAvnWindowBaseEvents
    {
        public void Activated()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 0])(PPV);
        }

        public void Deactivated()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 1])(PPV);
        }

        public void PositionChanged(AvnPoint position)
        {
            ((delegate* unmanaged[Stdcall]<void*, AvnPoint, void>)(*PPV)[base.VTableSize + 2])(PPV, position);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnWindowBaseEvents), new Guid("939b6599-40a8-4710-a4c8-5d72d8f174fb"), (p, owns) => new __MicroComIAvnWindowBaseEventsProxy(p, owns));
        }

        protected __MicroComIAvnWindowBaseEventsProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 3;
    }

    unsafe class __MicroComIAvnWindowBaseEventsVTable : __MicroComIAvnTopLevelEventsVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ActivatedDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Activated(void* @this)
        {
            IAvnWindowBaseEvents __target = null;
            try
            {
                {
                    __target = (IAvnWindowBaseEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Activated();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void DeactivatedDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Deactivated(void* @this)
        {
            IAvnWindowBaseEvents __target = null;
            try
            {
                {
                    __target = (IAvnWindowBaseEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Deactivated();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void PositionChangedDelegate(void* @this, AvnPoint position);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void PositionChanged(void* @this, AvnPoint position)
        {
            IAvnWindowBaseEvents __target = null;
            try
            {
                {
                    __target = (IAvnWindowBaseEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.PositionChanged(position);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnWindowBaseEventsVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Activated); 
#else
            base.AddMethod((ActivatedDelegate)Activated); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Deactivated); 
#else
            base.AddMethod((DeactivatedDelegate)Deactivated); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnPoint, void>)&PositionChanged); 
#else
            base.AddMethod((PositionChangedDelegate)PositionChanged); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnWindowBaseEvents), new __MicroComIAvnWindowBaseEventsVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnWindowEventsProxy : __MicroComIAvnWindowBaseEventsProxy, IAvnWindowEvents
    {
        public int Closing()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 0])(PPV);
            return __result;
        }

        public void WindowStateChanged(AvnWindowState state)
        {
            ((delegate* unmanaged[Stdcall]<void*, AvnWindowState, void>)(*PPV)[base.VTableSize + 1])(PPV, state);
        }

        public void GotInputWhenDisabled()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 2])(PPV);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnWindowEvents), new Guid("1ae178ee-1fcc-447f-b6dd-b7bb727f934c"), (p, owns) => new __MicroComIAvnWindowEventsProxy(p, owns));
        }

        protected __MicroComIAvnWindowEventsProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 3;
    }

    unsafe class __MicroComIAvnWindowEventsVTable : __MicroComIAvnWindowBaseEventsVTable
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ClosingDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Closing(void* @this)
        {
            IAvnWindowEvents __target = null;
            try
            {
                {
                    __target = (IAvnWindowEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Closing();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void WindowStateChangedDelegate(void* @this, AvnWindowState state);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void WindowStateChanged(void* @this, AvnWindowState state)
        {
            IAvnWindowEvents __target = null;
            try
            {
                {
                    __target = (IAvnWindowEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.WindowStateChanged(state);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void GotInputWhenDisabledDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void GotInputWhenDisabled(void* @this)
        {
            IAvnWindowEvents __target = null;
            try
            {
                {
                    __target = (IAvnWindowEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.GotInputWhenDisabled();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnWindowEventsVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&Closing); 
#else
            base.AddMethod((ClosingDelegate)Closing); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnWindowState, void>)&WindowStateChanged); 
#else
            base.AddMethod((WindowStateChangedDelegate)WindowStateChanged); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&GotInputWhenDisabled); 
#else
            base.AddMethod((GotInputWhenDisabledDelegate)GotInputWhenDisabled); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnWindowEvents), new __MicroComIAvnWindowEventsVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnTextInputMethodClientProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnTextInputMethodClient
    {
        public void SetPreeditText(string preeditText)
        {
            var __bytemarshal_preeditText = new byte[System.Text.Encoding.UTF8.GetByteCount(preeditText) + 1];
            System.Text.Encoding.UTF8.GetBytes(preeditText, 0, preeditText.Length, __bytemarshal_preeditText, 0);
            fixed (byte* __fixedmarshal_preeditText = __bytemarshal_preeditText)
                ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 0])(PPV, __fixedmarshal_preeditText);
        }

        public void SelectInSurroundingText(int start, int length)
        {
            ((delegate* unmanaged[Stdcall]<void*, int, int, void>)(*PPV)[base.VTableSize + 1])(PPV, start, length);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnTextInputMethodClient), new Guid("f2079145-a2d9-42b8-a85e-2732e3c2b055"), (p, owns) => new __MicroComIAvnTextInputMethodClientProxy(p, owns));
        }

        protected __MicroComIAvnTextInputMethodClientProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIAvnTextInputMethodClientVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SetPreeditTextDelegate(void* @this, byte* preeditText);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void SetPreeditText(void* @this, byte* preeditText)
        {
            IAvnTextInputMethodClient __target = null;
            try
            {
                {
                    __target = (IAvnTextInputMethodClient)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetPreeditText((preeditText == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(preeditText))));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SelectInSurroundingTextDelegate(void* @this, int start, int length);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void SelectInSurroundingText(void* @this, int start, int length)
        {
            IAvnTextInputMethodClient __target = null;
            try
            {
                {
                    __target = (IAvnTextInputMethodClient)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SelectInSurroundingText(start, length);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnTextInputMethodClientVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, byte*, void>)&SetPreeditText); 
#else
            base.AddMethod((SetPreeditTextDelegate)SetPreeditText); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int, void>)&SelectInSurroundingText); 
#else
            base.AddMethod((SelectInSurroundingTextDelegate)SelectInSurroundingText); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnTextInputMethodClient), new __MicroComIAvnTextInputMethodClientVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnTextInputMethodProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnTextInputMethod
    {
        public void SetClient(IAvnTextInputMethodClient client)
        {
            int __result;
            using var __client = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(client);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, __client.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetClient failed", __result);
        }

        public void Reset()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 1])(PPV);
        }

        public void SetCursorRect(AvnRect rect)
        {
            ((delegate* unmanaged[Stdcall]<void*, AvnRect, void>)(*PPV)[base.VTableSize + 2])(PPV, rect);
        }

        public void SetSurroundingText(string text, int anchorOffset, int cursorOffset)
        {
            var __bytemarshal_text = new byte[System.Text.Encoding.UTF8.GetByteCount(text) + 1];
            System.Text.Encoding.UTF8.GetBytes(text, 0, text.Length, __bytemarshal_text, 0);
            fixed (byte* __fixedmarshal_text = __bytemarshal_text)
                ((delegate* unmanaged[Stdcall]<void*, void*, int, int, void>)(*PPV)[base.VTableSize + 3])(PPV, __fixedmarshal_text, anchorOffset, cursorOffset);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnTextInputMethod), new Guid("1382a29f-e260-4c7a-b83f-c99fc72e27c2"), (p, owns) => new __MicroComIAvnTextInputMethodProxy(p, owns));
        }

        protected __MicroComIAvnTextInputMethodProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 4;
    }

    unsafe class __MicroComIAvnTextInputMethodVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetClientDelegate(void* @this, void* client);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetClient(void* @this, void* client)
        {
            IAvnTextInputMethod __target = null;
            try
            {
                {
                    __target = (IAvnTextInputMethod)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetClient(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnTextInputMethodClient>(client, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ResetDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Reset(void* @this)
        {
            IAvnTextInputMethod __target = null;
            try
            {
                {
                    __target = (IAvnTextInputMethod)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Reset();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SetCursorRectDelegate(void* @this, AvnRect rect);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void SetCursorRect(void* @this, AvnRect rect)
        {
            IAvnTextInputMethod __target = null;
            try
            {
                {
                    __target = (IAvnTextInputMethod)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetCursorRect(rect);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SetSurroundingTextDelegate(void* @this, byte* text, int anchorOffset, int cursorOffset);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void SetSurroundingText(void* @this, byte* text, int anchorOffset, int cursorOffset)
        {
            IAvnTextInputMethod __target = null;
            try
            {
                {
                    __target = (IAvnTextInputMethod)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetSurroundingText((text == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(text))), anchorOffset, cursorOffset);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnTextInputMethodVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetClient); 
#else
            base.AddMethod((SetClientDelegate)SetClient); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Reset); 
#else
            base.AddMethod((ResetDelegate)Reset); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnRect, void>)&SetCursorRect); 
#else
            base.AddMethod((SetCursorRectDelegate)SetCursorRect); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, byte*, int, int, void>)&SetSurroundingText); 
#else
            base.AddMethod((SetSurroundingTextDelegate)SetSurroundingText); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnTextInputMethod), new __MicroComIAvnTextInputMethodVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnMacOptionsProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnMacOptions
    {
        public void SetShowInDock(int show)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 0])(PPV, show);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetShowInDock failed", __result);
        }

        public void SetApplicationTitle(string utf8string)
        {
            int __result;
            var __bytemarshal_utf8string = new byte[System.Text.Encoding.UTF8.GetByteCount(utf8string) + 1];
            System.Text.Encoding.UTF8.GetBytes(utf8string, 0, utf8string.Length, __bytemarshal_utf8string, 0);
            fixed (byte* __fixedmarshal_utf8string = __bytemarshal_utf8string)
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, __fixedmarshal_utf8string);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetApplicationTitle failed", __result);
        }

        public void SetDisableSetProcessName(int disable)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 2])(PPV, disable);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetDisableSetProcessName failed", __result);
        }

        public void SetDisableAppDelegate(int disable)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 3])(PPV, disable);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetDisableAppDelegate failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnMacOptions), new Guid("e34ae0f8-18b4-48a3-b09d-2e6b19a3cf5e"), (p, owns) => new __MicroComIAvnMacOptionsProxy(p, owns));
        }

        protected __MicroComIAvnMacOptionsProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 4;
    }

    unsafe class __MicroComIAvnMacOptionsVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetShowInDockDelegate(void* @this, int show);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetShowInDock(void* @this, int show)
        {
            IAvnMacOptions __target = null;
            try
            {
                {
                    __target = (IAvnMacOptions)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetShowInDock(show);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetApplicationTitleDelegate(void* @this, byte* utf8string);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetApplicationTitle(void* @this, byte* utf8string)
        {
            IAvnMacOptions __target = null;
            try
            {
                {
                    __target = (IAvnMacOptions)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetApplicationTitle((utf8string == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(utf8string))));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetDisableSetProcessNameDelegate(void* @this, int disable);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetDisableSetProcessName(void* @this, int disable)
        {
            IAvnMacOptions __target = null;
            try
            {
                {
                    __target = (IAvnMacOptions)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetDisableSetProcessName(disable);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetDisableAppDelegateDelegate(void* @this, int disable);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetDisableAppDelegate(void* @this, int disable)
        {
            IAvnMacOptions __target = null;
            try
            {
                {
                    __target = (IAvnMacOptions)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetDisableAppDelegate(disable);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnMacOptionsVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetShowInDock); 
#else
            base.AddMethod((SetShowInDockDelegate)SetShowInDock); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, byte*, int>)&SetApplicationTitle); 
#else
            base.AddMethod((SetApplicationTitleDelegate)SetApplicationTitle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetDisableSetProcessName); 
#else
            base.AddMethod((SetDisableSetProcessNameDelegate)SetDisableSetProcessName); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetDisableAppDelegate); 
#else
            base.AddMethod((SetDisableAppDelegateDelegate)SetDisableAppDelegate); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnMacOptions), new __MicroComIAvnMacOptionsVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnActionCallbackProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnActionCallback
    {
        public void Run()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 0])(PPV);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnActionCallback), new Guid("04c1b049-1f43-418a-9159-cae627ec1367"), (p, owns) => new __MicroComIAvnActionCallbackProxy(p, owns));
        }

        protected __MicroComIAvnActionCallbackProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIAvnActionCallbackVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void RunDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Run(void* @this)
        {
            IAvnActionCallback __target = null;
            try
            {
                {
                    __target = (IAvnActionCallback)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Run();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnActionCallbackVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Run); 
#else
            base.AddMethod((RunDelegate)Run); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnActionCallback), new __MicroComIAvnActionCallbackVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnPlatformThreadingInterfaceEventsProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnPlatformThreadingInterfaceEvents
    {
        public void Signaled()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 0])(PPV);
        }

        public void Timer()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 1])(PPV);
        }

        public void ReadyForBackgroundProcessing()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 2])(PPV);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnPlatformThreadingInterfaceEvents), new Guid("6df4d2db-0b80-4f59-ad88-0baa5e21eb14"), (p, owns) => new __MicroComIAvnPlatformThreadingInterfaceEventsProxy(p, owns));
        }

        protected __MicroComIAvnPlatformThreadingInterfaceEventsProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 3;
    }

    unsafe class __MicroComIAvnPlatformThreadingInterfaceEventsVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SignaledDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Signaled(void* @this)
        {
            IAvnPlatformThreadingInterfaceEvents __target = null;
            try
            {
                {
                    __target = (IAvnPlatformThreadingInterfaceEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Signaled();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void TimerDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Timer(void* @this)
        {
            IAvnPlatformThreadingInterfaceEvents __target = null;
            try
            {
                {
                    __target = (IAvnPlatformThreadingInterfaceEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Timer();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ReadyForBackgroundProcessingDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ReadyForBackgroundProcessing(void* @this)
        {
            IAvnPlatformThreadingInterfaceEvents __target = null;
            try
            {
                {
                    __target = (IAvnPlatformThreadingInterfaceEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ReadyForBackgroundProcessing();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnPlatformThreadingInterfaceEventsVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Signaled); 
#else
            base.AddMethod((SignaledDelegate)Signaled); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Timer); 
#else
            base.AddMethod((TimerDelegate)Timer); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&ReadyForBackgroundProcessing); 
#else
            base.AddMethod((ReadyForBackgroundProcessingDelegate)ReadyForBackgroundProcessing); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnPlatformThreadingInterfaceEvents), new __MicroComIAvnPlatformThreadingInterfaceEventsVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnLoopCancellationProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnLoopCancellation
    {
        public void Cancel()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 0])(PPV);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnLoopCancellation), new Guid("97330f88-c22b-4a8e-a130-201520091b01"), (p, owns) => new __MicroComIAvnLoopCancellationProxy(p, owns));
        }

        protected __MicroComIAvnLoopCancellationProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIAvnLoopCancellationVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void CancelDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Cancel(void* @this)
        {
            IAvnLoopCancellation __target = null;
            try
            {
                {
                    __target = (IAvnLoopCancellation)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Cancel();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnLoopCancellationVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Cancel); 
#else
            base.AddMethod((CancelDelegate)Cancel); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnLoopCancellation), new __MicroComIAvnLoopCancellationVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnPlatformThreadingInterfaceProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnPlatformThreadingInterface
    {
        public int CurrentThreadIsLoopThread
        {
            get
            {
                int __result;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 0])(PPV);
                return __result;
            }
        }

        public void SetEvents(IAvnPlatformThreadingInterfaceEvents cb)
        {
            using var __cb = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(cb);
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 1])(PPV, __cb.Pointer);
        }

        public IAvnLoopCancellation CreateLoopCancellation()
        {
            void* __result;
            __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 2])(PPV);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnLoopCancellation>(__result, true);
        }

        public void RunLoop(IAvnLoopCancellation cancel)
        {
            using var __cancel = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(cancel);
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 3])(PPV, __cancel.Pointer);
        }

        public void Signal()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 4])(PPV);
        }

        public void UpdateTimer(int ms)
        {
            ((delegate* unmanaged[Stdcall]<void*, int, void>)(*PPV)[base.VTableSize + 5])(PPV, ms);
        }

        public void RequestBackgroundProcessing()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 6])(PPV);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnPlatformThreadingInterface), new Guid("fbc06f3d-7860-42df-83fd-53c4b02dd9c3"), (p, owns) => new __MicroComIAvnPlatformThreadingInterfaceProxy(p, owns));
        }

        protected __MicroComIAvnPlatformThreadingInterfaceProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 7;
    }

    unsafe class __MicroComIAvnPlatformThreadingInterfaceVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetCurrentThreadIsLoopThreadDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetCurrentThreadIsLoopThread(void* @this)
        {
            IAvnPlatformThreadingInterface __target = null;
            try
            {
                {
                    __target = (IAvnPlatformThreadingInterface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CurrentThreadIsLoopThread;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SetEventsDelegate(void* @this, void* cb);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void SetEvents(void* @this, void* cb)
        {
            IAvnPlatformThreadingInterface __target = null;
            try
            {
                {
                    __target = (IAvnPlatformThreadingInterface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetEvents(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnPlatformThreadingInterfaceEvents>(cb, false));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* CreateLoopCancellationDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* CreateLoopCancellation(void* @this)
        {
            IAvnPlatformThreadingInterface __target = null;
            try
            {
                {
                    __target = (IAvnPlatformThreadingInterface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateLoopCancellation();
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void RunLoopDelegate(void* @this, void* cancel);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void RunLoop(void* @this, void* cancel)
        {
            IAvnPlatformThreadingInterface __target = null;
            try
            {
                {
                    __target = (IAvnPlatformThreadingInterface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.RunLoop(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnLoopCancellation>(cancel, false));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SignalDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Signal(void* @this)
        {
            IAvnPlatformThreadingInterface __target = null;
            try
            {
                {
                    __target = (IAvnPlatformThreadingInterface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Signal();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void UpdateTimerDelegate(void* @this, int ms);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void UpdateTimer(void* @this, int ms)
        {
            IAvnPlatformThreadingInterface __target = null;
            try
            {
                {
                    __target = (IAvnPlatformThreadingInterface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.UpdateTimer(ms);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void RequestBackgroundProcessingDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void RequestBackgroundProcessing(void* @this)
        {
            IAvnPlatformThreadingInterface __target = null;
            try
            {
                {
                    __target = (IAvnPlatformThreadingInterface)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.RequestBackgroundProcessing();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnPlatformThreadingInterfaceVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&GetCurrentThreadIsLoopThread); 
#else
            base.AddMethod((GetCurrentThreadIsLoopThreadDelegate)GetCurrentThreadIsLoopThread); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void>)&SetEvents); 
#else
            base.AddMethod((SetEventsDelegate)SetEvents); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&CreateLoopCancellation); 
#else
            base.AddMethod((CreateLoopCancellationDelegate)CreateLoopCancellation); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void>)&RunLoop); 
#else
            base.AddMethod((RunLoopDelegate)RunLoop); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Signal); 
#else
            base.AddMethod((SignalDelegate)Signal); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, void>)&UpdateTimer); 
#else
            base.AddMethod((UpdateTimerDelegate)UpdateTimer); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&RequestBackgroundProcessing); 
#else
            base.AddMethod((RequestBackgroundProcessingDelegate)RequestBackgroundProcessing); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnPlatformThreadingInterface), new __MicroComIAvnPlatformThreadingInterfaceVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnSystemDialogEventsProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnSystemDialogEvents
    {
        public void OnCompleted(IAvnStringArray array)
        {
            using var __array = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(array);
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 0])(PPV, __array.Pointer);
        }

        public void OnCompletedWithFilter(IAvnStringArray array, int selectedFilterIndex)
        {
            using var __array = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(array);
            ((delegate* unmanaged[Stdcall]<void*, void*, int, void>)(*PPV)[base.VTableSize + 1])(PPV, __array.Pointer, selectedFilterIndex);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnSystemDialogEvents), new Guid("6c621a6e-e4c1-4ae3-9749-83eeeffa09b6"), (p, owns) => new __MicroComIAvnSystemDialogEventsProxy(p, owns));
        }

        protected __MicroComIAvnSystemDialogEventsProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIAvnSystemDialogEventsVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void OnCompletedDelegate(void* @this, void* array);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void OnCompleted(void* @this, void* array)
        {
            IAvnSystemDialogEvents __target = null;
            try
            {
                {
                    __target = (IAvnSystemDialogEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.OnCompleted(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnStringArray>(array, false));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void OnCompletedWithFilterDelegate(void* @this, void* array, int selectedFilterIndex);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void OnCompletedWithFilter(void* @this, void* array, int selectedFilterIndex)
        {
            IAvnSystemDialogEvents __target = null;
            try
            {
                {
                    __target = (IAvnSystemDialogEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.OnCompletedWithFilter(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnStringArray>(array, false), selectedFilterIndex);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnSystemDialogEventsVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void>)&OnCompleted); 
#else
            base.AddMethod((OnCompletedDelegate)OnCompleted); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int, void>)&OnCompletedWithFilter); 
#else
            base.AddMethod((OnCompletedWithFilterDelegate)OnCompletedWithFilter); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnSystemDialogEvents), new __MicroComIAvnSystemDialogEventsVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnStorageProviderProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnStorageProvider
    {
        public void SelectFolderDialog(IAvnTopLevel parentTopLevel, IAvnSystemDialogEvents events, int allowMultiple, string title, string initialPath)
        {
            using var __parentTopLevel = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(parentTopLevel);
            using var __events = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(events);
            var __bytemarshal_title = new byte[System.Text.Encoding.UTF8.GetByteCount(title) + 1];
            System.Text.Encoding.UTF8.GetBytes(title, 0, title.Length, __bytemarshal_title, 0);
            var __bytemarshal_initialPath = new byte[System.Text.Encoding.UTF8.GetByteCount(initialPath) + 1];
            System.Text.Encoding.UTF8.GetBytes(initialPath, 0, initialPath.Length, __bytemarshal_initialPath, 0);
            fixed (byte* __fixedmarshal_initialPath = __bytemarshal_initialPath)
            fixed (byte* __fixedmarshal_title = __bytemarshal_title)
                ((delegate* unmanaged[Stdcall]<void*, void*, void*, int, void*, void*, void>)(*PPV)[base.VTableSize + 0])(PPV, __parentTopLevel.Pointer, __events.Pointer, allowMultiple, __fixedmarshal_title, __fixedmarshal_initialPath);
        }

        public void OpenFileDialog(IAvnTopLevel parentTopLevel, IAvnSystemDialogEvents events, int allowMultiple, string title, string initialDirectory, string initialFile, IAvnFilePickerFileTypes filters)
        {
            using var __parentTopLevel = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(parentTopLevel);
            using var __events = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(events);
            var __bytemarshal_title = new byte[System.Text.Encoding.UTF8.GetByteCount(title) + 1];
            System.Text.Encoding.UTF8.GetBytes(title, 0, title.Length, __bytemarshal_title, 0);
            var __bytemarshal_initialDirectory = new byte[System.Text.Encoding.UTF8.GetByteCount(initialDirectory) + 1];
            System.Text.Encoding.UTF8.GetBytes(initialDirectory, 0, initialDirectory.Length, __bytemarshal_initialDirectory, 0);
            var __bytemarshal_initialFile = new byte[System.Text.Encoding.UTF8.GetByteCount(initialFile) + 1];
            System.Text.Encoding.UTF8.GetBytes(initialFile, 0, initialFile.Length, __bytemarshal_initialFile, 0);
            using var __filters = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(filters);
            fixed (byte* __fixedmarshal_initialFile = __bytemarshal_initialFile)
            fixed (byte* __fixedmarshal_initialDirectory = __bytemarshal_initialDirectory)
            fixed (byte* __fixedmarshal_title = __bytemarshal_title)
                ((delegate* unmanaged[Stdcall]<void*, void*, void*, int, void*, void*, void*, void*, void>)(*PPV)[base.VTableSize + 1])(PPV, __parentTopLevel.Pointer, __events.Pointer, allowMultiple, __fixedmarshal_title, __fixedmarshal_initialDirectory, __fixedmarshal_initialFile, __filters.Pointer);
        }

        public void SaveFileDialog(IAvnTopLevel parentTopLevel, IAvnSystemDialogEvents events, string title, string initialDirectory, string initialFile, IAvnFilePickerFileTypes filters)
        {
            using var __parentTopLevel = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(parentTopLevel);
            using var __events = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(events);
            var __bytemarshal_title = new byte[System.Text.Encoding.UTF8.GetByteCount(title) + 1];
            System.Text.Encoding.UTF8.GetBytes(title, 0, title.Length, __bytemarshal_title, 0);
            var __bytemarshal_initialDirectory = new byte[System.Text.Encoding.UTF8.GetByteCount(initialDirectory) + 1];
            System.Text.Encoding.UTF8.GetBytes(initialDirectory, 0, initialDirectory.Length, __bytemarshal_initialDirectory, 0);
            var __bytemarshal_initialFile = new byte[System.Text.Encoding.UTF8.GetByteCount(initialFile) + 1];
            System.Text.Encoding.UTF8.GetBytes(initialFile, 0, initialFile.Length, __bytemarshal_initialFile, 0);
            using var __filters = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(filters);
            fixed (byte* __fixedmarshal_initialFile = __bytemarshal_initialFile)
            fixed (byte* __fixedmarshal_initialDirectory = __bytemarshal_initialDirectory)
            fixed (byte* __fixedmarshal_title = __bytemarshal_title)
                ((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, void*, void*, void*, void>)(*PPV)[base.VTableSize + 2])(PPV, __parentTopLevel.Pointer, __events.Pointer, __fixedmarshal_title, __fixedmarshal_initialDirectory, __fixedmarshal_initialFile, __filters.Pointer);
        }

        public IAvnString SaveBookmarkToBytes(IAvnString fileUri, void** err)
        {
            int __result;
            using var __fileUri = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(fileUri);
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, __fileUri.Pointer, err, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SaveBookmarkToBytes failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__marshal_ppv, true);
        }

        public IAvnString ReadBookmarkFromBytes(void* ptr, int len)
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, ptr, len, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ReadBookmarkFromBytes failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__marshal_ppv, true);
        }

        public void ReleaseBookmark(IAvnString fileUri)
        {
            using var __fileUri = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(fileUri);
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 5])(PPV, __fileUri.Pointer);
        }

        public int OpenSecurityScope(IAvnString fileUri)
        {
            int __result;
            using var __fileUri = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(fileUri);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, __fileUri.Pointer);
            return __result;
        }

        public void CloseSecurityScope(IAvnString fileUri)
        {
            using var __fileUri = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(fileUri);
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 7])(PPV, __fileUri.Pointer);
        }

        public IAvnString TryResolveFileReferenceUri(IAvnString fileUri)
        {
            int __result;
            using var __fileUri = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(fileUri);
            void* __marshal_ret = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 8])(PPV, __fileUri.Pointer, &__marshal_ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("TryResolveFileReferenceUri failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__marshal_ret, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnStorageProvider), new Guid("4d7a47db-a944-4061-abe7-62cb6aa0ffd5"), (p, owns) => new __MicroComIAvnStorageProviderProxy(p, owns));
        }

        protected __MicroComIAvnStorageProviderProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 9;
    }

    unsafe class __MicroComIAvnStorageProviderVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SelectFolderDialogDelegate(void* @this, void* parentTopLevel, void* events, int allowMultiple, byte* title, byte* initialPath);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void SelectFolderDialog(void* @this, void* parentTopLevel, void* events, int allowMultiple, byte* title, byte* initialPath)
        {
            IAvnStorageProvider __target = null;
            try
            {
                {
                    __target = (IAvnStorageProvider)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SelectFolderDialog(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnTopLevel>(parentTopLevel, false), global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnSystemDialogEvents>(events, false), allowMultiple, (title == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(title))), (initialPath == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(initialPath))));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void OpenFileDialogDelegate(void* @this, void* parentTopLevel, void* events, int allowMultiple, byte* title, byte* initialDirectory, byte* initialFile, void* filters);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void OpenFileDialog(void* @this, void* parentTopLevel, void* events, int allowMultiple, byte* title, byte* initialDirectory, byte* initialFile, void* filters)
        {
            IAvnStorageProvider __target = null;
            try
            {
                {
                    __target = (IAvnStorageProvider)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.OpenFileDialog(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnTopLevel>(parentTopLevel, false), global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnSystemDialogEvents>(events, false), allowMultiple, (title == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(title))), (initialDirectory == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(initialDirectory))), (initialFile == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(initialFile))), global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnFilePickerFileTypes>(filters, false));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SaveFileDialogDelegate(void* @this, void* parentTopLevel, void* events, byte* title, byte* initialDirectory, byte* initialFile, void* filters);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void SaveFileDialog(void* @this, void* parentTopLevel, void* events, byte* title, byte* initialDirectory, byte* initialFile, void* filters)
        {
            IAvnStorageProvider __target = null;
            try
            {
                {
                    __target = (IAvnStorageProvider)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SaveFileDialog(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnTopLevel>(parentTopLevel, false), global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnSystemDialogEvents>(events, false), (title == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(title))), (initialDirectory == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(initialDirectory))), (initialFile == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(initialFile))), global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnFilePickerFileTypes>(filters, false));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SaveBookmarkToBytesDelegate(void* @this, void* fileUri, void** err, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SaveBookmarkToBytes(void* @this, void* fileUri, void** err, void** ppv)
        {
            IAvnStorageProvider __target = null;
            try
            {
                {
                    __target = (IAvnStorageProvider)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.SaveBookmarkToBytes(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(fileUri, false), err);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ReadBookmarkFromBytesDelegate(void* @this, void* ptr, int len, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ReadBookmarkFromBytes(void* @this, void* ptr, int len, void** ppv)
        {
            IAvnStorageProvider __target = null;
            try
            {
                {
                    __target = (IAvnStorageProvider)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ReadBookmarkFromBytes(ptr, len);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ReleaseBookmarkDelegate(void* @this, void* fileUri);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ReleaseBookmark(void* @this, void* fileUri)
        {
            IAvnStorageProvider __target = null;
            try
            {
                {
                    __target = (IAvnStorageProvider)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ReleaseBookmark(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(fileUri, false));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int OpenSecurityScopeDelegate(void* @this, void* fileUri);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int OpenSecurityScope(void* @this, void* fileUri)
        {
            IAvnStorageProvider __target = null;
            try
            {
                {
                    __target = (IAvnStorageProvider)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.OpenSecurityScope(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(fileUri, false));
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void CloseSecurityScopeDelegate(void* @this, void* fileUri);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void CloseSecurityScope(void* @this, void* fileUri)
        {
            IAvnStorageProvider __target = null;
            try
            {
                {
                    __target = (IAvnStorageProvider)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.CloseSecurityScope(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(fileUri, false));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int TryResolveFileReferenceUriDelegate(void* @this, void* fileUri, void** ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int TryResolveFileReferenceUri(void* @this, void* fileUri, void** ret)
        {
            IAvnStorageProvider __target = null;
            try
            {
                {
                    __target = (IAvnStorageProvider)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.TryResolveFileReferenceUri(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(fileUri, false));
                        *ret = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnStorageProviderVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void*, int, byte*, byte*, void>)&SelectFolderDialog); 
#else
            base.AddMethod((SelectFolderDialogDelegate)SelectFolderDialog); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void*, int, byte*, byte*, byte*, void*, void>)&OpenFileDialog); 
#else
            base.AddMethod((OpenFileDialogDelegate)OpenFileDialog); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void*, byte*, byte*, byte*, void*, void>)&SaveFileDialog); 
#else
            base.AddMethod((SaveFileDialogDelegate)SaveFileDialog); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, void**, int>)&SaveBookmarkToBytes); 
#else
            base.AddMethod((SaveBookmarkToBytesDelegate)SaveBookmarkToBytes); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int, void**, int>)&ReadBookmarkFromBytes); 
#else
            base.AddMethod((ReadBookmarkFromBytesDelegate)ReadBookmarkFromBytes); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void>)&ReleaseBookmark); 
#else
            base.AddMethod((ReleaseBookmarkDelegate)ReleaseBookmark); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&OpenSecurityScope); 
#else
            base.AddMethod((OpenSecurityScopeDelegate)OpenSecurityScope); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void>)&CloseSecurityScope); 
#else
            base.AddMethod((CloseSecurityScopeDelegate)CloseSecurityScope); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&TryResolveFileReferenceUri); 
#else
            base.AddMethod((TryResolveFileReferenceUriDelegate)TryResolveFileReferenceUri); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnStorageProvider), new __MicroComIAvnStorageProviderVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnFilePickerFileTypesProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnFilePickerFileTypes
    {
        public int Count
        {
            get
            {
                int __result;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 0])(PPV);
                return __result;
            }
        }

        public int IsDefaultType(int index)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 1])(PPV, index);
            return __result;
        }

        public int IsAnyType(int index)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 2])(PPV, index);
            return __result;
        }

        public IAvnString GetName(int index)
        {
            void* __result;
            __result = (void*)((delegate* unmanaged[Stdcall]<void*, int, void*>)(*PPV)[base.VTableSize + 3])(PPV, index);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__result, true);
        }

        public IAvnStringArray GetPatterns(int index)
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, index, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetPatterns failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnStringArray>(__marshal_ppv, true);
        }

        public IAvnStringArray GetExtensions(int index)
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, void*, int>)(*PPV)[base.VTableSize + 5])(PPV, index, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetExtensions failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnStringArray>(__marshal_ppv, true);
        }

        public IAvnStringArray GetMimeTypes(int index)
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, index, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetMimeTypes failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnStringArray>(__marshal_ppv, true);
        }

        public IAvnStringArray GetAppleUniformTypeIdentifiers(int index)
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, void*, int>)(*PPV)[base.VTableSize + 7])(PPV, index, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetAppleUniformTypeIdentifiers failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnStringArray>(__marshal_ppv, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnFilePickerFileTypes), new Guid("4d7ab7db-a111-406f-abeb-11cb6aa033d5"), (p, owns) => new __MicroComIAvnFilePickerFileTypesProxy(p, owns));
        }

        protected __MicroComIAvnFilePickerFileTypesProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 8;
    }

    unsafe class __MicroComIAvnFilePickerFileTypesVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetCountDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetCount(void* @this)
        {
            IAvnFilePickerFileTypes __target = null;
            try
            {
                {
                    __target = (IAvnFilePickerFileTypes)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Count;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsDefaultTypeDelegate(void* @this, int index);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsDefaultType(void* @this, int index)
        {
            IAvnFilePickerFileTypes __target = null;
            try
            {
                {
                    __target = (IAvnFilePickerFileTypes)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsDefaultType(index);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsAnyTypeDelegate(void* @this, int index);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsAnyType(void* @this, int index)
        {
            IAvnFilePickerFileTypes __target = null;
            try
            {
                {
                    __target = (IAvnFilePickerFileTypes)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsAnyType(index);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetNameDelegate(void* @this, int index);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetName(void* @this, int index)
        {
            IAvnFilePickerFileTypes __target = null;
            try
            {
                {
                    __target = (IAvnFilePickerFileTypes)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetName(index);
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetPatternsDelegate(void* @this, int index, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetPatterns(void* @this, int index, void** ppv)
        {
            IAvnFilePickerFileTypes __target = null;
            try
            {
                {
                    __target = (IAvnFilePickerFileTypes)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetPatterns(index);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetExtensionsDelegate(void* @this, int index, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetExtensions(void* @this, int index, void** ppv)
        {
            IAvnFilePickerFileTypes __target = null;
            try
            {
                {
                    __target = (IAvnFilePickerFileTypes)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetExtensions(index);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetMimeTypesDelegate(void* @this, int index, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetMimeTypes(void* @this, int index, void** ppv)
        {
            IAvnFilePickerFileTypes __target = null;
            try
            {
                {
                    __target = (IAvnFilePickerFileTypes)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetMimeTypes(index);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetAppleUniformTypeIdentifiersDelegate(void* @this, int index, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetAppleUniformTypeIdentifiers(void* @this, int index, void** ppv)
        {
            IAvnFilePickerFileTypes __target = null;
            try
            {
                {
                    __target = (IAvnFilePickerFileTypes)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetAppleUniformTypeIdentifiers(index);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnFilePickerFileTypesVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&GetCount); 
#else
            base.AddMethod((GetCountDelegate)GetCount); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&IsDefaultType); 
#else
            base.AddMethod((IsDefaultTypeDelegate)IsDefaultType); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&IsAnyType); 
#else
            base.AddMethod((IsAnyTypeDelegate)IsAnyType); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, void*>)&GetName); 
#else
            base.AddMethod((GetNameDelegate)GetName); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, void**, int>)&GetPatterns); 
#else
            base.AddMethod((GetPatternsDelegate)GetPatterns); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, void**, int>)&GetExtensions); 
#else
            base.AddMethod((GetExtensionsDelegate)GetExtensions); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, void**, int>)&GetMimeTypes); 
#else
            base.AddMethod((GetMimeTypesDelegate)GetMimeTypes); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, void**, int>)&GetAppleUniformTypeIdentifiers); 
#else
            base.AddMethod((GetAppleUniformTypeIdentifiersDelegate)GetAppleUniformTypeIdentifiers); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnFilePickerFileTypes), new __MicroComIAvnFilePickerFileTypesVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnScreenEventsProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnScreenEvents
    {
        public void OnChanged()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 0])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("OnChanged failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnScreenEvents), new Guid("424b1bd4-a111-4987-bfd0-9d642154b1b3"), (p, owns) => new __MicroComIAvnScreenEventsProxy(p, owns));
        }

        protected __MicroComIAvnScreenEventsProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIAvnScreenEventsVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int OnChangedDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int OnChanged(void* @this)
        {
            IAvnScreenEvents __target = null;
            try
            {
                {
                    __target = (IAvnScreenEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.OnChanged();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnScreenEventsVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&OnChanged); 
#else
            base.AddMethod((OnChangedDelegate)OnChanged); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnScreenEvents), new __MicroComIAvnScreenEventsVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnScreensProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnScreens
    {
        public int GetScreenIds(uint* ptrFirstResult)
        {
            int __result;
            int ret = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, ptrFirstResult, &ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetScreenIds failed", __result);
            return ret;
        }

        public AvnScreen GetScreen(uint screenId, void** localizedName)
        {
            int __result;
            AvnScreen ret = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, screenId, localizedName, &ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetScreen failed", __result);
            return ret;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnScreens), new Guid("9a52bc7a-d8c7-4230-8d34-704a0b70a933"), (p, owns) => new __MicroComIAvnScreensProxy(p, owns));
        }

        protected __MicroComIAvnScreensProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIAvnScreensVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetScreenIdsDelegate(void* @this, uint* ptrFirstResult, int* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetScreenIds(void* @this, uint* ptrFirstResult, int* ret)
        {
            IAvnScreens __target = null;
            try
            {
                {
                    __target = (IAvnScreens)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetScreenIds(ptrFirstResult);
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetScreenDelegate(void* @this, uint screenId, void** localizedName, AvnScreen* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetScreen(void* @this, uint screenId, void** localizedName, AvnScreen* ret)
        {
            IAvnScreens __target = null;
            try
            {
                {
                    __target = (IAvnScreens)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetScreen(screenId, localizedName);
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnScreensVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint*, int*, int>)&GetScreenIds); 
#else
            base.AddMethod((GetScreenIdsDelegate)GetScreenIds); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, void**, AvnScreen*, int>)&GetScreen); 
#else
            base.AddMethod((GetScreenDelegate)GetScreen); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnScreens), new __MicroComIAvnScreensVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnClipboardProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnClipboard
    {
        public IAvnStringArray GetFormats(long changeCount)
        {
            int __result;
            void* __marshal_ret = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, long, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, changeCount, &__marshal_ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetFormats failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnStringArray>(__marshal_ret, true);
        }

        public int GetItemCount(long changeCount)
        {
            int __result;
            int ret = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, long, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, changeCount, &ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetItemCount failed", __result);
            return ret;
        }

        public IAvnStringArray GetItemFormats(int index, long changeCount)
        {
            int __result;
            void* __marshal_ret = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, long, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, index, changeCount, &__marshal_ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetItemFormats failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnStringArray>(__marshal_ret, true);
        }

        public IAvnString GetItemValueAsString(int index, long changeCount, string format)
        {
            int __result;
            var __bytemarshal_format = new byte[System.Text.Encoding.UTF8.GetByteCount(format) + 1];
            System.Text.Encoding.UTF8.GetBytes(format, 0, format.Length, __bytemarshal_format, 0);
            void* __marshal_ret = null;
            fixed (byte* __fixedmarshal_format = __bytemarshal_format)
                __result = (int)((delegate* unmanaged[Stdcall]<void*, int, long, void*, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, index, changeCount, __fixedmarshal_format, &__marshal_ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetItemValueAsString failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__marshal_ret, true);
        }

        public IAvnString GetItemValueAsBytes(int index, long changeCount, string format)
        {
            int __result;
            var __bytemarshal_format = new byte[System.Text.Encoding.UTF8.GetByteCount(format) + 1];
            System.Text.Encoding.UTF8.GetBytes(format, 0, format.Length, __bytemarshal_format, 0);
            void* __marshal_ret = null;
            fixed (byte* __fixedmarshal_format = __bytemarshal_format)
                __result = (int)((delegate* unmanaged[Stdcall]<void*, int, long, void*, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, index, changeCount, __fixedmarshal_format, &__marshal_ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetItemValueAsBytes failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__marshal_ret, true);
        }

        public long Clear()
        {
            int __result;
            long ret = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 5])(PPV, &ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Clear failed", __result);
            return ret;
        }

        public long ChangeCount
        {
            get
            {
                int __result;
                long ret = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, &ret);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetChangeCount failed", __result);
                return ret;
            }
        }

        public void SetData(IAvnClipboardDataSource dataSource)
        {
            int __result;
            using var __dataSource = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(dataSource);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 7])(PPV, __dataSource.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetData failed", __result);
        }

        public int IsTextFormat(string format)
        {
            int __result;
            var __bytemarshal_format = new byte[System.Text.Encoding.UTF8.GetByteCount(format) + 1];
            System.Text.Encoding.UTF8.GetBytes(format, 0, format.Length, __bytemarshal_format, 0);
            fixed (byte* __fixedmarshal_format = __bytemarshal_format)
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 8])(PPV, __fixedmarshal_format);
            return __result;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnClipboard), new Guid("792b1bd4-76cc-46ea-bfd0-9d642154b1b3"), (p, owns) => new __MicroComIAvnClipboardProxy(p, owns));
        }

        protected __MicroComIAvnClipboardProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 9;
    }

    unsafe class __MicroComIAvnClipboardVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetFormatsDelegate(void* @this, long changeCount, void** ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetFormats(void* @this, long changeCount, void** ret)
        {
            IAvnClipboard __target = null;
            try
            {
                {
                    __target = (IAvnClipboard)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetFormats(changeCount);
                        *ret = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetItemCountDelegate(void* @this, long changeCount, int* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetItemCount(void* @this, long changeCount, int* ret)
        {
            IAvnClipboard __target = null;
            try
            {
                {
                    __target = (IAvnClipboard)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetItemCount(changeCount);
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetItemFormatsDelegate(void* @this, int index, long changeCount, void** ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetItemFormats(void* @this, int index, long changeCount, void** ret)
        {
            IAvnClipboard __target = null;
            try
            {
                {
                    __target = (IAvnClipboard)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetItemFormats(index, changeCount);
                        *ret = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetItemValueAsStringDelegate(void* @this, int index, long changeCount, byte* format, void** ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetItemValueAsString(void* @this, int index, long changeCount, byte* format, void** ret)
        {
            IAvnClipboard __target = null;
            try
            {
                {
                    __target = (IAvnClipboard)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetItemValueAsString(index, changeCount, (format == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(format))));
                        *ret = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetItemValueAsBytesDelegate(void* @this, int index, long changeCount, byte* format, void** ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetItemValueAsBytes(void* @this, int index, long changeCount, byte* format, void** ret)
        {
            IAvnClipboard __target = null;
            try
            {
                {
                    __target = (IAvnClipboard)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetItemValueAsBytes(index, changeCount, (format == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(format))));
                        *ret = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ClearDelegate(void* @this, long* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Clear(void* @this, long* ret)
        {
            IAvnClipboard __target = null;
            try
            {
                {
                    __target = (IAvnClipboard)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Clear();
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetChangeCountDelegate(void* @this, long* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetChangeCount(void* @this, long* ret)
        {
            IAvnClipboard __target = null;
            try
            {
                {
                    __target = (IAvnClipboard)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ChangeCount;
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetDataDelegate(void* @this, void* dataSource);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetData(void* @this, void* dataSource)
        {
            IAvnClipboard __target = null;
            try
            {
                {
                    __target = (IAvnClipboard)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetData(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnClipboardDataSource>(dataSource, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsTextFormatDelegate(void* @this, byte* format);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsTextFormat(void* @this, byte* format)
        {
            IAvnClipboard __target = null;
            try
            {
                {
                    __target = (IAvnClipboard)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsTextFormat((format == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(format))));
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComIAvnClipboardVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, long, void**, int>)&GetFormats); 
#else
            base.AddMethod((GetFormatsDelegate)GetFormats); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, long, int*, int>)&GetItemCount); 
#else
            base.AddMethod((GetItemCountDelegate)GetItemCount); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, long, void**, int>)&GetItemFormats); 
#else
            base.AddMethod((GetItemFormatsDelegate)GetItemFormats); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, long, byte*, void**, int>)&GetItemValueAsString); 
#else
            base.AddMethod((GetItemValueAsStringDelegate)GetItemValueAsString); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, long, byte*, void**, int>)&GetItemValueAsBytes); 
#else
            base.AddMethod((GetItemValueAsBytesDelegate)GetItemValueAsBytes); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, long*, int>)&Clear); 
#else
            base.AddMethod((ClearDelegate)Clear); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, long*, int>)&GetChangeCount); 
#else
            base.AddMethod((GetChangeCountDelegate)GetChangeCount); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetData); 
#else
            base.AddMethod((SetDataDelegate)SetData); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, byte*, int>)&IsTextFormat); 
#else
            base.AddMethod((IsTextFormatDelegate)IsTextFormat); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnClipboard), new __MicroComIAvnClipboardVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnClipboardDataSourceProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnClipboardDataSource
    {
        public int ItemCount
        {
            get
            {
                int __result;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 0])(PPV);
                return __result;
            }
        }

        public IAvnClipboardDataItem GetItem(int index)
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, index, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetItem failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnClipboardDataItem>(__marshal_ppv, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnClipboardDataSource), new Guid("10b39f02-efcb-428b-bee5-a0b012c1fb7d"), (p, owns) => new __MicroComIAvnClipboardDataSourceProxy(p, owns));
        }

        protected __MicroComIAvnClipboardDataSourceProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIAvnClipboardDataSourceVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetItemCountDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetItemCount(void* @this)
        {
            IAvnClipboardDataSource __target = null;
            try
            {
                {
                    __target = (IAvnClipboardDataSource)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ItemCount;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetItemDelegate(void* @this, int index, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetItem(void* @this, int index, void** ppv)
        {
            IAvnClipboardDataSource __target = null;
            try
            {
                {
                    __target = (IAvnClipboardDataSource)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetItem(index);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnClipboardDataSourceVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&GetItemCount); 
#else
            base.AddMethod((GetItemCountDelegate)GetItemCount); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, void**, int>)&GetItem); 
#else
            base.AddMethod((GetItemDelegate)GetItem); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnClipboardDataSource), new __MicroComIAvnClipboardDataSourceVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnClipboardDataItemProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnClipboardDataItem
    {
        public IAvnStringArray ProvideFormats()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ProvideFormats failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnStringArray>(__marshal_ppv, true);
        }

        public IAvnClipboardDataValue GetValue(string format)
        {
            int __result;
            var __bytemarshal_format = new byte[System.Text.Encoding.UTF8.GetByteCount(format) + 1];
            System.Text.Encoding.UTF8.GetBytes(format, 0, format.Length, __bytemarshal_format, 0);
            void* __marshal_ppv = null;
            fixed (byte* __fixedmarshal_format = __bytemarshal_format)
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, __fixedmarshal_format, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetValue failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnClipboardDataValue>(__marshal_ppv, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnClipboardDataItem), new Guid("e40f36d9-69f4-45fd-9ca2-6e64e80feb6d"), (p, owns) => new __MicroComIAvnClipboardDataItemProxy(p, owns));
        }

        protected __MicroComIAvnClipboardDataItemProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIAvnClipboardDataItemVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ProvideFormatsDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ProvideFormats(void* @this, void** ppv)
        {
            IAvnClipboardDataItem __target = null;
            try
            {
                {
                    __target = (IAvnClipboardDataItem)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ProvideFormats();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetValueDelegate(void* @this, byte* format, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetValue(void* @this, byte* format, void** ppv)
        {
            IAvnClipboardDataItem __target = null;
            try
            {
                {
                    __target = (IAvnClipboardDataItem)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetValue((format == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(format))));
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnClipboardDataItemVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&ProvideFormats); 
#else
            base.AddMethod((ProvideFormatsDelegate)ProvideFormats); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, byte*, void**, int>)&GetValue); 
#else
            base.AddMethod((GetValueDelegate)GetValue); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnClipboardDataItem), new __MicroComIAvnClipboardDataItemVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnClipboardDataValueProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnClipboardDataValue
    {
        public int IsString()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 0])(PPV);
            return __result;
        }

        public IAvnString AsString()
        {
            void* __result;
            __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 1])(PPV);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__result, true);
        }

        public IntPtr ByteLength
        {
            get
            {
                IntPtr __result;
                __result = (IntPtr)((delegate* unmanaged[Stdcall]<void*, IntPtr>)(*PPV)[base.VTableSize + 2])(PPV);
                return __result;
            }
        }

        public void CopyBytesTo(void* buffer)
        {
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 3])(PPV, buffer);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnClipboardDataValue), new Guid("e97f24f6-1c84-4d95-8ffe-5b2c72e016ed"), (p, owns) => new __MicroComIAvnClipboardDataValueProxy(p, owns));
        }

        protected __MicroComIAvnClipboardDataValueProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 4;
    }

    unsafe class __MicroComIAvnClipboardDataValueVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsStringDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsString(void* @this)
        {
            IAvnClipboardDataValue __target = null;
            try
            {
                {
                    __target = (IAvnClipboardDataValue)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsString();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* AsStringDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* AsString(void* @this)
        {
            IAvnClipboardDataValue __target = null;
            try
            {
                {
                    __target = (IAvnClipboardDataValue)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.AsString();
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate IntPtr GetByteLengthDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static IntPtr GetByteLength(void* @this)
        {
            IAvnClipboardDataValue __target = null;
            try
            {
                {
                    __target = (IAvnClipboardDataValue)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ByteLength;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void CopyBytesToDelegate(void* @this, void* buffer);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void CopyBytesTo(void* @this, void* buffer)
        {
            IAvnClipboardDataValue __target = null;
            try
            {
                {
                    __target = (IAvnClipboardDataValue)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.CopyBytesTo(buffer);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnClipboardDataValueVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsString); 
#else
            base.AddMethod((IsStringDelegate)IsString); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&AsString); 
#else
            base.AddMethod((AsStringDelegate)AsString); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr>)&GetByteLength); 
#else
            base.AddMethod((GetByteLengthDelegate)GetByteLength); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void>)&CopyBytesTo); 
#else
            base.AddMethod((CopyBytesToDelegate)CopyBytesTo); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnClipboardDataValue), new __MicroComIAvnClipboardDataValueVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnCursorProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnCursor
    {
        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnCursor), new Guid("3f998545-f027-4d4d-bd2a-1a80926d984e"), (p, owns) => new __MicroComIAvnCursorProxy(p, owns));
        }

        protected __MicroComIAvnCursorProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 0;
    }

    unsafe class __MicroComIAvnCursorVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        protected __MicroComIAvnCursorVTable()
        {
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnCursor), new __MicroComIAvnCursorVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnCursorFactoryProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnCursorFactory
    {
        public IAvnCursor GetCursor(AvnStandardCursorType cursorType)
        {
            int __result;
            void* __marshal_retOut = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnStandardCursorType, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, cursorType, &__marshal_retOut);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("GetCursor failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnCursor>(__marshal_retOut, true);
        }

        public IAvnCursor CreateCustomCursor(void* bitmapData, System.IntPtr length, AvnPixelSize hotPixel)
        {
            int __result;
            void* __marshal_retOut = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, System.IntPtr, AvnPixelSize, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, bitmapData, length, hotPixel, &__marshal_retOut);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateCustomCursor failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnCursor>(__marshal_retOut, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnCursorFactory), new Guid("51ecfb12-c427-4757-a2c9-1596bfce53ef"), (p, owns) => new __MicroComIAvnCursorFactoryProxy(p, owns));
        }

        protected __MicroComIAvnCursorFactoryProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIAvnCursorFactoryVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetCursorDelegate(void* @this, AvnStandardCursorType cursorType, void** retOut);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetCursor(void* @this, AvnStandardCursorType cursorType, void** retOut)
        {
            IAvnCursorFactory __target = null;
            try
            {
                {
                    __target = (IAvnCursorFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetCursor(cursorType);
                        *retOut = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateCustomCursorDelegate(void* @this, void* bitmapData, System.IntPtr length, AvnPixelSize hotPixel, void** retOut);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateCustomCursor(void* @this, void* bitmapData, System.IntPtr length, AvnPixelSize hotPixel, void** retOut)
        {
            IAvnCursorFactory __target = null;
            try
            {
                {
                    __target = (IAvnCursorFactory)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateCustomCursor(bitmapData, length, hotPixel);
                        *retOut = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnCursorFactoryVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnStandardCursorType, void**, int>)&GetCursor); 
#else
            base.AddMethod((GetCursorDelegate)GetCursor); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, System.IntPtr, AvnPixelSize, void**, int>)&CreateCustomCursor); 
#else
            base.AddMethod((CreateCustomCursorDelegate)CreateCustomCursor); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnCursorFactory), new __MicroComIAvnCursorFactoryVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnSoftwareRenderTargetProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnSoftwareRenderTarget
    {
        public void SetFrame(AvnFramebuffer* fb)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, fb);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetFrame failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnSoftwareRenderTarget), new Guid("931062d2-5bc8-4062-8588-83dd8deb99c2"), (p, owns) => new __MicroComIAvnSoftwareRenderTargetProxy(p, owns));
        }

        protected __MicroComIAvnSoftwareRenderTargetProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIAvnSoftwareRenderTargetVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetFrameDelegate(void* @this, AvnFramebuffer* fb);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetFrame(void* @this, AvnFramebuffer* fb)
        {
            IAvnSoftwareRenderTarget __target = null;
            try
            {
                {
                    __target = (IAvnSoftwareRenderTarget)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetFrame(fb);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnSoftwareRenderTargetVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnFramebuffer*, int>)&SetFrame); 
#else
            base.AddMethod((SetFrameDelegate)SetFrame); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnSoftwareRenderTarget), new __MicroComIAvnSoftwareRenderTargetVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnGlDisplayProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnGlDisplay
    {
        public IAvnGlContext CreateContext(IAvnGlContext share)
        {
            int __result;
            using var __share = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(share);
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, __share.Pointer, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateContext failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnGlContext>(__marshal_ppv, true);
        }

        public void LegacyClearCurrentContext()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 1])(PPV);
        }

        public IAvnGlContext WrapContext(IntPtr native)
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, native, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("WrapContext failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnGlContext>(__marshal_ppv, true);
        }

        public IntPtr GetProcAddress(string proc)
        {
            IntPtr __result;
            var __bytemarshal_proc = new byte[System.Text.Encoding.UTF8.GetByteCount(proc) + 1];
            System.Text.Encoding.UTF8.GetBytes(proc, 0, proc.Length, __bytemarshal_proc, 0);
            fixed (byte* __fixedmarshal_proc = __bytemarshal_proc)
                __result = (IntPtr)((delegate* unmanaged[Stdcall]<void*, void*, IntPtr>)(*PPV)[base.VTableSize + 3])(PPV, __fixedmarshal_proc);
            return __result;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnGlDisplay), new Guid("60452465-8616-40af-bc00-042e69828ce7"), (p, owns) => new __MicroComIAvnGlDisplayProxy(p, owns));
        }

        protected __MicroComIAvnGlDisplayProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 4;
    }

    unsafe class __MicroComIAvnGlDisplayVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateContextDelegate(void* @this, void* share, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateContext(void* @this, void* share, void** ppv)
        {
            IAvnGlDisplay __target = null;
            try
            {
                {
                    __target = (IAvnGlDisplay)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateContext(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnGlContext>(share, false));
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void LegacyClearCurrentContextDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void LegacyClearCurrentContext(void* @this)
        {
            IAvnGlDisplay __target = null;
            try
            {
                {
                    __target = (IAvnGlDisplay)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.LegacyClearCurrentContext();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int WrapContextDelegate(void* @this, IntPtr native, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int WrapContext(void* @this, IntPtr native, void** ppv)
        {
            IAvnGlDisplay __target = null;
            try
            {
                {
                    __target = (IAvnGlDisplay)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.WrapContext(native);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate IntPtr GetProcAddressDelegate(void* @this, byte* proc);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static IntPtr GetProcAddress(void* @this, byte* proc)
        {
            IAvnGlDisplay __target = null;
            try
            {
                {
                    __target = (IAvnGlDisplay)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetProcAddress((proc == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(proc))));
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComIAvnGlDisplayVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)&CreateContext); 
#else
            base.AddMethod((CreateContextDelegate)CreateContext); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&LegacyClearCurrentContext); 
#else
            base.AddMethod((LegacyClearCurrentContextDelegate)LegacyClearCurrentContext); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&WrapContext); 
#else
            base.AddMethod((WrapContextDelegate)WrapContext); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, byte*, IntPtr>)&GetProcAddress); 
#else
            base.AddMethod((GetProcAddressDelegate)GetProcAddress); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnGlDisplay), new __MicroComIAvnGlDisplayVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnGlContextProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnGlContext
    {
        public IUnknown MakeCurrent()
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("MakeCurrent failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IUnknown>(__marshal_ppv, true);
        }

        public void LegacyMakeCurrent()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 1])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("LegacyMakeCurrent failed", __result);
        }

        public int SampleCount
        {
            get
            {
                int __result;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 2])(PPV);
                return __result;
            }
        }

        public int StencilSize
        {
            get
            {
                int __result;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 3])(PPV);
                return __result;
            }
        }

        public IntPtr NativeHandle
        {
            get
            {
                IntPtr __result;
                __result = (IntPtr)((delegate* unmanaged[Stdcall]<void*, IntPtr>)(*PPV)[base.VTableSize + 4])(PPV);
                return __result;
            }
        }

        public int texImageIOSurface2D(int target, int internal_format, int width, int height, int format, int type, IntPtr ioSurface, int plane)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int, int, int, int, int, IntPtr, int, int>)(*PPV)[base.VTableSize + 5])(PPV, target, internal_format, width, height, format, type, ioSurface, plane);
            return __result;
        }

        public int GetIOKitRegistryId(ulong* value)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 6])(PPV, value);
            return __result;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnGlContext), new Guid("78c5711e-2a98-40d2-bac4-0cc9a49dc4f3"), (p, owns) => new __MicroComIAvnGlContextProxy(p, owns));
        }

        protected __MicroComIAvnGlContextProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 7;
    }

    unsafe class __MicroComIAvnGlContextVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int MakeCurrentDelegate(void* @this, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int MakeCurrent(void* @this, void** ppv)
        {
            IAvnGlContext __target = null;
            try
            {
                {
                    __target = (IAvnGlContext)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.MakeCurrent();
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int LegacyMakeCurrentDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int LegacyMakeCurrent(void* @this)
        {
            IAvnGlContext __target = null;
            try
            {
                {
                    __target = (IAvnGlContext)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.LegacyMakeCurrent();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetSampleCountDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetSampleCount(void* @this)
        {
            IAvnGlContext __target = null;
            try
            {
                {
                    __target = (IAvnGlContext)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.SampleCount;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetStencilSizeDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetStencilSize(void* @this)
        {
            IAvnGlContext __target = null;
            try
            {
                {
                    __target = (IAvnGlContext)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.StencilSize;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate IntPtr GetNativeHandleDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static IntPtr GetNativeHandle(void* @this)
        {
            IAvnGlContext __target = null;
            try
            {
                {
                    __target = (IAvnGlContext)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.NativeHandle;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int texImageIOSurface2DDelegate(void* @this, int target, int internal_format, int width, int height, int format, int type, IntPtr ioSurface, int plane);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int texImageIOSurface2D(void* @this, int target, int internal_format, int width, int height, int format, int type, IntPtr ioSurface, int plane)
        {
            IAvnGlContext __target = null;
            try
            {
                {
                    __target = (IAvnGlContext)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.texImageIOSurface2D(target, internal_format, width, height, format, type, ioSurface, plane);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetIOKitRegistryIdDelegate(void* @this, ulong* value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetIOKitRegistryId(void* @this, ulong* value)
        {
            IAvnGlContext __target = null;
            try
            {
                {
                    __target = (IAvnGlContext)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetIOKitRegistryId(value);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComIAvnGlContextVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&MakeCurrent); 
#else
            base.AddMethod((MakeCurrentDelegate)MakeCurrent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&LegacyMakeCurrent); 
#else
            base.AddMethod((LegacyMakeCurrentDelegate)LegacyMakeCurrent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&GetSampleCount); 
#else
            base.AddMethod((GetSampleCountDelegate)GetSampleCount); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&GetStencilSize); 
#else
            base.AddMethod((GetStencilSizeDelegate)GetStencilSize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr>)&GetNativeHandle); 
#else
            base.AddMethod((GetNativeHandleDelegate)GetNativeHandle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int, int, int, int, int, IntPtr, int, int>)&texImageIOSurface2D); 
#else
            base.AddMethod((texImageIOSurface2DDelegate)texImageIOSurface2D); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, ulong*, int>)&GetIOKitRegistryId); 
#else
            base.AddMethod((GetIOKitRegistryIdDelegate)GetIOKitRegistryId); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnGlContext), new __MicroComIAvnGlContextVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnGlSurfaceRenderTargetProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnGlSurfaceRenderTarget
    {
        public IAvnGlSurfaceRenderingSession BeginDrawing()
        {
            int __result;
            void* __marshal_ret = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &__marshal_ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("BeginDrawing failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnGlSurfaceRenderingSession>(__marshal_ret, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnGlSurfaceRenderTarget), new Guid("931062d2-5bc8-4062-8588-83dd8deb99c2"), (p, owns) => new __MicroComIAvnGlSurfaceRenderTargetProxy(p, owns));
        }

        protected __MicroComIAvnGlSurfaceRenderTargetProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIAvnGlSurfaceRenderTargetVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int BeginDrawingDelegate(void* @this, void** ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int BeginDrawing(void* @this, void** ret)
        {
            IAvnGlSurfaceRenderTarget __target = null;
            try
            {
                {
                    __target = (IAvnGlSurfaceRenderTarget)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.BeginDrawing();
                        *ret = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnGlSurfaceRenderTargetVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&BeginDrawing); 
#else
            base.AddMethod((BeginDrawingDelegate)BeginDrawing); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnGlSurfaceRenderTarget), new __MicroComIAvnGlSurfaceRenderTargetVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnGlSurfaceRenderingSessionProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnGlSurfaceRenderingSession
    {
        public AvnPixelSize PixelSize
        {
            get
            {
                int __result;
                AvnPixelSize ret = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &ret);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetPixelSize failed", __result);
                return ret;
            }
        }

        public double Scaling
        {
            get
            {
                int __result;
                double ret = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, &ret);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetScaling failed", __result);
                return ret;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnGlSurfaceRenderingSession), new Guid("e625b406-f04c-484e-946a-4abd2c6015ad"), (p, owns) => new __MicroComIAvnGlSurfaceRenderingSessionProxy(p, owns));
        }

        protected __MicroComIAvnGlSurfaceRenderingSessionProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIAvnGlSurfaceRenderingSessionVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetPixelSizeDelegate(void* @this, AvnPixelSize* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetPixelSize(void* @this, AvnPixelSize* ret)
        {
            IAvnGlSurfaceRenderingSession __target = null;
            try
            {
                {
                    __target = (IAvnGlSurfaceRenderingSession)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.PixelSize;
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetScalingDelegate(void* @this, double* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetScaling(void* @this, double* ret)
        {
            IAvnGlSurfaceRenderingSession __target = null;
            try
            {
                {
                    __target = (IAvnGlSurfaceRenderingSession)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Scaling;
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnGlSurfaceRenderingSessionVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnPixelSize*, int>)&GetPixelSize); 
#else
            base.AddMethod((GetPixelSizeDelegate)GetPixelSize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double*, int>)&GetScaling); 
#else
            base.AddMethod((GetScalingDelegate)GetScaling); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnGlSurfaceRenderingSession), new __MicroComIAvnGlSurfaceRenderingSessionVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnMetalDisplayProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnMetalDisplay
    {
        public IAvnMetalDevice CreateDevice()
        {
            int __result;
            void* __marshal_ret = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &__marshal_ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateDevice failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMetalDevice>(__marshal_ret, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnMetalDisplay), new Guid("da291767-4db3-4598-893d-09ecaa23893f"), (p, owns) => new __MicroComIAvnMetalDisplayProxy(p, owns));
        }

        protected __MicroComIAvnMetalDisplayProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIAvnMetalDisplayVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateDeviceDelegate(void* @this, void** ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateDevice(void* @this, void** ret)
        {
            IAvnMetalDisplay __target = null;
            try
            {
                {
                    __target = (IAvnMetalDisplay)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateDevice();
                        *ret = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnMetalDisplayVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&CreateDevice); 
#else
            base.AddMethod((CreateDeviceDelegate)CreateDevice); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnMetalDisplay), new __MicroComIAvnMetalDisplayVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnMetalDeviceProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnMetalDevice
    {
        public IntPtr Device
        {
            get
            {
                IntPtr __result;
                __result = (IntPtr)((delegate* unmanaged[Stdcall]<void*, IntPtr>)(*PPV)[base.VTableSize + 0])(PPV);
                return __result;
            }
        }

        public IntPtr Queue
        {
            get
            {
                IntPtr __result;
                __result = (IntPtr)((delegate* unmanaged[Stdcall]<void*, IntPtr>)(*PPV)[base.VTableSize + 1])(PPV);
                return __result;
            }
        }

        public int GetIOKitRegistryId(ulong* value)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, value);
            return __result;
        }

        public IAvnMetalTexture ImportIOSurface(IntPtr handle, AvnPixelFormat pixelFormat)
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, AvnPixelFormat, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, handle, pixelFormat, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ImportIOSurface failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMetalTexture>(__marshal_ppv, true);
        }

        public IAvnMTLSharedEvent ImportSharedEvent(IntPtr mtlSharedEventInstance)
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, mtlSharedEventInstance, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ImportSharedEvent failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMTLSharedEvent>(__marshal_ppv, true);
        }

        public void SubmitWait(IAvnMTLSharedEvent ev, ulong value)
        {
            int __result;
            using var __ev = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(ev);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, ulong, int>)(*PPV)[base.VTableSize + 5])(PPV, __ev.Pointer, value);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SubmitWait failed", __result);
        }

        public void SubmitSignal(IAvnMTLSharedEvent ev, ulong value)
        {
            int __result;
            using var __ev = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(ev);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, ulong, int>)(*PPV)[base.VTableSize + 6])(PPV, __ev.Pointer, value);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SubmitSignal failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnMetalDevice), new Guid("969fa914-b74a-4c9f-8725-5160dc63579e"), (p, owns) => new __MicroComIAvnMetalDeviceProxy(p, owns));
        }

        protected __MicroComIAvnMetalDeviceProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 7;
    }

    unsafe class __MicroComIAvnMetalDeviceVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate IntPtr GetDeviceDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static IntPtr GetDevice(void* @this)
        {
            IAvnMetalDevice __target = null;
            try
            {
                {
                    __target = (IAvnMetalDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Device;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate IntPtr GetQueueDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static IntPtr GetQueue(void* @this)
        {
            IAvnMetalDevice __target = null;
            try
            {
                {
                    __target = (IAvnMetalDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Queue;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetIOKitRegistryIdDelegate(void* @this, ulong* value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetIOKitRegistryId(void* @this, ulong* value)
        {
            IAvnMetalDevice __target = null;
            try
            {
                {
                    __target = (IAvnMetalDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetIOKitRegistryId(value);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ImportIOSurfaceDelegate(void* @this, IntPtr handle, AvnPixelFormat pixelFormat, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ImportIOSurface(void* @this, IntPtr handle, AvnPixelFormat pixelFormat, void** ppv)
        {
            IAvnMetalDevice __target = null;
            try
            {
                {
                    __target = (IAvnMetalDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ImportIOSurface(handle, pixelFormat);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ImportSharedEventDelegate(void* @this, IntPtr mtlSharedEventInstance, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ImportSharedEvent(void* @this, IntPtr mtlSharedEventInstance, void** ppv)
        {
            IAvnMetalDevice __target = null;
            try
            {
                {
                    __target = (IAvnMetalDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ImportSharedEvent(mtlSharedEventInstance);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SubmitWaitDelegate(void* @this, void* ev, ulong value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SubmitWait(void* @this, void* ev, ulong value)
        {
            IAvnMetalDevice __target = null;
            try
            {
                {
                    __target = (IAvnMetalDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SubmitWait(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMTLSharedEvent>(ev, false), value);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SubmitSignalDelegate(void* @this, void* ev, ulong value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SubmitSignal(void* @this, void* ev, ulong value)
        {
            IAvnMetalDevice __target = null;
            try
            {
                {
                    __target = (IAvnMetalDevice)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SubmitSignal(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMTLSharedEvent>(ev, false), value);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnMetalDeviceVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr>)&GetDevice); 
#else
            base.AddMethod((GetDeviceDelegate)GetDevice); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr>)&GetQueue); 
#else
            base.AddMethod((GetQueueDelegate)GetQueue); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, ulong*, int>)&GetIOKitRegistryId); 
#else
            base.AddMethod((GetIOKitRegistryIdDelegate)GetIOKitRegistryId); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, AvnPixelFormat, void**, int>)&ImportIOSurface); 
#else
            base.AddMethod((ImportIOSurfaceDelegate)ImportIOSurface); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void**, int>)&ImportSharedEvent); 
#else
            base.AddMethod((ImportSharedEventDelegate)ImportSharedEvent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, ulong, int>)&SubmitWait); 
#else
            base.AddMethod((SubmitWaitDelegate)SubmitWait); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, ulong, int>)&SubmitSignal); 
#else
            base.AddMethod((SubmitSignalDelegate)SubmitSignal); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnMetalDevice), new __MicroComIAvnMetalDeviceVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnMetalRenderTargetProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnMetalRenderTarget
    {
        public IAvnMetalRenderingSession BeginDrawing()
        {
            int __result;
            void* __marshal_ret = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &__marshal_ret);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("BeginDrawing failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMetalRenderingSession>(__marshal_ret, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnMetalRenderTarget), new Guid("f1306b71-eca0-426e-8700-105192693b1a"), (p, owns) => new __MicroComIAvnMetalRenderTargetProxy(p, owns));
        }

        protected __MicroComIAvnMetalRenderTargetProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIAvnMetalRenderTargetVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int BeginDrawingDelegate(void* @this, void** ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int BeginDrawing(void* @this, void** ret)
        {
            IAvnMetalRenderTarget __target = null;
            try
            {
                {
                    __target = (IAvnMetalRenderTarget)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.BeginDrawing();
                        *ret = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnMetalRenderTargetVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void**, int>)&BeginDrawing); 
#else
            base.AddMethod((BeginDrawingDelegate)BeginDrawing); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnMetalRenderTarget), new __MicroComIAvnMetalRenderTargetVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnMTLSharedEventProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnMTLSharedEvent
    {
        public IntPtr NativeHandle
        {
            get
            {
                IntPtr __result;
                __result = (IntPtr)((delegate* unmanaged[Stdcall]<void*, IntPtr>)(*PPV)[base.VTableSize + 0])(PPV);
                return __result;
            }
        }

        public int Wait(ulong value, ulong timeoutMS)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, ulong, ulong, int>)(*PPV)[base.VTableSize + 1])(PPV, value, timeoutMS);
            return __result;
        }

        public void SetSignaledValue(ulong value)
        {
            ((delegate* unmanaged[Stdcall]<void*, ulong, void>)(*PPV)[base.VTableSize + 2])(PPV, value);
        }

        public ulong SignaledValue
        {
            get
            {
                ulong __result;
                __result = (ulong)((delegate* unmanaged[Stdcall]<void*, ulong>)(*PPV)[base.VTableSize + 3])(PPV);
                return __result;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnMTLSharedEvent), new Guid("a1f4fcde-9152-48bd-bf8a-b1b651134a69"), (p, owns) => new __MicroComIAvnMTLSharedEventProxy(p, owns));
        }

        protected __MicroComIAvnMTLSharedEventProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 4;
    }

    unsafe class __MicroComIAvnMTLSharedEventVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate IntPtr GetNativeHandleDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static IntPtr GetNativeHandle(void* @this)
        {
            IAvnMTLSharedEvent __target = null;
            try
            {
                {
                    __target = (IAvnMTLSharedEvent)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.NativeHandle;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int WaitDelegate(void* @this, ulong value, ulong timeoutMS);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Wait(void* @this, ulong value, ulong timeoutMS)
        {
            IAvnMTLSharedEvent __target = null;
            try
            {
                {
                    __target = (IAvnMTLSharedEvent)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Wait(value, timeoutMS);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SetSignaledValueDelegate(void* @this, ulong value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void SetSignaledValue(void* @this, ulong value)
        {
            IAvnMTLSharedEvent __target = null;
            try
            {
                {
                    __target = (IAvnMTLSharedEvent)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetSignaledValue(value);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate ulong GetSignaledValueDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static ulong GetSignaledValue(void* @this)
        {
            IAvnMTLSharedEvent __target = null;
            try
            {
                {
                    __target = (IAvnMTLSharedEvent)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.SignaledValue;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComIAvnMTLSharedEventVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr>)&GetNativeHandle); 
#else
            base.AddMethod((GetNativeHandleDelegate)GetNativeHandle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, ulong, ulong, int>)&Wait); 
#else
            base.AddMethod((WaitDelegate)Wait); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, ulong, void>)&SetSignaledValue); 
#else
            base.AddMethod((SetSignaledValueDelegate)SetSignaledValue); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, ulong>)&GetSignaledValue); 
#else
            base.AddMethod((GetSignaledValueDelegate)GetSignaledValue); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnMTLSharedEvent), new __MicroComIAvnMTLSharedEventVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnMetalTextureProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnMetalTexture
    {
        public IntPtr NativeHandle
        {
            get
            {
                IntPtr __result;
                __result = (IntPtr)((delegate* unmanaged[Stdcall]<void*, IntPtr>)(*PPV)[base.VTableSize + 0])(PPV);
                return __result;
            }
        }

        public int Width
        {
            get
            {
                int __result;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 1])(PPV);
                return __result;
            }
        }

        public int Height
        {
            get
            {
                int __result;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 2])(PPV);
                return __result;
            }
        }

        public int SampleCount
        {
            get
            {
                int __result;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 3])(PPV);
                return __result;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnMetalTexture), new Guid("722aad20-a87b-4ce5-b50f-f05cfa4cda39"), (p, owns) => new __MicroComIAvnMetalTextureProxy(p, owns));
        }

        protected __MicroComIAvnMetalTextureProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 4;
    }

    unsafe class __MicroComIAvnMetalTextureVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate IntPtr GetNativeHandleDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static IntPtr GetNativeHandle(void* @this)
        {
            IAvnMetalTexture __target = null;
            try
            {
                {
                    __target = (IAvnMetalTexture)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.NativeHandle;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetWidthDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetWidth(void* @this)
        {
            IAvnMetalTexture __target = null;
            try
            {
                {
                    __target = (IAvnMetalTexture)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Width;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetHeightDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetHeight(void* @this)
        {
            IAvnMetalTexture __target = null;
            try
            {
                {
                    __target = (IAvnMetalTexture)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Height;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetSampleCountDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetSampleCount(void* @this)
        {
            IAvnMetalTexture __target = null;
            try
            {
                {
                    __target = (IAvnMetalTexture)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.SampleCount;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComIAvnMetalTextureVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr>)&GetNativeHandle); 
#else
            base.AddMethod((GetNativeHandleDelegate)GetNativeHandle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&GetWidth); 
#else
            base.AddMethod((GetWidthDelegate)GetWidth); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&GetHeight); 
#else
            base.AddMethod((GetHeightDelegate)GetHeight); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&GetSampleCount); 
#else
            base.AddMethod((GetSampleCountDelegate)GetSampleCount); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnMetalTexture), new __MicroComIAvnMetalTextureVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnNativeObjectsMemoryManagementProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnNativeObjectsMemoryManagement
    {
        public void RetainNSObject(IntPtr obj)
        {
            ((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)(*PPV)[base.VTableSize + 0])(PPV, obj);
        }

        public void ReleaseNSObject(IntPtr obj)
        {
            ((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)(*PPV)[base.VTableSize + 1])(PPV, obj);
        }

        public ulong GetRetainCountForNSObject(IntPtr obj)
        {
            ulong __result;
            __result = (ulong)((delegate* unmanaged[Stdcall]<void*, IntPtr, ulong>)(*PPV)[base.VTableSize + 2])(PPV, obj);
            return __result;
        }

        public void RetainCFObject(IntPtr obj)
        {
            ((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)(*PPV)[base.VTableSize + 3])(PPV, obj);
        }

        public void ReleaseCFObject(IntPtr obj)
        {
            ((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)(*PPV)[base.VTableSize + 4])(PPV, obj);
        }

        public long GetRetainCountForCFObject(IntPtr obj)
        {
            long __result;
            __result = (long)((delegate* unmanaged[Stdcall]<void*, IntPtr, long>)(*PPV)[base.VTableSize + 5])(PPV, obj);
            return __result;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnNativeObjectsMemoryManagement), new Guid("74027aa2-5262-45a5-a74a-5a53373dcc17"), (p, owns) => new __MicroComIAvnNativeObjectsMemoryManagementProxy(p, owns));
        }

        protected __MicroComIAvnNativeObjectsMemoryManagementProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 6;
    }

    unsafe class __MicroComIAvnNativeObjectsMemoryManagementVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void RetainNSObjectDelegate(void* @this, IntPtr obj);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void RetainNSObject(void* @this, IntPtr obj)
        {
            IAvnNativeObjectsMemoryManagement __target = null;
            try
            {
                {
                    __target = (IAvnNativeObjectsMemoryManagement)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.RetainNSObject(obj);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ReleaseNSObjectDelegate(void* @this, IntPtr obj);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ReleaseNSObject(void* @this, IntPtr obj)
        {
            IAvnNativeObjectsMemoryManagement __target = null;
            try
            {
                {
                    __target = (IAvnNativeObjectsMemoryManagement)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ReleaseNSObject(obj);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate ulong GetRetainCountForNSObjectDelegate(void* @this, IntPtr obj);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static ulong GetRetainCountForNSObject(void* @this, IntPtr obj)
        {
            IAvnNativeObjectsMemoryManagement __target = null;
            try
            {
                {
                    __target = (IAvnNativeObjectsMemoryManagement)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetRetainCountForNSObject(obj);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void RetainCFObjectDelegate(void* @this, IntPtr obj);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void RetainCFObject(void* @this, IntPtr obj)
        {
            IAvnNativeObjectsMemoryManagement __target = null;
            try
            {
                {
                    __target = (IAvnNativeObjectsMemoryManagement)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.RetainCFObject(obj);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ReleaseCFObjectDelegate(void* @this, IntPtr obj);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ReleaseCFObject(void* @this, IntPtr obj)
        {
            IAvnNativeObjectsMemoryManagement __target = null;
            try
            {
                {
                    __target = (IAvnNativeObjectsMemoryManagement)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ReleaseCFObject(obj);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate long GetRetainCountForCFObjectDelegate(void* @this, IntPtr obj);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static long GetRetainCountForCFObject(void* @this, IntPtr obj)
        {
            IAvnNativeObjectsMemoryManagement __target = null;
            try
            {
                {
                    __target = (IAvnNativeObjectsMemoryManagement)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.GetRetainCountForCFObject(obj);
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComIAvnNativeObjectsMemoryManagementVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)&RetainNSObject); 
#else
            base.AddMethod((RetainNSObjectDelegate)RetainNSObject); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)&ReleaseNSObject); 
#else
            base.AddMethod((ReleaseNSObjectDelegate)ReleaseNSObject); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, ulong>)&GetRetainCountForNSObject); 
#else
            base.AddMethod((GetRetainCountForNSObjectDelegate)GetRetainCountForNSObject); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)&RetainCFObject); 
#else
            base.AddMethod((RetainCFObjectDelegate)RetainCFObject); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)&ReleaseCFObject); 
#else
            base.AddMethod((ReleaseCFObjectDelegate)ReleaseCFObject); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, long>)&GetRetainCountForCFObject); 
#else
            base.AddMethod((GetRetainCountForCFObjectDelegate)GetRetainCountForCFObject); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnNativeObjectsMemoryManagement), new __MicroComIAvnNativeObjectsMemoryManagementVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnMetalRenderingSessionProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnMetalRenderingSession
    {
        public AvnPixelSize PixelSize
        {
            get
            {
                int __result;
                AvnPixelSize ret = default;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, &ret);
                if (__result != 0)
                    throw new System.Runtime.InteropServices.COMException("GetPixelSize failed", __result);
                return ret;
            }
        }

        public double Scaling
        {
            get
            {
                double __result;
                __result = (double)((delegate* unmanaged[Stdcall]<void*, double>)(*PPV)[base.VTableSize + 1])(PPV);
                return __result;
            }
        }

        public IntPtr Texture
        {
            get
            {
                IntPtr __result;
                __result = (IntPtr)((delegate* unmanaged[Stdcall]<void*, IntPtr>)(*PPV)[base.VTableSize + 2])(PPV);
                return __result;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnMetalRenderingSession), new Guid("e625b406-f04c-484e-946a-4abd2c6015ad"), (p, owns) => new __MicroComIAvnMetalRenderingSessionProxy(p, owns));
        }

        protected __MicroComIAvnMetalRenderingSessionProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 3;
    }

    unsafe class __MicroComIAvnMetalRenderingSessionVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetPixelSizeDelegate(void* @this, AvnPixelSize* ret);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetPixelSize(void* @this, AvnPixelSize* ret)
        {
            IAvnMetalRenderingSession __target = null;
            try
            {
                {
                    __target = (IAvnMetalRenderingSession)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.PixelSize;
                        *ret = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate double GetScalingDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static double GetScaling(void* @this)
        {
            IAvnMetalRenderingSession __target = null;
            try
            {
                {
                    __target = (IAvnMetalRenderingSession)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Scaling;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate IntPtr GetTextureDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static IntPtr GetTexture(void* @this)
        {
            IAvnMetalRenderingSession __target = null;
            try
            {
                {
                    __target = (IAvnMetalRenderingSession)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Texture;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComIAvnMetalRenderingSessionVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnPixelSize*, int>)&GetPixelSize); 
#else
            base.AddMethod((GetPixelSizeDelegate)GetPixelSize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double>)&GetScaling); 
#else
            base.AddMethod((GetScalingDelegate)GetScaling); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr>)&GetTexture); 
#else
            base.AddMethod((GetTextureDelegate)GetTexture); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnMetalRenderingSession), new __MicroComIAvnMetalRenderingSessionVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnTrayIconProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnTrayIcon
    {
        public void SetIcon(void* data, System.IntPtr length)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, System.IntPtr, int>)(*PPV)[base.VTableSize + 0])(PPV, data, length);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetIcon failed", __result);
        }

        public void SetMenu(IAvnMenu menu)
        {
            int __result;
            using var __menu = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(menu);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, __menu.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetMenu failed", __result);
        }

        public void SetIsVisible(int isVisible)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 2])(PPV, isVisible);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetIsVisible failed", __result);
        }

        public void SetToolTipText(string text)
        {
            int __result;
            var __bytemarshal_text = new byte[System.Text.Encoding.UTF8.GetByteCount(text) + 1];
            System.Text.Encoding.UTF8.GetBytes(text, 0, text.Length, __bytemarshal_text, 0);
            fixed (byte* __fixedmarshal_text = __bytemarshal_text)
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 3])(PPV, __fixedmarshal_text);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetToolTipText failed", __result);
        }

        public void SetIsTemplateIcon(int text)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 4])(PPV, text);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetIsTemplateIcon failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnTrayIcon), new Guid("60992d19-38f0-4141-a0a9-76ac303801f3"), (p, owns) => new __MicroComIAvnTrayIconProxy(p, owns));
        }

        protected __MicroComIAvnTrayIconProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 5;
    }

    unsafe class __MicroComIAvnTrayIconVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetIconDelegate(void* @this, void* data, System.IntPtr length);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetIcon(void* @this, void* data, System.IntPtr length)
        {
            IAvnTrayIcon __target = null;
            try
            {
                {
                    __target = (IAvnTrayIcon)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetIcon(data, length);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetMenuDelegate(void* @this, void* menu);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetMenu(void* @this, void* menu)
        {
            IAvnTrayIcon __target = null;
            try
            {
                {
                    __target = (IAvnTrayIcon)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetMenu(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMenu>(menu, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetIsVisibleDelegate(void* @this, int isVisible);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetIsVisible(void* @this, int isVisible)
        {
            IAvnTrayIcon __target = null;
            try
            {
                {
                    __target = (IAvnTrayIcon)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetIsVisible(isVisible);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetToolTipTextDelegate(void* @this, byte* text);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetToolTipText(void* @this, byte* text)
        {
            IAvnTrayIcon __target = null;
            try
            {
                {
                    __target = (IAvnTrayIcon)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetToolTipText((text == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(text))));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetIsTemplateIconDelegate(void* @this, int text);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetIsTemplateIcon(void* @this, int text)
        {
            IAvnTrayIcon __target = null;
            try
            {
                {
                    __target = (IAvnTrayIcon)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetIsTemplateIcon(text);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnTrayIconVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, System.IntPtr, int>)&SetIcon); 
#else
            base.AddMethod((SetIconDelegate)SetIcon); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetMenu); 
#else
            base.AddMethod((SetMenuDelegate)SetMenu); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetIsVisible); 
#else
            base.AddMethod((SetIsVisibleDelegate)SetIsVisible); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, byte*, int>)&SetToolTipText); 
#else
            base.AddMethod((SetToolTipTextDelegate)SetToolTipText); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetIsTemplateIcon); 
#else
            base.AddMethod((SetIsTemplateIconDelegate)SetIsTemplateIcon); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnTrayIcon), new __MicroComIAvnTrayIconVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnMenuProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnMenu
    {
        public void InsertItem(int index, IAvnMenuItem item)
        {
            int __result;
            using var __item = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(item);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, index, __item.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("InsertItem failed", __result);
        }

        public void RemoveItem(IAvnMenuItem item)
        {
            int __result;
            using var __item = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(item);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, __item.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("RemoveItem failed", __result);
        }

        public void SetTitle(string utf8String)
        {
            int __result;
            var __bytemarshal_utf8String = new byte[System.Text.Encoding.UTF8.GetByteCount(utf8String) + 1];
            System.Text.Encoding.UTF8.GetBytes(utf8String, 0, utf8String.Length, __bytemarshal_utf8String, 0);
            fixed (byte* __fixedmarshal_utf8String = __bytemarshal_utf8String)
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, __fixedmarshal_utf8String);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetTitle failed", __result);
        }

        public void Clear()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 3])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Clear failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnMenu), new Guid("a7724dc1-cf6b-4fa8-9d23-228bf2593edc"), (p, owns) => new __MicroComIAvnMenuProxy(p, owns));
        }

        protected __MicroComIAvnMenuProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 4;
    }

    unsafe class __MicroComIAvnMenuVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int InsertItemDelegate(void* @this, int index, void* item);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int InsertItem(void* @this, int index, void* item)
        {
            IAvnMenu __target = null;
            try
            {
                {
                    __target = (IAvnMenu)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.InsertItem(index, global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMenuItem>(item, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int RemoveItemDelegate(void* @this, void* item);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int RemoveItem(void* @this, void* item)
        {
            IAvnMenu __target = null;
            try
            {
                {
                    __target = (IAvnMenu)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.RemoveItem(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMenuItem>(item, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetTitleDelegate(void* @this, byte* utf8String);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetTitle(void* @this, byte* utf8String)
        {
            IAvnMenu __target = null;
            try
            {
                {
                    __target = (IAvnMenu)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetTitle((utf8String == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(utf8String))));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ClearDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Clear(void* @this)
        {
            IAvnMenu __target = null;
            try
            {
                {
                    __target = (IAvnMenu)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Clear();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnMenuVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, void*, int>)&InsertItem); 
#else
            base.AddMethod((InsertItemDelegate)InsertItem); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&RemoveItem); 
#else
            base.AddMethod((RemoveItemDelegate)RemoveItem); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, byte*, int>)&SetTitle); 
#else
            base.AddMethod((SetTitleDelegate)SetTitle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&Clear); 
#else
            base.AddMethod((ClearDelegate)Clear); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnMenu), new __MicroComIAvnMenuVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnPredicateCallbackProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnPredicateCallback
    {
        public int Evaluate()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 0])(PPV);
            return __result;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnPredicateCallback), new Guid("59e0586d-bd1c-4b85-9882-80d448b0fed9"), (p, owns) => new __MicroComIAvnPredicateCallbackProxy(p, owns));
        }

        protected __MicroComIAvnPredicateCallbackProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIAvnPredicateCallbackVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int EvaluateDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Evaluate(void* @this)
        {
            IAvnPredicateCallback __target = null;
            try
            {
                {
                    __target = (IAvnPredicateCallback)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Evaluate();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComIAvnPredicateCallbackVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&Evaluate); 
#else
            base.AddMethod((EvaluateDelegate)Evaluate); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnPredicateCallback), new __MicroComIAvnPredicateCallbackVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnMenuItemProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnMenuItem
    {
        public void SetSubMenu(IAvnMenu menu)
        {
            int __result;
            using var __menu = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(menu);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, __menu.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetSubMenu failed", __result);
        }

        public void SetTitle(string utf8String)
        {
            int __result;
            var __bytemarshal_utf8String = new byte[System.Text.Encoding.UTF8.GetByteCount(utf8String) + 1];
            System.Text.Encoding.UTF8.GetBytes(utf8String, 0, utf8String.Length, __bytemarshal_utf8String, 0);
            fixed (byte* __fixedmarshal_utf8String = __bytemarshal_utf8String)
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, __fixedmarshal_utf8String);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetTitle failed", __result);
        }

        public void SetToolTip(string utf8String)
        {
            int __result;
            var __bytemarshal_utf8String = new byte[System.Text.Encoding.UTF8.GetByteCount(utf8String) + 1];
            System.Text.Encoding.UTF8.GetBytes(utf8String, 0, utf8String.Length, __bytemarshal_utf8String, 0);
            fixed (byte* __fixedmarshal_utf8String = __bytemarshal_utf8String)
                __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, __fixedmarshal_utf8String);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetToolTip failed", __result);
        }

        public void SetGesture(AvnKey key, AvnInputModifiers modifiers)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnKey, AvnInputModifiers, int>)(*PPV)[base.VTableSize + 3])(PPV, key, modifiers);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetGesture failed", __result);
        }

        public void SetAction(IAvnPredicateCallback predicate, IAvnActionCallback callback)
        {
            int __result;
            using var __predicate = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(predicate);
            using var __callback = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(callback);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)(*PPV)[base.VTableSize + 4])(PPV, __predicate.Pointer, __callback.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetAction failed", __result);
        }

        public void SetIsChecked(int isChecked)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 5])(PPV, isChecked);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetIsChecked failed", __result);
        }

        public void SetIsVisible(int isVisible)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int, int>)(*PPV)[base.VTableSize + 6])(PPV, isVisible);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetIsVisible failed", __result);
        }

        public void SetToggleType(AvnMenuItemToggleType toggleType)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, AvnMenuItemToggleType, int>)(*PPV)[base.VTableSize + 7])(PPV, toggleType);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetToggleType failed", __result);
        }

        public void SetIcon(void* data, System.IntPtr length)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, System.IntPtr, int>)(*PPV)[base.VTableSize + 8])(PPV, data, length);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("SetIcon failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnMenuItem), new Guid("f890219a-1720-4cd5-9a26-cd95fccbf53c"), (p, owns) => new __MicroComIAvnMenuItemProxy(p, owns));
        }

        protected __MicroComIAvnMenuItemProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 9;
    }

    unsafe class __MicroComIAvnMenuItemVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetSubMenuDelegate(void* @this, void* menu);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetSubMenu(void* @this, void* menu)
        {
            IAvnMenuItem __target = null;
            try
            {
                {
                    __target = (IAvnMenuItem)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetSubMenu(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnMenu>(menu, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetTitleDelegate(void* @this, byte* utf8String);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetTitle(void* @this, byte* utf8String)
        {
            IAvnMenuItem __target = null;
            try
            {
                {
                    __target = (IAvnMenuItem)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetTitle((utf8String == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(utf8String))));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetToolTipDelegate(void* @this, byte* utf8String);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetToolTip(void* @this, byte* utf8String)
        {
            IAvnMenuItem __target = null;
            try
            {
                {
                    __target = (IAvnMenuItem)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetToolTip((utf8String == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(utf8String))));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetGestureDelegate(void* @this, AvnKey key, AvnInputModifiers modifiers);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetGesture(void* @this, AvnKey key, AvnInputModifiers modifiers)
        {
            IAvnMenuItem __target = null;
            try
            {
                {
                    __target = (IAvnMenuItem)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetGesture(key, modifiers);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetActionDelegate(void* @this, void* predicate, void* callback);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetAction(void* @this, void* predicate, void* callback)
        {
            IAvnMenuItem __target = null;
            try
            {
                {
                    __target = (IAvnMenuItem)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetAction(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnPredicateCallback>(predicate, false), global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnActionCallback>(callback, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetIsCheckedDelegate(void* @this, int isChecked);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetIsChecked(void* @this, int isChecked)
        {
            IAvnMenuItem __target = null;
            try
            {
                {
                    __target = (IAvnMenuItem)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetIsChecked(isChecked);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetIsVisibleDelegate(void* @this, int isVisible);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetIsVisible(void* @this, int isVisible)
        {
            IAvnMenuItem __target = null;
            try
            {
                {
                    __target = (IAvnMenuItem)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetIsVisible(isVisible);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetToggleTypeDelegate(void* @this, AvnMenuItemToggleType toggleType);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetToggleType(void* @this, AvnMenuItemToggleType toggleType)
        {
            IAvnMenuItem __target = null;
            try
            {
                {
                    __target = (IAvnMenuItem)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetToggleType(toggleType);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SetIconDelegate(void* @this, void* data, System.IntPtr length);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SetIcon(void* @this, void* data, System.IntPtr length)
        {
            IAvnMenuItem __target = null;
            try
            {
                {
                    __target = (IAvnMenuItem)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetIcon(data, length);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnMenuItemVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&SetSubMenu); 
#else
            base.AddMethod((SetSubMenuDelegate)SetSubMenu); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, byte*, int>)&SetTitle); 
#else
            base.AddMethod((SetTitleDelegate)SetTitle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, byte*, int>)&SetToolTip); 
#else
            base.AddMethod((SetToolTipDelegate)SetToolTip); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnKey, AvnInputModifiers, int>)&SetGesture); 
#else
            base.AddMethod((SetGestureDelegate)SetGesture); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)&SetAction); 
#else
            base.AddMethod((SetActionDelegate)SetAction); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetIsChecked); 
#else
            base.AddMethod((SetIsCheckedDelegate)SetIsChecked); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, int>)&SetIsVisible); 
#else
            base.AddMethod((SetIsVisibleDelegate)SetIsVisible); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnMenuItemToggleType, int>)&SetToggleType); 
#else
            base.AddMethod((SetToggleTypeDelegate)SetToggleType); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, System.IntPtr, int>)&SetIcon); 
#else
            base.AddMethod((SetIconDelegate)SetIcon); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnMenuItem), new __MicroComIAvnMenuItemVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnMenuEventsProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnMenuEvents
    {
        public void NeedsUpdate()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 0])(PPV);
        }

        public void Opening()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 1])(PPV);
        }

        public void Closed()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 2])(PPV);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnMenuEvents), new Guid("0af7df53-7632-42f4-a650-0992c361b477"), (p, owns) => new __MicroComIAvnMenuEventsProxy(p, owns));
        }

        protected __MicroComIAvnMenuEventsProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 3;
    }

    unsafe class __MicroComIAvnMenuEventsVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void NeedsUpdateDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void NeedsUpdate(void* @this)
        {
            IAvnMenuEvents __target = null;
            try
            {
                {
                    __target = (IAvnMenuEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.NeedsUpdate();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void OpeningDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Opening(void* @this)
        {
            IAvnMenuEvents __target = null;
            try
            {
                {
                    __target = (IAvnMenuEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Opening();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ClosedDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Closed(void* @this)
        {
            IAvnMenuEvents __target = null;
            try
            {
                {
                    __target = (IAvnMenuEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Closed();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnMenuEventsVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&NeedsUpdate); 
#else
            base.AddMethod((NeedsUpdateDelegate)NeedsUpdate); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Opening); 
#else
            base.AddMethod((OpeningDelegate)Opening); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Closed); 
#else
            base.AddMethod((ClosedDelegate)Closed); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnMenuEvents), new __MicroComIAvnMenuEventsVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnStringArrayProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnStringArray
    {
        public uint Count
        {
            get
            {
                uint __result;
                __result = (uint)((delegate* unmanaged[Stdcall]<void*, uint>)(*PPV)[base.VTableSize + 0])(PPV);
                return __result;
            }
        }

        public IAvnString Get(uint index)
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, index, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Get failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__marshal_ppv, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnStringArray), new Guid("5142bb41-66ab-49e7-bb37-cd079c000f27"), (p, owns) => new __MicroComIAvnStringArrayProxy(p, owns));
        }

        protected __MicroComIAvnStringArrayProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIAvnStringArrayVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate uint GetCountDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static uint GetCount(void* @this)
        {
            IAvnStringArray __target = null;
            try
            {
                {
                    __target = (IAvnStringArray)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Count;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetDelegate(void* @this, uint index, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Get(void* @this, uint index, void** ppv)
        {
            IAvnStringArray __target = null;
            try
            {
                {
                    __target = (IAvnStringArray)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Get(index);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnStringArrayVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint>)&GetCount); 
#else
            base.AddMethod((GetCountDelegate)GetCount); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)&Get); 
#else
            base.AddMethod((GetDelegate)Get); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnStringArray), new __MicroComIAvnStringArrayVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnDndResultCallbackProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnDndResultCallback
    {
        public void OnDragAndDropComplete(AvnDragDropEffects effecct)
        {
            ((delegate* unmanaged[Stdcall]<void*, AvnDragDropEffects, void>)(*PPV)[base.VTableSize + 0])(PPV, effecct);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnDndResultCallback), new Guid("a13d2382-3b3a-4d1c-9b27-8f34653d3f01"), (p, owns) => new __MicroComIAvnDndResultCallbackProxy(p, owns));
        }

        protected __MicroComIAvnDndResultCallbackProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIAvnDndResultCallbackVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void OnDragAndDropCompleteDelegate(void* @this, AvnDragDropEffects effecct);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void OnDragAndDropComplete(void* @this, AvnDragDropEffects effecct)
        {
            IAvnDndResultCallback __target = null;
            try
            {
                {
                    __target = (IAvnDndResultCallback)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.OnDragAndDropComplete(effecct);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnDndResultCallbackVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnDragDropEffects, void>)&OnDragAndDropComplete); 
#else
            base.AddMethod((OnDragAndDropCompleteDelegate)OnDragAndDropComplete); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnDndResultCallback), new __MicroComIAvnDndResultCallbackVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnGCHandleDeallocatorCallbackProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnGCHandleDeallocatorCallback
    {
        public void FreeGCHandle(IntPtr handle)
        {
            ((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)(*PPV)[base.VTableSize + 0])(PPV, handle);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnGCHandleDeallocatorCallback), new Guid("f07c608e-52e9-422d-836e-c70f6e9b80f5"), (p, owns) => new __MicroComIAvnGCHandleDeallocatorCallbackProxy(p, owns));
        }

        protected __MicroComIAvnGCHandleDeallocatorCallbackProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIAvnGCHandleDeallocatorCallbackVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void FreeGCHandleDelegate(void* @this, IntPtr handle);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void FreeGCHandle(void* @this, IntPtr handle)
        {
            IAvnGCHandleDeallocatorCallback __target = null;
            try
            {
                {
                    __target = (IAvnGCHandleDeallocatorCallback)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.FreeGCHandle(handle);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnGCHandleDeallocatorCallbackVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)&FreeGCHandle); 
#else
            base.AddMethod((FreeGCHandleDelegate)FreeGCHandle); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnGCHandleDeallocatorCallback), new __MicroComIAvnGCHandleDeallocatorCallbackVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnDispatcherProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnDispatcher
    {
        public void Post(IAvnActionCallback cb)
        {
            using var __cb = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(cb);
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 0])(PPV, __cb.Pointer);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnDispatcher), new Guid("96688589-5dc7-41ec-9ce3-d481942454ee"), (p, owns) => new __MicroComIAvnDispatcherProxy(p, owns));
        }

        protected __MicroComIAvnDispatcherProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIAvnDispatcherVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void PostDelegate(void* @this, void* cb);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Post(void* @this, void* cb)
        {
            IAvnDispatcher __target = null;
            try
            {
                {
                    __target = (IAvnDispatcher)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Post(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnActionCallback>(cb, false));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnDispatcherVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void>)&Post); 
#else
            base.AddMethod((PostDelegate)Post); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnDispatcher), new __MicroComIAvnDispatcherVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnNativeControlHostProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnNativeControlHost
    {
        public IntPtr CreateDefaultChild(IntPtr parent)
        {
            int __result;
            IntPtr retOut = default;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, parent, &retOut);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("CreateDefaultChild failed", __result);
            return retOut;
        }

        public IAvnNativeControlHostTopLevelAttachment CreateAttachment()
        {
            void* __result;
            __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 1])(PPV);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnNativeControlHostTopLevelAttachment>(__result, true);
        }

        public void DestroyDefaultChild(IntPtr child)
        {
            ((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)(*PPV)[base.VTableSize + 2])(PPV, child);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnNativeControlHost), new Guid("91c7f677-f26b-4ff3-93cc-cf15aa966ffa"), (p, owns) => new __MicroComIAvnNativeControlHostProxy(p, owns));
        }

        protected __MicroComIAvnNativeControlHostProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 3;
    }

    unsafe class __MicroComIAvnNativeControlHostVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int CreateDefaultChildDelegate(void* @this, IntPtr parent, IntPtr* retOut);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int CreateDefaultChild(void* @this, IntPtr parent, IntPtr* retOut)
        {
            IAvnNativeControlHost __target = null;
            try
            {
                {
                    __target = (IAvnNativeControlHost)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateDefaultChild(parent);
                        *retOut = __result;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* CreateAttachmentDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* CreateAttachment(void* @this)
        {
            IAvnNativeControlHost __target = null;
            try
            {
                {
                    __target = (IAvnNativeControlHost)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.CreateAttachment();
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void DestroyDefaultChildDelegate(void* @this, IntPtr child);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void DestroyDefaultChild(void* @this, IntPtr child)
        {
            IAvnNativeControlHost __target = null;
            try
            {
                {
                    __target = (IAvnNativeControlHost)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.DestroyDefaultChild(child);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnNativeControlHostVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, IntPtr*, int>)&CreateDefaultChild); 
#else
            base.AddMethod((CreateDefaultChildDelegate)CreateDefaultChild); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&CreateAttachment); 
#else
            base.AddMethod((CreateAttachmentDelegate)CreateAttachment); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, void>)&DestroyDefaultChild); 
#else
            base.AddMethod((DestroyDefaultChildDelegate)DestroyDefaultChild); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnNativeControlHost), new __MicroComIAvnNativeControlHostVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnNativeControlHostTopLevelAttachmentProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnNativeControlHostTopLevelAttachment
    {
        public IntPtr ParentHandle
        {
            get
            {
                IntPtr __result;
                __result = (IntPtr)((delegate* unmanaged[Stdcall]<void*, IntPtr>)(*PPV)[base.VTableSize + 0])(PPV);
                return __result;
            }
        }

        public void InitializeWithChildHandle(IntPtr child)
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, IntPtr, int>)(*PPV)[base.VTableSize + 1])(PPV, child);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("InitializeWithChildHandle failed", __result);
        }

        public void AttachTo(IAvnNativeControlHost host)
        {
            int __result;
            using var __host = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(host);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 2])(PPV, __host.Pointer);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("AttachTo failed", __result);
        }

        public void ShowInBounds(float x, float y, float width, float height)
        {
            ((delegate* unmanaged[Stdcall]<void*, float, float, float, float, void>)(*PPV)[base.VTableSize + 3])(PPV, x, y, width, height);
        }

        public void HideWithSize(float width, float height)
        {
            ((delegate* unmanaged[Stdcall]<void*, float, float, void>)(*PPV)[base.VTableSize + 4])(PPV, width, height);
        }

        public void ReleaseChild()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 5])(PPV);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnNativeControlHostTopLevelAttachment), new Guid("14a9e164-1aae-4271-bb78-7b5230999b52"), (p, owns) => new __MicroComIAvnNativeControlHostTopLevelAttachmentProxy(p, owns));
        }

        protected __MicroComIAvnNativeControlHostTopLevelAttachmentProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 6;
    }

    unsafe class __MicroComIAvnNativeControlHostTopLevelAttachmentVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate IntPtr GetParentHandleDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static IntPtr GetParentHandle(void* @this)
        {
            IAvnNativeControlHostTopLevelAttachment __target = null;
            try
            {
                {
                    __target = (IAvnNativeControlHostTopLevelAttachment)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ParentHandle;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int InitializeWithChildHandleDelegate(void* @this, IntPtr child);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int InitializeWithChildHandle(void* @this, IntPtr child)
        {
            IAvnNativeControlHostTopLevelAttachment __target = null;
            try
            {
                {
                    __target = (IAvnNativeControlHostTopLevelAttachment)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.InitializeWithChildHandle(child);
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int AttachToDelegate(void* @this, void* host);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int AttachTo(void* @this, void* host)
        {
            IAvnNativeControlHostTopLevelAttachment __target = null;
            try
            {
                {
                    __target = (IAvnNativeControlHostTopLevelAttachment)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.AttachTo(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnNativeControlHost>(host, false));
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ShowInBoundsDelegate(void* @this, float x, float y, float width, float height);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ShowInBounds(void* @this, float x, float y, float width, float height)
        {
            IAvnNativeControlHostTopLevelAttachment __target = null;
            try
            {
                {
                    __target = (IAvnNativeControlHostTopLevelAttachment)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ShowInBounds(x, y, width, height);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void HideWithSizeDelegate(void* @this, float width, float height);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void HideWithSize(void* @this, float width, float height)
        {
            IAvnNativeControlHostTopLevelAttachment __target = null;
            try
            {
                {
                    __target = (IAvnNativeControlHostTopLevelAttachment)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.HideWithSize(width, height);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ReleaseChildDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ReleaseChild(void* @this)
        {
            IAvnNativeControlHostTopLevelAttachment __target = null;
            try
            {
                {
                    __target = (IAvnNativeControlHostTopLevelAttachment)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ReleaseChild();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnNativeControlHostTopLevelAttachmentVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr>)&GetParentHandle); 
#else
            base.AddMethod((GetParentHandleDelegate)GetParentHandle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr, int>)&InitializeWithChildHandle); 
#else
            base.AddMethod((InitializeWithChildHandleDelegate)InitializeWithChildHandle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&AttachTo); 
#else
            base.AddMethod((AttachToDelegate)AttachTo); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, float, float, float, float, void>)&ShowInBounds); 
#else
            base.AddMethod((ShowInBoundsDelegate)ShowInBounds); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, float, float, void>)&HideWithSize); 
#else
            base.AddMethod((HideWithSizeDelegate)HideWithSize); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&ReleaseChild); 
#else
            base.AddMethod((ReleaseChildDelegate)ReleaseChild); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnNativeControlHostTopLevelAttachment), new __MicroComIAvnNativeControlHostTopLevelAttachmentVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnApplicationEventsProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnApplicationEvents
    {
        public void FilesOpened(IAvnStringArray args)
        {
            using var __args = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(args);
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 0])(PPV, __args.Pointer);
        }

        public void UrlsOpened(IAvnStringArray urls)
        {
            using var __urls = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(urls);
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 1])(PPV, __urls.Pointer);
        }

        public int TryShutdown()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 2])(PPV);
            return __result;
        }

        public void OnReopen()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 3])(PPV);
        }

        public void OnHide()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 4])(PPV);
        }

        public void OnUnhide()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 5])(PPV);
        }

        public void OnActivate()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 6])(PPV);
        }

        public void OnDeactivate()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 7])(PPV);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnApplicationEvents), new Guid("6575b5af-f27a-4609-866c-f1f014c20f79"), (p, owns) => new __MicroComIAvnApplicationEventsProxy(p, owns));
        }

        protected __MicroComIAvnApplicationEventsProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 8;
    }

    unsafe class __MicroComIAvnApplicationEventsVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void FilesOpenedDelegate(void* @this, void* args);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void FilesOpened(void* @this, void* args)
        {
            IAvnApplicationEvents __target = null;
            try
            {
                {
                    __target = (IAvnApplicationEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.FilesOpened(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnStringArray>(args, false));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void UrlsOpenedDelegate(void* @this, void* urls);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void UrlsOpened(void* @this, void* urls)
        {
            IAvnApplicationEvents __target = null;
            try
            {
                {
                    __target = (IAvnApplicationEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.UrlsOpened(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnStringArray>(urls, false));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int TryShutdownDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int TryShutdown(void* @this)
        {
            IAvnApplicationEvents __target = null;
            try
            {
                {
                    __target = (IAvnApplicationEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.TryShutdown();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void OnReopenDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void OnReopen(void* @this)
        {
            IAvnApplicationEvents __target = null;
            try
            {
                {
                    __target = (IAvnApplicationEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.OnReopen();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void OnHideDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void OnHide(void* @this)
        {
            IAvnApplicationEvents __target = null;
            try
            {
                {
                    __target = (IAvnApplicationEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.OnHide();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void OnUnhideDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void OnUnhide(void* @this)
        {
            IAvnApplicationEvents __target = null;
            try
            {
                {
                    __target = (IAvnApplicationEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.OnUnhide();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void OnActivateDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void OnActivate(void* @this)
        {
            IAvnApplicationEvents __target = null;
            try
            {
                {
                    __target = (IAvnApplicationEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.OnActivate();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void OnDeactivateDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void OnDeactivate(void* @this)
        {
            IAvnApplicationEvents __target = null;
            try
            {
                {
                    __target = (IAvnApplicationEvents)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.OnDeactivate();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnApplicationEventsVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void>)&FilesOpened); 
#else
            base.AddMethod((FilesOpenedDelegate)FilesOpened); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void>)&UrlsOpened); 
#else
            base.AddMethod((UrlsOpenedDelegate)UrlsOpened); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&TryShutdown); 
#else
            base.AddMethod((TryShutdownDelegate)TryShutdown); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&OnReopen); 
#else
            base.AddMethod((OnReopenDelegate)OnReopen); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&OnHide); 
#else
            base.AddMethod((OnHideDelegate)OnHide); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&OnUnhide); 
#else
            base.AddMethod((OnUnhideDelegate)OnUnhide); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&OnActivate); 
#else
            base.AddMethod((OnActivateDelegate)OnActivate); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&OnDeactivate); 
#else
            base.AddMethod((OnDeactivateDelegate)OnDeactivate); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnApplicationEvents), new __MicroComIAvnApplicationEventsVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnApplicationCommandsProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnApplicationCommands
    {
        public void UnhideApp()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 0])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("UnhideApp failed", __result);
        }

        public void HideApp()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 1])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("HideApp failed", __result);
        }

        public void ShowAll()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 2])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("ShowAll failed", __result);
        }

        public void HideOthers()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 3])(PPV);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("HideOthers failed", __result);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnApplicationCommands), new Guid("b4284791-055b-4313-8c2e-50f0a8c72ce9"), (p, owns) => new __MicroComIAvnApplicationCommandsProxy(p, owns));
        }

        protected __MicroComIAvnApplicationCommandsProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 4;
    }

    unsafe class __MicroComIAvnApplicationCommandsVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int UnhideAppDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int UnhideApp(void* @this)
        {
            IAvnApplicationCommands __target = null;
            try
            {
                {
                    __target = (IAvnApplicationCommands)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.UnhideApp();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int HideAppDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int HideApp(void* @this)
        {
            IAvnApplicationCommands __target = null;
            try
            {
                {
                    __target = (IAvnApplicationCommands)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.HideApp();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ShowAllDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ShowAll(void* @this)
        {
            IAvnApplicationCommands __target = null;
            try
            {
                {
                    __target = (IAvnApplicationCommands)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ShowAll();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int HideOthersDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int HideOthers(void* @this)
        {
            IAvnApplicationCommands __target = null;
            try
            {
                {
                    __target = (IAvnApplicationCommands)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.HideOthers();
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnApplicationCommandsVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&UnhideApp); 
#else
            base.AddMethod((UnhideAppDelegate)UnhideApp); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&HideApp); 
#else
            base.AddMethod((HideAppDelegate)HideApp); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&ShowAll); 
#else
            base.AddMethod((ShowAllDelegate)ShowAll); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&HideOthers); 
#else
            base.AddMethod((HideOthersDelegate)HideOthers); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnApplicationCommands), new __MicroComIAvnApplicationCommandsVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnAutomationPeerProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnAutomationPeer
    {
        public IAvnAutomationNode Node
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 0])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationNode>(__result, true);
            }
        }

        public void SetNode(IAvnAutomationNode node)
        {
            using var __node = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(node);
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 1])(PPV, __node.Pointer);
        }

        public IAvnString AcceleratorKey
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 2])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__result, true);
            }
        }

        public IAvnString AccessKey
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 3])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__result, true);
            }
        }

        public AvnAutomationControlType AutomationControlType
        {
            get
            {
                AvnAutomationControlType __result;
                __result = (AvnAutomationControlType)((delegate* unmanaged[Stdcall]<void*, AvnAutomationControlType>)(*PPV)[base.VTableSize + 4])(PPV);
                return __result;
            }
        }

        public IAvnString AutomationId
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 5])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__result, true);
            }
        }

        public AvnRect BoundingRectangle
        {
            get
            {
                AvnRect __result;
                __result = (AvnRect)((delegate* unmanaged[Stdcall]<void*, AvnRect>)(*PPV)[base.VTableSize + 6])(PPV);
                return __result;
            }
        }

        public IAvnAutomationPeerArray Children
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 7])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationPeerArray>(__result, true);
            }
        }

        public IAvnString ClassName
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 8])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__result, true);
            }
        }

        public IAvnAutomationPeer LabeledBy
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 9])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationPeer>(__result, true);
            }
        }

        public IAvnString Name
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 10])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__result, true);
            }
        }

        public IAvnAutomationPeer Parent
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 11])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationPeer>(__result, true);
            }
        }

        public IAvnAutomationPeer VisualRoot
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 12])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationPeer>(__result, true);
            }
        }

        public int HasKeyboardFocus()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 13])(PPV);
            return __result;
        }

        public int IsContentElement()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 14])(PPV);
            return __result;
        }

        public int IsControlElement()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 15])(PPV);
            return __result;
        }

        public int IsEnabled()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 16])(PPV);
            return __result;
        }

        public int IsKeyboardFocusable()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 17])(PPV);
            return __result;
        }

        public void SetFocus()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 18])(PPV);
        }

        public int ShowContextMenu()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 19])(PPV);
            return __result;
        }

        public IAvnAutomationPeer RootPeer
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 20])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationPeer>(__result, true);
            }
        }

        public int IsInteropPeer()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 21])(PPV);
            return __result;
        }

        public IntPtr InteropPeer_GetNativeControlHandle()
        {
            IntPtr __result;
            __result = (IntPtr)((delegate* unmanaged[Stdcall]<void*, IntPtr>)(*PPV)[base.VTableSize + 22])(PPV);
            return __result;
        }

        public int IsRootProvider()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 23])(PPV);
            return __result;
        }

        public IAvnWindowBase RootProvider_GetWindow()
        {
            void* __result;
            __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 24])(PPV);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnWindowBase>(__result, true);
        }

        public IAvnAutomationPeer RootProvider_GetFocus()
        {
            void* __result;
            __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 25])(PPV);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationPeer>(__result, true);
        }

        public IAvnAutomationPeer RootProvider_GetPeerFromPoint(AvnPoint point)
        {
            void* __result;
            __result = (void*)((delegate* unmanaged[Stdcall]<void*, AvnPoint, void*>)(*PPV)[base.VTableSize + 26])(PPV, point);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationPeer>(__result, true);
        }

        public int IsEmbeddedRootProvider()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 27])(PPV);
            return __result;
        }

        public IAvnAutomationPeer EmbeddedRootProvider_GetFocus()
        {
            void* __result;
            __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 28])(PPV);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationPeer>(__result, true);
        }

        public IAvnAutomationPeer EmbeddedRootProvider_GetPeerFromPoint(AvnPoint point)
        {
            void* __result;
            __result = (void*)((delegate* unmanaged[Stdcall]<void*, AvnPoint, void*>)(*PPV)[base.VTableSize + 29])(PPV, point);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationPeer>(__result, true);
        }

        public int IsExpandCollapseProvider()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 30])(PPV);
            return __result;
        }

        public int ExpandCollapseProvider_GetIsExpanded()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 31])(PPV);
            return __result;
        }

        public int ExpandCollapseProvider_GetShowsMenu()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 32])(PPV);
            return __result;
        }

        public void ExpandCollapseProvider_Expand()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 33])(PPV);
        }

        public void ExpandCollapseProvider_Collapse()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 34])(PPV);
        }

        public int IsInvokeProvider()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 35])(PPV);
            return __result;
        }

        public void InvokeProvider_Invoke()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 36])(PPV);
        }

        public int IsRangeValueProvider()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 37])(PPV);
            return __result;
        }

        public double RangeValueProvider_GetValue()
        {
            double __result;
            __result = (double)((delegate* unmanaged[Stdcall]<void*, double>)(*PPV)[base.VTableSize + 38])(PPV);
            return __result;
        }

        public double RangeValueProvider_GetMinimum()
        {
            double __result;
            __result = (double)((delegate* unmanaged[Stdcall]<void*, double>)(*PPV)[base.VTableSize + 39])(PPV);
            return __result;
        }

        public double RangeValueProvider_GetMaximum()
        {
            double __result;
            __result = (double)((delegate* unmanaged[Stdcall]<void*, double>)(*PPV)[base.VTableSize + 40])(PPV);
            return __result;
        }

        public double RangeValueProvider_GetSmallChange()
        {
            double __result;
            __result = (double)((delegate* unmanaged[Stdcall]<void*, double>)(*PPV)[base.VTableSize + 41])(PPV);
            return __result;
        }

        public double RangeValueProvider_GetLargeChange()
        {
            double __result;
            __result = (double)((delegate* unmanaged[Stdcall]<void*, double>)(*PPV)[base.VTableSize + 42])(PPV);
            return __result;
        }

        public void RangeValueProvider_SetValue(double value)
        {
            ((delegate* unmanaged[Stdcall]<void*, double, void>)(*PPV)[base.VTableSize + 43])(PPV, value);
        }

        public int IsSelectionItemProvider()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 44])(PPV);
            return __result;
        }

        public int SelectionItemProvider_IsSelected()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 45])(PPV);
            return __result;
        }

        public int IsToggleProvider()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 46])(PPV);
            return __result;
        }

        public int ToggleProvider_GetToggleState()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 47])(PPV);
            return __result;
        }

        public void ToggleProvider_Toggle()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 48])(PPV);
        }

        public int IsValueProvider()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 49])(PPV);
            return __result;
        }

        public IAvnString ValueProvider_GetValue()
        {
            void* __result;
            __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 50])(PPV);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__result, true);
        }

        public void ValueProvider_SetValue(string value)
        {
            var __bytemarshal_value = new byte[System.Text.Encoding.UTF8.GetByteCount(value) + 1];
            System.Text.Encoding.UTF8.GetBytes(value, 0, value.Length, __bytemarshal_value, 0);
            fixed (byte* __fixedmarshal_value = __bytemarshal_value)
                ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 51])(PPV, __fixedmarshal_value);
        }

        public IAvnString HelpText
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 52])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__result, true);
            }
        }

        public IAvnString PlaceholderText
        {
            get
            {
                void* __result;
                __result = (void*)((delegate* unmanaged[Stdcall]<void*, void*>)(*PPV)[base.VTableSize + 53])(PPV);
                return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnString>(__result, true);
            }
        }

        public AvnLandmarkType LandmarkType
        {
            get
            {
                AvnLandmarkType __result;
                __result = (AvnLandmarkType)((delegate* unmanaged[Stdcall]<void*, AvnLandmarkType>)(*PPV)[base.VTableSize + 54])(PPV);
                return __result;
            }
        }

        public int HeadingLevel
        {
            get
            {
                int __result;
                __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 55])(PPV);
                return __result;
            }
        }

        public AvnLiveSetting LiveSetting
        {
            get
            {
                AvnLiveSetting __result;
                __result = (AvnLiveSetting)((delegate* unmanaged[Stdcall]<void*, AvnLiveSetting>)(*PPV)[base.VTableSize + 56])(PPV);
                return __result;
            }
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnAutomationPeer), new Guid("b87016f3-7eec-41de-b385-07844c268dc4"), (p, owns) => new __MicroComIAvnAutomationPeerProxy(p, owns));
        }

        protected __MicroComIAvnAutomationPeerProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 57;
    }

    unsafe class __MicroComIAvnAutomationPeerVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetNodeDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetNode(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Node;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SetNodeDelegate(void* @this, void* node);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void SetNode(void* @this, void* node)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetNode(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationNode>(node, false));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetAcceleratorKeyDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetAcceleratorKey(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.AcceleratorKey;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetAccessKeyDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetAccessKey(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.AccessKey;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate AvnAutomationControlType GetAutomationControlTypeDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static AvnAutomationControlType GetAutomationControlType(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.AutomationControlType;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetAutomationIdDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetAutomationId(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.AutomationId;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate AvnRect GetBoundingRectangleDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static AvnRect GetBoundingRectangle(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.BoundingRectangle;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetChildrenDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetChildren(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Children;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetClassNameDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetClassName(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ClassName;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetLabeledByDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetLabeledBy(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.LabeledBy;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetNameDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetName(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Name;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetParentDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetParent(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Parent;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetVisualRootDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetVisualRoot(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.VisualRoot;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int HasKeyboardFocusDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int HasKeyboardFocus(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.HasKeyboardFocus();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsContentElementDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsContentElement(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsContentElement();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsControlElementDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsControlElement(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsControlElement();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsEnabledDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsEnabled(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsEnabled();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsKeyboardFocusableDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsKeyboardFocusable(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsKeyboardFocusable();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SetFocusDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void SetFocus(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetFocus();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ShowContextMenuDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ShowContextMenu(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ShowContextMenu();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetRootPeerDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetRootPeer(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RootPeer;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsInteropPeerDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsInteropPeer(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsInteropPeer();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate IntPtr InteropPeer_GetNativeControlHandleDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static IntPtr InteropPeer_GetNativeControlHandle(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.InteropPeer_GetNativeControlHandle();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsRootProviderDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsRootProvider(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsRootProvider();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* RootProvider_GetWindowDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* RootProvider_GetWindow(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RootProvider_GetWindow();
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* RootProvider_GetFocusDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* RootProvider_GetFocus(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RootProvider_GetFocus();
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* RootProvider_GetPeerFromPointDelegate(void* @this, AvnPoint point);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* RootProvider_GetPeerFromPoint(void* @this, AvnPoint point)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RootProvider_GetPeerFromPoint(point);
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsEmbeddedRootProviderDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsEmbeddedRootProvider(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsEmbeddedRootProvider();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* EmbeddedRootProvider_GetFocusDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* EmbeddedRootProvider_GetFocus(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.EmbeddedRootProvider_GetFocus();
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* EmbeddedRootProvider_GetPeerFromPointDelegate(void* @this, AvnPoint point);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* EmbeddedRootProvider_GetPeerFromPoint(void* @this, AvnPoint point)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.EmbeddedRootProvider_GetPeerFromPoint(point);
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsExpandCollapseProviderDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsExpandCollapseProvider(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsExpandCollapseProvider();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ExpandCollapseProvider_GetIsExpandedDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ExpandCollapseProvider_GetIsExpanded(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ExpandCollapseProvider_GetIsExpanded();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ExpandCollapseProvider_GetShowsMenuDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ExpandCollapseProvider_GetShowsMenu(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ExpandCollapseProvider_GetShowsMenu();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ExpandCollapseProvider_ExpandDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ExpandCollapseProvider_Expand(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ExpandCollapseProvider_Expand();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ExpandCollapseProvider_CollapseDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ExpandCollapseProvider_Collapse(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ExpandCollapseProvider_Collapse();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsInvokeProviderDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsInvokeProvider(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsInvokeProvider();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void InvokeProvider_InvokeDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void InvokeProvider_Invoke(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.InvokeProvider_Invoke();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsRangeValueProviderDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsRangeValueProvider(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsRangeValueProvider();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate double RangeValueProvider_GetValueDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static double RangeValueProvider_GetValue(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RangeValueProvider_GetValue();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate double RangeValueProvider_GetMinimumDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static double RangeValueProvider_GetMinimum(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RangeValueProvider_GetMinimum();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate double RangeValueProvider_GetMaximumDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static double RangeValueProvider_GetMaximum(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RangeValueProvider_GetMaximum();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate double RangeValueProvider_GetSmallChangeDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static double RangeValueProvider_GetSmallChange(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RangeValueProvider_GetSmallChange();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate double RangeValueProvider_GetLargeChangeDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static double RangeValueProvider_GetLargeChange(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RangeValueProvider_GetLargeChange();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void RangeValueProvider_SetValueDelegate(void* @this, double value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void RangeValueProvider_SetValue(void* @this, double value)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.RangeValueProvider_SetValue(value);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsSelectionItemProviderDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsSelectionItemProvider(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsSelectionItemProvider();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int SelectionItemProvider_IsSelectedDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int SelectionItemProvider_IsSelected(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.SelectionItemProvider_IsSelected();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsToggleProviderDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsToggleProvider(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsToggleProvider();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int ToggleProvider_GetToggleStateDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int ToggleProvider_GetToggleState(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ToggleProvider_GetToggleState();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ToggleProvider_ToggleDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ToggleProvider_Toggle(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ToggleProvider_Toggle();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int IsValueProviderDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int IsValueProvider(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.IsValueProvider();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* ValueProvider_GetValueDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* ValueProvider_GetValue(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.ValueProvider_GetValue();
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ValueProvider_SetValueDelegate(void* @this, byte* value);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ValueProvider_SetValue(void* @this, byte* value)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ValueProvider_SetValue((value == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(value))));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetHelpTextDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetHelpText(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.HelpText;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void* GetPlaceholderTextDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void* GetPlaceholderText(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.PlaceholderText;
                        return global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate AvnLandmarkType GetLandmarkTypeDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static AvnLandmarkType GetLandmarkType(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.LandmarkType;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetHeadingLevelDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int GetHeadingLevel(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.HeadingLevel;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate AvnLiveSetting GetLiveSettingDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static AvnLiveSetting GetLiveSetting(void* @this)
        {
            IAvnAutomationPeer __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.LiveSetting;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComIAvnAutomationPeerVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetNode); 
#else
            base.AddMethod((GetNodeDelegate)GetNode); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void>)&SetNode); 
#else
            base.AddMethod((SetNodeDelegate)SetNode); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetAcceleratorKey); 
#else
            base.AddMethod((GetAcceleratorKeyDelegate)GetAcceleratorKey); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetAccessKey); 
#else
            base.AddMethod((GetAccessKeyDelegate)GetAccessKey); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnAutomationControlType>)&GetAutomationControlType); 
#else
            base.AddMethod((GetAutomationControlTypeDelegate)GetAutomationControlType); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetAutomationId); 
#else
            base.AddMethod((GetAutomationIdDelegate)GetAutomationId); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnRect>)&GetBoundingRectangle); 
#else
            base.AddMethod((GetBoundingRectangleDelegate)GetBoundingRectangle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetChildren); 
#else
            base.AddMethod((GetChildrenDelegate)GetChildren); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetClassName); 
#else
            base.AddMethod((GetClassNameDelegate)GetClassName); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetLabeledBy); 
#else
            base.AddMethod((GetLabeledByDelegate)GetLabeledBy); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetName); 
#else
            base.AddMethod((GetNameDelegate)GetName); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetParent); 
#else
            base.AddMethod((GetParentDelegate)GetParent); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetVisualRoot); 
#else
            base.AddMethod((GetVisualRootDelegate)GetVisualRoot); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&HasKeyboardFocus); 
#else
            base.AddMethod((HasKeyboardFocusDelegate)HasKeyboardFocus); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsContentElement); 
#else
            base.AddMethod((IsContentElementDelegate)IsContentElement); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsControlElement); 
#else
            base.AddMethod((IsControlElementDelegate)IsControlElement); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsEnabled); 
#else
            base.AddMethod((IsEnabledDelegate)IsEnabled); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsKeyboardFocusable); 
#else
            base.AddMethod((IsKeyboardFocusableDelegate)IsKeyboardFocusable); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&SetFocus); 
#else
            base.AddMethod((SetFocusDelegate)SetFocus); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&ShowContextMenu); 
#else
            base.AddMethod((ShowContextMenuDelegate)ShowContextMenu); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetRootPeer); 
#else
            base.AddMethod((GetRootPeerDelegate)GetRootPeer); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsInteropPeer); 
#else
            base.AddMethod((IsInteropPeerDelegate)IsInteropPeer); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, IntPtr>)&InteropPeer_GetNativeControlHandle); 
#else
            base.AddMethod((InteropPeer_GetNativeControlHandleDelegate)InteropPeer_GetNativeControlHandle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsRootProvider); 
#else
            base.AddMethod((IsRootProviderDelegate)IsRootProvider); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&RootProvider_GetWindow); 
#else
            base.AddMethod((RootProvider_GetWindowDelegate)RootProvider_GetWindow); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&RootProvider_GetFocus); 
#else
            base.AddMethod((RootProvider_GetFocusDelegate)RootProvider_GetFocus); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnPoint, void*>)&RootProvider_GetPeerFromPoint); 
#else
            base.AddMethod((RootProvider_GetPeerFromPointDelegate)RootProvider_GetPeerFromPoint); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsEmbeddedRootProvider); 
#else
            base.AddMethod((IsEmbeddedRootProviderDelegate)IsEmbeddedRootProvider); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&EmbeddedRootProvider_GetFocus); 
#else
            base.AddMethod((EmbeddedRootProvider_GetFocusDelegate)EmbeddedRootProvider_GetFocus); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnPoint, void*>)&EmbeddedRootProvider_GetPeerFromPoint); 
#else
            base.AddMethod((EmbeddedRootProvider_GetPeerFromPointDelegate)EmbeddedRootProvider_GetPeerFromPoint); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsExpandCollapseProvider); 
#else
            base.AddMethod((IsExpandCollapseProviderDelegate)IsExpandCollapseProvider); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&ExpandCollapseProvider_GetIsExpanded); 
#else
            base.AddMethod((ExpandCollapseProvider_GetIsExpandedDelegate)ExpandCollapseProvider_GetIsExpanded); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&ExpandCollapseProvider_GetShowsMenu); 
#else
            base.AddMethod((ExpandCollapseProvider_GetShowsMenuDelegate)ExpandCollapseProvider_GetShowsMenu); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&ExpandCollapseProvider_Expand); 
#else
            base.AddMethod((ExpandCollapseProvider_ExpandDelegate)ExpandCollapseProvider_Expand); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&ExpandCollapseProvider_Collapse); 
#else
            base.AddMethod((ExpandCollapseProvider_CollapseDelegate)ExpandCollapseProvider_Collapse); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsInvokeProvider); 
#else
            base.AddMethod((IsInvokeProviderDelegate)IsInvokeProvider); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&InvokeProvider_Invoke); 
#else
            base.AddMethod((InvokeProvider_InvokeDelegate)InvokeProvider_Invoke); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsRangeValueProvider); 
#else
            base.AddMethod((IsRangeValueProviderDelegate)IsRangeValueProvider); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double>)&RangeValueProvider_GetValue); 
#else
            base.AddMethod((RangeValueProvider_GetValueDelegate)RangeValueProvider_GetValue); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double>)&RangeValueProvider_GetMinimum); 
#else
            base.AddMethod((RangeValueProvider_GetMinimumDelegate)RangeValueProvider_GetMinimum); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double>)&RangeValueProvider_GetMaximum); 
#else
            base.AddMethod((RangeValueProvider_GetMaximumDelegate)RangeValueProvider_GetMaximum); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double>)&RangeValueProvider_GetSmallChange); 
#else
            base.AddMethod((RangeValueProvider_GetSmallChangeDelegate)RangeValueProvider_GetSmallChange); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double>)&RangeValueProvider_GetLargeChange); 
#else
            base.AddMethod((RangeValueProvider_GetLargeChangeDelegate)RangeValueProvider_GetLargeChange); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, double, void>)&RangeValueProvider_SetValue); 
#else
            base.AddMethod((RangeValueProvider_SetValueDelegate)RangeValueProvider_SetValue); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsSelectionItemProvider); 
#else
            base.AddMethod((IsSelectionItemProviderDelegate)IsSelectionItemProvider); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&SelectionItemProvider_IsSelected); 
#else
            base.AddMethod((SelectionItemProvider_IsSelectedDelegate)SelectionItemProvider_IsSelected); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsToggleProvider); 
#else
            base.AddMethod((IsToggleProviderDelegate)IsToggleProvider); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&ToggleProvider_GetToggleState); 
#else
            base.AddMethod((ToggleProvider_GetToggleStateDelegate)ToggleProvider_GetToggleState); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&ToggleProvider_Toggle); 
#else
            base.AddMethod((ToggleProvider_ToggleDelegate)ToggleProvider_Toggle); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&IsValueProvider); 
#else
            base.AddMethod((IsValueProviderDelegate)IsValueProvider); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&ValueProvider_GetValue); 
#else
            base.AddMethod((ValueProvider_GetValueDelegate)ValueProvider_GetValue); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, byte*, void>)&ValueProvider_SetValue); 
#else
            base.AddMethod((ValueProvider_SetValueDelegate)ValueProvider_SetValue); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetHelpText); 
#else
            base.AddMethod((GetHelpTextDelegate)GetHelpText); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*>)&GetPlaceholderText); 
#else
            base.AddMethod((GetPlaceholderTextDelegate)GetPlaceholderText); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnLandmarkType>)&GetLandmarkType); 
#else
            base.AddMethod((GetLandmarkTypeDelegate)GetLandmarkType); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&GetHeadingLevel); 
#else
            base.AddMethod((GetHeadingLevelDelegate)GetHeadingLevel); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnLiveSetting>)&GetLiveSetting); 
#else
            base.AddMethod((GetLiveSettingDelegate)GetLiveSetting); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnAutomationPeer), new __MicroComIAvnAutomationPeerVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnAutomationPeerArrayProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnAutomationPeerArray
    {
        public uint Count
        {
            get
            {
                uint __result;
                __result = (uint)((delegate* unmanaged[Stdcall]<void*, uint>)(*PPV)[base.VTableSize + 0])(PPV);
                return __result;
            }
        }

        public IAvnAutomationPeer Get(uint index)
        {
            int __result;
            void* __marshal_ppv = null;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, uint, void*, int>)(*PPV)[base.VTableSize + 1])(PPV, index, &__marshal_ppv);
            if (__result != 0)
                throw new System.Runtime.InteropServices.COMException("Get failed", __result);
            return global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnAutomationPeer>(__marshal_ppv, true);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnAutomationPeerArray), new Guid("b00af5da-78af-4b33-bfff-4ce13a6239a9"), (p, owns) => new __MicroComIAvnAutomationPeerArrayProxy(p, owns));
        }

        protected __MicroComIAvnAutomationPeerArrayProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 2;
    }

    unsafe class __MicroComIAvnAutomationPeerArrayVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate uint GetCountDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static uint GetCount(void* @this)
        {
            IAvnAutomationPeerArray __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeerArray)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Count;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int GetDelegate(void* @this, uint index, void** ppv);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int Get(void* @this, uint index, void** ppv)
        {
            IAvnAutomationPeerArray __target = null;
            try
            {
                {
                    __target = (IAvnAutomationPeerArray)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.Get(index);
                        *ppv = global::MicroCom.Runtime.MicroComRuntime.GetNativePointer(__result, true);
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException __com_exception__)
            {
                return __com_exception__.ErrorCode;
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return unchecked((int)0x80004005u);
            }

            return 0;
        }

        protected __MicroComIAvnAutomationPeerArrayVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint>)&GetCount); 
#else
            base.AddMethod((GetCountDelegate)GetCount); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)&Get); 
#else
            base.AddMethod((GetDelegate)Get); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnAutomationPeerArray), new __MicroComIAvnAutomationPeerArrayVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnAutomationNodeProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnAutomationNode
    {
        public void Dispose()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 0])(PPV);
        }

        public void ChildrenChanged()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 1])(PPV);
        }

        public void PropertyChanged(AvnAutomationProperty property)
        {
            ((delegate* unmanaged[Stdcall]<void*, AvnAutomationProperty, void>)(*PPV)[base.VTableSize + 2])(PPV, property);
        }

        public void FocusChanged()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 3])(PPV);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnAutomationNode), new Guid("004dc40b-e435-49dc-bac5-6272ee35382a"), (p, owns) => new __MicroComIAvnAutomationNodeProxy(p, owns));
        }

        protected __MicroComIAvnAutomationNodeProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 4;
    }

    unsafe class __MicroComIAvnAutomationNodeVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void DisposeDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Dispose(void* @this)
        {
            IAvnAutomationNode __target = null;
            try
            {
                {
                    __target = (IAvnAutomationNode)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Dispose();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void ChildrenChangedDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void ChildrenChanged(void* @this)
        {
            IAvnAutomationNode __target = null;
            try
            {
                {
                    __target = (IAvnAutomationNode)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.ChildrenChanged();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void PropertyChangedDelegate(void* @this, AvnAutomationProperty property);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void PropertyChanged(void* @this, AvnAutomationProperty property)
        {
            IAvnAutomationNode __target = null;
            try
            {
                {
                    __target = (IAvnAutomationNode)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.PropertyChanged(property);
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void FocusChangedDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void FocusChanged(void* @this)
        {
            IAvnAutomationNode __target = null;
            try
            {
                {
                    __target = (IAvnAutomationNode)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.FocusChanged();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnAutomationNodeVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Dispose); 
#else
            base.AddMethod((DisposeDelegate)Dispose); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&ChildrenChanged); 
#else
            base.AddMethod((ChildrenChangedDelegate)ChildrenChanged); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnAutomationProperty, void>)&PropertyChanged); 
#else
            base.AddMethod((PropertyChangedDelegate)PropertyChanged); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&FocusChanged); 
#else
            base.AddMethod((FocusChangedDelegate)FocusChanged); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnAutomationNode), new __MicroComIAvnAutomationNodeVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnPlatformSettingsProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnPlatformSettings
    {
        public AvnPlatformThemeVariant PlatformTheme
        {
            get
            {
                AvnPlatformThemeVariant __result;
                __result = (AvnPlatformThemeVariant)((delegate* unmanaged[Stdcall]<void*, AvnPlatformThemeVariant>)(*PPV)[base.VTableSize + 0])(PPV);
                return __result;
            }
        }

        public uint AccentColor
        {
            get
            {
                uint __result;
                __result = (uint)((delegate* unmanaged[Stdcall]<void*, uint>)(*PPV)[base.VTableSize + 1])(PPV);
                return __result;
            }
        }

        public void RegisterColorsChange(IAvnActionCallback callback)
        {
            using var __callback = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(callback);
            ((delegate* unmanaged[Stdcall]<void*, void*, void>)(*PPV)[base.VTableSize + 2])(PPV, __callback.Pointer);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnPlatformSettings), new Guid("d1f009cc-9d2d-493b-845d-90d2c104baae"), (p, owns) => new __MicroComIAvnPlatformSettingsProxy(p, owns));
        }

        protected __MicroComIAvnPlatformSettingsProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 3;
    }

    unsafe class __MicroComIAvnPlatformSettingsVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate AvnPlatformThemeVariant GetPlatformThemeDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static AvnPlatformThemeVariant GetPlatformTheme(void* @this)
        {
            IAvnPlatformSettings __target = null;
            try
            {
                {
                    __target = (IAvnPlatformSettings)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.PlatformTheme;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate uint GetAccentColorDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static uint GetAccentColor(void* @this)
        {
            IAvnPlatformSettings __target = null;
            try
            {
                {
                    __target = (IAvnPlatformSettings)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.AccentColor;
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void RegisterColorsChangeDelegate(void* @this, void* callback);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void RegisterColorsChange(void* @this, void* callback)
        {
            IAvnPlatformSettings __target = null;
            try
            {
                {
                    __target = (IAvnPlatformSettings)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.RegisterColorsChange(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnActionCallback>(callback, false));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnPlatformSettingsVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, AvnPlatformThemeVariant>)&GetPlatformTheme); 
#else
            base.AddMethod((GetPlatformThemeDelegate)GetPlatformTheme); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, uint>)&GetAccentColor); 
#else
            base.AddMethod((GetAccentColorDelegate)GetAccentColor); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, void>)&RegisterColorsChange); 
#else
            base.AddMethod((RegisterColorsChangeDelegate)RegisterColorsChange); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnPlatformSettings), new __MicroComIAvnPlatformSettingsVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnPlatformBehaviorInhibitionProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnPlatformBehaviorInhibition
    {
        public void SetInhibitAppSleep(int inhibitAppSleep, string reason)
        {
            var __bytemarshal_reason = new byte[System.Text.Encoding.UTF8.GetByteCount(reason) + 1];
            System.Text.Encoding.UTF8.GetBytes(reason, 0, reason.Length, __bytemarshal_reason, 0);
            fixed (byte* __fixedmarshal_reason = __bytemarshal_reason)
                ((delegate* unmanaged[Stdcall]<void*, int, void*, void>)(*PPV)[base.VTableSize + 0])(PPV, inhibitAppSleep, __fixedmarshal_reason);
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnPlatformBehaviorInhibition), new Guid("12edf00d-5803-4d3f-9947-b4840e5e9372"), (p, owns) => new __MicroComIAvnPlatformBehaviorInhibitionProxy(p, owns));
        }

        protected __MicroComIAvnPlatformBehaviorInhibitionProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 1;
    }

    unsafe class __MicroComIAvnPlatformBehaviorInhibitionVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void SetInhibitAppSleepDelegate(void* @this, int inhibitAppSleep, byte* reason);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void SetInhibitAppSleep(void* @this, int inhibitAppSleep, byte* reason)
        {
            IAvnPlatformBehaviorInhibition __target = null;
            try
            {
                {
                    __target = (IAvnPlatformBehaviorInhibition)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.SetInhibitAppSleep(inhibitAppSleep, (reason == null ? null : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(new IntPtr(reason))));
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        protected __MicroComIAvnPlatformBehaviorInhibitionVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int, byte*, void>)&SetInhibitAppSleep); 
#else
            base.AddMethod((SetInhibitAppSleepDelegate)SetInhibitAppSleep); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnPlatformBehaviorInhibition), new __MicroComIAvnPlatformBehaviorInhibitionVTable().CreateVTable());
    }

    internal unsafe partial class __MicroComIAvnPlatformRenderTimerProxy : global::MicroCom.Runtime.MicroComProxyBase, IAvnPlatformRenderTimer
    {
        public int RegisterTick(IAvnActionCallback callback)
        {
            int __result;
            using var __callback = global::MicroCom.Runtime.MicroComRuntime.LeaseNativePointerForCall(callback);
            __result = (int)((delegate* unmanaged[Stdcall]<void*, void*, int>)(*PPV)[base.VTableSize + 0])(PPV, __callback.Pointer);
            return __result;
        }

        public void Start()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 1])(PPV);
        }

        public void Stop()
        {
            ((delegate* unmanaged[Stdcall]<void*, void>)(*PPV)[base.VTableSize + 2])(PPV);
        }

        public int RunsInBackground()
        {
            int __result;
            __result = (int)((delegate* unmanaged[Stdcall]<void*, int>)(*PPV)[base.VTableSize + 3])(PPV);
            return __result;
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit()
        {
            global::MicroCom.Runtime.MicroComRuntime.Register(typeof(IAvnPlatformRenderTimer), new Guid("22edf20d-5803-2d3f-9247-b4842e5e9322"), (p, owns) => new __MicroComIAvnPlatformRenderTimerProxy(p, owns));
        }

        protected __MicroComIAvnPlatformRenderTimerProxy(IntPtr nativePointer, bool ownsHandle) : base(nativePointer, ownsHandle)
        {
        }

        protected override int VTableSize => base.VTableSize + 4;
    }

    unsafe class __MicroComIAvnPlatformRenderTimerVTable : global::MicroCom.Runtime.MicroComVtblBase
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int RegisterTickDelegate(void* @this, void* callback);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int RegisterTick(void* @this, void* callback)
        {
            IAvnPlatformRenderTimer __target = null;
            try
            {
                {
                    __target = (IAvnPlatformRenderTimer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RegisterTick(global::MicroCom.Runtime.MicroComRuntime.CreateProxyOrNullFor<IAvnActionCallback>(callback, false));
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void StartDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Start(void* @this)
        {
            IAvnPlatformRenderTimer __target = null;
            try
            {
                {
                    __target = (IAvnPlatformRenderTimer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Start();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate void StopDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static void Stop(void* @this)
        {
            IAvnPlatformRenderTimer __target = null;
            try
            {
                {
                    __target = (IAvnPlatformRenderTimer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    __target.Stop();
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                ;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        delegate int RunsInBackgroundDelegate(void* @this);
#if NET5_0_OR_GREATER
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })] 
#endif
        static int RunsInBackground(void* @this)
        {
            IAvnPlatformRenderTimer __target = null;
            try
            {
                {
                    __target = (IAvnPlatformRenderTimer)global::MicroCom.Runtime.MicroComRuntime.GetObjectFromCcw(new IntPtr(@this));
                    {
                        var __result = __target.RunsInBackground();
                        return __result;
                    }
                }
            }
            catch (System.Exception __exception__)
            {
                global::MicroCom.Runtime.MicroComRuntime.UnhandledException(__target, __exception__);
                return default;
            }
        }

        protected __MicroComIAvnPlatformRenderTimerVTable()
        {
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void*, int>)&RegisterTick); 
#else
            base.AddMethod((RegisterTickDelegate)RegisterTick); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Start); 
#else
            base.AddMethod((StartDelegate)Start); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, void>)&Stop); 
#else
            base.AddMethod((StopDelegate)Stop); 
#endif
#if NET5_0_OR_GREATER
            base.AddMethod((delegate* unmanaged[Stdcall]<void*, int>)&RunsInBackground); 
#else
            base.AddMethod((RunsInBackgroundDelegate)RunsInBackground); 
#endif
        }

        [System.Runtime.CompilerServices.ModuleInitializer()]
        internal static void __MicroComModuleInit() => global::MicroCom.Runtime.MicroComRuntime.RegisterVTable(typeof(IAvnPlatformRenderTimer), new __MicroComIAvnPlatformRenderTimerVTable().CreateVTable());
    }
}