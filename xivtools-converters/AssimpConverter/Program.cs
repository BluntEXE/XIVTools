using System;
using System.IO;
using Assimp;
using Microsoft.Data.Sqlite;

static void Die(string msg) { Console.Error.WriteLine($"ERROR: {msg}"); Environment.Exit(1); }

if (args.Length < 1) Die("Usage: converter <input.db>");

var inputDb = args[0];
if (!File.Exists(inputDb)) Die($"Input DB not found: {inputDb}");

var folder  = Path.GetDirectoryName(inputDb)!;
var srcPath = FindSourceFile(inputDb, folder);

Console.WriteLine($"Converting: {srcPath}");

var ctx   = new AssimpContext();
var flags = PostProcessSteps.Triangulate
          | PostProcessSteps.GenerateSmoothNormals
          | PostProcessSteps.CalculateTangentSpace
          | PostProcessSteps.JoinIdenticalVertices
          | PostProcessSteps.FlipUVs;

var scene = ctx.ImportFile(srcPath, flags);
if (scene == null || scene.MeshCount == 0) Die($"No meshes found in {srcPath}");
scene = scene!;

var resultDb = Path.Combine(folder, "result.db");
if (File.Exists(resultDb)) File.Delete(resultDb);

using var conn = new SqliteConnection($"Data Source={resultDb}");
conn.Open();
CreateSchema(conn);
WriteMeta(conn, "xivtools_assimp_converter");
conn.Execute("INSERT INTO models VALUES (0,'model')");

// Materials
for (int i = 0; i < scene.MaterialCount; i++)
{
    var mat      = scene.Materials[i];
    var name     = mat.Name ?? $"material_{i}";
    string? diff = mat.HasTextureDiffuse   ? mat.TextureDiffuse.FilePath   : null;
    string? norm = mat.HasTextureNormal    ? mat.TextureNormal.FilePath    : null;
    string? spec = mat.HasTextureSpecular  ? mat.TextureSpecular.FilePath  : null;
    conn.Execute("INSERT INTO materials VALUES (?,?,?,?,?,NULL,NULL)", i, name, diff, norm, spec);
}

