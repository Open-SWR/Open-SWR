using System;
using System.Collections.Generic;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// Options / saved-games screen (COMMON 20002): six save slots (openswr saves user://saves/slotN.json, name + side in
/// user://saves/slots.cfg), music / effects sliders, Play Music, tactical display toggles (stored only), Restart / Return / Exit.
/// </summary>
public partial class OptionsScreen : Control
{
    const string SlotsPath = "user://saves/slots.cfg";
    static readonly Color On = new(0, 1, 0), Off = new(0, 128 / 255f, 0);

    /// <summary>A game is running (in-game options); false = the load screen from the main menu.</summary>
    public bool InGame { get; set; }
    /// <summary>Opened from a tactical battle (state 9): Return 10026-10028, Save buttons and name fields disabled.</summary>
    public bool FromTactical { get; set; }
    /// <summary>Running game's side: picks the Return button images.</summary>
    public SimOwner Side { get; set; } = SimOwner.Alliance;
    /// <summary>Saves the running game to the path; returns the game's side or null on failure.</summary>
    public Func<string, SimOwner?>? Save { get; set; }
    public Action<string>? Load { get; set; }
    public Action? Restart { get; set; }
    public Action? Return { get; set; }
    public Action? Exit { get; set; }
    public Action? MusicChanged { get; set; }

    public Dictionary<int, FrontButton> Controls { get; } = [];

    readonly ConfigFile _slots = new();
    Error _slotsLoad; // a cfg that exists but cannot be read must not be rewritten (it would drop the other slot names)
    readonly LineEdit[] _names = new LineEdit[6];
    bool _savedThisVisit;

    static string SlotFile(int i) => $"user://saves/slot{i}.json";

    public override void _Ready()
    {
        Size = new Vector2(640, 480);
        _slotsLoad = _slots.Load(SlotsPath);
        for (int i = 1; i <= 6; i++)
        {
            int slot = i, y = 81 + 42 * (i - 1);
            Add(0x6E + i, new FrontButton { Up = Assets.Bitmap(10046), Down = Assets.Bitmap(10047), DisabledImage = Assets.Bitmap(10048), Disabled = !InGame || FromTactical },
                34, y, 42, 20, 4112, () => SaveSlot(slot));
            var name = new LineEdit
            {
                Position = new Vector2(119, y), Editable = InGame && !FromTactical, MaxLength = 40,
                PlaceholderText = Assets.Comm(4124), Text = SlotName(slot),
            };
            var box = new StyleBoxFlat { BgColor = Colors.Black, ContentMarginLeft = 2 };
            name.AddThemeStyleboxOverride("normal", box);
            name.AddThemeStyleboxOverride("focus", box);
            name.AddThemeStyleboxOverride("read_only", box);
            name.AddThemeColorOverride("font_color", Colors.White);
            name.AddThemeColorOverride("font_uneditable_color", Colors.White);
            name.AddThemeFontOverride("font", Assets.Font(10));
            name.AddThemeFontSizeOverride("font_size", Assets.FontSize(10));
            AddChild(_names[i - 1] = name);
            name.Size = new Vector2(160, 20); // after the font overrides: set earlier, the default theme's taller minimum wins
            Add(0x78 + i, new FrontButton { Up = Assets.Bitmap(10049), Down = Assets.Bitmap(10050), DisabledImage = Assets.Bitmap(10051), Disabled = !Occupied(slot) },
                287, y, 41, 20, 4113, () => LoadSlot(slot));
        }

        AddSlider(0x8D, 392, 130, 10052, 4114, FrontSettings.MusicVolume, v => FrontSettings.MusicVolume = FrontSettings.ToAttenuation(v));
        AddSlider(0x8E, 392, 190, 10053, 4115, FrontSettings.Volume, v => FrontSettings.Volume = FrontSettings.ToAttenuation(v));
        Add(0x99, new FrontButton { Up = Assets.Bitmap(10040), CheckedImage = Assets.Bitmap(10041), DisabledImage = Assets.Bitmap(10042), Toggle = true, Checked = FrontSettings.MusicOn },
            352, 76, 19, 35, null, () =>
            {
                FrontSettings.MusicOn = Controls[0x99].Checked;
                FrontSettings.Save();
                MusicChanged?.Invoke();
                QueueRedraw();
            });
        foreach (var (id, y, _, _, option) in Tactical)
            Add(id, new FrontButton
            {
                Up = Assets.Bitmap(10043), CheckedImage = Assets.Bitmap(10044), DisabledImage = Assets.Bitmap(10045), Toggle = true,
                Checked = FrontSettings.Display(option),
            }, 357, y, 35, 22, null, () =>
            {
                FrontSettings.SetDisplay(option, !FrontSettings.Display(option));
                FrontSettings.Save();
                QueueRedraw();
            });

        Add(0x91, new FrontButton { Up = Assets.Bitmap(10035), Down = Assets.Bitmap(10036) }, 76, 381, 42, 42, 4116, RestartGame);
        ushort r = FromTactical ? (ushort)10026 : Side == SimOwner.Empire ? (ushort)10023 : (ushort)10020; // Return button images by context
        Add(0x90, new FrontButton { Up = Assets.Bitmap(r), Down = Assets.Bitmap((ushort)(r + 1)), DisabledImage = Assets.Bitmap((ushort)(r + 2)), Disabled = !InGame },
            162, 382, 42, 42, 4117, () => { FrontSettings.Save(); Return?.Invoke(); });
        Add(0x8F, new FrontButton { Up = Assets.Bitmap(10038), Down = Assets.Bitmap(10039) }, 248, 381, 42, 42, 4120, () => Exit?.Invoke());
    }

