using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

static void Die(string msg) { Console.Error.WriteLine($"ERROR: {msg}"); Environment.Exit(1); }

if (args.Length < 1) Die("Usage: converter <input.db>");

var inputDb = args[0];
if (!File.Exists(inputDb)) Die($"Input DB not found: {inputDb}");

var folder  = Path.GetDirectoryName(inputDb)!;
var objPath = FindSourceFile(inputDb, folder);

Console.WriteLine($"Converting: {objPath}");
var (groups, positions, normals, uvs) = ReadObj(objPath);

var resultDb = Path.Combine(folder, "result.db");
if (File.Exists(resultDb)) File.Delete(resultDb);

using var conn = new SqliteConnection($"Data Source={resultDb}");
conn.Open();
CreateSchema(conn);
WriteMeta(conn);

conn.Execute("INSERT INTO models VALUES (0, 'model')");

int matId = 0;
var matMap = new Dictionary<string, int>();

for (int meshId = 0; meshId < groups.Count; meshId++)
{
    var group = groups[meshId];
    if (group.Faces.Count == 0) continue;

    var matName = group.Material ?? $"material_{meshId}";
    if (!matMap.ContainsKey(matName))
    {
        conn.Execute("INSERT INTO materials VALUES (?,?,NULL,NULL,NULL,NULL,NULL)", matId, matName);
        matMap[matName] = matId++;
    }

    conn.Execute("INSERT INTO meshes VALUES (?,0,?,?,?)", meshId, matMap[matName], group.Name, "Standard");
    conn.Execute("INSERT INTO parts VALUES (?,0,?,NULL)", meshId, group.Name);

    var vertKeyToId = new Dictionary<(int pi, int ti, int ni), int>();
    var vertices    = new List<(float[] pos, float[] norm, float[] uv)>();
    var faceRows    = new List<int[]>();

    foreach (var face in group.Faces)
    {
        var fn  = ComputeFlatNormal(positions, face);
        var tri = new int[3];
        for (int i = 0; i < 3; i++)
        {
            var (pi, ti, ni) = face[i];
            var key = (pi, ti, ni);
            if (!vertKeyToId.TryGetValue(key, out var vid))
            {
                vid = vertices.Count;
                vertKeyToId[key] = vid;
                var pos  = positions[pi];
                var norm = (ni >= 0 && ni < normals.Count) ? normals[ni] : fn;
                var uv   = (ti >= 0 && ti < uvs.Count)    ? uvs[ti]     : new[] { 0f, 0f };
                vertices.Add((pos, norm, uv));
            }
            tri[i] = vid;
        }
        faceRows.Add(tri);
    }

    using var tx = conn.BeginTransaction();
    for (int vid = 0; vid < vertices.Count; vid++)
    {
        var (pos, norm, uv) = vertices[vid];
        conn.Execute(@"INSERT INTO vertices
            (mesh,part,vertex_id,
             position_x,position_y,position_z,
             normal_x,normal_y,normal_z,
             binormal_x,binormal_y,binormal_z,
             tangent_x,tangent_y,tangent_z,
             color_r,color_g,color_b,color_a,
             color2_r,color2_g,color2_b,color2_a,
             uv_1_u,uv_1_v,uv_2_u,uv_2_v,uv_3_u,uv_3_v,
             flow_u,flow_v)
            VALUES (?,?,?,?,?,?,?,?,?,0,0,1,1,0,0,1,1,1,1,0,0,0,1,?,?,0,0,0,0,0,0)",
            meshId, 0, vid,
            pos[0], pos[1], pos[2],
            norm[0], norm[1], norm[2],
            uv[0], uv[1]);
    }

    int idxId = 0;
    foreach (var tri in faceRows)
        foreach (var vid in tri)
            conn.Execute("INSERT INTO indices VALUES (?,0,?,?)", meshId, idxId++, vid);

    tx.Commit();
}

Console.WriteLine($"Done: {resultDb} ({groups.Count} meshes)");

// ── Helpers ───────────────────────────────────────────────────────────────────