// Meshes
for (int meshId = 0; meshId < scene.MeshCount; meshId++)
{
    var mesh   = scene.Meshes[meshId];
    var mname  = string.IsNullOrEmpty(mesh.Name) ? $"mesh_{meshId}" : mesh.Name;
    conn.Execute("INSERT INTO meshes VALUES (?,0,?,?,?)", meshId, mesh.MaterialIndex, mname, "Standard");
    conn.Execute("INSERT INTO parts VALUES (?,0,?,NULL)", meshId, mname);

    bool hasUv1   = mesh.HasTextureCoords(0);
    bool hasUv2   = mesh.HasTextureCoords(1);
    bool hasNorm  = mesh.HasNormals;
    bool hasTan   = mesh.HasTangentBasis;

    using var tx = conn.BeginTransaction();

    for (int vid = 0; vid < mesh.VertexCount; vid++)
    {
        var v  = mesh.Vertices[vid];
        var n  = hasNorm ? mesh.Normals[vid]    : new Vector3D(0, 1, 0);
        var t  = hasTan  ? mesh.Tangents[vid]   : new Vector3D(1, 0, 0);
        var bt = hasTan  ? mesh.BiTangents[vid] : new Vector3D(0, 0, 1);
        var uv1 = hasUv1 ? mesh.TextureCoordinateChannels[0][vid] : new Vector3D(0, 0, 0);
        var uv2 = hasUv2 ? mesh.TextureCoordinateChannels[1][vid] : new Vector3D(0, 0, 0);

        // Bone weights (up to 8 slots)
        var boneIds     = new int?[8];
        var boneWeights = new float?[8];
        for (int bi = 0; bi < mesh.BoneCount; bi++)
        {
            foreach (var w in mesh.Bones[bi].VertexWeights)
            {
                if (w.VertexID != vid) continue;
                for (int s = 0; s < 8; s++)
                {
                    if (boneIds[s] == null) { boneIds[s] = bi; boneWeights[s] = w.Weight; break; }
                }
            }
        }

        conn.Execute(@"INSERT INTO vertices
            (mesh,part,vertex_id,
             position_x,position_y,position_z,
             normal_x,normal_y,normal_z,
             binormal_x,binormal_y,binormal_z,
             tangent_x,tangent_y,tangent_z,
             color_r,color_g,color_b,color_a,
             color2_r,color2_g,color2_b,color2_a,
             uv_1_u,uv_1_v,uv_2_u,uv_2_v,uv_3_u,uv_3_v,
             bone_1_id,bone_1_weight,bone_2_id,bone_2_weight,
             bone_3_id,bone_3_weight,bone_4_id,bone_4_weight,
             bone_5_id,bone_5_weight,bone_6_id,bone_6_weight,
             bone_7_id,bone_7_weight,bone_8_id,bone_8_weight,
             flow_u,flow_v)
            VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,
                    1,1,1,1,0,0,0,1,?,?,?,?,0,0,
                    ?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,0,0)",
            meshId, 0, vid,
            v.X, v.Y, v.Z,
            n.X, n.Y, n.Z,
            bt.X, bt.Y, bt.Z,
            t.X,  t.Y,  t.Z,
            uv1.X, uv1.Y, uv2.X, uv2.Y,
            boneIds[0], boneWeights[0], boneIds[1], boneWeights[1],
            boneIds[2], boneWeights[2], boneIds[3], boneWeights[3],
            boneIds[4], boneWeights[4], boneIds[5], boneWeights[5],
            boneIds[6], boneWeights[6], boneIds[7], boneWeights[7]);
    }

    int idxId = 0;
    foreach (var face in mesh.Faces)
        foreach (var idx in face.Indices)
            conn.Execute("INSERT INTO indices VALUES (?,0,?,?)", meshId, idxId++, idx);

    // Bone names
    for (int bi = 0; bi < mesh.BoneCount; bi++)
        conn.Execute("INSERT OR IGNORE INTO bones VALUES (?,?,?)", meshId, bi, mesh.Bones[bi].Name);

    tx.Commit();
}

Console.WriteLine($"Done: {resultDb} ({scene.MeshCount} meshes)");

// ── Helpers ───────────────────────────────────────────────────────────────────

