namespace fs_mdl_viewer

open System
open System.IO
open System.Numerics
open System.Runtime.InteropServices
open System.Diagnostics
open System.Threading
open Avalonia
open Avalonia.Controls
open Avalonia.VisualTree
open Avalonia.Platform
open Avalonia.Rendering
open Avalonia.Threading
open Avalonia.Visuals
open Veldrid
open Veldrid.Sdl2
open Veldrid.StartupUtilities
open Veldrid.Vk
open xivModdingFramework.Cache
open xivModdingFramework.General.Enums
open ModelLoader
open ShaderBuilder
open CameraController

type CustomVeldridControl() as this =
    inherit NativeControlHost()

    // --- mutable state ---
    let mutable gd          : GraphicsDevice        option  = None
    let mutable cl          : CommandList           option  = None
    let mutable pl          : Pipeline              option  = None
    let mutable indexCount  : int                           = 0
    let mutable intd        : bool                          = false
    let mutable childHwnd   : IntPtr                        = IntPtr.Zero

    let mutable timer       : DispatcherTimer       option  = None
    let mutable resize      : IDisposable           option  = None

    let mutable camera      : CameraController              = CameraController()
    let mutable model       : LoadedModel           option  = None

    let mutable vb          : DeviceBuffer          option  = None
    let mutable ib          : DeviceBuffer          option  = None
    let mutable mb          : DeviceBuffer          option  = None
    let mutable ml          : ResourceLayout        option  = None
    let mutable ms          : ResourceSet           option  = None
    let mutable ts          : ResourceSet           option  = None


    // --- Initialization ---
    

    override this.CreateNativeControlCore (parent: IPlatformHandle): IPlatformHandle = 
        let native = base.CreateNativeControlCore(parent)
        childHwnd <- native.Handle
        native

    

    override this.OnAttachedToVisualTree (e: VisualTreeAttachmentEventArgs): unit = 
        base.OnAttachedToVisualTree(e: VisualTreeAttachmentEventArgs)

        let access = this.CheckAccess()

        resize <-
            Some (this.GetObservable(NativeControlHost.BoundsProperty)
            .Subscribe(fun rect ->
                printfn $"Resize caught, initializing..."
                let w = uint32 rect.Width
                let h = uint32 rect.Height

                if rect.Width > 0. && rect.Height > 0. && access then
                    if not intd && childHwnd <> IntPtr.Zero then
                        printfn "We've made it to the main loop, I think..."
                        let hinst = Process.GetCurrentProcess().MainModule.BaseAddress
                        let vkSrc = VkSurfaceSource.CreateWin32(hinst, childHwnd)

                        let dev = GraphicsDevice.CreateVulkan(
                            GraphicsDeviceOptions(true, PixelFormat.D32_Float_S8_UInt, true, ResourceBindingModel.Improved),
                            vkSrc,
                            w, h
                        )
                        gd <- Some dev

                        this.InitializeVeldrid dev

                        let rec renderLoop () = async {
                            do! Async.Sleep 16
                            Dispatcher.UIThread.Post(fun () ->
                                this.RenderVeldridFrame()
                            )
                            return! renderLoop()
                        }
                        Async.StartImmediate (renderLoop())

                        intd <- true
                        printfn $"If you're seeing this, this should end in true. {intd}"
                    else if intd then
                        match gd with
                        | Some d -> d.MainSwapchain.Resize(w, h)
                        | None   -> ()

                    else
                        printfn $"Failing intialize conditions: {intd} {childHwnd} {access}"
            ))

    //override this.OnDetachedFromVisualTree(e: VisualTreeAttachmentEventArgs): unit =
    //    timer   |> Option.iter (fun t -> t.Stop())
    //    resize  |> Option.iter (fun r -> r.Dispose())
    //    vb      |> Option.iter (fun b -> b.Dispose())
    //    ib      |> Option.iter (fun b -> b.Dispose())
    //    mb      |> Option.iter (fun b -> b.Dispose())
    //    ml      |> Option.iter (fun l -> l.Dispose())
    //    ms      |> Option.iter (fun s -> s.Dispose())
    //    ts      |> Option.iter (fun s -> s.Dispose())
    //    match cl        with Some   c -> c.Dispose()      | None -> ()
    //    match pl        with Some   p -> p.Dispose()      | None -> ()
    //    match gd        with Some   d -> d.Dispose()      | None -> ()

    //    base.OnDetachedFromVisualTree(e: VisualTreeAttachmentEventArgs)

    // Load the model, create buffers, etc
    member private this.InitializeVeldrid (device: GraphicsDevice) =
        let gdp             = @"F:\Games\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack\ffxiv"
        let mdlPath         = "chara/monster/m8299/obj/body/b0001/model/m8299b0001.mdl"
        let info            = xivModdingFramework.GameInfo(DirectoryInfo(gdp), XivLanguage.English)

        XivCache.SetGameInfo(info) |> ignore

        let initModel       = loadGameModel device (device.ResourceFactory) mdlPath |> Async.RunSynchronously
        let textureSet      = initModel.TextureSet
        model       <- Some initModel
        ts          <- Some textureSet.Value

        // Get the vertex and index buffers set up
        let vBuff = device.ResourceFactory.CreateBuffer(
            BufferDescription(
                uint32 (initModel.Vertices.Length * Marshal.SizeOf<VertexPositionColorUv>()),
                BufferUsage.VertexBuffer
            )
        )
        let iBuff = device.ResourceFactory.CreateBuffer(
            BufferDescription(
                uint32 (initModel.Indices.Length * Marshal.SizeOf<uint16>()),
                BufferUsage.IndexBuffer
            )
        )
        let mBuff = device.ResourceFactory.CreateBuffer(
            BufferDescription(
                uint32 (Marshal.SizeOf<Matrix4x4>()),
                BufferUsage.UniformBuffer ||| BufferUsage.Dynamic
            )
        )
        device.UpdateBuffer(vBuff, 0u, initModel.Vertices)
        device.UpdateBuffer(iBuff, 0u, initModel.Indices)
        vb          <- Some vBuff
        ib          <- Some iBuff
        indexCount  <- initModel.Indices.Length
        mb          <- Some mBuff

        let vertexLayout = VertexLayoutDescription(
            [|
                VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
                VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
                VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
            |]
        )
        let layout = device.ResourceFactory.CreateResourceLayout(
            ResourceLayoutDescription(
                ResourceLayoutElementDescription("MVPBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
            )
        )
        let set = device.ResourceFactory.CreateResourceSet(
            ResourceSetDescription(
                layout,
                mb.Value
            )
        )
        ml <- Some layout
        ms <- Some set



        let pipeline        = createDefaultPipeline device device.ResourceFactory vertexLayout device.SwapchainFramebuffer.OutputDescription ml.Value initModel.TextureLayout.Value
        let commandList     = device.ResourceFactory.CreateCommandList()

        pl <- Some pipeline
        cl <- Some commandList


    member private this.RenderVeldridFrame() =
        printfn $"Rendering a frame!"
        match gd, cl,  pl, vb, ib, mb, ms, ts with
        | Some d, Some c, Some p, Some v, Some i, Some m, Some s, Some t ->
            printfn "Resources matched! Beginning render..."

            
            // Set up buffers and camera
            let w       = float32 d.MainSwapchain.Framebuffer.Width
            let h       = float32 d.MainSwapchain.Framebuffer.Height
            let aspect  = w / h
            
            let view                            = camera.GetViewMatrix()
            let proj                            = camera.GetProjectionMatrix(aspect)
            let mutable mvp : Matrix4x4         = proj * view
            c.Begin()
            printfn "Begun. Setting framebuffer..."
            c.SetFramebuffer(d.SwapchainFramebuffer)

            printfn "Framebuffer set, setting clear color target..."
            c.ClearColorTarget(0u, RgbaFloat.Grey)
            printfn "Clear color target set, clearing depth stencil..."
            c.ClearDepthStencil(1.0f)

            printfn "Depth stencil cleared, setting pipeline..."
            c.SetPipeline(p)
            printfn "Pipeline set, setting graphics resource sets..."
            c.SetGraphicsResourceSet(0u, s)
            c.SetGraphicsResourceSet(1u, t)
            printfn "Resource sets set, setting vertex buffer..."
            c.SetVertexBuffer(0u, v)
            printfn "Vertex buffer set, setting index buffer..."
            c.SetIndexBuffer(i, IndexFormat.UInt16)

            printfn "Index buffer set, attempting to draw... Index count %d" indexCount
            c.DrawIndexed(uint32 indexCount, 1u, 0u, 0, 0u)

            printfn "Drawing complete, updating MVP buffer..."
            d.UpdateBuffer(mb.Value, 0u, mvp)
            printfn "MVP Buffer updated, ending..."
            c.End()

            printfn "Ended, submitting commands..."
            d.SubmitCommands(c)
            printfn "Commands submitted, swapping buffers..."
            d.SwapBuffers()
            printfn "Buffers swapped, waiting for idle..."
            d.WaitForIdle()
            printfn "All commands executed successfully. Frame should have fully rendered."
        | _ ->
            printfn "If you're seeing this, the frame failed to render. Something didn't match somewhere."
            ()

    override this.Render (context: Media.DrawingContext): unit = 
        base.Render(context: Media.DrawingContext)
        if intd then
            
            this.RenderVeldridFrame()
            
        else
            printfn "Render called, but Veldrid not intialized yet."