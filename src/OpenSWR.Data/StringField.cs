using System.Runtime.InteropServices;

namespace OpenSWR.Data;

/// <summary>
/// Edit field model (`CoolStringField`), plain .NET so the rules
/// are testable; the front end draws it. Text, caret index (-1 = "at the end", distinct from the length), selection begin / end
/// (-1 = none; there is no anchor), horizontal scroll s (<see cref="TextLayout.FieldScroll"/>), overwrite mode,
/// mouse selecting, length limit L (0 = none). Widths are prefixes in the field's font
/// (<see cref="TextLayout.TextWidth"/>). The front end calls the WM_CHAR / WM_KEYDOWN entries only while the field is focused.
/// </summary>
public sealed class StringField(int font, int width, int limit = 0)
{
    public enum Keys { Home, End, Left, Right, Backspace, Delete, Insert }

    public string Text { get; private set; } = "";
    public int Caret { get; private set; } = -1;
    public int SelectionBegin { get; private set; } = -1;
    public int SelectionEnd { get; private set; } = -1;
    public int Scroll { get; private set; }
    public bool Overwrite { get; private set; }
    public bool Selecting { get; private set; }
    public int Font => font;
    public int Width => width;

    /// <summary>A selection is drawn (while focused) when begin != end (also the degenerate -2 / -1 and -1 / length of a caret -1).</summary>
    public bool HasSelection => SelectionBegin != SelectionEnd;

    /// <summary>
    /// The painted selection in ascending order: a negative begin counts as 0, a negative end as the length (Inferred for
    /// the degenerate begin -1, whose x the game puts at the bitmap's right edge).
    /// </summary>
    public (int From, int To) Selection
    {
        get
        {
            int b = SelectionBegin < 0 ? 0 : Math.Min(SelectionBegin, Text.Length), e = SelectionEnd < 0 ? Text.Length : Math.Min(SelectionEnd, Text.Length);
            return (Math.Min(b, e), Math.Max(b, e));
        }
    }

    /// <summary>The caret as an index: -1 (and -2, Inferred: the game defines no width for it) = the end.</summary>
    int At => Caret < 0 ? Text.Length : Caret;

    /// <summary>Width of the first <paramref name="n"/> characters.</summary>
    public int X(int n) => TextLayout.TextWidth(font, Text[..Math.Clamp(n, 0, Text.Length)]);

    /// <summary>The caret's x in the control (X - s; caret -1: the whole text's width); its y is 0.</summary>
    public int CaretX => X(At) - Scroll;

    void Rescroll() => Scroll = TextLayout.FieldScroll(Scroll, X(At), width);

    void ClearSelection() => SelectionBegin = SelectionEnd = -1;

    /// <summary>
    /// Text set: with a limit L, a text of >= L characters keeps its first L - 1; caret -1, then the scroll rule. s and the
    /// selection are kept (a text narrower than s gets s := its width); no change notification.
    /// </summary>
    public void SetText(string text)
    {
        Text = limit > 0 && text.Length >= limit ? text[..(limit - 1)] : text;
        Caret = -1;
        Rescroll();
    }

    /// <summary>Clear: text empty, caret -1, s := 0, scroll rule; the selection is kept; no change notification.</summary>
    public void Clear()
    {
        Text = "";
        Caret = -1;
        Scroll = 0;
        Rescroll();
    }

    /// <summary>Entry fix-up of WM_KEYDOWN / WM_CHAR: begin >= 0 and end -1 gives end := length.</summary>
    void FixUp()
    {
        if (SelectionBegin >= 0 && SelectionEnd == -1) SelectionEnd = Text.Length;
    }

    /// <summary>String delete: nothing unless 0 <= from &lt; to <= length.</summary>
    void Delete(int from, int to)
    {
        if (from >= 0 && from < to && to <= Text.Length) Text = Text.Remove(from, to - from);
    }