static string FindSourceFile(string inputDb, string folder)
{
    var exts = new[] { ".fbx", ".glb", ".gltf", ".dae", ".blend" };
    foreach (var f in Directory.GetFiles(folder))
        foreach (var ext in exts)
            if (f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return f;
    try
    {
        using var c = new SqliteConnection($"Data Source={inputDb}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key='source_path'";
        var val = cmd.ExecuteScalar()?.ToString();
        if (val != null && File.Exists(val)) return val;
    }
    catch { }
    Die($"No supported 3D file (.fbx/.glb/.gltf/.dae) found in {folder}");
    return "";
}

static void CreateSchema(SqliteConnection conn)
{
    conn.Execute(@"
CREATE TABLE meta (key TEXT NOT NULL UNIQUE, value TEXT, PRIMARY KEY(key));
CREATE TABLE warnings (text TEXT NOT NULL);
CREATE TABLE indices (mesh INTEGER NOT NULL, part INTEGER NOT NULL, index_id INTEGER NOT NULL, vertex_id INTEGER NOT NULL, PRIMARY KEY(mesh,part,index_id));
CREATE TABLE vertices (
    mesh INTEGER NOT NULL, part INTEGER NOT NULL, vertex_id INTEGER NOT NULL,
    position_x REAL NOT NULL, position_y REAL NOT NULL, position_z REAL NOT NULL,
    normal_x REAL NOT NULL, normal_y REAL NOT NULL, normal_z REAL NOT NULL,
    binormal_x REAL, binormal_y REAL, binormal_z REAL,
    tangent_x REAL, tangent_y REAL, tangent_z REAL,
    color_r REAL NOT NULL DEFAULT 1.0, color_g REAL NOT NULL DEFAULT 1.0,
    color_b REAL NOT NULL DEFAULT 1.0, color_a REAL NOT NULL DEFAULT 1.0,
    color2_r REAL NOT NULL DEFAULT 0.0, color2_g REAL NOT NULL DEFAULT 0.0,
    color2_b REAL NOT NULL DEFAULT 0.0, color2_a REAL NOT NULL DEFAULT 1.0,
    uv_1_u REAL NOT NULL DEFAULT 0.0, uv_1_v REAL NOT NULL DEFAULT 0.0,
    uv_2_u REAL NOT NULL DEFAULT 0.0, uv_2_v REAL NOT NULL DEFAULT 0.0,
    uv_3_u REAL NOT NULL DEFAULT 0.0, uv_3_v REAL NOT NULL DEFAULT 0.0,
    bone_1_id INTEGER, bone_1_weight REAL, bone_2_id INTEGER, bone_2_weight REAL,
    bone_3_id INTEGER, bone_3_weight REAL, bone_4_id INTEGER, bone_4_weight REAL,
    bone_5_id INTEGER, bone_5_weight REAL, bone_6_id INTEGER, bone_6_weight REAL,
    bone_7_id INTEGER, bone_7_weight REAL, bone_8_id INTEGER, bone_8_weight REAL,
    flow_u REAL NOT NULL DEFAULT 0.0, flow_v REAL NOT NULL DEFAULT 0.0,
    PRIMARY KEY(mesh,part,vertex_id));
CREATE TABLE shape_vertices (shape TEXT NOT NULL, mesh INTEGER NOT NULL, part INTEGER NOT NULL, vertex_id INTEGER NOT NULL, position_x REAL NOT NULL, position_y REAL NOT NULL, position_z REAL NOT NULL, PRIMARY KEY(shape,mesh,part,vertex_id));
CREATE TABLE models (model INTEGER NOT NULL, name TEXT, PRIMARY KEY(model));
CREATE TABLE meshes (mesh INTEGER NOT NULL, model INTEGER NOT NULL, material_id INTEGER, name TEXT, type TEXT, PRIMARY KEY(mesh));
CREATE TABLE parts (mesh INTEGER NOT NULL, part INTEGER NOT NULL, name TEXT, attributes TEXT, PRIMARY KEY(mesh,part));
CREATE TABLE bones (mesh INTEGER NOT NULL, bone_id INTEGER NOT NULL, name TEXT NOT NULL, PRIMARY KEY(mesh,bone_id));
CREATE TABLE skeleton (name TEXT NOT NULL, parent TEXT, matrix_0 REAL, matrix_1 REAL, matrix_2 REAL, matrix_3 REAL, matrix_4 REAL, matrix_5 REAL, matrix_6 REAL, matrix_7 REAL, matrix_8 REAL, matrix_9 REAL, matrix_10 REAL, matrix_11 REAL, matrix_12 REAL, matrix_13 REAL, matrix_14 REAL, matrix_15 REAL, PRIMARY KEY(name));
CREATE TABLE materials (material_id INTEGER NOT NULL, name TEXT, diffuse TEXT, normal TEXT, specular TEXT, opacity TEXT, emissive TEXT, PRIMARY KEY(material_id));
");
}

static void WriteMeta(SqliteConnection conn, string appName)
{
    foreach (var (k, v) in new[] {
        ("application", appName), ("unit","meter"), ("up","y"),
        ("front","z"), ("handedness","r"), ("root_name","root"),
        ("for_3ds_max","0"), ("version","1.0") })
        conn.Execute("INSERT INTO meta VALUES (?,?)", k, v);
}

static class SqliteExtensions
{
    public static void Execute(this SqliteConnection conn, string sql, params object?[] p)
    {
        using var cmd = conn.CreateCommand();
        int idx = 1;
        var sb  = new System.Text.StringBuilder();
        foreach (var c in sql)
            sb.Append(c == '?' ? $"${idx++}" : c.ToString());
        cmd.CommandText = sb.ToString();
        for (int i = 0; i < p.Length; i++)
            cmd.Parameters.AddWithValue($"${i+1}", p[i] ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