    // (id, toggle y, label y, string, option)
    static readonly (int Id, int Y, int LabelY, int Text, DisplayOption Option)[] Tactical =
    [
        (0xA1, 311, 313, 4127, DisplayOption.Starfield), (0xA2, 337, 340, 4128, DisplayOption.Planet), (0xA3, 365, 367, 4129, DisplayOption.Pyrotechnics),
        (0xA4, 392, 394, 4130, DisplayOption.HighDetailModels), (0xA6, 419, 421, 4132, DisplayOption.Holocube),
    ];

    FrontButton Add(int id, FrontButton b, int x, int y, int w, int h, int? tooltip, Action command)
    {
        b.Place(x, y, w, h);
        if (tooltip is { } t) b.TooltipText = Assets.Tip(Assets.Comm(t));
        b.Command = command;
        Controls[id] = b;
        AddChild(b);
        return b;
    }

    void AddSlider(int id, int x, int y, ushort track, int tooltip, int attenuation, Action<int> set)
    {
        AddChild(new FrontSlider
        {
            Position = new Vector2(x, y), Size = new Vector2(201, 54), ThumbSize = new Vector2(11, 47),
            Track = Assets.Bitmap(track), Thumb = Assets.Bitmap(10054), TooltipText = Assets.Tip(Assets.Comm(tooltip)),
            Value = FrontSettings.ToSlider(attenuation), Name = $"Slider{id:X}",
            Changed = v =>
            {
                set(v);
                FrontSettings.Apply(); // changes apply immediately
                FrontSettings.Save();
            },
        });
    }

    bool Occupied(int slot) => FileAccess.FileExists(SlotFile(slot)); // the file decides; a missing cfg entry only means no name
    string SlotName(int slot) => Occupied(slot) ? (string)_slots.GetValue($"slot{slot}", "name", "") : "";

    void SaveSlot(int slot)
    {
        // Inferred reading of the test: an occupied slot whose name was not changed asks first (REBDLOG 4638 / 4639)
        // a slot file without its cfg entry (the cfg was lost) always asks: there is no name to compare
        if (Occupied(slot) && (!_slots.HasSection($"slot{slot}") || _names[slot - 1].Text == SlotName(slot)))
            Assets.Confirm(this, Assets.Dialog(4638) + "\n" + Assets.Dialog(4639), () => DoSave(slot));
        else DoSave(slot);
    }

