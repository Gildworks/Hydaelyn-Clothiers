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

type CustomVeldridControl() as this =
    inherit NativeControlHost()

    let mutable graphicsDevice : GraphicsDevice option = None
    let mutable commandList : CommandList option = None
    let mutable factory : ResourceFactory option = None
    let mutable isInitialized = false

    let mutable sdlWindow : Sdl2Window option = None
    let mutable swapchainSource : SwapchainSource option = None

    let mutable vertexBuffer : DeviceBuffer option = None

    let mutable pipeline : Pipeline option = None

    let vertexShaderCode = """
    #version 450
    layout(location = 0) in vec2 Position;
    layout(location = 1) in vec4 Color;

    layout(location =0) out vec4 fsin_Color;

    void main()
    {
        gl_Position = vec4(Position, 0.0, 1.0);
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
            GraphicsDeviceOptions(),
            GraphicsBackend.OpenGL
        )
        graphicsDevice <- Some gd
        factory <- Some gd.ResourceFactory

        let cl = gd.ResourceFactory.CreateCommandList()
        let triangleVertices = 
            [|
                Vertex(Vector2(0.0f, 0.5f), RgbaFloat.Red)
                Vertex(Vector2(0.5f, -0.5f), RgbaFloat.Green)
                Vertex(Vector2(-0.5f, -0.5f), RgbaFloat.Blue)
            |]

        let vb = gd.ResourceFactory.CreateBuffer(
            BufferDescription(
                uint32 (triangleVertices.Length * sizeof<Vertex>),
                BufferUsage.VertexBuffer
            )
        )
        vertexBuffer <- Some vb
        gd.UpdateBuffer(vertexBuffer.Value, 0u, triangleVertices)

        let vertexShader = loadShader gd.ResourceFactory ShaderStages.Vertex vertexShaderCode "main"
        let fragmentShader = loadShader gd.ResourceFactory ShaderStages.Fragment fragmentShaderCode "main"
        let shaders = [| vertexShader; fragmentShader |]

        let vertexLayout = Vertex.VertexLayout

        let mutable pipelineDescription = GraphicsPipelineDescription()
        pipelineDescription.BlendState <- BlendStateDescription.SingleOverrideBlend
        pipelineDescription.DepthStencilState <- DepthStencilStateDescription.Disabled
        pipelineDescription.RasterizerState <- RasterizerStateDescription.Default
        pipelineDescription.PrimitiveTopology <- PrimitiveTopology.TriangleList
        pipelineDescription.ResourceLayouts <- Array.empty
        pipelineDescription.ShaderSet <- ShaderSetDescription([| vertexLayout |], shaders)
        pipelineDescription.Outputs <- gd.SwapchainFramebuffer.OutputDescription

        pipeline <- Some (gd.ResourceFactory.CreateGraphicsPipeline(pipelineDescription))

        commandList <- Some cl

        this.RenderLoop()

    member private this.RenderLoop() =
        match graphicsDevice, commandList, pipeline, vertexBuffer with
        | Some gd, Some cl, Some pl, Some vb ->
            cl.Begin()
            cl.SetFramebuffer(gd.SwapchainFramebuffer)
            cl.ClearColorTarget(0u, RgbaFloat.Black)
            cl.SetPipeline(pl)
            cl.SetVertexBuffer(0u, vb)
            cl.Draw(
                vertexCount = uint32 3,
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
                this.RenderLoop()
            } |> Async.StartImmediate
        | _ -> ()