    /// <summary>Delete selection (nothing when begin = end), then no selection.</summary>
    void DeleteSelection()
    {
        int b = SelectionBegin, e = SelectionEnd;
        if (b == e) return;
        if (b == -1 && e >= 0) { Delete(e, Text.Length); Caret = e; }
        else if (e == -1 && b >= 0) { Delete(b, Text.Length); Caret = -1; }
        else { Delete(Math.Min(b, e), Math.Max(b, e)); Caret = Math.Min(b, e); }
        ClearSelection();
    }

    // The original runs setlocale(LC_ALL, "") at start-up: isprint / isspace / ispunct follow the user's
    // Windows code page, not the C locale. Inferred equivalence (code page 1252 for every character such a system can type):
    // the Unicode categories below.

    /// <summary>C isprint (the CRT's printable class) in the user locale: not a control or format character (Unicode Cc / Cf).</summary>
    static bool Printable(char c) => !char.IsControl(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format;

    /// <summary>
    /// A key's character is typed into the field: printable, and Ctrl and Alt both up or both held (AltGr,
    /// Inferred); exactly one of them held gives not typed. Accelerator keys never get here (the screen takes them first).
    /// </summary>
    public static bool Typed(char c, bool ctrl, bool alt) => ctrl == alt && Printable(c);

    /// <summary>Double-click separators, C isspace union ispunct in the user locale: whitespace, punctuation or symbols.</summary>
    static bool Separator(char c) => char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c);

    /// <summary>
    /// WM_CHAR (focused): non-printable gives ignored; a selection is deleted, else in overwrite mode with 0 &lt; caret &lt; length the
    /// character at the caret; caret -1 gives length; the character is inserted (caret + 1) only when L = 0 or caret &lt; L. True = change notification
    /// posted (every printable character, also one the limit refused). Selecting ends only on a printable character (a
    /// non-printable one returns before, so a drag goes on).
    /// </summary>
    public bool Type(char c)
    {
        FixUp();
        if (!Printable(c)) return false;
        Selecting = false;
        if (HasSelection) DeleteSelection();
        else if (Overwrite && Caret > 0 && Caret < Text.Length) Text = Text.Remove(Caret, 1);
        if (Caret < 0) Caret = Text.Length;
        if (limit == 0 || Caret < limit) Text = Text.Insert(Caret++, c.ToString());
        Rescroll();
        return true;
    }

    /// <summary>
    /// WM_KEYDOWN (key table, after the entry fix-up). True = change notification posted: Backspace after a selection delete, at caret &gt; 0
    /// or at caret -1 (also on an empty text), not at caret 0; Delete after a selection delete or at caret >= 0 (also at caret = length,
    /// nothing deleted), not at caret -1. Insert toggles overwrite only (no scroll rule). Moves without Shift clear the selection;
    /// Shift uses the anchor-free rules of <see cref="ShiftLeft"/> / <see cref="ShiftRight"/>; Shift+Home / Shift+End replace it.
    /// </summary>
    public bool Key(Keys key, bool shift)
    {
        FixUp();
        bool post = false;
        int c = Caret;
        switch (key)
        {
            case Keys.Insert:
                Overwrite = !Overwrite;
                return false;
            case Keys.Backspace:
                if (HasSelection) { DeleteSelection(); post = true; }
                else if (c > 0) { Text = Text.Remove(c - 1, 1); Caret = c - 1; post = true; }
                else if (c < 0) { if (Text.Length > 0) Text = Text[..^1]; post = true; }
                break;
            case Keys.Delete:
                if (HasSelection) { DeleteSelection(); post = true; }
                else if (c >= 0) { if (c < Text.Length) Text = Text.Remove(c, 1); post = true; }
                break;
            case Keys.Home:
                if (!shift) ClearSelection();
                else if (c > 0) (SelectionBegin, SelectionEnd) = (0, c);
                Caret = 0;
                break;
            case Keys.End:
                if (!shift) ClearSelection();
                else if (c < Text.Length) (SelectionBegin, SelectionEnd) = (c, Text.Length);
                Caret = Text.Length;
                break;
            case Keys.Left:
                Caret = c >= 1 ? c - 1 : c < 0 ? Text.Length - 1 : 0;
                if (shift) ShiftLeft(Caret); else ClearSelection();
                break;
            case Keys.Right:
                Caret = c >= 0 && c < Text.Length ? c + 1 : c;
                if (shift) ShiftRight(Caret); else ClearSelection();
                break;
        }
        Rescroll();
        return post;
    }