    void DoSave(int slot)
    {
        if (Save?.Invoke(SlotFile(slot)) is not { } side)
        {
            if (MainScreen.LastSaveError is { } why) Assets.Confirm(this, "Save failed: " + why, () => { });
            return;
        }
        string name = _names[slot - 1].Text.Trim();
        _slots.SetValue($"slot{slot}", "name", name);
        _slots.SetValue($"slot{slot}", "side", (int)side);
        if (_slotsLoad is Error.Ok or Error.FileNotFound) _slots.Save(SlotsPath);
        _names[slot - 1].Text = name;
        Controls[0x78 + slot].Disabled = false;
        _savedThisVisit = true;
        QueueRedraw();
    }

    void LoadSlot(int slot)
    {
        if (InGame && !_savedThisVisit) Assets.Confirm(this, Assets.Dialog(4636) + "\n" + Assets.Dialog(4637), () => Load?.Invoke(SlotFile(slot)));
        else Load?.Invoke(SlotFile(slot));
    }

    // Restart confirmation text Inferred: REBDLOG 4634 / 4635 ("Returning to the shuttle cockpit ...").
    void RestartGame()
    {
        FrontSettings.Save();
        if (InGame && !_savedThisVisit) Assets.Confirm(this, Assets.Dialog(4634) + "\n" + Assets.Dialog(4635), () => Restart?.Invoke());
        else Restart?.Invoke();
    }

    public override void _Draw()
    {
        var back = Assets.Bitmap(20002);
        DrawTextureRect(back, new Rect2(0, 0, 640, 480), false);
        DrawLog.Bitmap(this, back, new Rect2(0, 0, 640, 480));
        var green = new Color(0, 1, 0);
        Assets.DrawText(this, new Rect2(58, 37, 241, 21), Assets.Comm(4136), 7, green, TextFormat.CentredRow);
        Assets.DrawText(this, new Rect2(362, 37, 241, 21), Assets.Comm(4134), 7, green, TextFormat.CentredRow);
        Assets.DrawText(this, new Rect2(363, 273, 239, 21), Assets.Comm(4135), 7, green, TextFormat.CentredRow);
        Assets.DrawText(this, new Rect2(25, 437, 310, 18), Assets.Comm(4108) + GameFiles.AppName, 10, Colors.Black, TextFormat.CentredRow);
        for (int i = 1; i <= 6; i++)
        {
            if (!Occupied(i) || !_slots.HasSection($"slot{i}")) continue; // no cfg entry: the side is unknown
            var side = (SimOwner)(int)_slots.GetValue($"slot{i}", "side", 1);
            var mark = Assets.Bitmap(side == SimOwner.Empire ? (ushort)10055 : (ushort)10056);
            DrawTextureRect(mark, new Rect2(85, 81 + 42 * (i - 1) + 2, 26, 19), false);
            DrawLog.Bitmap(this, mark, new Rect2(85, 81 + 42 * (i - 1) + 2, 26, 19));
        }
        var music = FrontSettings.MusicOn ? On : Off;
        Assets.DrawText(this, new Rect2(376, 79, 183, 33), Assets.Comm(4133), 7, music, TextFormat.Row);
        Assets.DrawText(this, new Rect2(581, 79, 31, 33), Assets.Comm(FrontSettings.MusicOn ? 4145 : 4146), 7, music, TextFormat.Row);
        foreach (var (_, _, ly, text, option) in Tactical)
        {
            bool on = FrontSettings.Display(option);
            Assets.DrawText(this, new Rect2(394, ly, 183, 16), Assets.Comm(text), 7, on ? On : Off, TextFormat.Wrap);
            Assets.DrawText(this, new Rect2(577, ly, 31, 16), Assets.Comm(on ? 4145 : 4146), 7, on ? On : Off, TextFormat.Wrap);
        }
    }
}
