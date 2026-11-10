using OpenSWR.Data;

namespace OpenSWR.Tests;

public class MessageTextTests
{
    [GameFact]
    public void AllMessagesDecode()
    {
        var entries = PeResources.Load(GameDir.File("TEXTSTRA.DLL")).OfType(ResId.RcData).ToList();
        var messages = entries.Where(e => MessageText.IsMessage(e.Data.Span)).ToList();
        Assert.Equal(458, messages.Count);
        Assert.All(messages, m => Assert.All(MessageText.Decode(m.Data.Span).OfType<MessagePlaceholder>(), p => Assert.InRange(p.Slot, 1, 4)));

        var killed = MessageText.Decode(entries.Single(e => e.Name.Id == 29541).Data.Span);
        Assert.Equal([new MessagePlaceholder(1, 1), new MessageLiteral(" has been killed.")], killed);
    }

    // Each side's advice topic list: topics numbered from 1, each with a kind and a sort key; some are kind 7 (the burst at the briefing's end).
    [GameFact]
    public void AdviceTopicsDecode()
    {
        var textstra = PeResources.Load(GameDir.File("TEXTSTRA.DLL"));
        foreach (bool alliance in new[] { true, false })
        {
            var topics = AdviceTopic.Read(textstra, alliance);
            Assert.NotEmpty(topics);
            Assert.Equal(Enumerable.Range(1, topics.Count), topics.Select(t => t.N));
            Assert.All(topics, t => Assert.InRange(t.Kind, 0, 15));
            Assert.Contains(topics, t => t.Kind == 7);
        }
    }

    [GameFact]
    public void AcceleratorsDecode()
    {
        var table = Accelerator.Decode(PeResources.Load(GameDir.File("TEXTCOMM.DLL")).OfType(new ResId(9)).Single().Data.Span);
        Assert.NotEmpty(table);
        Assert.Equal(new Accelerator(19, 'W', 3001), table[0]); // FVIRTKEY, FNOINVERT and FALT: Alt+W
    }

    // TEXTCOMM accelerator table 11 is matched before the focused name box, with exact Shift / Ctrl / Alt.
    // Ctrl+X (the cut command) goes to the screen and leaves the field's text and selection alone. AltGr (Ctrl+Alt) matches no Alt entry,
    // so AltGr+P and AltGr+E are typed. A character with exactly one of Ctrl / Alt is not typed.
    [GameFact]
    public void AcceleratorsBeforeTheField()
    {
        var table = Accelerator.Decode(PeResources.Load(GameDir.File("TEXTCOMM.DLL")).Find(new ResId(9), 11)!.Data.Span);
        ushort? Cmd(ushort vk, char c, bool shift, bool ctrl, bool alt) => Accelerator.Find(table, vk, c, shift, ctrl, alt)?.Command;
        Assert.Equal((ushort)3029, Cmd('X', '\x18', false, true, false));
        Assert.Equal((ushort)3030, Cmd('S', '\x13', false, true, false));
        Assert.Equal((ushort)3027, Cmd(9, '\t', false, true, false));
        Assert.Equal((ushort)3028, Cmd(9, '\t', true, true, false));
        Assert.Equal((ushort)3004, Cmd('P', 'p', false, false, true));
        Assert.Equal((ushort)3002, Cmd(187, '=', false, false, true)); // character entry Alt+"="
        Assert.Equal((ushort)3002, Cmd(107, '+', false, false, true)); // Alt+Numpad+
        Assert.Null(Cmd('X', 'X', true, true, false)); // Ctrl+Shift+X
        Assert.Null(Cmd('X', '\0', false, true, true)); // Ctrl+Alt+X
        Assert.Null(Cmd('P', 'p', false, true, true)); // AltGr+P
        Assert.Null(Cmd('E', '€', false, true, true)); // AltGr+E (German layout)
        Assert.Null(Cmd('X', 'x', false, false, false));

        // the routing of a key with the field focused: accelerator first, else a typed character
        var field = new StringField(TextLayout.SystemFont, 250);
        field.SetText("Coruscant");
        field.Key(StringField.Keys.Left, shift: true);
        var before = (field.Text, field.SelectionBegin, field.SelectionEnd, field.Caret);
        void Press(ushort vk, char c, bool ctrl, bool alt)
        {
            if (Accelerator.Find(table, vk, c, false, ctrl, alt) is null && StringField.Typed(c, ctrl, alt)) field.Type(c);
        }
        Press('X', '\x18', true, false);
        Assert.Equal(before, (field.Text, field.SelectionBegin, field.SelectionEnd, field.Caret));
        Press('X', 'x', true, false); // Ctrl+letter as a character: not typed either
        Press('X', 'x', false, true);
        Assert.Equal(before, (field.Text, field.SelectionBegin, field.SelectionEnd, field.Caret));
        Press('E', '€', true, true);
        Assert.Equal("Coruscan€", field.Text); // the selection ("t") replaced
    }
}
