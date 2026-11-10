using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenSWR.Data;
using OpenSWR.Data.Dat;
using OpenSWR.Data.Tactical;
using OpenSWR.Sim;

/// <summary>
/// Tactical-battle models. A capital ship has three LOD meshes, TACTICAL.DLL type 301 ids id / id + 1 / id + 2, built as ArrayMeshes
/// with their named type-303 textures. A mod can supply a glTF model instead (GameDefinitions.Models). Fighter images
/// are here too. World and .x files are left-handed (D3D RM), so
/// z is negated for vertices, normals and the world positions alike (<see cref="ToGodot"/>): a .x nose at model -z becomes +Z.
/// Every ship node returned has its nose at +Z, 1 model unit = 1 world unit.
/// </summary>
public static class TacticalModels
{
    const string Module = "TACTICAL.DLL";
    static readonly Dictionary<int, ArrayMesh?> Meshes = [];
    static readonly Dictionary<string, Texture2D?> Textures = new(StringComparer.OrdinalIgnoreCase);
    static Dictionary<string, PeResource>? _named;
    static Color[]? _palette;

    /// <summary>
    /// Model id of a capital-ship class: the type index i from the class's SD StringId (BattleEngine.TypeIndex), which is not the record
    /// order. i <= 14: 2010 + 10*i, 15 <= i <= 28: 2510 + 10*(i - 15). The Death Star has no type-301 model: it is a decal of the 303
    /// image 5010 / 5020 (<see cref="TacticalScreen"/>).
    /// </summary>
    static int? ModelId(OpenSWR.Data.Dat.GameDefinitions defs, uint type)
    {
        int i = BattleEngine.TypeIndex(defs, type);
        return i <= TacticalTypeIndex.LastFirstBlockShip ? TacticalModelIds.CapitalShipBase + 10 * i
            : i <= TacticalTypeIndex.LastSecondBlockShip ? TacticalModelIds.CapitalShipSecondBase + 10 * (i - TacticalTypeIndex.FirstSecondBlockShip) : null;
    }

    /// <summary>
    /// Level of detail of a capital ship, every frame: depth = |(position - camera) * view direction|, f = the
    /// camera field. "Use High Detail Models" off: depth <= 20/f: _MEDIUM, else _FAR; on: depth &lt; 15/f: _CLOSE, &gt; 40/f: _FAR,
    /// between: _MEDIUM; exactly on a boundary the current level stays. A node with one mesh (glTF, placeholder) has no levels.
    /// </summary>
    public static void Lod(Node3D ship, float depth, float f, bool high)
    {
        if (ship.GetChildCount() != 3 || ship.GetChild(0).Name != "0") return;
        int current = Enumerable.Range(0, 3).FirstOrDefault(i => ship.GetChild<Node3D>(i).Visible, 1);
        int level = high
            ? depth < 15 / f ? 0 : depth > 40 / f ? 2 : depth > 15 / f && depth < 40 / f ? 1 : current
            : depth < 20 / f ? 1 : depth > 20 / f ? 2 : current;
        for (int i = 0; i < 3; i++) ship.GetChild<Node3D>(i).Visible = i == level;
    }

    /// <summary>
    /// Fighter image base, by the local side (by type - 29): local Alliance 4000, 4000, 4020, 4030, 4104, 4114,
    /// 4124, 4134; local Empire 4004, 4014, 4024, 4034, 4100, 4110, 4120, 4130, by the type index. A class outside 29 ... 36 has no image.
    /// </summary>
    public static int? FighterImage(OpenSWR.Data.Dat.GameDefinitions defs, uint type, int local)
    {
        int[][] table =
        [
            [TacticalBitmaps.XWingAllianceView, TacticalBitmaps.YWingAllianceView, TacticalBitmaps.AWingAllianceView, TacticalBitmaps.BWingAllianceView, TacticalBitmaps.EmpireFighterAllianceView, TacticalBitmaps.EmpireInterceptorAllianceView, TacticalBitmaps.EmpireBomberAllianceView, TacticalBitmaps.EmpireDefenderAllianceView],
            [TacticalBitmaps.XWingEmpireView, TacticalBitmaps.YWingEmpireView, TacticalBitmaps.AWingEmpireView, TacticalBitmaps.BWingEmpireView, TacticalBitmaps.EmpireFighterEmpireView, TacticalBitmaps.EmpireInterceptorEmpireView, TacticalBitmaps.EmpireBomberEmpireView, TacticalBitmaps.EmpireDefenderEmpireView],
        ];
        int i = BattleEngine.TypeIndex(defs, type) - TacticalTypeIndex.FirstFighter;
        return i is >= 0 and < 8 ? table[local][i] : null;
    }

