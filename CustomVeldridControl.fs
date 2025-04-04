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
open MdlParser
open generateNormals
open CameraController

[<Struct; StructLayout(LayoutKind.Sequential)>]
type VertexPositionColorUv = 
    val Position    : Vector3
    val Color       : Vector4
    val UV          : Vector2
    val Normal      : Vector3
    new(pos,col,uv,nor) = { Position = pos; Color = col; UV = uv; Normal = nor }

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

    let mutable isLeftDown = false
    let mutable isRightDown = false
    let mutable isMiddleDown = false
    let mutable lastMousePos = Vector2.Zero

    let mutable mvpb : DeviceBuffer option = None
    let mutable modm = null
    let mutable proj = null

    let cameraComtroller = CameraController()

    let vertexShaderCode = """
    #version 450
    layout(set = 0, binding = 0) uniform MVPBuffer {
        mat4 MVP;
    };
    layout(location = 0) in vec3 Position;
    layout(location = 1) in vec4 Color;
    layout(location = 2) in vec2 UV;
    layout(location = 3) in vec3 Normal;

    layout(location = 0) out vec3 fs_Position;
    layout(location = 1) out vec4 fs_Color;
    layout(location = 2) out vec2 fs_UV;
    layout(location = 3) out vec3 fs_Normal;

    void main()
    {
        gl_Position = MVP * vec4(Position, 1.0);
        fs_Position = Position;
        fs_Color = Color;
        fs_UV = UV;
        fs_Normal = normalize(Normal);
    }
    """

    let fragmentShaderCode = """
    #version 450
    layout(location = 0) in vec3 fs_Position;
    layout(location = 1) in vec4 fs_Color;
    layout(location = 2) in vec2 fs_UV;
    layout(location = 3) in vec3 fs_Normal;
    
    layout(location = 0) out vec4 fsout_Color;

    void main()
    {
        vec3 lightDir = normalize(vec3(-0.5, -1.0, -0.3));
        vec3 normal = normalize(fs_Normal);

        float diff = max(dot(normal, -lightDir), 0.0);

        vec3 viewDir = normalize(vec3(0.0, 0.0, 1.0));
        vec3 reflectDir = reflect(lightDir, normal);
        float spec = pow(max(dot(viewDir, reflectDir), 0.0), 16.0);

        vec3 baseColor = fs_Color.rgb * diff + vec3(spec);
        fsout_Color = vec4(baseColor, fs_Color.a);
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
        let flags = SDL_WindowFlags.OpenGL ||| SDL_WindowFlags.InputFocus ||| SDL_WindowFlags.AllowHighDpi ||| SDL_WindowFlags.Shown

        let sdl = new Sdl2Window(windowCI.WindowTitle, windowCI.X, windowCI.Y, windowCI.WindowWidth,windowCI.WindowHeight, flags, true)

        printfn "Hooking Mouse Event"
        // Add mouse button state
        sdl.add_MouseDown(fun (e: MouseEvent) ->
            match e.MouseButton with
            | MouseButton.Left -> isLeftDown <- true; cameraComtroller.StartOrbit(lastMousePos)
            | MouseButton.Right -> isRightDown <- true; cameraComtroller.StartDolly(lastMousePos)
            | MouseButton.Middle -> isMiddleDown <- true; cameraComtroller.StartPan(lastMousePos)
            | _ -> ()
        )

        sdl.add_MouseUp(fun (e: MouseEvent) ->
            match e.MouseButton with
            | MouseButton.Left -> isLeftDown <- false; cameraComtroller.Stop()
            | MouseButton.Right -> isRightDown <- false; cameraComtroller.Stop()
            | MouseButton.Middle -> isMiddleDown <- false; cameraComtroller.Stop()
            | _ -> ()
        )

        // Add camera controls
        printfn "Hooking mouse move"
        sdl.add_MouseMove(fun (e: MouseMoveEventArgs) ->
            let pos = Vector2(float32 e.MousePosition.X, float32 e.MousePosition.Y)
            lastMousePos <- pos
            cameraComtroller.MouseMove(pos)
        )

        sdl.Visible <- true
        async {
            while sdl.Exists do
                Sdl2Events.ProcessEvents()

                do! Async.Sleep(10)
        } |> Async.Start

        sdlWindow <- Some sdl

        if not sdl.Exists then
            failwith "SDL window does not exist!"

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

        let allDeclarations = declarations |> List.concat

        //let decodedVertices = MdlParser.decodeVertices declarations[0] rawBuffers.VertexBuffers
        let decodedVertices = MdlParser.decodeVerticesFromDeclaration declarations[0] rawBuffers.VertexBuffers
        let indices = MdlParser.decodeIndices rawBuffers.IndexBuffers[0]
        let generatedNormals = generateNormals (decodedVertices |> Array.map (fun v -> v.Position)) indices

        let convertedVertices = 
            decodedVertices
            |> Array.mapi (fun i v -> 
                VertexPositionColorUv(v.Position, v.Color, v.UV, generatedNormals[i]))

        conVert <- convertedVertices
        indices |> Array.take 20 |> Array.iteri (fun i ix -> printfn "Index %d: %d" i ix)
        printfn "Total vertices: %d" convertedVertices.Length
        printfn "Total triangles: %d" (indices.Length / 3)
        printfn "Index buffer length is multiple of 3: %b" (indices.Length % 3 = 0)

        printfn "First triangle indices: %A" indices.[0..2]

        declarations[0]
        |> List.iter (fun d ->
            printfn $"Usage: %A{d.VertexUsage}, Type: %A{d.VertexType}, Offset: {d.Offset}, Stream: {d.Stream}"
        )

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
            MathF.PI / 4.0f, aspectRatio, 0.5f, 100.0f
        )
     
        let view = Matrix4x4.CreateLookAt(
            Vector3(0.0f, 0.0f, -20.0f), Vector3.Zero, Vector3.UnitY
        )
        let convertCoordSystem = Matrix4x4.CreateRotationZ(MathF.PI / 2.0f)
        let modelMatrix = Matrix4x4.CreateScale(5.0f)

        //let mvp = modelMatrix * view * projection

        let mvpBuffer = gd.ResourceFactory.CreateBuffer(
            BufferDescription(uint32 sizeof<Matrix4x4>, BufferUsage.UniformBuffer ||| BufferUsage.Dynamic)
        )
        mvpb <- Some mvpBuffer

        //gd.UpdateBuffer(mvpBuffer, 0u, Matrix4x4.Transpose(mvp))

        let layout = gd.ResourceFactory.CreateResourceLayout(
            ResourceLayoutDescription(
                ResourceLayoutElementDescription("MVPBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
            )
        )

        let resourceSet = gd.ResourceFactory.CreateResourceSet(
            ResourceSetDescription(layout, mvpb.Value)
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
                    VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                |]
            )

        let mutable pipelineDescription = GraphicsPipelineDescription()
        pipelineDescription.BlendState <- BlendStateDescription.SingleOverrideBlend
        pipelineDescription.DepthStencilState <- DepthStencilStateDescription.DepthOnlyGreaterEqual
        pipelineDescription.RasterizerState <- RasterizerStateDescription(
            FaceCullMode.None,
            PolygonFillMode.Solid,
            FrontFace.Clockwise,
            true,
            false
        )
        pipelineDescription.PrimitiveTopology <- PrimitiveTopology.TriangleList
        //pipelineDescription.PrimitiveTopology <- PrimitiveTopology.PointList
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
            cl.DrawIndexed(
                indexCount = uint32 indexCount,
                instanceCount = 1u,
                indexStart = 0u,
                vertexOffset = 0,
                instanceStart = 0u
            )
            //cl.Draw(
            //    vertexCount = uint32 conVert.Length,
            //    instanceCount = 1u,
            //    vertexStart = 0u,
            //    instanceStart = 0u
            //)
            let model = Matrix4x4.CreateScale(5.0f)
            let view = cameraComtroller.GetViewMatrix()
            let projection = cameraComtroller.GetProjectionMatrix(
                float32 gd.SwapchainFramebuffer.Width / float32 gd.SwapchainFramebuffer.Height
            )

            let mvp = model * view * projection
            gd.UpdateBuffer(mvpb.Value, 0u, Matrix4x4.Transpose(mvp))
            cl.End()

            gd.SubmitCommands(cl)
            gd.SwapBuffers()
            gd.WaitForIdle()

            async {
                do! Async.Sleep(16)
                this.RenderLoop(resourceSet)
            } |> Async.StartImmediate
        | _ -> ()