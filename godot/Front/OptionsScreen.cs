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
            Add(ControlIds.OptionsSaveSlotBase + i, new FrontButton { Up = CommonArt.SaveUp, Down = CommonArt.SaveDown, DisabledImage = CommonArt.SaveDisabled, Disabled = !InGame || FromTactical },
                34, y, 42, 20, CommonTexts.SaveGame, () => SaveSlot(slot));
            var name = new LineEdit
            {
                Position = new Vector2(119, y), Editable = InGame && !FromTactical, MaxLength = 40,
                PlaceholderText = CommonText.EmptySlot, Text = SlotName(slot),
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
            Add(ControlIds.OptionsLoadSlotBase + i, new FrontButton { Up = CommonArt.LoadUp, Down = CommonArt.LoadDown, DisabledImage = CommonArt.LoadDisabled, Disabled = !Occupied(slot) },
                287, y, 41, 20, CommonTexts.LoadGame, () => LoadSlot(slot));
        }

        AddSlider(ControlIds.OptionsMusicVolume, 392, 130, CommonBitmaps.MusicSliderTrack, CommonTexts.MusicVolume, FrontSettings.MusicVolume, v => FrontSettings.MusicVolume = FrontSettings.ToAttenuation(v));
        AddSlider(ControlIds.OptionsSoundVolume, 392, 190, CommonBitmaps.SoundSliderTrack, CommonTexts.SoundEffectsVolume, FrontSettings.Volume, v => FrontSettings.Volume = FrontSettings.ToAttenuation(v));
        Add(ControlIds.OptionsPlayMusic, new FrontButton { Up = CommonArt.MusicToggleUp, CheckedImage = CommonArt.MusicToggleChecked, DisabledImage = CommonArt.MusicToggleDisabled, Toggle = true, Checked = FrontSettings.MusicOn },
            352, 76, 19, 35, null, () =>
            {
                FrontSettings.MusicOn = Controls[ControlIds.OptionsPlayMusic].Checked;
                FrontSettings.Save();
                MusicChanged?.Invoke();
                QueueRedraw();
            });
        foreach (var (id, y, _, _, option) in Tactical)
            Add(id, new FrontButton
            {
                Up = CommonArt.DisplayToggleOff, CheckedImage = CommonArt.DisplayToggleOn, DisabledImage = CommonArt.DisplayToggleDisabled, Toggle = true,
                Checked = FrontSettings.Display(option),
            }, 357, y, 35, 22, null, () =>
            {
                FrontSettings.SetDisplay(option, !FrontSettings.Display(option));
                FrontSettings.Save();
                QueueRedraw();
            });

        Add(ControlIds.OptionsRestart, new FrontButton { Up = CommonArt.RestartUp, Down = CommonArt.RestartDown }, 76, 381, 42, 42, CommonTexts.RestartGame, RestartGame);
        ushort r = FromTactical ? (ushort)CommonBitmaps.ReturnTacticalUp : Side == SimOwner.Empire ? (ushort)CommonBitmaps.ReturnEmpireUp : (ushort)CommonBitmaps.ReturnAllianceUp; // Return button images by context
        Add(ControlIds.OptionsReturn, new FrontButton { Up = Assets.Bitmap(r), Down = Assets.Bitmap((ushort)(r + 1)), DisabledImage = Assets.Bitmap((ushort)(r + 2)), Disabled = !InGame },
            162, 382, 42, 42, CommonTexts.ReturnToCommandCenter, () => { FrontSettings.Save(); Return?.Invoke(); });
        Add(ControlIds.OptionsExit, new FrontButton { Up = CommonArt.ExitUp, Down = CommonArt.ExitDown }, 248, 381, 42, 42, CommonTexts.OptionsExit, () => Exit?.Invoke());
    }

    // (id, toggle y, label y, string, option)
    static readonly (int Id, int Y, int LabelY, int Text, DisplayOption Option)[] Tactical =
    [
        (ControlIds.OptionsShowStarfield, 311, 313, CommonTexts.ShowStarfield, DisplayOption.Starfield), (ControlIds.OptionsShowPlanet, 337, 340, CommonTexts.ShowPlanet, DisplayOption.Planet), (ControlIds.OptionsShowPyrotechnics, 365, 367, CommonTexts.ShowPyrotechnics, DisplayOption.Pyrotechnics),
        (ControlIds.OptionsHighDetailModels, 392, 394, CommonTexts.HighDetailModels, DisplayOption.HighDetailModels), (ControlIds.OptionsDisplayHolocube, 419, 421, CommonTexts.DisplayHolocube, DisplayOption.Holocube),
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
            Track = Assets.Bitmap(track), Thumb = CommonArt.SliderThumb, TooltipText = Assets.Tip(Assets.Comm(tooltip)),
            Value = FrontSettings.ToSlider(attenuation), Name = $"Slider{id}",
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
            Assets.Confirm(this, DialogText.SaveOverwriteWarning + "\n" + DialogText.SaveOverwriteQuestion, () => DoSave(slot));
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
        Controls[ControlIds.OptionsLoadSlotBase + slot].Disabled = false;
        _savedThisVisit = true;
        QueueRedraw();
    }

    void LoadSlot(int slot)
    {
        if (InGame && !_savedThisVisit) Assets.Confirm(this, DialogText.LoadWarning + "\n" + DialogText.LoadQuestion, () => Load?.Invoke(SlotFile(slot)));
        else Load?.Invoke(SlotFile(slot));
    }

    // Restart confirmation text Inferred: REBDLOG 4634 / 4635 ("Returning to the shuttle cockpit ...").
    void RestartGame()
    {
        FrontSettings.Save();
        if (InGame && !_savedThisVisit) Assets.Confirm(this, DialogText.RestartWarning + "\n" + DialogText.RestartQuestion, () => Restart?.Invoke());
        else Restart?.Invoke();
    }

    public override void _Draw()
    {
        var back = CommonArt.OptionsBackground;
        DrawTextureRect(back, new Rect2(0, 0, 640, 480), false);
        DrawLog.Bitmap(this, back, new Rect2(0, 0, 640, 480));
        var green = new Color(0, 1, 0);
        Assets.DrawText(this, new Rect2(58, 37, 241, 21), CommonText.SavedGames, 7, green, TextFormat.CentredRow);
        Assets.DrawText(this, new Rect2(362, 37, 241, 21), CommonText.SoundOptions, 7, green, TextFormat.CentredRow);
        Assets.DrawText(this, new Rect2(363, 273, 239, 21), CommonText.TacticalDisplayOptions, 7, green, TextFormat.CentredRow);
        Assets.DrawText(this, new Rect2(25, 437, 310, 18), CommonText.Version + GameFiles.AppName, 10, Colors.Black, TextFormat.CentredRow);
        for (int i = 1; i <= 6; i++)
        {
            if (!Occupied(i) || !_slots.HasSection($"slot{i}")) continue; // no cfg entry: the side is unknown
            var side = (SimOwner)(int)_slots.GetValue($"slot{i}", "side", 1);
            var mark = Assets.Bitmap(side == SimOwner.Empire ? (ushort)CommonBitmaps.SaveSideEmpire : (ushort)CommonBitmaps.SaveSideAlliance);
            DrawTextureRect(mark, new Rect2(85, 81 + 42 * (i - 1) + 2, 26, 19), false);
            DrawLog.Bitmap(this, mark, new Rect2(85, 81 + 42 * (i - 1) + 2, 26, 19));
        }
        var music = FrontSettings.MusicOn ? On : Off;
        Assets.DrawText(this, new Rect2(376, 79, 183, 33), CommonText.PlayMusic, 7, music, TextFormat.Row);
        Assets.DrawText(this, new Rect2(581, 79, 31, 33), Assets.Comm(FrontSettings.MusicOn ? CommonTexts.On : CommonTexts.Off), 7, music, TextFormat.Row);
        foreach (var (_, _, ly, text, option) in Tactical)
        {
            bool on = FrontSettings.Display(option);
            Assets.DrawText(this, new Rect2(394, ly, 183, 16), Assets.Comm(text), 7, on ? On : Off, TextFormat.Wrap);
            Assets.DrawText(this, new Rect2(577, ly, 31, 16), Assets.Comm(on ? CommonTexts.On : CommonTexts.Off), 7, on ? On : Off, TextFormat.Wrap);
        }
    }
}
