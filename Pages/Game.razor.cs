using Blazor.Extensions;
using Blazor.Extensions.Canvas;
using Blazor.Extensions.Canvas.WebGL;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;
using WebFLCube.Math;

//Me equivoqué al nombrar la carpeta. F.
namespace WebFLCube.Pages {
  public partial class Game: ComponentBase {
    [Inject]
    private IJSRuntime JSRuntime {get; set;}
    private int currentCount = 0;
    private WebGLContext _context;
    protected BECanvasComponent _canvasReference;

    private static readonly float[] cubeVertices = {
      -1f, -1f, -1f,
      -1f, -1f, -1f,
      -1f, -1f, -1f,

      -1f, -1f,  1f,
      -1f, -1f,  1f,
      -1f, -1f,  1f,

      -1f,  1f, -1f,
      -1f,  1f, -1f,
      -1f,  1f, -1f,

      -1f,  1f,  1f,
      -1f,  1f,  1f,
      -1f,  1f,  1f,

       1f, -1f, -1f,
       1f, -1f, -1f,
       1f, -1f, -1f,
       
       1f, -1f,  1f,
       1f, -1f,  1f,
       1f, -1f,  1f,

       1f,  1f, -1f,
       1f,  1f, -1f,
       1f,  1f, -1f,

       1f,  1f,  1f,
       1f,  1f,  1f,
       1f,  1f,  1f
    };

    private static readonly int[] intCubeIndices = {
       6,  0,  3,
       3,  9,  6,
      12, 18, 21,
      21, 15, 12,
       1, 13, 16,
      16,  4,  1,
      19,  7, 10,
      10, 22, 19,
      11,  5, 17,
      17, 23, 11,
      20, 14,  2,
       2,  8, 20
    };

    private float[] cubeColors = new[] {
      0f,  1f,0f,1f, //0  green
      1f,  0f,0f,1f, //1  red
      1f,  1f,0f,1f, //2  yellow
      0f,  1f,0f,1f, //3  green
      1f,  0f,0f,1f, //4  red
      1f,  1f,1f,1f, //5  white
      0f,  1f,0f,1f, //6  green
      1f,0.5f,0f,1f, //7  orange
      1f,  1f,0f,1f, //8  yellow
      0f,  1f,0f,1f, //9  green
      1f,0.5f,0f,1f, //10 orange
      1f,  1f,1f,1f, //11 white
      0f,  1f,1f,1f, //12 cyan
      1f,  0f,0f,1f, //13 red
      1f,  1f,0f,1f, //14 yellow
      0f,  1f,1f,1f, //15 cyan
      1f,  0f,0f,1f, //16 red
      1f,  1f,1f,1f, //17 white
      0f,  1f,1f,1f, //18 cyan
      1f,0.5f,0f,1f, //19 orange
      1f,  1f,0f,1f, //20 yellow
      0f,  1f,1f,1f, //21 cyan
      1f,0.5f,0f,1f, //22 orange
      1f,  1f,1f,1f  //23 white
    };

    private static readonly ushort[] cubeIndices = Array.ConvertAll(intCubeIndices, val=>checked((ushort) val));

    private const string vsSource = @"
    uniform mat4 uModelViewMatrix;
    uniform mat4 uProjectionMatrix;
    attribute vec3 aVertexPosition;
    attribute vec4 aVertexColor;
    varying vec4 vVertexPosition;
    varying vec4 vVertexColor;
    void main(void) {
      vVertexPosition = uProjectionMatrix*uModelViewMatrix*vec4(0.5*aVertexPosition, 1.0);
      vVertexColor = aVertexColor;
      gl_Position = vVertexPosition;
    }";
    private const string fsSource = @"
    precision mediump float;
    varying vec4 vVertexColor;
    void main() {
      gl_FragColor = vVertexColor;
    }";

    private WebGLShader vertexShader;
    private WebGLShader fragmentShader;
    private WebGLProgram program;
    private WebGLBuffer vertexBuffer;
    private WebGLBuffer colorBuffer;
    private WebGLBuffer indexBuffer;
    private int positionAttribLocation;
    private int colorAttribLocation;
    private WebGLUniformLocation projectionUniformLocation;
    private WebGLUniformLocation modelViewUniformLocation;
    public AffineMat4 ModelViewMat;
    public AffineMat4 ProyMat;
    private float rotation_angle = 0f;
    private float lastTimestamp = 0f;

    private void IncrementCount()
    {
        currentCount++;
        Console.WriteLine("El valor del contador ahora es " + currentCount);
    }

    private async Task<WebGLShader> GetShader(string code, ShaderType stype) {
      WebGLShader shader = await this._context.CreateShaderAsync(stype);
      await this._context.ShaderSourceAsync(shader, code);
      await this._context.CompileShaderAsync(shader);
      if(!await this._context.GetShaderParameterAsync<bool>(shader, ShaderParameter.COMPILE_STATUS))
      {
        string info = await this._context.GetShaderInfoLogAsync(shader);
        await this._context.DeleteShaderAsync(shader);
        throw new Exception("An error occurred while compiling the shader: " + info);
      }
      return shader;
    }

