using OpenSWR.Data;
using OpenSWR.Data.Tactical;

namespace OpenSWR.Tests;

public class XFileTests
{
    static IEnumerable<PeResource> Models() => PeResources.Load(GameDir.File("TACTICAL.DLL")).OfType(new ResId(301));

    [GameFact("OPENSWR_DUMP_DIR")]
    public void DecompressMatchesDump()
    {
        var models = Models().ToList();
        Assert.Equal(87, models.Count);
        foreach (var m in models)
        {
            var dumped = File.ReadAllBytes(Path.Combine(Environment.GetEnvironmentVariable("OPENSWR_DUMP_DIR")!, "TACTICAL_DLL", "301", $"{m.Name}.x"));
            Assert.Equal(dumped, XFile.Decompress(m.Data.Span));
        }
    }

    [GameFact]
    public void EveryModelIsOneConsistentMesh()
    {
        foreach (var m in Models())
        {
            var mesh = XMesh.From(Assert.Single(XFile.Parse(XFile.Decompress(m.Data.Span)), n => n.Type == "Mesh"));
            int vertices = mesh.Positions.Length / 3;
            Assert.True(vertices > 0);
            Assert.All(mesh.Faces, f => Assert.All(f, i => Assert.InRange(i, 0, vertices - 1)));
            Assert.Equal(mesh.Faces.Length, mesh.NormalFaces.Length);
            Assert.Equal(vertices * 2, mesh.TexCoords.Length);
            Assert.Equal(mesh.Faces.Length, mesh.FaceMaterials.Length);
            Assert.All(mesh.FaceMaterials, i => Assert.InRange(i, 0, mesh.Materials.Count - 1));
            Assert.All(mesh.Materials, mat => Assert.False(string.IsNullOrEmpty(mat.Texture)));
        }
    }
}
