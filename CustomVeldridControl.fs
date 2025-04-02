module CustomVeldridControl

open System
open System.Numerics
open System.Runtime.InteropServices
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Platform
open Avalonia.Platform.Interop
open Veldrid
open Veldrid.StartupUtilities
open Veldrid.Sdl2
open Vertex

[<Struct; StructLayout(LayoutKind.Sequential)>]
type VertexPositionColorUv = 
    val Position    : Vector3
    val Color       : Vector4
    val UV          : Vector2
    new(pos,col, uv) = { Position = pos; Color = col; UV = uv }

type CustomVeldridControl() as this =
    inherit NativeControlHost()

    let mutable graphicsDevice : GraphicsDevice option = None
    let mutable commandList : CommandList option = None
    let mutable factory : ResourceFactory option = None
    let mutable isInitialized = false

    let mutable sdlWindow : Sdl2Window option = None
    let mutable swapchainSource : SwapchainSource option = None

    let mutable vertexBuffer : DeviceBuffer option = None
    let mutable indexBuffer : DeviceBuffer option = None
    let mutable indexCount = 0
    let mutable conVert : VertexPositionColorUv[] = [||]

    let mutable pipeline : Pipeline option = None

    let vertexShaderCode = """
    #version 450
    layout(set = 0, binding = 0) uniform MVPBuffer {
        mat4 MVP;
    };
    layout(location = 0) in vec3 Position;
    layout(location = 1) in vec4 Color;
    layout(location = 2) in vec2 UV;

    layout(location = 0) out vec4 fsin_Color;

    void main()
    {
        gl_Position = MVP * vec4(Position, 1.0);
        fsin_Color = Color;
    }
    """

    let fragmentShaderCode = """
    #version 450
    layout(location = 0) in vec4 fsin_Color;
    layout(location = 0) out vec4 fsout_Color;

    void main()
    {
        fsout_Color = fsin_Color;
    }
    """

    let loadShader (factory: ResourceFactory) (stage: ShaderStages) (code: string) (entryPoint: string) =
        let bytes = System.Text.Encoding.UTF8.GetBytes(code)
        factory.CreateShader(ShaderDescription(stage, bytes, entryPoint))

    do
        this.AttachedToVisualTree.Add(fun _ -> this.InitializeVeldrid())

    member private this.InitializeVeldrid() =
        if isInitialized then () else

        isInitialized <- true

        let windowCI = WindowCreateInfo(
            X = 100,
            Y = 100,
            WindowWidth = 800,
            WindowHeight = 600,
            WindowTitle = "Veldrid Test"
        )

        let sdl = VeldridStartup.CreateWindow(ref windowCI)
        sdl.Visible <- true
        sdlWindow <- Some sdl

        let swapchainSrc = VeldridStartup.GetSwapchainSource(sdl)
        swapchainSource <- Some swapchainSrc

        let gd = VeldridStartup.CreateGraphicsDevice(
            sdl,
            GraphicsDeviceOptions(
                debug = true,
                ResourceBindingModel = ResourceBindingModel.Improved
            ),
            GraphicsBackend.OpenGL
        )
        graphicsDevice <- Some gd
        factory <- Some gd.ResourceFactory

        let cl = gd.ResourceFactory.CreateCommandList()

        // === Load model ===
        let modelPath = "chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl"
        let gameDataPath = @"F:\Games\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack"
        let lumina = new Lumina.GameData(gameDataPath)
        let model = lumina.GetFile(modelPath)
        model.LoadFile()

        let header = MdlParser.parseHeader model.Data
        let declarations, _ = MdlParser.parseVertexDeclarations model.Data header
        let rawBuffers = MdlParser.extractRawBuffers model.Data header

        let decodedVertices = MdlParser.decodeVertices declarations[0] rawBuffers.VertexBuffers[0]
        let indices = MdlParser.decodeIndices rawBuffers.IndexBuffers[0]

        let convertedVertices = 
            decodedVertices
            |> Array.map (fun v -> VertexPositionColorUv(v.Position, v.Color, v.UV))

        conVert <- convertedVertices

        let vb = gd.ResourceFactory.CreateBuffer(
            BufferDescription(
                uint32 (convertedVertices.Length * Marshal.SizeOf<VertexPositionColorUv>()),
                BufferUsage.VertexBuffer
            )
        )
        gd.UpdateBuffer(vb, 0u, convertedVertices)
        vertexBuffer <- Some vb

        let ib = gd.ResourceFactory.CreateBuffer(
            BufferDescription(
                uint32 (indices.Length * sizeof<uint16>),
                BufferUsage.IndexBuffer
            )
        )
        gd.UpdateBuffer(ib, 0u, indices)
        indexBuffer <- Some ib
        indexCount <- indices.Length

        // === Create MVP matrix and buffer ===
        let aspectRatio = float32 sdl.Width / float32 sdl.Height

        let projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f, aspectRatio, 0.1f, 100.0f
        )
        let view = Matrix4x4.CreateLookAt(
            Vector3(0.0f, 0.0f, -20.0f), Vector3.Zero, Vector3.UnitY
        )
        let modelMatrix = Matrix4x4.CreateScale(5.0f)

        let mvp = modelMatrix * view * projection

        let mvpBuffer = gd.ResourceFactory.CreateBuffer(
            BufferDescription(uint32 sizeof<Matrix4x4>, BufferUsage.UniformBuffer ||| BufferUsage.Dynamic)
        )
        gd.UpdateBuffer(mvpBuffer, 0u, Matrix4x4.Transpose(mvp))

        let layout = gd.ResourceFactory.CreateResourceLayout(
            ResourceLayoutDescription(
                ResourceLayoutElementDescription("MVPBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
            )
        )

        let resourceSet = gd.ResourceFactory.CreateResourceSet(
            ResourceSetDescription(layout, mvpBuffer)
        )

        // === Shader setup ===
        let vertexShader = loadShader gd.ResourceFactory ShaderStages.Vertex vertexShaderCode "main"
        let fragmentShader = loadShader gd.ResourceFactory ShaderStages.Fragment fragmentShaderCode "main"
        let shaders = [| vertexShader; fragmentShader |]

        let vertexLayout = 
            VertexLayoutDescription(
                [|
                    VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                    VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
                    VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
                |]
            )

        let mutable pipelineDescription = GraphicsPipelineDescription()
        pipelineDescription.BlendState <- BlendStateDescription.SingleOverrideBlend
        pipelineDescription.DepthStencilState <- DepthStencilStateDescription.Disabled
        pipelineDescription.RasterizerState <- RasterizerStateDescription(
            FaceCullMode.None,
            PolygonFillMode.Solid,
            FrontFace.Clockwise,
            true,
            false
        )
        pipelineDescription.PrimitiveTopology <- PrimitiveTopology.PointList
        pipelineDescription.ResourceLayouts <- [| layout |]
        pipelineDescription.ShaderSet <- ShaderSetDescription([| vertexLayout |], shaders)
        pipelineDescription.Outputs <- gd.SwapchainFramebuffer.OutputDescription

        pipeline <- Some (gd.ResourceFactory.CreateGraphicsPipeline(pipelineDescription))

        commandList <- Some cl

        // Pass resourceSet into a member if needed, or use in RenderLoop
        this.RenderLoop(resourceSet)


    member private this.RenderLoop(resourceSet: ResourceSet) =
        match graphicsDevice, commandList, pipeline, vertexBuffer with
        | Some gd, Some cl, Some pl, Some vb ->
            cl.Begin()
            cl.SetFramebuffer(gd.SwapchainFramebuffer)
            cl.ClearColorTarget(0u, RgbaFloat.Black)
            cl.SetPipeline(pl)
            cl.SetGraphicsResourceSet(0u, resourceSet)
            cl.SetVertexBuffer(0u, vb)
            cl.SetIndexBuffer(indexBuffer.Value, IndexFormat.UInt16)
            cl.Draw(
                vertexCount = uint32 conVert.Length,
                instanceCount = 1u,
                vertexStart = 0u,
                instanceStart = 0u
            )
            cl.End()

            gd.SubmitCommands(cl)
            gd.SwapBuffers()
            gd.WaitForIdle()

            async {
                do! Async.Sleep(16)
                this.RenderLoop(resourceSet)
            } |> Async.StartImmediate
        | _ -> ()