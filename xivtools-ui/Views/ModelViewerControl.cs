using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using XivToolsUI.ViewModels;
using static Avalonia.OpenGL.GlConsts;


namespace XivToolsUI.Views;

// GL constants not in Avalonia's GlConsts
file static class GLExt {
    public const int GL_TEXTURE_WRAP_S = 0x2802;
    public const int GL_TEXTURE_WRAP_T = 0x2803;
    public const int GL_REPEAT         = 0x2901;
}

/// <summary>
/// OpenGL orbit-camera model viewer with texture support.
/// Vertex format: pos(3)+normal(3)+uv(2) = 8 floats, stride=32.
/// </summary>
public class ModelViewerControl : OpenGlControlBase
{
    private record GpuMesh(int Vao, int Vbo, int Ibo, int IndexCount,
                           float R, float G, float B, int TexId);

    private readonly List<GpuMesh> _meshes = new();
    private int _prog;
    private int _uMvp, _uLX, _uLY, _uLZ, _uCR, _uCG, _uCB, _uAmb, _uHasTex, _uTex;

    public List<GlMeshData>? PendingMeshes { get; set; }

    private float _az = 30f, _el = 20f, _radius = 3f;
    private Vector3 _pivot;
    private Avalonia.Point _last;
    private bool _ld, _rd;

    // ── Shaders ────────────────────────────────────────────────
    private const string VS = @"
#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNorm;
layout(location=2) in vec2 aUV;
uniform mat4 uMvp;
out vec3 vN;
out vec2 vUV;
void main(){ vN=aNorm; vUV=aUV; gl_Position=uMvp*vec4(aPos,1.0); }";

    private const string FS = @"
#version 330 core
in vec3 vN;
in vec2 vUV;
out vec4 oC;
uniform float uLX,uLY,uLZ, uCR,uCG,uCB, uAmb;
uniform int   uHasTex;
uniform sampler2D uTex;
void main(){
    vec3 n   = normalize(vN);
    vec3 l   = normalize(vec3(uLX,uLY,uLZ));
    float d  = max(dot(n,l),0.0) + max(dot(-n,l),0.0)*0.2;
    float r  = pow(1.0-abs(dot(n,vec3(0,0,1))),3.0)*0.12;
    float lit= uAmb + (1.0-uAmb)*d + r;
    vec3 col = (uHasTex==1) ? texture(uTex,vUV).rgb : vec3(uCR,uCG,uCB);
    oC = vec4(col*lit, 1.0);
}";

    // ── Lifecycle ──────────────────────────────────────────────
    protected override void OnOpenGlInit(GlInterface gl)
    {
        _prog    = MakeProg(gl);
        _uMvp    = gl.GetUniformLocationString(_prog, "uMvp");
        _uLX     = gl.GetUniformLocationString(_prog, "uLX");
        _uLY     = gl.GetUniformLocationString(_prog, "uLY");
        _uLZ     = gl.GetUniformLocationString(_prog, "uLZ");
        _uCR     = gl.GetUniformLocationString(_prog, "uCR");
        _uCG     = gl.GetUniformLocationString(_prog, "uCG");
        _uCB     = gl.GetUniformLocationString(_prog, "uCB");
        _uAmb    = gl.GetUniformLocationString(_prog, "uAmb");
        _uHasTex = gl.GetUniformLocationString(_prog, "uHasTex");
        _uTex    = gl.GetUniformLocationString(_prog, "uTex");
        gl.Enable(GL_DEPTH_TEST);
        gl.Enable(GL_CULL_FACE);
    }

    protected override void OnOpenGlDeinit(GlInterface gl) => FreeAll(gl);

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (PendingMeshes != null) { Upload(gl, PendingMeshes); PendingMeshes = null; }

