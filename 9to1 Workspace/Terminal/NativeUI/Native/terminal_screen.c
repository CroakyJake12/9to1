#include <vterm.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

/* Stable first-party ABI: no libvterm bitfield/union layout crosses into CLR. */
typedef struct {
    uint32_t chars[6];
    uint32_t foreground, background, attributes;
    int32_t width;
} HavenCell;
typedef struct { int count; HavenCell *cells; } HavenLine;
typedef struct {
    VTerm *terminal;
    VTermScreen *screen;
    unsigned char output[65536];
    size_t output_length;
    int output_overflow, rows, columns, cursor_visible;
    HavenLine history[256];
    int history_start, history_count;
} HavenScreen;

static void convert_cell(HavenScreen *host, const VTermScreenCell *source, HavenCell *target)
{
    VTermColor fg = source->fg, bg = source->bg;
    vterm_screen_convert_color_to_rgb(host->screen, &fg);
    vterm_screen_convert_color_to_rgb(host->screen, &bg);
    memset(target, 0, sizeof(*target));
    /* libvterm terminates the cell at the first zero; trailing slots may be
       uninitialised and must never cross the native boundary. */
    for (int i = 0; i < 6 && source->chars[i]; i++) target->chars[i] = source->chars[i];
    target->foreground = 0xff000000u | ((uint32_t)fg.rgb.red << 16) | ((uint32_t)fg.rgb.green << 8) | fg.rgb.blue;
    target->background = 0xff000000u | ((uint32_t)bg.rgb.red << 16) | ((uint32_t)bg.rgb.green << 8) | bg.rgb.blue;
    target->width = source->chars[0] == UINT32_MAX ? 0 : source->width;
    target->attributes = (source->attrs.bold ? 1u : 0u) | (source->attrs.italic ? 2u : 0u)
        | (source->attrs.underline ? 4u : 0u) | (source->attrs.reverse ? 8u : 0u)
        | (source->attrs.conceal ? 16u : 0u) | (source->attrs.strike ? 32u : 0u);
}
static void output(const char *bytes, size_t length, void *user)
{
    HavenScreen *host = user;
    if (length > sizeof(host->output) - host->output_length) { host->output_overflow = 1; return; }
    memcpy(host->output + host->output_length, bytes, length);
    host->output_length += length;
}
static int cursor(VTermPos position, VTermPos old, int visible, void *user)
{
    (void)position; (void)old; ((HavenScreen *)user)->cursor_visible = visible; return 1;
}
static int clear_history(void *user)
{
    HavenScreen *host = user;
    for (int i = 0; i < 256; i++) { free(host->history[i].cells); host->history[i].cells = NULL; host->history[i].count = 0; }
    host->history_start = host->history_count = 0;
    return 1;
}
static int push_line(int columns, const VTermScreenCell *cells, void *user)
{
    HavenScreen *host = user;
    if (columns < 1 || columns > 512) return 0;
    HavenCell *copy = calloc((size_t)columns, sizeof(HavenCell));
    if (!copy) return 0;
    for (int i = 0; i < columns; i++) convert_cell(host, cells + i, copy + i);
    int slot = (host->history_start + host->history_count) % 256;
    if (host->history_count == 256) { free(host->history[slot].cells); host->history_start = (host->history_start + 1) % 256; }
    else host->history_count++;
    host->history[slot].cells = copy; host->history[slot].count = columns;
    return 1;
}
static const VTermScreenCallbacks callbacks = { .movecursor = cursor, .sb_pushline = push_line, .sb_clear = clear_history };

void *haven_screen_new(int rows, int columns)
{
    if (rows < 1 || rows > 256 || columns < 1 || columns > 512) return NULL;
    HavenScreen *host = calloc(1, sizeof(*host));
    if (!host) return NULL;
    host->terminal = vterm_new(rows, columns);
    if (!host->terminal) { free(host); return NULL; }
    host->rows = rows; host->columns = columns; host->cursor_visible = 1;
    vterm_set_utf8(host->terminal, 1);
    vterm_output_set_callback(host->terminal, output, host);
    host->screen = vterm_obtain_screen(host->terminal);
    vterm_screen_set_callbacks(host->screen, &callbacks, host);
    vterm_screen_enable_altscreen(host->screen, 1);
    vterm_screen_reset(host->screen, 1);
    return host;
}
void haven_screen_free(HavenScreen *host)
{
    if (!host) return;
    clear_history(host); vterm_free(host->terminal); memset(host, 0, sizeof(*host)); free(host);
}
int haven_screen_feed(HavenScreen *host, const char *bytes, size_t length)
{
    if (!host || (!bytes && length) || length > 1048576) return 0;
    /* Feed scalar byte steps so a UTF-8 prefix following ASCII and its later
       continuation use the same donor UTF-8 decoder even across PTY chunks.
       The pinned donor maintains separate GL and high-bit decoder state. */
    for (size_t i = 0; i < length; i++)
        if (vterm_input_write(host->terminal, bytes + i, 1) != 1) return 0;
    vterm_screen_flush_damage(host->screen);
    return !host->output_overflow;
}
int haven_screen_resize(HavenScreen *host, int rows, int columns)
{
    if (!host || rows < 1 || rows > 256 || columns < 1 || columns > 512) return 0;
    vterm_set_size(host->terminal, rows, columns); host->rows = rows; host->columns = columns;
    vterm_screen_flush_damage(host->screen); return 1;
}
int haven_screen_cell(HavenScreen *host, int row, int column, HavenCell *cell)
{
    if (!host || !cell || row < 0 || row >= host->rows || column < 0 || column >= host->columns) return 0;
    VTermScreenCell source = {0};
    if (!vterm_screen_get_cell(host->screen, (VTermPos){row, column}, &source)) return 0;
    convert_cell(host, &source, cell); return 1;
}
int haven_screen_cursor(HavenScreen *host, int *row, int *column)
{
    VTermPos position; vterm_state_get_cursorpos(vterm_obtain_state(host->terminal), &position);
    *row = position.row; *column = position.col; return host->cursor_visible;
}
int haven_screen_history_count(HavenScreen *host) { return host->history_count; }
int haven_screen_history_cell(HavenScreen *host, int line, int column, HavenCell *cell)
{
    if (line < 0 || line >= host->history_count || column < 0 || !cell) return 0;
    HavenLine *saved = &host->history[(host->history_start + line) % 256];
    if (column >= saved->count) return 0;
    *cell = saved->cells[column]; return 1;
}
int haven_screen_key(HavenScreen *host, int key, int modifiers)
{
    vterm_keyboard_key(host->terminal, (VTermKey)key, (VTermModifier)modifiers); return !host->output_overflow;
}
int haven_screen_character(HavenScreen *host, uint32_t character, int modifiers)
{
    if (character > 0x10ffff || (character >= 0xd800 && character <= 0xdfff)) return 0;
    vterm_keyboard_unichar(host->terminal, character, (VTermModifier)modifiers); return !host->output_overflow;
}
int haven_screen_drain(HavenScreen *host, unsigned char *bytes, size_t capacity)
{
    if (host->output_overflow || capacity < host->output_length || (!bytes && host->output_length)) return -1;
    size_t length = host->output_length;
    memcpy(bytes, host->output, length); memset(host->output, 0, length); host->output_length = 0;
    return (int)length;
}
