using System;
using System.Collections.Generic;
using Godot;
using OpenSWR.Data;

/// <summary>
/// Darkened background: drawn behind its parent, it shows the screen under <see cref="Rects"/>
/// remapped c: c / 6 + 50 per channel and snapped to the nearest colour of the game palette (bitmap 900's), through a lookup
/// texture (<see cref="Darkening.Lut"/>). The screen copy is Godot's first one of the frame, so a stack of darkened windows each
/// shows the main-screen picture, as the original does. Used by the sector window, the legend window and
/// the popup menus (game menu, filter picker).
/// </summary>
public partial class Darkened : Control
{
    static ShaderMaterial? _material;

    /// <summary>Rects in the parent's coordinates; null = the parent's whole rect.</summary>
    public Func<IEnumerable<Rect2>>? Rects { get; set; }

    public Darkened()
    {
        ShowBehindParent = true;
        MouseFilter = MouseFilterEnum.Ignore;
        Material = _material ??= Build();
    }

    static ShaderMaterial Build()
    {
        var palette = Assets.Indexed(900, "STRATEGY.DLL")?.Palette ?? new byte[768];
        int n = Darkening.Levels;
        var lut = ImageTexture.CreateFromImage(Image.CreateFromData(n * n, n, false, Image.Format.Rgb8, Darkening.Lut(palette)));
        var shader = new Shader
        {
            Code = "shader_type canvas_item; uniform sampler2D screen : hint_screen_texture, filter_nearest; uniform sampler2D lut : filter_nearest;"
                + " void fragment() { ivec3 c = ivec3(round(texture(screen, SCREEN_UV).rgb * 255.0)) / 6;"
                + $" COLOR = vec4(texelFetch(lut, ivec2(c.r + {n} * c.g, c.b), 0).rgb, 1.0); }}",
        };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("lut", lut);
        return material;
    }

    public override void _Draw()
    {
        if (Rects is null) { DrawRect(new Rect2(Vector2.Zero, GetParent<Control>().Size), Colors.White); return; }
        foreach (var r in Rects()) DrawRect(r, Colors.White);
    }
}