        int w = (int)Bounds.Width, h = (int)Bounds.Height;
        if (w < 1 || h < 1) return;
        gl.Viewport(0, 0, w, h);
        gl.ClearColor(0.07f, 0.07f, 0.11f, 1f);
        gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
        if (_meshes.Count == 0) return;

        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, (float)w / h, _radius * 0.001f, _radius * 10f);
        var view = Matrix4x4.CreateLookAt(Eye(), _pivot, Vector3.UnitY);
        var mvp  = view * proj;

        gl.UseProgram(_prog);
        SetMat4(gl, _uMvp, mvp);
        gl.Uniform1f(_uLX, 0.6f); gl.Uniform1f(_uLY, 1.0f); gl.Uniform1f(_uLZ, 0.7f);
        gl.Uniform1f(_uAmb, 0.20f);

        foreach (var m in _meshes) {
            if (m.TexId > 0) {
                gl.ActiveTexture(GL_TEXTURE0);
                gl.BindTexture(GL_TEXTURE_2D, m.TexId);
                gl.Uniform1i(_uHasTex, 1);
                gl.Uniform1i(_uTex, 0);
            } else {
                gl.Uniform1i(_uHasTex, 0);
                gl.Uniform1f(_uCR, m.R); gl.Uniform1f(_uCG, m.G); gl.Uniform1f(_uCB, m.B);
            }
            gl.BindVertexArray(m.Vao);
            gl.DrawElements(GL_TRIANGLES, m.IndexCount, 0x1405, IntPtr.Zero);
        }
        gl.BindVertexArray(0);
        if (_meshes.Any(m => m.TexId > 0)) { gl.ActiveTexture(GL_TEXTURE0); gl.BindTexture(GL_TEXTURE_2D, 0); }
        gl.UseProgram(0);
    }

    // ── Upload ─────────────────────────────────────────────────
    private void Upload(GlInterface gl, List<GlMeshData> data)
    {
        FreeAll(gl);
        float mnX=float.MaxValue, mnY=float.MaxValue, mnZ=float.MaxValue;
        float mxX=float.MinValue, mxY=float.MinValue, mxZ=float.MinValue;
        foreach (var d in data)
            for (int i = 0; i < d.Vertices.Length; i += 8) {
                mnX=Math.Min(mnX,d.Vertices[i]);   mxX=Math.Max(mxX,d.Vertices[i]);
                mnY=Math.Min(mnY,d.Vertices[i+1]); mxY=Math.Max(mxY,d.Vertices[i+1]);
                mnZ=Math.Min(mnZ,d.Vertices[i+2]); mxZ=Math.Max(mxZ,d.Vertices[i+2]);
            }
        _pivot  = new Vector3((mnX+mxX)*0.5f, (mnY+mxY)*0.5f, (mnZ+mxZ)*0.5f);
        _radius = Math.Max(Math.Max(mxX-mnX, mxY-mnY), mxZ-mnZ) * 1.5f;

        foreach (var d in data) {
            int vao = gl.GenVertexArray(); gl.BindVertexArray(vao);
            int vbo = gl.GenBuffer();      gl.BindBuffer(GL_ARRAY_BUFFER, vbo);
            PutFloats(gl, GL_ARRAY_BUFFER, d.Vertices);
            int ibo = gl.GenBuffer();      gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, ibo);
            PutInts(gl, GL_ELEMENT_ARRAY_BUFFER, d.Indices);

            // stride=32 bytes: pos(12)+normal(12)+uv(8)
            gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, 32, IntPtr.Zero);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(1, 3, GL_FLOAT, 0, 32, new IntPtr(12));
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(2, 2, GL_FLOAT, 0, 32, new IntPtr(24));
            gl.EnableVertexAttribArray(2);
            gl.BindVertexArray(0);

            // Upload texture if available
            int texId = 0;
            if (d.Texture.HasValue) {
                var (pixels, tw, th) = d.Texture.Value;
                texId = gl.GenTexture();
                gl.ActiveTexture(GL_TEXTURE0);
                gl.BindTexture(GL_TEXTURE_2D, texId);
                gl.TexParameteri(GL_TEXTURE_2D, GLExt.GL_TEXTURE_WRAP_S, GLExt.GL_REPEAT);
                gl.TexParameteri(GL_TEXTURE_2D, GLExt.GL_TEXTURE_WRAP_T, GLExt.GL_REPEAT);
                gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
                gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
                UploadTexture(gl, pixels, tw, th);
                gl.BindTexture(GL_TEXTURE_2D, 0);
            }

            _meshes.Add(new GpuMesh(vao, vbo, ibo, d.Indices.Length, d.Color[0], d.Color[1], d.Color[2], texId));
        }
        RequestNextFrameRendering();
    }

    // ── Camera ─────────────────────────────────────────────────
    Vector3 Eye()
    {
        float az = _az*MathF.PI/180, el = _el*MathF.PI/180;
        return _pivot + _radius*new Vector3(MathF.Cos(el)*MathF.Sin(az), MathF.Sin(el), MathF.Cos(el)*MathF.Cos(az));
    }
    public void ResetCamera() { _az=30; _el=20; _pivot=Vector3.Zero; RequestNextFrameRendering(); }
    public void Orbit(float dx, float dy) { _az-=dx*0.4f; _el=Math.Clamp(_el+dy*0.4f,-88f,88f); RequestNextFrameRendering(); }
    public void Pan(float dx, float dy) {
        var r=Vector3.Normalize(Vector3.Cross(Eye()-_pivot,Vector3.UnitY));
        float s=_radius*0.0015f; _pivot-=r*dx*s; _pivot+=Vector3.UnitY*dy*s;
        RequestNextFrameRendering();
    }
    public void Zoom(float delta) { _radius=Math.Clamp(_radius*(float)Math.Pow(0.88,delta),0.001f,100000f); RequestNextFrameRendering(); }

    // ── Helpers ────────────────────────────────────────────────
    void FreeAll(GlInterface gl)
    {
        foreach (var m in _meshes) {
            gl.DeleteVertexArray(m.Vao); gl.DeleteBuffer(m.Vbo); gl.DeleteBuffer(m.Ibo);
            if (m.TexId > 0) gl.DeleteTexture(m.TexId);
        }
        _meshes.Clear();
    }

    static int MakeProg(GlInterface gl)
    {
        int vs=gl.CreateShader(GL_VERTEX_SHADER);   gl.ShaderSourceString(vs,VS); gl.CompileShader(vs);
        int fs=gl.CreateShader(GL_FRAGMENT_SHADER); gl.ShaderSourceString(fs,FS); gl.CompileShader(fs);
        int p=gl.CreateProgram(); gl.AttachShader(p,vs); gl.AttachShader(p,fs); gl.LinkProgram(p);
        gl.DeleteShader(vs); gl.DeleteShader(fs);
        return p;
    }

    static unsafe void PutFloats(GlInterface gl, int target, float[] d)
    { fixed (float* p=d) gl.BufferData(target, new IntPtr(d.Length*4), new IntPtr(p), GL_STATIC_DRAW); }

    static unsafe void PutInts(GlInterface gl, int target, int[] d)
    { fixed (int* p=d) gl.BufferData(target, new IntPtr(d.Length*4), new IntPtr(p), GL_STATIC_DRAW); }

    static unsafe void SetMat4(GlInterface gl, int loc, Matrix4x4 m)
    {
        float[] a={m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
        fixed(float* p=a) gl.UniformMatrix4fv(loc,1,false,p);
    }

    static unsafe void UploadTexture(GlInterface gl, byte[] pixels, int w, int h)
    {
        fixed (byte* p = pixels)
            gl.TexImage2D(GL_TEXTURE_2D, 0, 0x1908 /* GL_RGBA */, w, h, 0,
                          0x1908 /* GL_RGBA */, GL_UNSIGNED_BYTE, new IntPtr(p));
    }
}