    /// <summary>Shift+Left with the new caret c: c = b - 1 gives b := c; else c = e - 1 gives e := c (none when e = b); else no selection gives (c, c + 1).</summary>
    void ShiftLeft(int c)
    {
        int b = SelectionBegin, e = SelectionEnd;
        if (c == b - 1) SelectionBegin = c;
        else if (c == e - 1) { SelectionEnd = c; if (c == b) ClearSelection(); }
        else if (b == e) (SelectionBegin, SelectionEnd) = (c, c + 1);
    }

    /// <summary>Shift+Right with the new caret c: c = e + 1 gives e := c; else c = b + 1 gives b := c (none when b = e); else no selection gives (c - 1, c).</summary>
    void ShiftRight(int c)
    {
        int b = SelectionBegin, e = SelectionEnd;
        if (c == e + 1) SelectionEnd = c;
        else if (c == b + 1) { SelectionBegin = c; if (c == e) ClearSelection(); }
        else if (b == e) (SelectionBegin, SelectionEnd) = (c - 1, c);
    }

    /// <summary>Hit test (<see cref="TextLayout.FieldHit"/>) of a control x.</summary>
    public int Hit(float x) => TextLayout.FieldHit(font, Text, x + Scroll);

    /// <summary>Left button down: caret, begin and end := the hit index (a shown selection is gone); selecting on; scroll rule.</summary>
    public void MouseDown(float x)
    {
        Caret = SelectionBegin = SelectionEnd = Hit(x);
        Selecting = true;
        Rescroll();
    }

    /// <summary>
    /// Mouse move, only while selecting: with the button, end and caret := the hit index (begin stays the press point); without it,
    /// selecting off and the selection cleared (a button-up that never reached the field).
    /// </summary>
    public void MouseMove(float x, bool button)
    {
        if (!Selecting) return;
        if (button)
        {
            Caret = SelectionEnd = Hit(x);
            Rescroll();
            return;
        }
        Selecting = false;
        ClearSelection();
    }

    /// <summary>Left button up, or focus lost (WM_KILLFOCUS): selecting off; the selection is kept.</summary>
    public void MouseUp() => Selecting = false;

    /// <summary>Focus gained (WM_SETFOCUS): the scroll rule positions the caret.</summary>
    public void Focus() => Rescroll();

    /// <summary>
    /// Double click (after the button down has set the caret): begin := the start of the last word run starting at or before the
    /// caret (0 if none), end := the first separator after the caret + 1 (the separator is included), the length when there is
    /// none; caret := end. Separators = isspace union ispunct. The two tests are asymmetric: a word start counts only at an
    /// index <= the caret, a separator ends the walk only at an index &gt; the caret (with the caret directly before a separator,
    /// the walk goes on to the next one). Selecting is left as it is.
    /// </summary>
    public void DoubleClick()
    {
        int c = At, begin = 0, end = Text.Length;
        for (int i = 0; i < Text.Length; i++)
        {
            if (!Separator(Text[i]))
            {
                if (i <= c && (i == 0 || Separator(Text[i - 1]))) begin = i;
            }
            else if (i > c)
            {
                end = i + 1;
                break;
            }
        }
        (SelectionBegin, SelectionEnd, Caret) = (begin, end, end);
        Rescroll();
    }

    /// <summary>
    /// The system caret's blink half-period in ms (the game never sets it): GetCaretBlinkTime on Windows (0 = never blinks for
    /// INFINITE); shortcut: 530 ms (the Windows default) elsewhere, upgrade if a platform exposes its caret blink rate.
    /// </summary>
    public static int BlinkTime()
    {
        if (!OperatingSystem.IsWindows()) return 530;
        try
        {
            uint t = GetCaretBlinkTime();
            return t is 0 ? 530 : t == uint.MaxValue ? 0 : (int)t;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return 530; }
    }

    [DllImport("user32.dll")] static extern uint GetCaretBlinkTime();
}
