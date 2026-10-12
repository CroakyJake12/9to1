// Wire validation for the Home-owned account theme envelope. No credential or
// renderer objects are admitted; custom designs are bounded declarative values.
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const exact = (value, keys) => object(value) && Object.keys(value).every(key => keys.includes(key));
const range = (value, min, max) => typeof value === 'number' && Number.isFinite(value) && value >= min && value <= max;
const text = (value, max) => typeof value === 'string' && value.trim().length > 0 && value.length <= max && !/[\u0000-\u001f\u007f-\u009f]/.test(value);
const hex = value => typeof value === 'string' && /^#[0-9A-F]{6}$/.test(value);
const fontName = value => value == null || text(value, 128) && !value.includes(',');
const kinds = value => Number.isInteger(value) && value >= 0 && value <= 2;
const colour = value => exact(value, ['hex','transparencyPercent','backgroundBlur']) && hex(value.hex) && range(value.transparencyPercent,0,100) && range(value.backgroundBlur,0,32);
const fontWeights = Object.freeze({ Montserrat:[400,500,600,700,800,900], Inter:[400,500,600,700], 'DejaVu Sans':[400] });
const font = value => exact(value,['family','weight','size']) && Object.hasOwn(fontWeights,value.family) && fontWeights[value.family].includes(value.weight) && range(value.size,8,72);
function fill(value) {
    if (!exact(value,['kind','colour','gradient','pattern']) || !kinds(value.kind) || !colour(value.colour)) return false;
    const gradient = value.gradient, pattern = value.pattern;
    if (gradient != null && (!exact(gradient,['kind','stops','angle','centerX','centerY','radius']) || !kinds(gradient.kind) ||
        !range(gradient.angle,-360,360) || !range(gradient.centerX,0,1) || !range(gradient.centerY,0,1) || !range(gradient.radius,.01,2) ||
        !Array.isArray(gradient.stops) || gradient.stops.length < 2 || gradient.stops.length > 16 ||
        gradient.stops.some((stop,i) => !exact(stop,['offset','colour']) || !range(stop.offset,0,1) || !colour(stop.colour) || i > 0 && stop.offset < gradient.stops[i-1].offset) ||
        gradient.stops[0].offset !== 0 || gradient.stops.at(-1).offset !== 1)) return false;
    if (pattern != null && (!exact(pattern,['kind','foreground','background','size','spacing','angle']) || !kinds(pattern.kind) ||
        !colour(pattern.foreground) || !colour(pattern.background) || !range(pattern.size,1,128) || !range(pattern.spacing,0,128) || !range(pattern.angle,-360,360))) return false;
    return (value.kind !== 1 || gradient != null) && (value.kind !== 2 || pattern != null);
}
const builtIns = ['glow','bubble','retro','playful','cinematic','professional','frosted glass','metallic','western'];
export function validCustomThemeLibrary(json) {
    if (typeof json !== 'string' || !json || json.length > 1_048_576) return false;
    let library;
    try { library = JSON.parse(json); } catch { return false; }
    if (!Array.isArray(library) || library.length > 32) return false;
    const ids = new Set(), names = new Set();
    for (const theme of library) {
        if (!exact(theme,['schemaVersion','id','name','baseTheme','primaryAccent','secondaryAccent','tertiaryAccent','fonts','buttonRoundnessPercent']) ||
            theme.schemaVersion !== 1 || typeof theme.id !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(theme.id) ||
            !text(theme.name,64) || theme.name.trim() !== theme.name || builtIns.includes(theme.name.toLowerCase()) ||
            !Number.isInteger(theme.baseTheme) || theme.baseTheme < 0 || theme.baseTheme > 8 ||
            !fill(theme.primaryAccent) || !fill(theme.secondaryAccent) || !fill(theme.tertiaryAccent) ||
            !range(theme.buttonRoundnessPercent,0,100) || !exact(theme.fonts,['main','primaryButton','secondaryButton','tertiaryButton','heading','body','code']) ||
            !['main','primaryButton','secondaryButton','tertiaryButton','heading','body','code'].every(role => font(theme.fonts[role])) ||
            JSON.stringify(theme).length > 32768 || ids.has(theme.id) || names.has(theme.name.toLowerCase())) return false;
        ids.add(theme.id); names.add(theme.name.toLowerCase());
    }
    return true;
}
export function validThemePreference(value, accountId) {
    if (!object(value) || value.schemaVersion !== 1 || !Number.isSafeInteger(value.revision) || value.revision < 0 ||
        !object(value.scope) || value.scope.scope !== 1 || value.scope.ownerId !== accountId || value.scope.organizationId != null ||
        !text(value.themeName,128) || !object(value.accessibility)) return false;
    const a = value.accessibility;
    if (typeof a.reduceMotion !== 'boolean' || typeof a.highContrast !== 'boolean' || !range(a.displayScale,.5,5) ||
        !fontName(a.interfaceFontFamily) || !fontName(a.codeFontFamily) ||
        value.appearance != null && !['SuperBright','Bright','Dark','SuperDark'].includes(value.appearance)) return false;
    if (value.accent != null) {
        const accent = value.accent;
        if (!object(accent) || !kinds(accent.mode) || accent.allAppsColour != null && !hex(accent.allAppsColour) || !object(accent.individualApps) ||
            Object.keys(accent.individualApps).length > 32 || Object.entries(accent.individualApps).some(([id,colour]) =>
                !/^app\.[a-z][a-z0-9.-]*$/.test(id) || id.length > 64 || id.endsWith('.') || id.includes('..') || !hex(colour))) return false;
    }
    return value.customThemesJson == null || validCustomThemeLibrary(value.customThemesJson);
}