    /// <summary>A type-303 image as a texture (fighters, effects, background): palette 15001 unless given (a planet: 15000 + its picture) and index 0 transparent (both Inferred).</summary>
    public static Texture2D? Image(int id, int palette = TacticalBitmaps.ScenePalette)
    {
        string key = "#" + id + "/" + palette;
        if (Textures.TryGetValue(key, out var cached)) return cached;
        Texture2D? texture = null;
        if (Assets.Module(Module).Find(new ResId(ResourceTypes.TacticalImage), (ushort)id) is { } res && !RleImage.IsPalette(res.Data.Span))
            try { texture = Texture(RleImage.Decode(res.Data.Span), transparent: 0, palette); }
            catch (System.IO.InvalidDataException) { }
        return Textures[key] = texture;
    }

    /// <summary>Palette colour <paramref name="index"/> of palette 15001 (the far fighter dot, images 4200-4204 are not decodable).</summary>
    public static Color PaletteColour(int index) => (_palette ??= Palette(TacticalBitmaps.ScenePalette))[index];

    /// <summary>World (x, y, z): Godot (x, y, -z).</summary>
    public static Vector3 ToGodot(double x, double y, double z) => new((float)x, (float)y, (float)-z);

    /// <summary>
    /// The node of one capital ship class: the mod's glTF model when the record has one and it loads (Inferred: scale 1 world unit
    /// per glTF metre, the bow at +Z = the glTF forward, as the .x models; a file that fails or has no mesh falls back), else the .x
    /// LODs, else a box placeholder (a Death Star: a sphere).
    /// </summary>
    public static Node3D Ship(OpenSWR.Data.Dat.GameDefinitions defs, uint type)
    {
        var node = new Node3D();
        if (defs.Models.TryGetValue(type, out var path) && GltfScene(path)?.Duplicate() is Node3D mod)
        {
            node.AddChild(mod);
            mod.RotationDegrees = new Vector3(0, 180, 0); // glTF nose points to -z; turn it 180 degrees to +Z
            return node;
        }
        if (Family.Of(type) < Families.DeathStar && ModelId(defs, type) is { } id && Mesh(id) is not null && Mesh(id + 1) is not null && Mesh(id + 2) is not null)
        {
            for (int lod = 0; lod < 3; lod++) node.AddChild(new MeshInstance3D { Name = lod.ToString(), Mesh = Mesh(id + lod), Visible = lod == 1 });
            return node;
        }
        node.AddChild(new MeshInstance3D { Mesh = Family.Of(type) is >= Families.DeathStar and < Families.Fighter ? new SphereMesh { Radius = 9.5f, Height = 19 } : new BoxMesh { Size = new Vector3(1, 0.5f, 3) } });
        return node;
    }

    // one parsed glTF per file (outside the tree); every ship of the class gets a copy sharing its meshes
    static readonly Dictionary<string, Node3D?> GltfScenes = [];
    static Node3D? GltfScene(string path) => GltfScenes.TryGetValue(path, out var scene) ? scene : GltfScenes[path] = Gltf(path);

    static Node3D? Gltf(string path)
    {
        try
        {
            var doc = new GltfDocument();
            var state = new GltfState();
            if (doc.AppendFromFile(path, state) != Error.Ok) return null;
            if (doc.GenerateScene(state) is not Node3D scene) return null;
            if (scene.FindChildren("*", "MeshInstance3D", true, false).Count > 0) return scene;
            scene.Free(); // no mesh (an empty glTF): the .x model instead
            return null;
        }
        catch (Exception e)
        {
            GD.PushWarning($"glTF model {path}: {e.Message}");
            return null;
        }
    }

    static ArrayMesh? Mesh(int id)
    {
        if (Meshes.TryGetValue(id, out var cached)) return cached;
        ArrayMesh? mesh = null;
        if (Assets.Module(Module).Find(new ResId(ResourceTypes.TacticalModel), (ushort)id) is { } res
            && XFile.Parse(XFile.Decompress(res.Data.Span)).FirstOrDefault(n => n.Type == "Mesh") is { } node)
            mesh = Build(XMesh.From(node));
        return Meshes[id] = mesh;
    }