    private async Task prepareBuffers() {
      this.vertexBuffer = await this._context.CreateBufferAsync();
      await this._context.BindBufferAsync(BufferType.ARRAY_BUFFER, this.vertexBuffer);
      await this._context.BufferDataAsync(BufferType.ARRAY_BUFFER, cubeVertices, BufferUsageHint.STATIC_DRAW);

      this.colorBuffer = await this._context.CreateBufferAsync();
      await this._context.BindBufferAsync(BufferType.ARRAY_BUFFER, this.colorBuffer);
      await this._context.BufferDataAsync(BufferType.ARRAY_BUFFER, this.cubeColors, BufferUsageHint.STATIC_DRAW);

      this.indexBuffer = await this._context.CreateBufferAsync();
      await this._context.BindBufferAsync(BufferType.ELEMENT_ARRAY_BUFFER, this.indexBuffer);
      await this._context.BufferDataAsync(BufferType.ELEMENT_ARRAY_BUFFER, cubeIndices, BufferUsageHint.STATIC_DRAW);

      await this._context.BindBufferAsync(BufferType.ARRAY_BUFFER, null);
      await this._context.BindBufferAsync(BufferType.ELEMENT_ARRAY_BUFFER, null);
    }

    private async Task getAttributeLocations() {
      this.positionAttribLocation = await this._context.GetAttribLocationAsync(this.program, "aVertexPosition");
      this.colorAttribLocation = await this._context.GetAttribLocationAsync(this.program, "aVertexColor");
      this.projectionUniformLocation = await this._context.GetUniformLocationAsync(this.program, "uProjectionMatrix");
      this.modelViewUniformLocation = await this._context.GetUniformLocationAsync(this.program, "uModelViewMatrix");
    }

    private async Task<WebGLProgram> BuildProgram(WebGLShader vShader, WebGLShader fShader) {
      var prog = await this._context.CreateProgramAsync();
      await this._context.AttachShaderAsync(prog, vShader);
      await this._context.AttachShaderAsync(prog, fShader);
      await this._context.LinkProgramAsync(prog);

      if(!await this._context.GetProgramParameterAsync<bool>(prog, ProgramParameter.LINK_STATUS))
      {
        string info = await this._context.GetProgramInfoLogAsync(prog);
        throw new Exception("An error occurred while linking the program: " + info);
      }
      return prog;
    }

    public void InitializeModel() {
      Vector3 initial_translate = new Vector3(0f, 0f, -3f);
      this.ModelViewMat = new AffineMat4();
      this.ProyMat = new AffineMat4();
      this.ModelViewMat.Translate(initial_translate);
    }

    public void Update(float timestamp) {
      const float vel = 0.001f;
      float delta;
      double FOV = 45 * System.Math.PI / 180f;
      double r = this._context.DrawingBufferWidth / this._context.DrawingBufferHeight;
      double near = 0.1;
      double far = 100f;

      this.ProyMat.Perspective((float)FOV, (float)r, (float)near, (float)far);

      Vector3 axis = new Vector3(0f, 1f, 1f);
      axis.Normalize();
      delta = timestamp - this.lastTimestamp;
      this.lastTimestamp = timestamp;
      this.rotation_angle += vel*delta;
      this.ModelViewMat.Rotation(rotation_angle, axis);
    }

    public async Task Draw() {
      await this._context.BeginBatchAsync();
      await this._context.UseProgramAsync(this.program);
      await this._context.UniformMatrixAsync(this.projectionUniformLocation, false, this.ProyMat.GetArray());
      await this._context.UniformMatrixAsync(this.modelViewUniformLocation, false, this.ModelViewMat.GetArray());

      // Vertex
      await this._context.BindBufferAsync(BufferType.ARRAY_BUFFER, this.vertexBuffer);
      await this._context.EnableVertexAttribArrayAsync((uint)this.positionAttribLocation);
      await this._context.VertexAttribPointerAsync((uint)this.positionAttribLocation, 3, DataType.FLOAT, false, 0, 0L);
      // Color
      await this._context.BindBufferAsync(BufferType.ARRAY_BUFFER, this.colorBuffer);
      await this._context.EnableVertexAttribArrayAsync((uint)this.colorAttribLocation);
      await this._context.VertexAttribPointerAsync((uint)this.colorAttribLocation, 4, DataType.FLOAT, false, 0, 0L);

      await this._context.BindBufferAsync(BufferType.ELEMENT_ARRAY_BUFFER, this.indexBuffer);

      await this._context.ClearColorAsync(0.2f, 0.2f, 0.2f, 1);
      await this._context.ClearDepthAsync(1f);
      await this._context.DepthFuncAsync(CompareFunction.LEQUAL);
      await this._context.EnableAsync(EnableCap.DEPTH_TEST);
      await this._context.ClearAsync(BufferBits.COLOR_BUFFER_BIT | BufferBits.DEPTH_BUFFER_BIT);
      await this._context.ViewportAsync(0, 0, this._context.DrawingBufferWidth, this._context.DrawingBufferHeight);

      await this._context.DrawElementsAsync(Primitive.TRIANGLES, cubeIndices.Length, DataType.UNSIGNED_SHORT, 0);
      await this._context.EndBatchAsync();
    }


    protected override async Task OnAfterRenderAsync(bool firstRender) {
      this._context = await this._canvasReference.CreateWebGLAsync();
      this.vertexShader = await this.GetShader(vsSource, ShaderType.VERTEX_SHADER);
      this.fragmentShader = await this.GetShader(fsSource, ShaderType.FRAGMENT_SHADER);

      this.program = await this.BuildProgram(this.vertexShader, this.fragmentShader);
      await this._context.DeleteShaderAsync(this.vertexShader);
      await this._context.DeleteShaderAsync(this.fragmentShader);
      await this.prepareBuffers();
      await this.getAttributeLocations();

      await this._context.ClearColorAsync(1, 0, 0, 1);
      await this._context.ClearAsync(BufferBits.COLOR_BUFFER_BIT);

      InitializeModel();
      Console.WriteLine("Starting Game Loop");
      await JSRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this));
    }

    [JSInvokable]
    public async void GameLoop(float timestamp) {
      this.Update(timestamp);
      await this.Draw();
    }
  }
}