static string FindSourceFile(string inputDb, string folder)
{
    foreach (var f in Directory.GetFiles(folder))
        if (f.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
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
    Die($"No .obj file found in {folder}");
    return "";
}

static (List<MeshGroup> groups, List<float[]> positions, List<float[]> normals, List<float[]> uvs)
    ReadObj(string path)
{
    var positions = new List<float[]>();
    var normals   = new List<float[]>();
    var uvs       = new List<float[]>();
    var groups    = new List<MeshGroup>();
    MeshGroup? current = null;
    string? currentMat = null;

    foreach (var rawLine in File.ReadLines(path))
    {
        var line  = rawLine.Trim();
        if (string.IsNullOrEmpty(line) || line[0] == '#') continue;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts[0])
        {
            case "v":
                positions.Add(new[] { float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3]) });
                break;
            case "vn":
                normals.Add(new[] { float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3]) });
                break;
            case "vt":
                uvs.Add(new[] { float.Parse(parts[1]), parts.Length > 2 ? float.Parse(parts[2]) : 0f });
                break;
            case "g":
            case "o":
                var name = parts.Length > 1 ? parts[1] : $"mesh_{groups.Count}";
                current = new MeshGroup(name, currentMat);
                groups.Add(current);
                break;
            case "usemtl":
                currentMat = parts.Length > 1 ? parts[1] : null;
                if (current == null) { current = new MeshGroup($"mesh_{groups.Count}", currentMat); groups.Add(current); }
                else current.Material = currentMat;
                break;
            case "f":
                if (current == null) { current = new MeshGroup("mesh_0", currentMat); groups.Add(current); }
                var verts = parts[1..].Select(tok => {
                    var idx = tok.Split('/');
                    int pi  = int.Parse(idx[0]) - 1;
                    int ti  = idx.Length > 1 && idx[1].Length > 0 ? int.Parse(idx[1]) - 1 : -1;
                    int ni  = idx.Length > 2 && idx[2].Length > 0 ? int.Parse(idx[2]) - 1 : -1;
                    return (pi, ti, ni);
                }).ToList();
                // Fan triangulation
                for (int i = 1; i < verts.Count - 1; i++)
                    current.Faces.Add(new[] { verts[0], verts[i], verts[i + 1] });
                break;
        }
    }

    if (groups.Count == 0) Die($"No mesh groups found in {path}");
    return (groups, positions, normals, uvs);
}

static float[] ComputeFlatNormal(List<float[]> positions, (int pi, int ti, int ni)[] face)
{
    var p0 = positions[face[0].pi]; var p1 = positions[face[1].pi]; var p2 = positions[face[2].pi];
    float ax = p1[0]-p0[0], ay = p1[1]-p0[1], az = p1[2]-p0[2];
    float bx = p2[0]-p0[0], by = p2[1]-p0[1], bz = p2[2]-p0[2];
    float nx = ay*bz - az*by, ny = az*bx - ax*bz, nz = ax*by - ay*bx;
    float l  = MathF.Sqrt(nx*nx + ny*ny + nz*nz);
    if (l == 0) l = 1;
    return new[] { nx/l, ny/l, nz/l };
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

static void WriteMeta(SqliteConnection conn)
{
    foreach (var (k, v) in new[] {
        ("application","xivtools_obj_converter"), ("unit","meter"), ("up","y"),
        ("front","z"), ("handedness","r"), ("root_name","root"),
        ("for_3ds_max","0"), ("version","1.0") })
        conn.Execute("INSERT INTO meta VALUES (?,?)", k, v);
}

class MeshGroup
{
    public string Name;
    public string? Material;
    public List<(int pi, int ti, int ni)[]> Faces = new();
    public MeshGroup(string name, string? mat) { Name = name; Material = mat; }
}

// ── Extension helper ─────────────────────────────────────────────────────────
static class SqliteExtensions
{
    public static void Execute(this SqliteConnection conn, string sql, params object?[] p)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        for (int i = 0; i < p.Length; i++)
            cmd.Parameters.AddWithValue($"${i+1}", p[i] ?? DBNull.Value);
        // Replace ? with $1 $2 ... style
        cmd.CommandText = ReplaceQmarks(sql, p.Length);
        cmd.ExecuteNonQuery();
    }

    private static string ReplaceQmarks(string sql, int count)
    {
        int idx = 1;
        var sb  = new System.Text.StringBuilder();
        foreach (var c in sql)
            sb.Append(c == '?' ? $"${idx++}" : c.ToString());
        return sb.ToString();
    }
}