    /// <summary>One surface per material; polygons as fans; per face corner the vertex position / UV and the normal of the MeshNormals face.</summary>
    static ArrayMesh Build(XMesh x)
    {
        var mesh = new ArrayMesh();
        int materials = Math.Max(1, x.Materials.Count);
        for (int m = 0; m < materials; m++)
        {
            var st = new SurfaceTool();
            st.Begin(Godot.Mesh.PrimitiveType.Triangles);
            int corners = 0;
            for (int f = 0; f < x.Faces.Length; f++)
            {
                if (x.FaceMaterials.Length > f && x.FaceMaterials[f] != m && x.Materials.Count > 0) continue;
                var face = x.Faces[f];
                int[]? normals = x.NormalFaces.Length > f ? x.NormalFaces[f] : null;
                for (int k = 1; k + 1 < face.Length; k++)
                    foreach (int c in new[] { 0, k + 1, k }) // z negated: reversed winding
                    {
                        int v = face[c];
                        if (normals is not null && c < normals.Length)
                        {
                            int n = normals[c];
                            st.SetNormal(new Vector3(x.Normals[3 * n], x.Normals[3 * n + 1], -x.Normals[3 * n + 2]));
                        }
                        if (x.TexCoords.Length > 2 * v + 1) st.SetUV(new Vector2(x.TexCoords[2 * v], x.TexCoords[2 * v + 1]));
                        st.AddVertex(new Vector3(x.Positions[3 * v], x.Positions[3 * v + 1], -x.Positions[3 * v + 2]));
                        corners++;
                    }
            }
            if (corners == 0) continue;
            var material = new StandardMaterial3D { CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            if (m < x.Materials.Count && x.Materials[m].Texture is { } file && Texture(file) is { } tex) material.AlbedoTexture = tex;
            else if (m < x.Materials.Count) material.AlbedoColor = new Color(x.Materials[m].FaceColor[0], x.Materials[m].FaceColor[1], x.Materials[m].FaceColor[2]);
            st.SetMaterial(material);
            st.Commit(mesh);
        }
        return mesh;
    }

    /// <summary>A named type-303 RLE image with palette 15001. Inferred: the image does not say which palette it uses.</summary>
    static Texture2D? Texture(string file)
    {
        if (Textures.TryGetValue(file, out var cached)) return cached;
        var named = _named ??= Assets.Module(Module).OfType(new ResId(ResourceTypes.TacticalImage)).Where(e => e.Name.Name is not null)
            .GroupBy(e => e.Name.Name!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        Texture2D? texture = null;
        if (named.TryGetValue(file, out var res) && !RleImage.IsPalette(res.Data.Span)) texture = Texture(RleImage.Decode(res.Data.Span), transparent: -1);
        return Textures[file] = texture;
    }

    static Texture2D Texture(RleImage image, int transparent, int paletteId = TacticalBitmaps.ScenePalette)
    {
        var palette = paletteId == TacticalBitmaps.ScenePalette ? _palette ??= Palette(TacticalBitmaps.ScenePalette) : Palette(paletteId);
        var rgba = new byte[image.Width * image.Height * 4];
        for (int i = 0; i < image.Pixels.Length; i++)
        {
            var c = palette[image.Pixels[i]];
            (rgba[4 * i], rgba[4 * i + 1], rgba[4 * i + 2], rgba[4 * i + 3]) = ((byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255), (byte)(image.Pixels[i] == transparent ? 0 : 255));
        }
        var img = Godot.Image.CreateFromData(image.Width, image.Height, false, Godot.Image.Format.Rgba8, rgba);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    static Color[] Palette(int id)
    {
        // An unmodded install has no 15001-15200: 5557 is byte-equal to the mod's 15001.
        var module = Assets.Module(Module);
        var data = (module.Find(new ResId(ResourceTypes.TacticalImage), (ushort)id) ?? module.Find(new ResId(ResourceTypes.TacticalImage), TacticalBitmaps.PlainScenePalette))?.Data.ToArray() ?? new byte[768];
        return [.. Enumerable.Range(0, 256).Select(i => Color.Color8(data[3 * i], data[3 * i + 1], data[3 * i + 2]))];
    }
}
